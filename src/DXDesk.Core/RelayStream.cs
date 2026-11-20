using System.Net.WebSockets;

namespace DXDesk.Core;

/// <summary>
/// Relay üzərindən ekran axını: WebSocket-i adi bayt <see cref="Stream"/> kimi təqdim edir,
/// ona görə kadr protokolu (FrameProtocol) birbaşa TCP ilə eyni qalır.
/// </summary>
public sealed class RelayStream : Stream
{
    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _readBuffer = new byte[64 * 1024];
    private int _readPos;
    private int _readLen;
    private bool _closed;

    private RelayStream(ClientWebSocket socket) => _socket = socket;

    /// <summary>Relay hub ünvanından (http://x:5100/remotehub) axın ünvanını (ws://x:5100/stream) çıxarır.</summary>
    public static Uri StreamUrlFromHub(string hubUrl)
    {
        var b = new UriBuilder(hubUrl);
        b.Scheme = b.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        b.Path = "/stream";
        b.Query = string.Empty;
        return b.Uri;
    }

    /// <summary>Relay-ə qoşulur. <paramref name="role"/>: "agent" və ya "viewer".</summary>
    public static async Task<RelayStream> ConnectAsync(Uri streamUrl, string role, string sessionToken, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, errors) => RelayTls.Validate(cert, errors);
        // Açar URL-də yox, başlıqda gedir (loqlara düşməsin).
        socket.Options.SetRequestHeader("X-Session-Token", sessionToken);
        var url = new UriBuilder(streamUrl) { Query = "role=" + role }.Uri;
        try
        {
            await socket.ConnectAsync(url, ct);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
        return new RelayStream(socket);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_readPos >= _readLen)
        {
            if (_closed) return 0;
            WebSocketReceiveResult r;
            try
            {
                r = await _socket.ReceiveAsync(_readBuffer, ct);
            }
            catch (WebSocketException)
            {
                // Qarşı tərəf bağlantını kəsdi (sessiya bitdi) — axının sonu kimi qəbul edirik.
                _closed = true;
                return 0;
            }
            if (r.MessageType == WebSocketMessageType.Close)
            {
                _closed = true;
                return 0;
            }
            _readPos = 0;
            _readLen = r.Count;
        }

        var n = Math.Min(buffer.Length, _readLen - _readPos);
        _readBuffer.AsMemory(_readPos, n).CopyTo(buffer);
        _readPos += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(buffer, WebSocketMessageType.Binary, true, ct);
        }
        catch (WebSocketException ex)
        {
            throw new IOException("Relay bağlantısı kəsildi.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "end", cts.Token).GetAwaiter().GetResult();
                }
            }
            catch { }
            _socket.Dispose();
            _writeLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
