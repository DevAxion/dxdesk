using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace DXDesk.Relay.Services;

/// <summary>
/// Ekran axınını relay üzərindən ötürür (NAT/VPN/firewall arxasındakı kompüterlər üçün).
///
/// Agent və viewer hər ikisi relay-ə ÖZÜ qoşulur (<c>/stream</c>, WebSocket) və
/// <c>X-Session-Token</c> başlığında eyni bir dəfəlik sessiya açarını göndərir.
/// Relay onları cütləşdirib baytları iki istiqamətdə olduğu kimi ötürür.
/// Məzmun relay üçün şəffafdır: viewer-in açar yoxlaması (SessionAuth) agentlə
/// arasında birbaşa olduğu kimi qalır.
/// </summary>
public sealed class StreamBroker
{
    private static readonly TimeSpan AgentWaitForViewer = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ViewerWaitForAgent = TimeSpan.FromSeconds(20);
    private const int BufferSize = 64 * 1024;

    private readonly ClientRegistry _registry;
    private readonly AuditLog _audit;
    private readonly ILogger<StreamBroker> _logger;
    private readonly ConcurrentDictionary<string, Pair> _pairs = new(StringComparer.Ordinal);

    public StreamBroker(ClientRegistry registry, AuditLog audit, ILogger<StreamBroker> logger)
    {
        _registry = registry;
        _audit = audit;
        _logger = logger;
    }

    private sealed class Pair
    {
        public readonly TaskCompletionSource<WebSocket> Agent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<WebSocket> Viewer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var role = context.Request.Query["role"].ToString();
        var token = context.Request.Headers["X-Session-Token"].ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var host = _registry.GetStreamTokenHost(token);

        if ((role != "agent" && role != "viewer") || host is null)
        {
            _logger.LogWarning("Etibarsız axın sorğusu: role={Role} ({Ip})", role, ip);
            _audit.Write("stream_denied", ip, null, new { role });
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var pair = _pairs.GetOrAdd(token, _ => new Pair());
        var mine = role == "agent" ? pair.Agent : pair.Viewer;
        var other = role == "agent" ? pair.Viewer : pair.Agent;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        if (!mine.TrySetResult(socket))
        {
            // Eyni rol bu açarla artıq qoşulub — ikinci nüsxəyə icazə yoxdur.
            await CloseQuietly(socket, WebSocketCloseStatus.PolicyViolation, "duplicate");
            return;
        }

        var aborted = context.RequestAborted;
        try
        {
            if (role == "agent")
            {
                // Agent viewer-i gözləyir, sonra ötürməni özü idarə edir.
                var viewer = await other.Task.WaitAsync(AgentWaitForViewer, aborted);
                _registry.ConsumeStreamToken(token);
                _logger.LogInformation("Relay axını başladı: {Host}", host);
                _audit.Write("stream_relay_start", ip, host);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(aborted);
                var a = PumpAsync(socket, viewer, cts.Token);
                var b = PumpAsync(viewer, socket, cts.Token);
                await Task.WhenAny(a, b);
                cts.Cancel();
                await CloseQuietly(socket, WebSocketCloseStatus.NormalClosure, "end");
                await CloseQuietly(viewer, WebSocketCloseStatus.NormalClosure, "end");
                _logger.LogInformation("Relay axını bitdi: {Host}", host);
                _audit.Write("stream_relay_end", ip, host);
            }
            else
            {
                // Viewer agentin gəlməsini, sonra ötürmənin bitməsini gözləyir.
                await other.Task.WaitAsync(ViewerWaitForAgent, aborted);
                await pair.Done.Task.WaitAsync(aborted);
            }
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Relay axını: qarşı tərəf vaxtında qoşulmadı ({Role}, {Host})", role, host);
            await CloseQuietly(socket, WebSocketCloseStatus.EndpointUnavailable, "timeout");
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (role == "agent")
            {
                pair.Done.TrySetResult();
                _pairs.TryRemove(token, out _);
            }
            else if (!pair.Agent.Task.IsCompleted)
            {
                _pairs.TryRemove(token, out _);
            }
        }
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var r = await from.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                await to.SendAsync(buffer.AsMemory(0, r.Count), r.MessageType, r.EndOfMessage, ct);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            // bir tərəf ayrıldı
        }
    }

    private static async Task CloseQuietly(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(status, reason, cts.Token);
            }
        }
        catch { }
    }
}
