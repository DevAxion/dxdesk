using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DXDesk.Client;

/// <summary>
/// Sessiya boyu ekranın yuxuya getməsinin qarşısını alır və qara ekranda onu oyadır.
/// Ekran sönəndə (monitor yuxusu, qapalı laptop qapağı) tutulan kadr QARA olur.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DisplayWake
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    /// <summary>
    /// Viewer qoşulu qaldıqca ekranı və sistemi oyaq saxlayır. Dispose olunanda
    /// (eyni thread-də) normal vəziyyət qaytarılır.
    /// </summary>
    public static IDisposable KeepOn(Action<string> log)
    {
        var ok = SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED) != 0;
        if (!ok) log("display: SetThreadExecutionState alınmadı");
        Nudge();
        return new Restore();
    }

    /// <summary>Siçanı 1 piksel tərpədib geri qaytarır — sönmüş ekranı oyadır.</summary>
    public static void Nudge()
    {
        try
        {
            mouse_event(MOUSEEVENTF_MOVE, 1, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_MOVE, -1, 0, 0, UIntPtr.Zero);
        }
        catch { }
    }

    private sealed class Restore : IDisposable
    {
        public void Dispose() => SetThreadExecutionState(ES_CONTINUOUS);
    }
}
