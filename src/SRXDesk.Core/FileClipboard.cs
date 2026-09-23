using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SRXDesk.Core;

/// <summary>
/// Fayl clipboard-una (CF_HDROP) Win32 ilə giriş (agent WinForms/STA olmadığı üçün).
/// Get: clipboard-dakı fayl yollarını qaytarır. Set: verilmiş yolları clipboard-a yazır.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FileClipboard
{
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr hDrop, uint i, StringBuilder? file, uint cch);

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES { public uint pFiles; public int ptX; public int ptY; public int fNC; public int fWide; }

    /// <summary>Clipboard-dakı fayl yolları (yoxdursa null).</summary>
    public static string[]? Get()
    {
        if (!TryOpen()) return null;
        try
        {
            var h = GetClipboardData(CF_HDROP);
            if (h == IntPtr.Zero) return null;
            var hDrop = GlobalLock(h);
            if (hDrop == IntPtr.Zero) return null;
            try
            {
                var count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                var list = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    var len = DragQueryFile(hDrop, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFile(hDrop, i, sb, len + 1);
                    list.Add(sb.ToString());
                }
                return list.Count > 0 ? list.ToArray() : null;
            }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Verilmiş fayl yollarını clipboard-a yazır (CF_HDROP).</summary>
    public static bool Set(string[] paths)
    {
        if (paths is null || paths.Length == 0) return false;
        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();
            var sb = new StringBuilder();
            foreach (var p in paths) { sb.Append(p); sb.Append('\0'); }
            sb.Append('\0'); // ikiqat null-terminator
            var listBytes = Encoding.Unicode.GetBytes(sb.ToString());
            var header = Marshal.SizeOf<DROPFILES>();
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(header + listBytes.Length));
            if (hMem == IntPtr.Zero) return false;
            var p2 = GlobalLock(hMem);
            if (p2 == IntPtr.Zero) return false;
            try
            {
                var df = new DROPFILES { pFiles = (uint)header, fWide = 1 };
                Marshal.StructureToPtr(df, p2, false);
                Marshal.Copy(listBytes, 0, p2 + header, listBytes.Length);
            }
            finally { GlobalUnlock(hMem); }
            return SetClipboardData(CF_HDROP, hMem) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }

    private static bool TryOpen()
    {
        for (var i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }
}
