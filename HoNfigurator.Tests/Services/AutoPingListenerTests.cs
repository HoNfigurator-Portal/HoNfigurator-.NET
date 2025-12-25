using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Network;

namespace HoNfigurator.Tests.Services;

public class AutoPingListenerTests
{
    private readonly Mock<ILogger<AutoPingListener>> _loggerMock;
    private readonly HoNConfiguration _config;

    public AutoPingListenerTests()
    {
        _loggerMock = new Mock<ILogger<AutoPingListener>>();
        _config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                StartingGamePort = 11000,
                EnableProxy = false
            }
        };
    }

    [Fact]
    public void Constructor_ShouldCalculateCorrectPort_WhenProxyDisabled()
    {
        // Arrange & Act
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Assert
        listener.Port.Should().Be(10999); // StartingGamePort - 1
    }

    [Fact]
    public void Constructor_ShouldCalculateCorrectPort_WhenProxyEnabled()
    {
        // Arrange
        _config.HonData.EnableProxy = true;

        // Act
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Assert
        listener.Port.Should().Be(20999); // (StartingGamePort - 1) + 10000
    }

    [Fact]
    public void IsRunning_ShouldBeFalse_WhenNotStarted()
    {
        // Arrange
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Assert
        listener.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void PacketCount_ShouldBeZero_WhenNotStarted()
    {
        // Arrange
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Assert
        listener.PacketCount.Should().Be(0);
    }

    [Fact]
    public void CheckHealth_ShouldReturnFalse_WhenNotRunning()
    {
        // Arrange
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Act
        var isHealthy = listener.CheckHealth();

        // Assert
        isHealthy.Should().BeFalse();
    }

    [Fact]
    public void Stop_ShouldNotThrow_WhenNotStarted()
    {
        // Arrange
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Act
        var action = () => listener.Stop();

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldNotThrow_WhenNotStarted()
    {
        // Arrange
        var listener = new AutoPingListener(_loggerMock.Object, _config);

        // Act
        var action = () => listener.Dispose();

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void LastActivity_ShouldBeSet_OnConstruction()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;

        // Act
        var listener = new AutoPingListener(_loggerMock.Object, _config);
        var afterCreate = DateTime.UtcNow;

        // Assert
        listener.LastActivity.Should().BeOnOrAfter(beforeCreate);
        listener.LastActivity.Should().BeOnOrBefore(afterCreate);
    }
}
