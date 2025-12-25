using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Handles automatic game server patching and updates.
/// Port of Python HoNfigurator-Central patching functionality.
/// </summary>
public class ServerPatchingService
{
    private readonly ILogger<ServerPatchingService> _logger;
    private readonly HoNConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _patchLock = new(1, 1);
    private bool _isPatchingInProgress;

    public bool IsPatchingInProgress => _isPatchingInProgress;

    public ServerPatchingService(ILogger<ServerPatchingService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Check if a patch is available
    /// </summary>
    public async Task<GamePatchCheckResult> CheckForPatchAsync(CancellationToken cancellationToken = default)
    {
        var result = new GamePatchCheckResult();

        try
        {
            var patchServerUrl = $"http://{_config.HonData?.PatchServer ?? "api.kongor.net/patches"}";
            var currentVersion = await GetCurrentVersionAsync();
            
            result.CurrentVersion = currentVersion;

            // Get latest version info from patch server
            var latestInfo = await GetLatestVersionInfoAsync(patchServerUrl, cancellationToken);
            if (latestInfo == null)
            {
                result.Error = "Failed to get version info from patch server";
                return result;
            }

            result.LatestVersion = latestInfo.Version;
            result.PatchAvailable = CompareVersions(currentVersion, latestInfo.Version) < 0;
            result.PatchSize = latestInfo.PatchSize;
            result.PatchUrl = latestInfo.PatchUrl;
            result.ReleaseNotes = latestInfo.ReleaseNotes;
            result.Success = true;

            if (result.PatchAvailable)
            {
                _logger.LogInformation("Patch available: {Current} -> {Latest}", 
                    currentVersion, latestInfo.Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for patches");
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Download and apply a patch
    /// </summary>
    public async Task<GamePatchResult> ApplyPatchAsync(
        string? patchUrl = null,
        bool backupFirst = true,
        IProgress<GamePatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_isPatchingInProgress)
        {
            return new GamePatchResult
            {
                Success = false,
                Error = "Patching already in progress"
            };
        }

        await _patchLock.WaitAsync(cancellationToken);
        _isPatchingInProgress = true;

        try
        {
            var result = new GamePatchResult();
            var installDir = _config.HonData?.HonInstallDirectory;

            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            {
                result.Error = "Invalid HoN install directory";
                return result;
            }

            // Get patch URL if not provided
            if (string.IsNullOrEmpty(patchUrl))
            {
                var checkResult = await CheckForPatchAsync(cancellationToken);
                if (!checkResult.PatchAvailable)
                {
                    result.Success = true;
                    result.Message = "Already up to date";
                    return result;
                }
                patchUrl = checkResult.PatchUrl;
            }

            if (string.IsNullOrEmpty(patchUrl))
            {
                result.Error = "No patch URL available";
                return result;
            }

            result.PreviousVersion = await GetCurrentVersionAsync();

            // Backup if requested
            if (backupFirst)
            {
                progress?.Report(new GamePatchProgress { Stage = "Backup", Message = "Creating backup..." });
                await CreateBackupAsync(installDir, cancellationToken);
            }

            // Download patch
            progress?.Report(new GamePatchProgress { Stage = "Download", Message = "Downloading patch..." });
            var patchFile = await DownloadPatchAsync(patchUrl, progress, cancellationToken);
            
            if (string.IsNullOrEmpty(patchFile))
            {
                result.Error = "Failed to download patch";
                return result;
            }

            // Verify patch integrity
            progress?.Report(new GamePatchProgress { Stage = "Verify", Message = "Verifying patch..." });
            if (!await VerifyPatchAsync(patchFile, cancellationToken))
            {
                result.Error = "Patch verification failed";
                return result;
            }

            // Apply patch
            progress?.Report(new GamePatchProgress { Stage = "Apply", Message = "Applying patch..." });
            var applySuccess = await ApplyPatchFilesAsync(patchFile, installDir, progress, cancellationToken);
            
            if (!applySuccess)
            {
                result.Error = "Failed to apply patch files";
                return result;
            }

            // Cleanup
            progress?.Report(new GamePatchProgress { Stage = "Cleanup", Message = "Cleaning up..." });
            try { File.Delete(patchFile); } catch { }

            result.Success = true;
            result.NewVersion = await GetCurrentVersionAsync();
            result.Message = $"Patched from {result.PreviousVersion} to {result.NewVersion}";
            
            _logger.LogInformation("Patch applied successfully: {Message}", result.Message);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch");
            return new GamePatchResult
            {
                Success = false,
                Error = ex.Message
            };
        }
        finally
        {
            _isPatchingInProgress = false;
            _patchLock.Release();
        }
    }

    /// <summary>
    /// Verify game files integrity
    /// </summary>
    public async Task<IntegrityCheckResult> VerifyGameFilesAsync(
        IProgress<GamePatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new IntegrityCheckResult();
        var installDir = _config.HonData?.HonInstallDirectory;

        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
        {
            result.Error = "Invalid HoN install directory";
            return result;
        }

        try
        {
            // Get manifest from patch server
            var manifestUrl = $"http://{_config.HonData?.PatchServer}/manifest.json";
            var manifest = await _httpClient.GetFromJsonAsync<FileManifest>(manifestUrl, cancellationToken);

            if (manifest?.Files == null)
            {
                result.Error = "Failed to get file manifest";
                return result;
            }

            var totalFiles = manifest.Files.Count;
            var checkedFiles = 0;

            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var filePath = Path.Combine(installDir, file.Path);
                checkedFiles++;

                progress?.Report(new GamePatchProgress
                {
                    Stage = "Verify",
                    Message = $"Checking {file.Path}",
                    PercentComplete = (int)((checkedFiles * 100.0) / totalFiles)
                });

                if (!File.Exists(filePath))
                {
                    result.MissingFiles.Add(file.Path);
                    continue;
                }

                // Check CRC if provided
                if (!string.IsNullOrEmpty(file.Crc))
                {
                    var actualCrc = await CalculateCrcAsync(filePath, cancellationToken);
                    if (actualCrc != file.Crc)
                    {
                        result.CorruptedFiles.Add(new CorruptedFile
                        {
                            Path = file.Path,
                            ExpectedCrc = file.Crc,
                            ActualCrc = actualCrc
                        });
                    }
                }

                result.VerifiedFiles++;
            }

            result.Success = result.MissingFiles.Count == 0 && result.CorruptedFiles.Count == 0;
            result.TotalFiles = totalFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify game files");
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Repair corrupted or missing files
    /// </summary>
    public async Task<RepairResult> RepairGameFilesAsync(
        List<string>? filesToRepair = null,
        IProgress<GamePatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RepairResult();

        // First verify to find issues
        if (filesToRepair == null)
        {
            var verifyResult = await VerifyGameFilesAsync(progress, cancellationToken);
            filesToRepair = verifyResult.MissingFiles
                .Concat(verifyResult.CorruptedFiles.Select(f => f.Path))
                .ToList();
        }

        if (filesToRepair.Count == 0)
        {
            result.Success = true;
            result.Message = "No files need repair";
            return result;
        }

        var installDir = _config.HonData?.HonInstallDirectory;
        var patchServerUrl = $"http://{_config.HonData?.PatchServer}";

        var totalFiles = filesToRepair.Count;
        var repairedFiles = 0;

        foreach (var file in filesToRepair)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                progress?.Report(new GamePatchProgress
                {
                    Stage = "Repair",
                    Message = $"Repairing {file}",
                    PercentComplete = (int)((repairedFiles * 100.0) / totalFiles)
                });

                var fileUrl = $"{patchServerUrl}/files/{file.Replace("\\", "/")}";
                var destPath = Path.Combine(installDir!, file);

                // Ensure directory exists
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Download file
                var response = await _httpClient.GetAsync(fileUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var fs = File.Create(destPath);
                await response.Content.CopyToAsync(fs, cancellationToken);

                result.RepairedFiles.Add(file);
                repairedFiles++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to repair file: {File}", file);
                result.FailedFiles.Add(file);
            }
        }

        result.Success = result.FailedFiles.Count == 0;
        result.Message = $"Repaired {result.RepairedFiles.Count}/{totalFiles} files";

        return result;
    }

    /// <summary>
    /// Get current game version
    /// </summary>
    public async Task<string> GetCurrentVersionAsync()
    {
        var installDir = _config.HonData?.HonInstallDirectory;
        if (string.IsNullOrEmpty(installDir))
            return "unknown";

        // Try reading version file
        var versionFile = Path.Combine(installDir, "version.txt");
        if (File.Exists(versionFile))
        {
            return (await File.ReadAllTextAsync(versionFile)).Trim();
        }

        // Try reading from game executable
        var exePath = Path.Combine(installDir, 
            OperatingSystem.IsWindows() ? "hon.exe" : "hon");
        
        if (File.Exists(exePath))
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? "unknown";
            }
            catch
            {
                // Ignore
            }
        }

        return "unknown";
    }

    private async Task<GameVersionInfo?> GetLatestVersionInfoAsync(
        string patchServerUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var versionUrl = $"{patchServerUrl}/version.json";
            return await _httpClient.GetFromJsonAsync<GameVersionInfo>(versionUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get version info from {Url}", patchServerUrl);
            return null;
        }
    }

    private async Task<string?> DownloadPatchAsync(
        string patchUrl,
        IProgress<GamePatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"hon_patch_{Guid.NewGuid()}.zip");

        try
        {
            using var response = await _httpClient.GetAsync(patchUrl, 
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(tempFile);
            
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report(new GamePatchProgress
                    {
                        Stage = "Download",
                        Message = $"Downloading: {downloadedBytes / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB",
                        PercentComplete = (int)((downloadedBytes * 100) / totalBytes),
                        BytesDownloaded = downloadedBytes,
                        TotalBytes = totalBytes
                    });
                }
            }

            return tempFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download patch from {Url}", patchUrl);
            try { File.Delete(tempFile); } catch { }
            return null;
        }
    }

    private async Task<bool> VerifyPatchAsync(string patchFile, CancellationToken cancellationToken)
    {
        try
        {
            // Basic verification - check if it's a valid zip
            using var archive = ZipFile.OpenRead(patchFile);
            return archive.Entries.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patch file verification failed");
            return false;
        }
    }

    private async Task<bool> ApplyPatchFilesAsync(
        string patchFile,
        string installDir,
        IProgress<GamePatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(patchFile);
            var totalEntries = archive.Entries.Count;
            var processedEntries = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destPath = Path.Combine(installDir, entry.FullName);

                processedEntries++;
                progress?.Report(new GamePatchProgress
                {
                    Stage = "Apply",
                    Message = $"Extracting: {entry.FullName}",
                    PercentComplete = (int)((processedEntries * 100.0) / totalEntries)
                });

                // Skip directories
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                // Ensure parent directory exists
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Extract file
                entry.ExtractToFile(destPath, overwrite: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch files");
            return false;
        }
    }

    private async Task CreateBackupAsync(string installDir, CancellationToken cancellationToken)
    {
        var backupDir = Path.Combine(installDir, "..", "hon_backup_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        
        // Backup critical files
        var filesToBackup = new[] { "version.txt", "k2launcher.log" };
        
        Directory.CreateDirectory(backupDir);
        
        foreach (var file in filesToBackup)
        {
            var srcPath = Path.Combine(installDir, file);
            if (File.Exists(srcPath))
            {
                File.Copy(srcPath, Path.Combine(backupDir, file), overwrite: true);
            }
        }
    }

    private async Task<string> CalculateCrcAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var fs = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(fs, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private int CompareVersions(string version1, string version2)
    {
        var v1Parts = version1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var v2Parts = version2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

        for (var i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
        {
            var v1 = i < v1Parts.Length ? v1Parts[i] : 0;
            var v2 = i < v2Parts.Length ? v2Parts[i] : 0;

            if (v1 < v2) return -1;
            if (v1 > v2) return 1;
        }

        return 0;
    }
}

// DTOs - prefixed with "Game" to avoid conflicts with GitBranchService

public class GamePatchCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string CurrentVersion { get; set; } = "unknown";
    public string? LatestVersion { get; set; }
    public bool PatchAvailable { get; set; }
    public long? PatchSize { get; set; }
    public string? PatchUrl { get; set; }
    public string? ReleaseNotes { get; set; }
}

public class GamePatchResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? PreviousVersion { get; set; }
    public string? NewVersion { get; set; }
}

public class GamePatchProgress
{
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}

public class IntegrityCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalFiles { get; set; }
    public int VerifiedFiles { get; set; }
    public List<string> MissingFiles { get; set; } = new();
    public List<CorruptedFile> CorruptedFiles { get; set; } = new();
}

public class CorruptedFile
{
    public string Path { get; set; } = string.Empty;
    public string ExpectedCrc { get; set; } = string.Empty;
    public string ActualCrc { get; set; } = string.Empty;
}

public class RepairResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<string> RepairedFiles { get; set; } = new();
    public List<string> FailedFiles { get; set; } = new();
}

public class GameVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string? PatchUrl { get; set; }
    public long PatchSize { get; set; }
    public string? ReleaseNotes { get; set; }
    public DateTime? ReleaseDate { get; set; }
}

public class FileManifest
{
    public List<ManifestFile> Files { get; set; } = new();
}

public class ManifestFile
{
    public string Path { get; set; } = string.Empty;
    public string? Crc { get; set; }
    public long Size { get; set; }
}
