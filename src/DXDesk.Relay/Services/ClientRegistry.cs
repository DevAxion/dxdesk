using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

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
    int StreamPort,
    string? SessionToken = null);

/// <summary>Gözləyən qoşulma sorğusu: kim göndərib, hansı açarla, nə vaxt.</summary>
public sealed record PendingRequest(string AdminConnectionId, string Token, DateTimeOffset CreatedAtUtc);

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
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>sessiya açarı -> (hostname, yaradılma vaxtı): relay axını üçün.</summary>
    private readonly ConcurrentDictionary<string, (string Host, DateTimeOffset Created)> _streamTokens =
        new(StringComparer.Ordinal);

    /// <summary>Relay axını üçün açarın cütləşməyə qədər etibarlı olduğu müddət.</summary>
    public static readonly TimeSpan StreamTokenLifetime = TimeSpan.FromMinutes(3);

    /// <summary>Sorğu bu müddətdən sonra etibarsızdır.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Kliyenti qeydiyyata alır. Eyni hostname artıq BAŞQA IP-dən onlayndırsa,
    /// rədd edir (false) — başqasının adını "oğurlamaq" olmasın.
    /// </summary>
    public bool TryAddOrUpdateClient(ClientInfo client)
    {
        if (_clientsByHostname.TryGetValue(client.Hostname, out var existing) &&
            !string.Equals(existing.ConnectionId, client.ConnectionId, StringComparison.Ordinal) &&
            !string.Equals(existing.IpAddress, client.IpAddress, StringComparison.Ordinal))
        {
            return false;
        }

        AddOrUpdateClient(client);
        return true;
    }

    private void AddOrUpdateClient(ClientInfo client)
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

    public void SetPendingRequest(string hostname, string adminConnectionId, string token)
    {
        _pendingRequests[hostname] = new PendingRequest(adminConnectionId, token, DateTimeOffset.UtcNow);
        _streamTokens[token] = (hostname, DateTimeOffset.UtcNow);

        // Köhnəlmiş axın açarlarını təmizlə.
        foreach (var (key, value) in _streamTokens)
        {
            if (DateTimeOffset.UtcNow - value.Created > StreamTokenLifetime)
            {
                _streamTokens.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Açar relay axını üçün etibarlıdırsa, aid olduğu hostname-i qaytarır.</summary>
    public string? GetStreamTokenHost(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_streamTokens.TryGetValue(token, out var entry))
        {
            return null;
        }
        return DateTimeOffset.UtcNow - entry.Created <= StreamTokenLifetime ? entry.Host : null;
    }

    /// <summary>Agent və viewer cütləşəndən sonra açar bir daha istifadə oluna bilməz.</summary>
    public void ConsumeStreamToken(string token) => _streamTokens.TryRemove(token, out _);

    /// <summary>
    /// Gözləyən sorğunu YALNIZ açar uyğun gəlirsə və vaxtı keçməyibsə götürür.
    /// Beləliklə saxta "qəbul etdi" cavabı göndərmək mümkün olmur.
    /// </summary>
    public PendingRequest? TakePendingRequest(string hostname, string? token)
    {
        if (string.IsNullOrEmpty(token) || !_pendingRequests.TryGetValue(hostname, out var pending))
        {
            return null;
        }

        var match = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(pending.Token), Encoding.ASCII.GetBytes(token));
        if (!match)
        {
            return null;
        }

        _pendingRequests.TryRemove(hostname, out _);
        return DateTimeOffset.UtcNow - pending.CreatedAtUtc <= PendingLifetime ? pending : null;
    }
}
