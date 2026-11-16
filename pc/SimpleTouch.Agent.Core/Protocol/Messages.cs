namespace SimpleTouch.Core.Protocol;

public abstract record ProtocolMessage;

public sealed record HelloMessage(int Version, int Caps, string Name) : ProtocolMessage;
public sealed record ReadyMessage : ProtocolMessage;
public sealed record MouseMoveMessage(short Dx, short Dy) : ProtocolMessage;
public sealed record MouseButtonMessage(byte Button, byte Action) : ProtocolMessage;
public sealed record MouseScrollMessage(sbyte Dx, sbyte Dy) : ProtocolMessage;
public sealed record KeyMessage(byte Code, byte Action, byte Mods) : ProtocolMessage;
public sealed record TextMessage(bool Fin, byte[] Data) : ProtocolMessage;
public sealed record HotkeyMessage(ushort PresetId) : ProtocolMessage;
public sealed record ClipboardSetMessage(bool Fin, byte[] Data) : ProtocolMessage;
public sealed record PingMessage(long Timestamp) : ProtocolMessage;
public sealed record PongMessage(long Timestamp) : ProtocolMessage;
public sealed record ByeMessage(byte Reason) : ProtocolMessage;

/// <summary>兼容规则：未知 type 解码为 Unknown，由调用方丢弃（保留便于日志）。</summary>
public sealed record UnknownMessage(byte Type, byte[] Payload) : ProtocolMessage;

public sealed record ParsedFrame(byte Seq, ProtocolMessage Message);
