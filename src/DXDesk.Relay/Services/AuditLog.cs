using System.Text.Json;

namespace DXDesk.Relay.Services;

/// <summary>
/// Audit jurnalı: kim, nə vaxt, hansı kompüterə qoşuldu / nə işə saldı.
/// Hər sətir bir JSON obyektidir (JSON Lines). Fayl yolu: <c>Audit:Path</c>
/// (standart: <c>audit/audit.log</c>, Docker-də volume kimi bağlayın).
/// </summary>
public sealed class AuditLog
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<AuditLog> _logger;

    public AuditLog(IConfiguration config, IHostEnvironment env, ILogger<AuditLog> logger)
    {
        _logger = logger;
        var configured = config["Audit:Path"];
        _path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "audit", "audit.log")
            : configured;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        _logger.LogInformation("Audit jurnalı: {Path}", Path.GetFullPath(_path));
    }

    public void Write(string action, string? ip, string? target = null, object? details = null)
    {
        var entry = new
        {
            time = DateTimeOffset.UtcNow,
            action,
            ip,
            target,
            details,
        };
        var line = JsonSerializer.Serialize(entry);
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Audit yazıla bilmədi: {Msg} | {Line}", ex.Message, line);
        }
    }
}
