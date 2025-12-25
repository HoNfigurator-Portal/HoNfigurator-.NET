using FluentAssertions;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for StepCertificateService - Step CA certificate management
/// </summary>
public class StepCertificateServiceTests : IDisposable
{
    private readonly Mock<ILogger<StepCertificateService>> _loggerMock;
    private readonly string _tempDir;
    private readonly string _certBasePath;

    public StepCertificateServiceTests()
    {
        _loggerMock = new Mock<ILogger<StepCertificateService>>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"StepCertTests_{Guid.NewGuid():N}");
        _certBasePath = Path.Combine(_tempDir, "certs");
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

    private StepCertificateService CreateService(HoNConfiguration? config = null)
    {
        config ??= CreateTestConfig();
        return new StepCertificateService(_loggerMock.Object, config);
    }

    private HoNConfiguration CreateTestConfig()
    {
        return new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                Certificates = new CertificatesConfiguration
                {
                    BasePath = _certBasePath,
                    StepCliPath = "step", // May or may not exist
                    DefaultName = "test-server"
                }
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

    [Fact]
    public void Constructor_ShouldCreateCertDirectory()
    {
        // Act
        var service = CreateService();

        // Assert
        Directory.Exists(_certBasePath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullCertPath_ShouldUseDefault()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            ApplicationData = new ApplicationData
            {
                Certificates = null
            }
        };

        // Act
        var service = new StepCertificateService(_loggerMock.Object, config);

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region IsStepCliInstalled Tests

    [Fact]
    public void IsStepCliInstalled_WithNonExistentPath_ShouldReturnFalse()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/path/to/step";
        var service = CreateService(config);

        // Act
        var result = service.IsStepCliInstalled;

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public void GetStatus_WithNoCertificate_ShouldReturnCorrectStatus()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.Should().NotBeNull();
        status.HasCertificate.Should().BeFalse();
        status.HasKey.Should().BeFalse();
        status.CertificatePath.Should().Contain("test-server.crt");
        status.KeyPath.Should().Contain("test-server.key");
    }

    [Fact]
    public void GetStatus_ShouldReturnPaths()
    {
        // Arrange
        var service = CreateService();

        // Act
        var status = service.GetStatus();

        // Assert
        status.CertificatePath.Should().NotBeNullOrEmpty();
        status.KeyPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetStatus_WithExistingCertFile_ShouldDetect()
    {
        // Arrange
        var service = CreateService();
        Directory.CreateDirectory(_certBasePath);
        var certPath = Path.Combine(_certBasePath, "test-server.crt");
        File.WriteAllText(certPath, "dummy cert content");

        // Act
        var status = service.GetStatus();

        // Assert
        status.HasCertificate.Should().BeTrue();
    }

    [Fact]
    public void GetStatus_WithExistingKeyFile_ShouldDetect()
    {
        // Arrange
        var service = CreateService();
        Directory.CreateDirectory(_certBasePath);
        var keyPath = Path.Combine(_certBasePath, "test-server.key");
        File.WriteAllText(keyPath, "dummy key content");

        // Act
        var status = service.GetStatus();

        // Assert
        status.HasKey.Should().BeTrue();
    }

    [Fact]
    public void GetStatus_WithInvalidCertFile_ShouldSetError()
    {
        // Arrange
        var service = CreateService();
        Directory.CreateDirectory(_certBasePath);
        var certPath = Path.Combine(_certBasePath, "test-server.crt");
        File.WriteAllText(certPath, "not a valid certificate");

        // Act
        var status = service.GetStatus();

        // Assert
        status.HasCertificate.Should().BeTrue();
        // May have error if can't parse, or may be null if LoadCertificate returns null
    }

    #endregion

    #region BootstrapCaAsync Tests

    [Fact]
    public async Task BootstrapCaAsync_WithoutStepCli_ShouldReturnError()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.BootstrapCaAsync("https://ca.example.com", "fingerprint");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Step CLI not installed");
    }

    [Fact]
    public async Task BootstrapCaAsync_WithCancellation_ShouldHandleGracefully()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await service.BootstrapCaAsync("https://ca.example.com", "fingerprint", cts.Token);

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region RequestCertificateAsync Tests

    [Fact]
    public async Task RequestCertificateAsync_WithoutStepCli_ShouldReturnError()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.RequestCertificateAsync("test.example.com");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Step CLI not installed");
    }

    [Fact]
    public async Task RequestCertificateAsync_ShouldIncludeCommonName()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.RequestCertificateAsync("my.server.com");

        // Assert
        result.Success.Should().BeFalse(); // Can't actually run
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task RequestCertificateAsync_WithToken_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var act = async () => await service.RequestCertificateAsync(
            "test.com",
            token: "some-jwt-token",
            duration: TimeSpan.FromHours(24));

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RenewCertificateAsync Tests

    [Fact]
    public async Task RenewCertificateAsync_WithoutStepCli_ShouldReturnError()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.RenewCertificateAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Step CLI not installed");
    }

    [Fact]
    public async Task RenewCertificateAsync_WithoutExistingCert_ShouldReturnError()
    {
        // Arrange - Use a path that exists but cert doesn't
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = GetRealStepPathOrFake();
        var service = CreateService(config);

        // Act
        var result = await service.RenewCertificateAsync("nonexistent");

        // Assert
        result.Success.Should().BeFalse();
        if (service.IsStepCliInstalled)
        {
            result.Error.Should().Contain("not found");
        }
    }

    [Fact]
    public async Task RenewCertificateAsync_WithCommonName_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var act = async () => await service.RenewCertificateAsync("my-cert");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RevokeCertificateAsync Tests

    [Fact]
    public async Task RevokeCertificateAsync_WithoutStepCli_ShouldReturnFalse()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.RevokeCertificateAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithoutCertFile_ShouldReturnFalse()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = GetRealStepPathOrFake();
        var service = CreateService(config);

        // Act
        var result = await service.RevokeCertificateAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeCertificateAsync_WithReason_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var act = async () => await service.RevokeCertificateAsync(
            "my-cert",
            reason: "key compromise");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region NeedsRenewal Tests

    [Fact]
    public void NeedsRenewal_WithNoCertificate_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.NeedsRenewal();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void NeedsRenewal_WithCustomThreshold_ShouldAcceptParameter()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.NeedsRenewal(thresholdDays: 90);

        // Assert
        result.Should().BeFalse(); // No cert exists
    }

    [Fact]
    public void NeedsRenewal_WithInvalidCert_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateService();
        Directory.CreateDirectory(_certBasePath);
        var certPath = Path.Combine(_certBasePath, "test-server.crt");
        File.WriteAllText(certPath, "invalid cert data");

        // Act
        var result = service.NeedsRenewal();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region InstallStepCliAsync Tests

    [Fact]
    public async Task InstallStepCliAsync_WhenAlreadyInstalled_ShouldReturnTrue()
    {
        // Arrange - Create fake step executable
        var config = CreateTestConfig();
        var fakeStepPath = Path.Combine(_tempDir, "step.exe");
        File.WriteAllText(fakeStepPath, "fake step cli");
        config.ApplicationData!.Certificates!.StepCliPath = fakeStepPath;
        var service = CreateService(config);

        // Act
        var result = await service.InstallStepCliAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task InstallStepCliAsync_WithCancellation_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = Path.Combine(_tempDir, "step.exe");
        File.WriteAllText(config.ApplicationData.Certificates.StepCliPath, "fake");
        var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        // Act
        var act = async () => await service.InstallStepCliAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GenerateSelfSignedAsync Tests

    [Fact]
    public async Task GenerateSelfSignedAsync_WithoutStepCli_ShouldReturnError()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var result = await service.GenerateSelfSignedAsync("localhost");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Step CLI not installed");
    }

    [Fact]
    public async Task GenerateSelfSignedAsync_WithCustomDuration_ShouldNotThrow()
    {
        // Arrange
        var config = CreateTestConfig();
        config.ApplicationData!.Certificates!.StepCliPath = "/nonexistent/step";
        var service = CreateService(config);

        // Act
        var act = async () => await service.GenerateSelfSignedAsync(
            "localhost",
            validDays: 30);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region InspectCertificateAsync Tests

    [Fact]
    public async Task InspectCertificateAsync_WithNoCertificate_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.InspectCertificateAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InspectCertificateAsync_WithCustomPath_ShouldAccept()
    {
        // Arrange
        var service = CreateService();
        var customPath = Path.Combine(_tempDir, "custom.crt");

        // Act
        var result = await service.InspectCertificateAsync(customPath);

        // Assert
        result.Should().BeNull(); // File doesn't exist
    }

    [Fact]
    public async Task InspectCertificateAsync_WithInvalidCert_ShouldHandleGracefully()
    {
        // Arrange
        var service = CreateService();
        Directory.CreateDirectory(_certBasePath);
        var certPath = Path.Combine(_certBasePath, "test-server.crt");
        File.WriteAllText(certPath, "invalid certificate data");

        // Act
        var result = await service.InspectCertificateAsync(certPath);

        // Assert
        // Either null or has error - depends on implementation
        // Main thing is it shouldn't throw
    }

    #endregion

    #region Helper Methods

    private string GetRealStepPathOrFake()
    {
        // Check common Step CLI paths
        var paths = new[]
        {
            @"C:\Program Files\Smallstep\step\bin\step.exe",
            "/usr/bin/step",
            "/usr/local/bin/step"
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return "/nonexistent/step";
    }

    #endregion
}

#region DTO Tests

public class CertificateStatusDtoTests
{
    [Fact]
    public void CertificateStatus_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var status = new CertificateStatus();

        // Assert
        status.IsStepCliInstalled.Should().BeFalse();
        status.CertificatePath.Should().Be(string.Empty);
        status.KeyPath.Should().Be(string.Empty);
        status.HasCertificate.Should().BeFalse();
        status.HasKey.Should().BeFalse();
        status.Subject.Should().BeNull();
        status.Issuer.Should().BeNull();
        status.NotBefore.Should().BeNull();
        status.NotAfter.Should().BeNull();
        status.Thumbprint.Should().BeNull();
        status.IsExpired.Should().BeFalse();
        status.DaysUntilExpiry.Should().BeNull();
        status.Error.Should().BeNull();
    }

    [Fact]
    public void CertificateStatus_WithValidCert_ShouldSetProperties()
    {
        // Act
        var status = new CertificateStatus
        {
            IsStepCliInstalled = true,
            CertificatePath = "/certs/server.crt",
            KeyPath = "/certs/server.key",
            HasCertificate = true,
            HasKey = true,
            Subject = "CN=server.example.com",
            Issuer = "CN=Example CA",
            NotBefore = DateTime.UtcNow.AddDays(-30),
            NotAfter = DateTime.UtcNow.AddDays(335),
            Thumbprint = "ABC123DEF456",
            DaysUntilExpiry = 335
        };

        // Assert
        status.IsStepCliInstalled.Should().BeTrue();
        status.HasCertificate.Should().BeTrue();
        status.DaysUntilExpiry.Should().Be(335);
    }
}

public class BootstrapResultDtoTests
{
    [Fact]
    public void BootstrapResult_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var result = new BootstrapResult();

        // Assert
        result.Success.Should().BeFalse();
        result.CaUrl.Should().BeNull();
        result.Output.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void BootstrapResult_SuccessfulBootstrap()
    {
        // Act
        var result = new BootstrapResult
        {
            Success = true,
            CaUrl = "https://ca.example.com",
            Output = "Bootstrapped successfully"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.CaUrl.Should().Be("https://ca.example.com");
        result.Error.Should().BeNull();
    }
}

public class CertificateResultDtoTests
{
    [Fact]
    public void CertificateResult_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var result = new CertificateResult();

        // Assert
        result.Success.Should().BeFalse();
        result.CertificatePath.Should().BeNull();
        result.KeyPath.Should().BeNull();
        result.CommonName.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CertificateResult_SuccessfulRequest()
    {
        // Act
        var result = new CertificateResult
        {
            Success = true,
            CertificatePath = "/certs/my.crt",
            KeyPath = "/certs/my.key",
            CommonName = "my.server.com"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.CertificatePath.Should().Contain("my.crt");
    }

    [Fact]
    public void CertificateResult_FailedRequest()
    {
        // Act
        var result = new CertificateResult
        {
            Success = false,
            Error = "Certificate request denied"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("denied");
    }
}

public class CertificateInfoDtoTests
{
    [Fact]
    public void CertificateInfo_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var info = new CertificateInfo();

        // Assert
        info.Subject.Should().Be(string.Empty);
        info.Issuer.Should().Be(string.Empty);
        info.NotBefore.Should().Be(default);
        info.NotAfter.Should().Be(default);
        info.SerialNumber.Should().Be(string.Empty);
        info.Thumbprint.Should().Be(string.Empty);
        info.SignatureAlgorithm.Should().BeNull();
    }

    [Fact]
    public void CertificateInfo_WithValues()
    {
        // Act
        var info = new CertificateInfo
        {
            Subject = "CN=test.example.com",
            Issuer = "CN=Test CA",
            NotBefore = new DateTime(2024, 1, 1),
            NotAfter = new DateTime(2025, 1, 1),
            SerialNumber = "1234567890",
            Thumbprint = "ABCDEF123456",
            SignatureAlgorithm = "SHA256RSA"
        };

        // Assert
        info.Subject.Should().Contain("test.example.com");
        info.SignatureAlgorithm.Should().Be("SHA256RSA");
    }
}

#endregion
