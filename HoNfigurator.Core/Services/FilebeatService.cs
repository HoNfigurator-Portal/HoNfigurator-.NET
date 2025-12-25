using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages Filebeat installation, configuration, and monitoring for log shipping.
/// Port of Python HoNfigurator-Central Filebeat integration.
/// </summary>
public class FilebeatService
{
    private readonly ILogger<FilebeatService> _logger;
    private readonly HoNConfiguration _config;
    private readonly string _filebeatPath;
    private readonly string _filebeatConfigPath;
    private Process? _filebeatProcess;
    private bool _isRunning;

    // Filebeat download URLs by platform
    private const string FilebeatWindowsUrl = "https://artifacts.elastic.co/downloads/beats/filebeat/filebeat-8.11.0-windows-x86_64.zip";
    private const string FilebeatLinuxUrl = "https://artifacts.elastic.co/downloads/beats/filebeat/filebeat-8.11.0-linux-x86_64.tar.gz";

    public bool IsInstalled => File.Exists(FilebeatExecutablePath);
    public bool IsRunning => _isRunning;
    public string FilebeatExecutablePath => Path.Combine(_filebeatPath, 
        OperatingSystem.IsWindows() ? "filebeat.exe" : "filebeat");

    public FilebeatService(ILogger<FilebeatService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        // Default paths
        var basePath = config.ApplicationData?.Filebeat?.InstallPath 
            ?? Path.Combine(AppContext.BaseDirectory, "filebeat");
        _filebeatPath = basePath;
        _filebeatConfigPath = Path.Combine(_filebeatPath, "filebeat.yml");
        
        // Ensure directory exists
        if (!Directory.Exists(_filebeatPath))
            Directory.CreateDirectory(_filebeatPath);
    }

    /// <summary>
    /// Check if Filebeat is installed and running
    /// </summary>
    public FilebeatStatus GetStatus()
    {
        return new FilebeatStatus
        {
            IsInstalled = IsInstalled,
            IsRunning = _isRunning,
            Version = GetInstalledVersion(),
            ConfigPath = _filebeatConfigPath,
            InstallPath = _filebeatPath,
            ElasticsearchHost = _config.ApplicationData?.Filebeat?.ElasticsearchHost,
            LogPaths = _config.ApplicationData?.Filebeat?.LogPaths ?? new List<string>()
        };
    }

    /// <summary>
    /// Install Filebeat automatically
    /// </summary>
    public async Task<FilebeatInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return new FilebeatInstallResult
            {
                Success = true,
                Message = "Filebeat is already installed",
                Version = GetInstalledVersion()
            };
        }

        _logger.LogInformation("Installing Filebeat...");

        try
        {
            var downloadUrl = OperatingSystem.IsWindows() ? FilebeatWindowsUrl : FilebeatLinuxUrl;
            var tempFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(downloadUrl));

            // Download Filebeat
            _logger.LogInformation("Downloading Filebeat from {Url}", downloadUrl);
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(10);
                var response = await httpClient.GetAsync(downloadUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = File.Create(tempFile);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // Extract archive
            _logger.LogInformation("Extracting Filebeat to {Path}", _filebeatPath);
            await ExtractArchiveAsync(tempFile, _filebeatPath, cancellationToken);

            // Clean up temp file
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            // Generate default config
            await GenerateConfigAsync();

            _logger.LogInformation("Filebeat installed successfully");

            return new FilebeatInstallResult
            {
                Success = true,
                Message = "Filebeat installed successfully",
                Version = GetInstalledVersion()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Filebeat");
            return new FilebeatInstallResult
            {
                Success = false,
                Message = $"Installation failed: {ex.Message}",
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Generate Filebeat configuration file
    /// </summary>
    public async Task GenerateConfigAsync()
    {
        var filebeatConfig = _config.ApplicationData?.Filebeat;
        var honPath = _config.HonData?.HonInstallDirectory ?? "";
        
        // Default log paths for HoN servers
        var logPaths = filebeatConfig?.LogPaths?.Count > 0 
            ? filebeatConfig.LogPaths 
            : new List<string>
            {
                Path.Combine(honPath, "game", "logs", "*.log"),
                Path.Combine(honPath, "game", "logs", "server_*.log"),
                Path.Combine(AppContext.BaseDirectory, "logs", "*.log")
            };

        var config = $@"# Filebeat configuration generated by HoNfigurator
# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

filebeat.inputs:
- type: log
  enabled: true
  paths:
{string.Join("\n", logPaths.Select(p => $"    - {p}"))}
  
  # Multiline for stack traces
  multiline.pattern: '^\\['
  multiline.negate: true
  multiline.match: after

  # Add fields
  fields:
    server_name: ""{_config.HonData?.ServerName ?? "HoNfigurator"}"" 
    environment: ""{filebeatConfig.Environment ?? "production"}""
  fields_under_root: true

# Elasticsearch output
output.elasticsearch:
  hosts: [""{filebeatConfig.ElasticsearchHost ?? "localhost:9200"}""]
  index: ""honfigurator-logs-%{{+yyyy.MM.dd}}""
{(string.IsNullOrEmpty(filebeatConfig.ElasticsearchUsername) ? "" : $@"  username: ""{filebeatConfig.ElasticsearchUsername}""
  password: ""{filebeatConfig.ElasticsearchPassword}""")}

# Index template settings
setup.template.name: ""honfigurator-logs""
setup.template.pattern: ""honfigurator-logs-*""
setup.template.enabled: true
setup.template.settings:
  index.number_of_shards: 1
  index.number_of_replicas: 0

# Logging
logging.level: info
logging.to_files: true
logging.files:
  path: {Path.Combine(_filebeatPath, "logs")}
  name: filebeat
  keepfiles: 7
  permissions: 0644

# Processors
processors:
  - add_host_metadata: ~
  - add_cloud_metadata: ~
  - timestamp:
      field: ""@timestamp""
      layouts:
        - ""2006-01-02T15:04:05.000Z""
        - ""2006-01-02 15:04:05""
";

        await File.WriteAllTextAsync(_filebeatConfigPath, config);
        _logger.LogInformation("Filebeat configuration generated at {Path}", _filebeatConfigPath);
    }

    /// <summary>
    /// Start Filebeat service
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (!IsInstalled)
        {
            _logger.LogWarning("Cannot start Filebeat - not installed");
            return false;
        }

        if (_isRunning)
        {
            _logger.LogDebug("Filebeat is already running");
            return true;
        }

        try
        {
            // Ensure config exists
            if (!File.Exists(_filebeatConfigPath))
                await GenerateConfigAsync();

            var startInfo = new ProcessStartInfo
            {
                FileName = FilebeatExecutablePath,
                Arguments = $"-c \"{_filebeatConfigPath}\"",
                WorkingDirectory = _filebeatPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _filebeatProcess = Process.Start(startInfo);
            
            if (_filebeatProcess != null)
            {
                _isRunning = true;
                _filebeatProcess.EnableRaisingEvents = true;
                _filebeatProcess.Exited += (s, e) =>
                {
                    _isRunning = false;
                    _logger.LogWarning("Filebeat process exited");
                };

                _logger.LogInformation("Filebeat started with PID {Pid}", _filebeatProcess.Id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Filebeat");
            return false;
        }
    }

    /// <summary>
    /// Stop Filebeat service
    /// </summary>
    public async Task<bool> StopAsync()
    {
        if (_filebeatProcess == null || !_isRunning)
        {
            _logger.LogDebug("Filebeat is not running");
            return true;
        }

        try
        {
            _filebeatProcess.Kill(entireProcessTree: true);
            await _filebeatProcess.WaitForExitAsync();
            _isRunning = false;
            _filebeatProcess.Dispose();
            _filebeatProcess = null;

            _logger.LogInformation("Filebeat stopped");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop Filebeat");
            return false;
        }
    }

    /// <summary>
    /// Restart Filebeat service
    /// </summary>
    public async Task<bool> RestartAsync()
    {
        await StopAsync();
        await Task.Delay(1000);
        return await StartAsync();
    }

    /// <summary>
    /// Test Elasticsearch connection
    /// </summary>
    public async Task<ElasticsearchTestResult> TestElasticsearchConnectionAsync()
    {
        var host = _config.ApplicationData?.Filebeat?.ElasticsearchHost ?? "localhost:9200";
        
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.GetAsync($"http://{host}");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new ElasticsearchTestResult
                {
                    Success = true,
                    Host = host,
                    Message = "Connected to Elasticsearch",
                    ClusterInfo = content
                };
            }

            return new ElasticsearchTestResult
            {
                Success = false,
                Host = host,
                Message = $"Elasticsearch returned: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new ElasticsearchTestResult
            {
                Success = false,
                Host = host,
                Message = $"Connection failed: {ex.Message}",
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get installed Filebeat version
    /// </summary>
    private string? GetInstalledVersion()
    {
        if (!IsInstalled) return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = FilebeatExecutablePath,
                Arguments = "version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Parse version from output like "filebeat version 8.11.0 (amd64)..."
            var match = System.Text.RegularExpressions.Regex.Match(output, @"version (\d+\.\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract archive (zip or tar.gz)
    /// </summary>
    private async Task ExtractArchiveAsync(string archivePath, string destPath, CancellationToken cancellationToken)
    {
        if (archivePath.EndsWith(".zip"))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destPath, overwriteFiles: true);
            
            // Move contents from nested folder if exists
            var nestedDir = Directory.GetDirectories(destPath).FirstOrDefault();
            if (nestedDir != null && Directory.GetFiles(destPath).Length == 0)
            {
                foreach (var file in Directory.GetFiles(nestedDir))
                {
                    var destFile = Path.Combine(destPath, Path.GetFileName(file));
                    File.Move(file, destFile, overwrite: true);
                }
                foreach (var dir in Directory.GetDirectories(nestedDir))
                {
                    var destDir = Path.Combine(destPath, Path.GetFileName(dir));
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, recursive: true);
                    Directory.Move(dir, destDir);
                }
                Directory.Delete(nestedDir, recursive: true);
            }
        }
        else if (archivePath.EndsWith(".tar.gz") || archivePath.EndsWith(".tgz"))
        {
            // For Linux, use tar command
            var startInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destPath}\" --strip-components=1",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
    }
}

#region Models

public class FilebeatStatus
{
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string? Version { get; set; }
    public string? ConfigPath { get; set; }
    public string? InstallPath { get; set; }
    public string? ElasticsearchHost { get; set; }
    public List<string> LogPaths { get; set; } = new();
}

public class FilebeatInstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Error { get; set; }
}

public class ElasticsearchTestResult
{
    public bool Success { get; set; }
    public string Host { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ClusterInfo { get; set; }
    public string? Error { get; set; }
}

public class filebeatConfig
{
    public string? InstallPath { get; set; }
    public string? ElasticsearchHost { get; set; } = "localhost:9200";
    public string? ElasticsearchUsername { get; set; }
    public string? ElasticsearchPassword { get; set; }
    public string? Environment { get; set; } = "production";
    public List<string> LogPaths { get; set; } = new();
    public bool AutoStart { get; set; } = false;
}

#endregion
