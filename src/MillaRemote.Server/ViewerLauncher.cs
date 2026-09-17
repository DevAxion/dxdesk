using System.Diagnostics;

namespace MillaRemote.Server;

/// <summary>MillaRemote.Viewer-i uzaq ekrana qoşulma rejimində işə salır.</summary>
internal static class ViewerLauncher
{
    /// <summary><c>MillaRemote.Viewer.exe --connect HOST PORT</c> işə salır.</summary>
    public static bool Launch(string host, int port, string? configuredPath)
    {
        var exe = ResolveViewerPath(configuredPath);
        if (exe is null)
        {
            Console.WriteLine("MillaRemote.Viewer.exe tapılmadı. appsettings.json -> Viewer:Path təyin edin.");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("--connect");
            psi.ArgumentList.Add(host);
            psi.ArgumentList.Add(port.ToString());
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Viewer işə salına bilmədi: {ex.Message}");
            return false;
        }
    }

    private static string? ResolveViewerPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // Server ilə yanaşı (yayımda hər ikisi bir qovluqda olur).
        var beside = Path.Combine(AppContext.BaseDirectory, "MillaRemote.Viewer.exe");
        if (File.Exists(beside))
        {
            return beside;
        }

        // Development üçün ehtiyat yol.
        var dev = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "MillaRemote.Viewer", "bin", "Release", "net9.0-windows", "MillaRemote.Viewer.exe"));
        return File.Exists(dev) ? dev : null;
    }
}
