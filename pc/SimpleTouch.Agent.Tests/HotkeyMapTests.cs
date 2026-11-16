using SimpleTouch.Core.Config;
using SimpleTouch.Core.Protocol;
using Xunit;

namespace SimpleTouch.Agent.Tests;

public class HotkeyMapTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"st-hotkeys-{Guid.NewGuid():N}", "hotkeys.json");

    /// <summary>默认映射：文件缺失时自动生成，覆盖 M2 全部 11 个预置。</summary>
    [Fact]
    public void 默认映射_自动生成且覆盖全部M2预设()
    {
        var path = TempPath();
        try
        {
            var map = HotkeyMap.LoadOrCreate(path);
            Assert.Equal(11, map.Count);
            Assert.True(File.Exists(path), "首次加载应自动生成默认 hotkeys.json");

            var winD = map[ProtocolConsts.HotkeyPreset.WinD];
            Assert.Equal((ushort)0x44, winD.Vk);                       // VK_D
            Assert.Equal(ProtocolConsts.KeyMods.Meta, winD.Mods);      // Win 修饰键

            var play = map[ProtocolConsts.HotkeyPreset.PlayPause];
            Assert.Equal(0xB3, play.Vk);                               // VK_MEDIA_PLAY_PAUSE
            Assert.Equal(0, play.Mods);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    /// <summary>用户可编辑：改过的 JSON 加载后生效；未收录 presetId 查不到（兼容规则）。</summary>
    [Fact]
    public void 自定义映射_生效且未知presetId查不到()
    {
        var path = TempPath();
        try
        {
            HotkeyMap.LoadOrCreate(path); // 生成默认
            File.WriteAllText(path, """
                { "version": 1, "presets": {
                    "000B": { "name": "自定义Win+D", "vk": 68, "mods": 8, "holdMs": 50 }
                } }
                """);

            var map = HotkeyMap.LoadOrCreate(path);
            Assert.Single(map);
            Assert.Equal(50, map[ProtocolConsts.HotkeyPreset.WinD].HoldMs);
            Assert.False(map.TryGetValue(0xFFFF, out _), "未收录的 presetId 应查不到（上层忽略）");
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    /// <summary>settings.json：moveScale 读取与默认值。</summary>
    [Fact]
    public void AgentSettings_moveScale_读取与默认()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"st-settings-{Guid.NewGuid():N}");
        try
        {
            var settings = AgentSettings.LoadOrCreate(Path.Combine(dir, "settings.json"));
            Assert.Equal(1.0f, settings.MoveScale);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json")));

            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"moveScale\": 0.45 }");
            var tuned = AgentSettings.LoadOrCreate(Path.Combine(dir, "settings.json"));
            Assert.Equal(0.45f, tuned.MoveScale);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
