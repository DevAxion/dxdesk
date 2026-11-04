using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using DXDesk.Relay.Services;

namespace DXDesk.Relay.Hubs;

/// <summary>
/// Admin aləti (DXDesk.Server) ilə son istifadəçi kompüterləri
/// (DXDesk.Service/Client) arasında relay.
/// </summary>
public sealed class RemoteHub : Hub
{
    private readonly ClientRegistry _registry;
    private readonly RelaySecurity _security;
    private readonly AuditLog _audit;
    private readonly ILogger<RemoteHub> _logger;

    public RemoteHub(ClientRegistry registry, RelaySecurity security, AuditLog audit, ILogger<RemoteHub> logger)
    {
        _registry = registry;
        _security = security;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Admin əmrləri üçün token yoxlaması.</summary>
    private void RequireAdmin(string? token, string action, string? target = null)
    {
        if (!_security.IsAdmin(token))
        {
            _logger.LogWarning("İcazəsiz admin əmri cəhdi: {Action} {ConnectionId}", action, Context.ConnectionId);
            _audit.Write("admin_denied", ResolveRemoteIp(), target, new { action });
            throw new HubException("İcazə yoxdur (yanlış admin token).");
        }
    }

    /// <summary>Kliyent qoşulan kimi öz hostname-i ilə qeydiyyatdan keçir.</summary>
    public async Task RegisterClient(string hostname, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new HubException("Hostname boş ola bilməz.");
        }

        var ip = ResolveRemoteIp();
        if (!_security.IsValidClient(clientSecret))
        {
            _logger.LogWarning("Yanlış client açarı: {Hostname} ({Ip})", hostname, ip);
            _audit.Write("client_denied", ip, hostname.Trim(), new { reason = "secret" });
            throw new HubException("İcazə yoxdur (yanlış client açarı).");
        }

        var client = new ClientInfo(hostname.Trim(), Context.ConnectionId, ip, DateTimeOffset.UtcNow);
        if (!_registry.TryAddOrUpdateClient(client))
        {
            _logger.LogWarning("Hostname artıq başqa IP-dən onlayndır: {Hostname} ({Ip})", client.Hostname, ip);
            _audit.Write("client_denied", ip, client.Hostname, new { reason = "hostname_in_use" });
            throw new HubException("Bu hostname artıq başqa ünvandan qoşulub.");
        }

        _logger.LogInformation("Kliyent qoşuldu: {Hostname} ({Ip})", client.Hostname, client.IpAddress);
        await BroadcastClientsAsync();
    }

    /// <summary>Admin aləti qoşulanda çağırır — onlayn siyahı yeniliklərini almaq üçün.</summary>
    public async Task RegisterAdmin(string token)
    {
        RequireAdmin(token, "register_admin");
        _registry.AddAdmin(Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, ClientRegistry.AdminGroup);
        _logger.LogInformation("Admin qoşuldu: {ConnectionId}", Context.ConnectionId);
        _audit.Write("admin_login", ResolveRemoteIp());

        await Clients.Caller.SendAsync("ClientsChanged", _registry.GetClients());
    }

    /// <summary>Onlayn kliyentlərin siyahısı (yalnız qeydiyyatlı admin üçün).</summary>
    public IReadOnlyList<ClientInfo> GetOnlineClients()
    {
        if (!_registry.IsAdmin(Context.ConnectionId))
        {
            throw new HubException("İcazə yoxdur (əvvəlcə RegisterAdmin).");
        }
        return _registry.GetClients();
    }

    /// <summary>Admin çağırır: göstərilən kompüterə qoşulma sorğusu göndər.</summary>
    public async Task<RequestResult> RequestConnection(string token, string targetHostname)
    {
        RequireAdmin(token, "connect", targetHostname);
        if (string.IsNullOrWhiteSpace(targetHostname))
        {
            return new RequestResult(false, "Hostname boş ola bilməz.", null, null);
        }

        var client = _registry.GetByHostname(targetHostname);
        if (client is null)
        {
            return new RequestResult(false, $"'{targetHostname.Trim()}' onlayn deyil.", null, null);
        }

        // Bir dəfəlik sessiya açarı: agent yalnız bu açarı göndərən viewer-i qəbul edir.
        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _registry.SetPendingRequest(client.Hostname, Context.ConnectionId, sessionToken);
        await Clients.Client(client.ConnectionId).SendAsync("ConnectionRequest", client.Hostname, sessionToken);
        _logger.LogInformation("Qoşulma sorğusu göndərildi: {Hostname}", client.Hostname);
        _audit.Write("connect_request", ResolveRemoteIp(), client.Hostname, new { clientIp = client.IpAddress });

        return new RequestResult(true, null, client.Hostname, client.IpAddress);
    }

    /// <summary>
    /// Admin çağırır: uzaq kompüterdə proqramı SYSTEM kimi işə sal (UAC-siz
    /// quraşdırma/silmə). Yalnız düzgün admin token ilə.
    /// </summary>
    public async Task<RequestResult> LaunchElevated(string token, string targetHostname, string program, string arguments)
    {
        RequireAdmin(token, "launch_elevated", targetHostname);
        if (string.IsNullOrWhiteSpace(targetHostname) || string.IsNullOrWhiteSpace(program))
        {
            return new RequestResult(false, "Hostname və proqram tələb olunur.", null, null);
        }

        var client = _registry.GetByHostname(targetHostname);
        if (client is null)
        {
            return new RequestResult(false, $"'{targetHostname.Trim()}' onlayn deyil.", null, null);
        }

        await Clients.Client(client.ConnectionId).SendAsync("LaunchElevated", program, arguments ?? string.Empty);
        _logger.LogInformation("Elevated launch göndərildi: {Hostname} -> {Program}", client.Hostname, program);
        _audit.Write("launch_elevated", ResolveRemoteIp(), client.Hostname, new { program, arguments });
        return new RequestResult(true, null, client.Hostname, client.IpAddress);
    }

    /// <summary>
    /// Kliyent/agent çağırır: istifadəçi sorğunu qəbul etdi / rədd etdi.
    /// Cavab yalnız sorğudakı sessiya açarı ilə qəbul olunur (saxta cavab olmasın).
    /// </summary>
    public async Task ConnectionResponse(string hostname, bool accepted, int streamPort, string sessionToken)
    {
        var sender = _registry.GetByConnectionId(Context.ConnectionId);
        var effectiveHostname = sender?.Hostname ?? hostname?.Trim() ?? string.Empty;
        var ip = ResolveRemoteIp();

        var pending = string.IsNullOrEmpty(effectiveHostname)
            ? null
            : _registry.TakePendingRequest(effectiveHostname, sessionToken);
        if (pending is null)
        {
            _logger.LogWarning("Etibarsız cavab (açar uyğun deyil və ya vaxtı keçib): {Hostname} ({Ip})", effectiveHostname, ip);
            _audit.Write("response_denied", ip, effectiveHostname);
            throw new HubException("Etibarsız və ya vaxtı keçmiş sorğu.");
        }

        // Agent relay-ə ayrıca (qeydiyyatsız) bağlantı ilə cavab verir — IP-ni reyestrdən götürürük.
        var registered = _registry.GetByHostname(effectiveHostname);
        var message = new ConnectionResponseMessage(
            effectiveHostname,
            accepted,
            registered?.IpAddress ?? ip,
            streamPort,
            accepted ? pending.Token : null);

        _logger.LogInformation("Cavab alındı: {Hostname} -> {Result}", effectiveHostname, accepted ? "QƏBUL" : "RƏDD");
        _audit.Write(accepted ? "connect_accepted" : "connect_rejected", ip, effectiveHostname);

        // Açar yalnız sorğunu göndərən adminə gedir.
        await Clients.Client(pending.AdminConnectionId).SendAsync("ConnectionResponse", message);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var hostname = _registry.RemoveConnection(Context.ConnectionId);
        if (hostname is not null)
        {
            _logger.LogInformation("Kliyent ayrıldı: {Hostname}", hostname);
            await BroadcastClientsAsync();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastClientsAsync() =>
        Clients.Group(ClientRegistry.AdminGroup).SendAsync("ClientsChanged", _registry.GetClients());

    /// <summary>Kliyentin IP ünvanını bağlantıdan götürür (IPv4 formasına gətirilir).</summary>
    private string? ResolveRemoteIp()
    {
        var address = Context.GetHttpContext()?.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6 ? "127.0.0.1" : address.ToString();
        }

        return address.ToString();
    }
}
