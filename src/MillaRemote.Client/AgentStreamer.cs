using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using MillaRemote.Core;

namespace MillaRemote.Client;

/// <summary>
/// SYSTEM integrity-li agent üçün ekran yayımı + input yeritmə.
///
/// Masaüstü-izləmə: capture üçün hər aktiv masaüstünə (Default ↔ Secure/Winlogon)
/// AYRICA təzə thread yaradılır (GDI vəziyyəti SetThreadDesktop-u bloklamasın deyə).
/// Supervisor masaüstü dəyişimini izləyir və capture thread-ini yenidən yaradır.
/// Beləliklə "Run as administrator" edəndə çıxan UAC pəncərəsi də tutulur və
/// admin parolu uzaqdan yazıla bilir.
///
/// Bir sessiya = bir viewer; viewer ayrılanda qayıdır.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AgentStreamer
{
    public static void Run(int port, int targetFps, int tileSize, TimeSpan noConnectTimeout, Action<string> log)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        log($"Agent {port} portunda gözləyir (SYSTEM, masaüstü-izləmə).");

        try
        {
            if (!WaitForClient(listener, noConnectTimeout, out var client) || client is null)
            {
                log("Viewer vaxtında qoşulmadı — agent dayanır.");
                return;
            }

            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var stop = new CancellationTokenSource();

                var input = new Thread(() => InputLoop(stream, stop, log))
                {
                    IsBackground = true,
                    Name = "agent-input",
                };
                input.Start();

                // Capture supervisor: masaüstü dəyişəndə təzə thread yaradır.
                while (!stop.IsCancellationRequested)
                {
                    var capture = new Thread(() => CaptureOnCurrentDesktop(stream, targetFps, tileSize, stop, log))
                    {
                        IsBackground = true,
                        Name = "agent-capture",
                    };
                    capture.Start();
                    capture.Join();
                    // Thread qayıtdı: ya stop (disconnect/xəta), ya masaüstü dəyişdi.
                    // stop deyilsə, döngə təzə thread ilə yeni masaüstünü tutur.
                    if (!stop.IsCancellationRequested)
                    {
                        Thread.Sleep(50); // təkrar-yaratmanı yüngülcə tənzimlə
                    }
                }

                try { client.Close(); } catch { }
                input.Join(1000);
            }
        }
        finally
        {
            listener.Stop();
            log("Agent sessiyası bitdi.");
        }
    }

    private static bool WaitForClient(TcpListener listener, TimeSpan timeout, out TcpClient? client)
    {
        client = null;
        var ar = listener.BeginAcceptTcpClient(null, null);
        if (!ar.AsyncWaitHandle.WaitOne(timeout))
        {
            return false;
        }
        try { client = listener.EndAcceptTcpClient(ar); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Təzə thread-də cari input desktop-a bağlanıb onu tutur. Masaüstü dəyişəndə
    /// SAKİTCƏ qayıdır (supervisor təzə thread yaradacaq). Disconnect/xətada stop-u
    /// ləğv edir.
    /// </summary>
    private static void CaptureOnCurrentDesktop(
        NetworkStream stream, int targetFps, int tileSize, CancellationTokenSource stop, Action<string> log)
    {
        // Təzə thread-in İLK addımı: aktiv masaüstünə keç (GDI-dən əvvəl).
        var desktop = DesktopControl.AttachToInputDesktop();
        log($"capture: attach='{desktop ?? "(alınmadı)"}'");
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(targetFps, 1, 60));

        try
        {
            using var encoder = new TileScreenEncoder(tileSize);
            while (!stop.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;

                // Masaüstü dəyişibsə, bu thread-i bitir → supervisor təzəsini yaradır.
                var current = DesktopControl.CurrentInputDesktopName();
                if (current != desktop)
                {
                    log($"capture: masaüstü dəyişdi '{desktop}' -> '{current}', yenilənir");
                    return;
                }

                byte[]? payload;
                try
                {
                    payload = encoder.NextFrame();
                }
                catch (Exception ex)
                {
                    // Bir kadrın tutulması alınmadı (məs. keçid anı) — SESSİYANI
                    // ÖLDÜRMÜRÜK; bu thread-i bitirib supervisor-a təzədən başlatdırırıq.
                    log($"capture: kadr xətası (davam) '{desktop}': {ex.Message}");
                    return;
                }

                if (payload is not null)
                {
                    WriteFrame(stream, payload);
                }

                var wait = interval - (DateTime.UtcNow - start);
                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            log("capture: viewer ayrıldı");
            stop.Cancel(); // yalnız şəbəkə xətasında sessiyanı bitiririk
        }
        catch (Exception ex)
        {
            // Digər xətalar — sessiyanı öldürmə, yenilə.
            log($"capture: xəta (davam): {ex.Message}");
        }
    }

    private static void InputLoop(NetworkStream stream, CancellationTokenSource stop, Action<string> log)
    {
        string? attached = DesktopControl.AttachToInputDesktop();
        log($"input: attach='{attached ?? "(alınmadı)"}'");
        try
        {
            var injector = new InputInjector();
            while (!stop.IsCancellationRequested)
            {
                var msg = ReadFrame(stream);
                if (msg is null)
                {
                    break;
                }

                // Masaüstü dəyişibsə, input thread-ini yeni masaüstünə keçir
                // (input thread-də GDI olmadığı üçün təkrar SetThreadDesktop işləyir).
                var cur = DesktopControl.CurrentInputDesktopName();
                if (cur != attached)
                {
                    attached = DesktopControl.AttachToInputDesktop();
                    log($"input: masaüstü dəyişdi -> '{attached ?? "(alınmadı)"}'");
                }

                try { injector.Inject(msg); } catch { /* tək mesaj xətası döngəni dayandırmasın */ }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Viewer ayrıldı.
        }
        catch (Exception ex)
        {
            log($"Input xətası: {ex.Message}");
        }
        finally
        {
            stop.Cancel();
        }
    }

    // --- Sinxron kadr protokolu ([4 bayt BE uzunluq][data]) ---

    private static void WriteFrame(Stream s, byte[] payload)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        s.Write(header, 0, 4);
        s.Write(payload, 0, payload.Length);
        s.Flush();
    }

    private static byte[]? ReadFrame(Stream s)
    {
        var header = new byte[4];
        if (!ReadExact(s, header, 4)) return null;
        var len = BinaryPrimitives.ReadInt32BigEndian(header);
        if (len < 0 || len > FrameProtocol.MaxFrameBytes) throw new InvalidDataException("Yanlış kadr ölçüsü.");
        var buf = new byte[len];
        return ReadExact(s, buf, len) ? buf : null;
    }

    private static bool ReadExact(Stream s, byte[] buf, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = s.Read(buf, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
