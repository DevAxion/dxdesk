using System.Runtime.Versioning;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DXDesk.Service;

/// <summary>
/// LocalSystem xidmətinin əsas işçisi: relay-ə qoşulur, hostname ilə qeydiyyatdan
/// keçir və admin əmrlərini icra edir:
///   ConnectionRequest -> aktiv sessiyada SYSTEM agent (ekran+idarə) açır
///   LaunchElevated    -> aktiv sessiyada proqramı SYSTEM kimi açır (UAC-siz)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RelayWorker : BackgroundService
{
    private readonly ILogger<RelayWorker> _logger;
    private readonly string _relayUrl;
    private readonly int _streamPort;
    private readonly string _agentPath;
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(10);
    private readonly string _hostname = Environment.MachineName;
    private readonly string _clientSecret;
    private readonly bool _launchEnabled;
    private readonly string[] _launchAllowed;

    public RelayWorker(ILogger<RelayWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _relayUrl = config["Relay:Url"] ?? "http://localhost:5100/remotehub";
        _streamPort = int.TryParse(config["Stream:Port"], out var p) && p > 0 ? p : 7000;
        _agentPath = ResolveAgentPath(config["Agent:Path"]);
        _clientSecret = config["Relay:ClientSecret"] ?? "";
        _launchEnabled = !string.Equals(config["Launch:Enabled"], "false", StringComparison.OrdinalIgnoreCase);
        _launchAllowed = config.GetSection("Launch:AllowedPrograms").GetChildren()
            .Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToArray();
    }

    /// <summary>
    /// SYSTEM kimi açılacaq proqramı LOKAL siyasətlə yoxlayır. Relay ələ keçsə belə,
    /// bu kompüter yalnız icazəli siyahıdakı proqramları açır ("*" ilə bitən = prefiks).
    /// Siyahı boşdursa hamısına icazə var (köhnə davranış), amma loqda xəbərdarlıq olur.
    /// </summary>
    private bool IsLaunchAllowed(string program)
    {
        if (!_launchEnabled) return false;
        if (_launchAllowed.Length == 0) return true;
        var name = program.Trim().Trim('"');
        return _launchAllowed.Any(a =>
            a.EndsWith('*')
                ? name.StartsWith(a[..^1], StringComparison.OrdinalIgnoreCase)
                : string.Equals(name, a, StringComparison.OrdinalIgnoreCase));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_relayUrl)
            .WithAutomaticReconnect(new FixedIntervalRetryPolicy(_reconnectDelay))
            .Build();

        connection.On<string, string>("ConnectionRequest", (hostname, sessionToken) =>
        {
            _logger.LogInformation("Qoşulma sorğusu: {Host}. Agent açılır (SYSTEM).", hostname);
            if (string.IsNullOrEmpty(sessionToken) || !sessionToken.All(Uri.IsHexDigit))
            {
                _logger.LogWarning("Sorğuda düzgün sessiya açarı yoxdur — rədd edildi.");
                return;
            }
            var args = $"--agent \"{_relayUrl}\" \"{_hostname}\" {_streamPort} {sessionToken}";
            SessionLauncher.LaunchInActiveSession(_agentPath, args, m => _logger.LogInformation("{Msg}", m));
        });

        connection.On<string, string>("LaunchElevated", (program, arguments) =>
        {
            _logger.LogInformation("Elevated işə salma əmri: {Program} {Args}", program, arguments);
            if (!IsLaunchAllowed(program))
            {
                _logger.LogWarning("Elevated işə salma lokal siyasətlə BLOKLANDI: {Program}", program);
                return;
            }
            if (_launchAllowed.Length == 0)
            {
                _logger.LogWarning("Launch:AllowedPrograms boşdur — istənilən proqram SYSTEM kimi açıla bilər.");
            }
            SessionLauncher.LaunchInActiveSession(program, arguments ?? string.Empty, m => _logger.LogInformation("{Msg}", m));
        });

        connection.Reconnected += async _ =>
        {
            _logger.LogInformation("Relay-ə yenidən qoşuldu, qeydiyyat təkrarlanır.");
            await SafeRegisterAsync(connection, stoppingToken);
        };

        await ConnectLoopAsync(connection, stoppingToken);

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await connection.DisposeAsync();
    }

    private async Task ConnectLoopAsync(HubConnection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(ct);
                await SafeRegisterAsync(connection, ct);
                _logger.LogInformation("Relay-ə qoşuldu: {Url} (host {Host})", _relayUrl, _hostname);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning("Qoşulma alınmadı: {Msg}. {Sec}s sonra təkrar.", ex.Message, _reconnectDelay.TotalSeconds);
                try { await Task.Delay(_reconnectDelay, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task SafeRegisterAsync(HubConnection connection, CancellationToken ct)
    {
        try { await connection.InvokeAsync("RegisterClient", _hostname, _clientSecret, ct); }
        catch (Exception ex) { _logger.LogWarning("Qeydiyyat alınmadı: {Msg}", ex.Message); }
    }

    private static string ResolveAgentPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        // Xidmətlə yanaşı (adətən %ProgramData%\DXDesk).
        return Path.Combine(AppContext.BaseDirectory, "DXDesk.Client.exe");
    }

    private sealed class FixedIntervalRetryPolicy(TimeSpan delay) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => delay;
    }
}
