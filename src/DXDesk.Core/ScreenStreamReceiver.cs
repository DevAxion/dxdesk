using System.Net.Sockets;
using System.Threading.Channels;

namespace DXDesk.Core;

/// <summary>
/// Ekran serverinə (birbaşa TCP və ya relay üzərindən) qoşulub kadrları oxuyur;
/// input/clipboard/fayl mesajlarını göndərir.
/// </summary>
public sealed class ScreenStreamReceiver : IDisposable
{
    private TcpClient? _client;
    private Stream? _stream;

    // Göndərmə FIFO növbəsi — fayl chunk-larının sırası pozulmasın deyə (tək writer).
    private readonly Channel<byte[]> _sendQueue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _sendCts = new();
    private Task? _sendLoop;

    /// <summary>Bağlantının yolu: "birbaşa" və ya "relay".</summary>
    public string Via { get; private set; } = "";

    /// <summary>Birbaşa TCP ilə qoşulur.</summary>
    /// <param name="sessionToken">Relay-in verdiyi bir dəfəlik açar (test rejimində boş ola bilər).</param>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default, string? sessionToken = null)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, ct);
        _client.NoDelay = true;
        await StartAsync(_client.GetStream(), "birbaşa", sessionToken, ct);
    }

    /// <summary>Relay üzərindən (WebSocket) qoşulur — NAT/VPN/firewall arxasındakı kompüterlər üçün.</summary>
    public async Task ConnectRelayAsync(Uri streamUrl, string sessionToken, CancellationToken ct = default)
    {
        var rs = await RelayStream.ConnectAsync(streamUrl, "viewer", sessionToken, ct);
        await StartAsync(rs, "relay", sessionToken, ct);
    }

    private async Task StartAsync(Stream stream, string via, string? sessionToken, CancellationToken ct)
    {
        _stream = stream;
        Via = via;
        if (!string.IsNullOrEmpty(sessionToken))
        {
            await SessionAuth.SendAsync(_stream, sessionToken, ct);
        }
        _sendLoop = Task.Run(() => SendLoopAsync(_sendCts.Token));
    }

    /// <summary>Kadrları oxuyub <paramref name="onFrame"/>-ə ötürür. Axın bağlananadək davam edir.</summary>
    public async Task ReceiveLoopAsync(Action<byte[]> onFrame, CancellationToken ct = default)
    {
        var stream = _stream ?? throw new InvalidOperationException("Əvvəlcə qoşulun.");
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
        try { _stream?.Dispose(); } catch { }
        _client?.Dispose();
        _sendCts.Dispose();
    }
}
