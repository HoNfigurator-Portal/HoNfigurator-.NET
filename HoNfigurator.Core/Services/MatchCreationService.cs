using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Match state during creation/loading phase
/// </summary>
public enum MatchState
{
    /// <summary>No match is being created</summary>
    None,
    /// <summary>Match is being set up by chat server</summary>
    Creating,
    /// <summary>Waiting for players to connect</summary>
    WaitingForPlayers,
    /// <summary>All players connected, starting game</summary>
    Starting,
    /// <summary>Match is active and in progress</summary>
    Active,
    /// <summary>Match has ended</summary>
    Ended,
    /// <summary>Match was aborted</summary>
    Aborted
}

/// <summary>
/// Information about a player in the current match
/// </summary>
public record MatchPlayer
{
    public int AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Team { get; init; }
    public int Slot { get; init; }
    public bool IsConnected { get; set; }
    public bool IsReady { get; set; }
    public DateTime? ConnectedAt { get; set; }
}

/// <summary>
/// Current match information
/// </summary>
public record MatchInfo
{
    public int MatchId { get; init; }
    public string Map { get; init; } = "caldavar";
    public string GameMode { get; init; } = "normal";
    public ArrangedMatchType MatchType { get; init; }
    public MatchState State { get; set; }
    public List<MatchPlayer> Players { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? WinningTeam { get; set; }
}

/// <summary>
/// Service for managing match creation and lifecycle with the chat server.
/// Handles arranged match flow per NEXUS protocol.
/// </summary>
public interface IMatchCreationService
{
    /// <summary>Current match information (null if no match)</summary>
    MatchInfo? CurrentMatch { get; }
    
    /// <summary>Whether a match is currently in progress</summary>
    bool HasActiveMatch { get; }
    
    /// <summary>Initializes the service with a game server chat connector</summary>
    void Initialize(IGameServerChatConnector connector);
    
    /// <summary>Handles incoming create match request from chat server</summary>
    Task HandleCreateMatchAsync(ArrangedMatchData matchData);
    
    /// <summary>Handles player connecting to the match</summary>
    Task<bool> HandlePlayerConnectAsync(int accountId, string name);
    
    /// <summary>Handles player disconnecting from the match</summary>
    Task HandlePlayerDisconnectAsync(int accountId);
    
    /// <summary>Handles player ready status change</summary>
    Task HandlePlayerReadyAsync(int accountId, bool isReady);
    
    /// <summary>Checks if all players are connected and ready to start</summary>
    bool AreAllPlayersReady();
    
    /// <summary>Starts the match when all players are ready</summary>
    Task StartMatchAsync();
    
    /// <summary>Ends the match with the specified winning team</summary>
    Task EndMatchAsync(int winningTeam);
    
    /// <summary>Aborts the match (players didn't connect, etc.)</summary>
    Task AbortMatchAsync(string reason);
    
    /// <summary>Gets the expected players for the current match</summary>
    IReadOnlyList<MatchPlayer> GetExpectedPlayers();
}

public class MatchCreationService : IMatchCreationService
{
    private readonly ILogger<MatchCreationService> _logger;
    private IGameServerChatConnector? _connector;
    private MatchInfo? _currentMatch;
    private readonly object _lock = new();
    private CancellationTokenSource? _playerTimeoutCts;

    // Configuration
    private readonly TimeSpan _playerConnectTimeout = TimeSpan.FromSeconds(90);
    private readonly TimeSpan _playerReminderInterval = TimeSpan.FromSeconds(30);

    public MatchInfo? CurrentMatch => _currentMatch;
    public bool HasActiveMatch => _currentMatch?.State is MatchState.Creating or MatchState.WaitingForPlayers or MatchState.Starting or MatchState.Active;

    // Events
    public event Action<MatchInfo>? OnMatchCreated;
    public event Action<MatchPlayer>? OnPlayerConnected;
    public event Action<MatchPlayer>? OnPlayerDisconnected;
    public event Action<MatchInfo>? OnMatchStarted;
    public event Action<MatchInfo>? OnMatchEnded;
    public event Action<MatchInfo, string>? OnMatchAborted;
    public event Action<int>? OnPlayerReminder;

    public MatchCreationService(ILogger<MatchCreationService> logger)
    {
        _logger = logger;
    }

    public void Initialize(IGameServerChatConnector connector)
    {
        _connector = connector;
        
        // Subscribe to connector events
        connector.OnCreateMatchRequest += async data => await HandleCreateMatchAsync(data);
        connector.OnEndMatchRequest += async matchId => await AbortMatchAsync($"Chat server requested end of match {matchId}");
    }

    public async Task HandleCreateMatchAsync(ArrangedMatchData matchData)
    {
        lock (_lock)
        {
            if (_currentMatch != null && HasActiveMatch)
            {
                _logger.LogWarning("Received create match request while match {MatchId} is active", _currentMatch.MatchId);
                return;
            }

            var players = new List<MatchPlayer>();
            
            // Add team 1 players
            foreach (var player in matchData.Team1)
            {
                players.Add(new MatchPlayer
                {
                    AccountId = player.AccountId,
                    Name = player.Name,
                    Team = 1,
                    Slot = player.Slot,
                    IsConnected = false,
                    IsReady = false
                });
            }

            // Add team 2 players
            foreach (var player in matchData.Team2)
            {
                players.Add(new MatchPlayer
                {
                    AccountId = player.AccountId,
                    Name = player.Name,
                    Team = 2,
                    Slot = player.Slot,
                    IsConnected = false,
                    IsReady = false
                });
            }

            _currentMatch = new MatchInfo
            {
                MatchId = matchData.MatchId,
                Map = matchData.Map,
                GameMode = matchData.GameMode,
                MatchType = matchData.MatchType,
                State = MatchState.Creating,
                Players = players,
                CreatedAt = DateTime.UtcNow
            };
        }

        _logger.LogInformation("Match {MatchId} created - Map: {Map}, Mode: {Mode}, Players: {PlayerCount}",
            matchData.MatchId, matchData.Map, matchData.GameMode, _currentMatch.Players.Count);

        OnMatchCreated?.Invoke(_currentMatch);

        // Announce match to players
        if (_connector != null)
        {
            var accountIds = _currentMatch.Players.Select(p => p.AccountId).ToList();
            await _connector.SendAnnounceMatchAsync(matchData.MatchId, accountIds);
        }

        // Start waiting for players
        _currentMatch.State = MatchState.WaitingForPlayers;
        StartPlayerTimeoutWatch();
    }

    public async Task<bool> HandlePlayerConnectAsync(int accountId, string name)
    {
        if (_currentMatch == null)
        {
            _logger.LogWarning("Player {AccountId} ({Name}) tried to connect but no match exists", accountId, name);
            return false;
        }

        MatchPlayer? player;
        lock (_lock)
        {
            player = _currentMatch.Players.FirstOrDefault(p => p.AccountId == accountId);
            if (player == null)
            {
                _logger.LogWarning("Player {AccountId} ({Name}) not in expected player list for match {MatchId}",
                    accountId, name, _currentMatch.MatchId);
                return false;
            }

            player.IsConnected = true;
            player.ConnectedAt = DateTime.UtcNow;
        }

        _logger.LogInformation("Player {Name} ({AccountId}) connected to match {MatchId} [Team {Team}, Slot {Slot}]",
            name, accountId, _currentMatch.MatchId, player.Team, player.Slot);

        OnPlayerConnected?.Invoke(player);

        // Report auth result to chat server
        if (_connector != null)
        {
            await _connector.SendClientAuthResultAsync(accountId, true);
        }

        // Check if all players connected
        await CheckAllPlayersConnectedAsync();

        return true;
    }

    public async Task HandlePlayerDisconnectAsync(int accountId)
    {
        if (_currentMatch == null) return;

        MatchPlayer? player;
        lock (_lock)
        {
            player = _currentMatch.Players.FirstOrDefault(p => p.AccountId == accountId);
            if (player == null) return;

            player.IsConnected = false;
            player.IsReady = false;
        }

        _logger.LogInformation("Player {Name} ({AccountId}) disconnected from match {MatchId}",
            player.Name, accountId, _currentMatch.MatchId);

        OnPlayerDisconnected?.Invoke(player);

        // If match was active, might need to handle reconnect logic
        // For now, just log it
    }

    public Task HandlePlayerReadyAsync(int accountId, bool isReady)
    {
        if (_currentMatch == null) return Task.CompletedTask;

        lock (_lock)
        {
            var player = _currentMatch.Players.FirstOrDefault(p => p.AccountId == accountId);
            if (player != null)
            {
                player.IsReady = isReady;
                _logger.LogDebug("Player {Name} ready status: {IsReady}", player.Name, isReady);
            }
        }

        return Task.CompletedTask;
    }

    public bool AreAllPlayersReady()
    {
        if (_currentMatch == null) return false;
        
        lock (_lock)
        {
            return _currentMatch.Players.All(p => p.IsConnected && p.IsReady);
        }
    }

    public async Task StartMatchAsync()
    {
        if (_currentMatch == null)
        {
            throw new InvalidOperationException("No match to start");
        }

        lock (_lock)
        {
            if (_currentMatch.State != MatchState.WaitingForPlayers && _currentMatch.State != MatchState.Starting)
            {
                _logger.LogWarning("Cannot start match in state {State}", _currentMatch.State);
                return;
            }

            _currentMatch.State = MatchState.Starting;
            _currentMatch.StartedAt = DateTime.UtcNow;
        }

        // Stop the timeout watch
        _playerTimeoutCts?.Cancel();

        _logger.LogInformation("Starting match {MatchId}", _currentMatch.MatchId);

        // Notify chat server
        if (_connector != null)
        {
            await _connector.SendMatchStartedAsync(_currentMatch.MatchId);
            await _connector.SendStatusAsync(NexusServerStatus.Active, _currentMatch.Players.Count, _currentMatch.MatchId);
        }

        lock (_lock)
        {
            _currentMatch.State = MatchState.Active;
        }

        OnMatchStarted?.Invoke(_currentMatch);
    }

    public async Task EndMatchAsync(int winningTeam)
    {
        if (_currentMatch == null)
        {
            throw new InvalidOperationException("No match to end");
        }

        lock (_lock)
        {
            _currentMatch.State = MatchState.Ended;
            _currentMatch.EndedAt = DateTime.UtcNow;
            _currentMatch.WinningTeam = winningTeam;
        }

        _logger.LogInformation("Match {MatchId} ended - Winner: Team {WinningTeam}", _currentMatch.MatchId, winningTeam);

        // Notify chat server
        if (_connector != null)
        {
            await _connector.SendMatchEndedAsync(_currentMatch.MatchId, winningTeam);
            await _connector.SendStatusAsync(NexusServerStatus.Idle, 0);
        }

        OnMatchEnded?.Invoke(_currentMatch);

        // Clear match
        var endedMatch = _currentMatch;
        _currentMatch = null;
    }

    public async Task AbortMatchAsync(string reason)
    {
        if (_currentMatch == null) return;

        _playerTimeoutCts?.Cancel();

        lock (_lock)
        {
            _currentMatch.State = MatchState.Aborted;
            _currentMatch.EndedAt = DateTime.UtcNow;
        }

        _logger.LogWarning("Match {MatchId} aborted: {Reason}", _currentMatch.MatchId, reason);

        // Notify chat server
        if (_connector != null)
        {
            await _connector.SendStatusAsync(NexusServerStatus.Idle, 0);
        }

        OnMatchAborted?.Invoke(_currentMatch, reason);

        // Clear match
        _currentMatch = null;
    }

    public IReadOnlyList<MatchPlayer> GetExpectedPlayers()
    {
        if (_currentMatch == null) return Array.Empty<MatchPlayer>();
        
        lock (_lock)
        {
            return _currentMatch.Players.ToList().AsReadOnly();
        }
    }

    private async Task CheckAllPlayersConnectedAsync()
    {
        if (_currentMatch == null) return;

        bool allConnected;
        lock (_lock)
        {
            allConnected = _currentMatch.Players.All(p => p.IsConnected);
        }

        if (allConnected)
        {
            _logger.LogInformation("All players connected for match {MatchId}", _currentMatch.MatchId);
            
            // Auto-start if it's a matchmaking game
            if (_currentMatch.MatchType == ArrangedMatchType.Matchmaking)
            {
                await StartMatchAsync();
            }
        }
    }

    private void StartPlayerTimeoutWatch()
    {
        _playerTimeoutCts?.Cancel();
        _playerTimeoutCts = new CancellationTokenSource();
        
        _ = Task.Run(async () =>
        {
            var startTime = DateTime.UtcNow;
            var lastReminder = DateTime.UtcNow;

            while (!_playerTimeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _playerTimeoutCts.Token);

                if (_currentMatch == null || _currentMatch.State != MatchState.WaitingForPlayers)
                    break;

                var elapsed = DateTime.UtcNow - startTime;

                // Check timeout
                if (elapsed >= _playerConnectTimeout)
                {
                    var missingPlayers = _currentMatch.Players.Where(p => !p.IsConnected).ToList();
                    await AbortMatchAsync($"Players did not connect in time: {string.Join(", ", missingPlayers.Select(p => p.Name))}");
                    break;
                }

                // Send reminders
                if (DateTime.UtcNow - lastReminder >= _playerReminderInterval)
                {
                    lastReminder = DateTime.UtcNow;
                    
                    var missingPlayers = _currentMatch.Players.Where(p => !p.IsConnected).ToList();
                    foreach (var player in missingPlayers)
                    {
                        _logger.LogDebug("Sending reminder to player {Name} ({AccountId})", player.Name, player.AccountId);
                        OnPlayerReminder?.Invoke(player.AccountId);
                        
                        // Could send NET_CHAT_GS_REMIND_PLAYER here
                    }
                }
            }
        }, _playerTimeoutCts.Token);
    }
}
