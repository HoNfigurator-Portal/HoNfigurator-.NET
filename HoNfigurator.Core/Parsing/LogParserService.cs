using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Parses game server log files to extract useful information.
/// Port of Python HoNfigurator-Central logparser.py functionality.
/// </summary>
public partial class LogParserService
{
    private readonly ILogger<LogParserService> _logger;
    private readonly HoNConfiguration _config;

    // Regex patterns for log parsing
    [GeneratedRegex(@"Player connected: (\S+) \((\d+)\) from (\d+\.\d+\.\d+\.\d+):\d+", RegexOptions.Compiled)]
    private static partial Regex PlayerConnectPattern();

    [GeneratedRegex(@"Match ID: (\d+)", RegexOptions.Compiled)]
    private static partial Regex MatchIdPattern();

    [GeneratedRegex(@"Starting map: (\S+)", RegexOptions.Compiled)]
    private static partial Regex MapPattern();

    [GeneratedRegex(@"Game mode: (\S+)", RegexOptions.Compiled)]
    private static partial Regex GameModePattern();

    [GeneratedRegex(@"Server name: (.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ServerNamePattern();

    [GeneratedRegex(@"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]", RegexOptions.Compiled)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"Player disconnected: (\S+) \((\d+)\)", RegexOptions.Compiled)]
    private static partial Regex PlayerDisconnectPattern();

    [GeneratedRegex(@"Match ended. Winner: (Legion|Hellbourne|Draw)", RegexOptions.Compiled)]
    private static partial Regex MatchEndPattern();

    [GeneratedRegex(@"ERROR: (.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"WARNING: (.+)$", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex WarningPattern();

    [GeneratedRegex(@"Skipped (\d+) frames", RegexOptions.Compiled)]
    private static partial Regex SkippedFramesPattern();

    [GeneratedRegex(@"Server lag detected: (\d+)ms", RegexOptions.Compiled)]
    private static partial Regex ServerLagPattern();

    public LogParserService(ILogger<LogParserService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Parse a log file and extract all relevant information
    /// </summary>
    public async Task<LogParseResult> ParseLogFileAsync(string logPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(logPath))
        {
            return new LogParseResult
            {
                Success = false,
                Error = $"Log file not found: {logPath}"
            };
        }

        var result = new LogParseResult
        {
            LogPath = logPath,
            ParsedAt = DateTime.UtcNow,
            Success = true
        };

        try
        {
            var lines = await File.ReadAllLinesAsync(logPath, cancellationToken);
            result.TotalLines = lines.Length;

            foreach (var line in lines)
            {
                ParseLine(line, result);
            }

            // Calculate stats
            result.MatchDuration = result.MatchEndTime.HasValue && result.MatchStartTime.HasValue
                ? result.MatchEndTime.Value - result.MatchStartTime.Value
                : null;

            _logger.LogDebug("Parsed {Lines} lines from {Path}", lines.Length, logPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse log file: {Path}", logPath);
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Extract player IPs from a log file
    /// </summary>
    public async Task<List<PlayerConnection>> ExtractPlayerConnectionsAsync(
        string logPath, 
        CancellationToken cancellationToken = default)
    {
        var connections = new List<PlayerConnection>();

        if (!File.Exists(logPath))
            return connections;

        try
        {
            await foreach (var line in File.ReadLinesAsync(logPath, cancellationToken))
            {
                var match = PlayerConnectPattern().Match(line);
                if (match.Success)
                {
                    connections.Add(new PlayerConnection
                    {
                        PlayerName = match.Groups[1].Value,
                        PlayerId = int.Parse(match.Groups[2].Value),
                        IpAddress = match.Groups[3].Value,
                        Timestamp = ExtractTimestamp(line)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract player connections from {Path}", logPath);
        }

        return connections;
    }

    /// <summary>
    /// Extract match ID from log file
    /// </summary>
    public async Task<long?> ExtractMatchIdAsync(string logPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(logPath))
            return null;

        try
        {
            await foreach (var line in File.ReadLinesAsync(logPath, cancellationToken))
            {
                var match = MatchIdPattern().Match(line);
                if (match.Success && long.TryParse(match.Groups[1].Value, out var matchId))
                {
                    return matchId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract match ID from {Path}", logPath);
        }

        return null;
    }

    /// <summary>
    /// Extract game info (map, mode, server name) from log
    /// </summary>
    public async Task<GameInfo?> ExtractGameInfoAsync(string logPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(logPath))
            return null;

        var gameInfo = new GameInfo();
        var foundAny = false;

        try
        {
            var content = await File.ReadAllTextAsync(logPath, cancellationToken);

            var mapMatch = MapPattern().Match(content);
            if (mapMatch.Success)
            {
                gameInfo.MapName = mapMatch.Groups[1].Value;
                foundAny = true;
            }

            var modeMatch = GameModePattern().Match(content);
            if (modeMatch.Success)
            {
                gameInfo.GameMode = modeMatch.Groups[1].Value;
                foundAny = true;
            }

            var nameMatch = ServerNamePattern().Match(content);
            if (nameMatch.Success)
            {
                gameInfo.ServerName = nameMatch.Groups[1].Value.Trim();
                foundAny = true;
            }

            var idMatch = MatchIdPattern().Match(content);
            if (idMatch.Success && long.TryParse(idMatch.Groups[1].Value, out var matchId))
            {
                gameInfo.MatchId = matchId;
                foundAny = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract game info from {Path}", logPath);
        }

        return foundAny ? gameInfo : null;
    }

    /// <summary>
    /// Get errors and warnings from log file
    /// </summary>
    public async Task<LogIssues> ExtractIssuesAsync(
        string logPath, 
        int maxIssues = 100,
        CancellationToken cancellationToken = default)
    {
        var issues = new LogIssues();

        if (!File.Exists(logPath))
            return issues;

        try
        {
            await foreach (var line in File.ReadLinesAsync(logPath, cancellationToken))
            {
                if (issues.Errors.Count + issues.Warnings.Count >= maxIssues)
                    break;

                var errorMatch = ErrorPattern().Match(line);
                if (errorMatch.Success)
                {
                    issues.Errors.Add(new LogIssue
                    {
                        Message = errorMatch.Groups[1].Value,
                        Timestamp = ExtractTimestamp(line),
                        Line = line
                    });
                    continue;
                }

                var warningMatch = WarningPattern().Match(line);
                if (warningMatch.Success)
                {
                    issues.Warnings.Add(new LogIssue
                    {
                        Message = warningMatch.Groups[1].Value,
                        Timestamp = ExtractTimestamp(line),
                        Line = line
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract issues from {Path}", logPath);
        }

        return issues;
    }

    /// <summary>
    /// Get lag/performance issues from log
    /// </summary>
    public async Task<List<LagEvent>> ExtractLagEventsAsync(
        string logPath,
        CancellationToken cancellationToken = default)
    {
        var events = new List<LagEvent>();

        if (!File.Exists(logPath))
            return events;

        try
        {
            await foreach (var line in File.ReadLinesAsync(logPath, cancellationToken))
            {
                var frameMatch = SkippedFramesPattern().Match(line);
                if (frameMatch.Success && int.TryParse(frameMatch.Groups[1].Value, out var frames))
                {
                    events.Add(new LagEvent
                    {
                        Type = LagEventType.SkippedFrames,
                        Value = frames,
                        Timestamp = ExtractTimestamp(line)
                    });
                    continue;
                }

                var lagMatch = ServerLagPattern().Match(line);
                if (lagMatch.Success && int.TryParse(lagMatch.Groups[1].Value, out var lagMs))
                {
                    events.Add(new LagEvent
                    {
                        Type = LagEventType.ServerLag,
                        Value = lagMs,
                        Timestamp = ExtractTimestamp(line)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract lag events from {Path}", logPath);
        }

        return events;
    }

    /// <summary>
    /// Watch a log file for new entries (tail -f style)
    /// </summary>
    public async IAsyncEnumerable<string> TailLogAsync(
        string logPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(logPath))
            yield break;

        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        // Start from end
        fs.Seek(0, SeekOrigin.End);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            
            if (line != null)
            {
                yield return line;
            }
            else
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Parse recent log entries from multiple server logs
    /// </summary>
    public async Task<List<ServerLogSummary>> GetRecentLogSummariesAsync(
        string logsDirectory,
        int maxServers = 10,
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<ServerLogSummary>();

        if (!Directory.Exists(logsDirectory))
            return summaries;

        var logFiles = Directory.GetFiles(logsDirectory, "*.log")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(maxServers);

        foreach (var file in logFiles)
        {
            var parseResult = await ParseLogFileAsync(file.FullName, cancellationToken);
            if (parseResult.Success)
            {
                summaries.Add(new ServerLogSummary
                {
                    LogFile = file.Name,
                    LastModified = file.LastWriteTimeUtc,
                    MatchId = parseResult.MatchId,
                    MapName = parseResult.MapName,
                    PlayerCount = parseResult.PlayerConnections.Count,
                    ErrorCount = parseResult.Errors.Count,
                    WarningCount = parseResult.Warnings.Count,
                    TotalLines = parseResult.TotalLines
                });
            }
        }

        return summaries;
    }

    private void ParseLine(string line, LogParseResult result)
    {
        // Player connections
        var playerMatch = PlayerConnectPattern().Match(line);
        if (playerMatch.Success)
        {
            result.PlayerConnections.Add(new PlayerConnection
            {
                PlayerName = playerMatch.Groups[1].Value,
                PlayerId = int.Parse(playerMatch.Groups[2].Value),
                IpAddress = playerMatch.Groups[3].Value,
                Timestamp = ExtractTimestamp(line)
            });
            return;
        }

        // Player disconnections
        var disconnectMatch = PlayerDisconnectPattern().Match(line);
        if (disconnectMatch.Success)
        {
            result.PlayerDisconnections.Add(new PlayerDisconnection
            {
                PlayerName = disconnectMatch.Groups[1].Value,
                PlayerId = int.Parse(disconnectMatch.Groups[2].Value),
                Timestamp = ExtractTimestamp(line)
            });
            return;
        }

        // Match ID
        var matchIdMatch = MatchIdPattern().Match(line);
        if (matchIdMatch.Success && long.TryParse(matchIdMatch.Groups[1].Value, out var matchId))
        {
            result.MatchId = matchId;
            result.MatchStartTime ??= ExtractTimestamp(line);
            return;
        }

        // Map
        var mapMatch = MapPattern().Match(line);
        if (mapMatch.Success)
        {
            result.MapName = mapMatch.Groups[1].Value;
            return;
        }

        // Game mode
        var modeMatch = GameModePattern().Match(line);
        if (modeMatch.Success)
        {
            result.GameMode = modeMatch.Groups[1].Value;
            return;
        }

        // Match end
        var endMatch = MatchEndPattern().Match(line);
        if (endMatch.Success)
        {
            result.MatchResult = endMatch.Groups[1].Value;
            result.MatchEndTime = ExtractTimestamp(line);
            return;
        }

        // Errors
        var errorMatch = ErrorPattern().Match(line);
        if (errorMatch.Success)
        {
            result.Errors.Add(new LogIssue
            {
                Message = errorMatch.Groups[1].Value,
                Timestamp = ExtractTimestamp(line),
                Line = line
            });
            return;
        }

        // Warnings
        var warningMatch = WarningPattern().Match(line);
        if (warningMatch.Success)
        {
            result.Warnings.Add(new LogIssue
            {
                Message = warningMatch.Groups[1].Value,
                Timestamp = ExtractTimestamp(line),
                Line = line
            });
        }
    }

    private DateTime? ExtractTimestamp(string line)
    {
        var match = TimestampPattern().Match(line);
        if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var timestamp))
        {
            return timestamp;
        }
        return null;
    }
}

// DTOs

public class LogParseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string LogPath { get; set; } = string.Empty;
    public DateTime ParsedAt { get; set; }
    public int TotalLines { get; set; }

    public long? MatchId { get; set; }
    public string? MapName { get; set; }
    public string? GameMode { get; set; }
    public string? MatchResult { get; set; }
    public DateTime? MatchStartTime { get; set; }
    public DateTime? MatchEndTime { get; set; }
    public TimeSpan? MatchDuration { get; set; }

    public List<PlayerConnection> PlayerConnections { get; set; } = new();
    public List<PlayerDisconnection> PlayerDisconnections { get; set; } = new();
    public List<LogIssue> Errors { get; set; } = new();
    public List<LogIssue> Warnings { get; set; } = new();
}

public class PlayerConnection
{
    public string PlayerName { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
}

public class PlayerDisconnection
{
    public string PlayerName { get; set; } = string.Empty;
    public int PlayerId { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class GameInfo
{
    public long? MatchId { get; set; }
    public string? MapName { get; set; }
    public string? GameMode { get; set; }
    public string? ServerName { get; set; }
}

public class LogIssues
{
    public List<LogIssue> Errors { get; set; } = new();
    public List<LogIssue> Warnings { get; set; } = new();
}

public class LogIssue
{
    public string Message { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    public string Line { get; set; } = string.Empty;
}

public class LagEvent
{
    public LagEventType Type { get; set; }
    public int Value { get; set; }
    public DateTime? Timestamp { get; set; }
}

public enum LagEventType
{
    SkippedFrames,
    ServerLag
}

public class ServerLogSummary
{
    public string LogFile { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long? MatchId { get; set; }
    public string? MapName { get; set; }
    public int PlayerCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int TotalLines { get; set; }
}
