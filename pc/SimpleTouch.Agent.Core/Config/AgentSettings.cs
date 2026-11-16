using System.Text.Json;

namespace SimpleTouch.Core.Config;

/// <summary>
/// PC 端自身设置（settings.json，与手机端设置各自独立持有）。
/// MoveScale：MOVE 增量在注入前再乘的系数——用于补偿 Windows 指针加速造成的综合放大（M1 实测 ≈2.2×）。
/// DeviceName：上次成功使用的目标设备名（UI/CLI 启动时的默认值）。
/// </summary>
public sealed class AgentSettings
{
    public float MoveScale { get; set; } = 1.0f;

    public string DeviceName { get; set; } = "";

    public static AgentSettings LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\n  \"moveScale\": 1.0,\n  \"deviceName\": \"\"\n}");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var settings = new AgentSettings
        {
            MoveScale = doc.RootElement.TryGetProperty("moveScale", out var scale)
                ? scale.GetSingle()
                : 1.0f,
            DeviceName = doc.RootElement.TryGetProperty("deviceName", out var device)
                ? device.GetString() ?? ""
                : "",
        };
        return settings;
    }

    /// <summary>落盘（M4.5：UI 改设置后调用；JSON 结构与 LoadOrCreate 对应）。</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n",
            "{",
            $"  \"moveScale\": {MoveScale.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture)},",
            $"  \"deviceName\": \"{DeviceName.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            "}"));
    }
}
