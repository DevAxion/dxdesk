using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MillaRemote.Client;

// Miqyaslanmış ekranlarda tam çözünürlük üçün ƏN BAŞDA DPI-aware et.
MillaRemote.Client.NativeMethods.EnableDpiAwareness();

// ---- Konfiqurasiya ----
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "MILLA_")
    .Build();

var relayUrl = config["Relay:Url"] ?? "http://localhost:5100/remotehub";
var reconnectDelay = TimeSpan.FromSeconds(
    int.TryParse(config["Relay:ReconnectDelaySeconds"], out var s) && s > 0 ? s : 10);

var streamPort = int.TryParse(config["Stream:Port"], out var sp) && sp > 0 ? sp : 7000;
var streamFps = int.TryParse(config["Stream:TargetFps"], out var sf) && sf > 0 ? sf : 12;
var tileSize = int.TryParse(config["Stream:TileSize"], out var ts) && ts > 0 ? ts : 128;

var hostname = Environment.MachineName;
var session = new SessionStreamer(streamPort, streamFps, tileSize, Log);

// Arxa planda səssiz işləmək üçün konsol pəncərəsini gizlədirik.
NativeMethods.HideConsoleWindow();

// Ctrl+C və ya servis dayanması üçün ləğvetmə tokeni.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var connection = new HubConnectionBuilder()
    .WithUrl(relayUrl)
    .WithAutomaticReconnect(new FixedIntervalRetryPolicy(reconnectDelay))
    .Build();

// Relay-dən qoşulma sorğusu gələndə MessageBox göstəririk.
connection.On<string>("ConnectionRequest", async requestedHostname =>
{
    var accepted = ShowRequestDialog();
    var port = 0;
    if (accepted)
    {
        // İcazə verildi: bu sessiya üçün ekran serverini başladırıq.
        port = session.Begin(noConnectTimeout: TimeSpan.FromSeconds(30));
        Log($"İcazə verildi. Ekran serveri {port} portunda başladı.");
    }
    else
    {
        Log("İstifadəçi rədd etdi.");
    }

    try
    {
        await connection.InvokeAsync("ConnectionResponse", hostname, accepted, port, cts.Token);
    }
    catch (Exception ex)
    {
        Log($"Cavab göndərilə bilmədi: {ex.Message}");
        session.Stop();
    }
});

// Yenidən qoşulduqdan sonra hostname-i təkrar qeydiyyatdan keçiririk.
connection.Reconnected += async _ =>
{
    Log("Yenidən qoşuldu, qeydiyyat təkrarlanır.");
    await SafeRegisterAsync();
};

connection.Closed += async error =>
{
    Log($"Bağlantı kəsildi: {error?.Message ?? "naməlum"}. {reconnectDelay.TotalSeconds}s sonra cəhd olunacaq.");
    // WithAutomaticReconnect tükənəndə əl ilə yenidən qoşuluruq.
    await Task.Delay(reconnectDelay, CancellationToken.None);
    await ConnectLoopAsync();
};

Log($"MillaRemote Client başladı. Hostname: {hostname}, Relay: {relayUrl}");
await ConnectLoopAsync();

// Ləğv olunana qədər gözləyirik.
try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Normal dayanma.
}

session.Stop();
await connection.DisposeAsync();
return;

// ---- Köməkçi funksiyalar ----

bool ShowRequestDialog() => NativeMethods.AskYesNo(
    "IT departamenti sizin kompüterinizə qoşulmaq istəyir. Qəbul edirsiniz?",
    "Uzaqdan Qoşulma Sorğusu");

async Task ConnectLoopAsync()
{
    while (!cts.IsCancellationRequested)
    {
        try
        {
            if (connection.State == HubConnectionState.Connected)
            {
                return;
            }

            await connection.StartAsync(cts.Token);
            await SafeRegisterAsync();
            Log("Relay-ə qoşuldu.");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log($"Qoşulma alınmadı: {ex.Message}. {reconnectDelay.TotalSeconds}s sonra təkrar.");
            try
            {
                await Task.Delay(reconnectDelay, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

async Task SafeRegisterAsync()
{
    try
    {
        await connection.InvokeAsync("RegisterClient", hostname, cts.Token);
    }
    catch (Exception ex)
    {
        Log($"Qeydiyyat alınmadı: {ex.Message}");
    }
}

void Log(string message) =>
    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");

// Sabit intervalla sonsuz yenidən qoşulma siyasəti.
file sealed class FixedIntervalRetryPolicy(TimeSpan delay) : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext) => delay;
}
