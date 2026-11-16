using SimpleTouch.Core;
using SimpleTouch.Core.Config;
using SimpleTouch.Core.Protocol;
using SimpleTouch.Platform.Windows;

namespace SimpleTouch.Host;

/// <summary>Agent 连接状态机（M4.5 UI 展示用）。Reconnecting 时 <see cref="Attempt"/> 为已失败次数。</summary>
public enum AgentState
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Stopped,
}

/// <summary>握手完成后的对端信息。</summary>
public sealed record PeerInfo(string Name, int Version, int Caps);

/// <summary>
/// Agent 内核服务（M4.5 自控制台宿主解耦）：断线退避重连循环 + 会话事件处理。
/// 事件可能在 socket/线程池线程触发，UI 层需自行切回 UI 线程（Dispatcher）。
/// 注入器/配置/剪贴板跨会话复用（原控制台行为）；<see cref="Stop"/> 后不可重启（新建实例）。
/// </summary>
public sealed class AgentHost : IAsyncDisposable
{
    /// <summary>本机握手 caps 组合（--ping 与事件循环共用）。</summary>
    public const int HandshakeCaps = ProtocolConsts.CapsBits.TextInject | ProtocolConsts.CapsBits.Clipboard
                                   | ProtocolConsts.CapsBits.Macros | ProtocolConsts.CapsBits.Consumer;

    /// <summary>点按 down/up 目标按住时长（M1 验收报告建议 40–60ms）；设 55 补偿 Thread.Sleep ~15ms 定时器粒度。</summary>
    private const int TapGapMs = 55;

    private readonly WindowsInputInjector _injector = new();
    private readonly WindowsClipboard _clipboard = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly string _settingsPath;

    /// <summary>volatile：热键映射可经 <see cref="ReloadHotkeys"/> 热替换。</summary>
    private volatile Dictionary<ushort, HotkeyPresetDef> _hotkeys;
    private string? _lastClipboardFromPhone;
    private ProtocolSession? _currentSession;

    public string DeviceName { get; }
    public AgentSettings Settings { get; }
    public string SettingsPath => _settingsPath;
    public string HotkeysPath { get; }
    public AgentState State { get; private set; } = AgentState.Idle;
    public int Attempt { get; private set; }
    public PeerInfo? Peer { get; private set; }
    public int HotkeyCount => _hotkeys.Count;

    /// <summary>状态变化（含人类可读状态行；console 宿主可忽略）。</summary>
    public event Action<AgentState, string>? StateChanged;
    /// <summary>握手完成/会话结束。</summary>
    public event Action<PeerInfo?>? PeerChanged;

    public AgentHost(string deviceName, string? configDir = null)
    {
        DeviceName = deviceName;
        var dir = configDir ?? Path.Combine(AppContext.BaseDirectory, "config");
        _settingsPath = Path.Combine(dir, "settings.json");
        HotkeysPath = Path.Combine(dir, "hotkeys.json");
        Settings = AgentSettings.LoadOrCreate(_settingsPath);
        _hotkeys = HotkeyMap.LoadOrCreate(HotkeysPath);
        Log.Info($"配置: hotkeys.json {_hotkeys.Count} 项预设, moveScale={Settings.MoveScale:0.##}");

        // M3 剪贴板：轮询监听 + 双向同步（防回环：手机刚推来的内容不回推）。
        // 推送目标用 currentSession 间接引用——断线期间 PC 剪贴板变更静默跳过，重连后恢复推送
        _clipboard.StartMonitoring();
        _clipboard.TextChanged += text =>
        {
            var session = _currentSession;
            if (session is null) return;
            if (text == _lastClipboardFromPhone)
            {
                Log.Info("剪贴板变更与手机端推送内容相同，跳过回推（防回环）");
                return;
            }
            Log.Info($"PC 剪贴板变更 → 推送 CLIPBOARD_SET ({text.Length} 字符)");
            var frames = TextFrameSplitter.Split(text);
            _ = Task.Run(async () =>
            {
                for (var i = 0; i < frames.Count; i++)
                    await session.SendAsync(new ClipboardSetMessage(i == frames.Count - 1, frames[i]), CancellationToken.None);
            });
        };
    }

    /// <summary>持久重连循环：断开/失败按退避重试，握手成功归零；Stop 或取消后返回。</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopCts.Token);
        var token = linked.Token;
        var attempt = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SetState(attempt == 0 ? AgentState.Connecting : AgentState.Reconnecting,
                        attempt == 0 ? $"正在连接 {DeviceName}…" : $"第 {attempt + 1} 次尝试连接 {DeviceName}…");

                    await using var transport = await WinRfcommTransport.ConnectAsync(DeviceName, ProtocolConsts.RfcommServiceUuid, token);
                    await using var session = new ProtocolSession(transport);
                    await transport.StartReceivingAsync();
                    session.Disconnected += ex => Log.Warn($"连接断开: {ex?.Message ?? "对端正常关闭"}");

                    if (!await session.HandshakeAsync("SimpleTouch.PC", HandshakeCaps, sendReady: true, token))
                        throw new InvalidOperationException("握手失败（版本不兼容或 5s 内未收到对端 HELLO）");

                    Log.Info($"握手完成: peer={session.PeerName} ver={session.PeerVersion} caps=0x{session.PeerCaps:X}" +
                             (attempt > 0 ? "（断线重连成功）" : ""));
                    attempt = 0;
                    Attempt = 0;
                    Peer = new PeerInfo(session.PeerName ?? "?", session.PeerVersion, session.PeerCaps);
                    PeerChanged?.Invoke(Peer);
                    SetState(AgentState.Connected, $"已连接: {Peer.Name}");
                    _currentSession = session;
                    await RunSessionAsync(session, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempt++;
                    Attempt = attempt;
                    // SocketException 等异常 Message 可能为空串，补类型名保证日志可读
                    var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                    Log.Error($"连接失败: {reason}");
                    var delayMs = BackoffDelayMs(attempt);
                    SetState(AgentState.Reconnecting, $"连接失败（{reason}）；{delayMs / 1000.0:0.#}s 后第 {attempt + 1} 次重试");
                    Log.Info($"{delayMs / 1000.0:0.#}s 后自动重试（Ctrl+C 退出）");
                    await Task.Delay(delayMs, token);
                }
                finally
                {
                    _currentSession = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stop()/外部取消：正常退出路径
        }
        SetState(AgentState.Stopped, "已停止");
    }

    /// <summary>停止重连循环（幂等）。宿主实例随之不可复用。</summary>
    public void Stop() => _stopCts.Cancel();

    /// <summary>重载 hotkeys.json（UI「重新加载」入口；失败保留旧表并记日志）。</summary>
    public void ReloadHotkeys()
    {
        try
        {
            _hotkeys = HotkeyMap.LoadOrCreate(HotkeysPath);
            Log.Info($"hotkeys.json 已重载（{_hotkeys.Count} 项预设）");
        }
        catch (Exception ex)
        {
            Log.Error($"hotkeys.json 重载失败，保留旧表: {ex.Message}");
        }
    }

    /// <summary>调整指针灵敏度并落盘（UI 设置页入口；会话中即时生效）。</summary>
    public void SetMoveScale(float scale)
    {
        Settings.MoveScale = Math.Clamp(scale, 0.1f, 3f);
        Settings.Save(_settingsPath);
        Log.Info($"moveScale 调整为 {Settings.MoveScale:0.##} 并落盘");
    }

    /// <summary>记住默认设备并落盘（UI/CLI 下次启动的默认值）。</summary>
    public void RememberDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName == Settings.DeviceName) return;
        Settings.DeviceName = deviceName;
        Settings.Save(_settingsPath);
    }

    // 单个会话：消息处理 + 吞吐统计，直到断开/BYE/取消才返回
    private async Task RunSessionAsync(ProtocolSession session, CancellationToken ct)
    {
        // M3 TEXT/CLIPBOARD_SET 分帧重组缓冲（仅接收线程访问；随会话重建，防止跨会话污染）
        var pendingText = new List<byte>();
        var pendingClipboard = new List<byte>();

        var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long moveCount = 0;
        long lastButtonDownTicks = 0;
        byte lastDownBtn = 0xFF;
        session.Disconnected += _ => exitTcs.TrySetResult();
        session.MessageReceived += m =>
        {
            switch (m)
            {
                case MouseMoveMessage mv:
                    var dx = Math.Clamp((int)Math.Round(mv.Dx * Settings.MoveScale), short.MinValue, short.MaxValue);
                    var dy = Math.Clamp((int)Math.Round(mv.Dy * Settings.MoveScale), short.MinValue, short.MaxValue);
                    _injector.MouseMove((short)dx, (short)dy);
                    Interlocked.Increment(ref moveCount);
                    break;
                case MouseButtonMessage b:
                    // 点按间隙兜底：手机端发送侧的 45ms 间隙会被 BT 链路合并（实测同毫秒到达），
                    // 注入前补足到 ~45ms 按住时长，避免极快 down/up 被应用吞掉（M1 已知问题 #2）
                    if (b.Action == ProtocolConsts.PressAction.Down)
                    {
                        lastDownBtn = b.Button;
                        lastButtonDownTicks = Environment.TickCount64;
                    }
                    else if (b.Button == lastDownBtn)
                    {
                        var sinceDown = Environment.TickCount64 - lastButtonDownTicks;
                        if (sinceDown < TapGapMs) Thread.Sleep((int)(TapGapMs - sinceDown));
                    }
                    _injector.MouseButton(b.Button, b.Action == ProtocolConsts.PressAction.Down);
                    Log.Info($"BUTTON btn={b.Button} action={b.Action}");
                    break;
                case MouseScrollMessage s:
                    _injector.MouseScroll(s.Dx, s.Dy);
                    Log.Info($"SCROLL dx={s.Dx} dy={s.Dy}");
                    break;
                case KeyMessage k:
                    _injector.KeyPress(k.Code, k.Action == ProtocolConsts.PressAction.Down, k.Mods);
                    if (k.Action == ProtocolConsts.PressAction.Down)
                        Log.Info($"KEY code=0x{k.Code:X2} mods=0x{k.Mods:X2}");
                    break;
                case TextMessage t:
                    pendingText.AddRange(t.Data);
                    if (t.Fin)
                    {
                        var text = System.Text.Encoding.UTF8.GetString(pendingText.ToArray());
                        pendingText.Clear();
                        Log.Info($"TEXT 直注 {text.Length} 字符");
                        // 多字符注入耗时（逐码元 SendInput），放线程池避免拖慢接收线程
                        _ = Task.Run(() => _injector.Text(text));
                    }
                    break;
                case ClipboardSetMessage c:
                    pendingClipboard.AddRange(c.Data);
                    if (c.Fin)
                    {
                        var clipText = System.Text.Encoding.UTF8.GetString(pendingClipboard.ToArray());
                        pendingClipboard.Clear();
                        _lastClipboardFromPhone = clipText;
                        Log.Info($"CLIPBOARD_SET ← 手机 ({clipText.Length} 字符) → 写入 PC 剪贴板");
                        _ = Task.Run(() => _clipboard.SetText(clipText));
                    }
                    break;
                case HotkeyMessage h:
                    var hotkeys = _hotkeys;
                    if (hotkeys.TryGetValue(h.PresetId, out var preset))
                    {
                        Log.Info($"HOTKEY presetId={h.PresetId} → {preset.Name} (vk=0x{preset.Vk:X2} mods=0x{preset.Mods:X2})");
                        // TapKey 内部阻塞 holdMs，放线程池避免拖慢接收线程（PONG/后续事件）
                        _ = Task.Run(() => _injector.TapKey(preset.Vk, preset.Mods, preset.HoldMs));
                    }
                    else
                    {
                        Log.Warn($"HOTKEY 未知 presetId={h.PresetId}，已忽略（兼容规则）");
                    }
                    break;
                case ByeMessage:
                    exitTcs.TrySetResult();
                    break;
            }
        };
        Log.Info("事件循环已启动——触摸板/快捷键/文本/剪贴板就绪（断开后自动重连，Ctrl+C 退出）");
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !exitTcs.Task.IsCompleted)
            {
                try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { break; }
                var n = Interlocked.Exchange(ref moveCount, 0);
                Log.Info($"吞吐: {n / 5.0:F0} move/s");
            }
        });
        await exitTcs.Task.WaitAsync(ct);
        Log.Info("会话结束");
        Peer = null;
        PeerChanged?.Invoke(null);
        try { await session.SendAsync(new ByeMessage(ProtocolConsts.ByeReason.Normal), CancellationToken.None); } catch { /* 传输已断开 */ }
    }

    private void SetState(AgentState state, string statusText)
    {
        State = state;
        Log.Info($"[状态] {statusText}");
        StateChanged?.Invoke(state, statusText);
    }

    /// <summary>断线重连退避：1s→2s→4s→8s→15s，之后 30s 封顶。</summary>
    internal static int BackoffDelayMs(int failedAttempt) => failedAttempt switch
    {
        1 => 1_000,
        2 => 2_000,
        3 => 4_000,
        4 => 8_000,
        5 => 15_000,
        _ => 30_000,
    };

    public async ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        _clipboard.Dispose();
        _stopCts.Dispose();
        await Task.CompletedTask;
    }
}
