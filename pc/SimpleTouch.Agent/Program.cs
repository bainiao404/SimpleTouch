using System.Diagnostics;
using SimpleTouch.Core;
using SimpleTouch.Core.Config;
using SimpleTouch.Core.Protocol;
using SimpleTouch.Host;
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

// 设备名优先级：命令行参数 > settings.json 记忆值 > 出厂默认
var savedDevice = AgentSettings.LoadOrCreate(Path.Combine(AppContext.BaseDirectory, "config", "settings.json")).DeviceName;
var deviceName = argsList.FirstOrDefault(a => !a.StartsWith('-'))
    ?? (string.IsNullOrWhiteSpace(savedDevice) ? "Xiaomi Pad 5 Pro" : savedDevice);
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

    if (argsList.Contains("--ping"))
    {
        // RTT 测量模式：延迟报告数据源（执行文档 §4）。测量场景单次连接，不适用重连循环
        await using var pingTransport = await WinRfcommTransport.ConnectAsync(deviceName, ProtocolConsts.RfcommServiceUuid, cts.Token);
        await using var pingSession = new ProtocolSession(pingTransport);
        await pingTransport.StartReceivingAsync();

        if (!await pingSession.HandshakeAsync("SimpleTouch.PC", AgentHost.HandshakeCaps, sendReady: true, cts.Token))
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

    // M4.5 主模式：内核已解耦至 SimpleTouch.Agent.Host（与 WPF UI 共用同一 AgentHost）
    await using var host = new AgentHost(deviceName);
    host.RememberDevice(deviceName);
    await host.RunAsync(cts.Token);
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
