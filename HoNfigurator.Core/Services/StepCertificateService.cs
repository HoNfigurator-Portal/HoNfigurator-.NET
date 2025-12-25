using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages Step CA certificates for secure communication.
/// Port of Python HoNfigurator-Central step_certificate.py functionality.
/// </summary>
public class StepCertificateService
{
    private readonly ILogger<StepCertificateService> _logger;
    private readonly HoNConfiguration _config;
    private readonly string _certBasePath;
    private readonly string _stepCliPath;

    public bool IsStepCliInstalled => File.Exists(_stepCliPath);

    public StepCertificateService(ILogger<StepCertificateService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        _certBasePath = config.ApplicationData?.Certificates?.BasePath 
            ?? Path.Combine(AppContext.BaseDirectory, "certs");
        _stepCliPath = config.ApplicationData?.Certificates?.StepCliPath
            ?? FindStepCliPath();
        
        if (!Directory.Exists(_certBasePath))
            Directory.CreateDirectory(_certBasePath);
    }

    /// <summary>
    /// Get current certificate status
    /// </summary>
    public CertificateStatus GetStatus()
    {
        var certPath = GetCertificatePath();
        var keyPath = GetKeyPath();

        var status = new CertificateStatus
        {
            IsStepCliInstalled = IsStepCliInstalled,
            CertificatePath = certPath,
            KeyPath = keyPath,
            HasCertificate = File.Exists(certPath),
            HasKey = File.Exists(keyPath)
        };

        if (status.HasCertificate)
        {
            try
            {
                var cert = LoadCertificate(certPath);
                if (cert != null)
                {
                    status.Subject = cert.Subject;
                    status.Issuer = cert.Issuer;
                    status.NotBefore = cert.NotBefore;
                    status.NotAfter = cert.NotAfter;
                    status.Thumbprint = cert.Thumbprint;
                    status.IsExpired = cert.NotAfter < DateTime.UtcNow;
                    status.DaysUntilExpiry = (cert.NotAfter - DateTime.UtcNow).Days;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load certificate for status check");
                status.Error = ex.Message;
            }
        }

        return status;
    }

    /// <summary>
    /// Bootstrap connection to a Step CA server
    /// </summary>
    public async Task<BootstrapResult> BootstrapCaAsync(
        string caUrl, 
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        if (!IsStepCliInstalled)
        {
            return new BootstrapResult
            {
                Success = false,
                Error = "Step CLI not installed. Run InstallStepCliAsync() first."
            };
        }

        _logger.LogInformation("Bootstrapping Step CA: {Url}", caUrl);

        try
        {
            var result = await RunStepCommandAsync(
                $"ca bootstrap --ca-url {caUrl} --fingerprint {fingerprint} --install",
                cancellationToken);

            return new BootstrapResult
            {
                Success = result.ExitCode == 0,
                CaUrl = caUrl,
                Output = result.Output,
                Error = result.ExitCode != 0 ? result.Error : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap CA");
            return new BootstrapResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Request a new certificate from the CA
    /// </summary>
    public async Task<CertificateResult> RequestCertificateAsync(
        string commonName,
        string? token = null,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsStepCliInstalled)
        {
            return new CertificateResult
            {
                Success = false,
                Error = "Step CLI not installed"
            };
        }

        var certPath = GetCertificatePath(commonName);
        var keyPath = GetKeyPath(commonName);

        var args = $"ca certificate \"{commonName}\" \"{certPath}\" \"{keyPath}\"";
        
        if (!string.IsNullOrEmpty(token))
            args += $" --token {token}";
        
        if (duration.HasValue)
            args += $" --not-after {duration.Value.TotalHours}h";

        args += " --force"; // Overwrite existing

        _logger.LogInformation("Requesting certificate for: {CommonName}", commonName);

        try
        {
            var result = await RunStepCommandAsync(args, cancellationToken);

            if (result.ExitCode == 0)
            {
                return new CertificateResult
                {
                    Success = true,
                    CertificatePath = certPath,
                    KeyPath = keyPath,
                    CommonName = commonName
                };
            }

            return new CertificateResult
            {
                Success = false,
                Error = result.Error ?? result.Output
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request certificate");
            return new CertificateResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Renew an existing certificate
    /// </summary>
    public async Task<CertificateResult> RenewCertificateAsync(
        string? commonName = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsStepCliInstalled)
        {
            return new CertificateResult
            {
                Success = false,
                Error = "Step CLI not installed"
            };
        }

        var certPath = GetCertificatePath(commonName);
        var keyPath = GetKeyPath(commonName);

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return new CertificateResult
            {
                Success = false,
                Error = "Certificate or key not found"
            };
        }

        _logger.LogInformation("Renewing certificate: {Path}", certPath);

        try
        {
            var result = await RunStepCommandAsync(
                $"ca renew \"{certPath}\" \"{keyPath}\" --force",
                cancellationToken);

            return new CertificateResult
            {
                Success = result.ExitCode == 0,
                CertificatePath = certPath,
                KeyPath = keyPath,
                Error = result.ExitCode != 0 ? result.Error : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew certificate");
            return new CertificateResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Revoke a certificate
    /// </summary>
    public async Task<bool> RevokeCertificateAsync(
        string? commonName = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsStepCliInstalled)
        {
            return false;
        }

        var certPath = GetCertificatePath(commonName);
        
        if (!File.Exists(certPath))
        {
            _logger.LogWarning("Certificate not found: {Path}", certPath);
            return false;
        }

        var args = $"ca revoke \"{certPath}\"";
        if (!string.IsNullOrEmpty(reason))
            args += $" --reason \"{reason}\"";

        _logger.LogInformation("Revoking certificate: {Path}", certPath);

        try
        {
            var result = await RunStepCommandAsync(args, cancellationToken);
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke certificate");
            return false;
        }
    }

    /// <summary>
    /// Check if certificate needs renewal (default: within 30 days of expiry)
    /// </summary>
    public bool NeedsRenewal(string? commonName = null, int thresholdDays = 30)
    {
        var certPath = GetCertificatePath(commonName);
        if (!File.Exists(certPath))
            return false;

        try
        {
            var cert = LoadCertificate(certPath);
            if (cert == null)
                return false;

            var daysUntilExpiry = (cert.NotAfter - DateTime.UtcNow).Days;
            return daysUntilExpiry <= thresholdDays;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Install Step CLI automatically
    /// </summary>
    public async Task<bool> InstallStepCliAsync(CancellationToken cancellationToken = default)
    {
        if (IsStepCliInstalled)
        {
            _logger.LogInformation("Step CLI already installed");
            return true;
        }

        _logger.LogInformation("Installing Step CLI...");

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await InstallStepCliWindowsAsync(cancellationToken);
            }
            else if (OperatingSystem.IsLinux())
            {
                return await InstallStepCliLinuxAsync(cancellationToken);
            }
            else if (OperatingSystem.IsMacOS())
            {
                return await InstallStepCliMacAsync(cancellationToken);
            }

            _logger.LogError("Unsupported operating system for Step CLI installation");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Step CLI");
            return false;
        }
    }

    /// <summary>
    /// Generate a self-signed certificate (for testing/development)
    /// </summary>
    public async Task<CertificateResult> GenerateSelfSignedAsync(
        string commonName,
        int validDays = 365,
        CancellationToken cancellationToken = default)
    {
        if (!IsStepCliInstalled)
        {
            return new CertificateResult
            {
                Success = false,
                Error = "Step CLI not installed"
            };
        }

        var certPath = GetCertificatePath(commonName);
        var keyPath = GetKeyPath(commonName);

        var args = $"certificate create \"{commonName}\" \"{certPath}\" \"{keyPath}\" " +
                  $"--profile leaf --not-after {validDays * 24}h --no-password --insecure --force";

        _logger.LogInformation("Generating self-signed certificate for: {CommonName}", commonName);

        try
        {
            var result = await RunStepCommandAsync(args, cancellationToken);

            return new CertificateResult
            {
                Success = result.ExitCode == 0,
                CertificatePath = certPath,
                KeyPath = keyPath,
                CommonName = commonName,
                Error = result.ExitCode != 0 ? result.Error : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate self-signed certificate");
            return new CertificateResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get certificate info from a PEM file
    /// </summary>
    public async Task<CertificateInfo?> InspectCertificateAsync(
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        var certPath = path ?? GetCertificatePath();
        
        if (!File.Exists(certPath))
            return null;

        try
        {
            var result = await RunStepCommandAsync(
                $"certificate inspect \"{certPath}\"",
                cancellationToken);

            if (result.ExitCode != 0)
                return null;

            // Parse output
            var cert = LoadCertificate(certPath);
            if (cert == null)
                return null;

            return new CertificateInfo
            {
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                NotBefore = cert.NotBefore,
                NotAfter = cert.NotAfter,
                SerialNumber = cert.SerialNumber,
                Thumbprint = cert.Thumbprint,
                SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect certificate");
            return null;
        }
    }

    private string GetCertificatePath(string? name = null)
    {
        var certName = name ?? _config.ApplicationData?.Certificates?.DefaultName ?? "server";
        return Path.Combine(_certBasePath, $"{certName}.crt");
    }

    private string GetKeyPath(string? name = null)
    {
        var certName = name ?? _config.ApplicationData?.Certificates?.DefaultName ?? "server";
        return Path.Combine(_certBasePath, $"{certName}.key");
    }

    private X509Certificate2? LoadCertificate(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return X509CertificateLoader.LoadCertificateFromFile(path);
        }
        catch
        {
            return null;
        }
    }

    private string FindStepCliPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var paths = new[]
            {
                @"C:\Program Files\Smallstep\step\bin\step.exe",
                @"C:\Program Files (x86)\Smallstep\step\bin\step.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "Smallstep", "step", "bin", "step.exe")
            };
            return paths.FirstOrDefault(File.Exists) ?? "step";
        }

        // Unix-like systems
        var unixPaths = new[] { "/usr/bin/step", "/usr/local/bin/step", "/opt/step/bin/step" };
        return unixPaths.FirstOrDefault(File.Exists) ?? "step";
    }

    private async Task<(int ExitCode, string Output, string Error)> RunStepCommandAsync(
        string args, 
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _stepCliPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, "", "Failed to start step CLI");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, output, error);
    }

    private async Task<bool> InstallStepCliWindowsAsync(CancellationToken cancellationToken)
    {
        // Try winget first
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install Smallstep.step --silent",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0)
                    return true;
            }
        }
        catch
        {
            _logger.LogWarning("winget not available, trying manual install");
        }

        // Manual download fallback
        const string downloadUrl = "https://dl.smallstep.com/gh-release/cli/gh-release-header/v0.26.0/step_windows_0.26.0_amd64.zip";
        var tempFile = Path.Combine(Path.GetTempPath(), "step.zip");
        var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Smallstep", "step", "bin");

        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        await using var fs = File.Create(tempFile);
        await response.Content.CopyToAsync(fs, cancellationToken);
        fs.Close();

        // Extract
        if (!Directory.Exists(installPath))
            Directory.CreateDirectory(installPath);

        System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, installPath, true);
        File.Delete(tempFile);

        return File.Exists(Path.Combine(installPath, "step.exe"));
    }

    private async Task<bool> InstallStepCliLinuxAsync(CancellationToken cancellationToken)
    {
        // Try package manager
        var result = await RunShellCommandAsync(
            "curl -sSfL https://dl.smallstep.com/cli/install.sh | sh",
            cancellationToken);
        
        return result.ExitCode == 0;
    }

    private async Task<bool> InstallStepCliMacAsync(CancellationToken cancellationToken)
    {
        var result = await RunShellCommandAsync("brew install step", cancellationToken);
        return result.ExitCode == 0;
    }

    private async Task<(int ExitCode, string Output)> RunShellCommandAsync(
        string command, 
        CancellationToken cancellationToken)
    {
        var shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
        var shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"{shellArg} \"{command}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, output);
    }
}

// DTOs

public class CertificateStatus
{
    public bool IsStepCliInstalled { get; set; }
    public string CertificatePath { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    public bool HasCertificate { get; set; }
    public bool HasKey { get; set; }
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public string? Thumbprint { get; set; }
    public bool IsExpired { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public string? Error { get; set; }
}

public class BootstrapResult
{
    public bool Success { get; set; }
    public string? CaUrl { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

public class CertificateResult
{
    public bool Success { get; set; }
    public string? CertificatePath { get; set; }
    public string? KeyPath { get; set; }
    public string? CommonName { get; set; }
    public string? Error { get; set; }
}

public class CertificateInfo
{
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string? SignatureAlgorithm { get; set; }
}
