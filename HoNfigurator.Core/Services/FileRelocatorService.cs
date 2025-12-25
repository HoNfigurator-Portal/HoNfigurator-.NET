using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Moves old files (replays, logs) to long-term storage.
/// Port of Python HoNfigurator-Central FileRelocator.
/// </summary>
public class FileRelocatorService
{
    private readonly ILogger<FileRelocatorService> _logger;
    private readonly HoNConfiguration _config;
    private readonly string _primaryPath;
    private readonly string? _archivePath;
    private bool _isRunning;

    public bool IsConfigured => !string.IsNullOrEmpty(_archivePath);
    public bool IsRunning => _isRunning;

    public FileRelocatorService(ILogger<FileRelocatorService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        _primaryPath = config.ApplicationData?.Storage?.PrimaryPath ?? "replays";
        _archivePath = config.ApplicationData?.Storage?.ArchivePath;
    }

    /// <summary>
    /// Get current storage status
    /// </summary>
    public StorageStatus GetStatus()
    {
        var primaryInfo = GetDirectoryInfo(_primaryPath);
        var archiveInfo = _archivePath != null ? GetDirectoryInfo(_archivePath) : null;

        return new StorageStatus
        {
            IsConfigured = IsConfigured,
            PrimaryPath = _primaryPath,
            ArchivePath = _archivePath,
            PrimaryStats = primaryInfo,
            ArchiveStats = archiveInfo,
            RetentionDays = _config.ApplicationData?.Storage?.RetentionDays ?? 30,
            ArchiveAfterDays = _config.ApplicationData?.Storage?.ArchiveAfterDays ?? 7
        };
    }

    /// <summary>
    /// Relocate old files to archive storage
    /// </summary>
    public async Task<RelocationResult> RelocateOldFilesAsync(
        int? olderThanDays = null, 
        string? filePattern = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new RelocationResult
            {
                Success = false,
                Error = "Archive path not configured"
            };
        }

        var daysThreshold = olderThanDays ?? _config.ApplicationData?.Storage?.ArchiveAfterDays ?? 7;
        var pattern = filePattern ?? "*.honreplay";
        var cutoffDate = DateTime.UtcNow.AddDays(-daysThreshold);

        _isRunning = true;
        var result = new RelocationResult();

        try
        {
            _logger.LogInformation("Starting file relocation: files older than {Days} days, pattern: {Pattern}", 
                daysThreshold, pattern);

            // Ensure archive directory exists
            if (!Directory.Exists(_archivePath))
                Directory.CreateDirectory(_archivePath!);

            var filesToMove = Directory.GetFiles(_primaryPath, pattern, SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTimeUtc < cutoffDate)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            result.TotalFiles = filesToMove.Count;
            result.TotalSize = filesToMove.Sum(f => f.Length);

            foreach (var file in filesToMove)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Preserve directory structure in archive
                    var relativePath = Path.GetRelativePath(_primaryPath, file.FullName);
                    var destPath = Path.Combine(_archivePath!, relativePath);
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    // Move file
                    File.Move(file.FullName, destPath, overwrite: true);
                    result.MovedFiles++;
                    result.MovedSize += file.Length;

                    _logger.LogDebug("Moved: {Source} -> {Dest}", file.FullName, destPath);
                }
                catch (Exception ex)
                {
                    result.FailedFiles++;
                    result.Errors.Add($"{file.Name}: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to move file: {File}", file.FullName);
                }
            }

            result.Success = result.FailedFiles == 0;
            _logger.LogInformation("Relocation complete: {Moved}/{Total} files moved", 
                result.MovedFiles, result.TotalFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File relocation failed");
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            _isRunning = false;
        }

        return result;
    }

    /// <summary>
    /// Clean up old files from archive
    /// </summary>
    public async Task<CleanupResult> CleanupArchiveAsync(
        int? olderThanDays = null,
        string? filePattern = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrEmpty(_archivePath))
        {
            return new CleanupResult
            {
                Success = false,
                Error = "Archive path not configured"
            };
        }

        var daysThreshold = olderThanDays ?? _config.ApplicationData?.Storage?.RetentionDays ?? 90;
        var pattern = filePattern ?? "*.*";
        var cutoffDate = DateTime.UtcNow.AddDays(-daysThreshold);

        var result = new CleanupResult();

        try
        {
            _logger.LogInformation("Starting archive cleanup: files older than {Days} days", daysThreshold);

            var filesToDelete = Directory.GetFiles(_archivePath, pattern, SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastWriteTimeUtc < cutoffDate)
                .ToList();

            result.TotalFiles = filesToDelete.Count;
            result.TotalSize = filesToDelete.Sum(f => f.Length);

            foreach (var file in filesToDelete)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    file.Delete();
                    result.DeletedFiles++;
                    result.DeletedSize += file.Length;
                }
                catch (Exception ex)
                {
                    result.FailedFiles++;
                    result.Errors.Add($"{file.Name}: {ex.Message}");
                }
            }

            // Clean up empty directories
            CleanupEmptyDirectories(_archivePath);

            result.Success = result.FailedFiles == 0;
            _logger.LogInformation("Cleanup complete: {Deleted}/{Total} files deleted, freed {Size} bytes", 
                result.DeletedFiles, result.TotalFiles, result.DeletedSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive cleanup failed");
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Relocate logs to archive
    /// </summary>
    public async Task<RelocationResult> RelocateLogsAsync(int? olderThanDays = null, 
        CancellationToken cancellationToken = default)
    {
        var logsPath = _config.ApplicationData?.Storage?.LogsPath ?? "logs";
        var archiveLogsPath = _archivePath != null 
            ? Path.Combine(_archivePath, "logs") 
            : null;

        if (string.IsNullOrEmpty(archiveLogsPath))
        {
            return new RelocationResult
            {
                Success = false,
                Error = "Archive path not configured"
            };
        }

        // Temporarily swap paths for this operation
        var originalPrimary = _primaryPath;
        try
        {
            return await RelocateFilesFromPathAsync(logsPath, archiveLogsPath, 
                olderThanDays ?? 7, "*.log", cancellationToken);
        }
        catch (Exception ex)
        {
            return new RelocationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get detailed storage analytics
    /// </summary>
    public StorageAnalytics GetStorageAnalytics()
    {
        var analytics = new StorageAnalytics();

        // Analyze primary storage
        if (Directory.Exists(_primaryPath))
        {
            var files = Directory.GetFiles(_primaryPath, "*.*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();

            analytics.PrimaryFileCount = files.Count;
            analytics.PrimaryTotalSize = files.Sum(f => f.Length);
            analytics.PrimaryOldestFile = files.Min(f => (DateTime?)f.LastWriteTimeUtc);
            analytics.PrimaryNewestFile = files.Max(f => (DateTime?)f.LastWriteTimeUtc);

            // Group by extension
            analytics.PrimaryByExtension = files
                .GroupBy(f => f.Extension.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => new FileGroupStats
                {
                    Count = g.Count(),
                    TotalSize = g.Sum(f => f.Length)
                });
        }

        // Analyze archive storage
        if (!string.IsNullOrEmpty(_archivePath) && Directory.Exists(_archivePath))
        {
            var files = Directory.GetFiles(_archivePath, "*.*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();

            analytics.ArchiveFileCount = files.Count;
            analytics.ArchiveTotalSize = files.Sum(f => f.Length);
            analytics.ArchiveOldestFile = files.Min(f => (DateTime?)f.LastWriteTimeUtc);
            analytics.ArchiveNewestFile = files.Max(f => (DateTime?)f.LastWriteTimeUtc);

            analytics.ArchiveByExtension = files
                .GroupBy(f => f.Extension.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => new FileGroupStats
                {
                    Count = g.Count(),
                    TotalSize = g.Sum(f => f.Length)
                });
        }

        return analytics;
    }

    private async Task<RelocationResult> RelocateFilesFromPathAsync(
        string sourcePath, 
        string destPath, 
        int olderThanDays, 
        string pattern,
        CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var result = new RelocationResult();

        if (!Directory.Exists(sourcePath))
        {
            return result;
        }

        if (!Directory.Exists(destPath))
            Directory.CreateDirectory(destPath);

        var files = Directory.GetFiles(sourcePath, pattern, SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc < cutoffDate)
            .ToList();

        result.TotalFiles = files.Count;
        result.TotalSize = files.Sum(f => f.Length);

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var relativePath = Path.GetRelativePath(sourcePath, file.FullName);
                var dest = Path.Combine(destPath, relativePath);
                var destDir = Path.GetDirectoryName(dest);

                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.Move(file.FullName, dest, overwrite: true);
                result.MovedFiles++;
                result.MovedSize += file.Length;
            }
            catch (Exception ex)
            {
                result.FailedFiles++;
                result.Errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        result.Success = result.FailedFiles == 0;
        return result;
    }

    private DirectoryStats GetDirectoryInfo(string path)
    {
        var stats = new DirectoryStats { Path = path };

        if (!Directory.Exists(path))
        {
            stats.Exists = false;
            return stats;
        }

        try
        {
            stats.Exists = true;
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            stats.FileCount = files.Length;
            stats.TotalSize = files.Sum(f => new FileInfo(f).Length);

            var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "C:");
            stats.DriveFreeSpace = driveInfo.AvailableFreeSpace;
            stats.DriveTotalSpace = driveInfo.TotalSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get directory info for {Path}", path);
        }

        return stats;
    }

    private void CleanupEmptyDirectories(string path)
    {
        foreach (var dir in Directory.GetDirectories(path))
        {
            CleanupEmptyDirectories(dir);
            
            if (!Directory.GetFileSystemEntries(dir).Any())
            {
                try
                {
                    Directory.Delete(dir);
                }
                catch { /* Ignore */ }
            }
        }
    }
}

#region Models

public class StorageSettings
{
    public string PrimaryPath { get; set; } = "replays";
    public string? ArchivePath { get; set; }
    public string? LogsPath { get; set; } = "logs";
    public int ArchiveAfterDays { get; set; } = 7;
    public int RetentionDays { get; set; } = 90;
    public bool AutoRelocate { get; set; } = false;
    public bool AutoCleanup { get; set; } = false;
}

public class StorageStatus
{
    public bool IsConfigured { get; set; }
    public string? PrimaryPath { get; set; }
    public string? ArchivePath { get; set; }
    public DirectoryStats? PrimaryStats { get; set; }
    public DirectoryStats? ArchiveStats { get; set; }
    public int RetentionDays { get; set; }
    public int ArchiveAfterDays { get; set; }
}

public class DirectoryStats
{
    public string Path { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public long DriveFreeSpace { get; set; }
    public long DriveTotalSpace { get; set; }
    public double DriveUsagePercent => DriveTotalSpace > 0 
        ? 100.0 * (DriveTotalSpace - DriveFreeSpace) / DriveTotalSpace 
        : 0;
}

public class RelocationResult
{
    public bool Success { get; set; }
    public int TotalFiles { get; set; }
    public int MovedFiles { get; set; }
    public int FailedFiles { get; set; }
    public long TotalSize { get; set; }
    public long MovedSize { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? Error { get; set; }
}

public class CleanupResult
{
    public bool Success { get; set; }
    public int TotalFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public long TotalSize { get; set; }
    public long DeletedSize { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? Error { get; set; }
}

public class StorageAnalytics
{
    public int PrimaryFileCount { get; set; }
    public long PrimaryTotalSize { get; set; }
    public DateTime? PrimaryOldestFile { get; set; }
    public DateTime? PrimaryNewestFile { get; set; }
    public Dictionary<string, FileGroupStats> PrimaryByExtension { get; set; } = new();
    
    public int ArchiveFileCount { get; set; }
    public long ArchiveTotalSize { get; set; }
    public DateTime? ArchiveOldestFile { get; set; }
    public DateTime? ArchiveNewestFile { get; set; }
    public Dictionary<string, FileGroupStats> ArchiveByExtension { get; set; } = new();
}

public class FileGroupStats
{
    public int Count { get; set; }
    public long TotalSize { get; set; }
}

#endregion
