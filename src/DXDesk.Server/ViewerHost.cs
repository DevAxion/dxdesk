using System.Runtime.Versioning;
using DXDesk.Viewer;

namespace DXDesk.Server;

/// <summary>Viewer pəncərəsini ayrıca STA thread-də (proses daxilində) açır.</summary>
[SupportedOSPlatform("windows")]
internal static class ViewerHost
{
    public static void Launch(string host, int port, string? sessionToken)
    {
        var t = new Thread(() =>
        {
            try { ViewerSession.RunRemote(host, port, sessionToken); }
            catch (Exception ex) { Console.WriteLine($"Viewer xətası: {ex.Message}"); }
        })
        {
            IsBackground = true,
            Name = $"Viewer-{host}:{port}",
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }
}
