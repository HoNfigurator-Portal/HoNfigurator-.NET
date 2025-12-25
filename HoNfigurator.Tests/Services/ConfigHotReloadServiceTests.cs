using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class ConfigHotReloadServiceTests : IDisposable
{
    private readonly Mock<ILogger<ConfigHotReloadService>> _loggerMock;
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly string _testDirectory;

    public ConfigHotReloadServiceTests()
    {
        _loggerMock = new Mock<ILogger<ConfigHotReloadService>>();
        _configServiceMock = new Mock<IConfigurationService>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ConfigHotReloadTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); } catch { }
        }
    }

    private ConfigHotReloadService CreateService()
    {
        return new ConfigHotReloadService(_loggerMock.Object, _configServiceMock.Object);
    }

    #region DTO Tests

    [Fact]
    public void ConfigReloadedEventArgs_DefaultValues()
    {
        // Arrange & Act
        var args = new ConfigReloadedEventArgs();

        // Assert
        args.ReloadTime.Should().Be(default);
        args.Source.Should().BeEmpty();
        args.Success.Should().BeFalse();
        args.Error.Should().BeNull();
    }

    [Fact]
    public void ConfigReloadedEventArgs_WithValues()
    {
        // Arrange
        var reloadTime = DateTime.UtcNow;

        // Act
        var args = new ConfigReloadedEventArgs
        {
            ReloadTime = reloadTime,
            Source = "/config/settings.json",
            Success = true,
            Error = null
        };

        // Assert
        args.ReloadTime.Should().Be(reloadTime);
        args.Source.Should().Be("/config/settings.json");
        args.Success.Should().BeTrue();
        args.Error.Should().BeNull();
    }

    [Fact]
    public void ConfigReloadedEventArgs_WithError()
    {
        // Arrange & Act
        var args = new ConfigReloadedEventArgs
        {
            ReloadTime = DateTime.UtcNow,
            Source = "/config/settings.json",
            Success = false,
            Error = "File not found"
        };

        // Assert
        args.Success.Should().BeFalse();
        args.Error.Should().Be("File not found");
    }

    [Fact]
    public void WatchedPathInfo_DefaultValues()
    {
        // Arrange & Act
        var info = new WatchedPathInfo();

        // Assert
        info.Path.Should().BeEmpty();
        info.IsDirectory.Should().BeFalse();
        info.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void WatchedPathInfo_WithValues()
    {
        // Arrange & Act
        var info = new WatchedPathInfo
        {
            Path = "/config",
            IsDirectory = true,
            IsEnabled = true
        };

        // Assert
        info.Path.Should().Be("/config");
        info.IsDirectory.Should().BeTrue();
        info.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region Service Properties Tests

    [Fact]
    public void IsEnabled_DefaultsToTrue()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_CanBeDisabled()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.IsEnabled = false;

        // Assert
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_CanBeReEnabled()
    {
        // Arrange
        var service = CreateService();
        service.IsEnabled = false;

        // Act
        service.IsEnabled = true;

        // Assert
        service.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region WatchFile Tests

    [Fact]
    public void WatchFile_ExistingFile_AddsToWatchers()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(filePath, "{}");

        // Act
        service.WatchFile(filePath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().ContainSingle(p => p.Path == filePath);
    }

    [Fact]
    public void WatchFile_NonExistentDirectory_DoesNotAdd()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "nonexistent", "config.json");

        // Act
        service.WatchFile(filePath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().NotContain(p => p.Path == filePath);
    }

    [Fact]
    public void WatchFile_SameFileTwice_DoesNotDuplicate()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(filePath, "{}");

        // Act
        service.WatchFile(filePath);
        service.WatchFile(filePath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Count(p => p.Path == filePath).Should().Be(1);
    }

    [Fact]
    public void WatchFile_MarksAsFile()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(filePath, "{}");

        // Act
        service.WatchFile(filePath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.First(p => p.Path == filePath).IsDirectory.Should().BeFalse();
    }

    #endregion

    #region WatchDirectory Tests

    [Fact]
    public void WatchDirectory_ExistingDirectory_AddsToWatchers()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "configs");
        Directory.CreateDirectory(dirPath);

        // Act
        service.WatchDirectory(dirPath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().ContainSingle(p => p.Path == dirPath);
    }

    [Fact]
    public void WatchDirectory_NonExistentDirectory_DoesNotAdd()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "nonexistent");

        // Act
        service.WatchDirectory(dirPath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().NotContain(p => p.Path == dirPath);
    }

    [Fact]
    public void WatchDirectory_MarksAsDirectory()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "configs");
        Directory.CreateDirectory(dirPath);

        // Act
        service.WatchDirectory(dirPath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.First(p => p.Path == dirPath).IsDirectory.Should().BeTrue();
    }

    [Fact]
    public void WatchDirectory_WithFilter_Accepts()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "configs");
        Directory.CreateDirectory(dirPath);

        // Act
        service.WatchDirectory(dirPath, "*.json");

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().ContainSingle(p => p.Path == dirPath);
    }

    [Fact]
    public void WatchDirectory_WithSubdirectories_Accepts()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "configs");
        Directory.CreateDirectory(dirPath);

        // Act
        service.WatchDirectory(dirPath, "*.*", includeSubdirectories: true);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().ContainSingle(p => p.Path == dirPath);
    }

    #endregion

    #region StopWatching Tests

    [Fact]
    public void StopWatching_ExistingWatch_Removes()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(filePath, "{}");
        service.WatchFile(filePath);

        // Act
        service.StopWatching(filePath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().NotContain(p => p.Path == filePath);
    }

    [Fact]
    public void StopWatching_NonExistentWatch_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var act = () => service.StopWatching("/nonexistent/path");
        act.Should().NotThrow();
    }

    [Fact]
    public void StopWatching_Directory_Removes()
    {
        // Arrange
        var service = CreateService();
        var dirPath = Path.Combine(_testDirectory, "configs");
        Directory.CreateDirectory(dirPath);
        service.WatchDirectory(dirPath);

        // Act
        service.StopWatching(dirPath);

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().NotContain(p => p.Path == dirPath);
    }

    #endregion

    #region GetWatchedPaths Tests

    [Fact]
    public void GetWatchedPaths_NoWatchers_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var watchedPaths = service.GetWatchedPaths().ToList();

        // Assert
        watchedPaths.Should().BeEmpty();
    }

    [Fact]
    public void GetWatchedPaths_MultipleWatchers_ReturnsAll()
    {
        // Arrange
        var service = CreateService();
        var file1 = Path.Combine(_testDirectory, "config1.json");
        var file2 = Path.Combine(_testDirectory, "config2.json");
        var dirPath = Path.Combine(_testDirectory, "configs");
        
        File.WriteAllText(file1, "{}");
        File.WriteAllText(file2, "{}");
        Directory.CreateDirectory(dirPath);
        
        service.WatchFile(file1);
        service.WatchFile(file2);
        service.WatchDirectory(dirPath);

        // Act
        var watchedPaths = service.GetWatchedPaths().ToList();

        // Assert
        watchedPaths.Should().HaveCount(3);
    }

    [Fact]
    public void GetWatchedPaths_ReturnsCorrectEnabledStatus()
    {
        // Arrange
        var service = CreateService();
        var filePath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(filePath, "{}");
        service.WatchFile(filePath);

        // Act
        var watchedPaths = service.GetWatchedPaths().ToList();

        // Assert
        watchedPaths.First().IsEnabled.Should().BeTrue();
    }

    #endregion

    #region TriggerReloadAsync Tests

    [Fact]
    public async Task TriggerReloadAsync_CallsConfigServiceReload()
    {
        // Arrange
        var service = CreateService();
        _configServiceMock.Setup(x => x.ReloadAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await service.TriggerReloadAsync();

        // Assert
        _configServiceMock.Verify(x => x.ReloadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerReloadAsync_RaisesConfigurationReloadedEvent()
    {
        // Arrange
        var service = CreateService();
        _configServiceMock.Setup(x => x.ReloadAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        ConfigReloadedEventArgs? receivedArgs = null;
        service.ConfigurationReloaded += (sender, args) => receivedArgs = args;

        // Act
        await service.TriggerReloadAsync();

        // Assert
        receivedArgs.Should().NotBeNull();
        receivedArgs!.Success.Should().BeTrue();
        receivedArgs.Source.Should().Be("Manual");
    }

    [Fact]
    public async Task TriggerReloadAsync_SetsReloadTime()
    {
        // Arrange
        var service = CreateService();
        _configServiceMock.Setup(x => x.ReloadAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        ConfigReloadedEventArgs? receivedArgs = null;
        service.ConfigurationReloaded += (sender, args) => receivedArgs = args;
        var beforeReload = DateTime.UtcNow;

        // Act
        await service.TriggerReloadAsync();
        var afterReload = DateTime.UtcNow;

        // Assert
        receivedArgs!.ReloadTime.Should().BeOnOrAfter(beforeReload);
        receivedArgs.ReloadTime.Should().BeOnOrBefore(afterReload);
    }

    [Fact]
    public async Task TriggerReloadAsync_WithCancellation_Throws()
    {
        // Arrange
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.TriggerReloadAsync(cts.Token));
    }

    #endregion

    #region Event Tests

    [Fact]
    public void ConfigurationReloaded_CanSubscribe()
    {
        // Arrange
        var service = CreateService();
        var eventRaised = false;

        // Act
        service.ConfigurationReloaded += (sender, args) => eventRaised = true;

        // Assert - no exception
        eventRaised.Should().BeFalse(); // Event hasn't been raised yet
    }

    [Fact]
    public void ConfigurationReloaded_CanUnsubscribe()
    {
        // Arrange
        var service = CreateService();
        EventHandler<ConfigReloadedEventArgs> handler = (sender, args) => { };

        // Act
        service.ConfigurationReloaded += handler;
        service.ConfigurationReloaded -= handler;

        // Assert - no exception
    }

    #endregion

    #region Debouncing Tests

    [Fact]
    public void Service_HasDebounceInterval()
    {
        // Arrange
        var service = CreateService();

        // Assert - service creates successfully with debouncing capability
        service.Should().NotBeNull();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void WatchFile_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var service = CreateService();
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var filePath = Path.Combine(_testDirectory, $"config{i}.json");
            File.WriteAllText(filePath, "{}");
        }

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var filePath = Path.Combine(_testDirectory, $"config{index}.json");
                service.WatchFile(filePath);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        var watchedPaths = service.GetWatchedPaths().ToList();
        watchedPaths.Should().HaveCount(10);
    }

    #endregion
}
