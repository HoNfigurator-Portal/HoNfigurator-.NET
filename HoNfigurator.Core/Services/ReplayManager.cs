using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages replay files - storage, retrieval, cleanup, and master server upload.
/// Port of Python's upload_replay_file functionality.
/// </summary>
public class ReplayManager
{
    private readonly ILogger<ReplayManager> _logger;
    private readonly string _replaysPath;
    private readonly string _archivePath;
    private readonly string _pendingUploadsPath;
    private readonly HttpClient _httpClient;
    private readonly Queue<PendingReplayUpload> _pendingUploads = new();
    private readonly object _uploadLock = new();
    private string? _masterServer;
    private string? _authHash;
    private string? _serverLogin;

    private const int MaxUploadRetries = 3;

    public ReplayManager(ILogger<ReplayManager> logger, string replaysPath = "replays")
    {
        _logger = logger;
        _replaysPath = replaysPath;
        _archivePath = Path.Combine(replaysPath, "archive");
        _pendingUploadsPath = Path.Combine(replaysPath, "pending_uploads.json");
        
        Directory.CreateDirectory(_replaysPath);
        Directory.CreateDirectory(_archivePath);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        LoadPendingUploads();
    }

    /// <summary>
    /// Configure master server settings for replay uploads
    /// </summary>
    public void Configure(string masterServer, string? authHash = null, string? serverLogin = null)
    {
        _masterServer = masterServer;
        _authHash = authHash;
        _serverLogin = serverLogin;
    }

    public List<ReplayInfo> GetReplays(int limit = 100, int offset = 0)
    {
        var replays = new List<ReplayInfo>();
        
        if (!Directory.Exists(_replaysPath))
            return replays;

        var files = Directory.GetFiles(_replaysPath, "*.honreplay")
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .Skip(offset)
            .Take(limit);

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            replays.Add(new ReplayInfo
            {
                FileName = info.Name,
                FilePath = file,
                SizeBytes = info.Length,
                SizeMb = Math.Round(info.Length / 1024.0 / 1024.0, 2),
                CreatedAt = info.CreationTimeUtc,
                MatchId = ExtractMatchId(info.Name)
            });
        }

        return replays;
    }

    public ReplayInfo? GetReplay(string fileName)
    {
        var path = Path.Combine(_replaysPath, fileName);
        if (!File.Exists(path))
            return null;

        var info = new FileInfo(path);
        return new ReplayInfo
        {
            FileName = info.Name,
            FilePath = path,
            SizeBytes = info.Length,
            SizeMb = Math.Round(info.Length / 1024.0 / 1024.0, 2),
            CreatedAt = info.CreationTimeUtc,
            MatchId = ExtractMatchId(info.Name)
        };
    }

    public byte[]? GetReplayData(string fileName)
    {
        var path = Path.Combine(_replaysPath, fileName);
        if (!File.Exists(path))
            return null;

        return File.ReadAllBytes(path);
    }

    public async Task<string?> SaveReplayAsync(long matchId, byte[] data)
    {
        try
        {
            var fileName = $"M{matchId}.honreplay";
            var path = Path.Combine(_replaysPath, fileName);
            
            await File.WriteAllBytesAsync(path, data);
            _logger.LogInformation("Saved replay for match {MatchId}: {FileName}", matchId, fileName);
            
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save replay for match {MatchId}", matchId);
            return null;
        }
    }

    public bool DeleteReplay(string fileName)
    {
        try
        {
            var path = Path.Combine(_replaysPath, fileName);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            _logger.LogInformation("Deleted replay: {FileName}", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete replay: {FileName}", fileName);
            return false;
        }
    }

    public async Task<int> ArchiveOldReplaysAsync(int daysOld = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var archived = 0;

        try
        {
            var oldFiles = Directory.GetFiles(_replaysPath, "*.honreplay")
                .Where(f => File.GetCreationTimeUtc(f) < cutoff);

            foreach (var file in oldFiles)
            {
                var fileName = Path.GetFileName(file);
                var archiveName = Path.Combine(_archivePath, fileName + ".gz");

                await using var input = File.OpenRead(file);
                await using var output = File.Create(archiveName);
                await using var gzip = new GZipStream(output, CompressionLevel.Optimal);
                await input.CopyToAsync(gzip);

                File.Delete(file);
                archived++;
            }

            if (archived > 0)
                _logger.LogInformation("Archived {Count} old replays", archived);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving old replays");
        }

        return archived;
    }

    public async Task<int> CleanupOldReplaysAsync(int daysOld = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var deleted = 0;

        try
        {
            // Clean archived replays
            if (Directory.Exists(_archivePath))
            {
                foreach (var file in Directory.GetFiles(_archivePath, "*.gz"))
                {
                    if (File.GetCreationTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
            }

            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} old archived replays", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old replays");
        }

        return deleted;
    }

    public ReplayStats GetStats()
    {
        var stats = new ReplayStats();

        if (Directory.Exists(_replaysPath))
        {
            var files = Directory.GetFiles(_replaysPath, "*.honreplay");
            stats.TotalReplays = files.Length;
            stats.TotalSizeBytes = files.Sum(f => new FileInfo(f).Length);
            stats.TotalSizeMb = Math.Round(stats.TotalSizeBytes / 1024.0 / 1024.0, 2);

            if (files.Any())
            {
                stats.OldestReplay = files.Min(f => File.GetCreationTimeUtc(f));
                stats.NewestReplay = files.Max(f => File.GetCreationTimeUtc(f));
            }
        }

        if (Directory.Exists(_archivePath))
        {
            var archivedFiles = Directory.GetFiles(_archivePath, "*.gz");
            stats.ArchivedReplays = archivedFiles.Length;
            stats.ArchivedSizeBytes = archivedFiles.Sum(f => new FileInfo(f).Length);
            stats.ArchivedSizeMb = Math.Round(stats.ArchivedSizeBytes / 1024.0 / 1024.0, 2);
        }

        return stats;
    }

    private long ExtractMatchId(string fileName)
    {
        // Format: M123456.honreplay
        if (fileName.StartsWith("M") && fileName.EndsWith(".honreplay"))
        {
            var idPart = fileName[1..^10];
            if (long.TryParse(idPart, out var matchId))
                return matchId;
        }
        return 0;
    }

    #region Master Server Upload

    /// <summary>
    /// Queue a replay for upload to the master server
    /// </summary>
    public void QueueUpload(string fileName, long matchId)
    {
        var filePath = Path.Combine(_replaysPath, fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Cannot queue replay - file not found: {FileName}", fileName);
            return;
        }

        lock (_uploadLock)
        {
            _pendingUploads.Enqueue(new PendingReplayUpload
            {
                FilePath = filePath,
                FileName = fileName,
                MatchId = matchId,
                QueuedAt = DateTime.UtcNow,
                RetryCount = 0
            });
            SavePendingUploads();
        }

        _logger.LogInformation("Queued replay for master server upload: {FileName}", fileName);
    }

    /// <summary>
    /// Upload a replay file to the master server
    /// </summary>
    public async Task<ReplayUploadResult> UploadToMasterAsync(string fileName, long matchId)
    {
        var result = new ReplayUploadResult { MatchId = matchId, FileName = fileName };
        var startTime = DateTime.UtcNow;

        if (string.IsNullOrEmpty(_masterServer))
        {
            result.Error = "Master server not configured";
            return result;
        }

        var filePath = Path.Combine(_replaysPath, fileName);
        if (!File.Exists(filePath))
        {
            result.Error = $"Replay file not found: {fileName}";
            return result;
        }

        try
        {
            var replayData = await File.ReadAllBytesAsync(filePath);
            var uploadUrl = $"https://{_masterServer}/replay/upload.php";

            _logger.LogInformation("Uploading replay {FileName} ({Size} bytes) for match {MatchId}",
                fileName, replayData.Length, matchId);

            // Calculate checksum like Python version
            using var md5 = MD5.Create();
            var checksum = BitConverter.ToString(md5.ComputeHash(replayData))
                .Replace("-", "").ToLowerInvariant();

            // Build multipart form data
            using var content = new MultipartFormDataContent();

            var fileContent = new ByteArrayContent(replayData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            content.Add(new StringContent(matchId.ToString()), "match_id");
            content.Add(new StringContent(checksum), "checksum");
            content.Add(new StringContent(replayData.Length.ToString()), "size");
            
            if (!string.IsNullOrEmpty(_serverLogin))
                content.Add(new StringContent(_serverLogin), "server_login");
            
            if (!string.IsNullOrEmpty(_authHash))
                content.Add(new StringContent(_authHash), "auth_hash");

            var response = await _httpClient.PostAsync(uploadUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && !responseText.Contains("error"))
            {
                result.Success = true;
                result.BytesUploaded = replayData.Length;

                // Try to parse replay URL from response
                var urlMatch = System.Text.RegularExpressions.Regex.Match(
                    responseText, @"""url""\s*:\s*""([^""]+)""");
                if (urlMatch.Success)
                {
                    result.ReplayUrl = urlMatch.Groups[1].Value;
                }

                _logger.LogInformation("Successfully uploaded replay {FileName} for match {MatchId}",
                    fileName, matchId);
            }
            else
            {
                result.Error = $"Upload failed: {responseText}";
                _logger.LogWarning("Failed to upload replay {FileName}: {Response}", fileName, responseText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading replay {FileName}", fileName);
            result.Error = ex.Message;
        }

        result.Duration = DateTime.UtcNow - startTime;
        return result;
    }

    /// <summary>
    /// Process all pending replay uploads
    /// </summary>
    public async Task<BatchReplayUploadResult> ProcessPendingUploadsAsync()
    {
        var result = new BatchReplayUploadResult();
        var toProcess = new List<PendingReplayUpload>();

        lock (_uploadLock)
        {
            while (_pendingUploads.Count > 0)
            {
                toProcess.Add(_pendingUploads.Dequeue());
            }
        }

        if (toProcess.Count == 0)
        {
            _logger.LogDebug("No pending replay uploads");
            result.Success = true;
            return result;
        }

        _logger.LogInformation("Processing {Count} pending replay uploads", toProcess.Count);

        var failedUploads = new List<PendingReplayUpload>();

        foreach (var upload in toProcess)
        {
            var uploadResult = await UploadToMasterAsync(upload.FileName, upload.MatchId);
            result.Results.Add(uploadResult);

            if (uploadResult.Success)
            {
                result.Uploaded++;
            }
            else
            {
                result.Failed++;

                if (upload.RetryCount < MaxUploadRetries)
                {
                    upload.RetryCount++;
                    failedUploads.Add(upload);
                }
                else
                {
                    _logger.LogWarning("Giving up on replay {FileName} after {Retries} retries",
                        upload.FileName, MaxUploadRetries);
                }
            }

            await Task.Delay(1000); // Delay between uploads
        }

        lock (_uploadLock)
        {
            foreach (var upload in failedUploads)
            {
                _pendingUploads.Enqueue(upload);
            }
            SavePendingUploads();
        }

        result.Success = result.Failed == 0;
        _logger.LogInformation("Replay upload complete: {Uploaded} uploaded, {Failed} failed",
            result.Uploaded, result.Failed);

        return result;
    }

    public int GetPendingUploadCount()
    {
        lock (_uploadLock)
        {
            return _pendingUploads.Count;
        }
    }

    private void LoadPendingUploads()
    {
        try
        {
            if (File.Exists(_pendingUploadsPath))
            {
                var json = File.ReadAllText(_pendingUploadsPath);
                var uploads = System.Text.Json.JsonSerializer.Deserialize<List<PendingReplayUpload>>(json);

                if (uploads != null)
                {
                    foreach (var upload in uploads)
                    {
                        if (File.Exists(upload.FilePath))
                        {
                            _pendingUploads.Enqueue(upload);
                        }
                    }
                    _logger.LogInformation("Loaded {Count} pending replay uploads", _pendingUploads.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pending replay uploads");
        }
    }

    private void SavePendingUploads()
    {
        try
        {
            var uploads = _pendingUploads.ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(uploads,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_pendingUploadsPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save pending replay uploads");
        }
    }

    #endregion
}

public class ReplayInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public double SizeMb { get; set; }
    public DateTime CreatedAt { get; set; }
    public long MatchId { get; set; }
}

public class ReplayStats
{
    public int TotalReplays { get; set; }
    public long TotalSizeBytes { get; set; }
    public double TotalSizeMb { get; set; }
    public int ArchivedReplays { get; set; }
    public long ArchivedSizeBytes { get; set; }
    public double ArchivedSizeMb { get; set; }
    public DateTime? OldestReplay { get; set; }
    public DateTime? NewestReplay { get; set; }
}

public class ReplayUploadResult
{
    public bool Success { get; set; }
    public long MatchId { get; set; }
    public string FileName { get; set; } = "";
    public string? ReplayUrl { get; set; }
    public string? Error { get; set; }
    public long BytesUploaded { get; set; }
    public TimeSpan Duration { get; set; }
}

public class BatchReplayUploadResult
{
    public bool Success { get; set; }
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public List<ReplayUploadResult> Results { get; set; } = new();
}

public class PendingReplayUpload
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long MatchId { get; set; }
    public DateTime QueuedAt { get; set; }
    public int RetryCount { get; set; }
}
