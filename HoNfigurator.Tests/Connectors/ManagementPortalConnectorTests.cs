using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Tests.Connectors;

public class ManagementPortalConnectorTests
{
    private readonly Mock<ILogger<ManagementPortalConnector>> _loggerMock;

    public ManagementPortalConnectorTests()
    {
        _loggerMock = new Mock<ILogger<ManagementPortalConnector>>();
    }

    private HoNConfiguration CreateConfig(bool enabled = false, string? serverIp = null, string? serverName = null)
    {
        return new HoNConfiguration
        {
            HonData = new HoNData
            {
                ServerName = serverName ?? "TestServer",
                ServerIp = serverIp,
                ApiPort = 5050
            },
            ApplicationData = new ApplicationData
            {
                ManagementPortal = new ManagementPortalSettings
                {
                    Enabled = enabled,
                    PortalUrl = "https://management.honfigurator.app:3001",
                    MqttHost = "mqtt.honfigurator.app",
                    MqttPort = 8883,
                    MqttUseTls = true
                }
            }
        };
    }

    [Fact]
    public void Constructor_InitializesCorrectly()
    {
        // Arrange
        var config = CreateConfig(enabled: true);

        // Act
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Assert
        Assert.True(connector.IsEnabled);
        Assert.False(connector.IsRegistered);
        Assert.Equal("TestServer", connector.ServerName);
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);

        // Act
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Assert
        Assert.False(connector.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenEnabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true);

        // Act
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Assert
        Assert.True(connector.IsEnabled);
    }

    [Fact]
    public async Task RegisterServerAsync_ReturnsFailure_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.RegisterServerAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("disabled", result.Message);
    }

    [Fact]
    public async Task RegisterServerAsync_ReturnsFailure_WhenServerIpNotConfigured()
    {
        // Arrange
        var config = CreateConfig(enabled: true, serverIp: null);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.RegisterServerAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task RegisterServerAsync_ReturnsFailure_WhenServerNameNotConfigured()
    {
        // Arrange
        var config = CreateConfig(enabled: true, serverIp: "192.168.1.1", serverName: "");
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.RegisterServerAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task PingManagementPortalAsync_ReturnsFalse_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.PingManagementPortalAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateConnectionAsync_ReturnsFalse_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.ValidateConnectionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetDiscordUserInfoAsync_ReturnsNull_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act
        var result = await connector.GetDiscordUserInfoAsync("123456789");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ReportServerStatusAsync_DoesNotThrow_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);
        var status = new ServerStatusReport
        {
            ServerName = "TestServer",
            ServerIp = "192.168.1.1",
            Status = "Online"
        };

        // Act & Assert - should not throw
        await connector.ReportServerStatusAsync(status);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Act & Assert - should not throw
        connector.Dispose();
        connector.Dispose();
    }

    [Fact]
    public void ServerName_ReturnsConfiguredName()
    {
        // Arrange
        var config = CreateConfig(enabled: true, serverName: "MyTestServer");
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Assert
        Assert.Equal("MyTestServer", connector.ServerName);
    }

    [Fact]
    public void ServerAddress_ReturnsConfiguredIp()
    {
        // Arrange
        var config = CreateConfig(enabled: true, serverIp: "10.0.0.1");
        using var connector = new ManagementPortalConnector(_loggerMock.Object, config);

        // Assert
        Assert.Equal("10.0.0.1", connector.ServerAddress);
    }
}

public class ManagementPortalSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new ManagementPortalSettings();

        // Assert
        Assert.False(settings.Enabled);
        Assert.Equal("https://management.honfigurator.app:3001", settings.PortalUrl);
        Assert.Equal("mqtt.honfigurator.app", settings.MqttHost);
        Assert.Equal(8883, settings.MqttPort);
        Assert.True(settings.MqttUseTls);
        Assert.Equal(30, settings.StatusReportIntervalSeconds);
        Assert.True(settings.AutoRegister);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        // Arrange & Act
        var settings = new ManagementPortalSettings
        {
            Enabled = true,
            PortalUrl = "https://custom.portal.com",
            MqttHost = "custom.mqtt.host",
            MqttPort = 1883,
            MqttUseTls = false,
            DiscordUserId = "123456789",
            ServerId = "server-001",
            ApiKey = "my-api-key",
            StatusReportIntervalSeconds = 60,
            AutoRegister = false,
            CaCertificatePath = "/path/to/ca.crt",
            ClientCertificatePath = "/path/to/client.crt",
            ClientKeyPath = "/path/to/client.key"
        };

        // Assert
        Assert.True(settings.Enabled);
        Assert.Equal("https://custom.portal.com", settings.PortalUrl);
        Assert.Equal("custom.mqtt.host", settings.MqttHost);
        Assert.Equal(1883, settings.MqttPort);
        Assert.False(settings.MqttUseTls);
        Assert.Equal("123456789", settings.DiscordUserId);
        Assert.Equal("server-001", settings.ServerId);
        Assert.Equal("my-api-key", settings.ApiKey);
        Assert.Equal(60, settings.StatusReportIntervalSeconds);
        Assert.False(settings.AutoRegister);
        Assert.Equal("/path/to/ca.crt", settings.CaCertificatePath);
        Assert.Equal("/path/to/client.crt", settings.ClientCertificatePath);
        Assert.Equal("/path/to/client.key", settings.ClientKeyPath);
    }
}

public class RegistrationResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new RegistrationResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Message);
        Assert.Null(result.ServerName);
        Assert.Null(result.ServerAddress);
        Assert.Null(result.Error);
    }

    [Fact]
    public void SuccessResult_HasCorrectValues()
    {
        // Arrange & Act
        var result = new RegistrationResult
        {
            Success = true,
            Message = "Registration successful",
            ServerName = "TestServer",
            ServerAddress = "192.168.1.1:5050"
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Registration successful", result.Message);
        Assert.Equal("TestServer", result.ServerName);
        Assert.Equal("192.168.1.1:5050", result.ServerAddress);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FailureResult_HasCorrectValues()
    {
        // Arrange & Act
        var result = new RegistrationResult
        {
            Success = false,
            Message = "Registration failed",
            Error = "Connection timeout"
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Registration failed", result.Message);
        Assert.Equal("Connection timeout", result.Error);
    }
}

public class ServerStatusReportTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var report = new ServerStatusReport();

        // Assert
        Assert.Equal(string.Empty, report.ServerName);
        Assert.Equal(string.Empty, report.ServerIp);
        Assert.Equal(0, report.ApiPort);
        Assert.Equal(string.Empty, report.Status);
        Assert.Equal(0, report.TotalServers);
        Assert.Equal(0, report.RunningServers);
        Assert.Equal(0, report.PlayersOnline);
        Assert.Null(report.HonVersion);
        Assert.Null(report.HonfiguratorVersion);
        Assert.True(report.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Act
        var report = new ServerStatusReport
        {
            ServerName = "TestServer",
            ServerIp = "192.168.1.1",
            ApiPort = 5050,
            Status = "Online",
            TotalServers = 5,
            RunningServers = 3,
            PlayersOnline = 24,
            HonVersion = "4.10.1",
            HonfiguratorVersion = "1.0.0",
            Timestamp = timestamp
        };

        // Assert
        Assert.Equal("TestServer", report.ServerName);
        Assert.Equal("192.168.1.1", report.ServerIp);
        Assert.Equal(5050, report.ApiPort);
        Assert.Equal("Online", report.Status);
        Assert.Equal(5, report.TotalServers);
        Assert.Equal(3, report.RunningServers);
        Assert.Equal(24, report.PlayersOnline);
        Assert.Equal("4.10.1", report.HonVersion);
        Assert.Equal("1.0.0", report.HonfiguratorVersion);
        Assert.Equal(timestamp, report.Timestamp);
    }
}

public class DiscordUserInfoTests
{
    [Fact]
    public void DefaultValues_AreNull()
    {
        // Arrange & Act
        var info = new DiscordUserInfo();

        // Assert
        Assert.Null(info.Id);
        Assert.Null(info.Username);
        Assert.Null(info.Discriminator);
        Assert.Null(info.Avatar);
        Assert.Null(info.GlobalName);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        // Arrange & Act
        var info = new DiscordUserInfo
        {
            Id = "123456789",
            Username = "testuser",
            Discriminator = "1234",
            Avatar = "avatar_hash",
            GlobalName = "Test User"
        };

        // Assert
        Assert.Equal("123456789", info.Id);
        Assert.Equal("testuser", info.Username);
        Assert.Equal("1234", info.Discriminator);
        Assert.Equal("avatar_hash", info.Avatar);
        Assert.Equal("Test User", info.GlobalName);
    }
}
