using FluentAssertions;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Connectors;

/// <summary>
/// Tests for MqttStatusPublisher - MQTT status publishing service
/// </summary>
public class MqttStatusPublisherTests : IDisposable
{
    private readonly Mock<ILogger<MqttStatusPublisher>> _mockLogger;

    public MqttStatusPublisherTests()
    {
        _mockLogger = new Mock<ILogger<MqttStatusPublisher>>();
    }

    public void Dispose()
    {
    }

    private static HoNConfiguration CreateConfig(bool mqttEnabled = true)
    {
        return new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "TestServer", ManVersion = "1.0.0" },
            ApplicationData = new ApplicationData
            {
                Mqtt = new MqttSettings
                {
                    Enabled = mqttEnabled,
                    Host = "localhost",
                    Port = 1883,
                    TopicPrefix = "test/honfigurator"
                }
            }
        };
    }

    #region MqttServerStatus Enum Tests

    [Fact]
    public void MqttServerStatus_ShouldContainExpectedValues()
    {
        var values = Enum.GetValues<MqttServerStatus>();

        values.Should().Contain(MqttServerStatus.Unknown);
        values.Should().Contain(MqttServerStatus.Starting);
        values.Should().Contain(MqttServerStatus.Online);
        values.Should().Contain(MqttServerStatus.InGame);
        values.Should().Contain(MqttServerStatus.Stopping);
        values.Should().Contain(MqttServerStatus.Offline);
        values.Should().Contain(MqttServerStatus.Error);
    }

    [Theory]
    [InlineData(MqttServerStatus.Unknown, 0)]
    [InlineData(MqttServerStatus.Starting, 1)]
    [InlineData(MqttServerStatus.Online, 2)]
    [InlineData(MqttServerStatus.InGame, 3)]
    [InlineData(MqttServerStatus.Stopping, 4)]
    [InlineData(MqttServerStatus.Offline, 5)]
    [InlineData(MqttServerStatus.Error, 6)]
    public void MqttServerStatus_ShouldHaveCorrectValues(MqttServerStatus status, int expectedValue)
    {
        ((int)status).Should().Be(expectedValue);
    }

    #endregion

    #region GameState Enum Tests

    [Fact]
    public void GameState_ShouldContainExpectedValues()
    {
        var values = Enum.GetValues<GameState>();

        values.Should().Contain(GameState.Idle);
        values.Should().Contain(GameState.Lobby);
        values.Should().Contain(GameState.Picking);
        values.Should().Contain(GameState.Loading);
        values.Should().Contain(GameState.Playing);
        values.Should().Contain(GameState.Ended);
    }

    [Theory]
    [InlineData(GameState.Idle, 0)]
    [InlineData(GameState.Lobby, 1)]
    [InlineData(GameState.Picking, 2)]
    [InlineData(GameState.Loading, 3)]
    [InlineData(GameState.Playing, 4)]
    [InlineData(GameState.Ended, 5)]
    public void GameState_ShouldHaveCorrectValues(GameState state, int expectedValue)
    {
        ((int)state).Should().Be(expectedValue);
    }

    #endregion

    #region Model Tests

    [Fact]
    public void ServerMqttState_ShouldHaveDefaultValues()
    {
        var state = new ServerMqttState();

        state.ServerId.Should().Be(0);
        state.Status.Should().Be(MqttServerStatus.Unknown);
        state.CurrentMatch.Should().BeNull();
        state.LastUpdate.Should().Be(default);
    }

    [Fact]
    public void ServerMqttState_ShouldSetProperties()
    {
        var now = DateTime.UtcNow;
        var match = new MatchMqttInfo { MatchId = 123 };
        
        var state = new ServerMqttState
        {
            ServerId = 1,
            Status = MqttServerStatus.Online,
            CurrentMatch = match,
            LastUpdate = now
        };

        state.ServerId.Should().Be(1);
        state.Status.Should().Be(MqttServerStatus.Online);
        state.CurrentMatch.Should().BeSameAs(match);
        state.LastUpdate.Should().Be(now);
    }

    [Fact]
    public void MatchMqttInfo_ShouldHaveDefaultValues()
    {
        var info = new MatchMqttInfo();

        info.MatchId.Should().Be(0);
        info.MapName.Should().BeEmpty();
        info.GameMode.Should().BeEmpty();
        info.PlayerCount.Should().Be(0);
        info.IsInProgress.Should().BeFalse();
        info.StartTime.Should().BeNull();
    }

    [Fact]
    public void MatchMqttInfo_ShouldSetProperties()
    {
        var startTime = DateTime.UtcNow;
        
        var info = new MatchMqttInfo
        {
            MatchId = 12345,
            MapName = "caldavar",
            GameMode = "Normal",
            PlayerCount = 10,
            IsInProgress = true,
            StartTime = startTime
        };

        info.MatchId.Should().Be(12345);
        info.MapName.Should().Be("caldavar");
        info.GameMode.Should().Be("Normal");
        info.PlayerCount.Should().Be(10);
        info.IsInProgress.Should().BeTrue();
        info.StartTime.Should().Be(startTime);
    }

    [Fact]
    public void ServerMetrics_ShouldHaveDefaultValues()
    {
        var metrics = new ServerMetrics();

        metrics.CpuUsage.Should().Be(0);
        metrics.MemoryUsageMb.Should().Be(0);
        metrics.PlayerCount.Should().Be(0);
        metrics.AveragePing.Should().Be(0);
        metrics.UptimeSeconds.Should().Be(0);
    }

    [Fact]
    public void ServerMetrics_ShouldSetProperties()
    {
        var metrics = new ServerMetrics
        {
            CpuUsage = 75.5,
            MemoryUsageMb = 2048,
            PlayerCount = 10,
            AveragePing = 50,
            UptimeSeconds = 3600
        };

        metrics.CpuUsage.Should().Be(75.5);
        metrics.MemoryUsageMb.Should().Be(2048);
        metrics.PlayerCount.Should().Be(10);
        metrics.AveragePing.Should().Be(50);
        metrics.UptimeSeconds.Should().Be(3600);
    }

    #endregion

    #region Service Creation Tests

    [Fact]
    public void Constructor_WithNullMqttHandler_ShouldNotBeConnected()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        publisher.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithDisabledMqtt_ShouldNotBeConnected()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        publisher.IsConnected.Should().BeFalse();
    }

    #endregion

    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.InitializeAsync())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_WithNullHandler_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.Invoking(p => p.InitializeAsync())
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishManagerStatusAsync Tests

    [Fact]
    public async Task PublishManagerStatusAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishManagerStatusAsync("online"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishManagerStatusAsync_WithNullHandler_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.Invoking(p => p.PublishManagerStatusAsync("online"))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishServerStatusAsync Tests

    [Fact]
    public async Task PublishServerStatusAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishServerStatusAsync(1, MqttServerStatus.Online))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishServerStatusAsync_WhenNotConnected_ShouldNotTrackState()
    {
        // When not connected, CanPublish returns false and state is NOT tracked
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.PublishServerStatusAsync(1, MqttServerStatus.Online);

        var states = publisher.GetServerStates();
        states.Should().BeEmpty(); // State not tracked when not connected
    }

    [Theory]
    [InlineData(MqttServerStatus.Unknown)]
    [InlineData(MqttServerStatus.Starting)]
    [InlineData(MqttServerStatus.Online)]
    [InlineData(MqttServerStatus.InGame)]
    [InlineData(MqttServerStatus.Stopping)]
    [InlineData(MqttServerStatus.Offline)]
    [InlineData(MqttServerStatus.Error)]
    public async Task PublishServerStatusAsync_WithVariousStatuses_ShouldNotThrow(MqttServerStatus status)
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.Invoking(p => p.PublishServerStatusAsync(1, status))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishGameStateAsync Tests

    [Fact]
    public async Task PublishGameStateAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishGameStateAsync(1, GameState.Playing))
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(GameState.Idle)]
    [InlineData(GameState.Lobby)]
    [InlineData(GameState.Picking)]
    [InlineData(GameState.Loading)]
    [InlineData(GameState.Playing)]
    [InlineData(GameState.Ended)]
    public async Task PublishGameStateAsync_WithVariousStates_ShouldNotThrow(GameState state)
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.Invoking(p => p.PublishGameStateAsync(1, state))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishMatchInfoAsync Tests

    [Fact]
    public async Task PublishMatchInfoAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));
        var matchInfo = new MatchMqttInfo { MatchId = 1 };

        await publisher.Invoking(p => p.PublishMatchInfoAsync(1, matchInfo))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishMatchInfoAsync_WhenNotConnected_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());
        var matchInfo = new MatchMqttInfo { MatchId = 12345, MapName = "caldavar" };

        await publisher.Invoking(p => p.PublishMatchInfoAsync(1, matchInfo))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishPlayerJoinedAsync Tests

    [Fact]
    public async Task PublishPlayerJoinedAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishPlayerJoinedAsync(1, "TestPlayer", 1001))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishPlayerLeftAsync Tests

    [Fact]
    public async Task PublishPlayerLeftAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishPlayerLeftAsync(1, "TestPlayer", 1001, "disconnect"))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishMatchStartedAsync Tests

    [Fact]
    public async Task PublishMatchStartedAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishMatchStartedAsync(1, 12345, "caldavar", "Normal"))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishMatchEndedAsync Tests

    [Fact]
    public async Task PublishMatchEndedAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishMatchEndedAsync(1, 12345, "Legion", 3600))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishServerMetricsAsync Tests

    [Fact]
    public async Task PublishServerMetricsAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));
        var metrics = new ServerMetrics { CpuUsage = 50, MemoryUsageMb = 1024 };

        await publisher.Invoking(p => p.PublishServerMetricsAsync(1, metrics))
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishAggregateStatsAsync Tests

    [Fact]
    public async Task PublishAggregateStatsAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishAggregateStatsAsync())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAggregateStatsAsync_WithMultipleServers_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.PublishServerStatusAsync(1, MqttServerStatus.Online);
        await publisher.PublishServerStatusAsync(2, MqttServerStatus.InGame);
        await publisher.PublishServerStatusAsync(3, MqttServerStatus.Offline);

        await publisher.Invoking(p => p.PublishAggregateStatsAsync())
            .Should().NotThrowAsync();
    }

    #endregion

    #region PublishAlertAsync Tests

    [Fact]
    public async Task PublishAlertAsync_WhenDisabled_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig(mqttEnabled: false));

        await publisher.Invoking(p => p.PublishAlertAsync("test", "Test message"))
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("critical")]
    public async Task PublishAlertAsync_WithVariousSeverities_ShouldNotThrow(string severity)
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.Invoking(p => p.PublishAlertAsync("test", "Test message", severity))
            .Should().NotThrowAsync();
    }

    #endregion

    #region RemoveServer Tests

    [Fact]
    public void RemoveServer_NonExistent_ShouldNotThrow()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        publisher.Invoking(p => p.RemoveServer(999))
            .Should().NotThrow();
    }

    [Fact]
    public void RemoveServer_ShouldNotThrowWhenEmpty()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        var statesBefore = publisher.GetServerStates();
        statesBefore.Should().BeEmpty();

        publisher.RemoveServer(1);

        var statesAfter = publisher.GetServerStates();
        statesAfter.Should().BeEmpty();
    }

    #endregion

    #region GetServerStates Tests

    [Fact]
    public void GetServerStates_InitiallyEmpty_ShouldReturnEmptyDictionary()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        var states = publisher.GetServerStates();

        states.Should().BeEmpty();
    }

    [Fact]
    public async Task GetServerStates_ShouldReturnCopyNotReference()
    {
        using var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        await publisher.PublishServerStatusAsync(1, MqttServerStatus.Online);

        var states1 = publisher.GetServerStates();
        var states2 = publisher.GetServerStates();

        states1.Should().NotBeSameAs(states2);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        publisher.Invoking(p => p.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        var publisher = new MqttStatusPublisher(_mockLogger.Object, CreateConfig());

        publisher.Dispose();
        publisher.Invoking(p => p.Dispose()).Should().NotThrow();
    }

    #endregion
}
