using System.Text;
using SimpleTouch.Core.Protocol;
using Xunit;

namespace SimpleTouch.Agent.Tests;

/// <summary>M3 分帧器单测：协议要求「data 必须是完整 UTF-8 码点序列，不跨帧拆码点」。</summary>
public class TextFrameSplitterTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>每帧必须能独立按 UTF-8 严格解码（抛异常即说明截断了码点）。</summary>
    private static void AssertFrameIsCompleteCodePoints(byte[] frame)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        strict.GetString(frame); // 拆了码点会抛 DecoderFallbackException
    }

    [Fact]
    public void 短文本_单帧即UTF8字节()
    {
        const string text = "你好 SimpleTouch";
        var frames = TextFrameSplitter.Split(text);
        Assert.Single(frames);
        Assert.Equal(Utf8.GetBytes(text), frames[0]);
    }

    [Fact]
    public void 中文长文_多帧且不跨帧拆码点()
    {
        var text = string.Concat(Enumerable.Repeat("剪贴板同步测试", 10)); // 每汉字 3B，共 210B
        var frames = TextFrameSplitter.Split(text, 16); // maxData=15 → 切点落进汉字内部
        Assert.True(frames.Count > 1);
        foreach (var f in frames)
        {
            Assert.True(f.Length <= 15);
            Assert.True((f[0] & 0xC0) != 0x80, "帧首字节是续字节");
            AssertFrameIsCompleteCodePoints(f);
        }
        Assert.Equal(Utf8.GetBytes(text), frames.SelectMany(f => f).ToArray());
    }

    [Fact]
    public void emoji四字节码点_保留完整()
    {
        var text = string.Concat(Enumerable.Repeat("ab🚀cd🚀ef", 3));
        var frames = TextFrameSplitter.Split(text, 8);
        Assert.True(frames.Count > 1);
        foreach (var f in frames)
        {
            Assert.True((f[0] & 0xC0) != 0x80);
            AssertFrameIsCompleteCodePoints(f);
        }
        Assert.Equal(text, Utf8.GetString(frames.Aggregate((a, b) => a.Concat(b).ToArray())));
    }

    [Fact]
    public void 切点恰在码点前_回退避免截断()
    {
        var frames = TextFrameSplitter.Split("aaa你", 5);
        Assert.Equal(2, frames.Count);
        Assert.Equal(Utf8.GetBytes("aaa"), frames[0]);
        Assert.Equal(Utf8.GetBytes("你"), frames[1]);
    }

    [Fact]
    public void 空文本_单空帧()
    {
        var frames = TextFrameSplitter.Split("");
        Assert.Single(frames);
        Assert.Empty(frames[0]);
    }
}
