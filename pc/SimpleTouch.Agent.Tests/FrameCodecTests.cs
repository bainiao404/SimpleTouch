using System.Text.Json;
using SimpleTouch.Core.Protocol;
using Xunit;

namespace SimpleTouch.Agent.Tests;

public sealed record VectorCase(string Name, string Desc, byte[] Bytes);

public static class VectorLoader
{
    /// <summary>从仓库根向上查找 docs/protocol/test-vectors.json（双端同一向量源）。</summary>
    public static string FindVectorsFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "protocol", "test-vectors.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"未找到 docs/protocol/test-vectors.json（自 {AppContext.BaseDirectory} 向上查找）");
    }

    public static TheoryData<VectorCase> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindVectorsFile()));
        var data = new TheoryData<VectorCase>();
        foreach (var el in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            data.Add(new VectorCase(
                el.GetProperty("name").GetString()!,
                el.GetProperty("desc").GetString()!,
                Convert.FromHexString(el.GetProperty("hex").GetString()!)));
        }
        return data;
    }
}

public class FrameCodecTests
{
    public static TheoryData<VectorCase> Vectors => VectorLoader.Load();

    /// <summary>核心断言：解码 → 以相同 seq 重编码 → 与向量逐字节一致（双向证明编解码器符合规范）。</summary>
    [Theory]
    [MemberData(nameof(Vectors))]
    public void 解码与重编码_逐字节一致(VectorCase vector)
    {
        var frame = FrameCodec.Decode(vector.Bytes);
        var reEncoded = FrameCodec.Encode(frame.Message, frame.Seq);
        Assert.Equal(vector.Bytes, reEncoded);
    }

    [Fact]
    public void Hello_字段断言()
    {
        var frame = FrameCodec.Decode(Convert.FromHexString("1800010101000F0000001153696D706C65546F7563682E4167656E74"));
        var hello = Assert.IsType<HelloMessage>(frame.Message);
        Assert.Equal(1, hello.Version);
        Assert.Equal(0x0F, hello.Caps);
        Assert.Equal("SimpleTouch.Agent", hello.Name);
        Assert.Equal(0x01, frame.Seq);
    }

    [Fact]
    public void MouseMove_负数增量()
    {
        var frame = FrameCodec.Decode(Convert.FromHexString("040010040F00E2FF"));
        var mv = Assert.IsType<MouseMoveMessage>(frame.Message);
        Assert.Equal(15, mv.Dx);
        Assert.Equal(-30, mv.Dy);
    }

    [Fact]
    public void Text_中文单帧FIN()
    {
        var frame = FrameCodec.Decode(Convert.FromHexString("0700210801E4BDA0E5A5BD"));
        var text = Assert.IsType<TextMessage>(frame.Message);
        Assert.True(text.Fin);
        Assert.Equal("你好", System.Text.Encoding.UTF8.GetString(text.Data));
    }

    [Fact]
    public void ClipboardSet_中文内容()
    {
        var frame = FrameCodec.Decode(Convert.FromHexString("0A00300C01E589AAE8B4B4E69DBF"));
        var clip = Assert.IsType<ClipboardSetMessage>(frame.Message);
        Assert.True(clip.Fin);
        Assert.Equal("剪贴板", System.Text.Encoding.UTF8.GetString(clip.Data));
    }

    [Fact]
    public void 未知类型_解码为Unknown且可重编码()
    {
        // type=0x7F（规范未定义）payload=2A
        var bytes = Convert.FromHexString("01007F012A");
        var frame = FrameCodec.Decode(bytes);
        var unknown = Assert.IsType<UnknownMessage>(frame.Message);
        Assert.Equal(0x7F, unknown.Type);
        Assert.Equal(bytes, FrameCodec.Encode(unknown, frame.Seq));
    }

    [Fact]
    public void 截断帧_抛InvalidData()
    {
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(Convert.FromHexString("080040")));
    }
}
