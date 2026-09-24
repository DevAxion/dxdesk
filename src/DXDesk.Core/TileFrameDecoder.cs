using System.Buffers.Binary;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;

namespace DXDesk.Core;

/// <summary>
/// <see cref="TileScreenEncoder"/> payload-larını daimi bir kətan (canvas)
/// üzərinə tətbiq edir. Keyframe kətanı yeniləyir, delta yalnız dəyişən
/// xanaları çəkir.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TileFrameDecoder : IDisposable
{
    private Bitmap? _canvas;

    public Size CanvasSize => _canvas is null ? Size.Empty : _canvas.Size;

    /// <summary>Payload-u tətbiq edib cari kətanı qaytarır (null = hələ keyframe yoxdur).</summary>
    public Bitmap? Apply(byte[] payload)
    {
        if (payload.Length < 1) return _canvas;
        var kind = payload[0];
        var pos = 1;

        if (kind == TileScreenEncoder.KindKeyFrame)
        {
            var w = ReadInt(payload, ref pos);
            var h = ReadInt(payload, ref pos);
            var len = ReadInt(payload, ref pos);
            using var ms = new MemoryStream(payload, pos, len);
            using var img = Image.FromStream(ms);

            var canvas = new Bitmap(w, h);
            using (var g = Graphics.FromImage(canvas))
            {
                g.DrawImage(img, 0, 0, w, h);
            }
            _canvas?.Dispose();
            _canvas = canvas;
        }
        else if (kind == TileScreenEncoder.KindDelta && _canvas is not null)
        {
            var count = ReadInt(payload, ref pos);
            using var g = Graphics.FromImage(_canvas);
            for (var i = 0; i < count; i++)
            {
                var x = ReadInt(payload, ref pos);
                var y = ReadInt(payload, ref pos);
                var w = ReadInt(payload, ref pos);
                var h = ReadInt(payload, ref pos);
                var len = ReadInt(payload, ref pos);
                using var ms = new MemoryStream(payload, pos, len);
                using var tile = Image.FromStream(ms);
                g.DrawImage(tile, x, y, w, h);
                pos += len;
            }
        }

        return _canvas;
    }

    private static int ReadInt(byte[] b, ref int pos)
    {
        var v = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos, 4));
        pos += 4;
        return v;
    }

    public void Dispose() => _canvas?.Dispose();
}
