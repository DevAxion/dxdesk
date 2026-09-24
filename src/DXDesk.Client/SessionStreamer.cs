using System.Runtime.Versioning;
using DXDesk.Core;

namespace DXDesk.Client;

/// <summary>
/// Bir icazə verilmiş sessiya üçün ekran serverinin ömrünü idarə edir.
/// User "Bəli" deyəndə başlayır; viewer ayrılanda və ya heç kim qoşulmayanda
/// (timeout) avtomatik dayanır — beləliklə icazəsiz qoşulmanın qarşısı alınır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionStreamer
{
    private readonly int _port;
    private readonly int _targetFps;
    private readonly int _tileSize;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cts;
    private bool _hadViewer;
    private TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Sessiya bitəndə (viewer ayrıldı / timeout / Stop) tamamlanır.</summary>
    public Task Completion => _done.Task;

    public SessionStreamer(int port, int targetFps, int tileSize, Action<string> log)
    {
        _port = port;
        _targetFps = targetFps;
        _tileSize = tileSize;
        _log = log;
    }

    public int Port => _port;

    /// <summary>Yeni sessiya başladır (əvvəlkini dayandırır). Dinlədiyi portu qaytarır.</summary>
    public int Begin(TimeSpan noConnectTimeout)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _hadViewer = false;
        _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var server = new ScreenStreamServer(_port, _targetFps, _tileSize, _log);
        server.ViewersChanged += count =>
        {
            if (count > 0)
            {
                _hadViewer = true;
            }
            else if (_hadViewer)
            {
                _log("Viewer ayrıldı — sessiya bağlanır.");
                Stop();
            }
        };

        var ct = cts.Token;
        _ = server.RunAsync(ct);

        // Heç kim qoşulmasa, sessiyanı bağlayırıq.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(noConnectTimeout, ct);
                if (!_hadViewer)
                {
                    _log("Viewer vaxtında qoşulmadı — sessiya dayandırıldı.");
                    Stop();
                }
            }
            catch (OperationCanceledException) { }
        });

        return _port;
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        cts.Dispose();
        _done.TrySetResult();
    }
}
