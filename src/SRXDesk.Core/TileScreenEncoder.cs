using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SRXDesk.Core;

/// <summary>
/// Ekranı xanalara (tile) bölüb yalnız dəyişən xanaları göndərir.
/// Hər bağlantı üçün ayrıca nüsxə (əvvəlki kadrı yadda saxlayır).
///
/// Mesaj formatı (FrameProtocol payload-u):
///   kind=0 KEYFRAME: [0][int32 w][int32 h][int32 jpegLen][jpeg]
///   kind=1 DELTA:    [1][int32 count]( [int32 x][int32 y][int32 w][int32 h][int32 len][jpeg] )*
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TileScreenEncoder : IDisposable
{
    public const byte KindKeyFrame = 0;
    public const byte KindDelta = 1;

    private readonly int _tile;
    private readonly ScreenCapturer _capturer = new();

    private ulong[]? _hashes;
    private int _cols, _rows;
    private Size _size;

    public TileScreenEncoder(int tileSize = 128) => _tile = Math.Clamp(tileSize, 32, 512);

    /// <summary>
    /// Növbəti kadrı kodlayır. Dəyişiklik yoxdursa <c>null</c> qaytarır (göndərməyə ehtiyac yoxdur).
    /// </summary>
    public byte[]? NextFrame()
    {
        var bmp = _capturer.CaptureBitmap();
        var size = new Size(bmp.Width, bmp.Height);

        // İlk kadr və ya ölçü dəyişdi -> KEYFRAME.
        if (_hashes is null || _size != size)
        {
            _size = size;
            _cols = (size.Width + _tile - 1) / _tile;
            _rows = (size.Height + _tile - 1) / _tile;
            _hashes = new ulong[_cols * _rows];

            var pixels = CopyPixels(bmp, out var stride);
            for (var r = 0; r < _rows; r++)
            for (var c = 0; c < _cols; c++)
                _hashes[r * _cols + c] = HashTile(pixels, stride, c, r);

            return BuildKeyFrame(bmp);
        }

        // DELTA: dəyişən xanaları tapırıq.
        var buf = CopyPixels(bmp, out var st);
        var changed = new List<(int c, int r)>();
        for (var r = 0; r < _rows; r++)
        for (var c = 0; c < _cols; c++)
        {
            var h = HashTile(buf, st, c, r);
            if (h != _hashes[r * _cols + c])
            {
                _hashes[r * _cols + c] = h;
                changed.Add((c, r));
            }
        }

        if (changed.Count == 0)
        {
            return null; // Ekran sabitdir — heç nə göndərmirik.
        }

        return BuildDelta(bmp, changed);
    }

    private byte[] BuildKeyFrame(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(KindKeyFrame);
        WriteInt(ms, bmp.Width);
        WriteInt(ms, bmp.Height);
        var jpeg = ToJpeg(bmp);
        WriteInt(ms, jpeg.Length);
        ms.Write(jpeg);
        return ms.ToArray();
    }

    private byte[] BuildDelta(Bitmap bmp, List<(int c, int r)> changed)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(KindDelta);
        WriteInt(ms, changed.Count);
        foreach (var (c, r) in changed)
        {
            var x = c * _tile;
            var y = r * _tile;
            var w = Math.Min(_tile, bmp.Width - x);
            var h = Math.Min(_tile, bmp.Height - y);
            using var tile = bmp.Clone(new Rectangle(x, y, w, h), PixelFormat.Format24bppRgb);
            var jpeg = ToJpeg(tile);
            WriteInt(ms, x);
            WriteInt(ms, y);
            WriteInt(ms, w);
            WriteInt(ms, h);
            WriteInt(ms, jpeg.Length);
            ms.Write(jpeg);
        }
        return ms.ToArray();
    }

    private static byte[] ToJpeg(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private static byte[] CopyPixels(Bitmap bmp, out int stride)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            stride = data.Stride;
            var bytes = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private ulong HashTile(byte[] pixels, int stride, int c, int r)
    {
        var x0 = c * _tile;
        var y0 = r * _tile;
        var w = Math.Min(_tile, _size.Width - x0);
        var h = Math.Min(_tile, _size.Height - y0);

        const ulong fnvOffset = 1469598103934665603UL;
        const ulong fnvPrime = 1099511628211UL;
        var hash = fnvOffset;

        for (var y = 0; y < h; y++)
        {
            var rowStart = (y0 + y) * stride + x0 * 3;
            var end = rowStart + w * 3;
            for (var i = rowStart; i < end; i++)
            {
                hash ^= pixels[i];
                hash *= fnvPrime;
            }
        }
        return hash;
    }

    private static void WriteInt(Stream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(b, value);
        s.Write(b);
    }

    public void Dispose() => _capturer.Dispose();
}
