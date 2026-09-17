using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace MillaRemote.Core;

/// <summary>
/// Ekranı davamlı tutub qoşulan hər viewer-ə TCP ilə JPEG kadrları göndərir.
/// Bir neçə viewer eyni anda qoşula bilər (hər biri öz döngəsində).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenStreamServer
{
    private readonly int _port;
    private readonly int _targetFps;
    private readonly int _tileSize;
    private readonly Action<string>? _log;
    private int _activeViewers;

    /// <summary>Qoşulu viewer sayı dəyişəndə tetiklenir (sessiya idarəsi üçün).</summary>
    public event Action<int>? ViewersChanged;

    public int ActiveViewers => Volatile.Read(ref _activeViewers);

    public ScreenStreamServer(int port, int targetFps = 12, int tileSize = 128, Action<string>? log = null)
    {
        _port = port;
        _targetFps = Math.Clamp(targetFps, 1, 60);
        _tileSize = tileSize;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        _log?.Invoke($"Ekran serveri {_port} portunda dinləyir (hədəf {_targetFps} FPS).");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleViewerAsync(client, ct); // Fon: hər viewer ayrıca.
            }
        }
        catch (OperationCanceledException)
        {
            // Normal dayanma.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleViewerAsync(TcpClient client, CancellationToken ct)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
        var count = Interlocked.Increment(ref _activeViewers);
        ViewersChanged?.Invoke(count);
        _log?.Invoke($"Viewer qoşuldu: {endpoint} (aktiv: {count})");

        var frameInterval = TimeSpan.FromSeconds(1.0 / _targetFps);
        using var encoder = new TileScreenEncoder(_tileSize);

        try
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();

            // Əks istiqamət: viewer-dən gələn input mesajlarını oxuyub yeridirik.
            var injector = new InputInjector();
            var inputTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var msg = await FrameProtocol.ReadFrameAsync(stream, ct);
                        if (msg is null) break;
                        try { injector.Inject(msg); } catch { /* tək mesaj xətası döngəni dayandırmasın */ }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (SocketException) { }
            }, ct);

            while (!ct.IsCancellationRequested && client.Connected)
            {
                var start = DateTime.UtcNow;

                var payload = encoder.NextFrame();
                if (payload is not null) // Dəyişiklik yoxdursa bant sərf etmirik.
                {
                    await FrameProtocol.WriteFrameAsync(stream, payload, ct);
                }

                var elapsed = DateTime.UtcNow - start;
                var wait = frameInterval - elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }             // Viewer bağladı.
        catch (SocketException) { }
        finally
        {
            client.Dispose();
            var remaining = Interlocked.Decrement(ref _activeViewers);
            ViewersChanged?.Invoke(remaining);
            _log?.Invoke($"Viewer ayrıldı: {endpoint} (aktiv: {remaining})");
        }
    }
}
