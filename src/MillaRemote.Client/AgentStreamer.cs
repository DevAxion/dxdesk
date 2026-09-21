using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using MillaRemote.Core;

namespace MillaRemote.Client;

/// <summary>
/// SYSTEM integrity-li agent üçün ekran yayımı + input yeritmə.
/// Xüsusi (dedicated) thread-lərdə işləyir və hər addımda aktiv input desktop-a
/// keçir (Default ↔ Secure/Winlogon), beləliklə "Run as administrator" edəndə
/// çıxan UAC pəncərəsi də tutulur və idarə oluna bilir (admin parolu uzaqdan
/// yazıla bilir). Bir sessiya = bir viewer; viewer ayrılanda qayıdır.
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
                using var stream = client.GetStream();
                var stop = new CancellationTokenSource();

                var capture = new Thread(() => CaptureLoop(stream, targetFps, tileSize, stop, log))
                {
                    IsBackground = true,
                    Name = "agent-capture",
                };
                var input = new Thread(() => InputLoop(stream, stop, log))
                {
                    IsBackground = true,
                    Name = "agent-input",
                };

                capture.Start();
                input.Start();

                capture.Join(); // viewer ayrılanda bitir
                stop.Cancel();
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

    private static void CaptureLoop(
        NetworkStream stream, int targetFps, int tileSize, CancellationTokenSource stop, Action<string> log)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(targetFps, 1, 60));
        try
        {
            using var encoder = new TileScreenEncoder(tileSize);
            while (!stop.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;

                // Aktiv masaüstünə keç (Default ↔ Secure Desktop / UAC).
                DesktopControl.EnsureOnInputDesktop();

                var payload = encoder.NextFrame();
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
            // Viewer ayrıldı — normal.
        }
        catch (Exception ex)
        {
            log($"Capture xətası: {ex.Message}");
        }
        finally
        {
            stop.Cancel();
        }
    }

    private static void InputLoop(NetworkStream stream, CancellationTokenSource stop, Action<string> log)
    {
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
                DesktopControl.EnsureOnInputDesktop();
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

    // --- Sinxron kadr protokolu (FrameProtocol ilə eyni format: [4 bayt BE uzunluq][data]) ---

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
