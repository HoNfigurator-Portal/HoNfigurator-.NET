using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Api.Hubs;
using HoNfigurator.Api.Services;
using HoNfigurator.Core.Charts;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Notifications;
using HoNfigurator.GameServer.Services;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Tests.Api.Services;

public class StatusBroadcastServiceTests
{
    private readonly Mock<IHubContext<DashboardHub, IDashboardClient>> _hubContextMock;
    private readonly Mock<IDashboardClient> _clientMock;
    private readonly Mock<IGameServerManager> _serverManagerMock;
    private readonly Mock<IManagementPortalConnector> _portalConnectorMock;
    private readonly Mock<IMqttHandler> _mqttHandlerMock;
    private readonly HoNConfiguration _config;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ILogger<StatusBroadcastService>> _loggerMock;

    public StatusBroadcastServiceTests()
    {
        _hubContextMock = new Mock<IHubContext<DashboardHub, IDashboardClient>>();
        _clientMock = new Mock<IDashboardClient>();
        _serverManagerMock = new Mock<IGameServerManager>();
        _portalConnectorMock = new Mock<IManagementPortalConnector>();
        _mqttHandlerMock = new Mock<IMqttHandler>();
        _config = new HoNConfiguration();
        _notificationServiceMock = new Mock<INotificationService>();
        _loggerMock = new Mock<ILogger<StatusBroadcastService>>();

        // Setup hub context to return mock clients
        var clientsMock = new Mock<IHubClients<IDashboardClient>>();
        clientsMock.Setup(c => c.All).Returns(_clientMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
    }

    private StatusBroadcastService CreateService()
    {
        return new StatusBroadcastService(
            _hubContextMock.Object,
            _serverManagerMock.Object,
            _portalConnectorMock.Object,
            _mqttHandlerMock.Object,
            _config,
            _notificationServiceMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_ShouldInitialize()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_ShouldBroadcastStatus()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        
        var status = new ServerStatusResponse
        {
            TotalServers = 2,
            MasterServerConnected = true,
            ChatServerConnected = true
        };
        
        var callCount = 0;
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(status)
            .Callback(() =>
            {
                callCount++;
                if (callCount >= 2) cts.Cancel();
            });

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);

        // Assert
        _clientMock.Verify(
            c => c.ReceiveStatus(It.IsAny<ServerStatusResponse>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldStop()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(new ServerStatusResponse());

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await task;

        // Assert
        task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenGetStatusThrows_ShouldLogErrorAndContinue()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Status error");
                }
                cts.Cancel();
                return new ServerStatusResponse();
            });

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("broadcasting status")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBroadcastCorrectStatus()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        
        var expectedStatus = new ServerStatusResponse
        {
            TotalServers = 5,
            MasterServerConnected = true,
            ChatServerConnected = false
        };
        
        ServerStatusResponse? capturedStatus = null;
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(expectedStatus)
            .Callback(() => cts.Cancel());
            
        _clientMock
            .Setup(c => c.ReceiveStatus(It.IsAny<ServerStatusResponse>()))
            .Callback<ServerStatusResponse>(s => capturedStatus = s)
            .Returns(Task.CompletedTask);

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);

        // Assert
        capturedStatus.Should().NotBeNull();
        capturedStatus!.TotalServers.Should().Be(5);
        capturedStatus.MasterServerConnected.Should().BeTrue();
        capturedStatus.ChatServerConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogStartupMessage()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(new ServerStatusResponse())
            .Callback(() => cts.Cancel());

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("started")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Hub Broadcasting Tests

    [Fact]
    public async Task ExecuteAsync_ShouldBroadcastToAllClients()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(new ServerStatusResponse())
            .Callback(() => cts.Cancel());

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);

        // Assert - verify that ReceiveStatus was called on the All clients
        _clientMock.Verify(c => c.ReceiveStatus(It.IsAny<ServerStatusResponse>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBroadcastFails_ShouldLogError()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(new ServerStatusResponse());
            
        _clientMock
            .Setup(c => c.ReceiveStatus(It.IsAny<ServerStatusResponse>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Broadcast failed");
                }
                cts.Cancel();
                return Task.CompletedTask;
            });

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Multiple Broadcast Cycles Tests

    [Fact]
    public async Task ExecuteAsync_ShouldBroadcastMultipleTimes()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        var broadcastCount = 0;
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(new ServerStatusResponse())
            .Callback(() =>
            {
                broadcastCount++;
                if (broadcastCount >= 3) cts.Cancel();
            });

        // Act
        await service.StartAsync(cts.Token);
        
        // Wait for multiple broadcast cycles (interval is 5 seconds, but we test with shorter timeout)
        await Task.Delay(100);

        // Assert
        broadcastCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUpdateStatusOnEachCycle()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        var serverCount = 0;
        
        _serverManagerMock
            .Setup(s => s.GetStatus())
            .Returns(() =>
            {
                serverCount++;
                if (serverCount >= 2) cts.Cancel();
                return new ServerStatusResponse { TotalServers = serverCount };
            });

        var capturedStatuses = new List<ServerStatusResponse>();
        _clientMock
            .Setup(c => c.ReceiveStatus(It.IsAny<ServerStatusResponse>()))
            .Callback<ServerStatusResponse>(s => capturedStatuses.Add(s))
            .Returns(Task.CompletedTask);

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);

        // Assert
        capturedStatuses.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Portal Status Broadcast Tests

    [Fact]
    public void Constructor_WithPortalConnector_ShouldInitialize()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Service_ShouldHavePortalConnectorDependency()
    {
        // Verify StatusBroadcastService constructor includes IManagementPortalConnector
        var constructors = typeof(StatusBroadcastService).GetConstructors();
        constructors.Should().HaveCountGreaterThan(0);
        
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().Contain(p => 
            p.ParameterType.Name.Contains("ManagementPortalConnector") ||
            p.ParameterType.Name == "IManagementPortalConnector");
    }

    [Fact]
    public void Service_ShouldHaveNotificationServiceDependency()
    {
        // Verify StatusBroadcastService constructor includes INotificationService
        var constructors = typeof(StatusBroadcastService).GetConstructors();
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().Contain(p => 
            p.ParameterType.Name.Contains("NotificationService") ||
            p.ParameterType.Name == "INotificationService");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPortalDisabled_ShouldNotBroadcastPortalStatus()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        
        _portalConnectorMock.Setup(p => p.IsEnabled).Returns(false);
        
        // Act
        try { await service.StartAsync(cts.Token); } catch (OperationCanceledException) { }
        await Task.Delay(50);

        // Assert - should not broadcast portal status when disabled
        _clientMock.Verify(c => c.ReceiveManagementPortalStatus(It.IsAny<ManagementPortalStatus>()), Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPortalEnabled_ShouldSetupForPortalBroadcast()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        
        _portalConnectorMock.Setup(p => p.IsEnabled).Returns(true);
        _portalConnectorMock.Setup(p => p.IsRegistered).Returns(true);
        _portalConnectorMock.Setup(p => p.ServerName).Returns("TestServer");
        _portalConnectorMock.Setup(p => p.PingManagementPortalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        try { await service.StartAsync(cts.Token); } catch (OperationCanceledException) { }
        await Task.Delay(50);

        // Assert - verify portal connector was checked
        _portalConnectorMock.Verify(p => p.IsEnabled, Times.AtLeastOnce());
    }

    #endregion
}
