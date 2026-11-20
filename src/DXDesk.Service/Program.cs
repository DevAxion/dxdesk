using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DXDesk.Service;

[assembly: SupportedOSPlatform("windows")]

// DXDesk.Service — LocalSystem Windows xidməti.
// Rejimlər:
//   (arqumentsiz / xidmət kimi)  -> relay-ə qoşulur, əmrləri icra edir
//   console                      -> konsolda debug üçün eyni işi görür
ProcessDllSafety.Apply();
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "DXDesk");
builder.Services.AddHostedService<RelayWorker>();

var host = builder.Build();
host.Run();

// TƏHLÜKƏSİZLİK: prosesin bütün sonrakı DLL yükləmələri yalnız System32-dən olsun.
static class ProcessDllSafety
{
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    public static void Apply()
    {
        try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32); } catch { }
    }
}
