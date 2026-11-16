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

    // M0 验收：PING ×100 @200ms → RTT 统计（M1 延迟报告复用该方法）
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
