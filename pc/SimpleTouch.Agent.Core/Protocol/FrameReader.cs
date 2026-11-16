using System.Buffers.Binary;

namespace SimpleTouch.Core.Protocol;

/// <summary>
/// 流式帧读取器：处理 RFCOMM 字节流的粘包/半包。
/// 流在帧边界干净关闭返回 null；在帧中途断开抛 EndOfStreamException。
/// </summary>
public sealed class FrameReader
{
    private readonly Stream _stream;

    public FrameReader(Stream stream) => _stream = stream;

    public async Task<ParsedFrame?> ReadFrameAsync(CancellationToken ct = default)
    {
        var header = new byte[ProtocolConsts.FrameHeaderSize];
        int first = await ReadByteAsync(ct).ConfigureAwait(false);
        if (first < 0) return null; // 帧边界 EOF
        header[0] = (byte)first;
        if (!await ReadExactlyAsync(header, 1, 3, ct).ConfigureAwait(false))
            throw new EndOfStreamException("流在帧头中途结束");

        int len = BinaryPrimitives.ReadUInt16LittleEndian(header);
        if (len > ProtocolConsts.MaxPayloadSize)
            throw new InvalidDataException($"payload 超上限: {len}");

        var payload = new byte[len];
        if (len > 0 && !await ReadExactlyAsync(payload, 0, len, ct).ConfigureAwait(false))
            throw new EndOfStreamException("流在 payload 中途结束");

        var frame = new byte[ProtocolConsts.FrameHeaderSize + len];
        header.CopyTo(frame, 0);
        if (len > 0) payload.CopyTo(frame, 4);
        return FrameCodec.Decode(frame);
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        var buf = new byte[1];
        int n = await _stream.ReadAsync(buf, ct).ConfigureAwait(false);
        return n == 0 ? -1 : buf[0];
    }

    private async Task<bool> ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int total = 0;
        while (total < count)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct).ConfigureAwait(false);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }
}
