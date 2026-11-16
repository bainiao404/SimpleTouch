using System.Globalization;
using System.Text.Json;
using SimpleTouch.Core.Protocol;

namespace SimpleTouch.Core.Config;

/// <summary>hotkeys.json 单条预设：presetId → 注入键序（按下 → holdMs → 抬起）。</summary>
public sealed record HotkeyPresetDef(string Name, ushort Vk, byte Mods, int HoldMs);

/// <summary>
/// PC 端 HOTKEY 映射表（docs/protocol.md §4 HOTKEY：映射表存于 PC 端 JSON 配置，用户可编辑）。
/// 文件缺失时自动生成覆盖全部 M2 预设的默认文件；未收录的 presetId 查不到 → 上层忽略并记日志（兼容规则）。
/// </summary>
public static class HotkeyMap
{
    public static Dictionary<ushort, HotkeyPresetDef> LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildDefaultJson());
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<ushort, HotkeyPresetDef>();
        foreach (var prop in doc.RootElement.GetProperty("presets").EnumerateObject())
        {
            var id = ushort.Parse(prop.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            map[id] = new HotkeyPresetDef(
                prop.Value.GetProperty("name").GetString() ?? "?",
                (ushort)prop.Value.GetProperty("vk").GetInt32(),
                (byte)(prop.Value.TryGetProperty("mods", out var mods) ? mods.GetInt32() : 0),
                prop.Value.TryGetProperty("holdMs", out var hold) ? hold.GetInt32() : 30);
        }
        return map;
    }

    /// <summary>默认映射：M2 预设全集（media VK + 常规 VK），Win+D = Meta(0x08)+D。</summary>
    public static string BuildDefaultJson()
    {
        var presets = new (ushort Id, string Name, int Vk, int Mods)[]
        {
            (ProtocolConsts.HotkeyPreset.VolumeUp, "音量+", 0xAF, 0),
            (ProtocolConsts.HotkeyPreset.VolumeDown, "音量−", 0xB0, 0),
            (ProtocolConsts.HotkeyPreset.VolumeMute, "静音", 0xAD, 0),
            (ProtocolConsts.HotkeyPreset.PlayPause, "播放/暂停", 0xB3, 0),
            (ProtocolConsts.HotkeyPreset.MediaPrev, "上一曲", 0xB1, 0),
            (ProtocolConsts.HotkeyPreset.MediaNext, "下一曲", 0xB2, 0),
            (ProtocolConsts.HotkeyPreset.PageUp, "PgUp", 0x21, 0),
            (ProtocolConsts.HotkeyPreset.PageDown, "PgDn", 0x22, 0),
            (ProtocolConsts.HotkeyPreset.F5, "F5", 0x74, 0),
            (ProtocolConsts.HotkeyPreset.Esc, "Esc", 0x1B, 0),
            (ProtocolConsts.HotkeyPreset.WinD, "Win+D", 0x44, ProtocolConsts.KeyMods.Meta),
        };
        var obj = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["presets"] = presets.ToDictionary(
                p => p.Id.ToString("X4", CultureInfo.InvariantCulture),
                p => (object)new Dictionary<string, object>
                {
                    ["name"] = p.Name,
                    ["vk"] = p.Vk,
                    ["mods"] = p.Mods,
                    ["holdMs"] = 30,
                }),
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }
}
