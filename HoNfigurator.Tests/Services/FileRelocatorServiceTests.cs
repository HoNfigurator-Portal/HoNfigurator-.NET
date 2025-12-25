using FluentAssertions;
using HoNfigurator.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Comprehensive tests for FileRelocatorService
/// </summary>
public class FileRelocatorServiceTests
{
    private readonly Mock<ILogger<FileRelocatorService>> _mockLogger;

    public FileRelocatorServiceTests()
    {
        _mockLogger = new Mock<ILogger<FileRelocatorService>>();
    }

    #region StorageSettings Tests

    [Fact]
    public void StorageSettings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new StorageSettings();

        // Assert
        settings.PrimaryPath.Should().Be("replays");
        settings.ArchivePath.Should().BeNull();
        settings.LogsPath.Should().Be("logs");
        settings.ArchiveAfterDays.Should().Be(7);
        settings.RetentionDays.Should().Be(90);
        settings.AutoRelocate.Should().BeFalse();
        settings.AutoCleanup.Should().BeFalse();
    }

    [Fact]
    public void StorageSettings_Properties_CanBeSet()
    {
        // Arrange
        var settings = new StorageSettings();

        // Act
        settings.PrimaryPath = "primary";
        settings.ArchivePath = "archive";
        settings.LogsPath = "my_logs";
        settings.ArchiveAfterDays = 14;
        settings.RetentionDays = 180;
        settings.AutoRelocate = true;
        settings.AutoCleanup = true;

        // Assert
        settings.PrimaryPath.Should().Be("primary");
        settings.ArchivePath.Should().Be("archive");
        settings.LogsPath.Should().Be("my_logs");
        settings.ArchiveAfterDays.Should().Be(14);
        settings.RetentionDays.Should().Be(180);
        settings.AutoRelocate.Should().BeTrue();
        settings.AutoCleanup.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void StorageSettings_ArchiveAfterDays_AcceptsVariousValues(int days)
    {
        // Arrange
        var settings = new StorageSettings();

        // Act
        settings.ArchiveAfterDays = days;

        // Assert
        settings.ArchiveAfterDays.Should().Be(days);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    [InlineData(730)]
    public void StorageSettings_RetentionDays_AcceptsVariousValues(int days)
    {
        // Arrange
        var settings = new StorageSettings();

        // Act
        settings.RetentionDays = days;

        // Assert
        settings.RetentionDays.Should().Be(days);
    }

    #endregion

    #region StorageStatus Tests

    [Fact]
    public void StorageStatus_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var status = new StorageStatus();

        // Assert
        status.IsConfigured.Should().BeFalse();
        status.PrimaryPath.Should().BeNull();
        status.ArchivePath.Should().BeNull();
        status.PrimaryStats.Should().BeNull();
        status.ArchiveStats.Should().BeNull();
        status.RetentionDays.Should().Be(0);
        status.ArchiveAfterDays.Should().Be(0);
    }

    [Fact]
    public void StorageStatus_AllProperties_CanBeSet()
    {
        // Arrange
        var primaryStats = new DirectoryStats { Path = "primary", FileCount = 10 };
        var archiveStats = new DirectoryStats { Path = "archive", FileCount = 20 };

        // Act
        var status = new StorageStatus
        {
            IsConfigured = true,
            PrimaryPath = "C:\\replays",
            ArchivePath = "D:\\archive",
            PrimaryStats = primaryStats,
            ArchiveStats = archiveStats,
            RetentionDays = 90,
            ArchiveAfterDays = 7
        };

        // Assert
        status.IsConfigured.Should().BeTrue();
        status.PrimaryPath.Should().Be("C:\\replays");
        status.ArchivePath.Should().Be("D:\\archive");
        status.PrimaryStats.Should().BeSameAs(primaryStats);
        status.ArchiveStats.Should().BeSameAs(archiveStats);
        status.RetentionDays.Should().Be(90);
        status.ArchiveAfterDays.Should().Be(7);
    }

    [Fact]
    public void StorageStatus_IsConfigured_ReflectsState()
    {
        // Arrange & Act
        var configuredStatus = new StorageStatus { IsConfigured = true };
        var unconfiguredStatus = new StorageStatus { IsConfigured = false };

        // Assert
        configuredStatus.IsConfigured.Should().BeTrue();
        unconfiguredStatus.IsConfigured.Should().BeFalse();
    }

    #endregion

    #region DirectoryStats Tests

    [Fact]
    public void DirectoryStats_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var stats = new DirectoryStats();

        // Assert
        stats.Path.Should().BeEmpty();
        stats.Exists.Should().BeFalse();
        stats.FileCount.Should().Be(0);
        stats.TotalSize.Should().Be(0);
        stats.DriveFreeSpace.Should().Be(0);
        stats.DriveTotalSpace.Should().Be(0);
    }

    [Fact]
    public void DirectoryStats_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var stats = new DirectoryStats
        {
            Path = "C:\\data",
            Exists = true,
            FileCount = 100,
            TotalSize = 1024 * 1024 * 100, // 100 MB
            DriveFreeSpace = 1024L * 1024 * 1024 * 50, // 50 GB
            DriveTotalSpace = 1024L * 1024 * 1024 * 200 // 200 GB
        };

        // Assert
        stats.Path.Should().Be("C:\\data");
        stats.Exists.Should().BeTrue();
        stats.FileCount.Should().Be(100);
        stats.TotalSize.Should().Be(104857600);
        stats.DriveFreeSpace.Should().Be(53687091200);
        stats.DriveTotalSpace.Should().Be(214748364800);
    }

    [Fact]
    public void DirectoryStats_DriveUsagePercent_CalculatesCorrectly()
    {
        // Arrange
        var stats = new DirectoryStats
        {
            DriveFreeSpace = 1024L * 1024 * 1024 * 50, // 50 GB free
            DriveTotalSpace = 1024L * 1024 * 1024 * 100 // 100 GB total
        };

        // Act
        var usagePercent = stats.DriveUsagePercent;

        // Assert
        usagePercent.Should().BeApproximately(50.0, 0.001);
    }

    [Fact]
    public void DirectoryStats_DriveUsagePercent_ZeroWhenDriveEmpty()
    {
        // Arrange
        var stats = new DirectoryStats
        {
            DriveFreeSpace = 0,
            DriveTotalSpace = 0
        };

        // Act
        var usagePercent = stats.DriveUsagePercent;

        // Assert
        usagePercent.Should().Be(0);
    }

    [Theory]
    [InlineData(100L * 1024 * 1024 * 1024, 50L * 1024 * 1024 * 1024, 50.0)] // 50% used
    [InlineData(100L * 1024 * 1024 * 1024, 25L * 1024 * 1024 * 1024, 75.0)] // 75% used
    [InlineData(100L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024, 90.0)] // 90% used
    [InlineData(100L * 1024 * 1024 * 1024, 100L * 1024 * 1024 * 1024, 0.0)] // 0% used (all free)
    public void DirectoryStats_DriveUsagePercent_VariousScenarios(long total, long free, double expected)
    {
        // Arrange
        var stats = new DirectoryStats
        {
            DriveTotalSpace = total,
            DriveFreeSpace = free
        };

        // Act & Assert
        stats.DriveUsagePercent.Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void DirectoryStats_LargeValues_HandledCorrectly()
    {
        // Arrange
        var stats = new DirectoryStats
        {
            DriveTotalSpace = 4L * 1024 * 1024 * 1024 * 1024, // 4 TB
            DriveFreeSpace = 1L * 1024 * 1024 * 1024 * 1024  // 1 TB free
        };

        // Act & Assert
        stats.DriveUsagePercent.Should().BeApproximately(75.0, 0.001);
    }

    #endregion

    #region RelocationResult Tests

    [Fact]
    public void RelocationResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new RelocationResult();

        // Assert
        result.Success.Should().BeFalse();
        result.TotalFiles.Should().Be(0);
        result.MovedFiles.Should().Be(0);
        result.FailedFiles.Should().Be(0);
        result.TotalSize.Should().Be(0);
        result.MovedSize.Should().Be(0);
        result.Errors.Should().NotBeNull().And.BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void RelocationResult_SuccessfulRelocation_HasCorrectState()
    {
        // Arrange & Act
        var result = new RelocationResult
        {
            Success = true,
            TotalFiles = 100,
            MovedFiles = 100,
            FailedFiles = 0,
            TotalSize = 1024 * 1024 * 500, // 500 MB
            MovedSize = 1024 * 1024 * 500
        };

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFiles.Should().Be(100);
        result.MovedFiles.Should().Be(100);
        result.FailedFiles.Should().Be(0);
        result.TotalSize.Should().Be(result.MovedSize);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void RelocationResult_PartialFailure_HasCorrectState()
    {
        // Arrange & Act
        var result = new RelocationResult
        {
            Success = false,
            TotalFiles = 100,
            MovedFiles = 90,
            FailedFiles = 10,
            TotalSize = 1024 * 1024 * 500,
            MovedSize = 1024 * 1024 * 450
        };
        result.Errors.Add("file1.dat: Access denied");
        result.Errors.Add("file2.dat: Disk full");

        // Assert
        result.Success.Should().BeFalse();
        result.TotalFiles.Should().Be(100);
        result.MovedFiles.Should().Be(90);
        result.FailedFiles.Should().Be(10);
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain("file1.dat: Access denied");
    }

    [Fact]
    public void RelocationResult_ErrorProperty_SetsCorrectly()
    {
        // Arrange
        var result = new RelocationResult();

        // Act
        result.Error = "Archive path not configured";

        // Assert
        result.Error.Should().Be("Archive path not configured");
    }

    [Fact]
    public void RelocationResult_Errors_CanAddMultiple()
    {
        // Arrange
        var result = new RelocationResult();

        // Act
        result.Errors.Add("Error 1");
        result.Errors.Add("Error 2");
        result.Errors.Add("Error 3");

        // Assert
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().ContainInOrder("Error 1", "Error 2", "Error 3");
    }

    #endregion

    #region CleanupResult Tests

    [Fact]
    public void CleanupResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new CleanupResult();

        // Assert
        result.Success.Should().BeFalse();
        result.TotalFiles.Should().Be(0);
        result.DeletedFiles.Should().Be(0);
        result.FailedFiles.Should().Be(0);
        result.TotalSize.Should().Be(0);
        result.DeletedSize.Should().Be(0);
        result.Errors.Should().NotBeNull().And.BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CleanupResult_SuccessfulCleanup_HasCorrectState()
    {
        // Arrange & Act
        var result = new CleanupResult
        {
            Success = true,
            TotalFiles = 50,
            DeletedFiles = 50,
            FailedFiles = 0,
            TotalSize = 1024 * 1024 * 200, // 200 MB
            DeletedSize = 1024 * 1024 * 200
        };

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFiles.Should().Be(50);
        result.DeletedFiles.Should().Be(50);
        result.FailedFiles.Should().Be(0);
        result.TotalSize.Should().Be(result.DeletedSize);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CleanupResult_PartialFailure_TracksFailures()
    {
        // Arrange & Act
        var result = new CleanupResult
        {
            Success = false,
            TotalFiles = 50,
            DeletedFiles = 45,
            FailedFiles = 5,
            TotalSize = 1024 * 1024 * 200,
            DeletedSize = 1024 * 1024 * 180
        };
        result.Errors.Add("locked_file.dat: File in use");

        // Assert
        result.Success.Should().BeFalse();
        result.FailedFiles.Should().Be(5);
        result.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void CleanupResult_NotConfiguredError_SetsCorrectly()
    {
        // Arrange & Act
        var result = new CleanupResult
        {
            Success = false,
            Error = "Archive path not configured"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Archive path not configured");
    }

    #endregion

    #region StorageAnalytics Tests

    [Fact]
    public void StorageAnalytics_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var analytics = new StorageAnalytics();

        // Assert
        analytics.PrimaryFileCount.Should().Be(0);
        analytics.PrimaryTotalSize.Should().Be(0);
        analytics.PrimaryOldestFile.Should().BeNull();
        analytics.PrimaryNewestFile.Should().BeNull();
        analytics.PrimaryByExtension.Should().NotBeNull().And.BeEmpty();

        analytics.ArchiveFileCount.Should().Be(0);
        analytics.ArchiveTotalSize.Should().Be(0);
        analytics.ArchiveOldestFile.Should().BeNull();
        analytics.ArchiveNewestFile.Should().BeNull();
        analytics.ArchiveByExtension.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void StorageAnalytics_PrimaryStorage_TracksMetrics()
    {
        // Arrange
        var oldest = DateTime.UtcNow.AddDays(-30);
        var newest = DateTime.UtcNow;

        // Act
        var analytics = new StorageAnalytics
        {
            PrimaryFileCount = 1000,
            PrimaryTotalSize = 1024L * 1024 * 1024 * 5, // 5 GB
            PrimaryOldestFile = oldest,
            PrimaryNewestFile = newest
        };

        // Assert
        analytics.PrimaryFileCount.Should().Be(1000);
        analytics.PrimaryTotalSize.Should().Be(5368709120);
        analytics.PrimaryOldestFile.Should().Be(oldest);
        analytics.PrimaryNewestFile.Should().Be(newest);
    }

    [Fact]
    public void StorageAnalytics_ArchiveStorage_TracksMetrics()
    {
        // Arrange
        var oldest = DateTime.UtcNow.AddDays(-365);
        var newest = DateTime.UtcNow.AddDays(-30);

        // Act
        var analytics = new StorageAnalytics
        {
            ArchiveFileCount = 5000,
            ArchiveTotalSize = 1024L * 1024 * 1024 * 50, // 50 GB
            ArchiveOldestFile = oldest,
            ArchiveNewestFile = newest
        };

        // Assert
        analytics.ArchiveFileCount.Should().Be(5000);
        analytics.ArchiveTotalSize.Should().Be(53687091200);
        analytics.ArchiveOldestFile.Should().Be(oldest);
        analytics.ArchiveNewestFile.Should().Be(newest);
    }

    [Fact]
    public void StorageAnalytics_ByExtension_GroupsCorrectly()
    {
        // Arrange
        var analytics = new StorageAnalytics();

        // Act
        analytics.PrimaryByExtension[".honreplay"] = new FileGroupStats { Count = 500, TotalSize = 1024L * 1024 * 2500 };
        analytics.PrimaryByExtension[".log"] = new FileGroupStats { Count = 200, TotalSize = 1024L * 1024 * 50 };
        analytics.PrimaryByExtension[".json"] = new FileGroupStats { Count = 50, TotalSize = 1024L * 100 };

        // Assert
        analytics.PrimaryByExtension.Should().HaveCount(3);
        analytics.PrimaryByExtension[".honreplay"].Count.Should().Be(500);
        analytics.PrimaryByExtension[".log"].Count.Should().Be(200);
        analytics.PrimaryByExtension[".json"].Count.Should().Be(50);
    }

    [Fact]
    public void StorageAnalytics_ArchiveByExtension_GroupsCorrectly()
    {
        // Arrange
        var analytics = new StorageAnalytics();

        // Act
        analytics.ArchiveByExtension[".honreplay"] = new FileGroupStats { Count = 2000, TotalSize = 1024L * 1024 * 10000 };
        analytics.ArchiveByExtension[".gz"] = new FileGroupStats { Count = 100, TotalSize = 1024 * 1024 * 200 };

        // Assert
        analytics.ArchiveByExtension.Should().HaveCount(2);
        analytics.ArchiveByExtension[".honreplay"].Count.Should().Be(2000);
        analytics.ArchiveByExtension[".gz"].Count.Should().Be(100);
    }

    #endregion

    #region FileGroupStats Tests

    [Fact]
    public void FileGroupStats_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var stats = new FileGroupStats();

        // Assert
        stats.Count.Should().Be(0);
        stats.TotalSize.Should().Be(0);
    }

    [Fact]
    public void FileGroupStats_Properties_CanBeSet()
    {
        // Arrange & Act
        var stats = new FileGroupStats
        {
            Count = 100,
            TotalSize = 1024 * 1024 * 500 // 500 MB
        };

        // Assert
        stats.Count.Should().Be(100);
        stats.TotalSize.Should().Be(524288000);
    }

    [Theory]
    [InlineData(1, 1024)]
    [InlineData(100, 1048576)]
    [InlineData(10000, 10737418240)]
    public void FileGroupStats_VariousValues_StoreCorrectly(int count, long size)
    {
        // Arrange & Act
        var stats = new FileGroupStats
        {
            Count = count,
            TotalSize = size
        };

        // Assert
        stats.Count.Should().Be(count);
        stats.TotalSize.Should().Be(size);
    }

    #endregion

    #region Size Calculation Tests

    [Fact]
    public void SizeCalculations_MovedVsTotalSize_TracksAccurately()
    {
        // Arrange
        var result = new RelocationResult
        {
            TotalFiles = 100,
            TotalSize = 1024 * 1024 * 100 // 100 MB total
        };

        // Act - simulate partial relocation
        result.MovedFiles = 80;
        result.MovedSize = 1024 * 1024 * 80; // 80 MB moved
        result.FailedFiles = 20;

        // Assert
        result.TotalSize.Should().BeGreaterThan(result.MovedSize);
        (result.MovedFiles + result.FailedFiles).Should().Be(result.TotalFiles);
    }

    [Fact]
    public void SizeCalculations_DeletedVsTotalSize_TracksAccurately()
    {
        // Arrange
        var result = new CleanupResult
        {
            TotalFiles = 50,
            TotalSize = 1024 * 1024 * 500 // 500 MB total
        };

        // Act - simulate partial cleanup
        result.DeletedFiles = 40;
        result.DeletedSize = 1024 * 1024 * 400; // 400 MB deleted
        result.FailedFiles = 10;

        // Assert
        result.TotalSize.Should().BeGreaterThan(result.DeletedSize);
        (result.DeletedFiles + result.FailedFiles).Should().Be(result.TotalFiles);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void RelocationResult_MultipleErrors_TrackAll()
    {
        // Arrange
        var result = new RelocationResult { Success = false };

        // Act
        var errorMessages = new[]
        {
            "file1.dat: Access denied",
            "file2.dat: Disk full",
            "file3.dat: Path too long",
            "file4.dat: File in use",
            "file5.dat: Permission denied"
        };

        foreach (var error in errorMessages)
        {
            result.Errors.Add(error);
        }

        // Assert
        result.Errors.Should().HaveCount(5);
        result.Errors.Should().Contain("file1.dat: Access denied");
        result.Errors.Should().Contain("file5.dat: Permission denied");
    }

    [Fact]
    public void CleanupResult_MultipleErrors_TrackAll()
    {
        // Arrange
        var result = new CleanupResult { Success = false };

        // Act
        result.Errors.Add("locked_file.dat: File in use by another process");
        result.Errors.Add("readonly_file.dat: Cannot delete read-only file");
        result.Errors.Add("system_file.dat: Access denied");

        // Assert
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().AllSatisfy(e => e.Should().Contain(":"));
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task RelocationResult_ConcurrentErrorAddition_ThreadSafe()
    {
        // Arrange
        var result = new RelocationResult();
        var tasks = new List<Task>();
        var errorCount = 100;

        // Act
        for (int i = 0; i < errorCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                lock (result.Errors)
                {
                    result.Errors.Add($"Error {index}");
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        result.Errors.Should().HaveCount(errorCount);
    }

    [Fact]
    public async Task StorageAnalytics_ConcurrentDictionaryAccess_ThreadSafe()
    {
        // Arrange
        var analytics = new StorageAnalytics();
        var extensions = new[] { ".txt", ".log", ".dat", ".bin", ".json" };
        var tasks = new List<Task>();

        // Act
        foreach (var ext in extensions)
        {
            var extension = ext;
            tasks.Add(Task.Run(() =>
            {
                lock (analytics.PrimaryByExtension)
                {
                    analytics.PrimaryByExtension[extension] = new FileGroupStats
                    {
                        Count = 10,
                        TotalSize = 1024
                    };
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        analytics.PrimaryByExtension.Should().HaveCount(extensions.Length);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void DirectoryStats_MaxValues_HandledCorrectly()
    {
        // Arrange & Act
        var stats = new DirectoryStats
        {
            FileCount = int.MaxValue,
            TotalSize = long.MaxValue,
            DriveFreeSpace = long.MaxValue / 2,
            DriveTotalSpace = long.MaxValue
        };

        // Assert
        stats.FileCount.Should().Be(int.MaxValue);
        stats.TotalSize.Should().Be(long.MaxValue);
        stats.DriveUsagePercent.Should().BeApproximately(50.0, 0.001);
    }

    [Fact]
    public void RelocationResult_ZeroFiles_ValidState()
    {
        // Arrange & Act
        var result = new RelocationResult
        {
            Success = true,
            TotalFiles = 0,
            MovedFiles = 0,
            FailedFiles = 0,
            TotalSize = 0,
            MovedSize = 0
        };

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFiles.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CleanupResult_ZeroFiles_ValidState()
    {
        // Arrange & Act
        var result = new CleanupResult
        {
            Success = true,
            TotalFiles = 0,
            DeletedFiles = 0,
            FailedFiles = 0
        };

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFiles.Should().Be(0);
    }

    [Fact]
    public void StorageAnalytics_EmptyStorage_ValidState()
    {
        // Arrange & Act
        var analytics = new StorageAnalytics
        {
            PrimaryFileCount = 0,
            PrimaryTotalSize = 0,
            ArchiveFileCount = 0,
            ArchiveTotalSize = 0
        };

        // Assert
        analytics.PrimaryFileCount.Should().Be(0);
        analytics.ArchiveFileCount.Should().Be(0);
        analytics.PrimaryByExtension.Should().BeEmpty();
        analytics.ArchiveByExtension.Should().BeEmpty();
    }

    [Fact]
    public void StorageSettings_EmptyPaths_HandledCorrectly()
    {
        // Arrange & Act
        var settings = new StorageSettings
        {
            PrimaryPath = "",
            ArchivePath = "",
            LogsPath = ""
        };

        // Assert
        settings.PrimaryPath.Should().BeEmpty();
        settings.ArchivePath.Should().BeEmpty();
        settings.LogsPath.Should().BeEmpty();
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void RelocationScenario_FullSuccess_AllMetricsMatch()
    {
        // Arrange
        var totalFiles = 100;
        var fileSize = 1024 * 1024L; // 1 MB per file
        var totalSize = totalFiles * fileSize;

        // Act
        var result = new RelocationResult
        {
            Success = true,
            TotalFiles = totalFiles,
            MovedFiles = totalFiles,
            FailedFiles = 0,
            TotalSize = totalSize,
            MovedSize = totalSize
        };

        // Assert
        result.Success.Should().BeTrue();
        result.MovedFiles.Should().Be(result.TotalFiles);
        result.MovedSize.Should().Be(result.TotalSize);
        result.FailedFiles.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CleanupScenario_PartialSuccess_MetricsAccurate()
    {
        // Arrange
        var totalFiles = 50;
        var deletedFiles = 45;
        var failedFiles = 5;

        // Act
        var result = new CleanupResult
        {
            Success = false,
            TotalFiles = totalFiles,
            DeletedFiles = deletedFiles,
            FailedFiles = failedFiles,
            TotalSize = 1024 * 1024 * 500,
            DeletedSize = 1024 * 1024 * 450
        };

        for (int i = 0; i < failedFiles; i++)
        {
            result.Errors.Add($"file{i}.dat: Error");
        }

        // Assert
        result.Success.Should().BeFalse();
        (result.DeletedFiles + result.FailedFiles).Should().Be(result.TotalFiles);
        result.Errors.Should().HaveCount(failedFiles);
    }

    [Fact]
    public void StorageAnalyticsScenario_BothStorages_TrackedSeparately()
    {
        // Arrange & Act
        var analytics = new StorageAnalytics
        {
            PrimaryFileCount = 1000,
            PrimaryTotalSize = 1024L * 1024 * 1024 * 5, // 5 GB
            PrimaryOldestFile = DateTime.UtcNow.AddDays(-7),
            PrimaryNewestFile = DateTime.UtcNow,
            
            ArchiveFileCount = 5000,
            ArchiveTotalSize = 1024L * 1024 * 1024 * 50, // 50 GB
            ArchiveOldestFile = DateTime.UtcNow.AddDays(-365),
            ArchiveNewestFile = DateTime.UtcNow.AddDays(-7)
        };

        // Add extension stats
        analytics.PrimaryByExtension[".honreplay"] = new FileGroupStats { Count = 900, TotalSize = 1024L * 1024 * 1024 * 4 };
        analytics.PrimaryByExtension[".log"] = new FileGroupStats { Count = 100, TotalSize = 1024L * 1024 * 1024 };

        analytics.ArchiveByExtension[".honreplay"] = new FileGroupStats { Count = 4500, TotalSize = 1024L * 1024 * 1024 * 45 };
        analytics.ArchiveByExtension[".log"] = new FileGroupStats { Count = 500, TotalSize = 1024L * 1024 * 1024 * 5 };

        // Assert
        analytics.PrimaryFileCount.Should().BeLessThan(analytics.ArchiveFileCount);
        analytics.PrimaryTotalSize.Should().BeLessThan(analytics.ArchiveTotalSize);
        analytics.PrimaryByExtension.Should().HaveCount(2);
        analytics.ArchiveByExtension.Should().HaveCount(2);
    }

    #endregion
}
