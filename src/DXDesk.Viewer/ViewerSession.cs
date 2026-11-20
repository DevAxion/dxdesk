using System.Runtime.Versioning;
using DXDesk.Core;

namespace DXDesk.Viewer;

/// <summary>
/// Uzaq ekran sessiyasını (pəncərə + qoşulma + idarə) işə salır.
/// Həm Viewer.exe, həm də Server bunu birbaşa çağıra bilir.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ViewerSession
{
    private static bool _initialized;

    /// <summary>Verilmiş host:port-a qoşulub uzaq ekranı göstərir (bloklayır).</summary>
    public static void RunRemote(string host, int port, string? sessionToken = null, Uri? relayStreamUrl = null)
    {
        if (!_initialized)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            _initialized = true;
        }

        using var form = new ViewerForm($"DXDesk Viewer — {host}:{port}  (uzaqdan idarə)")
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
                    await ConnectAsync(receiver, host, port, sessionToken, relayStreamUrl, form, cts.Token);
                    active = receiver;
                    form.ShowStatus($"Qoşuldu ({receiver.Via}). İlk kadr gözlənilir...");
                    using var decoder = new TileFrameDecoder();
                    await receiver.ReceiveLoopAsync(payload =>
                    {
                        // Clipboard mətni (agent -> viewer): lokal clipboard-a yaz.
                        if (payload.Length >= 1 && payload[0] == TileScreenEncoder.KindClipboard)
                        {
                            var text = payload.Length > 1
                                ? System.Text.Encoding.UTF8.GetString(payload, 1, payload.Length - 1)
                                : string.Empty;
                            form.SetClipboard(text);
                            return;
                        }

                        // Fayl köçürmə (agent -> viewer).
                        if (payload.Length >= 1 && payload[0] == TileScreenEncoder.KindFile)
                        {
                            form.HandleFileFrame(payload[1..]);
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

    /// <summary>
    /// Əvvəlcə birbaşa TCP (3 s), alınmasa relay üzərindən qoşulur.
    /// Birbaşa yol LAN-da daha sürətlidir; relay NAT/VPN/firewall arxasında işləyir.
    /// </summary>
    private static async Task ConnectAsync(
        ScreenStreamReceiver receiver, string host, int port, string? token, Uri? relay, ViewerForm form, CancellationToken ct)
    {
        form.ShowStatus($"Birbaşa qoşulur: {host}:{port}...");
        try
        {
            using var direct = CancellationTokenSource.CreateLinkedTokenSource(ct);
            direct.CancelAfter(TimeSpan.FromSeconds(3));
            await receiver.ConnectAsync(host, port, direct.Token, token);
            return;
        }
        catch (Exception ex) when (relay is not null && !string.IsNullOrEmpty(token) && !ct.IsCancellationRequested)
        {
            form.ShowStatus($"Birbaşa alınmadı ({ex.Message}). Relay üzərindən qoşulur...");
        }

        await receiver.ConnectRelayAsync(relay!, token!, ct);
    }
}
