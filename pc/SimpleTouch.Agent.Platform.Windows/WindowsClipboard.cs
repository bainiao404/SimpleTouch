using System.Runtime.InteropServices;
using SimpleTouch.Core;

namespace SimpleTouch.Platform.Windows;

/// <summary>
/// Windows 剪贴板实现（纯 Win32 P/Invoke，免 WinForms 依赖）：
/// - Set/Get：CF_UNICODETEXT，OpenClipboard 自旋重试（剪贴板可能被其他应用短暂占用）
/// - 监听：轮询 GetClipboardSequenceNumber（1s 间隔）——比 message-only 窗口少一套
///   窗口类/消息循环管道，Agent 场景 1s 同步延迟完全够用（M4 发布前如有需求再换监听窗口）
/// </summary>
public sealed class WindowsClipboard : IClipboard, IDisposable
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr handle, out nuint size);

    public event Action<string>? TextChanged;

    private volatile bool _running;
    private Thread? _pollThread;
    private uint _lastSeq;

    public void StartMonitoring()
    {
        if (_pollThread is not null) return;
        _running = true;
        _lastSeq = GetClipboardSequenceNumber();
        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "clipboard-poll" };
        _pollThread.Start();
    }

    private void PollLoop()
    {
        while (_running)
        {
            Thread.Sleep(1000);
            try
            {
                var seq = GetClipboardSequenceNumber();
                if (seq == _lastSeq) continue;
                _lastSeq = seq;

                var text = GetText();
                if (!string.IsNullOrEmpty(text))
                    TextChanged?.Invoke(text);
            }
            catch
            {
                // 监听永不因单次读失败中断
            }
        }
    }

    public void SetText(string text)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0"); // CF_UNICODETEXT = UTF-16LE 含终止符
        var handle = IntPtr.Zero;
        SpinUntilClipboardOpen();
        try
        {
            EmptyClipboard();
            handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            var ptr = GlobalLock(handle);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            GlobalUnlock(handle);
            if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
                throw new InvalidOperationException($"SetClipboardData 失败 (0x{Marshal.GetLastWin32Error():X})");
            handle = IntPtr.Zero; // 所有权已转移给系统，不 GlobalFree
        }
        finally
        {
            if (handle != IntPtr.Zero) GlobalFree(handle);
            CloseClipboard();
        }
    }

    public string? GetText()
    {
        if (!SpinUntilClipboardOpen()) return null;
        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return null;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                GlobalSize(handle, out var size);
                var bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, (int)size);
                return System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>OpenClipboard 自旋重试：剪贴板可能被其他进程短暂占用（~200ms 上限）。</summary>
    private static bool SpinUntilClipboardOpen()
    {
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr handle);

    public void Dispose()
    {
        _running = false;
    }
}
