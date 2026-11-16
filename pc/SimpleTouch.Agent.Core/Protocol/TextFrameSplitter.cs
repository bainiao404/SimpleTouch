using System.Text;

namespace SimpleTouch.Core.Protocol;

/// <summary>
/// TEXT/CLIPBOARD_SET 分帧器（protocol.md §4）：按 UTF-8 <b>码点边界</b>切分，
/// 每帧 data ≤ maxPayload-1（留 flags 1 字节），不跨帧拆码点；末帧带 FIN。
/// </summary>
public static class TextFrameSplitter
{
    public static IReadOnlyList<byte[]> Split(string text, int maxPayload = ProtocolConsts.MaxPayloadSize)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var maxData = maxPayload - 1;
        if (bytes.Length <= maxData) return [bytes];

        var frames = new List<byte[]>();
        var start = 0;
        while (start < bytes.Length)
        {
            var end = Math.Min(start + maxData, bytes.Length);
            // 切点若落在多字节码点的续字节上，回退到码点起始
            while (end > start && end < bytes.Length && (bytes[end] & 0xC0) == 0x80) end--;
            if (end == start) end = Math.Min(start + 4, bytes.Length); // 防御：单码点超限（UTF-8 最长 4B，实际不可达）
            frames.Add(bytes[start..end]);
            start = end;
        }
        return frames;
    }
}
