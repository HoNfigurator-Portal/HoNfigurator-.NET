using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages server command execution and scripting.
/// Port of Python HoNfigurator-Central server command functionality.
/// </summary>
public class ServerCommandService
{
    private readonly ILogger<ServerCommandService> _logger;
    private readonly ConcurrentDictionary<int, ServerCommandContext> _serverContexts = new();
    private readonly ConcurrentQueue<QueuedServerCommand> _commandQueue = new();
    private readonly SemaphoreSlim _queueProcessor = new(1);
    
    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public ServerCommandService(ILogger<ServerCommandService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register a server for command execution
    /// </summary>
    public void RegisterServer(int serverId, Process serverProcess)
    {
        var context = new ServerCommandContext
        {
            ServerId = serverId,
            Process = serverProcess,
            RegisteredAt = DateTime.UtcNow
        };

        _serverContexts[serverId] = context;
        _logger.LogDebug("Server {ServerId} registered for commands", serverId);
    }

    /// <summary>
    /// Unregister a server
    /// </summary>
    public void UnregisterServer(int serverId)
    {
        if (_serverContexts.TryRemove(serverId, out _))
        {
            _logger.LogDebug("Server {ServerId} unregistered", serverId);
        }
    }

    /// <summary>
    /// Send a console command to a server
    /// </summary>
    public async Task<ServerCommandResult> SendCommandAsync(int serverId, string command, CancellationToken cancellationToken = default)
    {
        if (!_serverContexts.TryGetValue(serverId, out var context))
        {
            return new ServerCommandResult
            {
                Success = false,
                Error = $"Server {serverId} not registered"
            };
        }

        if (context.Process.HasExited)
        {
            return new ServerCommandResult
            {
                Success = false,
                Error = $"Server {serverId} process has exited"
            };
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Write command to stdin
            await context.Process.StandardInput.WriteLineAsync(command);
            await context.Process.StandardInput.FlushAsync();
            
            stopwatch.Stop();

            var result = new ServerCommandResult
            {
                Success = true,
                Command = command,
                ServerId = serverId,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };

            // Track command history
            context.CommandHistory.Add(new CommandHistoryEntry
            {
                Command = command,
                ExecutedAt = DateTime.UtcNow,
                Success = true
            });

            CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(serverId, command, true));
            
            _logger.LogDebug("Command sent to server {ServerId}: {Command}", serverId, command);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to server {ServerId}", serverId);
            return new ServerCommandResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Queue a command for execution
    /// </summary>
    public void QueueCommand(int serverId, string command, int priority = 0)
    {
        _commandQueue.Enqueue(new QueuedServerCommand
        {
            ServerId = serverId,
            Command = command,
            Priority = priority,
            QueuedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Process queued commands
    /// </summary>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await _queueProcessor.WaitAsync(cancellationToken);
        
        try
        {
            var commands = new List<QueuedServerCommand>();
            while (_commandQueue.TryDequeue(out var cmd))
            {
                commands.Add(cmd);
            }

            // Process in priority order
            foreach (var cmd in commands.OrderByDescending(c => c.Priority))
            {
                await SendCommandAsync(cmd.ServerId, cmd.Command, cancellationToken);
            }
        }
        finally
        {
            _queueProcessor.Release();
        }
    }

    /// <summary>
    /// Send a broadcast message to all servers
    /// </summary>
    public async Task BroadcastCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var tasks = _serverContexts.Keys
            .Select(serverId => SendCommandAsync(serverId, command, cancellationToken));
        
        await Task.WhenAll(tasks);
        _logger.LogInformation("Broadcast command sent to {Count} servers", _serverContexts.Count);
    }

    /// <summary>
    /// Send a chat message to a server
    /// </summary>
    public Task<ServerCommandResult> SendChatAsync(int serverId, string message, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, $"sv_chat \"{EscapeMessage(message)}\"", cancellationToken);
    }

    /// <summary>
    /// Broadcast a chat message to all servers
    /// </summary>
    public async Task BroadcastChatAsync(string message, CancellationToken cancellationToken = default)
    {
        var command = $"sv_chat \"{EscapeMessage(message)}\"";
        await BroadcastCommandAsync(command, cancellationToken);
    }

    /// <summary>
    /// Kick a player from a server
    /// </summary>
    public Task<ServerCommandResult> KickPlayerAsync(int serverId, int clientId, string reason = "", CancellationToken cancellationToken = default)
    {
        var command = string.IsNullOrEmpty(reason)
            ? $"kick {clientId}"
            : $"kick {clientId} \"{EscapeMessage(reason)}\"";
        
        return SendCommandAsync(serverId, command, cancellationToken);
    }

    /// <summary>
    /// Ban a player from a server
    /// </summary>
    public Task<ServerCommandResult> BanPlayerAsync(int serverId, int accountId, int durationMinutes, string reason, CancellationToken cancellationToken = default)
    {
        var command = $"ban {accountId} {durationMinutes} \"{EscapeMessage(reason)}\"";
        return SendCommandAsync(serverId, command, cancellationToken);
    }

    /// <summary>
    /// Set a server cvar
    /// </summary>
    public Task<ServerCommandResult> SetCvarAsync(int serverId, string cvar, string value, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, $"set {cvar} \"{value}\"", cancellationToken);
    }

    /// <summary>
    /// Get server status
    /// </summary>
    public Task<ServerCommandResult> GetStatusAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, "status", cancellationToken);
    }

    /// <summary>
    /// Restart a match
    /// </summary>
    public Task<ServerCommandResult> RestartMatchAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, "restartmatch", cancellationToken);
    }

    /// <summary>
    /// End current game
    /// </summary>
    public Task<ServerCommandResult> EndGameAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, "endgame", cancellationToken);
    }

    /// <summary>
    /// Change map
    /// </summary>
    public Task<ServerCommandResult> ChangeMapAsync(int serverId, string mapName, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, $"map {mapName}", cancellationToken);
    }

    /// <summary>
    /// Pause game
    /// </summary>
    public Task<ServerCommandResult> PauseGameAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, "sv_pause 1", cancellationToken);
    }

    /// <summary>
    /// Resume game
    /// </summary>
    public Task<ServerCommandResult> ResumeGameAsync(int serverId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(serverId, "sv_pause 0", cancellationToken);
    }

    /// <summary>
    /// Execute a script file
    /// </summary>
    public async Task<ScriptExecutionResult> ExecuteScriptAsync(int serverId, string scriptPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(scriptPath))
        {
            return new ScriptExecutionResult
            {
                Success = false,
                FirstError = $"Script file not found: {scriptPath}"
            };
        }

        var result = new ScriptExecutionResult
        {
            ScriptPath = scriptPath,
            ServerId = serverId,
            StartTime = DateTime.UtcNow
        };

        var lines = await File.ReadAllLinesAsync(scriptPath, cancellationToken);
        var commandResults = new List<ServerCommandResult>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip comments and empty lines
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            // Handle delay commands
            if (trimmed.StartsWith("delay ", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed[6..], out var delayMs))
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
                continue;
            }

            var cmdResult = await SendCommandAsync(serverId, trimmed, cancellationToken);
            commandResults.Add(cmdResult);

            if (!cmdResult.Success)
            {
                result.FirstError = cmdResult.Error;
                if (!result.ContinueOnError)
                    break;
            }
        }

        result.EndTime = DateTime.UtcNow;
        result.Success = commandResults.All(r => r.Success);
        result.CommandsExecuted = commandResults.Count;
        result.CommandsFailed = commandResults.Count(r => !r.Success);

        return result;
    }

    /// <summary>
    /// Get command history for a server
    /// </summary>
    public IEnumerable<CommandHistoryEntry> GetCommandHistory(int serverId, int limit = 100)
    {
        if (_serverContexts.TryGetValue(serverId, out var context))
        {
            return context.CommandHistory
                .OrderByDescending(e => e.ExecutedAt)
                .Take(limit)
                .ToList();
        }

        return Enumerable.Empty<CommandHistoryEntry>();
    }

    /// <summary>
    /// Get registered server IDs
    /// </summary>
    public IEnumerable<int> GetRegisteredServers()
    {
        return _serverContexts.Keys.ToList();
    }

    private static string EscapeMessage(string message)
    {
        return message
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}

// Internal classes

internal class ServerCommandContext
{
    public int ServerId { get; set; }
    public Process Process { get; set; } = null!;
    public DateTime RegisteredAt { get; set; }
    public List<CommandHistoryEntry> CommandHistory { get; set; } = new();
}

internal class QueuedServerCommand
{
    public int ServerId { get; set; }
    public string Command { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime QueuedAt { get; set; }
}

// DTOs

public class ServerCommandResult
{
    public bool Success { get; set; }
    public string Command { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public string? Error { get; set; }
    public long ExecutionTimeMs { get; set; }
}

public class CommandHistoryEntry
{
    public string Command { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public bool Success { get; set; }
}

public class ScriptExecutionResult
{
    public bool Success { get; set; }
    public string ScriptPath { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int CommandsExecuted { get; set; }
    public int CommandsFailed { get; set; }
    public string? FirstError { get; set; }
    public bool ContinueOnError { get; set; } = false;
}

public class CommandExecutedEventArgs : EventArgs
{
    public int ServerId { get; }
    public string Command { get; }
    public bool Success { get; }

    public CommandExecutedEventArgs(int serverId, string command, bool success)
    {
        ServerId = serverId;
        Command = command;
        Success = success;
    }
}
