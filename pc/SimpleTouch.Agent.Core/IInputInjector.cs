namespace SimpleTouch.Core;

/// <summary>
/// 输入注入抽象（方案文档 §6）：Windows 用 SendInput 实现；macOS/Linux 后续各加实现。
/// hidUsage 采用 USB HID Usage ID（Keyboard 页 0x07），与协议 KEY 消息同词表。
/// </summary>
public interface IInputInjector
{
    void MouseMove(short dx, short dy);
    void MouseButton(byte button, bool down);
    void MouseScroll(int dx, int dy); // dy 正值=向上滚动
    void KeyPress(byte hidUsage, bool down, byte mods);
    void Text(string text);           // Unicode 直注（含代理对字符）

    /// <summary>组合键整段注入（HOTKEY 映射用）：修饰键+VK 按下 → holdMs → 按相反序抬起。调用方须在线程池执行（内部阻塞）。</summary>
    void TapKey(ushort vk, byte mods, int holdMs);
}
