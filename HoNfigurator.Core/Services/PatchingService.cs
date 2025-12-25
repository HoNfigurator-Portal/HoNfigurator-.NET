using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Service for checking and applying HoN game patches from the master server.
/// </summary>
public interface IPatchingService
{
    bool IsPatching { get; }
    string? CurrentVersion { get; }
    string? LatestVersion { get; }
    
    Task<PatchCheckResult> CheckForUpdatesAsync();
    Task<PatchResult> ApplyPatchAsync(string patchUrl, IProgress<PatchProgress>? progress = null);
    string? GetLocalVersion();
}

public class PatchCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public string? PatchUrl { get; set; }
    public long PatchSize { get; set; }
    public string? Checksum { get; set; }
    public string? Error { get; set; }
}

public class PatchResult
{
    public bool Success { get; set; }
    public string? NewVersion { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}

public class PatchProgress
{
    public string Stage { get; set; } = "";
    public int PercentComplete { get; set; }
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
}

public class PatchingService : IPatchingService
{
    private readonly ILogger<PatchingService> _logger;
    private readonly HoNConfiguration _config;
    private readonly HttpClient _httpClient;
    private bool _isPatching;
    private string? _currentVersion;
    private string? _latestVersion;

    public bool IsPatching => _isPatching;
    public string? CurrentVersion => _currentVersion;
    public string? LatestVersion => _latestVersion;

    public PatchingService(ILogger<PatchingService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        
        // Set user agent like Python version - use TryParseAdd to avoid format exceptions
        // Format: ProductName/Version (comment)
        var version = config.HonData?.ManVersion ?? "4.10.1";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new System.Net.Http.Headers.ProductInfoHeaderValue("HoNfigurator", version));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new System.Net.Http.Headers.ProductInfoHeaderValue("(HoN-Server-Manager)"));
        _httpClient.DefaultRequestHeaders.Add("Server-Launcher", "HoNfigurator");
        
        _currentVersion = GetLocalVersion();
    }

    public string? GetLocalVersion()
    {
        try
        {
            var installDir = _config.HonData.HonInstallDirectory;
            if (string.IsNullOrEmpty(installDir)) return null;

            // Try to read version from manifest file
            var manifestPath = Path.Combine(installDir, "manifest.xml");
            if (File.Exists(manifestPath))
            {
                var content = File.ReadAllText(manifestPath);
                // Parse version from manifest XML
                var versionMatch = System.Text.RegularExpressions.Regex.Match(
                    content, @"version=""([^""]+)""");
                if (versionMatch.Success)
                {
                    return versionMatch.Groups[1].Value;
                }
            }

            // Alternative: Check version from executable
            var exePath = FindHoNExecutable(installDir);
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                {
                    return versionInfo.FileVersion;
                }
            }

            // Fallback to config
            return _config.HonData.ManVersion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get local HoN version");
            return _config.HonData.ManVersion;
        }
    }

    private string? FindHoNExecutable(string installDir)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "hon_x64.exe", "hon.exe", "k2_x64.exe" }
            : new[] { "hon_x64-server", "hon_x64", "hon" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(installDir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public async Task<PatchCheckResult> CheckForUpdatesAsync()
    {
        var result = new PatchCheckResult
        {
            CurrentVersion = _currentVersion
        };

        try
        {
            var patchServer = _config.HonData.PatchServer ?? "api.kongor.net/patch";
            var url = $"http://{patchServer}/patcher/patcher.php";

            // Build request like Python version
            var formData = new Dictionary<string, string>
            {
                ["version"] = _currentVersion ?? "0.0.0.0",
                ["os"] = OperatingSystem.IsWindows() ? "wac" : "lac",
                ["arch"] = "x86_64"
            };

            var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Patch server returned {response.StatusCode}";
                return result;
            }

            var responseText = await response.Content.ReadAsStringAsync();
            
            // Parse PHP serialized response (simplified)
            // In production, use a proper PHP serialization library
            var parsed = ParsePatchResponse(responseText);
            
            if (parsed.TryGetValue("latest_version", out var latestVersion))
            {
                _latestVersion = latestVersion;
                result.LatestVersion = latestVersion;
                result.UpdateAvailable = latestVersion != _currentVersion;
            }

            if (parsed.TryGetValue("url", out var patchUrl))
            {
                result.PatchUrl = patchUrl;
            }

            if (parsed.TryGetValue("size", out var sizeStr) && long.TryParse(sizeStr, out var size))
            {
                result.PatchSize = size;
            }

            if (parsed.TryGetValue("checksum", out var checksum))
            {
                result.Checksum = checksum;
            }

            _logger.LogInformation("Patch check: Current={Current}, Latest={Latest}, UpdateAvailable={Update}",
                result.CurrentVersion, result.LatestVersion, result.UpdateAvailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            result.Error = ex.Message;
        }

        return result;
    }

    private Dictionary<string, string> ParsePatchResponse(string response)
    {
        // Simplified PHP serialization parser
        // Format: a:N:{s:key_len:"key";s:val_len:"value";...}
        var result = new Dictionary<string, string>();
        
        try
        {
            // Very basic parsing - in production use proper library
            var matches = System.Text.RegularExpressions.Regex.Matches(
                response, @"s:\d+:""([^""]+)"";s:\d+:""([^""]*)"";");
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    result[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        
        return result;
    }

    public async Task<PatchResult> ApplyPatchAsync(string patchUrl, IProgress<PatchProgress>? progress = null)
    {
        if (_isPatching)
        {
            return new PatchResult { Success = false, Error = "Patching already in progress" };
        }

        _isPatching = true;
        var startTime = DateTime.UtcNow;
        var result = new PatchResult();

        try
        {
            _logger.LogInformation("Starting patch download from {Url}", patchUrl);
            
            progress?.Report(new PatchProgress { Stage = "Downloading", PercentComplete = 0 });

            // Create temp directory for patch
            var tempDir = Path.Combine(Path.GetTempPath(), "HoNfigurator_Patch");
            Directory.CreateDirectory(tempDir);
            var patchFile = Path.Combine(tempDir, "patch.zip");

            // Download patch
            await DownloadFileAsync(patchUrl, patchFile, progress);

            progress?.Report(new PatchProgress { Stage = "Extracting", PercentComplete = 60 });

            // Extract patch
            var installDir = _config.HonData.HonInstallDirectory;
            if (string.IsNullOrEmpty(installDir))
            {
                result.Error = "HoN install directory not configured";
                return result;
            }

            // Backup current files
            progress?.Report(new PatchProgress { Stage = "Backing up", PercentComplete = 70 });
            await BackupCurrentFilesAsync(installDir, tempDir);

            // Extract new files
            progress?.Report(new PatchProgress { Stage = "Installing", PercentComplete = 80 });
            ZipFile.ExtractToDirectory(patchFile, installDir, true);

            // Verify installation
            progress?.Report(new PatchProgress { Stage = "Verifying", PercentComplete = 95 });
            var newVersion = GetLocalVersion();

            // Cleanup
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }

            progress?.Report(new PatchProgress { Stage = "Complete", PercentComplete = 100 });

            _currentVersion = newVersion;
            result.Success = true;
            result.NewVersion = newVersion;
            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Patch applied successfully. New version: {Version}", newVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply patch");
            result.Error = ex.Message;
        }
        finally
        {
            _isPatching = false;
        }

        return result;
    }

    private async Task DownloadFileAsync(string url, string destinationPath, IProgress<PatchProgress>? progress)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var buffer = new byte[81920];
        var bytesRead = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        int read;
        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;

            if (totalBytes > 0)
            {
                var percentComplete = (int)((bytesRead * 50) / totalBytes); // 0-50% for download
                progress?.Report(new PatchProgress
                {
                    Stage = "Downloading",
                    PercentComplete = percentComplete,
                    BytesDownloaded = bytesRead,
                    TotalBytes = totalBytes
                });
            }
        }
    }

    private async Task BackupCurrentFilesAsync(string installDir, string backupDir)
    {
        var backupPath = Path.Combine(backupDir, "backup");
        Directory.CreateDirectory(backupPath);

        // Backup critical files
        var filesToBackup = new[] { "manifest.xml", "startup.cfg" };
        
        foreach (var file in filesToBackup)
        {
            var srcPath = Path.Combine(installDir, file);
            if (File.Exists(srcPath))
            {
                var destPath = Path.Combine(backupPath, file);
                File.Copy(srcPath, destPath, true);
            }
        }

        await Task.CompletedTask;
    }
}
