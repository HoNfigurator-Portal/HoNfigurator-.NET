using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class BackupRestoreServiceTests : IDisposable
{
    private readonly Mock<ILogger<BackupRestoreService>> _loggerMock;
    private readonly Mock<ConfigurationService> _configServiceMock;
    private readonly string _testDirectory;
    private readonly string _originalAppData;

    public BackupRestoreServiceTests()
    {
        _loggerMock = new Mock<ILogger<BackupRestoreService>>();
        _configServiceMock = new Mock<ConfigurationService>(
            Mock.Of<ILogger<ConfigurationService>>(),
            (string?)null);
        
        // Set up isolated test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), $"BackupRestoreTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        
        // Store original AppData and set test directory
        _originalAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); } catch { }
        }
    }

    #region DTO Tests

    [Fact]
    public void BackupInfo_DefaultValues()
    {
        // Arrange & Act
        var info = new BackupInfo();

        // Assert
        info.BackupId.Should().BeEmpty();
        info.FileName.Should().BeEmpty();
        info.FilePath.Should().BeEmpty();
        info.Description.Should().BeEmpty();
        info.SizeBytes.Should().Be(0);
        info.FileCount.Should().Be(0);
        info.BackupType.Should().Be(BackupType.Full);
    }

    [Fact]
    public void BackupInfo_WithValues()
    {
        // Arrange & Act
        var timestamp = DateTime.UtcNow;
        var info = new BackupInfo
        {
            BackupId = "abc123",
            FileName = "backup.zip",
            FilePath = "/path/to/backup.zip",
            CreatedAt = timestamp,
            Description = "Test backup",
            SizeBytes = 12345,
            FileCount = 5,
            BackupType = BackupType.Incremental
        };

        // Assert
        info.BackupId.Should().Be("abc123");
        info.FileName.Should().Be("backup.zip");
        info.FilePath.Should().Be("/path/to/backup.zip");
        info.CreatedAt.Should().Be(timestamp);
        info.Description.Should().Be("Test backup");
        info.SizeBytes.Should().Be(12345);
        info.FileCount.Should().Be(5);
        info.BackupType.Should().Be(BackupType.Incremental);
    }

    [Fact]
    public void BackupDetails_DefaultValues()
    {
        // Arrange & Act
        var details = new BackupDetails();

        // Assert
        details.BackupId.Should().BeEmpty();
        details.FileName.Should().BeEmpty();
        details.Description.Should().BeEmpty();
        details.SizeBytes.Should().Be(0);
        details.BackupType.Should().Be(BackupType.Full);
        details.IncludedFiles.Should().BeEmpty();
    }

    [Fact]
    public void BackupDetails_WithIncludedFiles()
    {
        // Arrange & Act
        var details = new BackupDetails
        {
            BackupId = "backup123",
            IncludedFiles = new List<string> { "config.json", "servers/server1.json", "bans.json" }
        };

        // Assert
        details.IncludedFiles.Should().HaveCount(3);
        details.IncludedFiles.Should().Contain("config.json");
        details.IncludedFiles.Should().Contain("bans.json");
    }

    [Fact]
    public void RestoreOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new RestoreOptions();

        // Assert
        options.CreatePreRestoreBackup.Should().BeTrue();
        options.ReloadAfterRestore.Should().BeTrue();
        options.OnlyConfig.Should().BeFalse();
        options.OnlyBans.Should().BeFalse();
    }

    [Fact]
    public void RestoreOptions_WithFilters()
    {
        // Arrange & Act
        var options = new RestoreOptions
        {
            CreatePreRestoreBackup = false,
            ReloadAfterRestore = false,
            OnlyConfig = true,
            OnlyBans = false
        };

        // Assert
        options.CreatePreRestoreBackup.Should().BeFalse();
        options.ReloadAfterRestore.Should().BeFalse();
        options.OnlyConfig.Should().BeTrue();
        options.OnlyBans.Should().BeFalse();
    }

    [Fact]
    public void RestoreResult_DefaultValues()
    {
        // Arrange & Act
        var result = new RestoreResult();

        // Assert
        result.Success.Should().BeFalse();
        result.BackupId.Should().BeEmpty();
        result.PreRestoreBackupId.Should().BeNull();
        result.RestoredFiles.Should().BeEmpty();
        result.FailedFiles.Should().BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void RestoreResult_WithMixedResults()
    {
        // Arrange & Act
        var result = new RestoreResult
        {
            Success = false,
            BackupId = "backup123",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow,
            PreRestoreBackupId = "prerestore456",
            RestoredFiles = new List<string> { "config.json", "bans.json" },
            FailedFiles = new List<string> { "roles.db" },
            Error = "Permission denied"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.RestoredFiles.Should().HaveCount(2);
        result.FailedFiles.Should().HaveCount(1);
        result.Error.Should().Be("Permission denied");
        result.PreRestoreBackupId.Should().Be("prerestore456");
    }

    #endregion

    #region BackupType Tests

    [Theory]
    [InlineData(BackupType.Full, 0)]
    [InlineData(BackupType.Incremental, 1)]
    [InlineData(BackupType.Config, 2)]
    [InlineData(BackupType.Imported, 3)]
    public void BackupType_EnumValues(BackupType type, int expectedValue)
    {
        // Assert
        ((int)type).Should().Be(expectedValue);
    }

    [Fact]
    public void BackupType_AllValuesDefined()
    {
        // Arrange
        var values = Enum.GetValues<BackupType>();

        // Assert
        values.Should().HaveCount(4);
        values.Should().Contain(BackupType.Full);
        values.Should().Contain(BackupType.Incremental);
        values.Should().Contain(BackupType.Config);
        values.Should().Contain(BackupType.Imported);
    }

    #endregion

    #region BackupInfo Equality and Comparison Tests

    [Fact]
    public void BackupInfo_SameBackupId_IsDistinct()
    {
        // Arrange
        var info1 = new BackupInfo { BackupId = "abc123", FileName = "backup1.zip" };
        var info2 = new BackupInfo { BackupId = "abc123", FileName = "backup2.zip" };

        // Assert - Different objects even with same ID (no Equals override)
        info1.Should().NotBeSameAs(info2);
    }

    [Fact]
    public void BackupInfo_CanBeUsedInCollections()
    {
        // Arrange
        var backups = new List<BackupInfo>
        {
            new() { BackupId = "a", CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new() { BackupId = "b", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { BackupId = "c", CreatedAt = DateTime.UtcNow }
        };

        // Act
        var sorted = backups.OrderByDescending(b => b.CreatedAt).ToList();

        // Assert
        sorted[0].BackupId.Should().Be("c");
        sorted[1].BackupId.Should().Be("b");
        sorted[2].BackupId.Should().Be("a");
    }

    #endregion

    #region RestoreResult Duration Tests

    [Fact]
    public void RestoreResult_Duration_CanBeCalculated()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddMinutes(-5);
        var endTime = DateTime.UtcNow;

        var result = new RestoreResult
        {
            StartTime = startTime,
            EndTime = endTime
        };

        // Act
        var duration = result.EndTime - result.StartTime;

        // Assert
        duration.TotalMinutes.Should().BeApproximately(5, 0.1);
    }

    #endregion

    #region BackupDetails File Manipulation Tests

    [Fact]
    public void BackupDetails_IncludedFiles_CanBeModified()
    {
        // Arrange
        var details = new BackupDetails();

        // Act
        details.IncludedFiles.Add("config.json");
        details.IncludedFiles.Add("servers/server1.json");
        details.IncludedFiles.Add("servers/server2.json");

        // Assert
        details.IncludedFiles.Should().HaveCount(3);
        details.IncludedFiles.Where(f => f.StartsWith("servers/")).Should().HaveCount(2);
    }

    [Fact]
    public void BackupDetails_IncludedFiles_SupportsLinqOperations()
    {
        // Arrange
        var details = new BackupDetails
        {
            IncludedFiles = new List<string>
            {
                "config.json",
                "bans.json",
                "servers/server1.json",
                "servers/server2.json",
                "roles.db"
            }
        };

        // Act
        var jsonFiles = details.IncludedFiles.Where(f => f.EndsWith(".json")).ToList();
        var serverFiles = details.IncludedFiles.Where(f => f.StartsWith("servers/")).ToList();

        // Assert
        jsonFiles.Should().HaveCount(4);
        serverFiles.Should().HaveCount(2);
    }

    #endregion

    #region RestoreOptions Combination Tests

    [Fact]
    public void RestoreOptions_OnlyConfigAndOnlyBans_AreMutuallyExclusive_ByConvention()
    {
        // These options should be mutually exclusive by convention
        // The implementation should honor OnlyConfig over OnlyBans if both set

        // Arrange
        var options = new RestoreOptions
        {
            OnlyConfig = true,
            OnlyBans = true
        };

        // Assert - Both can be set (no runtime validation)
        options.OnlyConfig.Should().BeTrue();
        options.OnlyBans.Should().BeTrue();
    }

    [Fact]
    public void RestoreOptions_MinimalRestore()
    {
        // Arrange
        var options = new RestoreOptions
        {
            CreatePreRestoreBackup = false,
            ReloadAfterRestore = false,
            OnlyConfig = true
        };

        // Assert
        options.CreatePreRestoreBackup.Should().BeFalse();
        options.ReloadAfterRestore.Should().BeFalse();
        options.OnlyConfig.Should().BeTrue();
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public void RestoreResult_PartialSuccess_HasBothRestoredAndFailedFiles()
    {
        // Arrange
        var result = new RestoreResult
        {
            Success = false,
            BackupId = "test123",
            RestoredFiles = new List<string> { "config.json", "bans.json" },
            FailedFiles = new List<string> { "roles.db" },
            Error = "Partial restore - some files failed"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.RestoredFiles.Should().HaveCount(2);
        result.FailedFiles.Should().HaveCount(1);
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BackupInfo_SizeDisplay_CanBeFormatted()
    {
        // Arrange
        var info = new BackupInfo
        {
            SizeBytes = 1_234_567_890 // ~1.15 GB
        };

        // Act
        var sizeKB = info.SizeBytes / 1024.0;
        var sizeMB = info.SizeBytes / (1024.0 * 1024);
        var sizeGB = info.SizeBytes / (1024.0 * 1024 * 1024);

        // Assert
        sizeKB.Should().BeApproximately(1205632.7, 0.1);
        sizeMB.Should().BeApproximately(1177.4, 0.1);
        sizeGB.Should().BeApproximately(1.15, 0.01);
    }

    [Fact]
    public void BackupDetails_EmptyBackup_IsValid()
    {
        // Arrange
        var details = new BackupDetails
        {
            BackupId = "empty123",
            FileName = "empty_backup.zip",
            Description = "Empty backup for testing",
            SizeBytes = 0,
            IncludedFiles = new List<string>()
        };

        // Assert
        details.BackupId.Should().NotBeEmpty();
        details.SizeBytes.Should().Be(0);
        details.IncludedFiles.Should().BeEmpty();
    }

    #endregion

    #region Backup Type Specific Tests

    [Fact]
    public void BackupInfo_ImportedType_HasSpecialHandling()
    {
        // Arrange
        var imported = new BackupInfo
        {
            BackupId = "imported123",
            BackupType = BackupType.Imported,
            Description = "Imported: User backup from external source"
        };

        // Assert
        imported.BackupType.Should().Be(BackupType.Imported);
        imported.Description.Should().StartWith("Imported:");
    }

    [Fact]
    public void BackupInfo_IncrementalType_CanBeDistinguished()
    {
        // Arrange
        var full = new BackupInfo { BackupType = BackupType.Full };
        var incremental = new BackupInfo { BackupType = BackupType.Incremental };

        // Assert
        full.BackupType.Should().NotBe(incremental.BackupType);
        ((int)BackupType.Incremental).Should().BeGreaterThan((int)BackupType.Full);
    }

    #endregion

    #region RestoreResult Timing Tests

    [Fact]
    public void RestoreResult_ZeroDuration_IsPossible()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var result = new RestoreResult
        {
            StartTime = now,
            EndTime = now
        };

        // Act
        var duration = result.EndTime - result.StartTime;

        // Assert
        duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RestoreResult_DefaultTimes_AreDefaultDateTime()
    {
        // Arrange
        var result = new RestoreResult();

        // Assert
        result.StartTime.Should().Be(default(DateTime));
        result.EndTime.Should().Be(default(DateTime));
    }

    #endregion

    #region Collection Operations Tests

    [Fact]
    public void BackupInfo_ListOperations_WorkCorrectly()
    {
        // Arrange
        var backups = new List<BackupInfo>
        {
            new() { BackupId = "1", SizeBytes = 1000, BackupType = BackupType.Full },
            new() { BackupId = "2", SizeBytes = 500, BackupType = BackupType.Incremental },
            new() { BackupId = "3", SizeBytes = 2000, BackupType = BackupType.Full },
            new() { BackupId = "4", SizeBytes = 100, BackupType = BackupType.Config }
        };

        // Act
        var totalSize = backups.Sum(b => b.SizeBytes);
        var fullBackups = backups.Where(b => b.BackupType == BackupType.Full).ToList();
        var largestBackup = backups.MaxBy(b => b.SizeBytes);

        // Assert
        totalSize.Should().Be(3600);
        fullBackups.Should().HaveCount(2);
        largestBackup!.BackupId.Should().Be("3");
    }

    [Fact]
    public void RestoreResult_RestoredFiles_CanGroupByDirectory()
    {
        // Arrange
        var result = new RestoreResult
        {
            RestoredFiles = new List<string>
            {
                "config.json",
                "bans.json",
                "servers/server1.json",
                "servers/server2.json",
                "servers/configs/default.json"
            }
        };

        // Act
        var grouped = result.RestoredFiles
            .GroupBy(f => Path.GetDirectoryName(f) ?? "root")
            .ToDictionary(g => g.Key, g => g.ToList());

        // Assert
        grouped.Should().ContainKey("");  // Root files
        grouped[""].Should().HaveCount(2);
        grouped.Should().ContainKey("servers");
        grouped["servers"].Should().HaveCount(2);
    }

    #endregion
}
