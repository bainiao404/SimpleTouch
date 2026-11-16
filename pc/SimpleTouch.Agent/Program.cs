using System.Diagnostics;
using SimpleTouch.Core;
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

    await using var transport = await WinRfcommTransport.ConnectAsync(deviceName, ProtocolConsts.RfcommServiceUuid, cts.Token);
    await using var session = new ProtocolSession(transport);
    await transport.StartReceivingAsync();
    session.Disconnected += ex => Log.Warn($"连接断开: {ex?.Message ?? "对端正常关闭"}");

    const int allCaps = ProtocolConsts.CapsBits.TextInject | ProtocolConsts.CapsBits.Clipboard
                      | ProtocolConsts.CapsBits.Macros | ProtocolConsts.CapsBits.Consumer;
    if (!await session.HandshakeAsync("SimpleTouch.PC", allCaps, sendReady: true, cts.Token))
    {
        Log.Error("握手失败（版本不兼容或 5s 内未收到对端 HELLO）");
        return 1;
    }
    Log.Info($"握手完成: peer={session.PeerName} ver={session.PeerVersion} caps=0x{session.PeerCaps:X}");

    if (argsList.Contains("--ping"))
    {
        // RTT 测量模式：延迟报告数据源（执行文档 §4）
        var rtts = new List<double>();
        for (var i = 0; i < 100 && !cts.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew();
            var pong = await session.PingAsync(Environment.TickCount64, cts.Token);
            sw.Stop();
            rtts.Add(sw.Elapsed.TotalMilliseconds);
            Log.Info($"PING #{i + 1}: {sw.Elapsed.TotalMilliseconds:F1} ms (ts={pong.Timestamp})");
            await Task.Delay(200, cts.Token);
        }

        var sorted = rtts.OrderBy(x => x).ToList();
        Log.Info($"RTT 统计: N={sorted.Count} P50={Percentile(sorted, 50):F1}ms P95={Percentile(sorted, 95):F1}ms Max={sorted[^1]:F1}ms");
        await session.SendAsync(new ByeMessage(ProtocolConsts.ByeReason.Normal), CancellationToken.None);
        return 0;
    }

    // 事件循环模式：触摸板/键盘事件实时注入（M1 主模式）
    var injector = new WindowsInputInjector();
    var exitTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    long moveCount = 0;
    session.Disconnected += _ => exitTcs.TrySetResult();
    session.MessageReceived += m =>
    {
        switch (m)
        {
            case MouseMoveMessage mv:
                injector.MouseMove(mv.Dx, mv.Dy);
                Interlocked.Increment(ref moveCount);
                break;
            case MouseButtonMessage b:
                injector.MouseButton(b.Button, b.Action == ProtocolConsts.PressAction.Down);
                Log.Info($"BUTTON btn={b.Button} action={b.Action}");
                break;
            case MouseScrollMessage s:
                injector.MouseScroll(s.Dx, s.Dy);
                Log.Info($"SCROLL dx={s.Dx} dy={s.Dy}");
                break;
            case ByeMessage:
                exitTcs.TrySetResult();
                break;
        }
    };
    Log.Info("事件循环已启动——在平板触摸板上滑动/点按即可控制 PC 指针（Ctrl+C 退出）");
    _ = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested && !exitTcs.Task.IsCompleted)
        {
            try { await Task.Delay(5000, cts.Token); } catch (OperationCanceledException) { break; }
            var n = Interlocked.Exchange(ref moveCount, 0);
            Log.Info($"吞吐: {n / 5.0:F0} move/s");
        }
    });
    await exitTcs.Task.WaitAsync(cts.Token);
    Log.Info("会话结束");
    try { await session.SendAsync(new ByeMessage(ProtocolConsts.ByeReason.Normal), CancellationToken.None); } catch { /* 传输已断开 */ }
    return 0;
}
catch (OperationCanceledException) when (!cts.IsCancellationRequested)
{
    Log.Error("PING 超时（5s 内未收到 PONG）或握手超时");
    return 2;
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

static double Percentile(List<double> sorted, int p) =>
    sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * p / 100.0) - 1)];
