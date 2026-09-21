using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using MillaRemote.Server;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "MILLA_")
    .Build();

var relayUrl = config["Relay:Url"] ?? "http://localhost:5100/remotehub";
var fallbackPort = int.TryParse(config["Stream:Port"], out var fp) && fp > 0 ? fp : 7000;
var adminSecret = config["Admin:Secret"] ?? "";

var connection = new HubConnectionBuilder()
    .WithUrl(relayUrl)
    .WithAutomaticReconnect()
    .Build();

// Relay-dən onlayn siyahı dəyişikliyi gələndə lokal keşi yeniləyirik.
var clients = new List<ClientInfo>();
var clientsLock = new object();

connection.On<List<ClientInfo>>("ClientsChanged", updated =>
{
    lock (clientsLock)
    {
        clients.Clear();
        clients.AddRange(updated);
    }
});

// Kliyent Qəbul/Rədd cavabı gələndə uyğun addımı atırıq.
connection.On<ConnectionResponseMessage>("ConnectionResponse", response =>
{
    Console.WriteLine();
    if (response.Accepted)
    {
        var target = !string.IsNullOrWhiteSpace(response.IpAddress) ? response.IpAddress! : response.Hostname;
        var port = response.StreamPort > 0 ? response.StreamPort : fallbackPort;
        Console.WriteLine($"  ✓ {response.Hostname} qəbul etdi. Ekran açılır ({target}:{port})...");
        ViewerHost.Launch(target, port);
    }
    else
    {
        Console.WriteLine($"  ✗ İstifadəçi rədd etdi: {response.Hostname}");
    }
    Console.WriteLine();
    Console.Write("Seçim edin: ");
});

Console.WriteLine("MillaRemote Server — admin aləti");
Console.WriteLine($"Relay: {relayUrl}");
Console.WriteLine("Relay-ə qoşulur...");

try
{
    await connection.StartAsync();
    await connection.InvokeAsync("RegisterAdmin", adminSecret);
    Console.WriteLine("Qoşuldu.\n");
}
catch (Exception ex)
{
    Console.WriteLine($"Relay-ə qoşulmaq alınmadı: {ex.Message}");
    Console.WriteLine("Relay serverin işlədiyindən əmin olun. Çıxmaq üçün Enter basın.");
    Console.ReadLine();
    return;
}

var running = true;
while (running)
{
    ShowMenu();
    Console.Write("Seçim edin: ");
    var choice = Console.ReadLine()?.Trim();

    switch (choice)
    {
        case "1":
            await ShowOnlineClientsAsync();
            break;
        case "2":
            await RequestConnectionAsync();
            break;
        case "4":
            await LaunchElevatedAsync();
            break;
        case "3":
            running = false;
            break;
        default:
            Console.WriteLine("Yanlış seçim.\n");
            break;
    }
}

await connection.DisposeAsync();
return;

// ---- Menyu funksiyaları ----

void ShowMenu()
{
    Console.WriteLine("========================================");
    Console.WriteLine("  [1] Online olan kompüterlərin siyahısı");
    Console.WriteLine("  [2] Kompüterə qoşul (hostname daxil et)");
    Console.WriteLine("  [4] Uzaq PC-də SYSTEM proqram aç (UAC-siz quraşdırma)");
    Console.WriteLine("  [3] Çıx");
    Console.WriteLine("========================================");
}

async Task ShowOnlineClientsAsync()
{
    // Relay-dən ən son siyahını çəkirik.
    try
    {
        var latest = await connection.InvokeAsync<List<ClientInfo>>("GetOnlineClients");
        lock (clientsLock)
        {
            clients.Clear();
            clients.AddRange(latest);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Siyahı alına bilmədi: {ex.Message}\n");
        return;
    }

    List<ClientInfo> snapshot;
    lock (clientsLock)
    {
        snapshot = clients.ToList();
    }

    Console.WriteLine();
    if (snapshot.Count == 0)
    {
        Console.WriteLine("  Heç bir kompüter onlayn deyil.\n");
        return;
    }

    Console.WriteLine($"  Onlayn kompüterlər ({snapshot.Count}):");
    Console.WriteLine("  ----------------------------------------");
    var i = 1;
    foreach (var c in snapshot)
    {
        Console.WriteLine($"  {i,2}. {c.Hostname,-20} {c.IpAddress ?? "-",-15}  (qoşuldu: {c.ConnectedAtUtc.ToLocalTime():HH:mm:ss})");
        i++;
    }
    Console.WriteLine();
}

async Task RequestConnectionAsync()
{
    Console.Write("Hostname daxil edin: ");
    var target = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.WriteLine("Hostname boş ola bilməz.\n");
        return;
    }

    try
    {
        var result = await connection.InvokeAsync<RequestResult>("RequestConnection", adminSecret, target);
        if (result.Sent)
        {
            Console.WriteLine($"  Sorğu göndərildi: {result.Hostname}. İstifadəçinin cavabı gözlənilir...\n");
        }
        else
        {
            Console.WriteLine($"  Sorğu göndərilmədi: {result.Error}\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Xəta: {ex.Message}\n");
    }
}

async Task LaunchElevatedAsync()
{
    Console.Write("Hostname daxil edin: ");
    var target = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.WriteLine("Hostname boş ola bilməz.\n");
        return;
    }

    Console.WriteLine("  Nümunələr: cmd.exe  |  powershell.exe  |  msiexec.exe (arqument: /i C:\\setup.msi /qn)");
    Console.Write("Proqram (məs. cmd.exe): ");
    var program = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(program))
    {
        program = "cmd.exe";
    }

    Console.Write("Arqumentlər (boş ola bilər): ");
    var arguments = Console.ReadLine()?.Trim() ?? "";

    Console.WriteLine($"  DİQQƏT: '{program}' uzaq PC-də SYSTEM səlahiyyəti ilə açılacaq.");
    Console.Write("  Təsdiq edirsiniz? (b/x): ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "b", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("  Ləğv edildi.\n");
        return;
    }

    try
    {
        var result = await connection.InvokeAsync<RequestResult>("LaunchElevated", adminSecret, target, program, arguments);
        Console.WriteLine(result.Sent
            ? $"  Göndərildi: {result.Hostname} üzərində '{program}' SYSTEM kimi açılır.\n"
            : $"  Göndərilmədi: {result.Error}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Xəta: {ex.Message}\n");
    }
}
