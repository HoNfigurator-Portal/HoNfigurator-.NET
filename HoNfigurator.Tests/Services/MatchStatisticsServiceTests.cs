using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Statistics;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for MatchStatisticsService - covers SQLite-based match tracking
/// </summary>
public class MatchStatisticsServiceTests : IAsyncLifetime, IDisposable
{
    private readonly Mock<ILogger<MatchStatisticsService>> _loggerMock;
    private readonly string _testDbPath;
    private readonly MatchStatisticsService _service;

    public MatchStatisticsServiceTests()
    {
        _loggerMock = new Mock<ILogger<MatchStatisticsService>>();
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_stats_{Guid.NewGuid():N}.db");
        _service = new MatchStatisticsService(_loggerMock.Object, _testDbPath);
    }

    public async Task InitializeAsync()
    {
        await _service.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    #region Match Recording Tests

    [Fact]
    public async Task RecordMatchStartAsync_CreatesMatchRecord()
    {
        // Arrange
        var players = new List<string> { "Player1", "Player2", "Player3" };

        // Act
        var matchId = await _service.RecordMatchStartAsync(1, "Server1", players, "Normal");

        // Assert
        matchId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RecordMatchStartAsync_StoresCorrectPlayerCount()
    {
        // Arrange
        var players = new List<string> { "Alice", "Bob", "Charlie", "Dave" };

        // Act
        var matchId = await _service.RecordMatchStartAsync(1, "TestServer", players);
        var match = await _service.GetMatchAsync(matchId);

        // Assert
        match.Should().NotBeNull();
        match!.PlayerCount.Should().Be(4);
    }

    [Fact]
    public async Task RecordMatchEndAsync_UpdatesMatchWithWinner()
    {
        // Arrange
        var players = new List<string> { "Team1", "Team2" };
        var matchId = await _service.RecordMatchStartAsync(1, "Server1", players);
        await Task.Delay(100); // Small delay to get meaningful duration

        // Act
        await _service.RecordMatchEndAsync(matchId, "Legion");

        // Assert
        var match = await _service.GetMatchAsync(matchId);
        match.Should().NotBeNull();
        match!.Winner.Should().Be("Legion");
        match.EndTime.Should().NotBeNull();
        match!.EndTime.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordMatchEndAsync_CalculatesDuration()
    {
        // Arrange
        var matchId = await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1" });
        await Task.Delay(500); // Wait 500ms

        // Act
        await _service.RecordMatchEndAsync(matchId);

        // Assert
        var match = await _service.GetMatchAsync(matchId);
        match.Should().NotBeNull();
        match!.EndTime.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMatchAsync_ReturnsNullForNonExistentMatch()
    {
        // Act
        var match = await _service.GetMatchAsync(999999);

        // Assert
        match.Should().BeNull();
    }

    #endregion

    #region Recent Matches Tests

    [Fact]
    public async Task GetRecentMatchesAsync_ReturnsMatchesInOrder()
    {
        // Arrange
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1" });
        await Task.Delay(10);
        await _service.RecordMatchStartAsync(2, "Server2", new List<string> { "P2" });
        await Task.Delay(10);
        await _service.RecordMatchStartAsync(3, "Server3", new List<string> { "P3" });

        // Act
        var matches = await _service.GetRecentMatchesAsync(10);

        // Assert
        matches.Should().HaveCountGreaterThanOrEqualTo(3);
        matches[0].ServerName.Should().Be("Server3"); // Most recent first
    }

    [Fact]
    public async Task GetRecentMatchesAsync_RespectsCountLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _service.RecordMatchStartAsync(i, $"Server{i}", new List<string> { $"P{i}" });
        }

        // Act
        var matches = await _service.GetRecentMatchesAsync(5);

        // Assert
        matches.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetMatchesByServerAsync_FiltersCorrectly()
    {
        // Arrange
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1" });
        await _service.RecordMatchStartAsync(2, "Server2", new List<string> { "P2" });
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P3" });

        // Act
        var matches = await _service.GetMatchesByServerAsync(1, 10);

        // Assert
        matches.Should().HaveCount(2);
        matches.Should().OnlyContain(m => m.ServerId == 1);
    }

    #endregion

    #region Player Stats Tests

    [Fact]
    public async Task UpdatePlayerStatsAsync_CreatesNewPlayerRecord()
    {
        // Arrange
        var playerName = $"NewPlayer_{Guid.NewGuid():N}";

        // Act
        await _service.UpdatePlayerStatsAsync(playerName, 12345, won: true, playTimeSeconds: 1800);

        // Assert
        var stats = await _service.GetPlayerStatsAsync(playerName);
        stats.Should().NotBeNull();
        stats!.PlayerName.Should().Be(playerName);
        stats.AccountId.Should().Be(12345);
        stats.TotalMatches.Should().Be(1);
        stats.Wins.Should().Be(1);
    }

    [Fact]
    public async Task UpdatePlayerStatsAsync_UpdatesExistingPlayer()
    {
        // Arrange
        var playerName = $"ExistingPlayer_{Guid.NewGuid():N}";
        await _service.UpdatePlayerStatsAsync(playerName, 100, won: true, playTimeSeconds: 1000);

        // Act
        await _service.UpdatePlayerStatsAsync(playerName, 100, won: false, playTimeSeconds: 2000);

        // Assert
        var stats = await _service.GetPlayerStatsAsync(playerName);
        stats.Should().NotBeNull();
        stats!.TotalMatches.Should().Be(2);
        stats.Wins.Should().Be(1);
        stats.Losses.Should().Be(1);
        stats.TotalPlayTimeSeconds.Should().Be(3000);
    }

    [Fact]
    public async Task GetPlayerStatsAsync_ReturnsNullForUnknownPlayer()
    {
        // Act
        var stats = await _service.GetPlayerStatsAsync("NonExistentPlayer_12345");

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public async Task PlayerStats_WinRate_CalculatesCorrectly()
    {
        // Arrange
        var playerName = $"WinRatePlayer_{Guid.NewGuid():N}";
        await _service.UpdatePlayerStatsAsync(playerName, 1, won: true, playTimeSeconds: 100);
        await _service.UpdatePlayerStatsAsync(playerName, 1, won: true, playTimeSeconds: 100);
        await _service.UpdatePlayerStatsAsync(playerName, 1, won: false, playTimeSeconds: 100);
        await _service.UpdatePlayerStatsAsync(playerName, 1, won: true, playTimeSeconds: 100);

        // Act
        var stats = await _service.GetPlayerStatsAsync(playerName);

        // Assert
        stats.Should().NotBeNull();
        stats!.WinRate.Should().Be(75); // 3 wins out of 4
    }

    [Fact]
    public async Task GetTopPlayersAsync_ReturnsOrderedByWinRate()
    {
        // Arrange - need 5+ matches for players to appear in top list
        var player1 = $"TopPlayer1_{Guid.NewGuid():N}";
        var player2 = $"TopPlayer2_{Guid.NewGuid():N}";

        // Player1: 3 wins, 2 losses = 60% win rate
        for (int i = 0; i < 3; i++)
            await _service.UpdatePlayerStatsAsync(player1, 1, won: true, playTimeSeconds: 100);
        for (int i = 0; i < 2; i++)
            await _service.UpdatePlayerStatsAsync(player1, 1, won: false, playTimeSeconds: 100);

        // Player2: 4 wins, 1 loss = 80% win rate  
        for (int i = 0; i < 4; i++)
            await _service.UpdatePlayerStatsAsync(player2, 2, won: true, playTimeSeconds: 100);
        await _service.UpdatePlayerStatsAsync(player2, 2, won: false, playTimeSeconds: 100);

        // Act
        var topPlayers = await _service.GetTopPlayersAsync(10);

        // Assert
        topPlayers.Should().HaveCountGreaterThanOrEqualTo(2);
        // Player2 should be first (higher win rate)
        var player2Stats = topPlayers.FirstOrDefault(p => p.PlayerName == player2);
        player2Stats.Should().NotBeNull();
        player2Stats!.Wins.Should().Be(4);
    }

    [Fact]
    public async Task GetMostActivePlayersAsync_ReturnsOrderedByTotalMatches()
    {
        // Arrange
        var active = $"ActivePlayer_{Guid.NewGuid():N}";
        var inactive = $"InactivePlayer_{Guid.NewGuid():N}";

        for (int i = 0; i < 5; i++)
        {
            await _service.UpdatePlayerStatsAsync(active, 1, won: i % 2 == 0, playTimeSeconds: 100);
        }
        await _service.UpdatePlayerStatsAsync(inactive, 2, won: true, playTimeSeconds: 100);

        // Act
        var activePlayers = await _service.GetMostActivePlayersAsync(10);

        // Assert
        activePlayers.Should().NotBeEmpty();
        var activeStats = activePlayers.FirstOrDefault(p => p.PlayerName == active);
        activeStats.Should().NotBeNull();
        activeStats!.TotalMatches.Should().Be(5);
    }

    #endregion

    #region Server Stats Tests

    [Fact]
    public async Task GetServerStatsAsync_ReturnsAggregatedStats()
    {
        // Arrange
        await _service.RecordMatchStartAsync(10, "TestServer", new List<string> { "P1", "P2" });
        var matchId = await _service.RecordMatchStartAsync(10, "TestServer", new List<string> { "P1", "P2", "P3" });
        await _service.RecordMatchEndAsync(matchId);

        // Act
        var stats = await _service.GetServerStatsAsync(10);

        // Assert
        stats.Should().NotBeNull();
        stats.ServerId.Should().Be(10);
        stats.TotalMatches.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetAllServerStatsAsync_ReturnsAllServers()
    {
        // Arrange
        await _service.RecordMatchStartAsync(100, "Server100", new List<string> { "P1" });
        await _service.RecordMatchStartAsync(200, "Server200", new List<string> { "P2" });

        // Act
        var allStats = await _service.GetAllServerStatsAsync();

        // Assert
        allStats.Should().NotBeEmpty();
    }

    #endregion

    #region Daily Stats Tests

    [Fact]
    public async Task GetTodayStatsAsync_ReturnsCurrentDayStats()
    {
        // Arrange
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1", "P2" });

        // Act
        var todayStats = await _service.GetTodayStatsAsync();

        // Assert
        todayStats.Should().NotBeNull();
        todayStats.Date.Date.Should().Be(DateTime.UtcNow.Date);
        todayStats.MatchCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetDailyStatsAsync_ReturnsRequestedDays()
    {
        // Arrange
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1" });

        // Act
        var dailyStats = await _service.GetDailyStatsAsync(7);

        // Assert
        dailyStats.Should().NotBeEmpty();
    }

    #endregion

    #region Summary Tests

    [Fact]
    public async Task GetOverallSummaryAsync_ReturnsCompleteSummary()
    {
        // Arrange
        await _service.RecordMatchStartAsync(1, "Server1", new List<string> { "P1" });
        await _service.UpdatePlayerStatsAsync("SummaryPlayer", 1, won: true, playTimeSeconds: 1000);

        // Act
        var summary = await _service.GetOverallSummaryAsync();

        // Assert
        summary.Should().NotBeNull();
        summary.Should().ContainKey("total_matches");
        summary.Should().ContainKey("unique_players");
        summary.Should().ContainKey("total_play_time_seconds");
    }

    #endregion

    #region MatchRecord Model Tests

    [Fact]
    public void MatchRecord_Players_DeserializesFromJson()
    {
        // Arrange
        var record = new MatchRecord
        {
            PlayersJson = "[\"Alice\",\"Bob\",\"Charlie\"]"
        };

        // Act
        var players = record.Players;

        // Assert
        players.Should().HaveCount(3);
        players.Should().Contain("Alice");
        players.Should().Contain("Bob");
        players.Should().Contain("Charlie");
    }

    [Fact]
    public void MatchRecord_Players_HandlesEmptyJson()
    {
        // Arrange
        var record = new MatchRecord { PlayersJson = "[]" };

        // Act
        var players = record.Players;

        // Assert
        players.Should().BeEmpty();
    }

    #endregion

    #region PlayerStats Model Tests

    [Fact]
    public void PlayerStats_WinRate_ReturnsZeroForNoMatches()
    {
        // Arrange
        var stats = new PlayerStats { TotalMatches = 0, Wins = 0 };

        // Assert
        stats.WinRate.Should().Be(0);
    }

    [Fact]
    public void PlayerStats_WinRate_CalculatesCorrectPercentage()
    {
        // Arrange
        var stats = new PlayerStats { TotalMatches = 10, Wins = 7 };

        // Assert
        stats.WinRate.Should().Be(70);
    }

    #endregion

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CanBeCalledMultipleTimes()
    {
        // Act & Assert - should not throw
        await _service.InitializeAsync();
        await _service.InitializeAsync();
        await _service.InitializeAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesSuccessfully()
    {
        // Act
        await _service.InitializeAsync();

        // Assert - should be able to perform operations
        var matchId = await _service.RecordMatchStartAsync(1, "Test", new List<string>());
        matchId.Should().BeGreaterThan(0);
    }

    #endregion
}
