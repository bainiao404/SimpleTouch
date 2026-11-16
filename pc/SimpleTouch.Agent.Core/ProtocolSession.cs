using System.Collections.Concurrent;
using SimpleTouch.Core.Protocol;

namespace SimpleTouch.Core;

/// <summary>
/// 协议会话：握手（HELLO→READY）、自动 PONG 回显、PING 往返测量、序列号管理。
/// </summary>
public sealed class ProtocolSession : IAsyncDisposable
{
    private readonly ITransport _transport;
    private int _txSeq;
    private TaskCompletionSource<HelloMessage>? _helloTcs;
    private readonly ConcurrentQueue<TaskCompletionSource<PongMessage>> _pongWaiters = new();

    public string? PeerName { get; private set; }
    public int PeerVersion { get; private set; }
    public int PeerCaps { get; private set; }
    public bool HandshakeCompleted { get; private set; }

    public event Action<ProtocolMessage>? MessageReceived;
    public event Action<Exception?>? Disconnected;

    public ProtocolSession(ITransport transport)
    {
        _transport = transport;
        transport.FrameReceived += OnFrame;
        transport.Disconnected += ex => Disconnected?.Invoke(ex);
    }

    private void OnFrame(ParsedFrame frame)
    {
        switch (frame.Message)
        {
            case HelloMessage hello:
                PeerName = hello.Name;
                PeerVersion = hello.Version;
                PeerCaps = hello.Caps;
                _helloTcs?.TrySetResult(hello);
                break;
            case PingMessage ping:
                _ = SendAsync(new PongMessage(ping.Timestamp)); // 协议 §5：自动回显
                break;
            case PongMessage pong:
                while (_pongWaiters.TryDequeue(out var waiter))
                    if (waiter.TrySetResult(pong)) break;
                break;
        }
        MessageReceived?.Invoke(frame.Message);
    }

    public async Task SendAsync(ProtocolMessage message, CancellationToken ct = default)
    {
        byte seq = (byte)(Interlocked.Increment(ref _txSeq) & 0xFF);
        var frame = FrameCodec.Encode(message, seq);
        await _transport.SendFrameAsync(FrameCodec.TypeOf(message), seq, frame.AsMemory(4), ct).ConfigureAwait(false);
    }

    /// <summary>握手：发 HELLO → 等对端 HELLO（5s 超时）→ 校验版本 → PC 侧发 READY。</summary>
    public async Task<bool> HandshakeAsync(string localName, int localCaps, bool sendReady, CancellationToken ct = default)
    {
        _helloTcs = new TaskCompletionSource<HelloMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(new HelloMessage(ProtocolConsts.ProtocolVersion, localCaps, localName), ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var hello = await _helloTcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            if (hello.Version != ProtocolConsts.ProtocolVersion)
            {
                await SendAsync(new ByeMessage(ProtocolConsts.ByeReason.VersionMismatch), CancellationToken.None).ConfigureAwait(false);
                return false;
            }
            if (sendReady)
                await SendAsync(new ReadyMessage(), ct).ConfigureAwait(false);
            HandshakeCompleted = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>发送 PING 并等待下一个 PONG（同会话内单飞假设，M0 足够）。</summary>
    public async Task<PongMessage> PingAsync(long timestamp, CancellationToken ct = default)
    {
        var waiter = new TaskCompletionSource<PongMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pongWaiters.Enqueue(waiter);
        await SendAsync(new PingMessage(timestamp), ct).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await waiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
