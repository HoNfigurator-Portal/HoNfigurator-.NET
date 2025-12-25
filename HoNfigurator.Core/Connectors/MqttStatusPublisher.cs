using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Connectors;

/// <summary>
/// Publishes real-time game server status to MQTT broker.
/// Port of Python HoNfigurator-Central MQTT functionality.
/// </summary>
public class MqttStatusPublisher : IDisposable
{
    private readonly ILogger<MqttStatusPublisher> _logger;
    private readonly HoNConfiguration _config;
    private readonly MqttHandler? _mqttHandler;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _topicPrefix;
    private readonly Dictionary<int, ServerMqttState> _serverStates = new();
    private readonly object _lock = new();
    private bool _isEnabled;

    public bool IsConnected => _mqttHandler?.IsConnected ?? false;

    public MqttStatusPublisher(
        ILogger<MqttStatusPublisher> logger,
        HoNConfiguration config,
        MqttHandler? mqttHandler = null)
    {
        _logger = logger;
        _config = config;
        _mqttHandler = mqttHandler;
        _topicPrefix = config.ApplicationData?.Mqtt?.TopicPrefix ?? "honfigurator";
        _isEnabled = config.ApplicationData?.Mqtt?.Enabled ?? false;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Initialize and connect to MQTT broker
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_isEnabled || _mqttHandler == null)
        {
            _logger.LogDebug("MQTT status publishing is disabled");
            return;
        }

        try
        {
            // MqttHandler should already be connected via DI
            if (!_mqttHandler.IsConnected)
            {
                _logger.LogWarning("MQTT handler is not connected");
                return;
            }

            // Publish initial status
            await PublishManagerStatusAsync("online", cancellationToken);
            
            _logger.LogInformation("MQTT status publisher initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MQTT status publisher");
        }
    }

    /// <summary>
    /// Publish manager online/offline status
    /// </summary>
    public async Task PublishManagerStatusAsync(string status, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            status,
            timestamp = DateTime.UtcNow,
            version = _config.HonData?.ManVersion ?? "unknown",
            serverCount = GetServerCount()
        };

        await PublishAsync($"{_topicPrefix}/manager/status", payload, retain: true, cancellationToken);
    }

    /// <summary>
    /// Publish server status update
    /// </summary>
    public async Task PublishServerStatusAsync(int serverId, MqttServerStatus status, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        lock (_lock)
        {
            if (!_serverStates.TryGetValue(serverId, out var state))
            {
                state = new ServerMqttState { ServerId = serverId };
                _serverStates[serverId] = state;
            }
            state.Status = status;
            state.LastUpdate = DateTime.UtcNow;
        }

        var payload = new
        {
            serverId,
            status = status.ToString(),
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/status", payload, retain: true, cancellationToken);
    }

    /// <summary>
    /// Publish game state update (lobby, picking, playing, etc.)
    /// </summary>
    public async Task PublishGameStateAsync(int serverId, GameState gameState, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            state = gameState.ToString(),
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/game/state", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish match info
    /// </summary>
    public async Task PublishMatchInfoAsync(int serverId, MatchMqttInfo matchInfo, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        lock (_lock)
        {
            if (_serverStates.TryGetValue(serverId, out var state))
            {
                state.CurrentMatch = matchInfo;
            }
        }

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/match", matchInfo, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish player joined event
    /// </summary>
    public async Task PublishPlayerJoinedAsync(int serverId, string playerName, int accountId, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            eventType = "player_joined",
            playerName,
            accountId,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/events/player", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish player left event
    /// </summary>
    public async Task PublishPlayerLeftAsync(int serverId, string playerName, int accountId, string reason, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            eventType = "player_left",
            playerName,
            accountId,
            reason,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/events/player", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish match started event
    /// </summary>
    public async Task PublishMatchStartedAsync(int serverId, int matchId, string mapName, string gameMode, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            eventType = "match_started",
            matchId,
            mapName,
            gameMode,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/events/match", payload, cancellationToken: cancellationToken);
        await PublishAsync($"{_topicPrefix}/events/match_started", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish match ended event
    /// </summary>
    public async Task PublishMatchEndedAsync(int serverId, int matchId, string winner, int duration, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            eventType = "match_ended",
            matchId,
            winner,
            durationSeconds = duration,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/events/match", payload, cancellationToken: cancellationToken);
        await PublishAsync($"{_topicPrefix}/events/match_ended", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish server metrics
    /// </summary>
    public async Task PublishServerMetricsAsync(int serverId, ServerMetrics metrics, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            serverId,
            cpu = metrics.CpuUsage,
            memory = metrics.MemoryUsageMb,
            players = metrics.PlayerCount,
            ping = metrics.AveragePing,
            uptime = metrics.UptimeSeconds,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/servers/{serverId}/metrics", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publish aggregate server statistics
    /// </summary>
    public async Task PublishAggregateStatsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        int totalServers, onlineServers, playersOnline, matchesInProgress;
        
        lock (_lock)
        {
            totalServers = _serverStates.Count;
            onlineServers = _serverStates.Values.Count(s => s.Status == MqttServerStatus.Online);
            playersOnline = _serverStates.Values
                .Where(s => s.CurrentMatch != null)
                .Sum(s => s.CurrentMatch!.PlayerCount);
            matchesInProgress = _serverStates.Values
                .Count(s => s.CurrentMatch?.IsInProgress == true);
        }

        var payload = new
        {
            totalServers,
            onlineServers,
            playersOnline,
            matchesInProgress,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/stats/aggregate", payload, retain: true, cancellationToken);
    }

    /// <summary>
    /// Publish alert/notification
    /// </summary>
    public async Task PublishAlertAsync(string alertType, string message, string severity = "info", CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;

        var payload = new
        {
            alertType,
            message,
            severity,
            timestamp = DateTime.UtcNow
        };

        await PublishAsync($"{_topicPrefix}/alerts/{severity}", payload, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Remove server from tracking
    /// </summary>
    public void RemoveServer(int serverId)
    {
        lock (_lock)
        {
            _serverStates.Remove(serverId);
        }
    }

    /// <summary>
    /// Get current state of all tracked servers
    /// </summary>
    public IReadOnlyDictionary<int, ServerMqttState> GetServerStates()
    {
        lock (_lock)
        {
            return _serverStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    private bool CanPublish()
    {
        return _isEnabled && _mqttHandler?.IsConnected == true;
    }

    private int GetServerCount()
    {
        lock (_lock)
        {
            return _serverStates.Count;
        }
    }

    private async Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        if (_mqttHandler == null) return;

        try
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            await _mqttHandler.PublishAsync(topic, json, retain);
            
            _logger.LogDebug("Published to {Topic}: {Payload}", topic, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish to MQTT topic: {Topic}", topic);
        }
    }

    public void Dispose()
    {
        // Publish offline status before disposing
        try
        {
            PublishManagerStatusAsync("offline").GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore errors during dispose
        }
    }
}

// Enums and DTOs

public enum MqttServerStatus
{
    Unknown,
    Starting,
    Online,
    InGame,
    Stopping,
    Offline,
    Error
}

public enum GameState
{
    Idle,
    Lobby,
    Picking,
    Loading,
    Playing,
    Ended
}

public class ServerMqttState
{
    public int ServerId { get; set; }
    public MqttServerStatus Status { get; set; }
    public MatchMqttInfo? CurrentMatch { get; set; }
    public DateTime LastUpdate { get; set; }
}

public class MatchMqttInfo
{
    public int MatchId { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string GameMode { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public bool IsInProgress { get; set; }
    public DateTime? StartTime { get; set; }
}

public class ServerMetrics
{
    public double CpuUsage { get; set; }
    public long MemoryUsageMb { get; set; }
    public int PlayerCount { get; set; }
    public int AveragePing { get; set; }
    public long UptimeSeconds { get; set; }
}
