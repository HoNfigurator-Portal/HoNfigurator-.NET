using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Protocol;

namespace HoNfigurator.Core.Services;

/// <summary>
/// NEXUS-compatible replay upload status codes
/// </summary>
public enum ReplayUploadStatusCode : byte
{
    NotFound = 0x01,
    AlreadyUploaded = 0x02,
    InQueue = 0x03,
    Uploading = 0x04,
    HaveReplay = 0x05,
    UploadingNow = 0x06,
    UploadComplete = 0x07,
    Failed = 0x08
}

/// <summary>
/// Upload target types
/// </summary>
public enum UploadTargetType
{
    Http,
    Ftp,
    S3,
    Azure,
    Local
}

/// <summary>
/// Upload target configuration
/// </summary>
public record ReplayUploadTarget
{
    public string Name { get; init; } = "default";
    public UploadTargetType Type { get; init; } = UploadTargetType.Http;
    public string Url { get; init; } = string.Empty;
    public string? BasePath { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 0;
}

/// <summary>
/// Upload progress event args
/// </summary>
public class ReplayUploadProgressEventArgs : EventArgs
{
    public required string JobId { get; init; }
    public long BytesUploaded { get; init; }
    public long TotalBytes { get; init; }
    public double ProgressPercent => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100 : 0;
}

/// <summary>
/// Manages replay upload workflow with queuing, status tracking, and NEXUS protocol support.
/// Port of Python HoNfigurator-Central replay upload functionality with enhancements.
/// </summary>
public class ReplayUploadService : IDisposable
{
    private readonly ILogger<ReplayUploadService> _logger;
    private readonly HoNConfiguration _config;
    private readonly ConcurrentQueue<ReplayUploadJob> _uploadQueue = new();
    private readonly ConcurrentDictionary<string, ReplayUploadJob> _jobsById = new();
    private readonly ConcurrentDictionary<int, string> _matchToJobId = new();
    private readonly ConcurrentDictionary<string, ReplayUploadTarget> _targets = new();
    private readonly SemaphoreSlim _uploadSemaphore;
    private readonly HttpClient _httpClient;
    private FileSystemWatcher? _replayWatcher;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private bool _isRunning;
    private bool _disposed;
    private bool _autoUploadEnabled;

    public int QueuedCount => _uploadQueue.Count;
    public bool IsRunning => _isRunning;
    public bool AutoUploadEnabled 
    { 
        get => _autoUploadEnabled;
        set
        {
            _autoUploadEnabled = value;
            _logger.LogInformation("Auto-upload {Status}", value ? "enabled" : "disabled");
        }
    }

    /// <summary>
    /// Event raised when upload status changes
    /// </summary>
    public event EventHandler<UploadStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Event raised when upload progress updates
    /// </summary>
    public event EventHandler<ReplayUploadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when all uploads complete
    /// </summary>
    public event EventHandler? QueueEmpty;

    public ReplayUploadService(ILogger<ReplayUploadService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        // Allow 3 concurrent uploads by default
        _uploadSemaphore = new SemaphoreSlim(3);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    #region Target Management

    /// <summary>
    /// Add or update an upload target
    /// </summary>
    public void AddTarget(ReplayUploadTarget target)
    {
        _targets[target.Name] = target;
        _logger.LogInformation("Added upload target: {Name} ({Type}) -> {Url}", 
            target.Name, target.Type, target.Url);
    }

    /// <summary>
    /// Configure master server upload target
    /// </summary>
    public void ConfigureMasterServer(string masterServerUrl, string? authHash = null, string? serverLogin = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/zip"
        };
        
        if (!string.IsNullOrEmpty(authHash))
            headers["X-Auth-Hash"] = authHash;
        if (!string.IsNullOrEmpty(serverLogin))
            headers["X-Server-Login"] = serverLogin;

        var target = new ReplayUploadTarget
        {
            Name = "master",
            Type = UploadTargetType.Http,
            Url = masterServerUrl.TrimEnd('/') + "/replay/upload.php",
            Headers = headers,
            Priority = 100
        };

        AddTarget(target);
    }

    /// <summary>
    /// Remove an upload target
    /// </summary>
    public void RemoveTarget(string targetName)
    {
        if (_targets.TryRemove(targetName, out _))
        {
            _logger.LogInformation("Removed upload target: {Name}", targetName);
        }
    }

    /// <summary>
    /// Get all configured targets
    /// </summary>
    public IReadOnlyList<ReplayUploadTarget> GetTargets()
    {
        return _targets.Values.OrderByDescending(t => t.Priority).ToList().AsReadOnly();
    }

    #endregion

    #region File Watching

    /// <summary>
    /// Start watching a directory for new replay files
    /// </summary>
    public void StartWatching(string replayDirectory)
    {
        StopWatching();

        if (!Directory.Exists(replayDirectory))
        {
            Directory.CreateDirectory(replayDirectory);
        }

        _replayWatcher = new FileSystemWatcher(replayDirectory)
        {
            Filter = "*.honreplay",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _replayWatcher.Created += OnReplayFileCreated;
        _logger.LogInformation("Started watching for replays in: {Directory}", replayDirectory);
    }

    /// <summary>
    /// Stop watching for new replay files
    /// </summary>
    public void StopWatching()
    {
        if (_replayWatcher != null)
        {
            _replayWatcher.EnableRaisingEvents = false;
            _replayWatcher.Created -= OnReplayFileCreated;
            _replayWatcher.Dispose();
            _replayWatcher = null;
            _logger.LogInformation("Stopped watching for replays");
        }
    }

    private void OnReplayFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!_autoUploadEnabled) return;

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(e.Name);
            if (fileName?.StartsWith("M") == true && int.TryParse(fileName[1..], out var matchId))
            {
                // Wait for file to be fully written
                Task.Delay(3000).ContinueWith(_ =>
                {
                    try
                    {
                        QueueUpload(e.FullPath, matchId);
                        _logger.LogInformation("Auto-queued replay: {FileName}", e.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-queue replay: {FileName}", e.Name);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing new replay: {FileName}", e.Name);
        }
    }

    #endregion

    #region NEXUS Protocol Status

    /// <summary>
    /// Get NEXUS-compatible upload status for a match
    /// </summary>
    public ReplayUploadStatusCode GetMatchUploadStatus(int matchId)
    {
        if (!_matchToJobId.TryGetValue(matchId, out var jobId))
            return ReplayUploadStatusCode.NotFound;

        if (!_jobsById.TryGetValue(jobId, out var job))
            return ReplayUploadStatusCode.NotFound;

        return job.Status switch
        {
            UploadStatus.Queued => ReplayUploadStatusCode.InQueue,
            UploadStatus.Uploading => ReplayUploadStatusCode.UploadingNow,
            UploadStatus.Completed => ReplayUploadStatusCode.UploadComplete,
            UploadStatus.Failed => ReplayUploadStatusCode.Failed,
            UploadStatus.Cancelled => ReplayUploadStatusCode.Failed,
            _ => ReplayUploadStatusCode.NotFound
        };
    }

    #endregion

    /// <summary>
    /// Start the upload worker
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        
        _logger.LogInformation("Replay upload service started");
    }

    /// <summary>
    /// Stop the upload worker
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning) return;

        _cts?.Cancel();
        
        if (_workerTask != null)
        {
            try
            {
                await _workerTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _isRunning = false;
        _logger.LogInformation("Replay upload service stopped");
    }

    /// <summary>
    /// Queue a replay for upload
    /// </summary>
    public string QueueUpload(string replayPath, int matchId, Dictionary<string, string>? metadata = null, string? targetName = null)
    {
        if (!File.Exists(replayPath))
            throw new FileNotFoundException($"Replay file not found: {replayPath}");

        var fileInfo = new FileInfo(replayPath);
        var job = new ReplayUploadJob
        {
            Id = Guid.NewGuid().ToString("N"),
            ReplayPath = replayPath,
            MatchId = matchId,
            Metadata = metadata ?? new Dictionary<string, string>(),
            Status = UploadStatus.Queued,
            QueuedAt = DateTime.UtcNow,
            FileSize = fileInfo.Length,
            Checksum = CalculateMd5(replayPath),
            TargetName = targetName
        };

        _jobsById[job.Id] = job;
        _matchToJobId[matchId] = job.Id;
        _uploadQueue.Enqueue(job);

        _logger.LogDebug("Queued replay upload: {Path} (Job: {JobId}, Size: {Size} bytes)", 
            replayPath, job.Id, job.FileSize);
        
        StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
        {
            JobId = job.Id,
            Status = job.Status,
            Message = "Queued for upload"
        });

        return job.Id;
    }

    /// <summary>
    /// Queue multiple replays for upload
    /// </summary>
    public IEnumerable<string> QueueUploads(IEnumerable<(string Path, int MatchId)> replays)
    {
        var jobIds = new List<string>();

        foreach (var (path, matchId) in replays)
        {
            var jobId = QueueUpload(path, matchId);
            jobIds.Add(jobId);
        }

        return jobIds;
    }

    /// <summary>
    /// Get status of a specific job
    /// </summary>
    public ReplayUploadJob? GetJobStatus(string jobId)
    {
        return _jobsById.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Get all jobs with their statuses
    /// </summary>
    public IReadOnlyDictionary<string, ReplayUploadJob> GetAllJobs()
    {
        return _jobsById.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Get jobs filtered by status
    /// </summary>
    public IEnumerable<ReplayUploadJob> GetJobsByStatus(UploadStatus status)
    {
        return _jobsById.Values.Where(j => j.Status == status);
    }

    /// <summary>
    /// Get a specific job by ID
    /// </summary>
    public ReplayUploadJob? GetJob(string jobId)
    {
        return _jobsById.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Get all queued jobs
    /// </summary>
    public IReadOnlyList<ReplayUploadJob> GetQueuedJobs()
    {
        return _jobsById.Values.Where(j => j.Status == UploadStatus.Queued).ToList().AsReadOnly();
    }

    /// <summary>
    /// Cancel all queued uploads
    /// </summary>
    public int CancelAll()
    {
        var cancelled = 0;
        foreach (var job in _jobsById.Values.Where(j => j.Status == UploadStatus.Queued))
        {
            job.Status = UploadStatus.Cancelled;
            cancelled++;
            
            StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
            {
                JobId = job.Id,
                Status = job.Status,
                Message = "Upload cancelled"
            });
        }
        return cancelled;
    }

    /// <summary>
    /// Start the upload worker (async version)
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the upload worker (with cancellation token)
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return StopAsync();
    }

    /// <summary>
    /// Retry a failed upload
    /// </summary>
    public bool RetryUpload(string jobId)
    {
        if (!_jobsById.TryGetValue(jobId, out var job))
            return false;

        if (job.Status != UploadStatus.Failed)
            return false;

        job.Status = UploadStatus.Queued;
        job.RetryCount++;
        job.Error = null;
        
        _uploadQueue.Enqueue(job);

        _logger.LogDebug("Retrying upload: {JobId} (Attempt {Attempt})", jobId, job.RetryCount + 1);
        
        StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
        {
            JobId = job.Id,
            Status = job.Status,
            Message = $"Retry attempt {job.RetryCount + 1}"
        });

        return true;
    }

    /// <summary>
    /// Cancel a queued upload
    /// </summary>
    public bool CancelUpload(string jobId)
    {
        if (!_jobsById.TryGetValue(jobId, out var job))
            return false;

        if (job.Status != UploadStatus.Queued)
            return false;

        job.Status = UploadStatus.Cancelled;
        
        StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
        {
            JobId = job.Id,
            Status = job.Status,
            Message = "Upload cancelled"
        });

        return true;
    }

    /// <summary>
    /// Clear completed and failed jobs from history
    /// </summary>
    public int ClearCompletedJobs()
    {
        var toRemove = _jobsById.Values
            .Where(j => j.Status == UploadStatus.Completed || 
                        j.Status == UploadStatus.Failed ||
                        j.Status == UploadStatus.Cancelled)
            .Select(j => j.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            if (_jobsById.TryRemove(id, out var job))
            {
                _matchToJobId.TryRemove(job.MatchId, out _);
            }
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Get upload statistics
    /// </summary>
    public ReplayUploadStats GetStats()
    {
        var jobs = _jobsById.Values.ToList();
        var completed = jobs.Where(j => j.Status == UploadStatus.Completed).ToList();
        
        return new ReplayUploadStats
        {
            TotalQueued = jobs.Count(j => j.Status == UploadStatus.Queued),
            TotalUploading = jobs.Count(j => j.Status == UploadStatus.Uploading),
            TotalCompleted = completed.Count,
            TotalFailed = jobs.Count(j => j.Status == UploadStatus.Failed),
            TotalBytesUploaded = completed.Sum(j => j.BytesUploaded),
            AverageUploadTime = completed.Any() 
                ? TimeSpan.FromTicks((long)completed.Where(j => j.Duration.HasValue).Average(j => j.Duration!.Value.Ticks))
                : TimeSpan.Zero
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopWatching();
        _cts?.Cancel();
        _cts?.Dispose();
        _uploadSemaphore.Dispose();
        _httpClient.Dispose();
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_uploadQueue.TryDequeue(out var job))
                {
                    // Check if cancelled
                    if (job.Status == UploadStatus.Cancelled)
                        continue;

                    await _uploadSemaphore.WaitAsync(cancellationToken);
                    
                    try
                    {
                        await ProcessJobAsync(job, cancellationToken);
                    }
                    finally
                    {
                        _uploadSemaphore.Release();
                    }
                }
                else
                {
                    // Check if queue is empty
                    if (_jobsById.Values.All(j => 
                        j.Status != UploadStatus.Queued && 
                        j.Status != UploadStatus.Uploading))
                    {
                        QueueEmpty?.Invoke(this, EventArgs.Empty);
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in upload queue processor");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ProcessJobAsync(ReplayUploadJob job, CancellationToken cancellationToken)
    {
        job.Status = UploadStatus.Uploading;
        job.StartedAt = DateTime.UtcNow;

        StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
        {
            JobId = job.Id,
            Status = job.Status,
            Message = "Upload started"
        });

        try
        {
            // Validate file exists
            if (!File.Exists(job.ReplayPath))
            {
                throw new FileNotFoundException($"Replay file not found: {job.ReplayPath}");
            }

            // Get target
            var target = GetTargetForJob(job);
            if (target == null)
            {
                // Fall back to config-based upload
                var settings = _config.ApplicationData?.ReplayUpload;
                if (settings == null || !settings.Enabled)
                {
                    throw new InvalidOperationException("Replay upload is not configured");
                }
                await ProcessLegacyUploadAsync(job, settings, cancellationToken);
                return;
            }

            // Process based on target type
            switch (target.Type)
            {
                case UploadTargetType.Http:
                    await UploadHttpAsync(job, target, cancellationToken);
                    break;
                case UploadTargetType.Ftp:
                    await UploadFtpAsync(job, target, cancellationToken);
                    break;
                case UploadTargetType.S3:
                case UploadTargetType.Azure:
                    await UploadCloudAsync(job, target, cancellationToken);
                    break;
                case UploadTargetType.Local:
                    await UploadLocalAsync(job, target, cancellationToken);
                    break;
            }

            // Success
            job.Status = UploadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Replay uploaded successfully: {Path} -> {Url}", 
                job.ReplayPath, job.ResultUrl);

            StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
            {
                JobId = job.Id,
                Status = job.Status,
                Message = "Upload completed",
                ResultUrl = job.ResultUrl
            });
        }
        catch (Exception ex)
        {
            job.Status = UploadStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTime.UtcNow;

            _logger.LogWarning(ex, "Failed to upload replay: {Path}", job.ReplayPath);

            StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
            {
                JobId = job.Id,
                Status = job.Status,
                Message = $"Upload failed: {ex.Message}"
            });

            // Auto-retry if configured
            var maxRetries = _config.ApplicationData?.ReplayUpload?.RetryCount ?? 3;
            if (job.RetryCount < maxRetries)
            {
                await Task.Delay(
                    (_config.ApplicationData?.ReplayUpload?.RetryDelaySeconds ?? 5) * 1000, 
                    cancellationToken);
                RetryUpload(job.Id);
            }
        }
    }

    private ReplayUploadTarget? GetTargetForJob(ReplayUploadJob job)
    {
        if (!string.IsNullOrEmpty(job.TargetName) && _targets.TryGetValue(job.TargetName, out var specific))
        {
            return specific.Enabled ? specific : null;
        }

        return _targets.Values
            .Where(t => t.Enabled)
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault();
    }

    private async Task UploadHttpAsync(ReplayUploadJob job, ReplayUploadTarget target, CancellationToken cancellationToken)
    {
        var fileBytes = await File.ReadAllBytesAsync(job.ReplayPath, cancellationToken);
        
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(job.ReplayPath));

        // Add metadata
        content.Add(new StringContent(job.MatchId.ToString()), "match_id");
        content.Add(new StringContent(job.Checksum ?? ""), "checksum");
        content.Add(new StringContent(job.FileSize.ToString()), "size");
        
        foreach (var (key, value) in job.Metadata)
        {
            content.Add(new StringContent(value), key);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
        {
            Content = content
        };

        foreach (var header in target.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (!string.IsNullOrEmpty(target.Username))
        {
            var auth = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{target.Username}:{target.Password ?? ""}"));
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP upload failed: {response.StatusCode} - {responseText}");
        }

        job.ResultUrl = ExtractUrlFromResponse(responseText) ?? 
            $"{target.Url.Replace("/upload.php", "")}/M{job.MatchId}.honreplay";
        job.BytesUploaded = fileBytes.Length;
        
        ReportProgress(job, fileBytes.Length, fileBytes.Length);
    }

    private async Task UploadFtpAsync(ReplayUploadJob job, ReplayUploadTarget target, CancellationToken cancellationToken)
    {
        var uri = new Uri(target.Url);
        var fileName = Path.GetFileName(job.ReplayPath);
        var ftpPath = $"{target.BasePath?.TrimEnd('/') ?? ""}/{fileName}";
        var ftpUri = new Uri($"ftp://{uri.Host}{ftpPath}");

        var request = (System.Net.FtpWebRequest)System.Net.WebRequest.Create(ftpUri);
        request.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new System.Net.NetworkCredential(target.Username, target.Password);
        request.UseBinary = true;
        request.UsePassive = true;

        var fileData = await File.ReadAllBytesAsync(job.ReplayPath, cancellationToken);
        request.ContentLength = fileData.Length;

        using (var requestStream = await request.GetRequestStreamAsync())
        {
            const int chunkSize = 65536;
            var totalSent = 0;

            while (totalSent < fileData.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var remaining = fileData.Length - totalSent;
                var toSend = Math.Min(chunkSize, remaining);
                
                await requestStream.WriteAsync(fileData.AsMemory(totalSent, toSend), cancellationToken);
                totalSent += toSend;
                
                job.BytesUploaded = totalSent;
                ReportProgress(job, totalSent, fileData.Length);
            }
        }

        using var response = (System.Net.FtpWebResponse)await request.GetResponseAsync();
        
        if (response.StatusCode != System.Net.FtpStatusCode.ClosingData)
        {
            throw new InvalidOperationException($"FTP upload failed: {response.StatusDescription}");
        }

        job.ResultUrl = ftpUri.ToString();
    }

    private async Task UploadCloudAsync(ReplayUploadJob job, ReplayUploadTarget target, CancellationToken cancellationToken)
    {
        var fileBytes = await File.ReadAllBytesAsync(job.ReplayPath, cancellationToken);
        
        using var content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

        foreach (var header in target.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var uploadUrl = $"{target.Url.TrimEnd('/')}/{target.BasePath?.Trim('/') ?? ""}/M{job.MatchId}.honreplay";
        var response = await _httpClient.PutAsync(uploadUrl, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Cloud upload failed: {response.StatusCode} - {responseText}");
        }

        job.ResultUrl = uploadUrl;
        job.BytesUploaded = fileBytes.Length;
    }

    private async Task UploadLocalAsync(ReplayUploadJob job, ReplayUploadTarget target, CancellationToken cancellationToken)
    {
        var destDir = target.BasePath ?? target.Url;
        Directory.CreateDirectory(destDir);
        
        var destPath = Path.Combine(destDir, $"M{job.MatchId}.honreplay");
        
        await using var source = File.OpenRead(job.ReplayPath);
        await using var dest = File.Create(destPath);
        
        var buffer = new byte[81920];
        int bytesRead;
        long totalBytes = 0;
        
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytes += bytesRead;
            job.BytesUploaded = totalBytes;
            ReportProgress(job, totalBytes, job.FileSize);
        }

        job.ResultUrl = destPath;
    }

    private async Task ProcessLegacyUploadAsync(ReplayUploadJob job, ReplayUploadSettings settings, CancellationToken cancellationToken)
    {
        var uploadUrl = GetUploadUrl(settings, job);
        
        try
        {
            // Read file
            var fileBytes = await File.ReadAllBytesAsync(job.ReplayPath, cancellationToken);
            job.FileSize = fileBytes.Length;

            // Create multipart content
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "replay", Path.GetFileName(job.ReplayPath));

            // Add metadata
            content.Add(new StringContent(job.MatchId.ToString()), "match_id");
            content.Add(new StringContent(job.Checksum ?? ""), "checksum");
            foreach (var (key, value) in job.Metadata)
            {
                content.Add(new StringContent(value), key);
            }

            // Upload
            var response = await _httpClient.PostAsync(uploadUrl, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Upload failed: {response.StatusCode} - {errorBody}");
            }

            // Success
            job.Status = UploadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ResultUrl = await GetResultUrlAsync(response, settings);
            job.BytesUploaded = fileBytes.Length;

            _logger.LogInformation("Replay uploaded successfully: {Path} -> {Url}", 
                job.ReplayPath, job.ResultUrl);

            StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
            {
                JobId = job.Id,
                Status = job.Status,
                Message = "Upload completed",
                ResultUrl = job.ResultUrl
            });
        }
        catch (Exception ex)
        {
            job.Status = UploadStatus.Failed;
            job.Error = ex.Message;
            job.CompletedAt = DateTime.UtcNow;

            _logger.LogWarning(ex, "Failed to upload replay: {Path}", job.ReplayPath);

            StatusChanged?.Invoke(this, new UploadStatusChangedEventArgs
            {
                JobId = job.Id,
                Status = job.Status,
                Message = $"Upload failed: {ex.Message}"
            });

            // Auto-retry
            var maxRetries = settings.RetryCount;
            if (job.RetryCount < maxRetries)
            {
                await Task.Delay(settings.RetryDelaySeconds * 1000, cancellationToken);
                RetryUpload(job.Id);
            }
        }
    }

    private void ReportProgress(ReplayUploadJob job, long bytesUploaded, long totalBytes)
    {
        ProgressChanged?.Invoke(this, new ReplayUploadProgressEventArgs
        {
            JobId = job.Id,
            BytesUploaded = bytesUploaded,
            TotalBytes = totalBytes
        });
    }

    private static string? ExtractUrlFromResponse(string response)
    {
        // Try JSON format
        var urlMatch = System.Text.RegularExpressions.Regex.Match(
            response, @"""(?:url|replay_url)""\s*:\s*""([^""]+)""");
        if (urlMatch.Success)
            return urlMatch.Groups[1].Value;

        if (response.StartsWith("http"))
            return response.Trim();

        return null;
    }

    private static string CalculateMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private string GetUploadUrl(ReplayUploadSettings settings, ReplayUploadJob job)
    {
        return settings.Provider.ToLowerInvariant() switch
        {
            "azure" => $"{settings.BaseUrl}/{settings.ContainerName}/{job.MatchId}.honreplay",
            "s3" => $"{settings.BaseUrl}/{settings.ContainerName}/{job.MatchId}.honreplay",
            "local" => Path.Combine(settings.BasePath, $"{job.MatchId}.honreplay"),
            _ => $"{settings.BaseUrl}/{settings.BasePath}"
        };
    }

    private async Task<string?> GetResultUrlAsync(HttpResponseMessage response, ReplayUploadSettings settings)
    {
        try
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            
            // Try to parse URL from response
            if (responseBody.StartsWith("http"))
                return responseBody.Trim();

            // Generate URL based on provider
            return settings.Provider.ToLowerInvariant() switch
            {
                "azure" => $"{settings.BaseUrl}/{settings.ContainerName}",
                "s3" => $"{settings.BaseUrl}/{settings.ContainerName}",
                _ => settings.BaseUrl
            };
        }
        catch
        {
            return null;
        }
    }
}

// Enums and DTOs

public enum UploadStatus
{
    Queued,
    Uploading,
    Completed,
    Failed,
    Cancelled
}

public class ReplayUploadJob
{
    public string Id { get; set; } = string.Empty;
    public string ReplayPath { get; set; } = string.Empty;
    public int MatchId { get; set; }
    public UploadStatus Status { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public string? ResultUrl { get; set; }
    public int RetryCount { get; set; }
    public long FileSize { get; set; }
    public long BytesUploaded { get; set; }
    public string? Checksum { get; set; }
    public string? TargetName { get; set; }
    
    /// <summary>Progress percentage (0-100)</summary>
    public double Progress => FileSize > 0 ? (double)BytesUploaded / FileSize * 100 : 0;
    
    /// <summary>Upload duration</summary>
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue 
        ? CompletedAt.Value - StartedAt.Value 
        : null;
}

public class UploadStatusChangedEventArgs : EventArgs
{
    public string JobId { get; set; } = string.Empty;
    public UploadStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ResultUrl { get; set; }
}

/// <summary>
/// Statistics for replay uploads
/// </summary>
public class ReplayUploadStats
{
    public int TotalQueued { get; set; }
    public int TotalUploading { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalFailed { get; set; }
    public long TotalBytesUploaded { get; set; }
    public TimeSpan AverageUploadTime { get; set; }
}
