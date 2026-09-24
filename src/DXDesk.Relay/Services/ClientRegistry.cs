using System.Collections.Concurrent;

namespace DXDesk.Relay.Services;

/// <summary>Bir dəfə qeydiyyatdan keçmiş son istifadəçi kompüteri.</summary>
public sealed record ClientInfo(
    string Hostname,
    string ConnectionId,
    string? IpAddress,
    DateTimeOffset ConnectedAtUtc);

/// <summary><c>RequestConnection</c> çağırışının nəticəsi.</summary>
public sealed record RequestResult(
    bool Sent,
    string? Error,
    string? Hostname,
    string? IpAddress);

/// <summary>Kliyentin cavabı — admin alətinə göndərilir.</summary>
public sealed record ConnectionResponseMessage(
    string Hostname,
    bool Accepted,
    string? IpAddress,
    int StreamPort);

/// <summary>
/// Onlayn kliyentlərin (hostname -> connectionId) və admin bağlantılarının
/// yaddaşdaxili reyestri. Bütün üzvlər thread-safe-dir.
/// </summary>
public sealed class ClientRegistry
{
    public const string AdminGroup = "admins";

    private readonly ConcurrentDictionary<string, ClientInfo> _clientsByHostname =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _hostnameByConnectionId =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> _admins =
        new(StringComparer.Ordinal);

    /// <summary>hostname -> sorğunu göndərən admin connectionId.</summary>
    private readonly ConcurrentDictionary<string, string> _pendingRequests =
        new(StringComparer.OrdinalIgnoreCase);

    public void AddOrUpdateClient(ClientInfo client)
    {
        // Eyni hostname yenidən qoşulubsa, köhnə connectionId-ni təmizləyirik.
        if (_clientsByHostname.TryGetValue(client.Hostname, out var previous) &&
            !string.Equals(previous.ConnectionId, client.ConnectionId, StringComparison.Ordinal))
        {
            _hostnameByConnectionId.TryRemove(previous.ConnectionId, out _);
        }

        _clientsByHostname[client.Hostname] = client;
        _hostnameByConnectionId[client.ConnectionId] = client.Hostname;
    }

    public ClientInfo? GetByHostname(string hostname) =>
        string.IsNullOrWhiteSpace(hostname)
            ? null
            : _clientsByHostname.TryGetValue(hostname.Trim(), out var client) ? client : null;

    public ClientInfo? GetByConnectionId(string connectionId) =>
        _hostnameByConnectionId.TryGetValue(connectionId, out var hostname)
            ? GetByHostname(hostname)
            : null;

    public IReadOnlyList<ClientInfo> GetClients() =>
        _clientsByHostname.Values
            .OrderBy(c => c.Hostname, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Bağlantı kəsiləndə çağırılır. Silinən kliyentin adını qaytarır.</summary>
    public string? RemoveConnection(string connectionId)
    {
        _admins.TryRemove(connectionId, out _);

        if (!_hostnameByConnectionId.TryRemove(connectionId, out var hostname))
        {
            return null;
        }

        // Yalnız hələ də bu connectionId-yə aid olan yazını silirik.
        if (_clientsByHostname.TryGetValue(hostname, out var client) &&
            string.Equals(client.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            _clientsByHostname.TryRemove(hostname, out _);
        }

        _pendingRequests.TryRemove(hostname, out _);
        return hostname;
    }

    public void AddAdmin(string connectionId) => _admins[connectionId] = 0;

    public bool IsAdmin(string connectionId) => _admins.ContainsKey(connectionId);

    public void SetPendingRequest(string hostname, string adminConnectionId) =>
        _pendingRequests[hostname] = adminConnectionId;

    /// <summary>Gözləyən sorğunu götürür və reyestrdən silir.</summary>
    public string? TakePendingRequest(string hostname) =>
        _pendingRequests.TryRemove(hostname, out var adminConnectionId) ? adminConnectionId : null;
}
