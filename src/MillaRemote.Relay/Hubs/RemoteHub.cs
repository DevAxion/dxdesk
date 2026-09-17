using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using MillaRemote.Relay.Services;

namespace MillaRemote.Relay.Hubs;

/// <summary>
/// Admin aləti (MillaRemote.Server) ilə son istifadəçi kompüterləri
/// (MillaRemote.Client) arasında relay.
/// </summary>
public sealed class RemoteHub : Hub
{
    private readonly ClientRegistry _registry;
    private readonly ILogger<RemoteHub> _logger;

    public RemoteHub(ClientRegistry registry, ILogger<RemoteHub> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>Kliyent qoşulan kimi öz hostname-i ilə qeydiyyatdan keçir.</summary>
    public async Task RegisterClient(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            throw new HubException("Hostname boş ola bilməz.");
        }

        var client = new ClientInfo(
            hostname.Trim(),
            Context.ConnectionId,
            ResolveRemoteIp(),
            DateTimeOffset.UtcNow);

        _registry.AddOrUpdateClient(client);
        _logger.LogInformation("Kliyent qoşuldu: {Hostname} ({Ip})", client.Hostname, client.IpAddress);

        await BroadcastClientsAsync();
    }

    /// <summary>Admin aləti qoşulanda çağırır — onlayn siyahı yeniliklərini almaq üçün.</summary>
    public async Task RegisterAdmin()
    {
        _registry.AddAdmin(Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, ClientRegistry.AdminGroup);
        _logger.LogInformation("Admin qoşuldu: {ConnectionId}", Context.ConnectionId);

        await Clients.Caller.SendAsync("ClientsChanged", _registry.GetClients());
    }

    /// <summary>Onlayn kliyentlərin siyahısı.</summary>
    public IReadOnlyList<ClientInfo> GetOnlineClients() => _registry.GetClients();

    /// <summary>Admin çağırır: göstərilən kompüterə qoşulma sorğusu göndər.</summary>
    public async Task<RequestResult> RequestConnection(string targetHostname)
    {
        if (string.IsNullOrWhiteSpace(targetHostname))
        {
            return new RequestResult(false, "Hostname boş ola bilməz.", null, null);
        }

        var client = _registry.GetByHostname(targetHostname);
        if (client is null)
        {
            return new RequestResult(false, $"'{targetHostname.Trim()}' onlayn deyil.", null, null);
        }

        _registry.SetPendingRequest(client.Hostname, Context.ConnectionId);
        await Clients.Client(client.ConnectionId).SendAsync("ConnectionRequest", client.Hostname);
        _logger.LogInformation("Qoşulma sorğusu göndərildi: {Hostname}", client.Hostname);

        return new RequestResult(true, null, client.Hostname, client.IpAddress);
    }

    /// <summary>Kliyent çağırır: istifadəçi sorğunu qəbul etdi / rədd etdi.</summary>
    public async Task ConnectionResponse(string hostname, bool accepted, int streamPort)
    {
        // Saxtakarlığın qarşısını almaq üçün qeydiyyatdakı adı üstün tuturuq.
        var client = _registry.GetByConnectionId(Context.ConnectionId);
        var effectiveHostname = client?.Hostname ?? hostname?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(effectiveHostname))
        {
            throw new HubException("Qeydiyyatdan keçməmiş kliyent cavab göndərə bilməz.");
        }

        var message = new ConnectionResponseMessage(effectiveHostname, accepted, client?.IpAddress, streamPort);
        _logger.LogInformation(
            "Cavab alındı: {Hostname} -> {Result}",
            effectiveHostname,
            accepted ? "QƏBUL" : "RƏDD");

        var adminConnectionId = _registry.TakePendingRequest(effectiveHostname);
        if (adminConnectionId is not null)
        {
            await Clients.Client(adminConnectionId).SendAsync("ConnectionResponse", message);
        }
        else
        {
            // Sorğunu göndərən admin artıq yoxdursa, bütün adminlərə bildiririk.
            await Clients.Group(ClientRegistry.AdminGroup).SendAsync("ConnectionResponse", message);
        }
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
