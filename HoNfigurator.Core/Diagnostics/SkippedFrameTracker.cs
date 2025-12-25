using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Diagnostics;

/// <summary>
/// Tracks and analyzes skipped frames (lag) for game servers.
/// Port of Python HoNfigurator-Central skipped frame analytics.
/// </summary>
public class SkippedFrameTracker
{
    private readonly ILogger<SkippedFrameTracker> _logger;
    private readonly ConcurrentDictionary<int, ServerFrameData> _serverData = new();
    private readonly int _maxHistoryPerServer;
    private readonly TimeSpan _dataRetention;

    public SkippedFrameTracker(ILogger<SkippedFrameTracker> logger, int maxHistoryPerServer = 1000, 
        TimeSpan? dataRetention = null)
    {
        _logger = logger;
        _maxHistoryPerServer = maxHistoryPerServer;
        _dataRetention = dataRetention ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Record a skipped frame event
    /// </summary>
    public void RecordSkippedFrame(int serverId, int port, int skippedMs, int? playerId = null, 
        string? playerName = null)
    {
        var data = _serverData.GetOrAdd(serverId, _ => new ServerFrameData(serverId, port));
        
        var entry = new SkippedFrameEntry
        {
            Timestamp = DateTime.UtcNow,
            SkippedMs = skippedMs,
            PlayerId = playerId,
            PlayerName = playerName
        };

        lock (data.Lock)
        {
            data.Entries.Add(entry);
            data.TotalSkippedFrames++;
            data.TotalSkippedMs += skippedMs;
            
            if (skippedMs > data.MaxSkippedMs)
                data.MaxSkippedMs = skippedMs;

            // Track per-player stats if player info provided
            if (playerId.HasValue)
            {
                if (!data.PlayerStats.TryGetValue(playerId.Value, out var playerStats))
                {
                    playerStats = new PlayerFrameStats { PlayerId = playerId.Value, PlayerName = playerName };
                    data.PlayerStats[playerId.Value] = playerStats;
                }
                playerStats.SkippedFrameCount++;
                playerStats.TotalSkippedMs += skippedMs;
                if (skippedMs > playerStats.MaxSkippedMs)
                    playerStats.MaxSkippedMs = skippedMs;
                playerStats.LastSkippedAt = DateTime.UtcNow;
            }

            // Trim old entries
            CleanupOldEntries(data);
        }
    }

    /// <summary>
    /// Get skipped frame data for a specific server
    /// </summary>
    public ServerFrameAnalytics? GetServerAnalytics(int serverId)
    {
        if (!_serverData.TryGetValue(serverId, out var data))
            return null;

        lock (data.Lock)
        {
            CleanupOldEntries(data);
            return CalculateAnalytics(data);
        }
    }

    /// <summary>
    /// Get skipped frame data for a specific port
    /// </summary>
    public ServerFrameAnalytics? GetServerAnalyticsByPort(int port)
    {
        var server = _serverData.Values.FirstOrDefault(s => s.Port == port);
        if (server == null) return null;
        
        return GetServerAnalytics(server.ServerId);
    }

    /// <summary>
    /// Get skipped frame data for all servers
    /// </summary>
    public List<ServerFrameAnalytics> GetAllServerAnalytics()
    {
        var result = new List<ServerFrameAnalytics>();
        
        foreach (var kvp in _serverData)
        {
            var analytics = GetServerAnalytics(kvp.Key);
            if (analytics != null)
                result.Add(analytics);
        }
        
        return result.OrderBy(a => a.ServerId).ToList();
    }

    /// <summary>
    /// Get recent skipped frame entries for a server
    /// </summary>
    public List<SkippedFrameEntry> GetRecentEntries(int serverId, int count = 100, 
        TimeSpan? timeWindow = null)
    {
        if (!_serverData.TryGetValue(serverId, out var data))
            return new List<SkippedFrameEntry>();

        lock (data.Lock)
        {
            var query = data.Entries.AsEnumerable();
            
            if (timeWindow.HasValue)
            {
                var cutoff = DateTime.UtcNow - timeWindow.Value;
                query = query.Where(e => e.Timestamp >= cutoff);
            }
            
            return query
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Get player-specific lag stats for a server
    /// </summary>
    public List<PlayerFrameStats> GetPlayerStats(int serverId)
    {
        if (!_serverData.TryGetValue(serverId, out var data))
            return new List<PlayerFrameStats>();

        lock (data.Lock)
        {
            return data.PlayerStats.Values
                .OrderByDescending(p => p.TotalSkippedMs)
                .ToList();
        }
    }

    /// <summary>
    /// Get aggregated statistics across all servers
    /// </summary>
    public GlobalFrameAnalytics GetGlobalAnalytics()
    {
        var allAnalytics = GetAllServerAnalytics();
        
        return new GlobalFrameAnalytics
        {
            TotalServers = allAnalytics.Count,
            TotalSkippedFrames = allAnalytics.Sum(a => a.TotalSkippedFrames),
            TotalSkippedMs = allAnalytics.Sum(a => a.TotalSkippedMs),
            MaxSkippedMs = allAnalytics.Max(a => (int?)a.MaxSkippedMs) ?? 0,
            AverageSkippedMs = allAnalytics.Count > 0 
                ? allAnalytics.Average(a => a.AverageSkippedMs) 
                : 0,
            ServersWithLag = allAnalytics.Count(a => a.TotalSkippedFrames > 0),
            LastUpdated = DateTime.UtcNow,
            ByServer = allAnalytics
        };
    }

    /// <summary>
    /// Clear data for a specific server
    /// </summary>
    public void ClearServerData(int serverId)
    {
        _serverData.TryRemove(serverId, out _);
    }

    /// <summary>
    /// Clear all data
    /// </summary>
    public void ClearAllData()
    {
        _serverData.Clear();
    }

    private ServerFrameAnalytics CalculateAnalytics(ServerFrameData data)
    {
        var now = DateTime.UtcNow;
        var lastHour = data.Entries.Where(e => e.Timestamp >= now.AddHours(-1)).ToList();
        var last5Min = data.Entries.Where(e => e.Timestamp >= now.AddMinutes(-5)).ToList();

        return new ServerFrameAnalytics
        {
            ServerId = data.ServerId,
            Port = data.Port,
            TotalSkippedFrames = data.TotalSkippedFrames,
            TotalSkippedMs = data.TotalSkippedMs,
            MaxSkippedMs = data.MaxSkippedMs,
            AverageSkippedMs = data.TotalSkippedFrames > 0 
                ? (double)data.TotalSkippedMs / data.TotalSkippedFrames 
                : 0,
            SkippedFramesLastHour = lastHour.Count,
            SkippedMsLastHour = lastHour.Sum(e => e.SkippedMs),
            SkippedFramesLast5Min = last5Min.Count,
            SkippedMsLast5Min = last5Min.Sum(e => e.SkippedMs),
            FirstRecorded = data.Entries.Min(e => (DateTime?)e.Timestamp),
            LastRecorded = data.Entries.Max(e => (DateTime?)e.Timestamp),
            UniquePlayersAffected = data.PlayerStats.Count,
            TopLaggyPlayers = data.PlayerStats.Values
                .OrderByDescending(p => p.TotalSkippedMs)
                .Take(5)
                .ToList()
        };
    }

    private void CleanupOldEntries(ServerFrameData data)
    {
        var cutoff = DateTime.UtcNow - _dataRetention;
        
        // Remove old entries
        data.Entries.RemoveAll(e => e.Timestamp < cutoff);
        
        // Trim to max size if needed
        while (data.Entries.Count > _maxHistoryPerServer)
        {
            data.Entries.RemoveAt(0);
        }
    }

    private class ServerFrameData
    {
        public int ServerId { get; }
        public int Port { get; }
        public List<SkippedFrameEntry> Entries { get; } = new();
        public Dictionary<int, PlayerFrameStats> PlayerStats { get; } = new();
        public long TotalSkippedFrames { get; set; }
        public long TotalSkippedMs { get; set; }
        public int MaxSkippedMs { get; set; }
        public object Lock { get; } = new();

        public ServerFrameData(int serverId, int port)
        {
            ServerId = serverId;
            Port = port;
        }
    }
}

#region Models

public class SkippedFrameEntry
{
    public DateTime Timestamp { get; set; }
    public int SkippedMs { get; set; }
    public int? PlayerId { get; set; }
    public string? PlayerName { get; set; }
}

public class PlayerFrameStats
{
    public int PlayerId { get; set; }
    public string? PlayerName { get; set; }
    public int SkippedFrameCount { get; set; }
    public long TotalSkippedMs { get; set; }
    public int MaxSkippedMs { get; set; }
    public double AverageSkippedMs => SkippedFrameCount > 0 ? (double)TotalSkippedMs / SkippedFrameCount : 0;
    public DateTime? LastSkippedAt { get; set; }
}

public class ServerFrameAnalytics
{
    public int ServerId { get; set; }
    public int Port { get; set; }
    public long TotalSkippedFrames { get; set; }
    public long TotalSkippedMs { get; set; }
    public int MaxSkippedMs { get; set; }
    public double AverageSkippedMs { get; set; }
    public int SkippedFramesLastHour { get; set; }
    public long SkippedMsLastHour { get; set; }
    public int SkippedFramesLast5Min { get; set; }
    public long SkippedMsLast5Min { get; set; }
    public DateTime? FirstRecorded { get; set; }
    public DateTime? LastRecorded { get; set; }
    public int UniquePlayersAffected { get; set; }
    public List<PlayerFrameStats> TopLaggyPlayers { get; set; } = new();
}

public class GlobalFrameAnalytics
{
    public int TotalServers { get; set; }
    public long TotalSkippedFrames { get; set; }
    public long TotalSkippedMs { get; set; }
    public int MaxSkippedMs { get; set; }
    public double AverageSkippedMs { get; set; }
    public int ServersWithLag { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<ServerFrameAnalytics> ByServer { get; set; } = new();
}

#endregion
