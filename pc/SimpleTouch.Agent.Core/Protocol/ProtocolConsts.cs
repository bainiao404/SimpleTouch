namespace SimpleTouch.Core.Protocol;

/// <summary>协议常量，与 docs/protocol.md v1 对应。</summary>
public static class ProtocolConsts
{
    public const int ProtocolVersion = 1;
    public const int MaxPayloadSize = 4096;
    public const int FrameHeaderSize = 4; // len:u16 + type:u8 + seq:u8

    public static readonly Guid RfcommServiceUuid = new("8b6d7c1a-2f4e-4c9b-9a3d-5e1f0a2b3c4d");
    public const string RfcommServiceName = "SimpleTouch";

    public static class MsgType
    {
        public const byte Hello = 0x01;
        public const byte Ready = 0x02;
        public const byte MouseMove = 0x10;
        public const byte MouseButton = 0x11;
        public const byte MouseScroll = 0x12;
        public const byte Key = 0x20;
        public const byte Text = 0x21;
        public const byte Hotkey = 0x22;
        public const byte ClipboardSet = 0x30;
        public const byte Ping = 0x40;
        public const byte Pong = 0x41;
        public const byte Bye = 0x50;
    }

    public static class MouseButton
    {
        public const byte Left = 0;
        public const byte Right = 1;
        public const byte Middle = 2;
    }

    public static class PressAction
    {
        public const byte Down = 0;
        public const byte Up = 1;
    }

    public static class KeyMods
    {
        public const byte Ctrl = 0x01;
        public const byte Shift = 0x02;
        public const byte Alt = 0x04;
        public const byte Meta = 0x08;
    }

    public static class ByeReason
    {
        public const byte Normal = 0;
        public const byte AppClosing = 1;
        public const byte SessionReplaced = 2;
        public const byte VersionMismatch = 3;
        public const byte ProtocolError = 4;
    }

    public static class CapsBits
    {
        public const int TextInject = 0x01;
        public const int Clipboard = 0x02;
        public const int Macros = 0x04;
        public const int Consumer = 0x08;
    }
}
