using System.Runtime.Versioning;
using SRXDesk.Core;

namespace SRXDesk.Viewer;

/// <summary>
/// Uzaq ekran sessiyasını (pəncərə + qoşulma + idarə) işə salır.
/// Həm Viewer.exe, həm də Server bunu birbaşa çağıra bilir.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ViewerSession
{
    private static bool _initialized;

    /// <summary>Verilmiş host:port-a qoşulub uzaq ekranı göstərir (bloklayır).</summary>
    public static void RunRemote(string host, int port)
    {
        if (!_initialized)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            _initialized = true;
        }

        using var form = new ViewerForm($"SRXDesk Viewer — {host}:{port}  (uzaqdan idarə)")
        {
            EnableInput = true,
        };

        var cts = new CancellationTokenSource();
        ScreenStreamReceiver? active = null;

        form.InputProduced += msg =>
        {
            var r = active;
            if (r is null) return;
            _ = r.SendInputAsync(msg, cts.Token);
        };

        form.Shown += async (_, _) =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var receiver = new ScreenStreamReceiver();
                    await receiver.ConnectAsync(host, port, cts.Token);
                    active = receiver;
                    using var decoder = new TileFrameDecoder();
                    await receiver.ReceiveLoopAsync(payload =>
                    {
                        // Clipboard kadrı (agent -> viewer): lokal clipboard-a yaz.
                        if (payload.Length >= 1 && payload[0] == TileScreenEncoder.KindClipboard)
                        {
                            var text = payload.Length > 1
                                ? System.Text.Encoding.UTF8.GetString(payload, 1, payload.Length - 1)
                                : string.Empty;
                            form.SetClipboard(text);
                            return;
                        }

                        var canvas = decoder.Apply(payload);
                        if (canvas is not null) form.ShowBitmap(canvas, payload.Length);
                    }, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    active = null;
                    form.ShowStatus($"Bağlantı kəsildi: {ex.Message}. 3s sonra təkrar...");
                    try { await Task.Delay(3000, cts.Token); } catch { break; }
                }
            }
        };

        form.FormClosing += (_, _) => cts.Cancel();
        Application.Run(form);
    }
}
