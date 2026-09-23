using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SRXDesk.Core;

/// <summary>
/// Mətn clipboard-una Win32 ilə giriş (agent WinForms/STA olmadığı üçün).
/// Yalnız mətn (CF_UNICODETEXT) dəstəklənir.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    /// <summary>Cari clipboard mətnini qaytarır (yoxdursa null).</summary>
    public static string? Get()
    {
        if (!TryOpen()) return null;
        try
        {
            var h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero) return null;
            var p = GlobalLock(h);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Clipboard-a mətn yazır.</summary>
    public static bool Set(string text)
    {
        text ??= string.Empty;
        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2; // UTF-16 + null
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hMem == IntPtr.Zero) return false;
            var p = GlobalLock(hMem);
            if (p == IntPtr.Zero) return false;
            try { Marshal.Copy((text + '\0').ToCharArray(), 0, p, text.Length + 1); }
            finally { GlobalUnlock(hMem); }
            return SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }

    private static bool TryOpen()
    {
        // Clipboard başqa proses tərəfindən kilidli ola bilər — bir neçə dəfə cəhd.
        for (var i = 0; i < 5; i++)
        {
            if (OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(10);
        }
        return false;
    }
}
