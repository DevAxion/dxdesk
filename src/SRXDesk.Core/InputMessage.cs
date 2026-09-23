using System.Buffers.Binary;

namespace SRXDesk.Core;

public enum InputType : byte
{
    MouseMove = 1,
    MouseDown = 2,
    MouseUp = 3,
    MouseWheel = 4,
    KeyDown = 5,
    KeyUp = 6,
    Clipboard = 7,
    File = 8, // viewer -> agent fayl köçürmə (sub-payload FileTransfer formatında)
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

    /// <summary>Viewer -> Host clipboard mətni: [7][utf8].</summary>
    public static byte[] Clipboard(string text)
    {
        var t = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
        var b = new byte[1 + t.Length];
        b[0] = (byte)InputType.Clipboard;
        Array.Copy(t, 0, b, 1, t.Length);
        return b;
    }

    /// <summary>Clipboard mesajından mətni çıxarır.</summary>
    public static string ReadClipboardText(byte[] msg) =>
        msg.Length <= 1 ? string.Empty : System.Text.Encoding.UTF8.GetString(msg, 1, msg.Length - 1);
}
