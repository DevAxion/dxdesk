using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DXDesk.Service;

[assembly: SupportedOSPlatform("windows")]

// DXDesk.Service — LocalSystem Windows xidməti.
// Rejimlər:
//   (arqumentsiz / xidmət kimi)  -> relay-ə qoşulur, əmrləri icra edir
//   console                      -> konsolda debug üçün eyni işi görür
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "DXDesk");
builder.Services.AddHostedService<RelayWorker>();

var host = builder.Build();
host.Run();
