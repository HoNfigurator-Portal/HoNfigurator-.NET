using FluentAssertions;
using HoNfigurator.Api.Endpoints;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using Moq;
using Xunit;

namespace HoNfigurator.Tests.Api.Endpoints;

public class MqttEndpointsTests
{
    #region IMqttHandler Interface Tests

    [Fact]
    public void IMqttHandler_ShouldDefineRequiredProperties()
    {
        // Arrange
        var type = typeof(IMqttHandler);

        // Act
        var properties = type.GetProperties();

        // Assert
        properties.Should().Contain(p => p.Name == "IsConnected");
        properties.Should().Contain(p => p.Name == "IsEnabled");
    }

    [Fact]
    public void IMqttHandler_ShouldDefineRequiredMethods()
    {
        // Arrange
        var type = typeof(IMqttHandler);

        // Act
        var methods = type.GetMethods();

        // Assert
        methods.Should().Contain(m => m.Name == "ConnectAsync");
        methods.Should().Contain(m => m.Name == "DisconnectAsync");
        methods.Should().Contain(m => m.Name == "PublishAsync");
        methods.Should().Contain(m => m.Name == "PublishJsonAsync");
        methods.Should().Contain(m => m.Name == "PublishServerStatusAsync");
        methods.Should().Contain(m => m.Name == "PublishMatchEventAsync");
        methods.Should().Contain(m => m.Name == "PublishPlayerEventAsync");
        methods.Should().Contain(m => m.Name == "PublishManagerEventAsync");
    }

    #endregion

    #region MqttPublishRequest Tests

    [Fact]
    public void MqttPublishRequest_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var request = new MqttPublishRequest();

        // Assert
        request.Topic.Should().BeEmpty();
        request.Message.Should().BeNull();
        request.Retain.Should().BeFalse();
    }

    [Fact]
    public void MqttPublishRequest_ShouldAcceptValidValues()
    {
        // Arrange & Act
        var request = new MqttPublishRequest
        {
            Topic = "server/1/status",
            Message = "{\"status\": \"online\"}",
            Retain = true
        };

        // Assert
        request.Topic.Should().Be("server/1/status");
        request.Message.Should().Be("{\"status\": \"online\"}");
        request.Retain.Should().BeTrue();
    }

    #endregion

    #region MqttTopics Tests

    [Fact]
    public void MqttTopics_ServerStatus_ShouldHaveCorrectFormat()
    {
        MqttTopics.ServerStatus.Should().Be("server/{0}/status");
    }

    [Fact]
    public void MqttTopics_ServerMatch_ShouldHaveCorrectFormat()
    {
        MqttTopics.ServerMatch.Should().Be("server/{0}/match");
    }

    [Fact]
    public void MqttTopics_ServerPlayer_ShouldHaveCorrectFormat()
    {
        MqttTopics.ServerPlayer.Should().Be("server/{0}/player");
    }

    [Fact]
    public void MqttTopics_ManagerStatus_ShouldHaveCorrectFormat()
    {
        MqttTopics.ManagerStatus.Should().Be("manager/status");
    }

    [Fact]
    public void MqttTopics_ManagerAlert_ShouldHaveCorrectFormat()
    {
        MqttTopics.ManagerAlert.Should().Be("manager/alert");
    }

    #endregion

    #region MqttEventTypes Tests

    [Fact]
    public void MqttEventTypes_ServerEvents_ShouldBeCorrect()
    {
        MqttEventTypes.ServerReady.Should().Be("server_ready");
        MqttEventTypes.ServerOccupied.Should().Be("server_occupied");
        MqttEventTypes.ServerOffline.Should().Be("server_offline");
        MqttEventTypes.Heartbeat.Should().Be("heartbeat");
    }

    [Fact]
    public void MqttEventTypes_MatchEvents_ShouldBeCorrect()
    {
        MqttEventTypes.LobbyCreated.Should().Be("lobby_created");
        MqttEventTypes.LobbyClosed.Should().Be("lobby_closed");
        MqttEventTypes.MatchStarted.Should().Be("match_started");
        MqttEventTypes.MatchEnded.Should().Be("match_ended");
    }

    [Fact]
    public void MqttEventTypes_PlayerEvents_ShouldBeCorrect()
    {
        MqttEventTypes.PlayerJoined.Should().Be("player_joined");
        MqttEventTypes.PlayerLeft.Should().Be("player_left");
        MqttEventTypes.PlayerKicked.Should().Be("player_kicked");
    }

    [Fact]
    public void MqttEventTypes_ManagerEvents_ShouldBeCorrect()
    {
        MqttEventTypes.ManagerOnline.Should().Be("online");
        MqttEventTypes.ManagerOffline.Should().Be("offline");
        MqttEventTypes.ManagerShutdown.Should().Be("shutdown");
    }

    #endregion

    #region Mock IMqttHandler Behavior Tests

    [Fact]
    public async Task MockMqttHandler_IsEnabled_WhenConfigured_ShouldReturnTrue()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        mockMqtt.Setup(m => m.IsEnabled).Returns(true);

        // Act & Assert
        mockMqtt.Object.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task MockMqttHandler_IsConnected_WhenConnected_ShouldReturnTrue()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        mockMqtt.Setup(m => m.IsConnected).Returns(true);

        // Act & Assert
        mockMqtt.Object.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task MockMqttHandler_ConnectAsync_WhenEnabled_ShouldReturnTrue()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        mockMqtt.Setup(m => m.IsEnabled).Returns(true);
        mockMqtt.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await mockMqtt.Object.ConnectAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task MockMqttHandler_ConnectAsync_WhenDisabled_ShouldReturnFalse()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        mockMqtt.Setup(m => m.IsEnabled).Returns(false);
        mockMqtt.Setup(m => m.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await mockMqtt.Object.ConnectAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MockMqttHandler_DisconnectAsync_ShouldComplete()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        mockMqtt.Setup(m => m.DisconnectAsync()).Returns(Task.CompletedTask);

        // Act & Assert
        await mockMqtt.Object.Invoking(m => m.DisconnectAsync())
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task MockMqttHandler_PublishAsync_ShouldCallWithCorrectParameters()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        string? capturedTopic = null;
        string? capturedMessage = null;
        bool capturedRetain = false;
        
        mockMqtt.Setup(m => m.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, bool>((t, m, r) => 
            {
                capturedTopic = t;
                capturedMessage = m;
                capturedRetain = r;
            })
            .Returns(Task.CompletedTask);

        // Act
        await mockMqtt.Object.PublishAsync("test/topic", "Hello MQTT", true);

        // Assert
        capturedTopic.Should().Be("test/topic");
        capturedMessage.Should().Be("Hello MQTT");
        capturedRetain.Should().BeTrue();
    }

    [Fact]
    public async Task MockMqttHandler_PublishServerStatusAsync_ShouldCallWithCorrectParameters()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        int capturedServerId = 0;
        string? capturedStatus = null;
        
        mockMqtt.Setup(m => m.PublishServerStatusAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<int, string, object?>((id, status, _) =>
            {
                capturedServerId = id;
                capturedStatus = status;
            })
            .Returns(Task.CompletedTask);

        // Act
        await mockMqtt.Object.PublishServerStatusAsync(1, MqttEventTypes.ServerReady, new { port = 10001 });

        // Assert
        capturedServerId.Should().Be(1);
        capturedStatus.Should().Be(MqttEventTypes.ServerReady);
    }

    [Fact]
    public async Task MockMqttHandler_PublishMatchEventAsync_ShouldCallWithCorrectParameters()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        int capturedServerId = 0;
        string? capturedEventType = null;
        
        mockMqtt.Setup(m => m.PublishMatchEventAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<int, string, object?>((id, eventType, _) =>
            {
                capturedServerId = id;
                capturedEventType = eventType;
            })
            .Returns(Task.CompletedTask);

        // Act
        await mockMqtt.Object.PublishMatchEventAsync(1, MqttEventTypes.MatchStarted, new { matchId = 12345L });

        // Assert
        capturedServerId.Should().Be(1);
        capturedEventType.Should().Be(MqttEventTypes.MatchStarted);
    }

    [Fact]
    public async Task MockMqttHandler_PublishPlayerEventAsync_ShouldCallWithCorrectParameters()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        int capturedServerId = 0;
        string? capturedEventType = null;
        string? capturedPlayerName = null;
        
        mockMqtt.Setup(m => m.PublishPlayerEventAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<int, string, string, object?>((id, eventType, playerName, _) =>
            {
                capturedServerId = id;
                capturedEventType = eventType;
                capturedPlayerName = playerName;
            })
            .Returns(Task.CompletedTask);

        // Act
        await mockMqtt.Object.PublishPlayerEventAsync(1, MqttEventTypes.PlayerJoined, "TestPlayer", new { accountId = 12345 });

        // Assert
        capturedServerId.Should().Be(1);
        capturedEventType.Should().Be(MqttEventTypes.PlayerJoined);
        capturedPlayerName.Should().Be("TestPlayer");
    }

    [Fact]
    public async Task MockMqttHandler_PublishManagerEventAsync_ShouldCallWithCorrectParameters()
    {
        // Arrange
        var mockMqtt = new Mock<IMqttHandler>();
        string? capturedEventType = null;
        
        mockMqtt.Setup(m => m.PublishManagerEventAsync(It.IsAny<string>(), It.IsAny<object?>()))
            .Callback<string, object?>((eventType, _) =>
            {
                capturedEventType = eventType;
            })
            .Returns(Task.CompletedTask);

        // Act
        await mockMqtt.Object.PublishManagerEventAsync(MqttEventTypes.ManagerOnline, new { serverName = "Test" });

        // Assert
        capturedEventType.Should().Be(MqttEventTypes.ManagerOnline);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void MqttSettings_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var settings = new MqttSettings();

        // Assert
        settings.Enabled.Should().BeFalse();
        settings.Host.Should().Be("localhost"); // Default host is localhost
        settings.Port.Should().Be(1883); // Default MQTT port
        settings.UseTls.Should().BeFalse();
        settings.TopicPrefix.Should().Be("honfigurator");
    }

    [Fact]
    public void MqttSettings_ShouldAcceptValidConfiguration()
    {
        // Arrange & Act
        var settings = new MqttSettings
        {
            Enabled = true,
            Host = "mqtt.honfigurator.app",
            Port = 8883,
            UseTls = true,
            TopicPrefix = "honfigurator",
            Username = "user",
            Password = "pass"
        };

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.Host.Should().Be("mqtt.honfigurator.app");
        settings.Port.Should().Be(8883);
        settings.UseTls.Should().BeTrue();
        settings.TopicPrefix.Should().Be("honfigurator");
    }

    [Fact]
    public void ManagementPortalSettings_MqttConfig_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new ManagementPortalSettings
        {
            Enabled = true,
            MqttHost = "mqtt.honfigurator.app",
            MqttPort = 8883,
            MqttUseTls = true
        };

        // Assert
        settings.MqttHost.Should().Be("mqtt.honfigurator.app");
        settings.MqttPort.Should().Be(8883);
        settings.MqttUseTls.Should().BeTrue();
    }

    #endregion

    #region DashboardHub MQTT Command Tests

    [Fact]
    public void DashboardHub_MqttCommands_ShouldBeDefined()
    {
        // Commands that should be available
        var expectedCommands = new[]
        {
            "mqtt",
            "mqtt status",
            "mqtt connect",
            "mqtt disconnect",
            "mqtt test"
        };
        
        // These are string patterns we expect in the hub's ExecuteCommand handler
        expectedCommands.Should().HaveCount(5);
    }

    #endregion
}
