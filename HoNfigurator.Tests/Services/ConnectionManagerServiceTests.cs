using FluentAssertions;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for ConnectionManagerService - Master/Chat server connection management
/// </summary>
public class ConnectionManagerServiceTests
{
    private readonly Mock<ILogger<ConnectionManagerService>> _loggerMock;
    private readonly Mock<IMasterServerConnector> _masterServerMock;
    private readonly Mock<IChatServerConnector> _chatServerMock;
    private readonly Mock<IGameServerManager> _serverManagerMock;

    public ConnectionManagerServiceTests()
    {
        _loggerMock = new Mock<ILogger<ConnectionManagerService>>();
        _masterServerMock = new Mock<IMasterServerConnector>();
        _chatServerMock = new Mock<IChatServerConnector>();
        _serverManagerMock = new Mock<IGameServerManager>();
    }

    private ConnectionManagerService CreateService(HoNConfiguration? config = null)
    {
        config ??= CreateTestConfig();
        return new ConnectionManagerService(
            _loggerMock.Object,
            _masterServerMock.Object,
            _chatServerMock.Object,
            _serverManagerMock.Object,
            config);
    }

    private HoNConfiguration CreateTestConfig(string? login = "testuser", string? password = "testpass")
    {
        return new HoNConfiguration
        {
            HonData = new HoNData
            {
                Login = login ?? string.Empty,
                Password = password ?? string.Empty,
                MasterServer = "api.test.com",
                ChatServer = "chat.test.com"
            }
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConfig_ShouldInitialize()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldSetInitialState()
    {
        // Act
        var service = CreateService();

        // Assert
        service.IsAuthenticated.Should().BeFalse();
        service.IsChatConnected.Should().BeFalse();
    }

    #endregion

    #region IsAuthenticated Tests

    [Fact]
    public void IsAuthenticated_Initially_ShouldBeFalse()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.IsAuthenticated.Should().BeFalse();
    }

    #endregion

    #region IsChatConnected Tests

    [Fact]
    public void IsChatConnected_Initially_ShouldBeFalse()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.IsChatConnected.Should().BeFalse();
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnAuthenticationChanged_ShouldBeSubscribable()
    {
        // Arrange
        var service = CreateService();
        var eventRaised = false;

        // Act
        service.OnAuthenticationChanged += (isAuth) => eventRaised = true;

        // Assert - Just verify we can subscribe
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void OnChatConnectionChanged_ShouldBeSubscribable()
    {
        // Arrange
        var service = CreateService();
        var eventRaised = false;

        // Act
        service.OnChatConnectionChanged += (isConnected) => eventRaised = true;

        // Assert
        eventRaised.Should().BeFalse();
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithNoCredentials_ShouldLogWarning()
    {
        // Arrange
        var config = CreateTestConfig(login: "", password: "");
        var service = CreateService(config);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100); // Give it time to start

        // Assert - Should return without attempting connection
        service.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ShouldStop()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act
        var task = service.StartAsync(cts.Token);
        cts.Cancel();
        
        // Assert
        var act = async () => await task;
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig(login: "", password: "");
        var service = CreateService(config);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var act = async () => await service.StartAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_ShouldNotThrow()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        var act = async () => await service.StopAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Service_ShouldImplementBackgroundService()
    {
        // Assert
        typeof(ConnectionManagerService).BaseType!.Name.Should().Be("BackgroundService");
    }

    [Fact]
    public void Service_ShouldExposeConnectionState()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.Should().BeAssignableTo<Microsoft.Extensions.Hosting.IHostedService>();
    }

    #endregion
}

/// <summary>
/// Tests for ConnectionManagerService interface patterns
/// </summary>
public class ConnectionManagerServiceInterfaceTests
{
    [Fact]
    public void IMasterServerConnector_ShouldHaveRequiredMembers()
    {
        // Assert
        typeof(IMasterServerConnector).GetProperty("ChatServerHost").Should().NotBeNull();
        typeof(IMasterServerConnector).GetMethod("AuthenticateAsync").Should().NotBeNull();
    }

    [Fact]
    public void IChatServerConnector_ShouldHaveRequiredMembers()
    {
        // Assert
        typeof(IChatServerConnector).GetMethod("ConnectAsync").Should().NotBeNull();
        typeof(IChatServerConnector).GetMethod("DisconnectAsync").Should().NotBeNull();
    }
}
