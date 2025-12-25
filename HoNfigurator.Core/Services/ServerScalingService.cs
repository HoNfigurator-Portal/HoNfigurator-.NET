using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Interface for server scaling operations to avoid circular dependencies.
/// Implemented by adapters in the API layer that wrap IGameServerManager.
/// </summary>
public interface IServerScalingProvider
{
    IReadOnlyList<GameServerInstance> Instances { get; }
    int AddNewServer();
    Task<GameServerInstance?> StartServerAsync(int serverId);
    Task<bool> StopServerAsync(int serverId, bool graceful = true);
    Task<bool> RestartServerAsync(int serverId);
}

/// <summary>
/// Handles dynamic server scaling - add/remove game servers via API.
/// Port of Python HoNfigurator-Central scaling functionality.
/// </summary>
public class ServerScalingService
{
    private readonly ILogger<ServerScalingService> _logger;
    private readonly HoNConfiguration _config;
    private readonly SemaphoreSlim _scalingLock = new(1, 1);
    private IServerScalingProvider? _serverProvider;

    public ServerScalingService(
        ILogger<ServerScalingService> logger, 
        HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Set the server provider (called after DI resolution to avoid circular dependency)
    /// </summary>
    public void SetServerProvider(IServerScalingProvider provider)
    {
        _serverProvider = provider;
    }

    /// <summary>
    /// Get current scaling status
    /// </summary>
    public ScalingStatus GetStatus()
    {
        if (_serverProvider == null)
            return new ScalingStatus { Message = "Server provider not configured" };

        var instances = _serverProvider.Instances;
        var running = instances.Count(i => i.Status == ServerStatus.Ready || 
                                           i.Status == ServerStatus.Occupied || 
                                           i.Status == ServerStatus.Idle);
        var total = instances.Count;
        var maxServers = _config.HonData?.TotalServers ?? 10;

        return new ScalingStatus
        {
            CurrentServers = total,
            RunningServers = running,
            IdleServers = instances.Count(i => i.Status == ServerStatus.Idle || i.Status == ServerStatus.Ready),
            OccupiedServers = instances.Count(i => i.Status == ServerStatus.Occupied),
            OfflineServers = instances.Count(i => i.Status == ServerStatus.Offline),
            MaxServers = maxServers,
            CanScaleUp = total < maxServers,
            CanScaleDown = running > 0,
            AutoScalingEnabled = _config.ApplicationData?.AutoScaling?.Enabled ?? false
        };
    }

    /// <summary>
    /// Add specified number of servers
    /// </summary>
    public async Task<ScalingResult> AddServersAsync(int count)
    {
        if (count <= 0)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Count must be positive"
            };
        }

        if (_serverProvider == null)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Server provider not configured"
            };
        }

        await _scalingLock.WaitAsync();
        try
        {
            var maxServers = _config.HonData?.TotalServers ?? 10;
            var currentCount = _serverProvider.Instances.Count;
            var canAdd = Math.Min(count, maxServers - currentCount);

            if (canAdd <= 0)
            {
                return new ScalingResult
                {
                    Success = false,
                    Error = $"Cannot add servers - already at maximum ({maxServers})",
                    PreviousCount = currentCount,
                    CurrentCount = currentCount
                };
            }

            _logger.LogInformation("Adding {Count} servers (requested: {Requested})", canAdd, count);

            var added = 0;
            var errors = new List<string>();

            for (int i = 0; i < canAdd; i++)
            {
                try
                {
                    var newId = _serverProvider.AddNewServer();
                    if (newId > 0)
                    {
                        var result = await _serverProvider.StartServerAsync(newId);
                        if (result != null)
                        {
                            added++;
                            _logger.LogInformation("Started new server #{Id} on port {Port}", newId, result.Port);
                        }
                        else
                        {
                            errors.Add($"Server #{newId} failed to start");
                        }
                    }
                    else
                    {
                        errors.Add($"Failed to create server instance");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error adding server: {ex.Message}");
                }
            }

            return new ScalingResult
            {
                Success = added > 0,
                PreviousCount = currentCount,
                CurrentCount = _serverProvider.Instances.Count,
                Added = added,
                Removed = 0,
                Error = errors.Count > 0 ? string.Join("; ", errors) : null
            };
        }
        finally
        {
            _scalingLock.Release();
        }
    }

    /// <summary>
    /// Remove specified number of idle servers
    /// </summary>
    public async Task<ScalingResult> RemoveServersAsync(int count, bool forceRemoveOccupied = false)
    {
        if (count <= 0)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Count must be positive"
            };
        }

        if (_serverProvider == null)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Server provider not configured"
            };
        }

        await _scalingLock.WaitAsync();
        try
        {
            var currentCount = _serverProvider.Instances.Count;

            // Get servers eligible for removal (prefer idle/offline, avoid occupied unless forced)
            var eligibleServers = _serverProvider.Instances
                .Where(s => s.Status == ServerStatus.Idle || 
                           s.Status == ServerStatus.Ready || 
                           s.Status == ServerStatus.Offline ||
                           (forceRemoveOccupied && s.Status == ServerStatus.Occupied))
                .OrderBy(s => s.Status == ServerStatus.Offline ? 0 : 
                             s.Status == ServerStatus.Idle ? 1 : 
                             s.Status == ServerStatus.Ready ? 2 : 3)
                .ThenBy(s => s.NumClients) // Prefer servers with fewer players
                .Take(count)
                .ToList();

            if (eligibleServers.Count == 0)
            {
                return new ScalingResult
                {
                    Success = false,
                    Error = "No eligible servers to remove (all servers are occupied)",
                    PreviousCount = currentCount,
                    CurrentCount = currentCount
                };
            }

            _logger.LogInformation("Removing {Count} servers", eligibleServers.Count);

            var removed = 0;
            var errors = new List<string>();

            foreach (var server in eligibleServers)
            {
                try
                {
                    var stopped = await _serverProvider.StopServerAsync(server.Id, graceful: true);
                    if (stopped)
                    {
                        removed++;
                        _logger.LogInformation("Stopped server #{Id}", server.Id);
                    }
                    else
                    {
                        errors.Add($"Failed to stop server #{server.Id}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error removing server #{server.Id}: {ex.Message}");
                }
            }

            return new ScalingResult
            {
                Success = removed > 0,
                PreviousCount = currentCount,
                CurrentCount = _serverProvider.Instances.Count,
                Added = 0,
                Removed = removed,
                Error = errors.Count > 0 ? string.Join("; ", errors) : null
            };
        }
        finally
        {
            _scalingLock.Release();
        }
    }

    /// <summary>
    /// Scale to a specific number of servers
    /// </summary>
    public async Task<ScalingResult> ScaleToAsync(int targetCount)
    {
        if (_serverProvider == null)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Server provider not configured"
            };
        }

        var currentCount = _serverProvider.Instances.Count;
        
        if (targetCount > currentCount)
        {
            return await AddServersAsync(targetCount - currentCount);
        }
        else if (targetCount < currentCount)
        {
            return await RemoveServersAsync(currentCount - targetCount);
        }
        
        return new ScalingResult
        {
            Success = true,
            PreviousCount = currentCount,
            CurrentCount = currentCount,
            Message = "Already at target count"
        };
    }

    /// <summary>
    /// Auto-balance servers based on current demand
    /// </summary>
    public async Task<ScalingResult> AutoBalanceAsync()
    {
        if (_serverProvider == null)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Server provider not configured"
            };
        }

        var settings = _config.ApplicationData?.AutoScaling;
        if (settings == null || !settings.Enabled)
        {
            return new ScalingResult
            {
                Success = false,
                Error = "Auto-scaling is not enabled"
            };
        }

        var instances = _serverProvider.Instances;
        var totalPlayers = instances.Sum(i => i.NumClients);
        var occupiedServers = instances.Count(i => i.Status == ServerStatus.Occupied);
        var idleServers = instances.Count(i => i.Status == ServerStatus.Idle || i.Status == ServerStatus.Ready);
        var currentCount = instances.Count;

        _logger.LogDebug("Auto-balance: {Players} players, {Occupied} occupied, {Idle} idle servers",
            totalPlayers, occupiedServers, idleServers);

        // Ensure minimum idle servers for quick player pickup
        var minReady = settings.MinReadyServers;
        if (idleServers < minReady && currentCount < settings.MaxServers)
        {
            var toAdd = Math.Min(minReady - idleServers, settings.MaxServers - currentCount);
            _logger.LogInformation("Auto-balance: Adding {Count} servers to maintain minimum idle", toAdd);
            return await AddServersAsync(toAdd);
        }

        // Scale down if too many idle servers
        var maxIdle = Math.Max(minReady, 2);
        if (idleServers > maxIdle && currentCount > settings.MinServers)
        {
            var toRemove = Math.Min(idleServers - maxIdle, currentCount - settings.MinServers);
            _logger.LogInformation("Auto-balance: Removing {Count} excess idle servers", toRemove);
            return await RemoveServersAsync(toRemove);
        }

        return new ScalingResult
        {
            Success = true,
            PreviousCount = currentCount,
            CurrentCount = currentCount,
            Message = "No scaling action needed"
        };
    }
}

#region Models

public class ScalingStatus
{
    public int CurrentServers { get; set; }
    public int RunningServers { get; set; }
    public int IdleServers { get; set; }
    public int OccupiedServers { get; set; }
    public int OfflineServers { get; set; }
    public int MaxServers { get; set; }
    public bool CanScaleUp { get; set; }
    public bool CanScaleDown { get; set; }
    public bool AutoScalingEnabled { get; set; }
    public string? Message { get; set; }
}

public class ScalingResult
{
    public bool Success { get; set; }
    public int PreviousCount { get; set; }
    public int CurrentCount { get; set; }
    public int Added { get; set; }
    public int Removed { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

#endregion
