using SRXDesk.Relay.Hubs;
using SRXDesk.Relay.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // İstifadəçi MessageBox-a cavab verənə qədər bağlantı açıq qalmalıdır.
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

app.MapHub<RemoteHub>("/remotehub");

// Sadə sağlamlıq və monitorinq endpoint-ləri.
app.MapGet("/", () => Results.Text("SRXDesk Relay işləyir. Hub: /remotehub", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));
app.MapGet("/api/clients", (ClientRegistry registry) => Results.Ok(registry.GetClients()));

app.Logger.LogInformation("SRXDesk Relay başladı. Hub endpoint: /remotehub");

app.Run();
