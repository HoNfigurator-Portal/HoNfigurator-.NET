using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Monitors disk utilization and sends alerts when thresholds are exceeded.
/// Port of Python HoNfigurator-Central disk monitoring functionality.
/// </summary>
public class DiskMonitorService
{
    private readonly ILogger<DiskMonitorService> _logger;
    private readonly HoNConfiguration _config;
    private readonly List<DiskAlert> _alertHistory = new();
    private readonly object _alertLock = new();
    private DateTime? _lastAlertTime;

    // Default thresholds
    private const int DefaultWarningThreshold = 80;
    private const int DefaultCriticalThreshold = 95;
    private const int AlertCooldownMinutes = 15;

    public DiskMonitorService(ILogger<DiskMonitorService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Get current disk utilization for all monitored paths
    /// </summary>
    public DiskUtilizationReport GetUtilization()
    {
        var report = new DiskUtilizationReport
        {
            Timestamp = DateTime.UtcNow,
            Drives = new List<DriveUtilization>()
        };

        // Check all available drives
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;

                var utilization = new DriveUtilization
                {
                    Name = drive.Name,
                    Label = drive.VolumeLabel,
                    DriveType = drive.DriveType.ToString(),
                    FileSystem = drive.DriveFormat,
                    TotalSizeBytes = drive.TotalSize,
                    FreeSizeBytes = drive.AvailableFreeSpace,
                    UsedSizeBytes = drive.TotalSize - drive.AvailableFreeSpace,
                    UsedPercentage = CalculatePercentage(drive.TotalSize, drive.AvailableFreeSpace)
                };

                utilization.AlertLevel = DetermineAlertLevel(utilization.UsedPercentage);
                report.Drives.Add(utilization);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get drive info for {Drive}", drive.Name);
            }
        }

        // Add monitored paths
        report.MonitoredPaths = GetMonitoredPathStats();
        
        // Determine overall status
        report.OverallStatus = report.Drives.Any(d => d.AlertLevel == AlertLevel.Critical)
            ? AlertLevel.Critical
            : report.Drives.Any(d => d.AlertLevel == AlertLevel.Warning)
                ? AlertLevel.Warning
                : AlertLevel.Normal;

        return report;
    }

    /// <summary>
    /// Check disk utilization and generate alerts if needed
    /// </summary>
    public DiskCheckResult CheckAndAlert()
    {
        var report = GetUtilization();
        var alerts = new List<DiskAlert>();

        var warningThreshold = _config.ApplicationData?.DiskMonitoring?.WarningThreshold ?? DefaultWarningThreshold;
        var criticalThreshold = _config.ApplicationData?.DiskMonitoring?.CriticalThreshold ?? DefaultCriticalThreshold;

        foreach (var drive in report.Drives)
        {
            if (drive.UsedPercentage >= criticalThreshold)
            {
                alerts.Add(CreateAlert(drive, AlertLevel.Critical));
            }
            else if (drive.UsedPercentage >= warningThreshold)
            {
                alerts.Add(CreateAlert(drive, AlertLevel.Warning));
            }
        }

        // Check monitored paths for size thresholds
        foreach (var path in report.MonitoredPaths)
        {
            if (path.SizeBytes > path.MaxSizeBytes && path.MaxSizeBytes > 0)
            {
                alerts.Add(new DiskAlert
                {
                    Timestamp = DateTime.UtcNow,
                    Level = AlertLevel.Warning,
                    Drive = path.Path,
                    Message = $"Path '{path.Path}' exceeds max size: {FormatBytes(path.SizeBytes)} > {FormatBytes(path.MaxSizeBytes)}",
                    UsedPercentage = 0,
                    FreeSizeBytes = 0
                });
            }
        }

        // Store alerts
        if (alerts.Any())
        {
            lock (_alertLock)
            {
                // Check cooldown
                if (_lastAlertTime.HasValue && 
                    (DateTime.UtcNow - _lastAlertTime.Value).TotalMinutes < AlertCooldownMinutes)
                {
                    _logger.LogDebug("Skipping alerts due to cooldown");
                }
                else
                {
                    _alertHistory.AddRange(alerts);
                    _lastAlertTime = DateTime.UtcNow;

                    foreach (var alert in alerts)
                    {
                        if (alert.Level == AlertLevel.Critical)
                            _logger.LogError("CRITICAL: {Message}", alert.Message);
                        else
                            _logger.LogWarning("WARNING: {Message}", alert.Message);
                    }
                }

                // Trim old alerts (keep last 100)
                while (_alertHistory.Count > 100)
                {
                    _alertHistory.RemoveAt(0);
                }
            }
        }

        return new DiskCheckResult
        {
            Report = report,
            NewAlerts = alerts,
            AlertsGenerated = alerts.Count > 0
        };
    }

    /// <summary>
    /// Get utilization for a specific path
    /// </summary>
    public PathUtilization? GetPathUtilization(string path)
    {
        if (!Directory.Exists(path))
            return null;

        try
        {
            var dirInfo = new DirectoryInfo(path);
            var sizeBytes = CalculateDirectorySize(dirInfo);
            var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);

            return new PathUtilization
            {
                Path = path,
                SizeBytes = sizeBytes,
                FileCount = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length,
                DriveFreeSizeBytes = drive.AvailableFreeSpace,
                DriveTotalSizeBytes = drive.TotalSize,
                LastModified = dirInfo.LastWriteTimeUtc
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get path utilization for {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Get recent alerts
    /// </summary>
    public List<DiskAlert> GetRecentAlerts(int count = 20)
    {
        lock (_alertLock)
        {
            return _alertHistory
                .OrderByDescending(a => a.Timestamp)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Clear alert history
    /// </summary>
    public void ClearAlertHistory()
    {
        lock (_alertLock)
        {
            _alertHistory.Clear();
            _lastAlertTime = null;
        }
    }

    /// <summary>
    /// Estimate time until disk full based on recent usage patterns
    /// </summary>
    public TimeSpan? EstimateTimeUntilFull(string driveName)
    {
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => 
            d.IsReady && d.Name.Equals(driveName, StringComparison.OrdinalIgnoreCase));

        if (drive == null)
            return null;

        // This would need historical data to calculate accurately
        // For now, return null - would be enhanced with time-series data
        return null;
    }

    private List<MonitoredPathStats> GetMonitoredPathStats()
    {
        var result = new List<MonitoredPathStats>();
        var paths = _config.ApplicationData?.DiskMonitoring?.MonitoredPaths ?? new List<MonitoredPath>();

        foreach (var mp in paths)
        {
            if (!Directory.Exists(mp.Path)) continue;

            try
            {
                var dirInfo = new DirectoryInfo(mp.Path);
                result.Add(new MonitoredPathStats
                {
                    Path = mp.Path,
                    Name = mp.Name ?? Path.GetFileName(mp.Path),
                    SizeBytes = CalculateDirectorySize(dirInfo),
                    MaxSizeBytes = mp.MaxSizeGb * 1024L * 1024L * 1024L,
                    FileCount = Directory.GetFiles(mp.Path, "*", SearchOption.AllDirectories).Length,
                    LastModified = dirInfo.LastWriteTimeUtc
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get stats for monitored path: {Path}", mp.Path);
            }
        }

        return result;
    }

    private DiskAlert CreateAlert(DriveUtilization drive, AlertLevel level)
    {
        var message = level == AlertLevel.Critical
            ? $"CRITICAL: Drive {drive.Name} is {drive.UsedPercentage:F1}% full ({FormatBytes(drive.FreeSizeBytes)} free)"
            : $"WARNING: Drive {drive.Name} is {drive.UsedPercentage:F1}% full ({FormatBytes(drive.FreeSizeBytes)} free)";

        return new DiskAlert
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Drive = drive.Name,
            UsedPercentage = drive.UsedPercentage,
            FreeSizeBytes = drive.FreeSizeBytes,
            Message = message
        };
    }

    private AlertLevel DetermineAlertLevel(double usedPercentage)
    {
        var criticalThreshold = _config.ApplicationData?.DiskMonitoring?.CriticalThreshold ?? DefaultCriticalThreshold;
        var warningThreshold = _config.ApplicationData?.DiskMonitoring?.WarningThreshold ?? DefaultWarningThreshold;

        if (usedPercentage >= criticalThreshold)
            return AlertLevel.Critical;
        if (usedPercentage >= warningThreshold)
            return AlertLevel.Warning;
        return AlertLevel.Normal;
    }

    private static double CalculatePercentage(long total, long free)
    {
        if (total == 0) return 0;
        return ((double)(total - free) / total) * 100;
    }

    private static long CalculateDirectorySize(DirectoryInfo directory)
    {
        long size = 0;
        try
        {
            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    size += file.Length;
                }
                catch
                {
                    // Skip files we can't access
                }
            }
        }
        catch
        {
            // Skip if enumeration fails
        }
        return size;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

// DTOs

public class DiskUtilizationReport
{
    public DateTime Timestamp { get; set; }
    public List<DriveUtilization> Drives { get; set; } = new();
    public List<MonitoredPathStats> MonitoredPaths { get; set; } = new();
    public AlertLevel OverallStatus { get; set; }
}

public class DriveUtilization
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string DriveType { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public long FreeSizeBytes { get; set; }
    public long UsedSizeBytes { get; set; }
    public double UsedPercentage { get; set; }
    public AlertLevel AlertLevel { get; set; }
    
    // Formatted properties for display
    public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);
    public string FreeSizeFormatted => FormatBytes(FreeSizeBytes);
    public string UsedSizeFormatted => FormatBytes(UsedSizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class MonitoredPathStats
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long MaxSizeBytes { get; set; }
    public int FileCount { get; set; }
    public DateTime LastModified { get; set; }
    public double UsedPercentage => MaxSizeBytes > 0 ? ((double)SizeBytes / MaxSizeBytes) * 100 : 0;
}

public class PathUtilization
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public long DriveFreeSizeBytes { get; set; }
    public long DriveTotalSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}

public class DiskAlert
{
    public DateTime Timestamp { get; set; }
    public AlertLevel Level { get; set; }
    public string Drive { get; set; } = string.Empty;
    public double UsedPercentage { get; set; }
    public long FreeSizeBytes { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DiskCheckResult
{
    public DiskUtilizationReport Report { get; set; } = new();
    public List<DiskAlert> NewAlerts { get; set; } = new();
    public bool AlertsGenerated { get; set; }
}

public enum AlertLevel
{
    Normal,
    Warning,
    Critical
}

// MonitoredPath is defined in HoNConfiguration.cs
