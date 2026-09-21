using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MillaRemote.Core;

/// <summary>
/// Cari "input desktop"-a (Default və ya Secure/Winlogon) keçidi idarə edir.
/// SYSTEM integrity-li proses hər iki masaüstünə çata bilir; bu köməkçi capture/
/// input thread-ini aktiv masaüstünə bağlayır ki, UAC (Secure Desktop) da tutulsun.
///
/// QEYD: SetThreadDesktop yalnız pəncərəsi/hook-u olmayan thread-də işləyir,
/// ona görə bunu YALNIZ xüsusi (dedicated) capture/input thread-lərində çağırın.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DesktopControl
{
    private const uint GENERIC_ALL = 0x10000000;
    private const int UOI_NAME = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, byte[]? pvInfo, uint nLength, out uint lpnLengthNeeded);

    // Hər thread öz cari masaüstü handle-ını və adını saxlayır.
    [ThreadStatic] private static IntPtr _current;
    [ThreadStatic] private static string? _currentName;

    /// <summary>
    /// Çağıran thread-i cari input desktop-a bağlayır (dəyişibsə). Best-effort:
    /// alınmasa səssizcə davam edir (normal masaüstündə qalır).
    /// </summary>
    public static void EnsureOnInputDesktop()
    {
        var h = OpenInputDesktop(0, true, GENERIC_ALL);
        if (h == IntPtr.Zero)
        {
            return;
        }

        var name = GetDesktopName(h);
        if (name is not null && name == _currentName)
        {
            CloseDesktop(h); // Eyni masaüstü — təzə handle-ı bağla.
            return;
        }

        if (SetThreadDesktop(h))
        {
            var old = _current;
            _current = h;
            _currentName = name;
            if (old != IntPtr.Zero)
            {
                CloseDesktop(old);
            }
        }
        else
        {
            CloseDesktop(h);
        }
    }

    private static string? GetDesktopName(IntPtr hDesktop)
    {
        GetUserObjectInformation(hDesktop, UOI_NAME, null, 0, out var needed);
        if (needed == 0)
        {
            return null;
        }
        var buf = new byte[needed];
        if (!GetUserObjectInformation(hDesktop, UOI_NAME, buf, needed, out _))
        {
            return null;
        }
        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }
}
