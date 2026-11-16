using System.Diagnostics;
using SimpleTouch.Core;
using SimpleTouch.Core.Config;
using SimpleTouch.Core.Protocol;
using SimpleTouch.Platform.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("SimpleTouch Agent 仅支持 Windows。");
    return 1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Log.Init(Path.Combine(AppContext.BaseDirectory, "logs", $"agent-{DateTime.Now:yyyyMMdd-HHmmss}.log"));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var argsList = args.ToList();
if (argsList.Contains("--list"))
{
    Console.WriteLine("已配对蓝牙设备:");
    foreach (var name in await DeviceScanner.ListPairedDeviceNamesAsync(cts.Token))
        Console.WriteLine($"  {name}");
    return 0;
}

if (argsList.Contains("--diag"))
{
    var diagDevice = argsList.FirstOrDefault(a => !a.StartsWith('-')) ?? "Xiaomi Pad 5 Pro";
    await DeviceDiagnostics.DumpAsync(diagDevice, ProtocolConsts.RfcommServiceUuid, cts.Token);
    return 0;
}

var deviceName = argsList.FirstOrDefault(a => !a.StartsWith('-')) ?? "Xiaomi Pad 5 Pro";
Log.Info($"SimpleTouch Agent 启动（协议 v{ProtocolConsts.ProtocolVersion}），目标设备: {deviceName}");
// 剪贴板防回环标记（跨会话保留；RunAgentLoopAsync 与 RunEventLoopAsync 共用）
string? lastClipboardFromPhone = null;

try
{
    if (argsList.Contains("--repair"))
    {
        Log.Info("=== 重新配对流程 ===");
        Log.Info("请配合：① 平板端先删除本机配对（如未删）② 保持平板蓝牙设置页打开 ③ App 保持监听中");
        var repaired = await RepairFlow.RunAsync(deviceName, cts.Token);
        if (!repaired)
        {
            Log.Error("重新配对未能缓存 SimpleTouch 服务，中止");
            return 1;
        }
        Log.Info("SimpleTouch 服务已进入配对缓存，继续连接验收");
    }

    if (argsList.Contains("--ping"))
    {
        // RTT 测量模式：延迟报告数据源（执行文档 §4）。测量场景单次连接，不适用重连循环
        await using var pingTransport = await WinRfcommTransport.ConnectAsync(deviceName, ProtocolConsts.RfcommServiceUuid, cts.Token);
        await using var pingSession = new ProtocolSession(pingTransport);
        await pingTransport.StartReceivingAsync();

        const int allCaps = ProtocolConsts.CapsBits.TextInject | ProtocolConsts.CapsBits.Clipboard
                          | ProtocolConsts.CapsBits.Macros | ProtocolConsts.CapsBits.Consumer;
        if (!await pingSession.HandshakeAsync("SimpleTouch.PC", allCaps, sendReady: true, cts.Token))
        {
            Log.Error("握手失败（版本不兼容或 5s 内未收到对端 HELLO）");
            return 1;
        }
        Log.Info($"握手完成: peer={pingSession.PeerName} ver={pingSession.PeerVersion} caps=0x{pingSession.PeerCaps:X}");

        var rtts = new List<double>();
        for (var i = 0; i < 100 && !cts.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            var pong = await pingSession.PingAsync(Environment.TickCount64, cts.Token);
            sw.Stop();
            rtts.Add(sw.Elapsed.TotalMilliseconds);
            Log.Info($"PING #{i + 1}: {sw.Elapsed.TotalMilliseconds:F1} ms (ts={pong.Timestamp})");
            await Task.Delay(200, cts.Token);
        }

        var sorted = rtts.OrderBy(x => x).ToList();
        Log.Info($"RTT 统计: N={sorted.Count} P50={Percentile(sorted, 50):F1}ms P95={Percentile(sorted, 95):F1}ms Max={sorted[^1]:F1}ms");
        await pingSession.SendAsync(new ByeMessage(ProtocolConsts.ByeReason.Normal), CancellationToken.None);
        return 0;
    }

    // M4 主模式：事件循环 + 断线退避重连（M0 已知限制的正式解，手机端配套自动重新监听）
    await RunAgentLoopAsync(deviceName, cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    Log.Info("用户取消（Ctrl+C）");
    return 130;
}
catch (Exception ex)
{
    Log.Error(ex.ToString());
    return 1;
}

// ===== 事件循环模式：断线退避重连 =====
async Task RunAgentLoopAsync(string targetDevice, CancellationToken ct)
{
    // 跨会话复用：注入器/配置/剪贴板只建一次，退避计数与会话级状态每次连接重置
    var injector = new WindowsInputInjector();
    var configDir = Path.Combine(AppContext.BaseDirectory, "config");
    var hotkeyMap = HotkeyMap.LoadOrCreate(Path.Combine(configDir, "hotkeys.json"));
    var settings = AgentSettings.LoadOrCreate(Path.Combine(configDir, "settings.json"));
    Log.Info($"配置: hotkeys.json {hotkeyMap.Count} 项预设, moveScale={settings.MoveScale:0.##}");

    // M3 剪贴板：轮询监听 + 双向同步（防回环：手机刚推来的内容不回推）。
    // 推送目标用 currentSession 间接引用——断线期间 PC 剪贴板变更静默跳过，重连后恢复推送
    var clipboard = new WindowsClipboard();
    clipboard.StartMonitoring();
    ProtocolSession? currentSession = null;
    clipboard.TextChanged += text =>
    {
        var session = currentSession;
        if (session is null) return;
        if (text == lastClipboardFromPhone)
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

    var attempt = 0; // 失败次数；握手成功归零（1s→2s→4s→8s→15s→30s 封顶）
    while (!ct.IsCancellationRequested)
    {
        try
        {
            if (attempt > 0)
                Log.Info($"第 {attempt + 1} 次尝试连接 {targetDevice}…");

            await using var transport = await WinRfcommTransport.ConnectAsync(targetDevice, ProtocolConsts.RfcommServiceUuid, ct);
            await using var session = new ProtocolSession(transport);
            await transport.StartReceivingAsync();
            session.Disconnected += ex => Log.Warn($"连接断开: {ex?.Message ?? "对端正常关闭"}");

            const int caps = ProtocolConsts.CapsBits.TextInject | ProtocolConsts.CapsBits.Clipboard
                           | ProtocolConsts.CapsBits.Macros | ProtocolConsts.CapsBits.Consumer;
            if (!await session.HandshakeAsync("SimpleTouch.PC", caps, sendReady: true, ct))
                throw new InvalidOperationException("握手失败（版本不兼容或 5s 内未收到对端 HELLO）");

            Log.Info($"握手完成: peer={session.PeerName} ver={session.PeerVersion} caps=0x{session.PeerCaps:X}" +
                     (attempt > 0 ? "（断线重连成功）" : ""));
            attempt = 0;
            currentSession = session;
            await RunEventLoopAsync(session, injector, hotkeyMap, settings, clipboard, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 用户 Ctrl+C：直接退出重连循环
        }
        catch (Exception ex)
        {
            attempt++;
            Log.Error($"连接失败: {ex.Message}");
            var delayMs = BackoffDelayMs(attempt);
            Log.Info($"{delayMs / 1000.0:0.#}s 后自动重试（Ctrl+C 退出）");
            await Task.Delay(delayMs, ct);
        }
        finally
        {
            currentSession = null;
        }
    }
}

// 单个会话：消息处理 + 吞吐统计，直到断开/BYE/取消才返回
async Task RunEventLoopAsync(
    ProtocolSession session,
    WindowsInputInjector injector,
    Dictionary<ushort, HotkeyPresetDef> hotkeyMap,
    AgentSettings settings,
    WindowsClipboard clipboard,
    CancellationToken ct)
{
    // M3 TEXT/CLIPBOARD_SET 分帧重组缓冲（仅接收线程访问；随会话重建，防止跨会话污染）
    var pendingText = new List<byte>();
    var pendingClipboard = new List<byte>();

    var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    long moveCount = 0;
    long lastButtonDownTicks = 0;
    byte lastDownBtn = 0xFF;
    // 点按 down/up 目标按住时长（M1 验收报告建议 40–60ms）；设 55 补偿 Thread.Sleep ~15ms 定时器粒度
    const int TapGapMs = 55;
    session.Disconnected += _ => exitTcs.TrySetResult();
    session.MessageReceived += m =>
    {
        switch (m)
        {
            case MouseMoveMessage mv:
                var dx = Math.Clamp((int)Math.Round(mv.Dx * settings.MoveScale), short.MinValue, short.MaxValue);
                var dy = Math.Clamp((int)Math.Round(mv.Dy * settings.MoveScale), short.MinValue, short.MaxValue);
                injector.MouseMove((short)dx, (short)dy);
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
                injector.MouseButton(b.Button, b.Action == ProtocolConsts.PressAction.Down);
                Log.Info($"BUTTON btn={b.Button} action={b.Action}");
                break;
            case MouseScrollMessage s:
                injector.MouseScroll(s.Dx, s.Dy);
                Log.Info($"SCROLL dx={s.Dx} dy={s.Dy}");
                break;
            case KeyMessage k:
                injector.KeyPress(k.Code, k.Action == ProtocolConsts.PressAction.Down, k.Mods);
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
                    _ = Task.Run(() => injector.Text(text));
                }
                break;
            case ClipboardSetMessage c:
                pendingClipboard.AddRange(c.Data);
                if (c.Fin)
                {
                    var clipText = System.Text.Encoding.UTF8.GetString(pendingClipboard.ToArray());
                    pendingClipboard.Clear();
                    lastClipboardFromPhone = clipText;
                    Log.Info($"CLIPBOARD_SET ← 手机 ({clipText.Length} 字符) → 写入 PC 剪贴板");
                    _ = Task.Run(() => clipboard.SetText(clipText));
                }
                break;
            case HotkeyMessage h:
                if (hotkeyMap.TryGetValue(h.PresetId, out var preset))
                {
                    Log.Info($"HOTKEY presetId={h.PresetId} → {preset.Name} (vk=0x{preset.Vk:X2} mods=0x{preset.Mods:X2})");
                    // TapKey 内部阻塞 holdMs，放线程池避免拖慢接收线程（PONG/后续事件）
                    _ = Task.Run(() => injector.TapKey(preset.Vk, preset.Mods, preset.HoldMs));
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
    try { await session.SendAsync(new ByeMessage(ProtocolConsts.ByeReason.Normal), CancellationToken.None); } catch { /* 传输已断开 */ }
}

/// <summary>断线重连退避：1s→2s→4s→8s→15s，之后 30s 封顶。</summary>
static int BackoffDelayMs(int failedAttempt) => failedAttempt switch
{
    1 => 1_000,
    2 => 2_000,
    3 => 4_000,
    4 => 8_000,
    5 => 15_000,
    _ => 30_000,
};

static double Percentile(List<double> sorted, int p) =>
    sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p / 100.0) - 1)];
