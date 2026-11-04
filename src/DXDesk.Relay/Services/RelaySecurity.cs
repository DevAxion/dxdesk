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

    public bool IsValidClient(string? token) => _client is null || Matches(_client, token);

    private static bool Matches(byte[] expected, string? token) =>
        token is not null && CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(token));

    private static bool IsStrong(string? s) =>
        !string.IsNullOrWhiteSpace(s) &&
        s.Length >= MinSecretLength &&
        !s.Contains("DEYISIN", StringComparison.OrdinalIgnoreCase);
}
