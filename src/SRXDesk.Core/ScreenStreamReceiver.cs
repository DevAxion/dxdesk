using System.Net.Sockets;

namespace SRXDesk.Core;

/// <summary>Ekran serverinə qoşulub JPEG kadrlarını oxuyur.</summary>
public sealed class ScreenStreamReceiver : IDisposable
{
    private readonly TcpClient _client = new();

    private NetworkStream? _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _client.ConnectAsync(host, port, ct);
        _client.NoDelay = true;
        _stream = _client.GetStream();
    }

    /// <summary>
    /// Kadrları oxuyub <paramref name="onFrame"/>-ə ötürür. Axın bağlananadək davam edir.
    /// </summary>
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

    /// <summary>Viewer-dən host-a bir input mesajı göndərir (eyni bağlantı üzərindən).</summary>
    public async Task SendInputAsync(byte[] message, CancellationToken ct = default)
    {
        var stream = _stream;
        if (stream is null) return;
        await _writeLock.WaitAsync(ct);
        try
        {
            await FrameProtocol.WriteFrameAsync(stream, message, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _client.Dispose();
    }
}
