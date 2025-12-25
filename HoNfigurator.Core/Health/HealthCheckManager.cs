using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Health;

/// <summary>
/// Health check result for a service or component
/// </summary>
public record HealthCheckResult
{
    public string Name { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
    public TimeSpan ResponseTime { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object> Data { get; init; } = new();
}

/// <summary>
/// IP validation result
/// </summary>
public record IpValidationResult
{
    public bool IsValid { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public string? ExternalIp { get; init; }
    public bool IsExternal { get; init; }
    public bool IsReachable { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Update check result
/// </summary>
public record UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string? CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseNotes { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// System resources information
/// </summary>
public record SystemResources
{
    public double CpuUsagePercent { get; init; }
    public long MemoryUsedMb { get; init; }
    public long MemoryTotalMb { get; init; }
    public double MemoryUsagePercent { get; init; }
    public long DiskUsedGb { get; init; }
    public long DiskTotalGb { get; init; }
    public double DiskUsagePercent { get; init; }
    public TimeSpan Uptime { get; init; }
}

/// <summary>
/// Manages health checks for the HoNfigurator system
/// </summary>
public class HealthCheckManager
{
    private readonly ILogger<HealthCheckManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly HoNConfiguration _config;
    private readonly Dictionary<string, HealthCheckResult> _lastResults = new();
    private DateTime _startTime = DateTime.UtcNow;

    public HealthCheckManager(ILogger<HealthCheckManager> logger, HoNConfiguration config, HttpClient? httpClient = null)
    {
        _logger = logger;
        _config = config;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Run all health checks
    /// </summary>
    public async Task<Dictionary<string, HealthCheckResult>> RunAllChecksAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, HealthCheckResult>();

        // Run checks in parallel
        var tasks = new List<Task<HealthCheckResult>>
        {
            CheckMasterServerAsync(cancellationToken),
            CheckChatServerAsync(cancellationToken),
            CheckDatabaseAsync(cancellationToken),
            CheckDiskSpaceAsync(cancellationToken),
            CheckNetworkAsync(cancellationToken)
        };

        var completedChecks = await Task.WhenAll(tasks);

        foreach (var result in completedChecks)
        {
            results[result.Name] = result;
            _lastResults[result.Name] = result;
        }

        return results;
    }

    /// <summary>
    /// Check Master Server connectivity - uses URL from config
    /// </summary>
    public async Task<HealthCheckResult> CheckMasterServerAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Use MasterServer URL from config
            var masterServerUrl = _config.MasterServerUrl;
            if (string.IsNullOrEmpty(masterServerUrl))
            {
                masterServerUrl = "http://api.kongor.net";
            }

            var response = await _httpClient.GetAsync(masterServerUrl, cancellationToken);
            sw.Stop();

            return new HealthCheckResult
            {
                Name = "MasterServer",
                IsHealthy = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound,
                Status = response.IsSuccessStatusCode ? "Connected" : "Reachable",
                Message = $"URL: {masterServerUrl}",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult
            {
                Name = "MasterServer",
                IsHealthy = false,
                Status = "Unreachable",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// Check Chat Server connectivity - uses URL from config
    /// </summary>
    public async Task<HealthCheckResult> CheckChatServerAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Use ChatServer from config (same as MasterServer domain for Kongor)
            var masterServer = _config.HonData?.MasterServer ?? "api.kongor.net";
            var chatServerHost = masterServer.Replace("http://", "").Replace("https://", "").Split('/')[0];
            
            // For Kongor, chat server is typically on chat subdomain
            if (chatServerHost.StartsWith("api."))
            {
                chatServerHost = chatServerHost.Replace("api.", "chat.");
            }

            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(chatServerHost, 11031);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await Task.WhenAny(connectTask, timeoutTask);
            
            if (!connectTask.IsCompleted)
                throw new TimeoutException("Connection timed out");
            
            await connectTask; // Rethrow any exception
            sw.Stop();

            return new HealthCheckResult
            {
                Name = "ChatServer",
                IsHealthy = true,
                Status = "Connected",
                Message = $"Host: {chatServerHost}",
                ResponseTime = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult
            {
                Name = "ChatServer",
                IsHealthy = false,
                Status = "Unreachable",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// Check database connectivity (placeholder)
    /// </summary>
    public Task<HealthCheckResult> CheckDatabaseAsync(CancellationToken cancellationToken = default)
    {
        // Placeholder - would check actual database connection
        return Task.FromResult(new HealthCheckResult
        {
            Name = "Database",
            IsHealthy = true,
            Status = "Connected",
            Message = "In-memory storage active"
        });
    }

    /// <summary>
    /// Check available disk space
    /// </summary>
    public Task<HealthCheckResult> CheckDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
            if (drive == null)
            {
                return Task.FromResult(new HealthCheckResult
                {
                    Name = "DiskSpace",
                    IsHealthy = false,
                    Status = "No Drive",
                    Message = "No fixed drives found"
                });
            }

            var freeGb = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
            var totalGb = drive.TotalSize / 1024 / 1024 / 1024;
            var usagePercent = (double)(totalGb - freeGb) / totalGb * 100;

            var isHealthy = freeGb > 5; // At least 5GB free

            return Task.FromResult(new HealthCheckResult
            {
                Name = "DiskSpace",
                IsHealthy = isHealthy,
                Status = isHealthy ? "OK" : "Low",
                Message = isHealthy ? null : $"Only {freeGb}GB free",
                Data = new Dictionary<string, object>
                {
                    ["FreeGB"] = freeGb,
                    ["TotalGB"] = totalGb,
                    ["UsagePercent"] = Math.Round(usagePercent, 1)
                }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult
            {
                Name = "DiskSpace",
                IsHealthy = false,
                Status = "Error",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Check network connectivity
    /// </summary>
    public async Task<HealthCheckResult> CheckNetworkAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 5000);
            sw.Stop();

            var isHealthy = reply.Status == IPStatus.Success;

            return new HealthCheckResult
            {
                Name = "Network",
                IsHealthy = isHealthy,
                Status = isHealthy ? "Connected" : "Disconnected",
                Message = isHealthy ? null : reply.Status.ToString(),
                ResponseTime = isHealthy ? TimeSpan.FromMilliseconds(reply.RoundtripTime) : sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["PingMs"] = reply.RoundtripTime
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult
            {
                Name = "Network",
                IsHealthy = false,
                Status = "Error",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// Validate an IP address
    /// </summary>
    public async Task<IpValidationResult> ValidateIpAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip))
        {
            return new IpValidationResult
            {
                IsValid = false,
                IpAddress = ipAddress,
                Error = "Invalid IP address format"
            };
        }

        var isExternal = !IsPrivateIp(ip);
        var isReachable = await CheckIpReachableAsync(ipAddress, cancellationToken);

        string? externalIp = null;
        if (!isExternal)
        {
            externalIp = await GetExternalIpAsync(cancellationToken);
        }

        return new IpValidationResult
        {
            IsValid = true,
            IpAddress = ipAddress,
            ExternalIp = externalIp,
            IsExternal = isExternal,
            IsReachable = isReachable
        };
    }

    /// <summary>
    /// Get the external IP address
    /// </summary>
    public async Task<string?> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetStringAsync("https://api.ipify.org", cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get external IP");
            return null;
        }
    }

    /// <summary>
    /// Check for updates
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        // Placeholder - would check actual update server
        return new UpdateCheckResult
        {
            UpdateAvailable = false,
            CurrentVersion = currentVersion,
            LatestVersion = currentVersion
        };
    }

    /// <summary>
    /// Get system resource usage
    /// </summary>
    public SystemResources GetSystemResources()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();

        // Memory info
        var memoryUsedMb = process.WorkingSet64 / 1024 / 1024;
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024;

        // Disk info
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
        var diskUsedGb = drive != null ? (drive.TotalSize - drive.AvailableFreeSpace) / 1024 / 1024 / 1024 : 0;
        var diskTotalGb = drive?.TotalSize / 1024 / 1024 / 1024 ?? 0;

        return new SystemResources
        {
            CpuUsagePercent = 0, // CPU usage requires sampling over time
            MemoryUsedMb = memoryUsedMb,
            MemoryTotalMb = totalMemory,
            MemoryUsagePercent = totalMemory > 0 ? (double)memoryUsedMb / totalMemory * 100 : 0,
            DiskUsedGb = diskUsedGb,
            DiskTotalGb = diskTotalGb,
            DiskUsagePercent = diskTotalGb > 0 ? (double)diskUsedGb / diskTotalGb * 100 : 0,
            Uptime = DateTime.UtcNow - _startTime
        };
    }

    /// <summary>
    /// Get last health check results
    /// </summary>
    public Dictionary<string, HealthCheckResult> GetLastResults() => new(_lastResults);

    /// <summary>
    /// Get overall health status
    /// </summary>
    public bool IsHealthy() => _lastResults.Count == 0 || _lastResults.Values.All(r => r.IsHealthy);

    /// <summary>
    /// Check HoN installation health
    /// Port of Python's healthcheck_manager.py installation checks
    /// </summary>
    public Task<HealthCheckResult> CheckHoNInstallationAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var issues = new List<string>();

        try
        {
            var installDir = _config.HonData?.HonInstallDirectory;
            
            if (string.IsNullOrEmpty(installDir))
            {
                return Task.FromResult(new HealthCheckResult
                {
                    Name = "HoNInstallation",
                    IsHealthy = false,
                    Status = "NotConfigured",
                    Message = "HoN install directory not configured"
                });
            }

            if (!Directory.Exists(installDir))
            {
                return Task.FromResult(new HealthCheckResult
                {
                    Name = "HoNInstallation",
                    IsHealthy = false,
                    Status = "NotFound",
                    Message = $"Install directory not found: {installDir}"
                });
            }

            // Check for required files
            var requiredFiles = OperatingSystem.IsWindows()
                ? new[] { "hon_x64.exe", "k2_x64.exe" }
                : new[] { "hon_x64-server", "hon_x64" };

            var foundExe = false;
            foreach (var file in requiredFiles)
            {
                if (File.Exists(Path.Combine(installDir, file)))
                {
                    foundExe = true;
                    break;
                }
            }

            if (!foundExe)
            {
                issues.Add("No HoN executable found");
            }

            // Check for game data directories
            var requiredDirs = new[] { "game", "base" };
            foreach (var dir in requiredDirs)
            {
                if (!Directory.Exists(Path.Combine(installDir, dir)))
                {
                    issues.Add($"Missing directory: {dir}");
                }
            }

            // Check disk space in install directory
            var driveInfo = new DriveInfo(Path.GetPathRoot(installDir) ?? "C:");
            var freeGb = driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024;
            if (freeGb < 2)
            {
                issues.Add($"Low disk space: {freeGb}GB free");
            }

            sw.Stop();

            var isHealthy = !issues.Any();
            return Task.FromResult(new HealthCheckResult
            {
                Name = "HoNInstallation",
                IsHealthy = isHealthy,
                Status = isHealthy ? "OK" : "Issues",
                Message = isHealthy ? $"Install directory: {installDir}" : string.Join("; ", issues),
                ResponseTime = sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["InstallDirectory"] = installDir,
                    ["DiskFreeGB"] = freeGb,
                    ["Issues"] = issues
                }
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new HealthCheckResult
            {
                Name = "HoNInstallation",
                IsHealthy = false,
                Status = "Error",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            });
        }
    }

    /// <summary>
    /// Check server lag/latency to master server
    /// Port of Python's lag detection
    /// </summary>
    public async Task<HealthCheckResult> CheckLagAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pingResults = new List<long>();

        try
        {
            var masterServer = _config.HonData?.MasterServer ?? "api.kongor.net";
            var host = masterServer.Replace("http://", "").Replace("https://", "").Split('/')[0];

            using var ping = new Ping();
            
            // Multiple ping samples like Python version
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(host, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        pingResults.Add(reply.RoundtripTime);
                    }
                }
                catch { }
                
                if (i < 4) await Task.Delay(200, cancellationToken);
            }

            sw.Stop();

            if (!pingResults.Any())
            {
                return new HealthCheckResult
                {
                    Name = "Lag",
                    IsHealthy = false,
                    Status = "Timeout",
                    Message = $"Could not reach {host}",
                    ResponseTime = sw.Elapsed
                };
            }

            var avgPing = pingResults.Average();
            var maxPing = pingResults.Max();
            var minPing = pingResults.Min();
            var jitter = maxPing - minPing;

            // Thresholds from Python version
            var isHealthy = avgPing < 150 && jitter < 50;
            var status = avgPing < 50 ? "Excellent" :
                         avgPing < 100 ? "Good" :
                         avgPing < 150 ? "Fair" : "Poor";

            return new HealthCheckResult
            {
                Name = "Lag",
                IsHealthy = isHealthy,
                Status = status,
                Message = $"Avg: {avgPing:F0}ms, Jitter: {jitter:F0}ms",
                ResponseTime = sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["AveragePingMs"] = Math.Round(avgPing, 1),
                    ["MinPingMs"] = minPing,
                    ["MaxPingMs"] = maxPing,
                    ["JitterMs"] = jitter,
                    ["Samples"] = pingResults.Count,
                    ["Host"] = host
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult
            {
                Name = "Lag",
                IsHealthy = false,
                Status = "Error",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// Check patch/version status
    /// </summary>
    public async Task<HealthCheckResult> CheckPatchStatusAsync(CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var currentVersion = _config.HonData?.ManVersion ?? "0.0.0.0";
            var patchServer = _config.HonData?.PatchServer ?? "api.kongor.net/patch";
            var url = $"http://{patchServer}/patcher/patcher.php";

            var formData = new Dictionary<string, string>
            {
                ["version"] = currentVersion,
                ["os"] = OperatingSystem.IsWindows() ? "wac" : "lac",
                ["arch"] = "x86_64"
            };

            var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            sw.Stop();

            // Parse response for version info
            var latestMatch = System.Text.RegularExpressions.Regex.Match(
                responseText, @"latest_version.*?""([^""]+)""");
            var latestVersion = latestMatch.Success ? latestMatch.Groups[1].Value : currentVersion;

            var updateAvailable = latestVersion != currentVersion;

            return new HealthCheckResult
            {
                Name = "PatchStatus",
                IsHealthy = true,
                Status = updateAvailable ? "UpdateAvailable" : "UpToDate",
                Message = updateAvailable 
                    ? $"Update available: {currentVersion} → {latestVersion}"
                    : $"Version: {currentVersion}",
                ResponseTime = sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["CurrentVersion"] = currentVersion,
                    ["LatestVersion"] = latestVersion,
                    ["UpdateAvailable"] = updateAvailable
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult
            {
                Name = "PatchStatus",
                IsHealthy = true, // Not critical if patch check fails
                Status = "Unknown",
                Message = $"Could not check: {ex.Message}",
                ResponseTime = sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["CurrentVersion"] = _config.HonData?.ManVersion ?? "Unknown"
                }
            };
        }
    }

    /// <summary>
    /// Check port availability
    /// </summary>
    public Task<HealthCheckResult> CheckPortAvailabilityAsync(int port, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var isAvailable = true;
            string? usedBy = null;

            try
            {
                using var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
            }
            catch (SocketException)
            {
                isAvailable = false;
                
                // Try to find what's using the port
                try
                {
                    var properties = IPGlobalProperties.GetIPGlobalProperties();
                    var tcpConnections = properties.GetActiveTcpListeners();
                    if (tcpConnections.Any(c => c.Port == port))
                    {
                        usedBy = "TCP listener";
                    }
                }
                catch { }
            }

            sw.Stop();

            return Task.FromResult(new HealthCheckResult
            {
                Name = $"Port{port}",
                IsHealthy = isAvailable,
                Status = isAvailable ? "Available" : "InUse",
                Message = isAvailable ? null : $"Port {port} is in use" + (usedBy != null ? $" by {usedBy}" : ""),
                ResponseTime = sw.Elapsed,
                Data = new Dictionary<string, object>
                {
                    ["Port"] = port,
                    ["Available"] = isAvailable
                }
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Task.FromResult(new HealthCheckResult
            {
                Name = $"Port{port}",
                IsHealthy = false,
                Status = "Error",
                Message = ex.Message,
                ResponseTime = sw.Elapsed
            });
        }
    }

    /// <summary>
    /// Run enhanced health checks including all Python version checks
    /// </summary>
    public async Task<Dictionary<string, HealthCheckResult>> RunEnhancedChecksAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, HealthCheckResult>();

        var tasks = new List<Task<HealthCheckResult>>
        {
            CheckMasterServerAsync(cancellationToken),
            CheckChatServerAsync(cancellationToken),
            CheckNetworkAsync(cancellationToken),
            CheckDiskSpaceAsync(cancellationToken),
            CheckHoNInstallationAsync(cancellationToken),
            CheckLagAsync(cancellationToken),
            CheckPatchStatusAsync(cancellationToken)
        };

        // Add port checks for configured game ports
        var startPort = _config.HonData?.StartingGamePort ?? 11235;
        var maxInstances = _config.HonData?.TotalServers ?? 5;
        for (int i = 0; i < Math.Min(maxInstances, 3); i++) // Check first 3 ports
        {
            var port = startPort + i;
            tasks.Add(CheckPortAvailabilityAsync(port, cancellationToken));
        }

        var completedChecks = await Task.WhenAll(tasks);

        foreach (var result in completedChecks)
        {
            results[result.Name] = result;
            _lastResults[result.Name] = result;
        }

        return results;
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        // 10.x.x.x
        if (bytes[0] == 10) return true;

        // 172.16.x.x - 172.31.x.x
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

        // 192.168.x.x
        if (bytes[0] == 192 && bytes[1] == 168) return true;

        // 127.x.x.x (loopback)
        if (bytes[0] == 127) return true;

        return false;
    }

    private async Task<bool> CheckIpReachableAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 3000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
