using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace SimpleTouch.Platform.Windows;

/// <summary>已配对蓝牙设备枚举（--list 与连接前检查共用）。</summary>
public static class DeviceScanner
{
    public static async Task<IReadOnlyList<string>> ListPairedDeviceNamesAsync(CancellationToken ct = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector).AsTask(ct);
        return devices
            .Select(d => d.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
