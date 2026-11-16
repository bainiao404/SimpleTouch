namespace SimpleTouch.Core;

/// <summary>
/// 极简日志：控制台 + UTF-8 文件双写。
/// 本机控制台为 GBK 代码页，联调排障以文件为准（执行文档 §2.3）。
/// M4.5：GUI 宿主可订阅 <see cref="Emitted"/> 在窗口内实时展示。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    /// <summary>每条日志触发（格式与文件行一致）；回调内勿做长阻塞，注意跨线程调度。</summary>
    public static event Action<string>? Emitted;

    public static void Init(string path)
    {
        lock (Gate)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        string line;
        lock (Gate)
        {
            line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
            Console.WriteLine(line);
            _writer?.WriteLine(line);
        }
        Emitted?.Invoke(line);
    }
}
