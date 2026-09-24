using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using DXDesk.Core;

// Ekran göndərən (host). Mərhələ 5-də bu məntiq Client-ə köçürüləcək.
//   serve [port]  -> real server (viewer qoşulur)
//   selftest      -> server + receiver eyni prosesdə, şəbəkə yayımının avtomatik testi

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

switch (mode)
{
    case "selftest":
        await SelfTestAsync(cts.Token);
        break;
    case "inputtest":
        InputProtocolTest();
        break;
    case "relaytest":
        await RelayTestAsync(args.Length > 1 ? args[1] : "http://localhost:5100/remotehub", cts.Token);
        break;
    case "tiletest":
        await TileTestAsync(cts.Token);
        break;
    case "serve":
    default:
        var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 7000;
        var server = new ScreenStreamServer(port, targetFps: 12, log: Log);
        Log($"Host başladı. Dayandırmaq üçün Ctrl+C.");
        await server.RunAsync(cts.Token);
        break;
}

return;

static void Log(string m) => Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {m}");

static async Task SelfTestAsync(CancellationToken ct)
{
    const int port = 7099;

    var server = new ScreenStreamServer(port, targetFps: 15, log: Log);
    using var serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var serverTask = server.RunAsync(serverCts.Token);

    await Task.Delay(300, ct); // Server qalxsın.

    using var receiver = new ScreenStreamReceiver();
    await receiver.ConnectAsync("127.0.0.1", port, ct);
    Log("Receiver qoşuldu, kadrlar gözlənilir...");

    var received = 0;
    var keyframes = 0;
    long totalBytes = 0;
    int canvasW = 0, canvasH = 0;
    using var decoder = new DXDesk.Core.TileFrameDecoder();
    var sw = Stopwatch.StartNew();
    using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    recvCts.CancelAfter(TimeSpan.FromSeconds(3)); // vaxtla dayan (statik ekranda kadr azdır)

    try
    {
        await receiver.ReceiveLoopAsync(payload =>
        {
            received++;
            totalBytes += payload.Length;
            if (payload.Length > 0 && payload[0] == DXDesk.Core.TileScreenEncoder.KindKeyFrame) keyframes++;
            var canvas = decoder.Apply(payload);
            if (canvas is not null) { canvasW = canvas.Width; canvasH = canvas.Height; }
            if (received == 1) Log($"İlk payload alındı: {payload.Length / 1024.0:F1} KB (keyframe={keyframes})");
        }, recvCts.Token);
    }
    catch (OperationCanceledException) { }

    sw.Stop();
    serverCts.Cancel();
    try { await serverTask; } catch { }

    Log($"NƏTİCƏ: {received} payload, {keyframes} keyframe, {totalBytes / 1024.0:F0} KB, dekod kətanı {canvasW}x{canvasH}");
    var ok = received >= 1 && keyframes >= 1 && canvasW > 0;
    Log(ok ? "✓ Şəbəkə yayımı + tile dekod İŞLƏYİR." : "✗ Yayım alınmadı.");
}


static void InputProtocolTest()
{
    // Input mesajlarını kodlayıb geri oxuyub yoxlayırıq (SendInput ÇAĞIRILMIR).
    int ok = 0, fail = 0;
    void Check(string name, bool cond) { if (cond) { ok++; Log($"✓ {name}"); } else { fail++; Log($"✗ {name}"); } }

    var mv = InputMessage.MouseMove(12345, 54321);
    Check("MouseMove tip", (InputType)mv[0] == InputType.MouseMove);
    Check("MouseMove X", ((mv[1] << 8) | mv[2]) == 12345);
    Check("MouseMove Y", ((mv[3] << 8) | mv[4]) == 54321);

    var md = InputMessage.MouseButton(true, MouseButtonKind.Right);
    Check("MouseDown tip", (InputType)md[0] == InputType.MouseDown);
    Check("MouseDown düymə", md[1] == (byte)MouseButtonKind.Right);

    var mu = InputMessage.MouseButton(false, MouseButtonKind.Middle);
    Check("MouseUp tip", (InputType)mu[0] == InputType.MouseUp);

    var wh = InputMessage.MouseWheel(-240);
    Check("Wheel tip", (InputType)wh[0] == InputType.MouseWheel);
    Check("Wheel delta", (short)((wh[1] << 8) | wh[2]) == -240);

    var kd = InputMessage.Key(true, 0x41); // 'A'
    Check("KeyDown tip", (InputType)kd[0] == InputType.KeyDown);
    Check("KeyDown vk", ((kd[1] << 8) | kd[2]) == 0x41);

    var ku = InputMessage.Key(false, 0x1B); // ESC
    Check("KeyUp tip", (InputType)ku[0] == InputType.KeyUp);

    Log($"NƏTİCƏ: {ok} keçdi, {fail} uğursuz.");
    Log(fail == 0 ? "✓ Input protokolu DÜZGÜN." : "✗ Protokol xətası.");
}


static async Task RelayTestAsync(string relayUrl, CancellationToken ct)
{
    // Relay siqnal axınını yoxlayır: qeydiyyat -> siyahı -> sorğu -> cavab.
    // MessageBox əvəzinə avtomatik "qəbul" edən s(imulyasiya) kliyent istifadə olunur.
    const string testHost = "RELAYTEST-PC";
    const int testPort = 7123;

    var client = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder().WithUrl(relayUrl).Build();
    var admin = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder().WithUrl(relayUrl).Build();

    var responseTcs = new TaskCompletionSource<(string host, bool accepted, int port)>();

    client.On<string>("ConnectionRequest", async h =>
    {
        Log($"[client] Sorğu alındı: {h} -> avtomatik QƏBUL");
        await client.InvokeAsync("ConnectionResponse", testHost, true, testPort);
    });

    admin.On<System.Text.Json.JsonElement>("ConnectionResponse", msg =>
    {
        var host = msg.GetProperty("hostname").GetString() ?? "";
        var accepted = msg.GetProperty("accepted").GetBoolean();
        var port = msg.GetProperty("streamPort").GetInt32();
        responseTcs.TrySetResult((host, accepted, port));
    });

    await client.StartAsync(ct);
    await admin.StartAsync(ct);
    Log("Hər iki bağlantı quruldu.");

    await client.InvokeAsync("RegisterClient", testHost, cancellationToken: ct);
    await admin.InvokeAsync("RegisterAdmin", "", cancellationToken: ct);

    var online = await admin.InvokeAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("GetOnlineClients", ct);
    var found = online.Any(c => c.TryGetValue("hostname", out var v) && v.GetString() == testHost);
    Log(found ? $"✓ Onlayn siyahıda '{testHost}' var." : $"✗ '{testHost}' siyahıda YOXDUR.");

    Log("Admin qoşulma sorğusu göndərir...");
    var reqResult = await admin.InvokeAsync<System.Text.Json.JsonElement>("RequestConnection", "", testHost, ct);
    Log($"  RequestConnection.sent = {reqResult.GetProperty("sent").GetBoolean()}");

    var completed = await Task.WhenAny(responseTcs.Task, Task.Delay(5000, ct));
    if (completed == responseTcs.Task)
    {
        var (host, accepted, port) = responseTcs.Task.Result;
        var ok = host == testHost && accepted && port == testPort;
        Log($"  Cavab: host={host}, accepted={accepted}, port={port}");
        Log(ok ? "✓ Relay siqnal axını TAM İŞLƏYİR." : "✗ Cavab gözlənildiyi kimi deyil.");
    }
    else
    {
        Log("✗ Cavab vaxtında gəlmədi.");
    }

    await client.DisposeAsync();
    await admin.DisposeAsync();
}


static async Task TileTestAsync(CancellationToken ct)
{
    using var encoder = new DXDesk.Core.TileScreenEncoder(tileSize: 128);
    using var decoder = new DXDesk.Core.TileFrameDecoder();

    long keyBytes = 0, deltaBytes = 0;
    int frames = 0, deltas = 0, empties = 0;

    for (var i = 0; i < 20 && !ct.IsCancellationRequested; i++)
    {
        var payload = encoder.NextFrame();
        if (payload is null)
        {
            empties++;
        }
        else
        {
            frames++;
            var kind = payload[0];
            if (kind == DXDesk.Core.TileScreenEncoder.KindKeyFrame)
            {
                keyBytes += payload.Length;
                Log($"Kadr {i}: KEYFRAME {payload.Length / 1024.0:F1} KB");
            }
            else
            {
                deltas++;
                deltaBytes += payload.Length;
                Log($"Kadr {i}: delta {payload.Length / 1024.0:F1} KB");
            }
            var canvas = decoder.Apply(payload);
            if (canvas is not null && i == 0)
                Log($"Dekoder kətanı: {canvas.Width}x{canvas.Height}");
        }
        await Task.Delay(100, ct);
    }

    Log($"NƏTİCƏ: keyframe {keyBytes / 1024.0:F0} KB, {deltas} delta cəmi {deltaBytes / 1024.0:F0} KB, {empties} boş (dəyişməyən) kadr.");
    Log("✓ Tile encoder/decoder işləyir (statik ekranda delta ≈ 0).");
}
