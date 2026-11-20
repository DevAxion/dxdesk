using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DXDesk.Relay.Services;

/// <summary>
/// Relay açarları və onların yoxlanması.
///   Admin:Secret        — admin aləti (Server.exe) üçün, MƏCBURİDİR (produksiyada).
///   Client:Secret       — kompüterlərin (Service/Client) qeydiyyatı üçün, tövsiyə olunur.
/// </summary>
public sealed class RelaySecurity
{
    private const int MinSecretLength = 16;
    private readonly byte[]? _admin;
    private readonly byte[]? _client;

    public bool ClientSecretEnabled => _client is not null;

    public RelaySecurity(IConfiguration config, IHostEnvironment env, ILogger<RelaySecurity> logger)
    {
        var admin = config["Admin:Secret"];
        var client = config["Client:Secret"];

        if (!IsStrong(admin))
        {
            if (!env.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Admin:Secret təyin olunmayıb və ya zəifdir (min 16 simvol, nümunə dəyər olmamalı). " +
                    "Docker-də MILLA_ADMIN_SECRET dəyişənini qoyun.");
            }
            logger.LogWarning("DEV rejimi: Admin:Secret yoxdur — admin yoxlaması SÖNDÜRÜLÜB.");
        }
        else
        {
            _admin = Encoding.UTF8.GetBytes(admin!);
        }

        if (IsStrong(client))
        {
            _client = Encoding.UTF8.GetBytes(client!);
        }
        else
        {
            logger.LogWarning("Client:Secret yoxdur — istənilən proqram kompüter kimi qeydiyyatdan keçə bilər.");
        }
    }

    public bool IsAdmin(string? token) => _admin is null || Matches(_admin, token);

    // ---- Səhv cəhd limiti (açarı sınaq-səhvlə tapmağa qarşı) ----
    private const int MaxFailures = 10;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset First, DateTimeOffset BlockedUntil)> _failures = new();

    /// <summary>Bu IP səhv cəhdlərə görə müvəqqəti bloklanıbmı.</summary>
    public bool IsBlocked(string? ip) =>
        ip is not null && _failures.TryGetValue(ip, out var f) && f.BlockedUntil > DateTimeOffset.UtcNow;

    /// <summary>Səhv açar cəhdini qeyd edir; limit aşılanda IP bloklanır. Bloklandısa true.</summary>
    public bool RecordFailure(string? ip)
    {
        if (ip is null) return false;
        var now = DateTimeOffset.UtcNow;
        var entry = _failures.AddOrUpdate(ip,
            _ => (1, now, DateTimeOffset.MinValue),
            (_, f) => now - f.First > FailureWindow ? (1, now, DateTimeOffset.MinValue) : (f.Count + 1, f.First, f.BlockedUntil));
        if (entry.Count >= MaxFailures && entry.BlockedUntil <= now)
        {
            _failures[ip] = (entry.Count, entry.First, now + BlockDuration);
            return true;
        }
        return false;
    }

    public bool IsValidClient(string? token) => _client is null || Matches(_client, token);

    private static bool Matches(byte[] expected, string? token) =>
        token is not null && CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(token));

    private static bool IsStrong(string? s) =>
        !string.IsNullOrWhiteSpace(s) &&
        s.Length >= MinSecretLength &&
        !s.Contains("DEYISIN", StringComparison.OrdinalIgnoreCase);
}
