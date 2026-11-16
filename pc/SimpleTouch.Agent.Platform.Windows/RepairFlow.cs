using SimpleTouch.Core;
using SimpleTouch.Core.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace SimpleTouch.Platform.Windows;

/// <summary>
/// 重新配对流程：自定义 RFCOMM 服务的 SDP 记录只在「配对时且 App 处于监听」才会进入 Windows 缓存
/// （本机适配器实测 GetRfcommServicesAsync(Uncached) 实时查询不可用）。
/// 用户操作：解除配对后保持平板蓝牙设置页可见，配对弹窗出现时点「配对」。
/// </summary>
public static class RepairFlow
{
    public static async Task<bool> RunAsync(string deviceName, CancellationToken ct)
    {
        // 1. 解除旧配对（双侧同时失效）
        var pairedSel = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var paired = (await DeviceInformation.FindAllAsync(pairedSel).AsTask(ct)).ToList();
        var old = paired.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        if (old is null)
        {
            Log.Info($"设备“{deviceName}”当前未配对，跳过解除");
        }
        else
        {
            Log.Info($"解除旧配对: {old.Name}");
            var unpairResult = await old.Pairing.UnpairAsync().AsTask(ct);
            Log.Info($"解除结果: {unpairResult.Status}");
            await Task.Delay(2000, ct);
        }

        // 2. 发现未配对的目标设备（要求平板端蓝牙设置页保持打开以进入可发现状态）
        var unpairedSel = BluetoothDevice.GetDeviceSelectorFromPairingState(false);
        DeviceInformation? target = null;
        for (var attempt = 1; attempt <= 10 && target is null; attempt++)
        {
            var candidates = (await DeviceInformation.FindAllAsync(unpairedSel).AsTask(ct)).ToList();
            target = candidates.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                Log.Info($"[{attempt}/10] 未发现目标设备（附近发现: [{string.Join("、", candidates.Select(d => d.Name))}]）——请确认平板蓝牙设置页保持打开");
                await Task.Delay(2000, ct);
            }
        }
        if (target is null)
        {
            Log.Error("重试后仍未发现目标设备，放弃");
            return false;
        }

        // 3. 自定义配对：PairingRequested 在 Custom 子对象上；控制台应用无系统配对 UI，
        //    须手动 Accept。平板端仍会弹出配对确认框，需要用户点「配对」。
        Log.Info($"发起配对: {target.Name} —— 请在平板上确认配对弹窗（App 必须保持监听中）");
        var custom = target.Pairing.Custom;
        custom.PairingRequested += (_, args) =>
        {
            Log.Info($"配对请求: Kind={args.PairingKind}, Pin={args.Pin}");
            switch (args.PairingKind)
            {
                case DevicePairingKinds.ConfirmOnly:
                case DevicePairingKinds.ConfirmPinMatch:
                case DevicePairingKinds.DisplayPin:
                    args.Accept();
                    break;
                default:
                    // 19041 SDK 无 AcceptWithPin，ProvidePin 不可用；数值比对/确认已覆盖安卓配对
                    Log.Warn($"未处理的配对类型 {args.PairingKind}，拒绝");
                    break;
            }
        };
        var kinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch
                  | DevicePairingKinds.DisplayPin;
        var result = await custom.PairAsync(kinds, DevicePairingProtectionLevel.Default).AsTask(ct);
        Log.Info($"配对结果: {result.Status}");
        if (result.Status != DevicePairingResultStatus.Paired) return false;

        // 4. 验证自定义服务已进入缓存
        await Task.Delay(1500, ct);
        var device = await BluetoothDevice.FromBluetoothAddressAsync(WinRfcommTransport.ParseAddressForDiagnostics(target.Id));
        if (device is null) return false;
        var services = await device.GetRfcommServicesAsync(BluetoothCacheMode.Cached).AsTask(ct);
        Log.Info($"配对后缓存 RFCOMM 服务数: {services.Services.Count}");
        foreach (var s in services.Services)
            Log.Info($"  - {s.ServiceId.Uuid}");
        return services.Services.Any(s => s.ServiceId.Uuid == ProtocolConsts.RfcommServiceUuid);
    }
}
