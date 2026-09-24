using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DXDesk.Core;

/// <summary>
/// Cari "input desktop"-a (Default və ya Secure/Winlogon) keçidi idarə edir.
/// SYSTEM integrity-li proses hər iki masaüstünə çata bilir.
///
/// VACİB: SetThreadDesktop thread-də GDI obyekti/pəncərə olduqda uğursuz olur
/// (error 170). Ona görə keçidi YALNIZ təzə (heç bir tutma etməmiş) thread-də,
/// ən birinci addım kimi edin. Masaüstü dəyişəndə köhnə capture thread-i
/// dayandırıb təzəsini yaratmaq lazımdır.
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

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, byte[]? pvInfo, uint nLength, out uint lpnLengthNeeded);

    /// <summary>
    /// Çağıran thread-i cari input desktop-a bağlayır və onun adını qaytarır.
    /// Təzə thread-də, hər hansı GDI çağırışından ƏVVƏL çağırılmalıdır.
    /// Uğursuz olsa null qaytarır.
    /// </summary>
    public static string? AttachToInputDesktop()
    {
        var h = OpenInputDesktop(0, true, GENERIC_ALL);
        if (h == IntPtr.Zero)
        {
            return null;
        }

        var name = GetDesktopName(h);
        if (!SetThreadDesktop(h))
        {
            CloseDesktop(h);
            return null;
        }

        // Handle-ı bağlamırıq: thread bu masaüstünə bağlıdır. Thread bitəndə
        // sistem təmizləyir (masaüstü dəyişimi seyrək olduğu üçün problem deyil).
        return name;
    }

    /// <summary>Cari input desktop-un adını qaytarır (keçid etmədən).</summary>
    public static string? CurrentInputDesktopName()
    {
        var h = OpenInputDesktop(0, true, GENERIC_ALL);
        if (h == IntPtr.Zero)
        {
            return null;
        }
        var name = GetDesktopName(h);
        CloseDesktop(h);
        return name;
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
