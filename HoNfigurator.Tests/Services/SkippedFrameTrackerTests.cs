using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Diagnostics;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for SkippedFrameTracker - covers lag tracking and analytics
/// </summary>
public class SkippedFrameTrackerTests
{
    private readonly Mock<ILogger<SkippedFrameTracker>> _loggerMock;
    private readonly SkippedFrameTracker _tracker;

    public SkippedFrameTrackerTests()
    {
        _loggerMock = new Mock<ILogger<SkippedFrameTracker>>();
        _tracker = new SkippedFrameTracker(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaultParameters_CreatesTracker()
    {
        // Act
        var tracker = new SkippedFrameTracker(_loggerMock.Object);

        // Assert
        tracker.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomParameters_CreatesTracker()
    {
        // Act
        var tracker = new SkippedFrameTracker(
            _loggerMock.Object, 
            maxHistoryPerServer: 500, 
            dataRetention: TimeSpan.FromHours(12));

        // Assert
        tracker.Should().NotBeNull();
    }

    #endregion

    #region RecordSkippedFrame Tests

    [Fact]
    public void RecordSkippedFrame_BasicRecording_CreatesEntry()
    {
        // Act
        _tracker.RecordSkippedFrame(serverId: 1, port: 11235, skippedMs: 100);

        // Assert
        var analytics = _tracker.GetServerAnalytics(1);
        analytics.Should().NotBeNull();
        analytics!.TotalSkippedFrames.Should().Be(1);
        analytics.TotalSkippedMs.Should().Be(100);
    }

    [Fact]
    public void RecordSkippedFrame_MultipleRecordings_AggregatesCorrectly()
    {
        // Act
        _tracker.RecordSkippedFrame(1, 11235, 50);
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(1, 11235, 150);

        // Assert
        var analytics = _tracker.GetServerAnalytics(1);
        analytics.Should().NotBeNull();
        analytics!.TotalSkippedFrames.Should().Be(3);
        analytics.TotalSkippedMs.Should().Be(300);
        analytics.MaxSkippedMs.Should().Be(150);
    }

    [Fact]
    public void RecordSkippedFrame_WithPlayerInfo_TracksPlayerStats()
    {
        // Act
        _tracker.RecordSkippedFrame(1, 11235, 100, playerId: 1001, playerName: "LaggyPlayer");
        _tracker.RecordSkippedFrame(1, 11235, 200, playerId: 1001, playerName: "LaggyPlayer");

        // Assert
        var playerStats = _tracker.GetPlayerStats(1);
        playerStats.Should().ContainSingle();
        var player = playerStats[0];
        player.PlayerId.Should().Be(1001);
        player.PlayerName.Should().Be("LaggyPlayer");
        player.SkippedFrameCount.Should().Be(2);
        player.TotalSkippedMs.Should().Be(300);
        player.MaxSkippedMs.Should().Be(200);
    }

    [Fact]
    public void RecordSkippedFrame_MultipleServers_TracksIndependently()
    {
        // Act
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);
        _tracker.RecordSkippedFrame(3, 11237, 300);

        // Assert
        var server1 = _tracker.GetServerAnalytics(1);
        var server2 = _tracker.GetServerAnalytics(2);
        var server3 = _tracker.GetServerAnalytics(3);

        server1!.TotalSkippedMs.Should().Be(100);
        server2!.TotalSkippedMs.Should().Be(200);
        server3!.TotalSkippedMs.Should().Be(300);
    }

    [Fact]
    public void RecordSkippedFrame_UpdatesMaxSkippedMs()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 50);
        _tracker.RecordSkippedFrame(1, 11235, 200);

        // Act
        _tracker.RecordSkippedFrame(1, 11235, 100); // Less than max

        // Assert
        var analytics = _tracker.GetServerAnalytics(1);
        analytics!.MaxSkippedMs.Should().Be(200); // Should still be 200
    }

    #endregion

    #region GetServerAnalytics Tests

    [Fact]
    public void GetServerAnalytics_NonExistentServer_ReturnsNull()
    {
        // Act
        var analytics = _tracker.GetServerAnalytics(999);

        // Assert
        analytics.Should().BeNull();
    }

    [Fact]
    public void GetServerAnalytics_ReturnsCorrectPort()
    {
        // Arrange
        _tracker.RecordSkippedFrame(5, 12345, 100);

        // Act
        var analytics = _tracker.GetServerAnalytics(5);

        // Assert
        analytics.Should().NotBeNull();
        analytics!.Port.Should().Be(12345);
    }

    [Fact]
    public void GetServerAnalytics_CalculatesAverageCorrectly()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(1, 11235, 200);
        _tracker.RecordSkippedFrame(1, 11235, 300);

        // Act
        var analytics = _tracker.GetServerAnalytics(1);

        // Assert
        analytics!.AverageSkippedMs.Should().Be(200); // (100+200+300)/3
    }

    #endregion

    #region GetServerAnalyticsByPort Tests

    [Fact]
    public void GetServerAnalyticsByPort_FindsCorrectServer()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11111, 100);
        _tracker.RecordSkippedFrame(2, 22222, 200);

        // Act
        var analytics = _tracker.GetServerAnalyticsByPort(22222);

        // Assert
        analytics.Should().NotBeNull();
        analytics!.ServerId.Should().Be(2);
        analytics.TotalSkippedMs.Should().Be(200);
    }

    [Fact]
    public void GetServerAnalyticsByPort_NonExistentPort_ReturnsNull()
    {
        // Act
        var analytics = _tracker.GetServerAnalyticsByPort(99999);

        // Assert
        analytics.Should().BeNull();
    }

    #endregion

    #region GetAllServerAnalytics Tests

    [Fact]
    public void GetAllServerAnalytics_ReturnsAllServers()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);
        _tracker.RecordSkippedFrame(3, 11237, 300);

        // Act
        var allAnalytics = _tracker.GetAllServerAnalytics();

        // Assert
        allAnalytics.Should().HaveCount(3);
    }

    [Fact]
    public void GetAllServerAnalytics_ReturnsOrderedByServerId()
    {
        // Arrange
        _tracker.RecordSkippedFrame(3, 11237, 300);
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);

        // Act
        var allAnalytics = _tracker.GetAllServerAnalytics();

        // Assert
        allAnalytics[0].ServerId.Should().Be(1);
        allAnalytics[1].ServerId.Should().Be(2);
        allAnalytics[2].ServerId.Should().Be(3);
    }

    [Fact]
    public void GetAllServerAnalytics_EmptyTracker_ReturnsEmptyList()
    {
        // Act
        var allAnalytics = _tracker.GetAllServerAnalytics();

        // Assert
        allAnalytics.Should().BeEmpty();
    }

    #endregion

    #region GetRecentEntries Tests

    [Fact]
    public void GetRecentEntries_ReturnsEntriesInDescendingOrder()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        Thread.Sleep(10);
        _tracker.RecordSkippedFrame(1, 11235, 200);
        Thread.Sleep(10);
        _tracker.RecordSkippedFrame(1, 11235, 300);

        // Act
        var entries = _tracker.GetRecentEntries(1, count: 10);

        // Assert
        entries.Should().HaveCount(3);
        entries[0].SkippedMs.Should().Be(300); // Most recent first
    }

    [Fact]
    public void GetRecentEntries_RespectsCountLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _tracker.RecordSkippedFrame(1, 11235, i * 10);
        }

        // Act
        var entries = _tracker.GetRecentEntries(1, count: 5);

        // Assert
        entries.Should().HaveCount(5);
    }

    [Fact]
    public void GetRecentEntries_NonExistentServer_ReturnsEmptyList()
    {
        // Act
        var entries = _tracker.GetRecentEntries(999);

        // Assert
        entries.Should().BeEmpty();
    }

    #endregion

    #region GetPlayerStats Tests

    [Fact]
    public void GetPlayerStats_ReturnsOrderedByTotalSkippedMs()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100, playerId: 1, playerName: "Player1");
        _tracker.RecordSkippedFrame(1, 11235, 500, playerId: 2, playerName: "Player2");
        _tracker.RecordSkippedFrame(1, 11235, 200, playerId: 3, playerName: "Player3");

        // Act
        var playerStats = _tracker.GetPlayerStats(1);

        // Assert
        playerStats.Should().HaveCount(3);
        playerStats[0].PlayerName.Should().Be("Player2"); // Highest lag first
        playerStats[0].TotalSkippedMs.Should().Be(500);
    }

    [Fact]
    public void GetPlayerStats_NonExistentServer_ReturnsEmptyList()
    {
        // Act
        var playerStats = _tracker.GetPlayerStats(999);

        // Assert
        playerStats.Should().BeEmpty();
    }

    #endregion

    #region GetGlobalAnalytics Tests

    [Fact]
    public void GetGlobalAnalytics_AggregatesAllServers()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);
        _tracker.RecordSkippedFrame(3, 11237, 300);

        // Act
        var global = _tracker.GetGlobalAnalytics();

        // Assert
        global.TotalServers.Should().Be(3);
        global.TotalSkippedFrames.Should().Be(3);
        global.TotalSkippedMs.Should().Be(600);
        global.MaxSkippedMs.Should().Be(300);
        global.ServersWithLag.Should().Be(3);
    }

    [Fact]
    public void GetGlobalAnalytics_CalculatesAverageAcrossServers()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);

        // Act
        var global = _tracker.GetGlobalAnalytics();

        // Assert
        global.AverageSkippedMs.Should().Be(150); // Average of 100 and 200
    }

    [Fact]
    public void GetGlobalAnalytics_IncludesByServerDetails()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);

        // Act
        var global = _tracker.GetGlobalAnalytics();

        // Assert
        global.ByServer.Should().HaveCount(2);
    }

    #endregion

    #region ClearServerData Tests

    [Fact]
    public void ClearServerData_RemovesSpecificServer()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);

        // Act
        _tracker.ClearServerData(1);

        // Assert
        _tracker.GetServerAnalytics(1).Should().BeNull();
        _tracker.GetServerAnalytics(2).Should().NotBeNull();
    }

    [Fact]
    public void ClearServerData_NonExistentServer_DoesNotThrow()
    {
        // Act & Assert - should not throw
        _tracker.ClearServerData(999);
    }

    #endregion

    #region ClearAllData Tests

    [Fact]
    public void ClearAllData_RemovesAllServers()
    {
        // Arrange
        _tracker.RecordSkippedFrame(1, 11235, 100);
        _tracker.RecordSkippedFrame(2, 11236, 200);
        _tracker.RecordSkippedFrame(3, 11237, 300);

        // Act
        _tracker.ClearAllData();

        // Assert
        _tracker.GetAllServerAnalytics().Should().BeEmpty();
    }

    #endregion

    #region Model Tests

    [Fact]
    public void PlayerFrameStats_AverageSkippedMs_CalculatesCorrectly()
    {
        // Arrange
        var stats = new PlayerFrameStats
        {
            SkippedFrameCount = 4,
            TotalSkippedMs = 400
        };

        // Assert
        stats.AverageSkippedMs.Should().Be(100);
    }

    [Fact]
    public void PlayerFrameStats_AverageSkippedMs_ReturnsZeroForNoFrames()
    {
        // Arrange
        var stats = new PlayerFrameStats
        {
            SkippedFrameCount = 0,
            TotalSkippedMs = 0
        };

        // Assert
        stats.AverageSkippedMs.Should().Be(0);
    }

    [Fact]
    public void SkippedFrameEntry_DefaultValues()
    {
        // Arrange & Act
        var entry = new SkippedFrameEntry
        {
            Timestamp = DateTime.UtcNow,
            SkippedMs = 150
        };

        // Assert
        entry.PlayerId.Should().BeNull();
        entry.PlayerName.Should().BeNull();
    }

    [Fact]
    public void ServerFrameAnalytics_TopLaggyPlayers_DefaultsToEmptyList()
    {
        // Arrange & Act
        var analytics = new ServerFrameAnalytics();

        // Assert
        analytics.TopLaggyPlayers.Should().BeEmpty();
    }

    [Fact]
    public void GlobalFrameAnalytics_ByServer_DefaultsToEmptyList()
    {
        // Arrange & Act
        var global = new GlobalFrameAnalytics();

        // Assert
        global.ByServer.Should().BeEmpty();
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task RecordSkippedFrame_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - multiple concurrent recordings
        for (int i = 0; i < 100; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(() =>
            {
                _tracker.RecordSkippedFrame(1, 11235, taskId, playerId: taskId % 10);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var analytics = _tracker.GetServerAnalytics(1);
        analytics.Should().NotBeNull();
        analytics!.TotalSkippedFrames.Should().Be(100);
    }

    #endregion
}
