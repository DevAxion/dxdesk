using System.Runtime.InteropServices;

namespace SRXDesk.Client;

/// <summary>Windows MessageBox və konsol pəncərəsi üçün P/Invoke köməkçiləri.</summary>
internal static partial class NativeMethods
{
    // MessageBox üslub bayraqları.
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_TOPMOST = 0x00040000;
    private const uint MB_SETFOREGROUND = 0x00010000;
    private const uint MB_DEFBUTTON2 = 0x00000100; // Standart olaraq "Xeyr" seçili.

    private const int IDYES = 6;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    /// <summary>
    /// Bəli/Xeyr dialoqunu göstərir. Standart seçim "Xeyr"-dir və pəncərə
    /// ən üstdə (topmost) görünür. İstifadəçi "Bəli" seçərsə <c>true</c> qaytarır.
    /// </summary>
    public static bool AskYesNo(string text, string caption)
    {
        var result = MessageBox(
            IntPtr.Zero,
            text,
            caption,
            MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND | MB_DEFBUTTON2);

        return result == IDYES;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDpiAwarenessContext(IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessDPIAware();

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (HANDLE)-4
    private static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>
    /// Prosesi Per-Monitor-V2 DPI-aware edir ki, miqyaslanmış (125%/150%)
    /// ekranlarda tam fiziki çözünürlük tutulsun (yoxsa ekranın yalnız bir
    /// hissəsi görünür). Ən başda, hər hansı tutmadan ƏVVƏL çağırılmalıdır.
    /// </summary>
    public static void EnableDpiAwareness()
    {
        try { if (SetProcessDpiAwarenessContext(PerMonitorV2)) return; } catch { }
        try { SetProcessDPIAware(); } catch { } // köhnə Windows üçün ehtiyat
    }

    /// <summary>Konsol pəncərəsini gizlədir (arxa planda işləmək üçün).</summary>
    public static void HideConsoleWindow()
    {
        var handle = GetConsoleWindow();
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_HIDE);
        }
    }
}
