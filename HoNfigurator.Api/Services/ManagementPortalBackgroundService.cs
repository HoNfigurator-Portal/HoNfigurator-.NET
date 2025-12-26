using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;

namespace HoNfigurator.Api.Services;

/// <summary>
/// Background service that maintains connection with management.honfigurator.app
/// Periodically reports server status and handles registration
/// </summary>
public class ManagementPortalBackgroundService : BackgroundService
{
    private readonly ILogger<ManagementPortalBackgroundService> _logger;
    private readonly IManagementPortalConnector _portalConnector;
    private readonly IGameServerManager _gameServerManager;
    private readonly HoNConfiguration _config;
    private readonly TimeSpan _reportInterval;
    private bool _isConnected;

    public ManagementPortalBackgroundService(
        ILogger<ManagementPortalBackgroundService> logger,
        IManagementPortalConnector portalConnector,
        IGameServerManager gameServerManager,
        HoNConfiguration config)
    {
        _logger = logger;
        _portalConnector = portalConnector;
        _gameServerManager = gameServerManager;
        _config = config;
        
        var intervalSeconds = config.ApplicationData?.ManagementPortal?.StatusReportIntervalSeconds ?? 30;
        _reportInterval = TimeSpan.FromSeconds(Math.Max(10, intervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_portalConnector.IsEnabled)
        {
            _logger.LogInformation("Management portal integration is disabled");
            return;
        }

        _logger.LogInformation("Management portal background service starting (interval: {Interval}s)", 
            _reportInterval.TotalSeconds);

        // Wait for application to fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        // Try to register on startup
        await TryRegisterAsync(stoppingToken);

        // Main loop
        using var timer = new PeriodicTimer(_reportInterval);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await ReportStatusAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in management portal background service");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("Management portal background service stopped");
    }

    private async Task TryRegisterAsync(CancellationToken stoppingToken)
    {
        var autoRegister = _config.ApplicationData?.ManagementPortal?.AutoRegister ?? true;
        
        if (!autoRegister)
        {
            _logger.LogInformation("Auto-registration is disabled");
            return;
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            _logger.LogInformation("Attempting to register with management portal (attempt {Attempt}/3)", attempt);
            
            var result = await _portalConnector.RegisterServerAsync(stoppingToken);
            
            if (result.Success)
            {
                _isConnected = true;
                _logger.LogInformation("Successfully registered with management portal: {ServerName} at {Address}",
                    result.ServerName, result.ServerAddress);
                return;
            }
            
            _logger.LogWarning("Registration attempt {Attempt} failed: {Message}", attempt, result.Message);
            
            if (attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(10 * attempt), stoppingToken);
            }
        }
        
        _logger.LogWarning("Failed to register with management portal after 3 attempts");
    }

    private async Task ReportStatusAsync(CancellationToken stoppingToken)
    {
        // Check connection first
        if (!_isConnected)
        {
            var connected = await _portalConnector.PingManagementPortalAsync(stoppingToken);
            if (!connected)
            {
                await TryRegisterAsync(stoppingToken);
            }
            _isConnected = connected;
        }

        if (!_portalConnector.IsRegistered)
            return;

        try
        {
            var instances = _gameServerManager.GetAllServers();
            var runningCount = instances.Count(s => s.Status == ServerStatus.Ready || s.Status == ServerStatus.Occupied);
            var totalPlayers = instances.Sum(s => s.NumClients);

            // Build detailed instance list
            var instanceStatuses = instances.Select(s => new GameInstanceStatus
            {
                InstanceId = s.Id,
                Name = s.Name ?? $"Server {s.Id}",
                Port = s.Port,
                Status = s.Status.ToString(),
                NumClients = s.NumClients,
                MatchId = string.IsNullOrEmpty(s.MatchId) ? null : long.TryParse(s.MatchId, out var mid) ? mid : (long?)null,
                GamePhase = s.GamePhase,
                Map = null, // Not available in GameServerInstance
                GameMode = null, // Not available in GameServerInstance
                StartTime = s.StartTime
            }).ToList();

            // Get system stats
            var cpuCount = Environment.ProcessorCount;
            var svrTotalPerCore = _config.HonData?.TotalPerCore ?? 1.0;
            var svrTotal = _config.HonData?.TotalServers ?? 1;
            var maxAllowedServers = GetTotalAllowedServers(cpuCount, svrTotalPerCore);
            
            var systemStats = new SystemStatsInfo
            {
                CpuPercent = GetCpuUsage(),
                CpuCount = cpuCount,
                MemoryPercent = GetMemoryUsage(),
                TotalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
                UsedMemoryMb = GC.GetTotalMemory(false) / (1024 * 1024),
                UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds,
                SvrTotalPerCore = svrTotalPerCore,
                MaxAllowedServers = maxAllowedServers,
                SvrTotal = svrTotal
            };

            var status = new ServerStatusReport
            {
                ServerName = _config.HonData?.ServerName ?? "Unknown",
                ServerIp = _config.HonData?.ServerIp ?? "",
                ApiPort = _config.HonData?.ApiPort ?? 5000,
                Status = _gameServerManager.MasterServerConnected ? "Online" : "Offline",
                TotalServers = instances.Count,
                RunningServers = runningCount,
                PlayersOnline = totalPlayers,
                HonVersion = _config.HonData?.ManVersion,
                HonfiguratorVersion = "1.0.0",
                Timestamp = DateTime.UtcNow,
                Instances = instanceStatuses,
                SystemStats = systemStats,
                MasterServerConnected = _gameServerManager.MasterServerConnected,
                ChatServerConnected = _gameServerManager.ChatServerConnected
            };

            await _portalConnector.ReportServerStatusAsync(status, stoppingToken);
            
            _logger.LogDebug("Reported status to management portal: {Running}/{Total} servers, {Players} players",
                runningCount, instances.Count, totalPlayers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report status to management portal");
            _isConnected = false;
        }
    }

    private static readonly DateTime _startTime = DateTime.UtcNow;
    
    private double GetCpuUsage()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalMilliseconds / 
                   (Environment.ProcessorCount * (DateTime.UtcNow - _startTime).TotalMilliseconds) * 100;
        }
        catch
        {
            return 0;
        }
    }

    private double GetMemoryUsage()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            var usedMemory = GC.GetTotalMemory(false);
            return (double)usedMemory / gcInfo.TotalAvailableMemoryBytes * 100;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Calculate maximum allowed servers based on CPU count and servers per core setting
    /// Reserves some CPUs for OS/Manager: ≤4 cores: 1 reserved, 5-12: 2 reserved, >12: 4 reserved
    /// </summary>
    private static int GetTotalAllowedServers(int cpuCount, double svrTotalPerCore)
    {
        var total = svrTotalPerCore * cpuCount;
        
        // Reserve CPUs for OS/Manager based on total cores
        if (cpuCount < 5)
            total -= 1;
        else if (cpuCount > 4 && cpuCount < 13)
            total -= 2;
        else if (cpuCount > 12)
            total -= 4;
        
        return Math.Max(0, (int)total);
    }
}
