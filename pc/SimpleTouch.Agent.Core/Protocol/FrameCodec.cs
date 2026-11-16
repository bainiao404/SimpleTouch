using System.Buffers.Binary;

namespace SimpleTouch.Core.Protocol;

/// <summary>帧编解码：[len:u16][type:u8][seq:u8][payload]，小端。与 docs/protocol.md §2 对应。</summary>
public static class FrameCodec
{
    public static byte TypeOf(ProtocolMessage message) => message switch
    {
        HelloMessage => ProtocolConsts.MsgType.Hello,
        ReadyMessage => ProtocolConsts.MsgType.Ready,
        MouseMoveMessage => ProtocolConsts.MsgType.MouseMove,
        MouseButtonMessage => ProtocolConsts.MsgType.MouseButton,
        MouseScrollMessage => ProtocolConsts.MsgType.MouseScroll,
        KeyMessage => ProtocolConsts.MsgType.Key,
        TextMessage => ProtocolConsts.MsgType.Text,
        HotkeyMessage => ProtocolConsts.MsgType.Hotkey,
        ClipboardSetMessage => ProtocolConsts.MsgType.ClipboardSet,
        PingMessage => ProtocolConsts.MsgType.Ping,
        PongMessage => ProtocolConsts.MsgType.Pong,
        ByeMessage => ProtocolConsts.MsgType.Bye,
        UnknownMessage u => u.Type,
        _ => throw new InvalidOperationException($"未知消息类型: {message.GetType().Name}"),
    };

    public static byte[] Encode(ProtocolMessage message, byte seq)
    {
        var payload = EncodePayload(message);
        if (payload.Length > ProtocolConsts.MaxPayloadSize)
            throw new InvalidOperationException($"payload {payload.Length} 超上限 {ProtocolConsts.MaxPayloadSize}");

        var frame = new byte[ProtocolConsts.FrameHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, (ushort)payload.Length);
        frame[2] = TypeOf(message);
        frame[3] = seq;
        payload.CopyTo(frame, 4);
        return frame;
    }

    public static ParsedFrame Decode(byte[] frame) => Decode(frame, 0, frame.Length);

    public static ParsedFrame Decode(byte[] buffer, int offset, int count)
    {
        if (count < ProtocolConsts.FrameHeaderSize)
            throw new InvalidDataException("帧不完整：不足 4 字节帧头");
        int len = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset));
        if (len > ProtocolConsts.MaxPayloadSize)
            throw new InvalidDataException($"payload 超上限: {len}");
        if (count < ProtocolConsts.FrameHeaderSize + len)
            throw new InvalidDataException("帧不完整：payload 截断");

        byte type = buffer[offset + 2];
        byte seq = buffer[offset + 3];
        var payload = buffer.AsSpan(offset + 4, len).ToArray();
        return new ParsedFrame(seq, DecodePayload(type, payload));
    }

    private static byte[] EncodePayload(ProtocolMessage m) => m switch
    {
        HelloMessage h => EncodeHello(h),
        ReadyMessage => [],
        MouseMoveMessage mv => Bytes16(mv.Dx, mv.Dy),
        MouseButtonMessage b => [b.Button, b.Action],
        MouseScrollMessage s => [(byte)s.Dx, (byte)s.Dy],
        KeyMessage k => [k.Code, k.Action, k.Mods],
        TextMessage t => EncodeFragment(t.Fin, t.Data),
        HotkeyMessage hk => Bytes16((short)hk.PresetId),
        ClipboardSetMessage c => EncodeFragment(c.Fin, c.Data),
        PingMessage p => BitConverter.GetBytes(p.Timestamp),
        PongMessage p => BitConverter.GetBytes(p.Timestamp),
        ByeMessage b => [b.Reason],
        UnknownMessage u => (byte[])u.Payload.Clone(),
        _ => throw new InvalidOperationException($"未知消息类型: {m.GetType().Name}"),
    };

    private static ProtocolMessage DecodePayload(byte type, byte[] p) => type switch
    {
        ProtocolConsts.MsgType.Hello => DecodeHello(p),
        ProtocolConsts.MsgType.Ready => RequireEmpty(p, new ReadyMessage()),
        ProtocolConsts.MsgType.MouseMove => RequireLen(p, 4, new MouseMoveMessage(
            BinaryPrimitives.ReadInt16LittleEndian(p),
            BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(2)))),
        ProtocolConsts.MsgType.MouseButton => RequireLen(p, 2, new MouseButtonMessage(p[0], p[1])),
        ProtocolConsts.MsgType.MouseScroll => RequireLen(p, 2, new MouseScrollMessage((sbyte)p[0], (sbyte)p[1])),
        ProtocolConsts.MsgType.Key => RequireLen(p, 3, new KeyMessage(p[0], p[1], p[2])),
        ProtocolConsts.MsgType.Text => DecodeFragment(p, static (fin, data) => new TextMessage(fin, data)),
        ProtocolConsts.MsgType.Hotkey => RequireLen(p, 2, new HotkeyMessage(BinaryPrimitives.ReadUInt16LittleEndian(p))),
        ProtocolConsts.MsgType.ClipboardSet => DecodeFragment(p, static (fin, data) => new ClipboardSetMessage(fin, data)),
        ProtocolConsts.MsgType.Ping => RequireLen(p, 8, new PingMessage(BinaryPrimitives.ReadInt64LittleEndian(p))),
        ProtocolConsts.MsgType.Pong => RequireLen(p, 8, new PongMessage(BinaryPrimitives.ReadInt64LittleEndian(p))),
        ProtocolConsts.MsgType.Bye => RequireLen(p, 1, new ByeMessage(p[0])),
        _ => new UnknownMessage(type, p),
    };

    private static byte[] EncodeHello(HelloMessage h)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(h.Name);
        if (name.Length > 255) throw new InvalidOperationException("name 超过 255 字节");
        var payload = new byte[7 + name.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)h.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(2), (uint)h.Caps);
        payload[6] = (byte)name.Length;
        name.CopyTo(payload, 7);
        return payload;
    }

    private static HelloMessage DecodeHello(byte[] p)
    {
        if (p.Length < 7) throw new InvalidDataException("HELLO 载荷不足 7 字节");
        int nameLen = p[6];
        if (p.Length != 7 + nameLen) throw new InvalidDataException("HELLO name 长度不符");
        var name = System.Text.Encoding.UTF8.GetString(p, 7, nameLen);
        return new HelloMessage(
            BinaryPrimitives.ReadUInt16LittleEndian(p),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(2)),
            name);
    }

    private static byte[] EncodeFragment(bool fin, byte[] data)
    {
        var payload = new byte[1 + data.Length];
        payload[0] = fin ? (byte)0x01 : (byte)0x00;
        data.CopyTo(payload, 1);
        return payload;
    }

    private static ProtocolMessage DecodeFragment(byte[] p, Func<bool, byte[], ProtocolMessage> factory)
    {
        if (p.Length < 1) throw new InvalidDataException("分帧载荷缺少 flags");
        return factory((p[0] & 0x01) != 0, p.AsSpan(1).ToArray());
    }

    private static byte[] Bytes16(short a, short b)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(payload, a);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2), b);
        return payload;
    }

    private static byte[] Bytes16(short a)
    {
        var payload = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(payload, a);
        return payload;
    }

    private static ProtocolMessage RequireLen(byte[] p, int expected, ProtocolMessage message)
    {
        if (p.Length != expected)
            throw new InvalidDataException($"载荷长度 {p.Length} ≠ 期望 {expected}");
        return message;
    }

    private static ProtocolMessage RequireEmpty(byte[] p, ProtocolMessage message) => RequireLen(p, 0, message);
}
