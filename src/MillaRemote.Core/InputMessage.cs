using System.Buffers.Binary;

namespace MillaRemote.Core;

public enum InputType : byte
{
    MouseMove = 1,
    MouseDown = 2,
    MouseUp = 3,
    MouseWheel = 4,
    KeyDown = 5,
    KeyUp = 6,
}

public enum MouseButtonKind : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

/// <summary>
/// Viewer -> Host input mesajlarının binar kodlaşdırılması.
/// Koordinatlar 0..65535 normalizə olunmuş formadadır (SendInput ABSOLUTE ilə uyğun).
/// </summary>
public static class InputMessage
{
    public static byte[] MouseMove(ushort nx, ushort ny)
    {
        var b = new byte[5];
        b[0] = (byte)InputType.MouseMove;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(1), nx);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(3), ny);
        return b;
    }

    public static byte[] MouseButton(bool down, MouseButtonKind button) =>
        new[] { (byte)(down ? InputType.MouseDown : InputType.MouseUp), (byte)button };

    public static byte[] MouseWheel(short delta)
    {
        var b = new byte[3];
        b[0] = (byte)InputType.MouseWheel;
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(1), delta);
        return b;
    }

    public static byte[] Key(bool down, ushort virtualKey)
    {
        var b = new byte[3];
        b[0] = (byte)(down ? InputType.KeyDown : InputType.KeyUp);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(1), virtualKey);
        return b;
    }
}
