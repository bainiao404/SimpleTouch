using System.Buffers.Binary;
using SimpleTouch.Core;
using SimpleTouch.Core.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace SimpleTouch.Platform.Windows;

/// <summary>
/// Windows RFCOMM 客户端传输实现（WinRT StreamSocket）。
/// 连接流程：枚举已配对设备 → 打开句柄 → SDP 发现自定义服务 UUID → 连接。
/// </summary>
public sealed class WinRfcommTransport : ITransport
{
    private readonly StreamSocket _socket;
    private readonly DataWriter _writer;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public event Action<ParsedFrame>? FrameReceived;
    public event Action<Exception?>? Disconnected;

    /// <summary>连接已在工厂方法完成；此实现为占位（接口约定保留）。</summary>
    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    private WinRfcommTransport(StreamSocket socket)
    {
        _socket = socket;
        _writer = new DataWriter(socket.OutputStream) { ByteOrder = ByteOrder.LittleEndian };
    }

    public static async Task<WinRfcommTransport> ConnectAsync(string deviceName, Guid serviceUuid, CancellationToken ct = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var paired = (await DeviceInformation.FindAllAsync(selector).AsTask(ct)).ToList();
        var info = paired.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"未找到已配对设备“{deviceName}”。已配对: [{string.Join("、", paired.Select(d => d.Name).Where(n => n.Length > 0))}]");

        var device = await BluetoothDevice.FromBluetoothAddressAsync(ParseBluetoothAddress(info.Id));
        if (device is null)
            throw new InvalidOperationException($"无法打开蓝牙设备句柄: {info.Name}");

        // 优先实时 SDP 发现（要求手机端服务已注册且在范围内），失败回退配对缓存记录
        var live = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(ct);
        var service = live.Services.FirstOrDefault(s => s.ServiceId.Uuid == serviceUuid);
        if (service is null)
        {
            var cached = await device.GetRfcommServicesAsync(BluetoothCacheMode.Cached).AsTask(ct);
            service = cached.Services.FirstOrDefault(s => s.ServiceId.Uuid == serviceUuid);
        }
        if (service is null)
            throw new InvalidOperationException(
                $"未发现 SimpleTouch RFCOMM 服务({serviceUuid})：请确认手机端 App 已启动并处于监听状态");

        var socket = new StreamSocket();
        await socket.ConnectAsync(
            service.ConnectionHostName,
            service.ConnectionServiceName,
            SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(ct);
        return new WinRfcommTransport(socket);
    }

    public Task StartReceivingAsync(CancellationToken ct = default)
    {
        var reader = new DataReader(_socket.InputStream) { ByteOrder = ByteOrder.LittleEndian };
        _ = Task.Run(() => ReceiveLoopAsync(reader), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task SendFrameAsync(byte type, byte seq, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        await _sendGate.WaitAsync(ct);
        try
        {
            _writer.WriteUInt16((ushort)payload.Length);
            _writer.WriteByte(type);
            _writer.WriteByte(seq);
            _writer.WriteBytes(payload.ToArray());
            await _writer.StoreAsync().AsTask(ct);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(DataReader reader)
    {
        try
        {
            while (true)
            {
                var header = new byte[ProtocolConsts.FrameHeaderSize];
                if (!await LoadExactlyAsync(reader, header, ProtocolConsts.FrameHeaderSize)) break; // 帧边界 EOF

                int len = BinaryPrimitives.ReadUInt16LittleEndian(header);
                if (len > ProtocolConsts.MaxPayloadSize)
                    throw new InvalidDataException($"payload 超上限: {len}");

                var payload = new byte[len];
                if (len > 0 && !await LoadExactlyAsync(reader, payload, len))
                    throw new EndOfStreamException("流在 payload 中途结束");

                var frame = new byte[ProtocolConsts.FrameHeaderSize + len];
                header.CopyTo(frame, 0);
                if (len > 0) payload.CopyTo(frame, 4);
                FrameReceived?.Invoke(FrameCodec.Decode(frame));
            }
            Disconnected?.Invoke(null);
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(ex);
        }
    }

    private static async Task<bool> LoadExactlyAsync(DataReader reader, byte[] target, int count)
    {
        int total = 0;
        while (total < count)
        {
            uint loaded = await reader.LoadAsync((uint)(count - total)).AsTask();
            if (loaded == 0) return false;
            var chunk = new byte[loaded];
            reader.ReadBytes(chunk);
            System.Buffer.BlockCopy(chunk, 0, target, total, chunk.Length);
            total += chunk.Length;
        }
        return true;
    }

    /// <summary>DeviceInformation.Id 形如 "Bluetooth#BluetoothAA:BB:CC:DD:EE:FF-..."，解析出 MAC 地址。</summary>
    internal static ulong ParseAddressForDiagnostics(string deviceInfoId) => ParseBluetoothAddress(deviceInfoId);

    /// <summary>DeviceInformation.Id 形如 "Bluetooth#BluetoothAA:BB:CC:DD:EE:FF-..."，解析出 MAC 地址。</summary>
    private static ulong ParseBluetoothAddress(string deviceInfoId)
    {
        const string marker = "Bluetooth#Bluetooth";
        int start = deviceInfoId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        start = start >= 0 ? start + marker.Length : 0;
        int end = deviceInfoId.IndexOf('-', start);
        if (end < start) end = deviceInfoId.Length;
        var hex = deviceInfoId[start..end].Replace(":", "", StringComparison.Ordinal);
        return Convert.ToUInt64(hex, 16);
    }

    public async ValueTask DisposeAsync()
    {
        try { _writer.DetachStream(); } catch { /* 流可能已关闭 */ }
        _socket.Dispose();
        _writer.Dispose();
        _sendGate.Dispose();
        await Task.CompletedTask;
    }
}
