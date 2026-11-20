using DXDesk.Relay.Hubs;
using DXDesk.Relay.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSingleton<RelaySecurity>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<StreamBroker>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // İstifadəçi MessageBox-a cavab verənə qədər bağlantı açıq qalmalıdır.
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    // Hub mesajları kiçikdir; böyük paketlərlə yükləməyə imkan verməyək.
    options.MaximumReceiveMessageSize = 64 * 1024;
});

var app = builder.Build();

// Açarları başlanğıcda yoxla: zəif/boş Admin:Secret ilə produksiyada relay başlamır.
var security = app.Services.GetRequiredService<RelaySecurity>();
app.Services.GetRequiredService<AuditLog>();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.MapHub<RemoteHub>("/remotehub");

// Ekran axını relay üzərindən (NAT/VPN arxasındakı kompüterlər üçün).
app.Map("/stream", (HttpContext ctx, StreamBroker broker) => broker.HandleAsync(ctx));

// Sadə sağlamlıq və monitorinq endpoint-ləri.
app.MapGet("/", () => Results.Text("DXDesk Relay işləyir. Hub: /remotehub", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

// Onlayn siyahı yalnız admin açarı ilə (başlıq: X-Admin-Token).
app.MapGet("/api/clients", (HttpRequest request, ClientRegistry registry) =>
    security.IsAdmin(request.Headers["X-Admin-Token"].ToString())
        ? Results.Ok(registry.GetClients())
        : Results.Unauthorized());

app.Logger.LogInformation("DXDesk Relay başladı. Hub endpoint: /remotehub");

app.Run();
