namespace SimpleTouch.Core;

/// <summary>
/// 剪贴板抽象（方案文档 §6）：Windows 原生实现；macOS/Linux 后续各加实现。
/// 文本以 UTF-16 字符串传递，分帧由协议层负责（CLIPBOARD_SET，TextFrameSplitter）。
/// </summary>
public interface IClipboard
{
    /// <summary>写入文本剪贴板（阻塞，内部自旋等待剪贴板可用）。</summary>
    void SetText(string text);

    /// <summary>读取文本剪贴板；剪贴板非文本内容时返回 null。</summary>
    string? GetText();

    /// <summary>开始监听剪贴板变更（触发 <see cref="TextChanged"/>）。</summary>
    void StartMonitoring();

    /// <summary>剪贴板文本变更通知（轮询线程触发；事件内不要做长阻塞操作）。</summary>
    event Action<string>? TextChanged;
}
