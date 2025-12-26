using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Connectors;

namespace HoNfigurator.Core.Events;

/// <summary>
/// Handles logging of game events
/// </summary>
public class LoggingEventHandler : IGameEventHandler
{
    private readonly ILogger<LoggingEventHandler> _logger;

    public LoggingEventHandler(ILogger<LoggingEventHandler> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(GameEventType eventType) => true;

    public Task HandleAsync(GameEvent gameEvent)
    {
        var logLevel = GetLogLevel(gameEvent.EventType);
        _logger.Log(logLevel, "[Server {ServerId}] {EventType}: {Data}",
            gameEvent.ServerId, gameEvent.EventType,
            string.Join(", ", gameEvent.Data.Select(kv => $"{kv.Key}={kv.Value}")));
        return Task.CompletedTask;
    }

    private LogLevel GetLogLevel(GameEventType eventType)
    {
        return eventType switch
        {
            GameEventType.ServerCrashed => LogLevel.Error,
            GameEventType.HealthCheckFailed => LogLevel.Warning,
            GameEventType.ResourceWarning => LogLevel.Warning,
            GameEventType.PlayerBanned => LogLevel.Warning,
            GameEventType.PlayerKicked => LogLevel.Warning,
            _ => LogLevel.Information
        };
    }
}

/// <summary>
/// Handles match statistics collection
/// </summary>
public class MatchStatsHandler : IGameEventHandler
{
    private readonly ILogger<MatchStatsHandler> _logger;
    private readonly List<MatchStats> _recentMatches = new();
    private readonly object _lock = new();

    public MatchStatsHandler(ILogger<MatchStatsHandler> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(GameEventType eventType) =>
        eventType is GameEventType.MatchStarted or GameEventType.MatchEnded or GameEventType.MatchAborted;

    public Task HandleAsync(GameEvent gameEvent)
    {
        lock (_lock)
        {
            switch (gameEvent.EventType)
            {
                case GameEventType.MatchStarted:
                    var matchId = gameEvent.GetData<long>("matchId");
                    _recentMatches.Add(new MatchStats
                    {
                        MatchId = matchId,
                        ServerId = gameEvent.ServerId,
                        StartTime = gameEvent.Timestamp,
                        GameMode = gameEvent.GetData<string>("gameMode") ?? "Unknown"
                    });
                    break;

                case GameEventType.MatchEnded:
                    var endedMatchId = gameEvent.GetData<long>("matchId");
                    var match = _recentMatches.FirstOrDefault(m => m.MatchId == endedMatchId);
                    if (match != null)
                    {
                        match.EndTime = gameEvent.Timestamp;
                        match.Duration = match.EndTime.Value - match.StartTime;
                        match.Winner = gameEvent.GetData<string>("winner");
                        _logger.LogInformation("Match {MatchId} ended. Duration: {Duration}", endedMatchId, match.Duration);
                    }
                    break;

                case GameEventType.MatchAborted:
                    var abortedMatchId = gameEvent.GetData<long>("matchId");
                    var abortedMatch = _recentMatches.FirstOrDefault(m => m.MatchId == abortedMatchId);
                    if (abortedMatch != null)
                    {
                        abortedMatch.WasAborted = true;
                        abortedMatch.EndTime = gameEvent.Timestamp;
                    }
                    break;
            }

            while (_recentMatches.Count > 100)
                _recentMatches.RemoveAt(0);
        }
        return Task.CompletedTask;
    }

    public List<MatchStats> GetRecentMatches()
    {
        lock (_lock) { return _recentMatches.ToList(); }
    }
}

public class MatchStats
{
    public long MatchId { get; set; }
    public int ServerId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public string GameMode { get; set; } = string.Empty;
    public string? Winner { get; set; }
    public bool WasAborted { get; set; }
}

/// <summary>
/// Handles publishing game events to MQTT broker
/// Automatically publishes match and player events to management portal
/// </summary>
public class MqttEventHandler : IGameEventHandler
{
    private readonly ILogger<MqttEventHandler> _logger;
    private readonly IMqttHandler _mqttHandler;
    
    // Event types to publish
    private static readonly HashSet<GameEventType> PublishableEvents = new()
    {
        // Server events
        GameEventType.ServerStarted,
        GameEventType.ServerStopped,
        GameEventType.ServerCrashed,
        GameEventType.ServerRestarted,
        
        // Match events
        GameEventType.MatchStarted,
        GameEventType.MatchEnded,
        GameEventType.MatchAborted,
        
        // Player events
        GameEventType.PlayerConnected,
        GameEventType.PlayerDisconnected,
        GameEventType.PlayerKicked,
        GameEventType.PlayerBanned,
        
        // Important game events
        GameEventType.FirstBlood,
        GameEventType.KongorKilled
    };

    public MqttEventHandler(ILogger<MqttEventHandler> logger, IMqttHandler mqttHandler)
    {
        _logger = logger;
        _mqttHandler = mqttHandler;
    }

    public bool CanHandle(GameEventType eventType) => 
        _mqttHandler.IsEnabled && _mqttHandler.IsConnected && PublishableEvents.Contains(eventType);

    public async Task HandleAsync(GameEvent gameEvent)
    {
        try
        {
            var eventCategory = GetEventCategory(gameEvent.EventType);
            
            switch (eventCategory)
            {
                case EventCategory.Server:
                    await PublishServerEventAsync(gameEvent);
                    break;
                    
                case EventCategory.Match:
                    await PublishMatchEventAsync(gameEvent);
                    break;
                    
                case EventCategory.Player:
                    await PublishPlayerEventAsync(gameEvent);
                    break;
                    
                case EventCategory.GamePlay:
                    await PublishGamePlayEventAsync(gameEvent);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish event {EventType} to MQTT", gameEvent.EventType);
        }
    }

    private async Task PublishServerEventAsync(GameEvent gameEvent)
    {
        var status = gameEvent.EventType switch
        {
            GameEventType.ServerStarted => MqttEventTypes.ServerReady,
            GameEventType.ServerStopped => MqttEventTypes.ServerOffline,
            GameEventType.ServerCrashed => MqttEventTypes.ServerOffline,
            GameEventType.ServerRestarted => MqttEventTypes.ServerReady,
            _ => gameEvent.EventType.ToString().ToLower()
        };
        
        await _mqttHandler.PublishServerStatusAsync(
            gameEvent.ServerId,
            status,
            new
            {
                EventId = gameEvent.Id,
                OriginalEventType = gameEvent.EventType.ToString(),
                gameEvent.Data
            });
            
        _logger.LogDebug("Published server event {EventType} for server {ServerId} to MQTT", 
            gameEvent.EventType, gameEvent.ServerId);
    }

    private async Task PublishMatchEventAsync(GameEvent gameEvent)
    {
        var eventType = gameEvent.EventType switch
        {
            GameEventType.MatchStarted => MqttEventTypes.MatchStarted,
            GameEventType.MatchEnded => MqttEventTypes.MatchEnded,
            GameEventType.MatchAborted => "match_aborted",
            _ => gameEvent.EventType.ToString().ToLower()
        };
        
        await _mqttHandler.PublishMatchEventAsync(
            gameEvent.ServerId,
            eventType,
            new
            {
                EventId = gameEvent.Id,
                MatchId = gameEvent.GetData<long>("matchId"),
                GameMode = gameEvent.GetData<string>("gameMode"),
                PlayerCount = gameEvent.GetData<int>("playerCount"),
                Winner = gameEvent.GetData<string>("winner"),
                Duration = gameEvent.GetData<TimeSpan?>("duration"),
                gameEvent.Data
            });
            
        _logger.LogDebug("Published match event {EventType} for server {ServerId} to MQTT", 
            gameEvent.EventType, gameEvent.ServerId);
    }

    private async Task PublishPlayerEventAsync(GameEvent gameEvent)
    {
        var playerName = gameEvent.GetData<string>("playerName") ?? "Unknown";
        var eventType = gameEvent.EventType switch
        {
            GameEventType.PlayerConnected => MqttEventTypes.PlayerJoined,
            GameEventType.PlayerDisconnected => MqttEventTypes.PlayerLeft,
            GameEventType.PlayerKicked => MqttEventTypes.PlayerKicked,
            GameEventType.PlayerBanned => "player_banned",
            _ => gameEvent.EventType.ToString().ToLower()
        };
        
        await _mqttHandler.PublishPlayerEventAsync(
            gameEvent.ServerId,
            eventType,
            playerName,
            new
            {
                EventId = gameEvent.Id,
                AccountId = gameEvent.GetData<int>("accountId"),
                Reason = gameEvent.GetData<string>("reason"),
                gameEvent.Data
            });
            
        _logger.LogDebug("Published player event {EventType} for player {Player} on server {ServerId} to MQTT", 
            gameEvent.EventType, playerName, gameEvent.ServerId);
    }

    private async Task PublishGamePlayEventAsync(GameEvent gameEvent)
    {
        var eventType = gameEvent.EventType switch
        {
            GameEventType.FirstBlood => "first_blood",
            GameEventType.KongorKilled => "kongor_killed",
            _ => gameEvent.EventType.ToString().ToLower()
        };
        
        await _mqttHandler.PublishMatchEventAsync(
            gameEvent.ServerId,
            eventType,
            new
            {
                EventId = gameEvent.Id,
                MatchId = gameEvent.GetData<long>("matchId"),
                gameEvent.Data
            });
            
        _logger.LogDebug("Published gameplay event {EventType} for server {ServerId} to MQTT", 
            gameEvent.EventType, gameEvent.ServerId);
    }

    private static EventCategory GetEventCategory(GameEventType eventType)
    {
        return eventType switch
        {
            GameEventType.ServerStarted or 
            GameEventType.ServerStopped or 
            GameEventType.ServerCrashed or 
            GameEventType.ServerRestarted => EventCategory.Server,
            
            GameEventType.MatchStarted or 
            GameEventType.MatchEnded or 
            GameEventType.MatchAborted => EventCategory.Match,
            
            GameEventType.PlayerConnected or 
            GameEventType.PlayerDisconnected or 
            GameEventType.PlayerKicked or 
            GameEventType.PlayerBanned => EventCategory.Player,
            
            GameEventType.FirstBlood or 
            GameEventType.KongorKilled => EventCategory.GamePlay,
            
            _ => EventCategory.Other
        };
    }

    private enum EventCategory
    {
        Server,
        Match,
        Player,
        GamePlay,
        Other
    }
}

/// <summary>
/// Handles sending notifications for important events via NotificationService
/// </summary>
public class NotificationEventHandler : IGameEventHandler
{
    private readonly ILogger<NotificationEventHandler> _logger;
    
    // Events that trigger notifications
    private static readonly HashSet<GameEventType> NotifiableEvents = new()
    {
        GameEventType.ServerCrashed,
        GameEventType.HealthCheckFailed,
        GameEventType.ResourceWarning,
        GameEventType.PlayerBanned,
        GameEventType.MatchAborted
    };

    public NotificationEventHandler(ILogger<NotificationEventHandler> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(GameEventType eventType) => NotifiableEvents.Contains(eventType);

    public Task HandleAsync(GameEvent gameEvent)
    {
        // Generate notification based on event type
        var (title, message, severity) = GenerateNotification(gameEvent);
        
        _logger.LogInformation("Notification: [{Severity}] {Title}: {Message}", 
            severity, title, message);
        
        // In a real implementation, this would call NotificationService
        // _notificationService.SendAsync(title, message, severity);
        
        return Task.CompletedTask;
    }

    private (string Title, string Message, string Severity) GenerateNotification(GameEvent gameEvent)
    {
        return gameEvent.EventType switch
        {
            GameEventType.ServerCrashed => (
                "Server Crashed",
                $"Server {gameEvent.ServerId} has crashed. Check logs for details.",
                "critical"),
                
            GameEventType.HealthCheckFailed => (
                "Health Check Failed",
                $"Server {gameEvent.ServerId} failed health check: {gameEvent.GetData<string>("reason") ?? "Unknown reason"}",
                "warning"),
                
            GameEventType.ResourceWarning => (
                "Resource Warning",
                $"Server {gameEvent.ServerId}: {gameEvent.GetData<string>("resource")} usage at {gameEvent.GetData<int>("percentage")}%",
                "warning"),
                
            GameEventType.PlayerBanned => (
                "Player Banned",
                $"Player {gameEvent.GetData<string>("playerName")} banned on server {gameEvent.ServerId}: {gameEvent.GetData<string>("reason")}",
                "info"),
                
            GameEventType.MatchAborted => (
                "Match Aborted",
                $"Match {gameEvent.GetData<long>("matchId")} aborted on server {gameEvent.ServerId}: {gameEvent.GetData<string>("reason") ?? "Unknown reason"}",
                "warning"),
                
            _ => (
                gameEvent.EventType.ToString(),
                $"Event on server {gameEvent.ServerId}",
                "info")
        };
    }
}
