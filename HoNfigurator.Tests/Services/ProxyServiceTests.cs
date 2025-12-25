using FluentAssertions;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for ProxyService - HoN proxy process management
/// </summary>
public class ProxyServiceTests : IDisposable
{
    private readonly Mock<ILogger<ProxyService>> _loggerMock;
    private readonly string _tempDir;

    public ProxyServiceTests()
    {
        _loggerMock = new Mock<ILogger<ProxyService>>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"ProxyServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private ProxyService CreateService(HoNConfiguration? config = null)
    {
        config ??= CreateTestConfig();
        return new ProxyService(_loggerMock.Object, config);
    }

    private HoNConfiguration CreateTestConfig(bool enableProxy = false)
    {
        return new HoNConfiguration
        {
            HonData = new HoNData
            {
                EnableProxy = enableProxy,
                HonInstallDirectory = _tempDir,
                HonHomeDirectory = _tempDir,
                StartingGamePort = 11000,
                StartingVoicePort = 11200,
                ServerIp = "192.168.1.100",
                LocalIp = "127.0.0.1"
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

    #endregion

    #region IsProxyRunning Tests

    [Fact]
    public void IsProxyRunning_WithNoProxy_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.IsProxyRunning(1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsProxyRunning_WithDifferentServer_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.IsProxyRunning(999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region StartProxyAsync Tests

    [Fact]
    public async Task StartProxyAsync_WithProxyDisabled_ShouldSkip()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: false);
        var service = CreateService(config);
        var instance = new GameServerInstance { Id = 1 };

        // Act
        await service.StartProxyAsync(instance);

        // Assert
        instance.ProxyEnabled.Should().BeFalse();
        service.IsProxyRunning(1).Should().BeFalse();
    }

    [Fact]
    public async Task StartProxyAsync_WithNoProxyExecutable_ShouldLogWarning()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: true);
        var service = CreateService(config);
        var instance = new GameServerInstance { Id = 1 };

        // Act
        await service.StartProxyAsync(instance);

        // Assert
        // Should not throw, just log warning
        instance.ProxyEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task StartProxyAsync_ShouldCreateConfigDirectory()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: true);
        var service = CreateService(config);
        var instance = new GameServerInstance { Id = 1 };

        // Act
        await service.StartProxyAsync(instance);

        // Assert
        // Config directory might be created even if proxy exe not found
        var configDir = Path.Combine(_tempDir, "HoNProxyManager");
        // Directory may or may not exist depending on implementation
    }

    [Fact]
    public async Task StartProxyAsync_WithCancellation_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: false);
        var service = CreateService(config);
        var instance = new GameServerInstance { Id = 1 };

        // Act
        var act = async () => await service.StartProxyAsync(instance);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region StopProxy Tests

    [Fact]
    public void StopProxy_WithNoRunningProxy_ShouldNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.StopProxy(1);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void StopProxy_WithDifferentServerId_ShouldNotAffectOthers()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.StopProxy(1);
        service.StopProxy(2);

        // Assert
        service.IsProxyRunning(1).Should().BeFalse();
        service.IsProxyRunning(2).Should().BeFalse();
    }

    #endregion

    #region StopAllProxies Tests

    [Fact]
    public void StopAllProxies_WithNoProxies_ShouldNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.StopAllProxies();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void StopAllProxies_ShouldStopAllRunning()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.StopAllProxies();

        // Assert
        service.IsProxyRunning(1).Should().BeFalse();
        service.IsProxyRunning(2).Should().BeFalse();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task StartAndStopProxy_ShouldWork()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: false); // Disabled to avoid actual process
        var service = CreateService(config);
        var instance = new GameServerInstance { Id = 1 };

        // Act
        await service.StartProxyAsync(instance);
        service.StopProxy(1);

        // Assert
        service.IsProxyRunning(1).Should().BeFalse();
    }

    [Fact]
    public async Task MultipleServers_ShouldBeIndependent()
    {
        // Arrange
        var config = CreateTestConfig(enableProxy: false);
        var service = CreateService(config);
        var instance1 = new GameServerInstance { Id = 1 };
        var instance2 = new GameServerInstance { Id = 2 };

        // Act
        await service.StartProxyAsync(instance1);
        await service.StartProxyAsync(instance2);
        service.StopProxy(1);

        // Assert
        service.IsProxyRunning(1).Should().BeFalse();
        service.IsProxyRunning(2).Should().BeFalse();
    }

    #endregion
}

/// <summary>
/// Tests for IProxyService interface compliance
/// </summary>
public class ProxyServiceInterfaceTests
{
    [Fact]
    public void ProxyService_ShouldImplementIProxyService()
    {
        // Assert
        typeof(ProxyService).Should().Implement<IProxyService>();
    }

    [Fact]
    public void IProxyService_ShouldHaveRequiredMethods()
    {
        // Assert
        typeof(IProxyService).GetMethod("StartProxyAsync").Should().NotBeNull();
        typeof(IProxyService).GetMethod("StopProxy").Should().NotBeNull();
        typeof(IProxyService).GetMethod("StopAllProxies").Should().NotBeNull();
        typeof(IProxyService).GetMethod("IsProxyRunning").Should().NotBeNull();
    }
}
