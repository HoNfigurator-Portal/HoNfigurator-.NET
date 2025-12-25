using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for ReplayUploadService - covers queuing, status tracking, target management, and NEXUS protocol.
/// </summary>
public class ReplayUploadServiceTests : IDisposable
{
    private readonly Mock<ILogger<ReplayUploadService>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly ReplayUploadService _service;
    private readonly string _testReplayDir;
    private readonly List<string> _createdFiles = new();

    public ReplayUploadServiceTests()
    {
        _loggerMock = new Mock<ILogger<ReplayUploadService>>();
        _config = new HoNConfiguration();
        _service = new ReplayUploadService(_loggerMock.Object, _config);
        _testReplayDir = Path.Combine(Path.GetTempPath(), $"replay_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testReplayDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        foreach (var file in _createdFiles)
        {
            if (File.Exists(file)) File.Delete(file);
        }
        if (Directory.Exists(_testReplayDir))
        {
            try { Directory.Delete(_testReplayDir, true); } catch { }
        }
    }

    private string CreateTestReplayFile(int matchId)
    {
        var filePath = Path.Combine(_testReplayDir, $"M{matchId}.honreplay");
        File.WriteAllBytes(filePath, new byte[] { 0x48, 0x6F, 0x4E, 0x00 }); // HoN magic bytes
        _createdFiles.Add(filePath);
        return filePath;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesWithDefaults()
    {
        // Assert
        _service.IsRunning.Should().BeFalse();
        _service.QueuedCount.Should().Be(0);
        _service.AutoUploadEnabled.Should().BeFalse();
    }

    #endregion

    #region Target Management Tests

    [Fact]
    public void AddTarget_AddsTargetSuccessfully()
    {
        // Arrange
        var target = new ReplayUploadTarget
        {
            Name = "test-target",
            Type = UploadTargetType.Http,
            Url = "https://example.com/upload",
            Priority = 10
        };

        // Act
        _service.AddTarget(target);

        // Assert
        var targets = _service.GetTargets();
        targets.Should().ContainSingle(t => t.Name == "test-target");
    }

    [Fact]
    public void AddTarget_UpdatesExistingTarget()
    {
        // Arrange
        var target1 = new ReplayUploadTarget
        {
            Name = "test-target",
            Type = UploadTargetType.Http,
            Url = "https://old-url.com/upload"
        };
        var target2 = new ReplayUploadTarget
        {
            Name = "test-target",
            Type = UploadTargetType.Http,
            Url = "https://new-url.com/upload"
        };

        // Act
        _service.AddTarget(target1);
        _service.AddTarget(target2);

        // Assert
        var targets = _service.GetTargets();
        targets.Should().ContainSingle(t => t.Name == "test-target" && t.Url == "https://new-url.com/upload");
    }

    [Fact]
    public void ConfigureMasterServer_CreatesTargetWithCorrectSettings()
    {
        // Arrange
        var masterUrl = "https://master.example.com";
        var authHash = "abc123";
        var serverLogin = "server1";

        // Act
        _service.ConfigureMasterServer(masterUrl, authHash, serverLogin);

        // Assert
        var targets = _service.GetTargets();
        var masterTarget = targets.FirstOrDefault(t => t.Name == "master");
        masterTarget.Should().NotBeNull();
        masterTarget!.Type.Should().Be(UploadTargetType.Http);
        masterTarget.Url.Should().Be("https://master.example.com/replay/upload.php");
        masterTarget.Headers.Should().ContainKey("X-Auth-Hash");
        masterTarget.Headers.Should().ContainKey("X-Server-Login");
        masterTarget.Priority.Should().Be(100);
    }

    [Fact]
    public void RemoveTarget_RemovesExistingTarget()
    {
        // Arrange
        var target = new ReplayUploadTarget { Name = "to-remove", Url = "https://example.com" };
        _service.AddTarget(target);

        // Act
        _service.RemoveTarget("to-remove");

        // Assert
        var targets = _service.GetTargets();
        targets.Should().NotContain(t => t.Name == "to-remove");
    }

    [Fact]
    public void RemoveTarget_DoesNothingForNonExistentTarget()
    {
        // Act & Assert - should not throw
        _service.RemoveTarget("non-existent");
    }

    [Fact]
    public void GetTargets_ReturnsOrderedByPriority()
    {
        // Arrange
        _service.AddTarget(new ReplayUploadTarget { Name = "low", Priority = 1, Url = "http://a" });
        _service.AddTarget(new ReplayUploadTarget { Name = "high", Priority = 100, Url = "http://b" });
        _service.AddTarget(new ReplayUploadTarget { Name = "medium", Priority = 50, Url = "http://c" });

        // Act
        var targets = _service.GetTargets();

        // Assert
        targets[0].Name.Should().Be("high");
        targets[1].Name.Should().Be("medium");
        targets[2].Name.Should().Be("low");
    }

    #endregion

    #region Queue Tests

    [Fact]
    public void QueueUpload_AddsJobToQueue()
    {
        // Arrange
        var replayPath = CreateTestReplayFile(12345);

        // Act
        var jobId = _service.QueueUpload(replayPath, 12345);

        // Assert
        jobId.Should().NotBeNullOrEmpty();
        _service.QueuedCount.Should().Be(1);
    }

    [Fact]
    public void QueueUpload_WithMetadata_StoresMetadata()
    {
        // Arrange
        var replayPath = CreateTestReplayFile(12346);
        var metadata = new Dictionary<string, string>
        {
            ["server_id"] = "srv001",
            ["region"] = "NA"
        };

        // Act
        var jobId = _service.QueueUpload(replayPath, 12346, metadata);

        // Assert
        jobId.Should().NotBeNullOrEmpty();
        var job = _service.GetJob(jobId);
        job.Should().NotBeNull();
        job!.Metadata.Should().ContainKey("server_id");
        job.Metadata.Should().ContainKey("region");
    }

    [Fact]
    public void QueueUpload_ThrowsForNonExistentFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testReplayDir, "nonexistent.honreplay");

        // Act
        var act = () => _service.QueueUpload(nonExistentPath, 99999);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void GetJob_ReturnsNullForUnknownId()
    {
        // Act
        var job = _service.GetJob("unknown-id");

        // Assert
        job.Should().BeNull();
    }

    [Fact]
    public void GetQueuedJobs_ReturnsAllQueuedJobs()
    {
        // Arrange
        var path1 = CreateTestReplayFile(1001);
        var path2 = CreateTestReplayFile(1002);
        _service.QueueUpload(path1, 1001);
        _service.QueueUpload(path2, 1002);

        // Act
        var jobs = _service.GetQueuedJobs();

        // Assert
        jobs.Should().HaveCount(2);
    }

    #endregion

    #region NEXUS Protocol Status Tests

    [Fact]
    public void GetMatchUploadStatus_ReturnsNotFound_WhenMatchNotQueued()
    {
        // Act
        var status = _service.GetMatchUploadStatus(99999);

        // Assert
        status.Should().Be(ReplayUploadStatusCode.NotFound);
    }

    [Fact]
    public void GetMatchUploadStatus_ReturnsInQueue_WhenMatchQueued()
    {
        // Arrange
        var replayPath = CreateTestReplayFile(12347);
        _service.QueueUpload(replayPath, 12347);

        // Act
        var status = _service.GetMatchUploadStatus(12347);

        // Assert
        status.Should().Be(ReplayUploadStatusCode.InQueue);
    }

    [Fact]
    public void ReplayUploadStatusCode_HasCorrectNexusValues()
    {
        // Assert - verify NEXUS protocol byte values
        ((byte)ReplayUploadStatusCode.NotFound).Should().Be(0x01);
        ((byte)ReplayUploadStatusCode.AlreadyUploaded).Should().Be(0x02);
        ((byte)ReplayUploadStatusCode.InQueue).Should().Be(0x03);
        ((byte)ReplayUploadStatusCode.Uploading).Should().Be(0x04);
        ((byte)ReplayUploadStatusCode.HaveReplay).Should().Be(0x05);
        ((byte)ReplayUploadStatusCode.UploadingNow).Should().Be(0x06);
        ((byte)ReplayUploadStatusCode.UploadComplete).Should().Be(0x07);
        ((byte)ReplayUploadStatusCode.Failed).Should().Be(0x08);
    }

    #endregion

    #region Service Lifecycle Tests

    [Fact]
    public async Task StartAsync_SetsIsRunningTrue()
    {
        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        _service.IsRunning.Should().BeTrue();

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_SetsIsRunningFalse()
    {
        // Arrange
        await _service.StartAsync(CancellationToken.None);

        // Act
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_MultipleCalls_DoesNotThrow()
    {
        // Act & Assert - should not throw
        await _service.StartAsync(CancellationToken.None);
        await _service.StartAsync(CancellationToken.None);

        // Cleanup
        await _service.StopAsync(CancellationToken.None);
    }

    #endregion

    #region File Watching Tests

    [Fact]
    public void StartWatching_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var watchDir = Path.Combine(_testReplayDir, "watch_test");
        if (Directory.Exists(watchDir)) Directory.Delete(watchDir, true);

        // Act
        _service.StartWatching(watchDir);

        // Assert
        Directory.Exists(watchDir).Should().BeTrue();

        // Cleanup
        _service.StopWatching();
    }

    [Fact]
    public void StopWatching_DisablesWatcher()
    {
        // Arrange
        _service.StartWatching(_testReplayDir);

        // Act
        _service.StopWatching();

        // Assert - should not throw on multiple stops
        _service.StopWatching();
    }

    [Fact]
    public void AutoUploadEnabled_CanBeToggled()
    {
        // Assert initial state
        _service.AutoUploadEnabled.Should().BeFalse();

        // Act & Assert
        _service.AutoUploadEnabled = true;
        _service.AutoUploadEnabled.Should().BeTrue();

        _service.AutoUploadEnabled = false;
        _service.AutoUploadEnabled.Should().BeFalse();
    }

    #endregion

    #region Cancel Tests

    [Fact]
    public void CancelUpload_CancelsQueuedJob()
    {
        // Arrange
        var replayPath = CreateTestReplayFile(12348);
        var jobId = _service.QueueUpload(replayPath, 12348);

        // Act
        var result = _service.CancelUpload(jobId);

        // Assert
        result.Should().BeTrue();
        var job = _service.GetJob(jobId);
        job!.Status.Should().Be(UploadStatus.Cancelled);
    }

    [Fact]
    public void CancelUpload_ReturnsFalseForUnknownJob()
    {
        // Act
        var result = _service.CancelUpload("unknown-job-id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CancelAll_CancelsAllQueuedJobs()
    {
        // Arrange
        var path1 = CreateTestReplayFile(2001);
        var path2 = CreateTestReplayFile(2002);
        _service.QueueUpload(path1, 2001);
        _service.QueueUpload(path2, 2002);

        // Act
        var cancelledCount = _service.CancelAll();

        // Assert
        cancelledCount.Should().Be(2);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public void GetStats_ReturnsCorrectCounts()
    {
        // Arrange
        var path1 = CreateTestReplayFile(3001);
        var path2 = CreateTestReplayFile(3002);
        _service.QueueUpload(path1, 3001);
        _service.QueueUpload(path2, 3002);

        // Act
        var stats = _service.GetStats();

        // Assert
        stats.TotalQueued.Should().Be(2);
        stats.TotalCompleted.Should().Be(0);
        stats.TotalFailed.Should().Be(0);
    }

    #endregion

    #region Upload Target Type Tests

    [Theory]
    [InlineData(UploadTargetType.Http)]
    [InlineData(UploadTargetType.Ftp)]
    [InlineData(UploadTargetType.S3)]
    [InlineData(UploadTargetType.Azure)]
    [InlineData(UploadTargetType.Local)]
    public void UploadTargetType_AllTypesValid(UploadTargetType targetType)
    {
        // Arrange
        var target = new ReplayUploadTarget
        {
            Name = $"test-{targetType}",
            Type = targetType,
            Url = targetType == UploadTargetType.Local ? "/tmp/replays" : "https://example.com"
        };

        // Act
        _service.AddTarget(target);

        // Assert
        var targets = _service.GetTargets();
        targets.Should().Contain(t => t.Type == targetType);
    }

    #endregion

    #region ReplayUploadJob Tests

    [Fact]
    public void ReplayUploadJob_Progress_CalculatesCorrectly()
    {
        // Arrange
        var job = new ReplayUploadJob
        {
            Id = "test",
            FileSize = 1000,
            BytesUploaded = 500
        };

        // Assert
        job.Progress.Should().Be(50);
    }

    [Fact]
    public void ReplayUploadJob_Progress_ReturnsZeroWhenFileSizeIsZero()
    {
        // Arrange
        var job = new ReplayUploadJob
        {
            Id = "test",
            FileSize = 0,
            BytesUploaded = 0
        };

        // Assert
        job.Progress.Should().Be(0);
    }

    [Fact]
    public void ReplayUploadJob_Duration_CalculatesCorrectly()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddMinutes(2);
        var job = new ReplayUploadJob
        {
            Id = "test",
            StartedAt = startTime,
            CompletedAt = endTime
        };

        // Assert
        job.Duration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ReplayUploadJob_Duration_ReturnsNullWhenNotCompleted()
    {
        // Arrange
        var job = new ReplayUploadJob
        {
            Id = "test",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null
        };

        // Assert
        job.Duration.Should().BeNull();
    }

    #endregion

    #region Event Tests

    [Fact]
    public void StatusChanged_RaisedWhenJobQueued()
    {
        // Arrange
        var replayPath = CreateTestReplayFile(4001);
        UploadStatusChangedEventArgs? receivedArgs = null;
        _service.StatusChanged += (sender, args) => receivedArgs = args;

        // Act
        _service.QueueUpload(replayPath, 4001);

        // Assert
        receivedArgs.Should().NotBeNull();
        receivedArgs!.Status.Should().Be(UploadStatus.Queued);
    }

    #endregion

    #region ReplayUploadTarget Tests

    [Fact]
    public void ReplayUploadTarget_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var target = new ReplayUploadTarget();

        // Assert
        target.Name.Should().Be("default");
        target.Type.Should().Be(UploadTargetType.Http);
        target.Url.Should().BeEmpty();
        target.Enabled.Should().BeTrue();
        target.Priority.Should().Be(0);
        target.Headers.Should().BeEmpty();
    }

    [Fact]
    public void ReplayUploadTarget_WithCredentials_StoresCorrectly()
    {
        // Arrange & Act
        var target = new ReplayUploadTarget
        {
            Name = "ftp-target",
            Type = UploadTargetType.Ftp,
            Url = "ftp://example.com",
            Username = "user",
            Password = "pass"
        };

        // Assert
        target.Username.Should().Be("user");
        target.Password.Should().Be("pass");
    }

    #endregion

    #region ProgressEventArgs Tests

    [Fact]
    public void ReplayUploadProgressEventArgs_ProgressPercent_CalculatesCorrectly()
    {
        // Arrange
        var args = new ReplayUploadProgressEventArgs
        {
            JobId = "test",
            BytesUploaded = 750,
            TotalBytes = 1000
        };

        // Assert
        args.ProgressPercent.Should().Be(75);
    }

    [Fact]
    public void ReplayUploadProgressEventArgs_ProgressPercent_ReturnsZeroWhenTotalIsZero()
    {
        // Arrange
        var args = new ReplayUploadProgressEventArgs
        {
            JobId = "test",
            BytesUploaded = 0,
            TotalBytes = 0
        };

        // Assert
        args.ProgressPercent.Should().Be(0);
    }

    #endregion

    #region ReplayUploadStats Tests

    [Fact]
    public void ReplayUploadStats_DefaultValues_AreZero()
    {
        // Arrange & Act
        var stats = new ReplayUploadStats();

        // Assert
        stats.TotalQueued.Should().Be(0);
        stats.TotalUploading.Should().Be(0);
        stats.TotalCompleted.Should().Be(0);
        stats.TotalFailed.Should().Be(0);
        stats.TotalBytesUploaded.Should().Be(0);
        stats.AverageUploadTime.Should().Be(TimeSpan.Zero);
    }

    #endregion
}
