using System.Buffers.Binary;

namespace DXDesk.Core;

/// <summary>
/// Sadə kadr protokolu: hər kadr [4 bayt uzunluq (big-endian)] + [JPEG baytları].
/// TCP axını üzərində istifadə olunur.
/// </summary>
public static class FrameProtocol
{
    /// <summary>İcazə verilən maksimum kadr ölçüsü (zədələnmiş məlumata qarşı qoruma).</summary>
    public const int MaxFrameBytes = 32 * 1024 * 1024; // 32 MB

    public static async Task WriteFrameAsync(
        Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Bir kadr oxuyur. Axın bağlananda <c>null</c> qaytarır.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Yanlış kadr ölçüsü: {length}");
        }

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct))
        {
            return null;
        }

        return payload;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0)
            {
                return false; // Axın bağlandı.
            }
            read += n;
        }
        return true;
    }
}
