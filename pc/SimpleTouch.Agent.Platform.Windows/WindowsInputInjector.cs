using System.Runtime.InteropServices;
using SimpleTouch.Core;
using SimpleTouch.Core.Protocol;

namespace SimpleTouch.Platform.Windows;

/// <summary>
/// Windows 输入注入实现（Win32 SendInput，免驱动）。
/// 文本注入走 KEYEVENTF_UNICODE（支持任意 Unicode/中文）；按键走 HID Usage → VK 映射表。
/// </summary>
public sealed class WindowsInputInjector : IInputInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventfMove = 0x0001;
    private const uint MouseEventfLeftDown = 0x0002, MouseEventfLeftUp = 0x0004;
    private const uint MouseEventfRightDown = 0x0008, MouseEventfRightUp = 0x0010;
    private const uint MouseEventfMiddleDown = 0x0020, MouseEventfMiddleUp = 0x0040;
    private const uint MouseEventfWheel = 0x0800, MouseEventfHWheel = 0x1000;
    private const uint KeyEventfKeyUp = 0x0002;
    private const uint KeyEventfUnicode = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private readonly HashSet<ushort> _vksDown = new();

    public void MouseMove(short dx, short dy) =>
        SendMouse(new MouseInput { Dx = dx, Dy = dy, Flags = MouseEventfMove });

    public void MouseButton(byte button, bool down)
    {
        uint flag = (button, down) switch
        {
            (ProtocolConsts.MouseButton.Left, true) => MouseEventfLeftDown,
            (ProtocolConsts.MouseButton.Left, false) => MouseEventfLeftUp,
            (ProtocolConsts.MouseButton.Right, true) => MouseEventfRightDown,
            (ProtocolConsts.MouseButton.Right, false) => MouseEventfRightUp,
            (ProtocolConsts.MouseButton.Middle, true) => MouseEventfMiddleDown,
            (ProtocolConsts.MouseButton.Middle, false) => MouseEventfMiddleUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "未知鼠标键"),
        };
        SendMouse(new MouseInput { Flags = flag });
    }

    public void MouseScroll(int dx, int dy)
    {
        var inputs = new List<Input>(2);
        if (dy != 0) inputs.Add(MouseWheelInput(MouseEventfWheel, dy * 120));
        if (dx != 0) inputs.Add(MouseWheelInput(MouseEventfHWheel, dx * 120));
        if (inputs.Count > 0) SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    public void KeyPress(byte hidUsage, bool down, byte mods)
    {
        ushort? vk = HidToVk(hidUsage);
        if (vk is null)
        {
            Log.Warn($"未收录的 HID Usage: 0x{hidUsage:X2}（后续版本补充映射表）");
            return;
        }

        if (down)
        {
            foreach (var modVk in ModsToVks(mods))
                if (_vksDown.Add(modVk)) SendVk(modVk, down: true);
            if (_vksDown.Add(vk.Value)) SendVk(vk.Value, down: true);
        }
        else
        {
            if (_vksDown.Remove(vk.Value)) SendVk(vk.Value, down: false);
            foreach (var modVk in ModsToVks(mods))
                if (_vksDown.Remove(modVk)) SendVk(modVk, down: false);
        }
    }

    public void Text(string text)
    {
        var units = text.ToCharArray(); // UTF-16 码元；代理对逐码元发送，Windows 自动组合
        var inputs = new List<Input>(units.Length * 2);
        foreach (var ch in units)
        {
            inputs.Add(UnicodeInput(ch, up: false));
            inputs.Add(UnicodeInput(ch, up: true));
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    /// <summary>组合键整段注入：修饰键→VK 按下，保持 holdMs，反序抬起（HOTKEY 媒体键/快捷键路径）。</summary>
    public void TapKey(ushort vk, byte mods, int holdMs)
    {
        var modVks = ModsToVks(mods).ToArray();
        var down = new List<Input>(modVks.Length + 1);
        foreach (var m in modVks) down.Add(VkInput(m, down: true));
        down.Add(VkInput(vk, down: true));
        SendInput((uint)down.Count, down.ToArray(), Marshal.SizeOf<Input>());

        // 媒体键消费太快部分应用不识别，保持一小段按下时长（可经 hotkeys.json holdMs 调整）
        Thread.Sleep(Math.Clamp(holdMs, 10, 200));

        var up = new List<Input>(modVks.Length + 1) { VkInput(vk, down: false) };
        foreach (var m in modVks) up.Add(VkInput(m, down: false));
        SendInput((uint)up.Count, up.ToArray(), Marshal.SizeOf<Input>());
    }

    // ---- 内部 ----

    private static Input MouseWheelInput(uint flag, int delta) =>
        new() { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { MouseData = (uint)delta, Flags = flag } } };

    private static void SendMouse(MouseInput mi) =>
        SendInput(1, [new Input { Type = InputMouse, U = new InputUnion { Mouse = mi } }], Marshal.SizeOf<Input>());

    private static Input UnicodeInput(char ch, bool up) =>
        new()
        {
            Type = InputKeyboard,
            U = new InputUnion { Keyboard = new KeyboardInput { Scan = ch, Flags = KeyEventfUnicode | (up ? KeyEventfKeyUp : 0) } },
        };

    private static Input VkInput(ushort vk, bool down) =>
        new()
        {
            Type = InputKeyboard,
            U = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = down ? 0 : KeyEventfKeyUp } },
        };

    private static void SendVk(ushort vk, bool down) =>
        SendInput(1, [new Input { Type = InputKeyboard, U = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = down ? 0 : KeyEventfKeyUp } } }], Marshal.SizeOf<Input>());

    private static IEnumerable<ushort> ModsToVks(byte mods)
    {
        if ((mods & ProtocolConsts.KeyMods.Ctrl) != 0) yield return 0x11;   // VK_CONTROL
        if ((mods & ProtocolConsts.KeyMods.Shift) != 0) yield return 0x10; // VK_SHIFT
        if ((mods & ProtocolConsts.KeyMods.Alt) != 0) yield return 0x12;   // VK_MENU
        if ((mods & ProtocolConsts.KeyMods.Meta) != 0) yield return 0x5B;  // VK_LWIN
    }

    /// <summary>USB HID Usage（Keyboard 页）→ Windows VK。覆盖常用键，M2/M3 按需扩充。</summary>
    private static ushort? HidToVk(byte u) => u switch
    {
        >= 0x04 and <= 0x1D => (ushort)('A' + u - 0x04),   // A–Z
        >= 0x1E and <= 0x26 => (ushort)('1' + u - 0x1E),   // 1–9
        0x27 => 0x30,                                       // 0
        0x28 => 0x0D,                                       // Enter
        0x29 => 0x1B,                                       // Escape
        0x2A => 0x08,                                       // Backspace
        0x2B => 0x09,                                       // Tab
        0x2C => 0x20,                                       // Space
        0x39 => 0x14,                                       // CapsLock
        0x4B => 0x21,                                       // PageUp
        0x4C => 0x2E,                                       // Delete
        0x4E => 0x22,                                       // PageDown
        0x4F => 0x27,                                       // Right
        0x50 => 0x25,                                       // Left
        0x51 => 0x28,                                       // Down
        0x52 => 0x26,                                       // Up
        >= 0x3A and <= 0x45 => (ushort)(0x70 + u - 0x3A),   // F1–F12
        0xE0 => 0x11,                                       // LCtrl
        0xE1 => 0x10,                                       // LShift
        0xE2 => 0x12,                                       // LAlt
        0xE3 => 0x5B,                                       // LMeta
        _ => null,
    };
}
