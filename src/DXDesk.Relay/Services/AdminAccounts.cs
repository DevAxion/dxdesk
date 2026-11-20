using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DXDesk.Relay.Services;

/// <summary>
/// Hər admin üçün ayrıca hesab (ad + parol hash-i).
/// Fayl: Admin:UsersFile (standart /app/config/admins.json), formatı:
///   { "users": [ { "name": "ali", "hash": "pbkdf2-sha256$600000$..." } ] }
/// Hash yaratmaq:  docker compose run --rm -it relay hash-password
/// Fayl hər girişdə yenidən oxunur — admin əlavə/silmək üçün relay-i yenidən başlatmaq lazım deyil.
/// </summary>
public sealed class AdminAccounts
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly string _path;
    private readonly ILogger<AdminAccounts> _logger;
    private readonly ConcurrentDictionary<string, (string Name, DateTimeOffset Expires)> _sessions = new();

    public AdminAccounts(IConfiguration config, ILogger<AdminAccounts> logger)
    {
        _path = config["Admin:UsersFile"] ?? "/app/config/admins.json";
        _logger = logger;
        var count = LoadUsers().Count;
        _logger.LogInformation("Admin hesabları: {Count} ({Path})", count, _path);
    }

    /// <summary>Ad və parolu yoxlayır; düzgündürsə sessiya açarı qaytarır.</summary>
    public string? Login(string? name, string? password)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(password)) return null;
        var users = LoadUsers();
        if (!users.TryGetValue(name.Trim(), out var hash))
        {
            // Mövcud olmayan ad üçün də eyni vaxt sərf olunsun (adları təxmin etməyə qarşı).
            Verify(password, $"{Prefix}${Iterations}$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
            return null;
        }
        if (!Verify(password, hash)) return null;

        CleanupExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = (name.Trim(), DateTimeOffset.UtcNow + SessionLifetime);
        return token;
    }

    /// <summary>Sessiya açarına görə admin adı (etibarsız/vaxtı keçibsə null).
    /// Hesab fayldan silinibsə sessiya da dərhal etibarsız olur.</summary>
    public string? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var s)) return null;
        if (s.Expires <= DateTimeOffset.UtcNow || !LoadUsers().ContainsKey(s.Name))
        {
            _sessions.TryRemove(token, out _);
            return null;
        }
        return s.Name;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _sessions)
            if (kv.Value.Expires <= now) _sessions.TryRemove(kv.Key, out _);
    }

    private Dictionary<string, string> LoadUsers()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(_path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (!doc.RootElement.TryGetProperty("users", out var arr)) return result;
            foreach (var u in arr.EnumerateArray())
            {
                var n = u.GetProperty("name").GetString();
                var h = u.GetProperty("hash").GetString();
                if (!string.IsNullOrWhiteSpace(n) && !string.IsNullOrWhiteSpace(h)) result[n.Trim()] = h;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Admin hesabları faylı oxunmadı ({Path}): {Error}", _path, ex.Message);
        }
        return result;
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix) return false;
            var iter = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"hash-password" əmri: parolu iki dəfə gizli oxuyur, hash sətrini çap edir.</summary>
    public static int RunHashTool()
    {
        var p1 = ReadSecret("Parol: ");
        var p2 = ReadSecret("Təkrar: ");
        if (p1 != p2) { Console.Error.WriteLine("Parollar uyğun deyil."); return 1; }
        if (p1.Length < 12) { Console.Error.WriteLine("Parol ən az 12 simvol olmalıdır."); return 1; }
        Console.WriteLine(HashPassword(p1));
        return 0;
    }

    private static string ReadSecret(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var sb = new StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.Error.WriteLine();
        return sb.ToString();
    }
}
