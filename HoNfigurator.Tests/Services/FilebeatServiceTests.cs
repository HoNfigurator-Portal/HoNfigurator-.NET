using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for FilebeatService - Log shipping via Filebeat to Elasticsearch
/// </summary>
public class FilebeatServiceTests : IDisposable
{
    private readonly Mock<ILogger<FilebeatService>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly string _tempDir;

    public FilebeatServiceTests()
    {
        _loggerMock = new Mock<ILogger<FilebeatService>>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"FilebeatTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                ServerName = "TestServer",
                HonInstallDirectory = _tempDir
            },
            ApplicationData = new ApplicationData
            {
                Filebeat = new FilebeatConfiguration
                {
                    InstallPath = Path.Combine(_tempDir, "filebeat"),
                    ElasticsearchHost = "localhost:9200",
                    Environment = "test",
                    LogPaths = new List<string> { "/var/log/*.log" }
                }
            }
        };
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

    private FilebeatService CreateService(HoNConfiguration? customConfig = null)
    {
        return new FilebeatService(_loggerMock.Object, customConfig ?? _config);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitialize()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldCreateFilebeatDirectory()
    {
        // Arrange
        var filebeatPath = Path.Combine(_tempDir, "filebeat");

        // Act
        var service = CreateService();

        // Assert
        Directory.Exists(filebeatPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullFilebeatConfig_ShouldUseDefaultPath()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData { Filebeat = null }
        };

        // Act
        var service = CreateService(config);

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region IsInstalled Property Tests

    [Fact]
    public void IsInstalled_WhenExecutableDoesNotExist_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var isInstalled = service.IsInstalled;

        // Assert
        isInstalled.Should().BeFalse();
    }

    [Fact]
    public void IsInstalled_WhenExecutableExists_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateService();
        var execPath = service.FilebeatExecutablePath;
        Directory.CreateDirectory(Path.GetDirectoryName(execPath)!);
        File.WriteAllText(execPath, "dummy");

        // Act
        var isInstalled = service.IsInstalled;

        // Assert
        isInstalled.Should().BeTrue();
    }

    #endregion

    #region IsRunning Property Tests

    [Fact]
    public void IsRunning_Initially_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var isRunning = service.IsRunning;

        // Assert
        isRunning.Should().BeFalse();
    }

    #endregion

    #region FilebeatExecutablePath Property Tests

    [Fact]
    public void FilebeatExecutablePath_OnWindows_ShouldEndWithExe()
    {
        // Arrange
        var service = CreateService();

        // Act
        var path = service.FilebeatExecutablePath;

        // Assert
        if (OperatingSystem.IsWindows())
        {
            path.Should().EndWith("filebeat.exe");
        }
        else
        {
            path.Should().EndWith("filebeat");
        }
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public void GetStatus_ShouldReturnFilebeatStatus()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.Should().NotBeNull();
        status.Should().BeOfType<FilebeatStatus>();
    }

    [Fact]
    public void GetStatus_WhenNotInstalled_ShouldShowNotInstalled()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.IsInstalled.Should().BeFalse();
        status.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void GetStatus_ShouldIncludeConfigPath()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.ConfigPath.Should().NotBeNullOrEmpty();
        status.ConfigPath.Should().EndWith("filebeat.yml");
    }

    [Fact]
    public void GetStatus_ShouldIncludeInstallPath()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.InstallPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetStatus_ShouldIncludeElasticsearchHost()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.ElasticsearchHost.Should().Be("localhost:9200");
    }

    [Fact]
    public void GetStatus_ShouldIncludeLogPaths()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.LogPaths.Should().NotBeNull();
        status.LogPaths.Should().Contain("/var/log/*.log");
    }

    #endregion

    #region InstallAsync Tests

    [Fact]
    public void InstallAsync_WhenAlreadyInstalled_IsInstalledShouldBeTrue()
    {
        // Arrange
        var service = CreateService();
        var execPath = service.FilebeatExecutablePath;
        Directory.CreateDirectory(Path.GetDirectoryName(execPath)!);
        File.WriteAllText(execPath, "dummy");

        // Act - Only test IsInstalled property, don't call InstallAsync
        // because it calls GetInstalledVersion() which tries to execute the file
        var isInstalled = service.IsInstalled;

        // Assert
        isInstalled.Should().BeTrue();
    }

    [Fact]
    public void InstallAsync_CancellationToken_ShouldBeAccepted()
    {
        // Arrange
        var service = CreateService();
        
        // Act & Assert - Just verify the method signature accepts CancellationToken
        // We don't actually call it to avoid downloading real Filebeat
        var methodInfo = typeof(FilebeatService).GetMethod("InstallAsync");
        methodInfo.Should().NotBeNull();
        
        var parameters = methodInfo!.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[0].HasDefaultValue.Should().BeTrue();
    }

    #endregion

    #region GenerateConfigAsync Tests

    [Fact]
    public async Task GenerateConfigAsync_ShouldCreateConfigFile()
    {
        // Arrange
        var service = CreateService();
        var configPath = Path.Combine(_tempDir, "filebeat", "filebeat.yml");

        // Act
        await service.GenerateConfigAsync();

        // Assert
        File.Exists(configPath).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateConfigAsync_ShouldIncludeServerName()
    {
        // Arrange
        var service = CreateService();
        var configPath = Path.Combine(_tempDir, "filebeat", "filebeat.yml");

        // Act
        await service.GenerateConfigAsync();

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("TestServer");
    }

    [Fact]
    public async Task GenerateConfigAsync_ShouldIncludeElasticsearchHost()
    {
        // Arrange
        var service = CreateService();
        var configPath = Path.Combine(_tempDir, "filebeat", "filebeat.yml");

        // Act
        await service.GenerateConfigAsync();

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("localhost:9200");
    }

    [Fact]
    public async Task GenerateConfigAsync_ShouldIncludeEnvironment()
    {
        // Arrange
        var service = CreateService();
        var configPath = Path.Combine(_tempDir, "filebeat", "filebeat.yml");

        // Act
        await service.GenerateConfigAsync();

        // Assert
        var content = await File.ReadAllTextAsync(configPath);
        content.Should().Contain("test");
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenNotInstalled_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.StartAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenNotInstalled_ShouldLogWarning()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.StartAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not installed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WhenNotRunning_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.StopAsync();

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region RestartAsync Tests

    [Fact]
    public async Task RestartAsync_WhenNotInstalled_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.RestartAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region TestElasticsearchConnectionAsync Tests

    [Fact]
    public async Task TestElasticsearchConnectionAsync_ShouldReturnResult()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.TestElasticsearchConnectionAsync();

        // Assert
        result.Should().NotBeNull();
        result.Host.Should().Be("localhost:9200");
    }

    [Fact]
    public async Task TestElasticsearchConnectionAsync_WhenConnectionFails_ShouldReturnError()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                Filebeat = new FilebeatConfiguration
                {
                    ElasticsearchHost = "nonexistent.invalid:9200"
                }
            }
        };
        var service = CreateService(config);

        // Act
        var result = await service.TestElasticsearchConnectionAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TestElasticsearchConnectionAsync_WithDefaultHost_ShouldUseLocalhost()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                Filebeat = new FilebeatConfiguration { ElasticsearchHost = null }
            }
        };
        var service = CreateService(config);

        // Act
        var result = await service.TestElasticsearchConnectionAsync();

        // Assert
        result.Host.Should().Be("localhost:9200");
    }

    #endregion
}

#region DTO Tests

public class FilebeatStatusDtoTests
{
    [Fact]
    public void FilebeatStatus_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var status = new FilebeatStatus();

        // Assert
        status.IsInstalled.Should().BeFalse();
        status.IsRunning.Should().BeFalse();
        status.Version.Should().BeNull();
        status.ConfigPath.Should().BeNull();
        status.InstallPath.Should().BeNull();
        status.ElasticsearchHost.Should().BeNull();
        status.LogPaths.Should().NotBeNull();
        status.LogPaths.Should().BeEmpty();
    }

    [Fact]
    public void FilebeatStatus_ShouldAllowSettingProperties()
    {
        // Act
        var status = new FilebeatStatus
        {
            IsInstalled = true,
            IsRunning = true,
            Version = "8.11.0",
            ConfigPath = "/etc/filebeat/filebeat.yml",
            InstallPath = "/opt/filebeat",
            ElasticsearchHost = "es.example.com:9200",
            LogPaths = new List<string> { "/var/log/app/*.log" }
        };

        // Assert
        status.IsInstalled.Should().BeTrue();
        status.IsRunning.Should().BeTrue();
        status.Version.Should().Be("8.11.0");
        status.LogPaths.Should().HaveCount(1);
    }
}

public class FilebeatInstallResultDtoTests
{
    [Fact]
    public void FilebeatInstallResult_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var result = new FilebeatInstallResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.Version.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FilebeatInstallResult_SuccessfulInstall()
    {
        // Act
        var result = new FilebeatInstallResult
        {
            Success = true,
            Message = "Filebeat installed successfully",
            Version = "8.11.0"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FilebeatInstallResult_FailedInstall()
    {
        // Act
        var result = new FilebeatInstallResult
        {
            Success = false,
            Message = "Installation failed",
            Error = "Download timeout"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Download timeout");
    }
}

public class ElasticsearchTestResultDtoTests
{
    [Fact]
    public void ElasticsearchTestResult_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var result = new ElasticsearchTestResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Host.Should().BeEmpty();
        result.Message.Should().BeEmpty();
        result.ClusterInfo.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ElasticsearchTestResult_SuccessfulConnection()
    {
        // Act
        var result = new ElasticsearchTestResult
        {
            Success = true,
            Host = "localhost:9200",
            Message = "Connected to Elasticsearch",
            ClusterInfo = "{\"cluster_name\":\"docker-cluster\"}"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.ClusterInfo.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ElasticsearchTestResult_FailedConnection()
    {
        // Act
        var result = new ElasticsearchTestResult
        {
            Success = false,
            Host = "es.example.com:9200",
            Message = "Connection failed: Connection refused",
            Error = "Connection refused"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Connection refused");
    }
}

public class FilebeatConfigurationDtoTests
{
    [Fact]
    public void FilebeatConfiguration_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var config = new FilebeatConfiguration();

        // Assert
        config.Enabled.Should().BeFalse();
        config.InstallPath.Should().NotBeNullOrEmpty();
        config.ElasticsearchHost.Should().Be("localhost:9200");
        config.ElasticsearchUsername.Should().BeNull();
        config.ElasticsearchPassword.Should().BeNull();
        config.Environment.Should().Be("production");
        config.LogPaths.Should().NotBeNull();
        config.AutoStart.Should().BeTrue();
    }

    [Fact]
    public void FilebeatConfiguration_ShouldAllowSettingProperties()
    {
        // Act
        var config = new FilebeatConfiguration
        {
            Enabled = true,
            InstallPath = "/opt/filebeat",
            ElasticsearchHost = "es.example.com:9200",
            ElasticsearchUsername = "elastic",
            ElasticsearchPassword = "changeme",
            Environment = "staging",
            LogPaths = new List<string> { "/var/log/*.log", "/app/logs/*.log" },
            AutoStart = true
        };

        // Assert
        config.Enabled.Should().BeTrue();
        config.InstallPath.Should().Be("/opt/filebeat");
        config.ElasticsearchHost.Should().Be("es.example.com:9200");
        config.ElasticsearchUsername.Should().Be("elastic");
        config.ElasticsearchPassword.Should().Be("changeme");
        config.Environment.Should().Be("staging");
        config.LogPaths.Should().HaveCount(2);
        config.AutoStart.Should().BeTrue();
    }
}

#endregion
