using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.GameServer.Services;

/// <summary>
/// Service for managing HoN Proxy processes.
/// Each game server that has proxy enabled needs its own proxy process running.
/// The proxy redirects traffic from public ports to internal game server ports.
/// </summary>
public interface IProxyService
{
    /// <summary>
    /// Start proxy for a game server instance.
    /// </summary>
    Task StartProxyAsync(GameServerInstance instance);
    
    /// <summary>
    /// Stop proxy for a game server instance.
    /// </summary>
    void StopProxy(int serverId);
    
    /// <summary>
    /// Stop all running proxies.
    /// </summary>
    void StopAllProxies();
    
    /// <summary>
    /// Check if proxy is running for a server.
    /// </summary>
    bool IsProxyRunning(int serverId);
}

public class ProxyService : IProxyService
{
    private readonly ILogger<ProxyService> _logger;
    private readonly HoNConfiguration _config;
    private readonly Dictionary<int, Process> _proxyProcesses = new();
    private readonly object _lock = new();
    
    public ProxyService(ILogger<ProxyService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }
    
    public async Task StartProxyAsync(GameServerInstance instance)
    {
        if (!_config.HonData.EnableProxy)
        {
            _logger.LogDebug("Proxy is disabled in config, skipping");
            return;
        }
        
        // Check for proxy executable
        var honDir = _config.HonData.HonInstallDirectory;
        var proxyExePath = FindProxyExecutable(honDir);
        
        if (string.IsNullOrEmpty(proxyExePath) || !File.Exists(proxyExePath))
        {
            _logger.LogWarning("proxy executable not found in {Path}. Download from https://github.com/wasserver/wasserver", honDir);
            return;
        }
        
        // Stop existing proxy if running
        StopProxy(instance.Id);
        
        // Create config directory
        var artefactsDir = !string.IsNullOrEmpty(_config.HonData.HonHomeDirectory) 
            ? _config.HonData.HonHomeDirectory 
            : honDir;
        var proxyConfigDir = Path.Combine(artefactsDir, "HoNProxyManager");
        Directory.CreateDirectory(proxyConfigDir);
        
        // Create proxy config file
        var configPath = Path.Combine(proxyConfigDir, $"Config{instance.Id}");
        
        // Calculate ports (same logic as GameServerManager)
        var gamePort = _config.HonData.StartingGamePort + instance.Id - 1;
        var voicePort = _config.HonData.StartingVoicePort + instance.Id - 1;
        var proxyGamePort = _config.HonData.StartingGamePort + instance.Id + 10000 - 1;
        var proxyVoicePort = _config.HonData.StartingVoicePort + instance.Id + 10000 - 1;
        
        // Get public IP
        var publicIp = _config.HonData.ServerIp ?? _config.HonData.LocalIp ?? "0.0.0.0";
        
        var configContent = $"""
redirectIP=127.0.0.1
publicip={publicIp}
publicPort={proxyGamePort}
redirectPort={gamePort}
voiceRedirectPort={voicePort}
voicePublicPort={proxyVoicePort}
region=naeu
""";
        
        // Check if config changed
        var configChanged = true;
        if (File.Exists(configPath))
        {
            var existingConfig = await File.ReadAllTextAsync(configPath);
            configChanged = existingConfig != configContent;
        }
        
        if (configChanged)
        {
            await File.WriteAllTextAsync(configPath, configContent);
            _logger.LogInformation("Created proxy config for Server #{Id}: {Path}", instance.Id, configPath);
        }
        
        // Check for existing proxy process
        var pidFile = $"{configPath}.pid";
        if (File.Exists(pidFile))
        {
            try
            {
                var pidStr = await File.ReadAllTextAsync(pidFile);
                if (int.TryParse(pidStr.Trim(), out var pid))
                {
                    try
                    {
                        var existingProcess = Process.GetProcessById(pid);
                        if (existingProcess.ProcessName.Equals("proxy", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Found existing proxy process for Server #{Id} (PID: {Pid})", instance.Id, pid);
                            lock (_lock)
                            {
                                _proxyProcesses[instance.Id] = existingProcess;
                            }
                            instance.ProxyEnabled = true;
                            return;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process not found
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading existing proxy PID file");
            }
        }
        
        // Start new proxy process
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = proxyExePath,
                Arguments = configPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(proxyExePath)
            };
            
            var process = Process.Start(startInfo);
            if (process != null)
            {
                lock (_lock)
                {
                    _proxyProcesses[instance.Id] = process;
                }
                
                // Save PID
                await File.WriteAllTextAsync(pidFile, process.Id.ToString());
                
                _logger.LogInformation("Started proxy for Server #{Id} (PID: {Pid})", instance.Id, process.Id);
                instance.ProxyEnabled = true;
                
                // Update public port
                instance.PublicPort = proxyGamePort;
                
                // Monitor process in background
                _ = MonitorProxyAsync(instance.Id, process);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start proxy for Server #{Id}", instance.Id);
        }
    }
    
    private async Task MonitorProxyAsync(int serverId, Process process)
    {
        try
        {
            await process.WaitForExitAsync();
            _logger.LogWarning("Proxy for Server #{Id} exited with code {Code}", serverId, process.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Proxy monitor ended for Server #{Id}", serverId);
        }
        finally
        {
            lock (_lock)
            {
                _proxyProcesses.Remove(serverId);
            }
        }
    }
    
    public void StopProxy(int serverId)
    {
        lock (_lock)
        {
            if (_proxyProcesses.TryGetValue(serverId, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        _logger.LogInformation("Stopped proxy for Server #{Id}", serverId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error stopping proxy for Server #{Id}", serverId);
                }
                _proxyProcesses.Remove(serverId);
            }
        }
    }
    
    public void StopAllProxies()
    {
        lock (_lock)
        {
            foreach (var (serverId, process) in _proxyProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        _logger.LogInformation("Stopped proxy for Server #{Id}", serverId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error stopping proxy for Server #{Id}", serverId);
                }
            }
            _proxyProcesses.Clear();
        }
    }
    
    public bool IsProxyRunning(int serverId)
    {
        lock (_lock)
        {
            if (_proxyProcesses.TryGetValue(serverId, out var process))
            {
                return !process.HasExited;
            }
            return false;
        }
    }
    
    /// <summary>
    /// Find proxy executable for current platform
    /// </summary>
    private string FindProxyExecutable(string honDir)
    {
        if (string.IsNullOrEmpty(honDir))
            return "";

        string[] executableNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "proxy.exe", "honproxy.exe" }
            : new[] { "proxy", "honproxy", "proxy.sh" };

        foreach (var exeName in executableNames)
        {
            var fullPath = Path.Combine(honDir, exeName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return "";
    }
}
