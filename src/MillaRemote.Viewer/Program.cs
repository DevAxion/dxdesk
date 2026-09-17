using System.Runtime.Versioning;
using MillaRemote.Core;
using MillaRemote.Viewer;

[assembly: SupportedOSPlatform("windows")]

// Rejimlər:
//   (arqumentsiz)              -> lokal loopback ön-baxışı (Mərhələ 2)
//   --connect <host> <port>    -> uzaq ekrana qoşul (Mərhələ 3)
ApplicationConfiguration.Initialize();

var connectIdx = Array.FindIndex(args, a => a.Equals("--connect", StringComparison.OrdinalIgnoreCase));
if (connectIdx >= 0 && args.Length > connectIdx + 2)
{
    var host = args[connectIdx + 1];
    var port = int.TryParse(args[connectIdx + 2], out var p) ? p : 7000;
    RunRemote(host, port);
}
else
{
    RunLocalPreview();
}

return;

// --- Mərhələ 3: uzaq ekran ---
static void RunRemote(string host, int port)
{
    var form = new ViewerForm($"MillaRemote Viewer — {host}:{port}  (uzaqdan idarə)");
    form.EnableInput = true;
    var cts = new CancellationTokenSource();
    ScreenStreamReceiver? active = null;

    // Input mesajlarını cari bağlantı üzərindən host-a göndəririk.
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

// --- Mərhələ 2: lokal ön-baxış ---
static void RunLocalPreview()
{
    using var capturer = new ScreenCapturer();
    using var form = new ViewerForm("MillaRemote Viewer — Lokal ön-baxış (Mərhələ 2)");
    using var timer = new System.Windows.Forms.Timer { Interval = 66 };
    timer.Tick += (_, _) =>
    {
        try { form.UpdateFrame(capturer.CaptureJpeg()); } catch { }
    };
    form.Load += (_, _) => timer.Start();
    form.FormClosing += (_, _) => timer.Stop();
    Application.Run(form);
}
