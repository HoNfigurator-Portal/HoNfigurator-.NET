using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages configuration backup and restore functionality.
/// Port of Python HoNfigurator-Central backup functionality.
/// </summary>
public class BackupRestoreService
{
    private readonly ILogger<BackupRestoreService> _logger;
    private readonly ConfigurationService _configService;
    private readonly string _backupDirectory;
    private readonly ConcurrentDictionary<string, BackupMetadata> _backupIndex = new();

    public BackupRestoreService(
        ILogger<BackupRestoreService> logger,
        ConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
        _backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator", "backups");
        
        Directory.CreateDirectory(_backupDirectory);
        LoadBackupIndex();
    }

    /// <summary>
    /// Create a full backup of all configuration
    /// </summary>
    public async Task<BackupInfo> CreateBackupAsync(string? description = null, CancellationToken cancellationToken = default)
    {
        var backupId = Guid.NewGuid().ToString("N")[..12];
        var timestamp = DateTime.UtcNow;
        var fileName = $"backup_{timestamp:yyyyMMdd_HHmmss}_{backupId}.zip";
        var filePath = Path.Combine(_backupDirectory, fileName);

        _logger.LogInformation("Creating backup {BackupId}", backupId);

        try
        {
            var metadata = new BackupMetadata
            {
                BackupId = backupId,
                FileName = fileName,
                CreatedAt = timestamp,
                Description = description ?? $"Backup created at {timestamp:yyyy-MM-dd HH:mm:ss}",
                BackupType = BackupType.Full
            };

            using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Create))
            {
                // Backup main configuration
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoNfigurator", "config.json");
                
                if (File.Exists(configPath))
                {
                    archive.CreateEntryFromFile(configPath, "config.json");
                    metadata.IncludedFiles.Add("config.json");
                }

                // Backup server configurations
                var serversPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoNfigurator", "servers");
                
                if (Directory.Exists(serversPath))
                {
                    await BackupDirectoryAsync(archive, serversPath, "servers", metadata, cancellationToken);
                }

                // Backup bans
                var bansPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoNfigurator", "bans.json");
                
                if (File.Exists(bansPath))
                {
                    archive.CreateEntryFromFile(bansPath, "bans.json");
                    metadata.IncludedFiles.Add("bans.json");
                }

                // Backup roles database
                var rolesDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoNfigurator", "roles.db");
                
                if (File.Exists(rolesDbPath))
                {
                    archive.CreateEntryFromFile(rolesDbPath, "roles.db");
                    metadata.IncludedFiles.Add("roles.db");
                }

                // Save metadata
                var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                var metadataEntry = archive.CreateEntry("backup_metadata.json");
                using var writer = new StreamWriter(metadataEntry.Open());
                await writer.WriteAsync(metadataJson);
            }

            // Get file size
            var fileInfo = new FileInfo(filePath);
            metadata.SizeBytes = fileInfo.Length;

            // Add to index
            _backupIndex[backupId] = metadata;
            await SaveBackupIndexAsync(cancellationToken);

            _logger.LogInformation("Backup {BackupId} created successfully: {FileName} ({Size:N0} bytes)",
                backupId, fileName, metadata.SizeBytes);

            return new BackupInfo
            {
                BackupId = backupId,
                FileName = fileName,
                FilePath = filePath,
                CreatedAt = timestamp,
                Description = metadata.Description,
                SizeBytes = metadata.SizeBytes,
                FileCount = metadata.IncludedFiles.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup");
            
            // Clean up partial backup
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Restore from a backup
    /// </summary>
    public async Task<RestoreResult> RestoreBackupAsync(string backupId, RestoreOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RestoreOptions();

        if (!_backupIndex.TryGetValue(backupId, out var metadata))
        {
            return new RestoreResult
            {
                Success = false,
                Error = $"Backup {backupId} not found"
            };
        }

        var filePath = Path.Combine(_backupDirectory, metadata.FileName);
        if (!File.Exists(filePath))
        {
            return new RestoreResult
            {
                Success = false,
                Error = $"Backup file not found: {metadata.FileName}"
            };
        }

        _logger.LogInformation("Restoring backup {BackupId}", backupId);

        var result = new RestoreResult
        {
            BackupId = backupId,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoNfigurator");

            // Create pre-restore backup if requested
            if (options.CreatePreRestoreBackup)
            {
                var preBackup = await CreateBackupAsync("Pre-restore automatic backup", cancellationToken);
                result.PreRestoreBackupId = preBackup.BackupId;
            }

            using var archive = ZipFile.OpenRead(filePath);
            
            foreach (var entry in archive.Entries)
            {
                // Skip metadata file
                if (entry.Name == "backup_metadata.json")
                    continue;

                // Check filters
                if (options.OnlyConfig && !entry.FullName.EndsWith("config.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (options.OnlyBans && !entry.FullName.EndsWith("bans.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetPath = Path.Combine(appDataPath, entry.FullName);
                var targetDir = Path.GetDirectoryName(targetPath);
                
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                try
                {
                    entry.ExtractToFile(targetPath, overwrite: true);
                    result.RestoredFiles.Add(entry.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore file: {File}", entry.FullName);
                    result.FailedFiles.Add(entry.FullName);
                }
            }

            // Reload configuration if requested
            if (options.ReloadAfterRestore)
            {
                await _configService.ReloadAsync(cancellationToken);
            }

            result.Success = result.FailedFiles.Count == 0;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Backup {BackupId} restored: {Restored} files, {Failed} failures",
                backupId, result.RestoredFiles.Count, result.FailedFiles.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup {BackupId}", backupId);
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// List all available backups
    /// </summary>
    public IEnumerable<BackupInfo> ListBackups()
    {
        return _backupIndex.Values
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new BackupInfo
            {
                BackupId = m.BackupId,
                FileName = m.FileName,
                FilePath = Path.Combine(_backupDirectory, m.FileName),
                CreatedAt = m.CreatedAt,
                Description = m.Description,
                SizeBytes = m.SizeBytes,
                FileCount = m.IncludedFiles.Count,
                BackupType = m.BackupType
            })
            .ToList();
    }

    /// <summary>
    /// Get backup details
    /// </summary>
    public BackupDetails? GetBackupDetails(string backupId)
    {
        if (!_backupIndex.TryGetValue(backupId, out var metadata))
            return null;

        return new BackupDetails
        {
            BackupId = metadata.BackupId,
            FileName = metadata.FileName,
            CreatedAt = metadata.CreatedAt,
            Description = metadata.Description,
            SizeBytes = metadata.SizeBytes,
            BackupType = metadata.BackupType,
            IncludedFiles = metadata.IncludedFiles.ToList()
        };
    }

    /// <summary>
    /// Delete a backup
    /// </summary>
    public async Task<bool> DeleteBackupAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (!_backupIndex.TryRemove(backupId, out var metadata))
            return false;

        var filePath = Path.Combine(_backupDirectory, metadata.FileName);
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await SaveBackupIndexAsync(cancellationToken);
        _logger.LogInformation("Backup {BackupId} deleted", backupId);
        
        return true;
    }

    /// <summary>
    /// Clean up old backups
    /// </summary>
    public async Task<int> CleanupOldBackupsAsync(int keepCount = 10, int maxAgeDays = 30, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeDays);
        var toDelete = _backupIndex.Values
            .OrderByDescending(m => m.CreatedAt)
            .Skip(keepCount)
            .Where(m => m.CreatedAt < cutoffDate)
            .Select(m => m.BackupId)
            .ToList();

        foreach (var backupId in toDelete)
        {
            await DeleteBackupAsync(backupId, cancellationToken);
        }

        _logger.LogInformation("Cleaned up {Count} old backups", toDelete.Count);
        return toDelete.Count;
    }

    /// <summary>
    /// Export a backup to a different location
    /// </summary>
    public async Task<bool> ExportBackupAsync(string backupId, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!_backupIndex.TryGetValue(backupId, out var metadata))
            return false;

        var sourcePath = Path.Combine(_backupDirectory, metadata.FileName);
        
        if (!File.Exists(sourcePath))
            return false;

        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        await using var source = File.OpenRead(sourcePath);
        await using var dest = File.Create(destinationPath);
        await source.CopyToAsync(dest, cancellationToken);

        _logger.LogInformation("Backup {BackupId} exported to {Path}", backupId, destinationPath);
        return true;
    }

    /// <summary>
    /// Import a backup from external location
    /// </summary>
    public async Task<BackupInfo?> ImportBackupAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Import source not found: {Path}", sourcePath);
            return null;
        }

        try
        {
            // Verify it's a valid backup
            using var archive = ZipFile.OpenRead(sourcePath);
            var metadataEntry = archive.GetEntry("backup_metadata.json");
            
            BackupMetadata? metadata = null;
            if (metadataEntry != null)
            {
                using var reader = new StreamReader(metadataEntry.Open());
                var json = await reader.ReadToEndAsync();
                metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);
            }

            // Generate new ID for imported backup
            var newId = Guid.NewGuid().ToString("N")[..12];
            var timestamp = DateTime.UtcNow;
            var fileName = $"imported_{timestamp:yyyyMMdd_HHmmss}_{newId}.zip";
            var destPath = Path.Combine(_backupDirectory, fileName);

            // Copy file
            await using (var source = File.OpenRead(sourcePath))
            await using (var dest = File.Create(destPath))
            {
                await source.CopyToAsync(dest, cancellationToken);
            }

            // Create metadata
            var newMetadata = new BackupMetadata
            {
                BackupId = newId,
                FileName = fileName,
                CreatedAt = metadata?.CreatedAt ?? timestamp,
                Description = $"Imported: {metadata?.Description ?? Path.GetFileName(sourcePath)}",
                BackupType = BackupType.Imported,
                SizeBytes = new FileInfo(destPath).Length,
                IncludedFiles = metadata?.IncludedFiles ?? new List<string>()
            };

            _backupIndex[newId] = newMetadata;
            await SaveBackupIndexAsync(cancellationToken);

            _logger.LogInformation("Backup imported: {BackupId}", newId);

            return new BackupInfo
            {
                BackupId = newId,
                FileName = fileName,
                FilePath = destPath,
                CreatedAt = newMetadata.CreatedAt,
                Description = newMetadata.Description,
                SizeBytes = newMetadata.SizeBytes,
                FileCount = newMetadata.IncludedFiles.Count,
                BackupType = BackupType.Imported
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import backup from {Path}", sourcePath);
            return null;
        }
    }

    private async Task BackupDirectoryAsync(ZipArchive archive, string sourceDir, string entryPrefix, BackupMetadata metadata, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var entryPath = Path.Combine(entryPrefix, relativePath).Replace('\\', '/');
            
            archive.CreateEntryFromFile(file, entryPath);
            metadata.IncludedFiles.Add(entryPath);
        }
        
        await Task.CompletedTask;
    }

    private void LoadBackupIndex()
    {
        var indexPath = Path.Combine(_backupDirectory, "backup_index.json");
        
        if (File.Exists(indexPath))
        {
            try
            {
                var json = File.ReadAllText(indexPath);
                var index = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, BackupMetadata>>(json);
                
                if (index != null)
                {
                    foreach (var kvp in index)
                    {
                        _backupIndex[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load backup index");
            }
        }

        // Scan for unindexed backups
        foreach (var file in Directory.GetFiles(_backupDirectory, "*.zip"))
        {
            var fileName = Path.GetFileName(file);
            if (!_backupIndex.Values.Any(m => m.FileName == fileName))
            {
                // Try to read metadata from archive
                try
                {
                    using var archive = ZipFile.OpenRead(file);
                    var metadataEntry = archive.GetEntry("backup_metadata.json");
                    
                    if (metadataEntry != null)
                    {
                        using var reader = new StreamReader(metadataEntry.Open());
                        var json = reader.ReadToEnd();
                        var metadata = System.Text.Json.JsonSerializer.Deserialize<BackupMetadata>(json);
                        
                        if (metadata != null)
                        {
                            _backupIndex[metadata.BackupId] = metadata;
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable archives
                }
            }
        }
    }

    private async Task SaveBackupIndexAsync(CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(_backupDirectory, "backup_index.json");
        var json = System.Text.Json.JsonSerializer.Serialize(
            _backupIndex.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        
        await File.WriteAllTextAsync(indexPath, json, cancellationToken);
    }
}

// Enums

public enum BackupType
{
    Full,
    Incremental,
    Config,
    Imported
}

// Internal DTOs

internal class BackupMetadata
{
    public string BackupId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public BackupType BackupType { get; set; }
    public long SizeBytes { get; set; }
    public List<string> IncludedFiles { get; set; } = new();
}

// Public DTOs

public class BackupInfo
{
    public string BackupId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public BackupType BackupType { get; set; }
}

public class BackupDetails
{
    public string BackupId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public BackupType BackupType { get; set; }
    public List<string> IncludedFiles { get; set; } = new();
}

public class RestoreOptions
{
    public bool CreatePreRestoreBackup { get; set; } = true;
    public bool ReloadAfterRestore { get; set; } = true;
    public bool OnlyConfig { get; set; } = false;
    public bool OnlyBans { get; set; } = false;
}

public class RestoreResult
{
    public bool Success { get; set; }
    public string BackupId { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? PreRestoreBackupId { get; set; }
    public List<string> RestoredFiles { get; set; } = new();
    public List<string> FailedFiles { get; set; } = new();
    public string? Error { get; set; }
}
