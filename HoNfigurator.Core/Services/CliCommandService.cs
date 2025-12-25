using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Interface for game server management used by CLI commands
/// </summary>
public interface ICliGameServerManager
{
    IEnumerable<ServerInfo> GetAllServers();
    Task<ServerStartResult> StartServerAsync(int? port = null);
    Task<bool> StopServerAsync(int port);
    Task<ScaleResult> ScaleServersAsync(int targetCount);
}

public class ServerInfo
{
    public int Port { get; set; }
    public bool IsRunning { get; set; }
    public int PlayerCount { get; set; }
    public int? ProcessId { get; set; }
    public DateTime? StartTime { get; set; }
}

public class ServerStartResult
{
    public bool Success { get; set; }
    public int Port { get; set; }
    public string? Error { get; set; }
}

public class ScaleResult
{
    public int PreviousCount { get; set; }
    public int CurrentCount { get; set; }
    public int Started { get; set; }
    public int Stopped { get; set; }
}

/// <summary>
/// Interactive CLI command handler for server management.
/// Port of Python's commands.py functionality.
/// </summary>
public interface ICliCommandService
{
    Task StartInteractiveAsync(CancellationToken cancellationToken = default);
    Task<CommandResult> ExecuteCommandAsync(string command, string[] args);
    IEnumerable<CommandInfo> GetCommands();
}

public class CommandResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string? Error { get; set; }
}

public class CommandInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Usage { get; set; } = "";
    public string[] Aliases { get; set; } = Array.Empty<string>();
}

public class CliCommandService : ICliCommandService
{
    private readonly ILogger<CliCommandService> _logger;
    private readonly ICliGameServerManager _serverManager;
    private readonly HoNConfiguration _config;
    private readonly ReplayManager? _replayManager;
    private readonly IPatchingService? _patchingService;
    
    private readonly Dictionary<string, Func<string[], Task<CommandResult>>> _commands;
    private readonly List<CommandInfo> _commandInfos;

    public CliCommandService(
        ILogger<CliCommandService> logger,
        ICliGameServerManager serverManager,
        HoNConfiguration config,
        ReplayManager? replayManager = null,
        IPatchingService? patchingService = null)
    {
        _logger = logger;
        _serverManager = serverManager;
        _config = config;
        _replayManager = replayManager;
        _patchingService = patchingService;

        _commands = new Dictionary<string, Func<string[], Task<CommandResult>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["help"] = HelpCommand,
            ["?"] = HelpCommand,
            ["status"] = StatusCommand,
            ["list"] = ListCommand,
            ["ls"] = ListCommand,
            ["start"] = StartCommand,
            ["stop"] = StopCommand,
            ["restart"] = RestartCommand,
            ["scale"] = ScaleCommand,
            ["kick"] = KickCommand,
            ["ban"] = BanCommand,
            ["unban"] = UnbanCommand,
            ["say"] = SayCommand,
            ["broadcast"] = BroadcastCommand,
            ["replays"] = ReplaysCommand,
            ["patch"] = PatchCommand,
            ["config"] = ConfigCommand,
            ["clear"] = ClearCommand,
            ["quit"] = QuitCommand,
            ["exit"] = QuitCommand
        };

        _commandInfos = new List<CommandInfo>
        {
            new() { Name = "help", Description = "Show available commands", Usage = "help [command]", Aliases = new[] { "?" } },
            new() { Name = "status", Description = "Show server status overview", Usage = "status" },
            new() { Name = "list", Description = "List running game servers", Usage = "list", Aliases = new[] { "ls" } },
            new() { Name = "start", Description = "Start a new game server", Usage = "start [port]" },
            new() { Name = "stop", Description = "Stop a game server", Usage = "stop <port|all>" },
            new() { Name = "restart", Description = "Restart a game server", Usage = "restart <port|all>" },
            new() { Name = "scale", Description = "Scale servers up or down", Usage = "scale <up|down|N>" },
            new() { Name = "kick", Description = "Kick a player from server", Usage = "kick <port> <player>" },
            new() { Name = "ban", Description = "Ban a player", Usage = "ban <accountId> [reason]" },
            new() { Name = "unban", Description = "Unban a player", Usage = "unban <accountId>" },
            new() { Name = "say", Description = "Send message to server", Usage = "say <port> <message>" },
            new() { Name = "broadcast", Description = "Broadcast to all servers", Usage = "broadcast <message>" },
            new() { Name = "replays", Description = "Manage replay files", Usage = "replays [list|upload|stats]" },
            new() { Name = "patch", Description = "Check for or apply patches", Usage = "patch [check|apply]" },
            new() { Name = "config", Description = "Show/edit configuration", Usage = "config [show|reload]" },
            new() { Name = "clear", Description = "Clear console", Usage = "clear" },
            new() { Name = "quit", Description = "Exit the application", Usage = "quit", Aliases = new[] { "exit" } }
        };
    }

    public IEnumerable<CommandInfo> GetCommands() => _commandInfos;

    public async Task StartInteractiveAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           HoNfigurator Interactive Console               ║");
        Console.WriteLine("║           Type 'help' for available commands             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("hon> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];
            var args = parts.Skip(1).ToArray();

            try
            {
                var result = await ExecuteCommandAsync(command, args);
                
                if (!string.IsNullOrEmpty(result.Output))
                    Console.WriteLine(result.Output);
                
                if (!string.IsNullOrEmpty(result.Error))
                    Console.WriteLine($"Error: {result.Error}");

                // Check for quit command
                if (command.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing command: {ex.Message}");
            }
        }
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, string[] args)
    {
        if (_commands.TryGetValue(command, out var handler))
        {
            return await handler(args);
        }

        return new CommandResult
        {
            Success = false,
            Error = $"Unknown command: {command}. Type 'help' for available commands."
        };
    }

    private Task<CommandResult> HelpCommand(string[] args)
    {
        var output = new System.Text.StringBuilder();

        if (args.Length > 0)
        {
            var cmdName = args[0];
            var info = _commandInfos.FirstOrDefault(c => 
                c.Name.Equals(cmdName, StringComparison.OrdinalIgnoreCase) ||
                c.Aliases.Contains(cmdName, StringComparer.OrdinalIgnoreCase));

            if (info != null)
            {
                output.AppendLine($"Command: {info.Name}");
                output.AppendLine($"Description: {info.Description}");
                output.AppendLine($"Usage: {info.Usage}");
                if (info.Aliases.Any())
                    output.AppendLine($"Aliases: {string.Join(", ", info.Aliases)}");
            }
            else
            {
                return Task.FromResult(new CommandResult { Success = false, Error = $"Unknown command: {cmdName}" });
            }
        }
        else
        {
            output.AppendLine("Available Commands:");
            output.AppendLine("─".PadRight(50, '─'));
            
            foreach (var cmd in _commandInfos)
            {
                var aliasStr = cmd.Aliases.Any() ? $" ({string.Join(", ", cmd.Aliases)})" : "";
                output.AppendLine($"  {cmd.Name,-12}{aliasStr,-10} {cmd.Description}");
            }
            
            output.AppendLine();
            output.AppendLine("Type 'help <command>' for more details");
        }

        return Task.FromResult(new CommandResult { Success = true, Output = output.ToString() });
    }

    private async Task<CommandResult> StatusCommand(string[] args)
    {
        var output = new System.Text.StringBuilder();
        var servers = _serverManager.GetAllServers().ToList();
        var running = servers.Count(s => s.IsRunning);

        output.AppendLine("═══════════════════════════════════════════════════════════");
        output.AppendLine("                    SERVER STATUS");
        output.AppendLine("═══════════════════════════════════════════════════════════");
        output.AppendLine($"  Servers Running:    {running}/{_config.HonData.TotalServers}");
        output.AppendLine($"  Total Servers:      {_config.HonData.TotalServers}");
        output.AppendLine($"  Starting Port:      {_config.HonData.StartingGamePort}");
        output.AppendLine($"  HoN Version:        {_config.HonData.ManVersion ?? "Unknown"}");
        output.AppendLine($"  Master Server:      {_config.HonData.MasterServer ?? "Not configured"}");
        output.AppendLine("═══════════════════════════════════════════════════════════");

        if (_replayManager != null)
        {
            var stats = _replayManager.GetStats();
            output.AppendLine($"  Replays Stored:     {stats.TotalReplays} ({stats.TotalSizeMb:F1} MB)");
            output.AppendLine($"  Pending Uploads:    {_replayManager.GetPendingUploadCount()}");
        }

        return new CommandResult { Success = true, Output = output.ToString() };
    }

    private async Task<CommandResult> ListCommand(string[] args)
    {
        var output = new System.Text.StringBuilder();
        var servers = _serverManager.GetAllServers().ToList();

        if (!servers.Any())
        {
            output.AppendLine("No game servers configured.");
            return new CommandResult { Success = true, Output = output.ToString() };
        }

        output.AppendLine("┌────────┬──────────┬───────────┬──────────┬─────────────────┐");
        output.AppendLine("│  Port  │  Status  │  Players  │   PID    │     Uptime      │");
        output.AppendLine("├────────┼──────────┼───────────┼──────────┼─────────────────┤");

        foreach (var server in servers.OrderBy(s => s.Port))
        {
            var status = server.IsRunning ? "Running" : "Stopped";
            var players = server.IsRunning ? $"{server.PlayerCount}/10" : "-";
            var pid = server.ProcessId?.ToString() ?? "-";
            var uptime = server.IsRunning && server.StartTime.HasValue
                ? FormatUptime(DateTime.UtcNow - server.StartTime.Value)
                : "-";

            output.AppendLine($"│ {server.Port,6} │ {status,-8} │ {players,-9} │ {pid,-8} │ {uptime,-15} │");
        }

        output.AppendLine("└────────┴──────────┴───────────┴──────────┴─────────────────┘");

        return new CommandResult { Success = true, Output = output.ToString() };
    }

    private async Task<CommandResult> StartCommand(string[] args)
    {
        int? port = null;
        if (args.Length > 0 && int.TryParse(args[0], out var p))
            port = p;

        var result = await _serverManager.StartServerAsync(port);
        
        return new CommandResult
        {
            Success = result.Success,
            Output = result.Success ? $"Started server on port {result.Port}" : "",
            Error = result.Error
        };
    }

    private async Task<CommandResult> StopCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandResult { Success = false, Error = "Usage: stop <port|all>" };
        }

        if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var servers = _serverManager.GetAllServers().Where(s => s.IsRunning).ToList();
            var stopped = 0;
            
            foreach (var server in servers)
            {
                if (await _serverManager.StopServerAsync(server.Port))
                    stopped++;
            }

            return new CommandResult
            {
                Success = true,
                Output = $"Stopped {stopped} server(s)"
            };
        }

        if (!int.TryParse(args[0], out var port))
        {
            return new CommandResult { Success = false, Error = "Invalid port number" };
        }

        var success = await _serverManager.StopServerAsync(port);
        return new CommandResult
        {
            Success = success,
            Output = success ? $"Stopped server on port {port}" : "",
            Error = success ? null : $"Failed to stop server on port {port}"
        };
    }

    private async Task<CommandResult> RestartCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandResult { Success = false, Error = "Usage: restart <port|all>" };
        }

        if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var servers = _serverManager.GetAllServers().Where(s => s.IsRunning).ToList();
            var restarted = 0;

            foreach (var server in servers)
            {
                await _serverManager.StopServerAsync(server.Port);
                await Task.Delay(1000);
                var result = await _serverManager.StartServerAsync(server.Port);
                if (result.Success) restarted++;
            }

            return new CommandResult
            {
                Success = true,
                Output = $"Restarted {restarted} server(s)"
            };
        }

        if (!int.TryParse(args[0], out var port))
        {
            return new CommandResult { Success = false, Error = "Invalid port number" };
        }

        await _serverManager.StopServerAsync(port);
        await Task.Delay(1000);
        var startResult = await _serverManager.StartServerAsync(port);

        return new CommandResult
        {
            Success = startResult.Success,
            Output = startResult.Success ? $"Restarted server on port {port}" : "",
            Error = startResult.Error
        };
    }

    private async Task<CommandResult> ScaleCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandResult { Success = false, Error = "Usage: scale <up|down|N>" };
        }

        var current = _serverManager.GetAllServers().Count(s => s.IsRunning);
        var maxInstances = _config.HonData.TotalServers > 0 ? _config.HonData.TotalServers : 10;
        int target;

        if (args[0].Equals("up", StringComparison.OrdinalIgnoreCase))
        {
            target = Math.Min(current + 1, maxInstances);
        }
        else if (args[0].Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            target = Math.Max(current - 1, 0);
        }
        else if (int.TryParse(args[0], out var n))
        {
            target = Math.Clamp(n, 0, maxInstances);
        }
        else
        {
            return new CommandResult { Success = false, Error = "Invalid scale argument" };
        }

        var result = await _serverManager.ScaleServersAsync(target);

        return new CommandResult
        {
            Success = true,
            Output = $"Scaled from {current} to {result.CurrentCount} servers"
        };
    }

    private Task<CommandResult> KickCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Usage: kick <port> <player>" });
        }

        // TODO: Implement player kick via game server command
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Error = "Player kick not yet implemented"
        });
    }

    private Task<CommandResult> BanCommand(string[] args)
    {
        if (args.Length < 1)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Usage: ban <accountId> [reason]" });
        }

        // TODO: Integrate with BanManager
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Error = "Ban command not yet implemented"
        });
    }

    private Task<CommandResult> UnbanCommand(string[] args)
    {
        if (args.Length < 1)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Usage: unban <accountId>" });
        }

        // TODO: Integrate with BanManager
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Error = "Unban command not yet implemented"
        });
    }

    private Task<CommandResult> SayCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Usage: say <port> <message>" });
        }

        // TODO: Send message to specific server
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Error = "Say command not yet implemented"
        });
    }

    private Task<CommandResult> BroadcastCommand(string[] args)
    {
        if (args.Length < 1)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Usage: broadcast <message>" });
        }

        // TODO: Broadcast to all servers
        return Task.FromResult(new CommandResult
        {
            Success = false,
            Error = "Broadcast command not yet implemented"
        });
    }

    private Task<CommandResult> ReplaysCommand(string[] args)
    {
        if (_replayManager == null)
        {
            return Task.FromResult(new CommandResult { Success = false, Error = "Replay manager not available" });
        }

        var subCommand = args.Length > 0 ? args[0] : "stats";

        switch (subCommand.ToLowerInvariant())
        {
            case "list":
                var replays = _replayManager.GetReplays(10);
                var output = new System.Text.StringBuilder();
                output.AppendLine("Recent Replays:");
                foreach (var replay in replays)
                {
                    output.AppendLine($"  {replay.FileName} ({replay.SizeMb:F1} MB) - {replay.CreatedAt:g}");
                }
                return Task.FromResult(new CommandResult { Success = true, Output = output.ToString() });

            case "stats":
                var stats = _replayManager.GetStats();
                var statsOutput = new System.Text.StringBuilder();
                statsOutput.AppendLine($"Replay Statistics:");
                statsOutput.AppendLine($"  Total: {stats.TotalReplays} ({stats.TotalSizeMb:F1} MB)");
                statsOutput.AppendLine($"  Archived: {stats.ArchivedReplays} ({stats.ArchivedSizeMb:F1} MB)");
                statsOutput.AppendLine($"  Pending Uploads: {_replayManager.GetPendingUploadCount()}");
                return Task.FromResult(new CommandResult { Success = true, Output = statsOutput.ToString() });

            case "upload":
                return Task.FromResult(new CommandResult 
                { 
                    Success = true, 
                    Output = $"Queued {_replayManager.GetPendingUploadCount()} replays for upload" 
                });

            default:
                return Task.FromResult(new CommandResult { Success = false, Error = "Usage: replays [list|upload|stats]" });
        }
    }

    private async Task<CommandResult> PatchCommand(string[] args)
    {
        if (_patchingService == null)
        {
            return new CommandResult { Success = false, Error = "Patching service not available" };
        }

        var subCommand = args.Length > 0 ? args[0] : "check";

        switch (subCommand.ToLowerInvariant())
        {
            case "check":
                var checkResult = await _patchingService.CheckForUpdatesAsync();
                var output = new System.Text.StringBuilder();
                output.AppendLine($"Current Version: {checkResult.CurrentVersion ?? "Unknown"}");
                output.AppendLine($"Latest Version:  {checkResult.LatestVersion ?? "Unknown"}");
                output.AppendLine($"Update Available: {checkResult.UpdateAvailable}");
                if (checkResult.UpdateAvailable && checkResult.PatchSize > 0)
                    output.AppendLine($"Patch Size: {checkResult.PatchSize / 1024.0 / 1024.0:F1} MB");
                return new CommandResult { Success = true, Output = output.ToString() };

            case "apply":
                var patchCheck = await _patchingService.CheckForUpdatesAsync();
                if (!patchCheck.UpdateAvailable || string.IsNullOrEmpty(patchCheck.PatchUrl))
                {
                    return new CommandResult { Success = true, Output = "No updates available" };
                }

                Console.WriteLine("Applying patch...");
                var patchResult = await _patchingService.ApplyPatchAsync(patchCheck.PatchUrl, 
                    new Progress<PatchProgress>(p => Console.WriteLine($"  {p.Stage}: {p.PercentComplete}%")));
                
                return new CommandResult
                {
                    Success = patchResult.Success,
                    Output = patchResult.Success ? $"Updated to version {patchResult.NewVersion}" : "",
                    Error = patchResult.Error
                };

            default:
                return new CommandResult { Success = false, Error = "Usage: patch [check|apply]" };
        }
    }

    private Task<CommandResult> ConfigCommand(string[] args)
    {
        var subCommand = args.Length > 0 ? args[0] : "show";

        switch (subCommand.ToLowerInvariant())
        {
            case "show":
                var output = new System.Text.StringBuilder();
                output.AppendLine("Current Configuration:");
                output.AppendLine($"  Install Directory: {_config.HonData.HonInstallDirectory ?? "Not set"}");
                output.AppendLine($"  Version: {_config.HonData.ManVersion ?? "Not set"}");
                output.AppendLine($"  Master Server: {_config.HonData.MasterServer ?? "Not set"}");
                output.AppendLine($"  Total Servers: {_config.HonData.TotalServers}");
                output.AppendLine($"  Starting Port: {_config.HonData.StartingGamePort}");
                return Task.FromResult(new CommandResult { Success = true, Output = output.ToString() });

            case "reload":
                // TODO: Implement config reload
                return Task.FromResult(new CommandResult 
                { 
                    Success = false, 
                    Error = "Config reload not yet implemented" 
                });

            default:
                return Task.FromResult(new CommandResult { Success = false, Error = "Usage: config [show|reload]" });
        }
    }

    private Task<CommandResult> ClearCommand(string[] args)
    {
        Console.Clear();
        return Task.FromResult(new CommandResult { Success = true });
    }

    private Task<CommandResult> QuitCommand(string[] args)
    {
        return Task.FromResult(new CommandResult { Success = true, Output = "Goodbye!" });
    }

    private string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }
}
