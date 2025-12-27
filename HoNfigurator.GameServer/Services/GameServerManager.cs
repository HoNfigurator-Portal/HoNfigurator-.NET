using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Net.Http;
using HoNfigurator.Core.Models;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.GameServer.Services;

public interface IGameServerManager
{
    IReadOnlyList<GameServerInstance> Instances { get; }
    HoNConfiguration? Configuration { get; set; }
    bool MasterServerConnected { get; set; }
    bool ChatServerConnected { get; set; }
    string? MasterServerStatus { get; set; }
    string? ChatServerStatus { get; set; }
    Task<GameServerInstance?> StartServerAsync(int id);
    Task<bool> StopServerAsync(int id, bool graceful = true);
    Task<bool> RestartServerAsync(int id);
    Task StartAllServersAsync();
    Task StopAllServersAsync();
    Task SendMessageToServerAsync(int id, string message);
    Task SendMessageToAllServersAsync(string message);
    ServerStatusResponse GetStatus();
    void Initialize(int totalServers, int startingPort, int startingVoicePort);
    void AddServer(GameServerInstance server);
    int AddNewServer();
    bool RemoveServer(int id);
    void ClearServers();
    void UpdateProcessStats();
    
    /// <summary>
    /// Set the listener reference for sending commands to game servers
    /// </summary>
    void SetListener(IGameServerListener listener);
    
    /// <summary>
    /// Get all server instances (for management portal integration)
    /// </summary>
    IReadOnlyList<GameServerInstance> GetAllServers();
    
    /// <summary>
    /// Get a server by its port number
    /// </summary>
    GameServerInstance? GetServerByPort(int port);
}

public class GameServerManager : IGameServerManager
{
    private readonly ConcurrentDictionary<int, GameServerInstance> _instances = new();
    private readonly ConcurrentDictionary<int, Process?> _processes = new();
    private readonly ILogger<GameServerManager> _logger;
    private readonly string _serverExecutablePath;
    private readonly string _serverName;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly SemaphoreSlim _startupSemaphore;
    private int _maxStartAtOnce = 2;
    private string? _publicIp;
    private IGameServerListener? _listener;
    
    // Connection status
    public bool MasterServerConnected { get; set; }
    public bool ChatServerConnected { get; set; }
    public string? MasterServerStatus { get; set; }
    public string? ChatServerStatus { get; set; }
    public HoNConfiguration? Configuration { get; set; }

    public IReadOnlyList<GameServerInstance> Instances => _instances.Values.OrderBy(i => i.Id).ToList();

    public GameServerManager(ILogger<GameServerManager> logger, string serverExecutablePath, string serverName)
    {
        _logger = logger;
        _serverExecutablePath = serverExecutablePath;
        _serverName = serverName;
        _startupSemaphore = new SemaphoreSlim(_maxStartAtOnce);
        
        // Lookup public IP at startup
        _ = LookupPublicIpAsync();
    }

    /// <summary>
    /// Set the listener reference for sending commands to game servers
    /// </summary>
    public void SetListener(IGameServerListener listener)
    {
        _listener = listener;
    }

    private async Task LookupPublicIpAsync()
    {
        var providers = new[] { "https://4.ident.me", "https://api.ipify.org", "https://ifconfig.me/ip" };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        
        foreach (var provider in providers)
        {
            try
            {
                _publicIp = (await client.GetStringAsync(provider)).Trim();
                _logger.LogInformation("Public IP: {PublicIp}", _publicIp);
                return;
            }
            catch
            {
                // Try next provider
            }
        }
        _logger.LogWarning("Failed to lookup public IP");
    }

    public void Initialize(int totalServers, int startingPort, int startingVoicePort)
    {
        _instances.Clear();
        _processes.Clear();
        
        for (int i = 0; i < totalServers; i++)
        {
            var instance = new GameServerInstance
            {
                Id = i + 1,
                Name = $"{_serverName} #{i + 1}",
                Port = startingPort + i,
                VoicePort = startingVoicePort + i,
                Status = ServerStatus.Offline
            };
            _instances.TryAdd(instance.Id, instance);
        }
        
        _logger.LogInformation("Initialized {Count} game server instances", totalServers);
    }

    public async Task<GameServerInstance?> StartServerAsync(int id)
    {
        if (!_instances.TryGetValue(id, out var instance))
        {
            _logger.LogWarning("Server {Id} not found", id);
            return null;
        }

        if (instance.Status != ServerStatus.Offline && instance.Status != ServerStatus.Crashed)
        {
            _logger.LogWarning("Server {Id} is already running or starting", id);
            return instance;
        }

        // Use semaphore to limit concurrent startups
        await _startupSemaphore.WaitAsync();
        
        try
        {
            instance.Status = ServerStatus.Starting;
            _logger.LogInformation("Starting server {Id} on port {Port}", id, instance.Port);

            // Check for existing process
            if (_processes.TryGetValue(id, out var existingProcess) && existingProcess != null && !existingProcess.HasExited)
            {
                _logger.LogInformation("Server {Id} already has a running process (PID: {Pid})", id, existingProcess.Id);
                instance.ProcessId = existingProcess.Id;
                instance.Status = ServerStatus.Ready;
                instance.StartTime = DateTime.UtcNow;
                return instance;
            }

            // Build command line arguments (similar to Python's build_commandline_args)
            var process = await StartHoNProcessAsync(instance);
            
            if (process != null)
            {
                _processes[id] = process;
                instance.ProcessId = process.Id;
                instance.Status = ServerStatus.Ready;
                instance.StartTime = DateTime.UtcNow;
                
                // Start monitoring the process
                _ = MonitorProcessAsync(id, process, instance);
                
                _logger.LogInformation("Server {Id} started successfully (PID: {Pid})", id, process.Id);
            }
            else
            {
                instance.Status = ServerStatus.Crashed;
                _logger.LogError("Failed to start server {Id} - process is null", id);
            }
        }
        catch (Exception ex)
        {
            instance.Status = ServerStatus.Crashed;
            _logger.LogError(ex, "Failed to start server {Id}", id);
        }
        finally
        {
            _startupSemaphore.Release();
        }

        return instance;
    }

    private async Task<Process?> StartHoNProcessAsync(GameServerInstance instance)
    {
        // Find the HoN executable
        var executablePath = FindHoNExecutable();
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            _logger.LogError("HoN executable not found at: {Path}", executablePath);
            return null;
        }

        // Build command line arguments like Python does
        var args = BuildCommandLineArgs(instance);
        
        _logger.LogInformation("Starting HoN: {Exe} {Args}", executablePath, string.Join(" ", args));

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = Configuration?.HonData?.NoConsole ?? true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
        };

        // Use ArgumentList instead of Arguments to handle special characters properly
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Set environment variables based on OS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var honHomeDir = Configuration?.HonData?.HonHomeDirectory ?? "";
            var artefactsDir = Path.Combine(honHomeDir, "Documents", "Heroes of Newerth x64");
            
            if (!string.IsNullOrEmpty(honHomeDir))
            {
                startInfo.Environment["USERPROFILE"] = honHomeDir;
            }
            if (!string.IsNullOrEmpty(artefactsDir))
            {
                startInfo.Environment["APPDATA"] = artefactsDir;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Linux/macOS environment setup
            var honHomeDir = Configuration?.HonData?.HonHomeDirectory ?? "";
            if (!string.IsNullOrEmpty(honHomeDir))
            {
                startInfo.Environment["HOME"] = honHomeDir;
            }
            
            // Set LD_LIBRARY_PATH for Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var installDir = Configuration?.HonData?.HonInstallDirectory ?? "";
                var libPath = Path.Combine(installDir, "libs-x86_64");
                var existingLibPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
                startInfo.Environment["LD_LIBRARY_PATH"] = string.IsNullOrEmpty(existingLibPath) 
                    ? libPath 
                    : $"{libPath}:{existingLibPath}";
            }
        }

        try
        {
            var process = Process.Start(startInfo);
            
            if (process != null)
            {
                // Wait a bit for the process to initialize
                await Task.Delay(1000);
                
                if (!process.HasExited)
                {
                    return process;
                }
                else
                {
                    _logger.LogError("HoN process exited immediately with code {ExitCode}", process.ExitCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HoN process");
        }

        return null;
    }

    private string FindHoNExecutable()
    {
        var installDir = Configuration?.HonData?.HonInstallDirectory ?? _serverExecutablePath;
        
        if (string.IsNullOrEmpty(installDir))
            return "";

        // Common executable names
        string[] executableNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "hon_x64.exe", "hon.exe", "k2_x64.exe", "k2.exe" }
            : new[] { "hon_x64-server", "hon-server", "hon_x64", "hon" };

        foreach (var exeName in executableNames)
        {
            var fullPath = Path.Combine(installDir, exeName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Return the install directory if it looks like it's already a file path
        if (File.Exists(installDir))
        {
            return installDir;
        }

        return "";
    }

    private List<string> BuildCommandLineArgs(GameServerInstance instance)
    {
        var args = new List<string>();
        var config = Configuration?.HonData;

        if (config == null) return args;

        // Calculate ports for this instance (same as Python)
        var gamePort = config.StartingGamePort + instance.Id - 1;
        var voicePort = config.StartingVoicePort + instance.Id - 1;
        var proxyGamePort = config.EnableProxy ? config.StartingGamePort + instance.Id + 10000 - 1 : gamePort;
        var proxyVoicePort = config.EnableProxy ? config.StartingVoicePort + instance.Id + 10000 - 1 : voicePort;

        // Build parameters string like Python: Set key value;Set key2 value2;...
        // Must match Python's get_local_configuration() params
        var setCommands = new List<string>
        {
            // Core server identity
            $"Set svr_login {config.Login}:{instance.Id}",  // Python: f"{login}:{id}"
            $"Set svr_password {config.Password}",
            $"Set sv_masterName {config.Login}:",
            $"Set svr_slave {instance.Id}",
            $"Set svr_name {config.ServerName} {instance.Id} 0",  // No quotes needed
            $"Set svr_adminPassword",
            $"Set svr_description HoNfigurator Server",
            
            // Network ports - CRITICAL for multiple servers
            $"Set svr_port {gamePort}",
            $"Set svr_proxyPort {proxyGamePort}",
            $"Set svr_proxyLocalVoicePort {voicePort}",
            $"Set svr_proxyRemoteVoicePort {proxyVoicePort}",
            $"Set svr_voicePortStart {voicePort}",
            $"Set svr_chatPort {11032 + instance.Id - 1}",  // Each server needs unique chat port
            
            // Server location and settings - Use public IP like Python
            $"Set svr_ip {config.ServerIp ?? _publicIp ?? "0.0.0.0"}",
            $"Set svr_location {config.Location}",
            $"Set man_enableProxy {(config.EnableProxy ? "true" : "false")}",
            $"Set svr_broadcast true",
            $"Set svr_chatAddress 96.127.149.202",  // Chat server address
            
            // System settings
            $"Set upd_checkForUpdates false",
            $"Set sv_autosaveReplay true",
            $"Set sys_autoSaveDump false",
            $"Set sys_dumpOnFatal false",
            $"Set svr_maxIncomingPacketsPerSecond 300",
            $"Set svr_maxIncomingBytesPerSecond 1048576",
            $"Set con_showNet false",
            $"Set http_printDebugInfo false",
            $"Set php_printDebugInfo false",
            $"Set svr_debugChatServer false",
            $"Set svr_submitStats true",
            $"Set http_useCompression false",
            $"Set man_resubmitStats true",
            $"Set man_uploadReplays true",
            $"Set sv_remoteAdmins"
        };

        var paramsString = string.Join(";", setCommands);

        // Build command line args exactly like Python:
        // [exe, -dedicated, -noconfig, -mod, game;KONGOR, -noconsole, -execute, params, -masterserver, url, -register, addr]
        
        args.Add("-dedicated");
        args.Add("-noconfig");
        
        // -mod game;KONGOR (Windows)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            args.Add("-mod");
            args.Add("game;PKOS");
            
            if (config.NoConsole)
            {
                args.Add("-noconsole");
            }
        }
        else
        {
            args.Add("-mod");
            args.Add("game;PKOS");
        }

        // -execute with all Set commands
        args.Add("-execute");
        args.Add(paramsString);

        // -masterserver
        args.Add("-masterserver");
        args.Add(config.MasterServer);

        // -register for manager communication
        args.Add("-register");
        args.Add($"127.0.0.1:{config.ManagerPort}");

        return args;
    }

    private async Task MonitorProcessAsync(int id, Process process, GameServerInstance instance)
    {
        try
        {
            // Monitor until process exits
            await process.WaitForExitAsync();
            
            _logger.LogWarning("Server {Id} process exited (PID: {Pid}, ExitCode: {ExitCode})", 
                id, process.Id, process.ExitCode);
            
            instance.Status = ServerStatus.Crashed;
            instance.ProcessId = null;
            instance.StartTime = null;
            instance.NumClients = 0;
            
            _processes.TryRemove(id, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring server {Id} process", id);
        }
    }

    public void UpdateProcessStats()
    {
        foreach (var (id, process) in _processes)
        {
            if (process == null || process.HasExited)
                continue;

            if (!_instances.TryGetValue(id, out var instance))
                continue;

            try
            {
                process.Refresh();
                
                // Update CPU and memory usage
                instance.MemoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not update stats for server {Id}", id);
            }
        }
    }

    public async Task<bool> StopServerAsync(int id, bool graceful = true)
    {
        if (!_instances.TryGetValue(id, out var instance))
        {
            return false;
        }

        if (instance.Status == ServerStatus.Offline)
        {
            return true;
        }

        _logger.LogInformation("Stopping server {Id} (graceful: {Graceful})", id, graceful);

        try
        {
            // If graceful shutdown is requested and we have a listener, send shutdown command first
            if (graceful && _listener != null)
            {
                _logger.LogInformation("Sending graceful shutdown command to server {Id}...", id);
                var shutdownSent = await _listener.SendShutdownCommandAsync(id);
                
                if (shutdownSent)
                {
                    // Wait for server to shutdown gracefully (up to 15 seconds)
                    // The server should kick all players and close itself
                    var gracefulTimeout = 15000;
                    var waited = 0;
                    var checkInterval = 500;
                    
                    while (waited < gracefulTimeout)
                    {
                        await Task.Delay(checkInterval);
                        waited += checkInterval;
                        
                        // Check if process has exited
                        if (_processes.TryGetValue(id, out var proc) && proc != null)
                        {
                            try
                            {
                                proc.Refresh();
                                if (proc.HasExited)
                                {
                                    _logger.LogInformation("Server {Id} shutdown gracefully", id);
                                    break;
                                }
                            }
                            catch
                            {
                                // Process likely already exited
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // Force kill if still running
            if (_processes.TryGetValue(id, out var process) && process != null && !process.HasExited)
            {
                _logger.LogWarning("Server {Id} did not exit gracefully, force killing", id);
                process.Kill(entireProcessTree: true);
                
                // Wait for exit with timeout
                var exitTask = process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(10000)) != exitTask)
                {
                    _logger.LogWarning("Server {Id} force kill timed out", id);
                }
            }

            _processes.TryRemove(id, out _);
            
            instance.Status = ServerStatus.Offline;
            instance.StartTime = null;
            instance.NumClients = 0;
            instance.MatchId = null;
            instance.ProcessId = null;
            
            _logger.LogInformation("Server {Id} stopped", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop server {Id}", id);
            return false;
        }
    }

    public async Task<bool> RestartServerAsync(int id)
    {
        _logger.LogInformation("Restarting server {Id}", id);
        await StopServerAsync(id, graceful: true);
        await Task.Delay(2000);
        var result = await StartServerAsync(id);
        return result != null;
    }

    public async Task StartAllServersAsync()
    {
        var maxConcurrent = Configuration?.HonData?.MaxStartAtOnce ?? 2;
        
        _logger.LogInformation("Starting all {Count} servers (max {MaxConcurrent} at once)", 
            _instances.Count, maxConcurrent);

        // Start servers sequentially with delay to avoid port conflicts
        foreach (var id in _instances.Keys.OrderBy(x => x))
        {
            await StartServerAsync(id);
            // Wait for server to fully initialize before starting next one
            await Task.Delay(3000);
        }
    }

    public async Task StopAllServersAsync()
    {
        _logger.LogInformation("Stopping all servers");
        var tasks = _instances.Keys.Select(id => StopServerAsync(id, graceful: true));
        await Task.WhenAll(tasks);
    }

    public async Task SendMessageToServerAsync(int id, string message)
    {
        if (!_processes.TryGetValue(id, out var process) || process == null || process.HasExited)
        {
            _logger.LogWarning("Cannot send message to server {Id} - process not found or has exited", id);
            return;
        }
        
        try
        {
            // Format message as HoN console command
            // sv_talk command sends a server message to all players
            var command = $"sv_talk \"{message.Replace("\"", "\\\"")}\"";
            
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            
            _logger.LogInformation("Sent message to server {Id}: {Message}", id, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to server {Id}", id);
        }
    }

    public async Task SendMessageToAllServersAsync(string message)
    {
        _logger.LogInformation("Broadcasting message to all servers: {Message}", message);
        foreach (var id in _instances.Keys)
        {
            await SendMessageToServerAsync(id, message);
        }
    }

    public ServerStatusResponse GetStatus()
    {
        UpdateProcessStats();
        
        var instances = _instances.Values.OrderBy(i => i.Id).ToList();
        var uptime = DateTime.UtcNow - _startTime;

        return new ServerStatusResponse
        {
            ServerName = _serverName,
            Version = "1.0.0-dotnet",
            MasterServerConnected = MasterServerConnected,
            ChatServerConnected = ChatServerConnected,
            TotalServers = instances.Count,
            OnlineServers = instances.Count(s => s.Status != ServerStatus.Offline && s.Status != ServerStatus.Crashed),
            TotalPlayers = instances.Sum(s => s.NumClients),
            Instances = instances,
            SystemStats = GetSystemStats()
        };
    }

    private SystemStats GetSystemStats()
    {
        var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - _startTime;

        // Calculate HoN process stats
        long totalHonMemory = 0;
        int honProcessCount = 0;
        
        foreach (var (_, honProcess) in _processes)
        {
            if (honProcess != null && !honProcess.HasExited)
            {
                try
                {
                    honProcess.Refresh();
                    totalHonMemory += honProcess.WorkingSet64;
                    honProcessCount++;
                }
                catch { }
            }
        }

        // Calculate server capacity
        var cpuCount = Environment.ProcessorCount;
        var svrTotalPerCore = Configuration?.HonData?.TotalPerCore ?? 1.0;
        var svrTotal = Configuration?.HonData?.TotalServers ?? 1;
        var maxAllowedServers = GetTotalAllowedServers(cpuCount, svrTotalPerCore);

        return new SystemStats
        {
            CpuUsagePercent = 0, // Would need PerformanceCounter for accurate CPU
            CpuCount = cpuCount,
            MemoryUsagePercent = 0,
            TotalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            UsedMemoryMb = process.WorkingSet64 / 1024 / 1024,
            HonProcessCount = honProcessCount,
            HonTotalMemoryMb = totalHonMemory / 1024 / 1024,
            Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
            UptimeSeconds = (long)uptime.TotalSeconds,
            SvrTotalPerCore = svrTotalPerCore,
            MaxAllowedServers = maxAllowedServers,
            SvrTotal = svrTotal
        };
    }
    
    /// <summary>
    /// Calculate maximum allowed servers based on CPU count and servers per core setting
    /// Reserves some CPUs for OS/Manager: ≤4 cores: 1 reserved, 5-12: 2 reserved, >12: 4 reserved
    /// </summary>
    private static int GetTotalAllowedServers(int cpuCount, double svrTotalPerCore)
    {
        var total = svrTotalPerCore * cpuCount;
        
        if (cpuCount < 5)
            total -= 1;
        else if (cpuCount > 4 && cpuCount < 13)
            total -= 2;
        else if (cpuCount > 12)
            total -= 4;
        
        return Math.Max(0, (int)total);
    }

    public void SetConnectionStatus(bool masterServer, bool chatServer)
    {
        MasterServerConnected = masterServer;
        ChatServerConnected = chatServer;
    }

    public void AddServer(GameServerInstance server)
    {
        _instances.TryAdd(server.Id, server);
        _logger.LogInformation("Added server {Id}: {Name}", server.Id, server.Name);
    }

    public int AddNewServer()
    {
        // Find next available ID
        var maxId = _instances.Keys.DefaultIfEmpty(0).Max();
        var newId = maxId + 1;
        
        // Calculate ports based on existing servers
        var basePort = Configuration?.HonData.StartingGamePort ?? 10001;
        var baseVoicePort = Configuration?.HonData.StartingVoicePort ?? 10061;
        
        var server = new GameServerInstance
        {
            Id = newId,
            Name = $"{_serverName} #{newId}",
            Port = basePort + maxId,
            VoicePort = baseVoicePort + maxId,
            Status = ServerStatus.Offline
        };
        
        _instances.TryAdd(newId, server);
        _logger.LogInformation("Added new server {Id}: {Name} (Port: {Port})", newId, server.Name, server.Port);
        
        return newId;
    }

    public bool RemoveServer(int id)
    {
        // Stop the server first if running
        if (_processes.TryRemove(id, out var process) && process != null && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
        }
        
        var removed = _instances.TryRemove(id, out var server);
        if (removed && server != null)
        {
            _logger.LogInformation("Removed server {Id}: {Name}", id, server.Name);
        }
        return removed;
    }

    public void ClearServers()
    {
        // Stop all processes first
        foreach (var (_, process) in _processes)
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
        }
        
        _instances.Clear();
        _processes.Clear();
        _logger.LogInformation("Cleared all servers");
    }

    /// <summary>
    /// Get all server instances (for management portal integration)
    /// </summary>
    public IReadOnlyList<GameServerInstance> GetAllServers()
    {
        return Instances;
    }

    /// <summary>
    /// Get a server by its port number
    /// </summary>
    public GameServerInstance? GetServerByPort(int port)
    {
        return _instances.Values.FirstOrDefault(s => s.Port == port);
    }
}
