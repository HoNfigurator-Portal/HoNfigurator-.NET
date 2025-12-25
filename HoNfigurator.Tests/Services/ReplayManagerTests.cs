using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

public class ReplayManagerTests
{
    private readonly Mock<ILogger<ReplayManager>> _loggerMock;
    private readonly string _testReplaysPath;
    private readonly ReplayManager _manager;

    public ReplayManagerTests()
    {
        _loggerMock = new Mock<ILogger<ReplayManager>>();
        _testReplaysPath = Path.Combine(Path.GetTempPath(), "HoNfigurator_Tests", $"replays_{Guid.NewGuid()}");
        
        // Create test directory
        if (!Directory.Exists(_testReplaysPath))
            Directory.CreateDirectory(_testReplaysPath);

        _manager = new ReplayManager(_loggerMock.Object, _testReplaysPath);
    }

    [Fact]
    public void Constructor_ShouldInitialize_WithValidPath()
    {
        // Assert
        _manager.Should().NotBeNull();
    }

    [Fact]
    public void GetStats_ShouldReturnStats()
    {
        // Act
        var stats = _manager.GetStats();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalReplays.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void DeleteReplay_ShouldReturnFalse_WhenFileNotExists()
    {
        // Act
        var result = _manager.DeleteReplay("nonexistent.honreplay");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ArchiveOldReplaysAsync_ShouldNotThrow_WhenNoReplays()
    {
        // Act
        var action = async () => await _manager.ArchiveOldReplaysAsync(30);

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CleanupOldReplaysAsync_ShouldNotThrow_WhenNoReplays()
    {
        // Act
        var action = async () => await _manager.CleanupOldReplaysAsync(90);

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public void Configure_ShouldSetMasterServerUrl()
    {
        // Act
        var action = () => _manager.Configure("http://test.server", "user", "pass");

        // Assert - no exception means success
        action.Should().NotThrow();
    }

    [Fact]
    public async Task ProcessPendingUploadsAsync_ShouldReturnResult_WhenNoPending()
    {
        // Act
        var result = await _manager.ProcessPendingUploadsAsync();

        // Assert - returns BatchReplayUploadResult
        result.Should().NotBeNull();
        result.Uploaded.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void GetReplays_ShouldReturnEmptyList_WhenNoReplays()
    {
        // Arrange - use a new empty directory
        var emptyPath = Path.Combine(Path.GetTempPath(), "HoNfigurator_Tests", $"empty_replays_{Guid.NewGuid()}");
        if (!Directory.Exists(emptyPath))
            Directory.CreateDirectory(emptyPath);
        
        var manager = new ReplayManager(_loggerMock.Object, emptyPath);

        // Act
        var replays = manager.GetReplays();

        // Assert
        replays.Should().NotBeNull();
        replays.Should().BeEmpty();
    }
}

public class ReplayInfoTests
{
    [Fact]
    public void ReplayInfo_ShouldInitialize_WithDefaults()
    {
        // Act
        var info = new ReplayInfo();

        // Assert
        info.FileName.Should().BeEmpty();
        info.FilePath.Should().BeEmpty();
        info.MatchId.Should().Be(0);
    }
}

public class ReplayStatsTests
{
    [Fact]
    public void ReplayStats_ShouldInitialize_WithDefaults()
    {
        // Act
        var stats = new ReplayStats();

        // Assert
        stats.TotalReplays.Should().Be(0);
        stats.ArchivedReplays.Should().Be(0);
    }
}
