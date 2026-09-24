using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DXDesk.Core;

/// <summary>
/// Əsas (primary) monitorun ekranını GDI ilə tutub JPEG bayt massivinə çevirir.
/// Sadə, hər yerdə işləyən ilkin variant; Mərhələ 6-da Desktop Duplication API
/// və WIC ilə sürət/keyfiyyət artırıla bilər.
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private Bitmap? _bitmap;
    private Size _size;

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
        }
        using (var g = Graphics.FromImage(_bitmap))
        {
            g.CopyFromScreen(0, 0, 0, 0, size, CopyPixelOperation.SourceCopy);
        }
        return _bitmap;
    }

    /// <summary>Ekranı tutub JPEG bayt massivi kimi qaytarır.</summary>
    public byte[] CaptureJpeg()
    {
        var size = ScreenSize;

        // Ölçü dəyişməyibsə eyni bitmap-i təkrar istifadə edirik (yaddaş üçün).
        if (_bitmap is null || _size != size)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            _size = size;
        }

        using (var g = Graphics.FromImage(_bitmap))
        {
            g.CopyFromScreen(0, 0, 0, 0, size, CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream();
        _bitmap.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    public void Dispose() => _bitmap?.Dispose();
}
