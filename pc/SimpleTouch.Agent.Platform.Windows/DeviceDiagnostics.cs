using SimpleTouch.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace SimpleTouch.Platform.Windows;

/// <summary>连接排障工具：枚举目标设备的全部 RFCOMM 服务并尝试服务选择器直查目标 UUID。</summary>
public static class DeviceDiagnostics
{
    public static async Task DumpAsync(string deviceName, Guid serviceUuid, CancellationToken ct)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var paired = (await DeviceInformation.FindAllAsync(selector).AsTask(ct)).ToList();
        var info = paired.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到已配对设备“{deviceName}”");

        var device = await BluetoothDevice.FromBluetoothAddressAsync(WinRfcommTransport.ParseAddressForDiagnostics(info.Id));
        if (device is null)
        {
            Log.Error("无法打开蓝牙设备句柄");
            return;
        }
        Log.Info($"设备句柄 OK: {device.Name} (Id={device.BluetoothDeviceId.Id})");

        foreach (var mode in new[] { BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
        {
            var result = await device.GetRfcommServicesAsync(mode).AsTask(ct);
            Log.Info($"[{mode}] RFCOMM 服务数: {result.Services.Count}");
            foreach (var s in result.Services)
                Log.Info($"  - {s.ServiceId.Uuid}");
        }

        // MS Rfcomm Chat 样例路线：服务选择器枚举 + FromIdAsync 直接取服务
        var serviceSelector = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(serviceUuid));
        var serviceInfos = await DeviceInformation.FindAllAsync(serviceSelector).AsTask(ct);
        Log.Info($"服务选择器({serviceUuid}) 命中设备数: {serviceInfos.Count}");
        foreach (var si in serviceInfos)
        {
            var svc = await RfcommDeviceService.FromIdAsync(si.Id).AsTask(ct);
            Log.Info($"  - {si.Name}: channel={svc?.ConnectionServiceName ?? "(null)"}");
        }

        // SDP 通路探针：用公认存在的标准服务判断「SDP 查询整体失效」还是「仅自定义服务缺失」
        var probes = new Dictionary<string, Guid>
        {
            ["HFP(111F)"] = new("0000111f-0000-1000-8000-00805f9b34fb"),
            ["OPP(1105)"] = new("00001105-0000-1000-8000-00805f9b34fb"),
            ["SPP(1101)"] = new("00001101-0000-1000-8000-00805f9b34fb"),
        };
        foreach (var (label, uuid) in probes)
        {
            var hit = await DeviceInformation.FindAllAsync(
                RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(uuid))).AsTask(ct);
            Log.Info($"探针 {label}: 命中 {hit.Count}");
        }
    }
}
