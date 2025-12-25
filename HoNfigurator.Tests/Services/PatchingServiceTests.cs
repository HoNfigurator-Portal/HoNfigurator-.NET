using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

public class PatchingServiceTests
{
    private readonly Mock<ILogger<PatchingService>> _loggerMock;
    private readonly HoNConfiguration _config;

    public PatchingServiceTests()
    {
        _loggerMock = new Mock<ILogger<PatchingService>>();
        _config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonInstallDirectory = @"C:\Games\HoN",
                ManVersion = "4.10.1",
                MasterServer = "test.master.server"
            }
        };
    }

    [Fact]
    public void Constructor_ShouldInitialize_WithCorrectDefaults()
    {
        // Arrange & Act
        var service = new PatchingService(_loggerMock.Object, _config);

        // Assert
        service.IsPatching.Should().BeFalse();
        // CurrentVersion may be populated from ManVersion if no manifest exists
        // LatestVersion starts null until we check for updates
        service.LatestVersion.Should().BeNull();
    }

    [Fact]
    public void GetLocalVersion_ShouldReturnNull_WhenInstallDirectoryNotExists()
    {
        // Arrange - use empty HoNData without ManVersion
        var config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonInstallDirectory = @"C:\NonExistent\Path",
                ManVersion = null // No fallback version
            }
        };
        var service = new PatchingService(_loggerMock.Object, config);

        // Act
        var version = service.GetLocalVersion();

        // Assert - should return null when no manifest and no ManVersion
        version.Should().BeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnError_WhenMasterServerNotConfigured()
    {
        // Arrange
        _config.HonData.MasterServer = null;
        var service = new PatchingService(_loggerMock.Object, _config);

        // Act
        var result = await service.CheckForUpdatesAsync();

        // Assert
        result.Should().NotBeNull();
        result.UpdateAvailable.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApplyPatchAsync_ShouldReturnError_WhenUrlIsNull()
    {
        // Arrange
        var service = new PatchingService(_loggerMock.Object, _config);

        // Act
        var result = await service.ApplyPatchAsync(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApplyPatchAsync_ShouldReturnError_WhenUrlIsEmpty()
    {
        // Arrange
        var service = new PatchingService(_loggerMock.Object, _config);

        // Act
        var result = await service.ApplyPatchAsync(string.Empty);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty(); // Error about invalid URI
    }
}

public class PatchCheckResultTests
{
    [Fact]
    public void PatchCheckResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new PatchCheckResult();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        result.CurrentVersion.Should().BeNull();
        result.LatestVersion.Should().BeNull();
        result.PatchUrl.Should().BeNull();
        result.PatchSize.Should().Be(0);
        result.Checksum.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void PatchResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new PatchResult();

        // Assert
        result.Success.Should().BeFalse();
        result.NewVersion.Should().BeNull();
        result.Error.Should().BeNull();
        result.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void PatchProgress_ShouldInitialize_WithDefaults()
    {
        // Act
        var progress = new PatchProgress();

        // Assert
        progress.Stage.Should().BeEmpty();
        progress.PercentComplete.Should().Be(0);
        progress.BytesDownloaded.Should().Be(0);
        progress.TotalBytes.Should().Be(0);
    }
}
