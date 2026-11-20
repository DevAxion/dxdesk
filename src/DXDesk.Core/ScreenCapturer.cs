using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DXDesk.Core;

/// <summary>
/// Əsas (primary) monitorun ekranını tutur.
///
/// İki üsul var:
///   GDI  (BitBlt + CAPTUREBLT) — sadə, hər yerdə işləyir;
///   DXGI (Desktop Duplication) — GDI bəzi Windows 11 drayverlərində QARA kadr
///         qaytaranda avtomatik keçilir.
/// GDI ardıcıl bir neçə tam qara kadr qaytarsa, DXGI sınanır. DXGI xəta verərsə
/// (məs. masaüstü dəyişdi), yenidən yaradılır; alınmasa GDI-yə qayıdılır.
/// </summary>
/// <remarks>
/// Qeyd: <see cref="ImageCodecInfo.GetImageEncoders"/> bəzi System.Drawing.Common
/// versiyalarında marshalling xətası (0xC0000005) verir, ona görə istifadə etmirik;
/// standart JPEG encoder ilə (<see cref="ImageFormat.Jpeg"/>) yazırıq.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ScreenCapturer : IDisposable
{
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    /// <summary>Neçə ardıcıl qara GDI kadrından sonra DXGI-yə keçilsin.</summary>
    private const int BlackFramesBeforeFallback = 3;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    /// <summary>Diaqnostika loqu (agent bunu fayl loquna bağlayır).</summary>
    public static Action<string>? Log { get; set; }

    /// <summary>Ekran tam qara görünəndə çağırılır (məs. ekranı oyatmaq üçün).</summary>
    public static Action? BlackScreenDetected { get; set; }

    private Bitmap? _bitmap;
    private Size _size;
    private DxgiCapturer? _dxgi;
    private bool _useDxgi = string.Equals(
        Environment.GetEnvironmentVariable("DXDESK_CAPTURE"), "dxgi", StringComparison.OrdinalIgnoreCase);
    private bool _dxgiFailed;
    private int _blackFrames;

    /// <summary>Hazırda istifadə olunan üsul ("GDI" və ya "DXGI").</summary>
    public string Method => _useDxgi ? "DXGI" : "GDI";

    /// <summary>Əsas monitorun ölçüsü (piksel).</summary>
    public Size ScreenSize => new(GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    /// <summary>
    /// Ekranı tutub daxili <see cref="Bitmap"/>-ə yazır və onu qaytarır.
    /// Qaytarılan obyekt encoder tərəfindən istifadə üçündür — dispose ETMƏYİN.
    /// </summary>
    public Bitmap CaptureBitmap()
    {
        var size = ScreenSize;
        if (_bitmap is null || _size != size)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            _size = size;
            ResetDxgi();
        }

        if (_useDxgi && TryCaptureDxgi(_bitmap))
        {
            return _bitmap;
        }

        CaptureGdi(_bitmap, size);
        if (IsBlack(_bitmap))
        {
            _blackFrames++;
            if (_blackFrames == BlackFramesBeforeFallback)
            {
                Log?.Invoke($"capture: GDI {BlackFramesBeforeFallback} qara kadr qaytardı ({size.Width}x{size.Height}) — DXGI sınanır, ekran oyadılır");
                BlackScreenDetected?.Invoke();
                if (!_dxgiFailed)
                {
                    _useDxgi = true;
                    TryCaptureDxgi(_bitmap);
                }
            }
        }
        else
        {
            _blackFrames = 0;
        }

        return _bitmap;
    }

    /// <summary>Ekranı tutub JPEG bayt massivi kimi qaytarır.</summary>
    public byte[] CaptureJpeg()
    {
        var bmp = CaptureBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private static void CaptureGdi(Bitmap target, Size size)
    {
        // CopyFromScreen CAPTUREBLT istifadə etmir (layered pəncərələr itir); birbaşa BitBlt edirik.
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            using var g = Graphics.FromImage(target);
            var hdc = g.GetHdc();
            try
            {
                BitBlt(hdc, 0, 0, size.Width, size.Height, screenDc, 0, 0, SRCCOPY | CAPTUREBLT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private bool TryCaptureDxgi(Bitmap target)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (_dxgi is null)
                {
                    _dxgi = new DxgiCapturer();
                    Log?.Invoke($"capture: DXGI hazırdır ({_dxgi.Width}x{_dxgi.Height})");
                }
                _dxgi.CaptureInto(target);
                if (!IsBlack(target))
                {
                    _blackFrames = 0;
                }
                return true;
            }
            catch (Exception ex)
            {
                // Dublikasiya itdi (masaüstü/rejim dəyişdi) — bir dəfə yenidən yarat.
                ResetDxgi();
                if (attempt == 1)
                {
                    Log?.Invoke($"capture: DXGI alınmadı, GDI-yə qayıdılır: {ex.Message}");
                    _dxgiFailed = true;
                    _useDxgi = false;
                }
            }
        }
        return false;
    }

    private void ResetDxgi()
    {
        try { _dxgi?.Dispose(); } catch { }
        _dxgi = null;
    }

    /// <summary>Kadr praktiki olaraq tam qaradırmı (seyrək nümunə ilə yoxlanır).</summary>
    private static bool IsBlack(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                var stepY = Math.Max(1, bmp.Height / 64);
                var stepX = Math.Max(1, bmp.Width / 64);
                for (var y = 0; y < bmp.Height; y += stepY)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < bmp.Width; x += stepX)
                    {
                        var p = row + x * 3;
                        if (p[0] > 8 || p[1] > 8 || p[2] > 8)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        ResetDxgi();
        _bitmap?.Dispose();
    }
}
