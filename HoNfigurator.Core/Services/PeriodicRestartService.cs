using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages periodic server restarts based on uptime limits.
/// Port of Python HoNfigurator-Central server lifecycle management.
/// </summary>
public class PeriodicRestartService : BackgroundService
{
    private readonly ILogger<PeriodicRestartService> _logger;
    private readonly HoNConfiguration _config;
    private readonly IServerScalingProvider? _serverScalingProvider;
    private readonly Dictionary<int, ServerUptimeInfo> _serverUptimes = new();
    private readonly object _lock = new();
    private readonly Random _random = new();

    /// <summary>
    /// Event raised when a server should be restarted
    /// </summary>
    public event EventHandler<ServerRestartEventArgs>? ServerRestartRequired;

    /// <summary>
    /// Event raised before a server restart begins
    /// </summary>
    public event EventHandler<ServerRestartEventArgs>? ServerRestartStarting;

    /// <summary>
    /// Event raised when a server restart completes
    /// </summary>
    public event EventHandler<ServerRestartCompletedEventArgs>? ServerRestartCompleted;

    public PeriodicRestartService(
        ILogger<PeriodicRestartService> logger, 
        HoNConfiguration config,
        IServerScalingProvider? serverScalingProvider = null)
    {
        _logger = logger;
        _config = config;
        _serverScalingProvider = serverScalingProvider;
    }

    /// <summary>
    /// Register a server for uptime monitoring
    /// </summary>
    public void RegisterServer(int serverId, int port)
    {
        lock (_lock)
        {
            if (!_serverUptimes.ContainsKey(serverId))
            {
                var minHours = _config.ServerLifecycle?.MinUptimeHours ?? 24;
                var maxHours = _config.ServerLifecycle?.MaxUptimeHours ?? 48;
                var targetUptime = TimeSpan.FromHours(_random.Next(minHours, maxHours + 1));

                _serverUptimes[serverId] = new ServerUptimeInfo
                {
                    ServerId = serverId,
                    Port = port,
                    StartTime = DateTime.UtcNow,
                    TargetUptime = targetUptime
                };

                _logger.LogInformation(
                    "Server {ServerId} registered for periodic restart. Target uptime: {Hours:F1} hours",
                    serverId, targetUptime.TotalHours);
            }
        }
    }

    /// <summary>
    /// Unregister a server from uptime monitoring
    /// </summary>
    public void UnregisterServer(int serverId)
    {
        lock (_lock)
        {
            _serverUptimes.Remove(serverId);
            _logger.LogDebug("Server {ServerId} unregistered from periodic restart", serverId);
        }
    }

    /// <summary>
    /// Reset server uptime (call after restart)
    /// </summary>
    public void ResetServerUptime(int serverId)
    {
        lock (_lock)
        {
            if (_serverUptimes.TryGetValue(serverId, out var info))
            {
                var minHours = _config.ServerLifecycle?.MinUptimeHours ?? 24;
                var maxHours = _config.ServerLifecycle?.MaxUptimeHours ?? 48;

                info.StartTime = DateTime.UtcNow;
                info.TargetUptime = TimeSpan.FromHours(_random.Next(minHours, maxHours + 1));
                info.RestartScheduled = false;
                info.LastRestartAttempt = null;

                _logger.LogInformation(
                    "Server {ServerId} uptime reset. Next target uptime: {Hours:F1} hours",
                    serverId, info.TargetUptime.TotalHours);
            }
        }
    }

    /// <summary>
    /// Get uptime info for all servers
    /// </summary>
    public IReadOnlyDictionary<int, ServerUptimeStatus> GetUptimeStatus()
    {
        lock (_lock)
        {
            return _serverUptimes.ToDictionary(
                kvp => kvp.Key,
                kvp => new ServerUptimeStatus
                {
                    ServerId = kvp.Value.ServerId,
                    Port = kvp.Value.Port,
                    Uptime = DateTime.UtcNow - kvp.Value.StartTime,
                    TargetUptime = kvp.Value.TargetUptime,
                    TimeUntilRestart = kvp.Value.TargetUptime - (DateTime.UtcNow - kvp.Value.StartTime),
                    RestartScheduled = kvp.Value.RestartScheduled
                });
        }
    }

    /// <summary>
    /// Get uptime for a specific server
    /// </summary>
    public ServerUptimeStatus? GetServerUptime(int serverId)
    {
        lock (_lock)
        {
            if (_serverUptimes.TryGetValue(serverId, out var info))
            {
                return new ServerUptimeStatus
                {
                    ServerId = info.ServerId,
                    Port = info.Port,
                    Uptime = DateTime.UtcNow - info.StartTime,
                    TargetUptime = info.TargetUptime,
                    TimeUntilRestart = info.TargetUptime - (DateTime.UtcNow - info.StartTime),
                    RestartScheduled = info.RestartScheduled
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Schedule an immediate restart for a server
    /// </summary>
    public void ScheduleImmediateRestart(int serverId, string reason)
    {
        lock (_lock)
        {
            if (_serverUptimes.TryGetValue(serverId, out var info))
            {
                info.RestartScheduled = true;
                info.ImmediateRestart = true;
                info.RestartReason = reason;
                _logger.LogInformation("Immediate restart scheduled for server {ServerId}: {Reason}", 
                    serverId, reason);
            }
        }
    }

    /// <summary>
    /// Cancel a scheduled restart
    /// </summary>
    public bool CancelScheduledRestart(int serverId)
    {
        lock (_lock)
        {
            if (_serverUptimes.TryGetValue(serverId, out var info) && info.RestartScheduled)
            {
                info.RestartScheduled = false;
                info.ImmediateRestart = false;
                info.RestartReason = null;
                _logger.LogInformation("Restart cancelled for server {ServerId}", serverId);
                return true;
            }
        }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkInterval = TimeSpan.FromMinutes(_config.ServerLifecycle?.CheckIntervalMinutes ?? 5);

        _logger.LogInformation("Periodic restart service started. Check interval: {Interval}", checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckServersAsync(stoppingToken);
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in periodic restart check loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Periodic restart service stopped");
    }

    private async Task CheckServersAsync(CancellationToken cancellationToken)
    {
        List<ServerUptimeInfo> serversToRestart;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            serversToRestart = _serverUptimes.Values
                .Where(s => s.ImmediateRestart || 
                            (!s.RestartScheduled && (now - s.StartTime) >= s.TargetUptime))
                .ToList();

            // Mark as scheduled
            foreach (var server in serversToRestart)
            {
                server.RestartScheduled = true;
            }
        }

        foreach (var server in serversToRestart)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reason = server.RestartReason ?? 
                $"Uptime limit reached ({server.TargetUptime.TotalHours:F1} hours)";

            _logger.LogInformation(
                "Server {ServerId} restart triggered: {Reason}",
                server.ServerId, reason);

            // Raise event
            ServerRestartRequired?.Invoke(this, new ServerRestartEventArgs
            {
                ServerId = server.ServerId,
                Port = server.Port,
                Reason = reason,
                Uptime = DateTime.UtcNow - server.StartTime
            });

            // Attempt restart if provider is available
            if (_serverScalingProvider != null)
            {
                await AttemptServerRestartAsync(server, reason, cancellationToken);
            }
        }
    }

    private async Task AttemptServerRestartAsync(
        ServerUptimeInfo server, 
        string reason,
        CancellationToken cancellationToken)
    {
        ServerRestartStarting?.Invoke(this, new ServerRestartEventArgs
        {
            ServerId = server.ServerId,
            Port = server.Port,
            Reason = reason,
            Uptime = DateTime.UtcNow - server.StartTime
        });

        try
        {
            server.LastRestartAttempt = DateTime.UtcNow;

            // Check if server is in a game (wait if so)
            var maxWaitTime = TimeSpan.FromMinutes(_config.ServerLifecycle?.MaxWaitForGameMinutes ?? 60);
            var waitStart = DateTime.UtcNow;

            while (DateTime.UtcNow - waitStart < maxWaitTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isInGame = await IsServerInGameAsync(server.ServerId);
                if (!isInGame) break;

                _logger.LogDebug("Server {ServerId} is in game, waiting before restart...", server.ServerId);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }

            // Perform restart
            var success = await _serverScalingProvider!.RestartServerAsync(server.ServerId);

            if (success)
            {
                ResetServerUptime(server.ServerId);
                
                ServerRestartCompleted?.Invoke(this, new ServerRestartCompletedEventArgs
                {
                    ServerId = server.ServerId,
                    Port = server.Port,
                    Success = true,
                    Reason = reason
                });

                _logger.LogInformation("Server {ServerId} restarted successfully", server.ServerId);
            }
            else
            {
                ServerRestartCompleted?.Invoke(this, new ServerRestartCompletedEventArgs
                {
                    ServerId = server.ServerId,
                    Port = server.Port,
                    Success = false,
                    Reason = reason,
                    Error = "Restart operation failed"
                });

                _logger.LogWarning("Failed to restart server {ServerId}", server.ServerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting server {ServerId}", server.ServerId);
            
            ServerRestartCompleted?.Invoke(this, new ServerRestartCompletedEventArgs
            {
                ServerId = server.ServerId,
                Port = server.Port,
                Success = false,
                Reason = reason,
                Error = ex.Message
            });
        }
    }

    private Task<bool> IsServerInGameAsync(int serverId)
    {
        // TODO: Implement actual game state check via GameServerManager
        return Task.FromResult(false);
    }
}

// Internal state tracking

internal class ServerUptimeInfo
{
    public int ServerId { get; set; }
    public int Port { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan TargetUptime { get; set; }
    public bool RestartScheduled { get; set; }
    public bool ImmediateRestart { get; set; }
    public string? RestartReason { get; set; }
    public DateTime? LastRestartAttempt { get; set; }
}

// DTOs and Event Args

public class ServerUptimeStatus
{
    public int ServerId { get; set; }
    public int Port { get; set; }
    public TimeSpan Uptime { get; set; }
    public TimeSpan TargetUptime { get; set; }
    public TimeSpan TimeUntilRestart { get; set; }
    public bool RestartScheduled { get; set; }
}

public class ServerRestartEventArgs : EventArgs
{
    public int ServerId { get; set; }
    public int Port { get; set; }
    public string Reason { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
}

public class ServerRestartCompletedEventArgs : EventArgs
{
    public int ServerId { get; set; }
    public int Port { get; set; }
    public bool Success { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Error { get; set; }
}
