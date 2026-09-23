using System.Net.Sockets;
using System.Threading.Channels;

namespace SRXDesk.Core;

/// <summary>Ekran serverinə qoşulub kadrları oxuyur; input/clipboard/fayl mesajlarını göndərir.</summary>
public sealed class ScreenStreamReceiver : IDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    // Göndərmə FIFO növbəsi — fayl chunk-larının sırası pozulmasın deyə (tək writer).
    private readonly Channel<byte[]> _sendQueue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _sendCts = new();
    private Task? _sendLoop;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _client.ConnectAsync(host, port, ct);
        _client.NoDelay = true;
        _stream = _client.GetStream();
        _sendLoop = Task.Run(() => SendLoopAsync(_sendCts.Token));
    }

    /// <summary>Kadrları oxuyub <paramref name="onFrame"/>-ə ötürür. Axın bağlananadək davam edir.</summary>
    public async Task ReceiveLoopAsync(Action<byte[]> onFrame, CancellationToken ct = default)
    {
        var stream = _stream ??= _client.GetStream();
        while (!ct.IsCancellationRequested)
        {
            var frame = await FrameProtocol.ReadFrameAsync(stream, ct);
            if (frame is null)
            {
                break; // Server bağladı.
            }
            onFrame(frame);
        }
    }

    /// <summary>
    /// Host-a bir mesaj göndərir (input/clipboard/fayl). Növbəyə yazılır və tək
    /// writer tərəfindən sırayla göndərilir (çağırış sırası qorunur).
    /// </summary>
    public Task SendInputAsync(byte[] message, CancellationToken ct = default)
    {
        _sendQueue.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        var stream = _stream;
        if (stream is null) return;
        try
        {
            await foreach (var msg in _sendQueue.Reader.ReadAllAsync(ct))
            {
                await FrameProtocol.WriteFrameAsync(stream, msg, ct);
            }
        }
        catch { /* bağlantı bağlandı / ləğv */ }
    }

    public void Dispose()
    {
        try { _sendCts.Cancel(); } catch { }
        _sendQueue.Writer.TryComplete();
        _client.Dispose();
        _sendCts.Dispose();
    }
}
