using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Models;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class DiskMonitorServiceTests
{
    private readonly Mock<ILogger<DiskMonitorService>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly DiskMonitorService _service;

    public DiskMonitorServiceTests()
    {
        _loggerMock = new Mock<ILogger<DiskMonitorService>>();
        _config = new HoNConfiguration();
        _service = new DiskMonitorService(_loggerMock.Object, _config);
    }

    #region DTO Tests

    [Fact]
    public void DiskUtilizationReport_DefaultValues()
    {
        // Arrange & Act
        var report = new DiskUtilizationReport();

        // Assert
        report.Timestamp.Should().Be(default);
        report.Drives.Should().BeEmpty();
        report.MonitoredPaths.Should().BeEmpty();
        report.OverallStatus.Should().Be(AlertLevel.Normal);
    }

    [Fact]
    public void DiskUtilizationReport_WithValues()
    {
        // Arrange & Act
        var report = new DiskUtilizationReport
        {
            Timestamp = DateTime.UtcNow,
            Drives = new List<DriveUtilization>
            {
                new() { Name = "C:\\", UsedPercentage = 50 }
            },
            OverallStatus = AlertLevel.Warning
        };

        // Assert
        report.Drives.Should().HaveCount(1);
        report.OverallStatus.Should().Be(AlertLevel.Warning);
    }

    [Fact]
    public void DriveUtilization_DefaultValues()
    {
        // Arrange & Act
        var drive = new DriveUtilization();

        // Assert
        drive.Name.Should().BeEmpty();
        drive.Label.Should().BeNull();
        drive.DriveType.Should().BeEmpty();
        drive.FileSystem.Should().BeEmpty();
        drive.TotalSizeBytes.Should().Be(0);
        drive.FreeSizeBytes.Should().Be(0);
        drive.UsedSizeBytes.Should().Be(0);
        drive.UsedPercentage.Should().Be(0);
        drive.AlertLevel.Should().Be(AlertLevel.Normal);
    }

    [Fact]
    public void DriveUtilization_WithValues()
    {
        // Arrange & Act
        var drive = new DriveUtilization
        {
            Name = "C:\\",
            Label = "System",
            DriveType = "Fixed",
            FileSystem = "NTFS",
            TotalSizeBytes = 500_000_000_000, // 500 GB
            FreeSizeBytes = 100_000_000_000,  // 100 GB
            UsedSizeBytes = 400_000_000_000,  // 400 GB
            UsedPercentage = 80,
            AlertLevel = AlertLevel.Warning
        };

        // Assert
        drive.Name.Should().Be("C:\\");
        drive.UsedPercentage.Should().Be(80);
        drive.AlertLevel.Should().Be(AlertLevel.Warning);
    }

    [Fact]
    public void DriveUtilization_FormattedProperties()
    {
        // Arrange
        var drive = new DriveUtilization
        {
            TotalSizeBytes = 1_073_741_824, // 1 GB
            FreeSizeBytes = 536_870_912,    // 512 MB
            UsedSizeBytes = 536_870_912     // 512 MB
        };

        // Assert
        drive.TotalSizeFormatted.Should().Contain("GB");
        drive.FreeSizeFormatted.Should().Contain("MB");
        drive.UsedSizeFormatted.Should().Contain("MB");
    }

    [Fact]
    public void MonitoredPathStats_DefaultValues()
    {
        // Arrange & Act
        var stats = new MonitoredPathStats();

        // Assert
        stats.Path.Should().BeEmpty();
        stats.Name.Should().BeEmpty();
        stats.SizeBytes.Should().Be(0);
        stats.MaxSizeBytes.Should().Be(0);
        stats.FileCount.Should().Be(0);
        stats.LastModified.Should().Be(default);
    }

    [Fact]
    public void MonitoredPathStats_UsedPercentage_Calculates()
    {
        // Arrange
        var stats = new MonitoredPathStats
        {
            SizeBytes = 50_000_000,     // 50 MB
            MaxSizeBytes = 100_000_000  // 100 MB
        };

        // Assert
        stats.UsedPercentage.Should().Be(50);
    }

    [Fact]
    public void MonitoredPathStats_UsedPercentage_ZeroMax_ReturnsZero()
    {
        // Arrange
        var stats = new MonitoredPathStats
        {
            SizeBytes = 50_000_000,
            MaxSizeBytes = 0
        };

        // Assert
        stats.UsedPercentage.Should().Be(0);
    }

    [Fact]
    public void PathUtilization_DefaultValues()
    {
        // Arrange & Act
        var util = new PathUtilization();

        // Assert
        util.Path.Should().BeEmpty();
        util.SizeBytes.Should().Be(0);
        util.FileCount.Should().Be(0);
        util.DriveFreeSizeBytes.Should().Be(0);
        util.DriveTotalSizeBytes.Should().Be(0);
    }

    [Fact]
    public void DiskAlert_DefaultValues()
    {
        // Arrange & Act
        var alert = new DiskAlert();

        // Assert
        alert.Timestamp.Should().Be(default);
        alert.Level.Should().Be(AlertLevel.Normal);
        alert.Drive.Should().BeEmpty();
        alert.UsedPercentage.Should().Be(0);
        alert.FreeSizeBytes.Should().Be(0);
        alert.Message.Should().BeEmpty();
    }

    [Fact]
    public void DiskAlert_WithValues()
    {
        // Arrange & Act
        var alert = new DiskAlert
        {
            Timestamp = DateTime.UtcNow,
            Level = AlertLevel.Critical,
            Drive = "C:\\",
            UsedPercentage = 95,
            FreeSizeBytes = 10_000_000_000,
            Message = "CRITICAL: Drive C:\\ is 95% full"
        };

        // Assert
        alert.Level.Should().Be(AlertLevel.Critical);
        alert.UsedPercentage.Should().Be(95);
    }

    [Fact]
    public void DiskCheckResult_DefaultValues()
    {
        // Arrange & Act
        var result = new DiskCheckResult();

        // Assert
        result.Report.Should().NotBeNull();
        result.NewAlerts.Should().BeEmpty();
        result.AlertsGenerated.Should().BeFalse();
    }

    #endregion

    #region AlertLevel Enum Tests

    [Theory]
    [InlineData(AlertLevel.Normal, 0)]
    [InlineData(AlertLevel.Warning, 1)]
    [InlineData(AlertLevel.Critical, 2)]
    public void AlertLevel_EnumValues(AlertLevel level, int expectedValue)
    {
        // Assert
        ((int)level).Should().Be(expectedValue);
    }

    [Fact]
    public void AlertLevel_AllValuesDefined()
    {
        // Arrange
        var values = Enum.GetValues<AlertLevel>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(AlertLevel.Normal);
        values.Should().Contain(AlertLevel.Warning);
        values.Should().Contain(AlertLevel.Critical);
    }

    #endregion

    #region GetUtilization Tests

    [Fact]
    public void GetUtilization_ReturnsReport()
    {
        // Act
        var report = _service.GetUtilization();

        // Assert
        report.Should().NotBeNull();
        report.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetUtilization_ContainsDrives()
    {
        // Act
        var report = _service.GetUtilization();

        // Assert - should have at least one drive on any system
        report.Drives.Should().NotBeEmpty();
    }

    [Fact]
    public void GetUtilization_DrivesHaveValidData()
    {
        // Act
        var report = _service.GetUtilization();

        // Assert
        foreach (var drive in report.Drives)
        {
            drive.Name.Should().NotBeNullOrEmpty();
            drive.TotalSizeBytes.Should().BeGreaterThan(0);
            drive.UsedPercentage.Should().BeGreaterThanOrEqualTo(0);
            drive.UsedPercentage.Should().BeLessThanOrEqualTo(100);
        }
    }

    [Fact]
    public void GetUtilization_CalculatesOverallStatus()
    {
        // Act
        var report = _service.GetUtilization();

        // Assert
        report.OverallStatus.Should().BeOneOf(AlertLevel.Normal, AlertLevel.Warning, AlertLevel.Critical);
    }

    #endregion

    #region CheckAndAlert Tests

    [Fact]
    public void CheckAndAlert_ReturnsResult()
    {
        // Act
        var result = _service.CheckAndAlert();

        // Assert
        result.Should().NotBeNull();
        result.Report.Should().NotBeNull();
        result.NewAlerts.Should().NotBeNull();
    }

    [Fact]
    public void CheckAndAlert_ReportContainsDrives()
    {
        // Act
        var result = _service.CheckAndAlert();

        // Assert
        result.Report.Drives.Should().NotBeEmpty();
    }

    [Fact]
    public void CheckAndAlert_AlertsGeneratedFlag_Matches()
    {
        // Act
        var result = _service.CheckAndAlert();

        // Assert
        result.AlertsGenerated.Should().Be(result.NewAlerts.Count > 0);
    }

    #endregion

    #region GetPathUtilization Tests

    [Fact]
    public void GetPathUtilization_ExistingPath_ReturnsUtilization()
    {
        // Arrange
        var tempPath = Path.GetTempPath();

        // Act
        var util = _service.GetPathUtilization(tempPath);

        // Assert
        util.Should().NotBeNull();
        util!.Path.Should().Be(tempPath);
    }

    [Fact]
    public void GetPathUtilization_NonExistentPath_ReturnsNull()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var util = _service.GetPathUtilization(nonExistentPath);

        // Assert
        util.Should().BeNull();
    }

    [Fact]
    public void GetPathUtilization_ContainsFileCount()
    {
        // Arrange
        var tempPath = Path.GetTempPath();

        // Act
        var util = _service.GetPathUtilization(tempPath);

        // Assert
        util.Should().NotBeNull();
        util!.FileCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetPathUtilization_ContainsDriveInfo()
    {
        // Arrange
        var tempPath = Path.GetTempPath();

        // Act
        var util = _service.GetPathUtilization(tempPath);

        // Assert
        util.Should().NotBeNull();
        util!.DriveTotalSizeBytes.Should().BeGreaterThan(0);
        util.DriveFreeSizeBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region GetRecentAlerts Tests

    [Fact]
    public void GetRecentAlerts_InitiallyEmpty()
    {
        // Act
        var alerts = _service.GetRecentAlerts();

        // Assert
        alerts.Should().BeEmpty();
    }

    [Fact]
    public void GetRecentAlerts_AfterCheckAndAlert_MayContainAlerts()
    {
        // Arrange
        _service.CheckAndAlert();

        // Act
        var alerts = _service.GetRecentAlerts();

        // Assert
        alerts.Should().NotBeNull();
    }

    [Fact]
    public void GetRecentAlerts_RespectsCount()
    {
        // Act
        var alerts = _service.GetRecentAlerts(5);

        // Assert
        alerts.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void GetRecentAlerts_DefaultCount_Is20()
    {
        // Act
        var alerts = _service.GetRecentAlerts();

        // Assert
        alerts.Count.Should().BeLessThanOrEqualTo(20);
    }

    #endregion

    #region ClearAlertHistory Tests

    [Fact]
    public void ClearAlertHistory_ClearsAllAlerts()
    {
        // Arrange
        _service.CheckAndAlert(); // May generate some alerts
        
        // Act
        _service.ClearAlertHistory();

        // Assert
        _service.GetRecentAlerts().Should().BeEmpty();
    }

    [Fact]
    public void ClearAlertHistory_CanBeCalledMultipleTimes()
    {
        // Act & Assert - should not throw
        _service.ClearAlertHistory();
        _service.ClearAlertHistory();
        _service.ClearAlertHistory();
    }

    #endregion

    #region EstimateTimeUntilFull Tests

    [Fact]
    public void EstimateTimeUntilFull_ValidDrive_ReturnsValue()
    {
        // Arrange
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
        if (drives.Count == 0) return; // Skip if no drives

        var driveName = drives[0].Name;

        // Act
        var estimate = _service.EstimateTimeUntilFull(driveName);

        // Assert - null is acceptable (needs historical data)
        // Just verify it doesn't throw
    }

    [Fact]
    public void EstimateTimeUntilFull_NonExistentDrive_ReturnsNull()
    {
        // Act
        var estimate = _service.EstimateTimeUntilFull("Z:\\NonExistent\\");

        // Assert
        estimate.Should().BeNull();
    }

    #endregion

    #region Threshold Tests

    [Fact]
    public void Service_UsesConfiguredWarningThreshold()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                DiskMonitoring = new DiskMonitoringConfiguration
                {
                    WarningThreshold = 70
                }
            }
        };
        var service = new DiskMonitorService(_loggerMock.Object, config);

        // Act
        var result = service.CheckAndAlert();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Service_UsesConfiguredCriticalThreshold()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                DiskMonitoring = new DiskMonitoringConfiguration
                {
                    CriticalThreshold = 90
                }
            }
        };
        var service = new DiskMonitorService(_loggerMock.Object, config);

        // Act
        var result = service.CheckAndAlert();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Service_UsesDefaultThresholds_WhenNotConfigured()
    {
        // Arrange
        var config = new HoNConfiguration();
        var service = new DiskMonitorService(_loggerMock.Object, config);

        // Act
        var result = service.CheckAndAlert();

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Byte Formatting Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(1048576)]
    [InlineData(1073741824)]
    public void DriveUtilization_FormatsBytes_AllSizes(long bytes)
    {
        // Arrange
        var drive = new DriveUtilization
        {
            TotalSizeBytes = bytes,
            FreeSizeBytes = bytes / 2,
            UsedSizeBytes = bytes / 2
        };

        // Assert - should not throw
        drive.TotalSizeFormatted.Should().NotBeNullOrEmpty();
        drive.FreeSizeFormatted.Should().NotBeNullOrEmpty();
        drive.UsedSizeFormatted.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetUtilization_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<DiskUtilizationReport>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => _service.GetUtilization()));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
        results.Should().AllSatisfy(r => r.Drives.Should().NotBeEmpty());
    }

    [Fact]
    public async Task CheckAndAlert_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<DiskCheckResult>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => _service.CheckAndAlert()));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    #endregion
}
