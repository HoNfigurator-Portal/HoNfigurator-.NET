using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Player session information
/// </summary>
public record PlayerSession
{
    public int AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string Cookie { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public bool IsAuthenticated { get; set; }
    public int? CurrentMatchId { get; set; }
    public int? Team { get; set; }
    public int? Slot { get; set; }
    
    // Statistics for this session
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int CreepKills { get; set; }
    public int CreepDenies { get; set; }
    public int GoldEarned { get; set; }
    public int ExperienceEarned { get; set; }
}

/// <summary>
/// Player authentication result from master server
/// </summary>
public record PlayerAuthResult
{
    public bool Success { get; init; }
    public int AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool IsBanned { get; init; }
    public bool IsLeaver { get; init; }
    public double LeaverPercent { get; init; }
}

/// <summary>
/// Service for managing player sessions on a game server.
/// Tracks connected players, authentication status, and session data.
/// </summary>
public interface IPlayerSessionService
{
    /// <summary>Number of currently connected players</summary>
    int PlayerCount { get; }
    
    /// <summary>Gets all active player sessions</summary>
    IReadOnlyList<PlayerSession> GetAllSessions();
    
    /// <summary>Gets a player session by account ID</summary>
    PlayerSession? GetSession(int accountId);
    
    /// <summary>Gets a player session by cookie</summary>
    PlayerSession? GetSessionByCookie(string cookie);
    
    /// <summary>Creates a new player session when they connect</summary>
    PlayerSession CreateSession(int accountId, string accountName, string cookie, string ipAddress);
    
    /// <summary>Authenticates a player session with the master server</summary>
    Task<PlayerAuthResult> AuthenticatePlayerAsync(int accountId, string cookie, CancellationToken cancellationToken = default);
    
    /// <summary>Updates a player's last activity timestamp</summary>
    void UpdateActivity(int accountId);
    
    /// <summary>Assigns a player to a team and slot</summary>
    void AssignToMatch(int accountId, int matchId, int team, int slot);
    
    /// <summary>Updates player statistics</summary>
    void UpdateStats(int accountId, Action<PlayerSession> updateAction);
    
    /// <summary>Removes a player session when they disconnect</summary>
    void RemoveSession(int accountId);
    
    /// <summary>Removes all sessions (e.g., when match ends)</summary>
    void ClearAllSessions();
    
    /// <summary>Gets players who haven't been active for the specified duration</summary>
    IReadOnlyList<PlayerSession> GetInactivePlayers(TimeSpan inactivityThreshold);
}

public class PlayerSessionService : IPlayerSessionService
{
    private readonly ILogger<PlayerSessionService> _logger;
    private readonly Dictionary<int, PlayerSession> _sessions = new();
    private readonly Dictionary<string, int> _cookieToAccountId = new();
    private readonly object _lock = new();

    // Events
    public event Action<PlayerSession>? OnSessionCreated;
    public event Action<PlayerSession>? OnSessionAuthenticated;
    public event Action<PlayerSession>? OnSessionRemoved;
    public event Action<PlayerSession>? OnStatsUpdated;

    public int PlayerCount
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    public PlayerSessionService(ILogger<PlayerSessionService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<PlayerSession> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.ToList().AsReadOnly();
        }
    }

    public PlayerSession? GetSession(int accountId)
    {
        lock (_lock)
        {
            return _sessions.GetValueOrDefault(accountId);
        }
    }

    public PlayerSession? GetSessionByCookie(string cookie)
    {
        lock (_lock)
        {
            if (_cookieToAccountId.TryGetValue(cookie, out var accountId))
            {
                return _sessions.GetValueOrDefault(accountId);
            }
            return null;
        }
    }

    public PlayerSession CreateSession(int accountId, string accountName, string cookie, string ipAddress)
    {
        lock (_lock)
        {
            // Remove existing session if any
            if (_sessions.TryGetValue(accountId, out var existingSession))
            {
                _logger.LogDebug("Removing existing session for player {AccountName} ({AccountId})", accountName, accountId);
                if (!string.IsNullOrEmpty(existingSession.Cookie))
                {
                    _cookieToAccountId.Remove(existingSession.Cookie);
                }
                _sessions.Remove(accountId);
            }

            var session = new PlayerSession
            {
                AccountId = accountId,
                AccountName = accountName,
                Cookie = cookie,
                IpAddress = ipAddress,
                ConnectedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                IsAuthenticated = false
            };

            _sessions[accountId] = session;
            if (!string.IsNullOrEmpty(cookie))
            {
                _cookieToAccountId[cookie] = accountId;
            }

            _logger.LogInformation("Created session for player {AccountName} ({AccountId}) from {IpAddress}",
                accountName, accountId, ipAddress);

            OnSessionCreated?.Invoke(session);
            return session;
        }
    }

    public async Task<PlayerAuthResult> AuthenticatePlayerAsync(int accountId, string cookie, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would validate with the master server
        // For now, we'll simulate authentication
        
        PlayerSession? session;
        lock (_lock)
        {
            session = _sessions.GetValueOrDefault(accountId);
        }

        if (session == null)
        {
            return new PlayerAuthResult
            {
                Success = false,
                AccountId = accountId,
                Error = "Session not found"
            };
        }

        // Validate cookie matches
        if (session.Cookie != cookie)
        {
            _logger.LogWarning("Cookie mismatch for player {AccountId}: expected {Expected}, got {Actual}",
                accountId, session.Cookie, cookie);
            
            return new PlayerAuthResult
            {
                Success = false,
                AccountId = accountId,
                Error = "Invalid cookie"
            };
        }

        // TODO: Call master server to validate:
        // POST /server_requester.php?f=c_conn
        // Form: cookie={cookie}, account_id={accountId}
        // Response includes: success, banned status, leaver status, etc.

        // For now, assume success
        lock (_lock)
        {
            session.IsAuthenticated = true;
        }

        _logger.LogInformation("Player {AccountName} ({AccountId}) authenticated successfully", session.AccountName, accountId);

        OnSessionAuthenticated?.Invoke(session);

        return new PlayerAuthResult
        {
            Success = true,
            AccountId = accountId,
            AccountName = session.AccountName
        };
    }

    public void UpdateActivity(int accountId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(accountId, out var session))
            {
                session.LastActivityAt = DateTime.UtcNow;
            }
        }
    }

    public void AssignToMatch(int accountId, int matchId, int team, int slot)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(accountId, out var session))
            {
                session.CurrentMatchId = matchId;
                session.Team = team;
                session.Slot = slot;
                
                _logger.LogDebug("Assigned player {AccountName} ({AccountId}) to match {MatchId}, Team {Team}, Slot {Slot}",
                    session.AccountName, accountId, matchId, team, slot);
            }
        }
    }

    public void UpdateStats(int accountId, Action<PlayerSession> updateAction)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(accountId, out var session))
            {
                updateAction(session);
                session.LastActivityAt = DateTime.UtcNow;
                OnStatsUpdated?.Invoke(session);
            }
        }
    }

    public void RemoveSession(int accountId)
    {
        PlayerSession? session;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(accountId, out session))
            {
                return;
            }

            if (!string.IsNullOrEmpty(session.Cookie))
            {
                _cookieToAccountId.Remove(session.Cookie);
            }
            _sessions.Remove(accountId);
        }

        _logger.LogInformation("Removed session for player {AccountName} ({AccountId})", session.AccountName, accountId);
        OnSessionRemoved?.Invoke(session);
    }

    public void ClearAllSessions()
    {
        List<PlayerSession> sessions;
        lock (_lock)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
            _cookieToAccountId.Clear();
        }

        _logger.LogInformation("Cleared all {Count} player sessions", sessions.Count);

        foreach (var session in sessions)
        {
            OnSessionRemoved?.Invoke(session);
        }
    }

    public IReadOnlyList<PlayerSession> GetInactivePlayers(TimeSpan inactivityThreshold)
    {
        var threshold = DateTime.UtcNow - inactivityThreshold;
        
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => s.LastActivityAt < threshold)
                .ToList()
                .AsReadOnly();
        }
    }
}

/// <summary>
/// Extension methods for player sessions
/// </summary>
public static class PlayerSessionExtensions
{
    /// <summary>
    /// Gets the duration the player has been connected
    /// </summary>
    public static TimeSpan GetSessionDuration(this PlayerSession session)
    {
        return DateTime.UtcNow - session.ConnectedAt;
    }

    /// <summary>
    /// Gets the player's KDA ratio
    /// </summary>
    public static double GetKda(this PlayerSession session)
    {
        if (session.Deaths == 0) return session.Kills + session.Assists;
        return (double)(session.Kills + session.Assists) / session.Deaths;
    }

    /// <summary>
    /// Gets creep score (CS)
    /// </summary>
    public static int GetCreepScore(this PlayerSession session)
    {
        return session.CreepKills + session.CreepDenies;
    }

    /// <summary>
    /// Creates a summary string for the player
    /// </summary>
    public static string ToSummaryString(this PlayerSession session)
    {
        return $"{session.AccountName} ({session.AccountId}): {session.Kills}/{session.Deaths}/{session.Assists} - CS: {session.GetCreepScore()}";
    }
}
