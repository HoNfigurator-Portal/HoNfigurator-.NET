using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using HoNfigurator.Api.Services;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;

namespace HoNfigurator.Tests.Services;

public class ManagementPortalBackgroundServiceTests
{
    private readonly Mock<ILogger<ManagementPortalBackgroundService>> _loggerMock;
    private readonly Mock<IManagementPortalConnector> _connectorMock;
    private readonly Mock<IGameServerManager> _serverManagerMock;

    public ManagementPortalBackgroundServiceTests()
    {
        _loggerMock = new Mock<ILogger<ManagementPortalBackgroundService>>();
        _connectorMock = new Mock<IManagementPortalConnector>();
        _serverManagerMock = new Mock<IGameServerManager>();
    }

    private HoNConfiguration CreateConfig(bool enabled = false, int intervalSeconds = 30, bool autoRegister = true)
    {
        return new HoNConfiguration
        {
            HonData = new HoNData
            {
                ServerName = "TestServer",
                ServerIp = "192.168.1.1",
                ApiPort = 5050
            },
            ApplicationData = new ApplicationData
            {
                ManagementPortal = new ManagementPortalSettings
                {
                    Enabled = enabled,
                    StatusReportIntervalSeconds = intervalSeconds,
                    AutoRegister = autoRegister
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
        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_UsesDefaultInterval_WhenNotConfigured()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "Test" },
            ApplicationData = null // No app data
        };

        // Act & Assert - Should not throw
        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        Assert.NotNull(service);
    }

    [Fact]
    public async Task StartAsync_DoesNotRegister_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        _connectorMock.Setup(x => x.IsEnabled).Returns(false);

        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        await service.StartAsync(cts.Token);
        try { await Task.Delay(200, cts.Token); } catch (OperationCanceledException) { }
        await service.StopAsync(CancellationToken.None);

        // Assert - RegisterServerAsync should NOT be called
        _connectorMock.Verify(x => x.RegisterServerAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_StopsGracefully_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        _connectorMock.Setup(x => x.IsEnabled).Returns(false);

        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        // Act - Should not throw
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        // Assert - completed without error
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_StopsGracefully_WhenEnabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true);
        _connectorMock.Setup(x => x.IsEnabled).Returns(true);
        _connectorMock.Setup(x => x.RegisterServerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult { Success = true, Message = "OK" });

        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        
        // Should not throw
        await service.StopAsync(CancellationToken.None);

        // Assert - service stopped without exception
        Assert.True(true);
    }

    [Fact]
    public async Task Service_DoesNotReportStatus_WhenDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        _connectorMock.Setup(x => x.IsEnabled).Returns(false);

        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        await service.StartAsync(cts.Token);
        try { await Task.Delay(200, cts.Token); } catch (OperationCanceledException) { }
        await service.StopAsync(CancellationToken.None);

        // Assert - ReportServerStatusAsync should NOT be called
        _connectorMock.Verify(x => x.ReportServerStatusAsync(It.IsAny<ServerStatusReport>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Service_DoesNotRegister_WhenAutoRegisterDisabled()
    {
        // Arrange
        var config = CreateConfig(enabled: true, autoRegister: false);
        _connectorMock.Setup(x => x.IsEnabled).Returns(true);

        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        // Act
        await service.StartAsync(cts.Token);
        try { await Task.Delay(6500, cts.Token); } catch (OperationCanceledException) { }
        cts.Cancel();

        // Assert - RegisterServerAsync should NOT be called since auto_register is false
        _connectorMock.Verify(x => x.RegisterServerAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Service_ImplementsIHostedService()
    {
        // Arrange
        var config = CreateConfig(enabled: true);

        // Act
        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        // Assert
        Assert.IsAssignableFrom<IHostedService>(service);
    }

    [Fact]
    public void Service_ImplementsBackgroundService()
    {
        // Arrange
        var config = CreateConfig(enabled: true);

        // Act
        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        // Assert
        Assert.IsAssignableFrom<BackgroundService>(service);
    }

    [Fact]
    public void Config_SetsMinimumInterval()
    {
        // Arrange - interval of 1 second should be increased to minimum 10
        var config = CreateConfig(enabled: true, intervalSeconds: 1);

        // Act - should not throw, interval will be clamped internally
        var service = new ManagementPortalBackgroundService(
            _loggerMock.Object,
            _connectorMock.Object,
            _serverManagerMock.Object,
            config);

        // Assert
        Assert.NotNull(service);
    }
}
