using SimpleTouch.Core.Config;
using Xunit;

namespace SimpleTouch.Agent.Tests;

public class AgentSettingsTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"st-settings-{Guid.NewGuid():N}", "settings.json");

    /// <summary>旧版 settings.json（无 deviceName 字段）：加载容忍未知缺失，deviceName 为空串。</summary>
    [Fact]
    public void 旧版配置_缺失deviceName_容忍并返回空()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\n  \"moveScale\": 1.7\n}");

            var settings = AgentSettings.LoadOrCreate(path);
            Assert.Equal(1.7f, settings.MoveScale);
            Assert.Equal("", settings.DeviceName);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    /// <summary>Save→Load 往返：moveScale 与 deviceName 均保留（M4.5 UI 落盘路径）。</summary>
    [Fact]
    public void 保存后加载_字段往返一致()
    {
        var path = TempPath();
        try
        {
            var settings = new AgentSettings { MoveScale = 1.35f, DeviceName = "Xiaomi Pad 5 Pro" };
            settings.Save(path);
            Assert.True(File.Exists(path));

            var loaded = AgentSettings.LoadOrCreate(path);
            Assert.Equal(1.35f, loaded.MoveScale);
            Assert.Equal("Xiaomi Pad 5 Pro", loaded.DeviceName);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    /// <summary>deviceName 含引号/反斜杠时的转义安全。</summary>
    [Fact]
    public void 保存后加载_特殊字符设备名_往返一致()
    {
        var path = TempPath();
        try
        {
            var settings = new AgentSettings { MoveScale = 1f, DeviceName = "DESKTOP-\\\"X\"BT" };
            settings.Save(path);
            var loaded = AgentSettings.LoadOrCreate(path);
            Assert.Equal("DESKTOP-\\\"X\"BT", loaded.DeviceName);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
