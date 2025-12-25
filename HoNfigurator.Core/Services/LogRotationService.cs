using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages log rotation and cleanup for server logs.
/// Port of Python HoNfigurator-Central log management.
/// </summary>
public class LogRotationService : BackgroundService
{
    private readonly ILogger<LogRotationService> _logger;
    private readonly LogRotationConfiguration _config;
    private readonly ConcurrentDictionary<string, LogFileInfo> _trackedFiles = new();

    public LogRotationService(
        ILogger<LogRotationService> logger,
        LogRotationConfiguration? config = null)
    {
        _logger = logger;
        _config = config ?? new LogRotationConfiguration();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Log rotation service disabled");
            return;
        }

        _logger.LogInformation("Log rotation service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformRotationAsync(stoppingToken);
                await PerformCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log rotation");
            }

            await Task.Delay(TimeSpan.FromHours(_config.CheckIntervalHours), stoppingToken);
        }

        _logger.LogInformation("Log rotation service stopped");
    }

    /// <summary>
    /// Add a directory to track for log rotation
    /// </summary>
    public void TrackDirectory(string directory, string pattern = "*.log")
    {
        foreach (var file in Directory.GetFiles(directory, pattern, SearchOption.AllDirectories))
        {
            TrackFile(file);
        }
    }

    /// <summary>
    /// Add a file to track for rotation
    /// </summary>
    public void TrackFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        _trackedFiles[filePath] = new LogFileInfo
        {
            FilePath = filePath,
            CurrentSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            LastModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue
        };
    }

    /// <summary>
    /// Manually trigger rotation for a file
    /// </summary>
    public async Task<RotationResult> RotateFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new RotationResult
            {
                Success = false,
                OriginalFile = filePath,
                Error = "File not found"
            };
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            var directory = fileInfo.DirectoryName ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var extension = fileInfo.Extension;
            
            // Generate rotated filename
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var rotatedName = $"{baseName}_{timestamp}{extension}";
            var rotatedPath = Path.Combine(directory, rotatedName);

            // Move current file to rotated name
            File.Move(filePath, rotatedPath);

            // Create empty new file
            await File.WriteAllTextAsync(filePath, string.Empty, cancellationToken);

            // Compress if configured
            string? compressedPath = null;
            if (_config.CompressRotatedLogs)
            {
                compressedPath = await CompressFileAsync(rotatedPath, cancellationToken);
                File.Delete(rotatedPath);
            }

            _logger.LogInformation("Rotated log file: {Original} -> {Rotated}", 
                filePath, compressedPath ?? rotatedPath);

            return new RotationResult
            {
                Success = true,
                OriginalFile = filePath,
                RotatedFile = compressedPath ?? rotatedPath,
                OriginalSizeBytes = fileInfo.Length,
                Compressed = _config.CompressRotatedLogs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate file: {Path}", filePath);
            return new RotationResult
            {
                Success = false,
                OriginalFile = filePath,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Clean up old rotated log files
    /// </summary>
    public async Task<LogCleanupResult> CleanupOldLogsAsync(string directory, CancellationToken cancellationToken = default)
    {
        var result = new LogCleanupResult { Directory = directory };

        if (!Directory.Exists(directory))
        {
            result.Error = "Directory not found";
            return result;
        }

        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-_config.RetentionDays);
            var patterns = new[] { "*.log.*", "*.log.gz", "*.log.zip" };

            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.GetFiles(directory, pattern, SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        try
                        {
                            result.TotalSizeFreed += fileInfo.Length;
                            File.Delete(file);
                            result.DeletedFiles.Add(file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete old log: {File}", file);
                            result.FailedFiles.Add(file);
                        }
                    }
                }
            }

            result.Success = result.FailedFiles.Count == 0;
            _logger.LogInformation("Cleaned up {Count} old logs from {Directory}, freed {Size:N0} bytes",
                result.DeletedFiles.Count, directory, result.TotalSizeFreed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during log cleanup in {Directory}", directory);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Get disk usage statistics for log directories
    /// </summary>
    public LogStorageStats GetStorageStats()
    {
        var stats = new LogStorageStats();

        foreach (var tracked in _trackedFiles.Values)
        {
            if (File.Exists(tracked.FilePath))
            {
                var info = new FileInfo(tracked.FilePath);
                stats.TotalCurrentSize += info.Length;
                stats.ActiveLogCount++;
            }
        }

        // Count rotated logs
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator");

        if (Directory.Exists(appDataPath))
        {
            var rotatedPatterns = new[] { "*.log.gz", "*.log.zip", "*.log.[0-9]*" };
            foreach (var pattern in rotatedPatterns)
            {
                try
                {
                    foreach (var file in Directory.GetFiles(appDataPath, pattern, SearchOption.AllDirectories))
                    {
                        var info = new FileInfo(file);
                        stats.TotalRotatedSize += info.Length;
                        stats.RotatedLogCount++;
                    }
                }
                catch
                {
                    // Ignore pattern errors
                }
            }
        }

        stats.TotalSize = stats.TotalCurrentSize + stats.TotalRotatedSize;
        return stats;
    }

    private async Task PerformRotationAsync(CancellationToken cancellationToken)
    {
        foreach (var tracked in _trackedFiles.Values.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(tracked.FilePath))
                continue;

            var fileInfo = new FileInfo(tracked.FilePath);
            var shouldRotate = false;

            // Check size threshold
            if (fileInfo.Length >= _config.MaxFileSizeMB * 1024 * 1024)
            {
                shouldRotate = true;
                _logger.LogDebug("File exceeds size limit: {Path} ({Size:N0} bytes)", 
                    tracked.FilePath, fileInfo.Length);
            }

            // Check age threshold
            var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;
            if (age.TotalDays >= _config.MaxFileAgeDays)
            {
                shouldRotate = true;
                _logger.LogDebug("File exceeds age limit: {Path} ({Days:N1} days old)",
                    tracked.FilePath, age.TotalDays);
            }

            if (shouldRotate)
            {
                await RotateFileAsync(tracked.FilePath, cancellationToken);
            }
        }
    }

    private async Task PerformCleanupAsync(CancellationToken cancellationToken)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator");

        if (Directory.Exists(appDataPath))
        {
            await CleanupOldLogsAsync(appDataPath, cancellationToken);
        }
    }

    private async Task<string> CompressFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var compressedPath = filePath + ".gz";

        await using var inputStream = File.OpenRead(filePath);
        await using var outputStream = File.Create(compressedPath);
        await using var gzipStream = new System.IO.Compression.GZipStream(outputStream, System.IO.Compression.CompressionLevel.Optimal);
        
        await inputStream.CopyToAsync(gzipStream, cancellationToken);

        return compressedPath;
    }
}

// Configuration

public class LogRotationConfiguration
{
    public bool Enabled { get; set; } = true;
    public int MaxFileSizeMB { get; set; } = 100;
    public int MaxFileAgeDays { get; set; } = 7;
    public int RetentionDays { get; set; } = 30;
    public bool CompressRotatedLogs { get; set; } = true;
    public int CheckIntervalHours { get; set; } = 6;
}

// Internal DTOs

internal class LogFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public long CurrentSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
}

// Public DTOs

public class RotationResult
{
    public bool Success { get; set; }
    public string OriginalFile { get; set; } = string.Empty;
    public string? RotatedFile { get; set; }
    public long OriginalSizeBytes { get; set; }
    public bool Compressed { get; set; }
    public string? Error { get; set; }
}

public class LogCleanupResult
{
    public bool Success { get; set; }
    public string Directory { get; set; } = string.Empty;
    public List<string> DeletedFiles { get; set; } = new();
    public List<string> FailedFiles { get; set; } = new();
    public long TotalSizeFreed { get; set; }
    public string? Error { get; set; }
}

public class LogStorageStats
{
    public long TotalSize { get; set; }
    public long TotalCurrentSize { get; set; }
    public long TotalRotatedSize { get; set; }
    public int ActiveLogCount { get; set; }
    public int RotatedLogCount { get; set; }
}
