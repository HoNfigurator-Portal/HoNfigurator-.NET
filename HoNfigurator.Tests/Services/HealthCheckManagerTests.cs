using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Health;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Tests.Services;

public class HealthCheckManagerTests
{
    private readonly Mock<ILogger<HealthCheckManager>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly HealthCheckManager _manager;

    public HealthCheckManagerTests()
    {
        _loggerMock = new Mock<ILogger<HealthCheckManager>>();
        _config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonInstallDirectory = @"C:\Games\HoN",
                StartingGamePort = 11000,
                TotalServers = 2
            }
        };

        _manager = new HealthCheckManager(_loggerMock.Object, _config);
    }

    [Fact]
    public void GetSystemResources_ShouldReturnValidData()
    {
        // Act
        var resources = _manager.GetSystemResources();

        // Assert
        resources.Should().NotBeNull();
        resources.CpuUsagePercent.Should().BeGreaterThanOrEqualTo(0);
        resources.MemoryUsagePercent.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task RunAllChecksAsync_ShouldReturnResults()
    {
        // Act
        var results = await _manager.RunAllChecksAsync();

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateIpAsync_ShouldReturnResult_ForValidIp()
    {
        // Arrange
        var ipAddress = "8.8.8.8";

        // Act
        var result = await _manager.ValidateIpAsync(ipAddress);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateIpAsync_ShouldReturnInvalid_ForBadIp()
    {
        // Arrange
        var ipAddress = "invalid-ip";

        // Act
        var result = await _manager.ValidateIpAsync(ipAddress);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateIpAsync_ShouldReturnResult_ForLocalhost()
    {
        // Arrange
        var ipAddress = "127.0.0.1";

        // Act
        var result = await _manager.ValidateIpAsync(ipAddress);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHoNInstallationAsync_ShouldReturnResult()
    {
        // Act
        var result = await _manager.CheckHoNInstallationAsync();

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Contain("Installation");
    }

    [Fact]
    public async Task CheckLagAsync_ShouldReturnResult()
    {
        // Act
        var result = await _manager.CheckLagAsync();

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Contain("Lag");
    }

    [Fact]
    public async Task CheckPortAvailabilityAsync_ShouldReturnResult()
    {
        // Arrange
        var port = 11000;

        // Act
        var result = await _manager.CheckPortAvailabilityAsync(port);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Contain("Port");
    }

    [Fact]
    public async Task RunEnhancedChecksAsync_ShouldReturnMultipleResults()
    {
        // Act
        var results = await _manager.RunEnhancedChecksAsync();

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
    }
}

public class HealthCheckResultRecordTests
{
    [Fact]
    public void HealthCheckResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new HealthCheckResult();

        // Assert
        result.Name.Should().BeEmpty();
        result.IsHealthy.Should().BeFalse();
        result.Status.Should().BeEmpty();
        result.Message.Should().BeNull();
        result.ResponseTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void HealthCheckResult_ShouldBeSettable()
    {
        // Arrange & Act
        var result = new HealthCheckResult
        {
            Name = "Test Check",
            IsHealthy = true,
            Status = "Passed",
            Message = "All good",
            ResponseTime = TimeSpan.FromMilliseconds(100)
        };

        // Assert
        result.Name.Should().Be("Test Check");
        result.IsHealthy.Should().BeTrue();
        result.Status.Should().Be("Passed");
        result.Message.Should().Be("All good");
        result.ResponseTime.Should().Be(TimeSpan.FromMilliseconds(100));
    }
}

public class SystemResourcesRecordTests
{
    [Fact]
    public void SystemResources_ShouldInitialize_WithDefaults()
    {
        // Act
        var resources = new SystemResources();

        // Assert
        resources.CpuUsagePercent.Should().Be(0);
        resources.MemoryUsagePercent.Should().Be(0);
        resources.MemoryTotalMb.Should().Be(0);
        resources.MemoryUsedMb.Should().Be(0);
        resources.DiskUsagePercent.Should().Be(0);
    }
}

public class IpValidationResultRecordTests
{
    [Fact]
    public void IpValidationResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new IpValidationResult();

        // Assert
        result.IpAddress.Should().BeEmpty();
        result.IsValid.Should().BeFalse();
        result.IsExternal.Should().BeFalse();
        result.IsReachable.Should().BeFalse();
    }

    [Fact]
    public void IpValidationResult_ShouldBeSettable()
    {
        // Arrange & Act
        var result = new IpValidationResult
        {
            IpAddress = "8.8.8.8",
            IsValid = true,
            IsExternal = true,
            IsReachable = true
        };

        // Assert
        result.IpAddress.Should().Be("8.8.8.8");
        result.IsValid.Should().BeTrue();
        result.IsExternal.Should().BeTrue();
        result.IsReachable.Should().BeTrue();
    }
}

/// <summary>
/// Tests for Management Portal Health Check
/// </summary>
public class ManagementPortalHealthCheckTests
{
    private readonly Mock<ILogger<HealthCheckManager>> _loggerMock;

    public ManagementPortalHealthCheckTests()
    {
        _loggerMock = new Mock<ILogger<HealthCheckManager>>();
    }

    [Fact]
    public async Task CheckManagementPortalAsync_ShouldReturnDisabled_WhenNotConfigured()
    {
        // Arrange - config without management portal
        var config = new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "Test" },
            ApplicationData = null
        };
        var manager = new HealthCheckManager(_loggerMock.Object, config);

        // Act
        var result = await manager.CheckManagementPortalAsync();

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("ManagementPortal");
        result.Status.Should().Be("Disabled");
        result.IsHealthy.Should().BeTrue(); // Disabled is not unhealthy
        result.Data.Should().ContainKey("Enabled");
        result.Data["Enabled"].Should().Be(false);
    }

    [Fact]
    public async Task CheckManagementPortalAsync_ShouldReturnDisabled_WhenExplicitlyDisabled()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "Test" },
            ApplicationData = new ApplicationData
            {
                ManagementPortal = new ManagementPortalSettings
                {
                    Enabled = false
                }
            }
        };
        var manager = new HealthCheckManager(_loggerMock.Object, config);

        // Act
        var result = await manager.CheckManagementPortalAsync();

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("ManagementPortal");
        result.Status.Should().Be("Disabled");
        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task CheckManagementPortalAsync_ShouldIncludeEnabled_WhenEnabled()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "Test" },
            ApplicationData = new ApplicationData
            {
                ManagementPortal = new ManagementPortalSettings
                {
                    Enabled = true,
                    PortalUrl = "https://test.portal.com:3001"
                }
            }
        };
        var manager = new HealthCheckManager(_loggerMock.Object, config);

        // Act
        var result = await manager.CheckManagementPortalAsync();

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("ManagementPortal");
        result.Data.Should().ContainKey("Enabled");
        result.Data["Enabled"].Should().Be(true);
        // Note: PortalUrl is only included on successful connection
        // When connection fails, it includes "Error" key instead
    }

    [Fact]
    public async Task RunAllChecksAsync_ShouldIncludeManagementPortalCheck()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData { ServerName = "Test" }
        };
        var manager = new HealthCheckManager(_loggerMock.Object, config);

        // Act
        var results = await manager.RunAllChecksAsync();

        // Assert
        results.Should().ContainKey("ManagementPortal");
    }
}
