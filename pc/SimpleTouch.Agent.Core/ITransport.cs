namespace SimpleTouch.Core;

using SimpleTouch.Core.Protocol;

/// <summary>
/// 传输层抽象（方案文档 §4 耦合控制）：RFCOMM 是第一个实现，WiFi(TCP) 后续以新实现接入，
/// 协议编解码与上层零改动。事件回调在接收线程触发，实现方需保证线程安全。
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>收到完整帧（已解码，含未知类型 UnknownMessage）。</summary>
    event Action<ParsedFrame>? FrameReceived;

    /// <summary>连接关闭。ex 为 null 表示对端在帧边界正常关闭。</summary>
    event Action<Exception?>? Disconnected;

    /// <summary>建立连接。</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>发送一帧（type/seq/payload 分离，编解码归协议层）。</summary>
    Task SendFrameAsync(byte type, byte seq, ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>启动接收循环（异步，不阻塞调用方）。</summary>
    Task StartReceivingAsync(CancellationToken ct = default);
}
