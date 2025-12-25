using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages configuration hot-reload functionality.
/// Port of Python HoNfigurator-Central config watcher.
/// </summary>
public class ConfigHotReloadService : BackgroundService
{
    private readonly ILogger<ConfigHotReloadService> _logger;
    private readonly IConfigurationService _configService;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastReloadTimes = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private readonly SemaphoreSlim _reloadLock = new(1);

    public event EventHandler<ConfigReloadedEventArgs>? ConfigurationReloaded;
    
    public bool IsEnabled { get; set; } = true;

    public ConfigHotReloadService(
        ILogger<ConfigHotReloadService> logger,
        IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Configuration hot-reload service starting");

        try
        {
            // Setup watchers for config files
            SetupWatchers();

            // Keep running until cancelled
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            CleanupWatchers();
            _logger.LogInformation("Configuration hot-reload service stopped");
        }
    }

    /// <summary>
    /// Watch a specific file for changes
    /// </summary>
    public void WatchFile(string filePath)
    {
        if (_watchers.ContainsKey(filePath))
            return;

        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            _logger.LogWarning("Cannot watch file, directory does not exist: {Path}", filePath);
            return;
        }

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Changed += async (_, e) => await OnFileChangedAsync(e.FullPath);
        watcher.Created += async (_, e) => await OnFileChangedAsync(e.FullPath);

        _watchers[filePath] = watcher;
        _logger.LogDebug("Now watching: {Path}", filePath);
    }

    /// <summary>
    /// Watch a directory for changes
    /// </summary>
    public void WatchDirectory(string directoryPath, string filter = "*.*", bool includeSubdirectories = false)
    {
        if (_watchers.ContainsKey(directoryPath))
            return;

        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Cannot watch directory, does not exist: {Path}", directoryPath);
            return;
        }

        var watcher = new FileSystemWatcher(directoryPath, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = includeSubdirectories,
            EnableRaisingEvents = true
        };

        watcher.Changed += async (_, e) => await OnFileChangedAsync(e.FullPath);
        watcher.Created += async (_, e) => await OnFileChangedAsync(e.FullPath);
        watcher.Deleted += async (_, e) => await OnFileDeletedAsync(e.FullPath);
        watcher.Renamed += async (_, e) => await OnFileRenamedAsync(e.OldFullPath, e.FullPath);

        _watchers[directoryPath] = watcher;
        _logger.LogDebug("Now watching directory: {Path}", directoryPath);
    }

    /// <summary>
    /// Stop watching a file or directory
    /// </summary>
    public void StopWatching(string path)
    {
        if (_watchers.TryRemove(path, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger.LogDebug("Stopped watching: {Path}", path);
        }
    }

    /// <summary>
    /// Manually trigger a reload
    /// </summary>
    public async Task TriggerReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogInformation("Manual configuration reload triggered");
            await _configService.ReloadAsync(cancellationToken);
            
            ConfigurationReloaded?.Invoke(this, new ConfigReloadedEventArgs
            {
                ReloadTime = DateTime.UtcNow,
                Source = "Manual",
                Success = true
            });
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Get list of watched paths
    /// </summary>
    public IEnumerable<WatchedPathInfo> GetWatchedPaths()
    {
        return _watchers.Select(kvp => new WatchedPathInfo
        {
            Path = kvp.Key,
            IsDirectory = Directory.Exists(kvp.Key),
            IsEnabled = kvp.Value.EnableRaisingEvents
        }).ToList();
    }

    private void SetupWatchers()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator");

        // Watch main config
        var configPath = Path.Combine(appDataPath, "config.json");
        if (File.Exists(configPath))
        {
            WatchFile(configPath);
        }

        // Watch servers directory
        var serversPath = Path.Combine(appDataPath, "servers");
        if (Directory.Exists(serversPath))
        {
            WatchDirectory(serversPath, "*.json", includeSubdirectories: true);
        }

        // Watch bans file
        var bansPath = Path.Combine(appDataPath, "bans.json");
        if (File.Exists(bansPath))
        {
            WatchFile(bansPath);
        }
    }

    private void CleanupWatchers()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private async Task OnFileChangedAsync(string filePath)
    {
        if (!IsEnabled)
            return;

        // Debounce rapid changes
        if (_lastReloadTimes.TryGetValue(filePath, out var lastReload))
        {
            if (DateTime.UtcNow - lastReload < _debounceInterval)
                return;
        }

        _lastReloadTimes[filePath] = DateTime.UtcNow;

        await _reloadLock.WaitAsync();
        
        try
        {
            _logger.LogInformation("Configuration file changed: {Path}", filePath);
            
            // Wait a bit for file to be fully written
            await Task.Delay(100);

            await _configService.ReloadAsync();

            ConfigurationReloaded?.Invoke(this, new ConfigReloadedEventArgs
            {
                ReloadTime = DateTime.UtcNow,
                Source = filePath,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload configuration after file change: {Path}", filePath);
            
            ConfigurationReloaded?.Invoke(this, new ConfigReloadedEventArgs
            {
                ReloadTime = DateTime.UtcNow,
                Source = filePath,
                Success = false,
                Error = ex.Message
            });
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private async Task OnFileDeletedAsync(string filePath)
    {
        _logger.LogWarning("Configuration file deleted: {Path}", filePath);
        
        // Trigger reload to reflect deletion
        await OnFileChangedAsync(filePath);
    }

    private async Task OnFileRenamedAsync(string oldPath, string newPath)
    {
        _logger.LogInformation("Configuration file renamed: {OldPath} -> {NewPath}", oldPath, newPath);
        
        // Trigger reload
        await OnFileChangedAsync(newPath);
    }
}

// DTOs

public class ConfigReloadedEventArgs : EventArgs
{
    public DateTime ReloadTime { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class WatchedPathInfo
{
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsEnabled { get; set; }
}
