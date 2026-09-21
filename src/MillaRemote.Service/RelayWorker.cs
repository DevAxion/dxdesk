using System.Runtime.Versioning;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MillaRemote.Service;

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

    public RelayWorker(ILogger<RelayWorker> logger, IConfiguration config)
    {
        _logger = logger;
        _relayUrl = config["Relay:Url"] ?? "http://localhost:5100/remotehub";
        _streamPort = int.TryParse(config["Stream:Port"], out var p) && p > 0 ? p : 7000;
        _agentPath = ResolveAgentPath(config["Agent:Path"]);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_relayUrl)
            .WithAutomaticReconnect(new FixedIntervalRetryPolicy(_reconnectDelay))
            .Build();

        connection.On<string>("ConnectionRequest", hostname =>
        {
            _logger.LogInformation("Qoşulma sorğusu: {Host}. Agent açılır (SYSTEM).", hostname);
            var args = $"--agent \"{_relayUrl}\" \"{_hostname}\" {_streamPort}";
            SessionLauncher.LaunchInActiveSession(_agentPath, args, m => _logger.LogInformation("{Msg}", m));
        });

        connection.On<string, string>("LaunchElevated", (program, arguments) =>
        {
            _logger.LogInformation("Elevated işə salma əmri: {Program} {Args}", program, arguments);
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
        try { await connection.InvokeAsync("RegisterClient", _hostname, ct); }
        catch (Exception ex) { _logger.LogWarning("Qeydiyyat alınmadı: {Msg}", ex.Message); }
    }

    private static string ResolveAgentPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }
        // Xidmətlə yanaşı (adətən %ProgramData%\MillaRemote).
        return Path.Combine(AppContext.BaseDirectory, "MillaRemote.Client.exe");
    }

    private sealed class FixedIntervalRetryPolicy(TimeSpan delay) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => delay;
    }
}
