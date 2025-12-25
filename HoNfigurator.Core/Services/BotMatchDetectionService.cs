using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Detects and optionally rejects bot matches.
/// Port of Python HoNfigurator-Central bot detection functionality.
/// </summary>
public class BotMatchDetectionService
{
    private readonly ILogger<BotMatchDetectionService> _logger;
    private readonly HoNConfiguration _config;
    private readonly HashSet<string> _knownBotPatterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _whitelistedAccounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MatchBotAnalysis> _matchAnalyses = new();
    private readonly object _lock = new();

    // Default bot name patterns
    private static readonly string[] DefaultBotPatterns = new[]
    {
        "Bot",
        "AI_",
        "Computer",
        "[Bot]",
        "HoN_Bot",
        "AutoPlayer",
        "NPC_"
    };

    public BotMatchDetectionService(ILogger<BotMatchDetectionService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;

        // Load default patterns
        foreach (var pattern in DefaultBotPatterns)
        {
            _knownBotPatterns.Add(pattern);
        }
    }

    /// <summary>
    /// Add a bot name pattern to detect
    /// </summary>
    public void AddBotPattern(string pattern)
    {
        lock (_lock)
        {
            _knownBotPatterns.Add(pattern);
            _logger.LogDebug("Added bot pattern: {Pattern}", pattern);
        }
    }

    /// <summary>
    /// Add a whitelisted account (never considered a bot)
    /// </summary>
    public void AddWhitelistedAccount(string accountName)
    {
        lock (_lock)
        {
            _whitelistedAccounts.Add(accountName);
            _logger.LogDebug("Whitelisted account: {Account}", accountName);
        }
    }

    /// <summary>
    /// Remove a whitelisted account
    /// </summary>
    public bool RemoveWhitelistedAccount(string accountName)
    {
        lock (_lock)
        {
            return _whitelistedAccounts.Remove(accountName);
        }
    }

    /// <summary>
    /// Get all bot patterns
    /// </summary>
    public IReadOnlySet<string> GetBotPatterns()
    {
        lock (_lock)
        {
            return _knownBotPatterns.ToHashSet();
        }
    }

    /// <summary>
    /// Get all whitelisted accounts
    /// </summary>
    public IReadOnlySet<string> GetWhitelistedAccounts()
    {
        lock (_lock)
        {
            return _whitelistedAccounts.ToHashSet();
        }
    }

    /// <summary>
    /// Analyze a player to determine if they're likely a bot
    /// </summary>
    public BotDetectionResult AnalyzePlayer(BotCheckPlayerInfo player)
    {
        var result = new BotDetectionResult
        {
            AccountId = player.AccountId,
            AccountName = player.AccountName,
            IsBot = false,
            Confidence = 0
        };

        // Whitelisted accounts are never bots
        lock (_lock)
        {
            if (_whitelistedAccounts.Contains(player.AccountName))
            {
                result.Reason = "Whitelisted";
                return result;
            }
        }

        var indicators = new List<string>();
        double confidence = 0;

        // Check name patterns
        lock (_lock)
        {
            foreach (var pattern in _knownBotPatterns)
            {
                if (player.AccountName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    indicators.Add($"Name contains bot pattern: {pattern}");
                    confidence += 40;
                    break;
                }
            }
        }

        // Check for account ID of 0 (often bots)
        if (player.AccountId == 0)
        {
            indicators.Add("Account ID is 0");
            confidence += 30;
        }

        // Check for very new accounts (created today)
        if (player.CreatedDate.HasValue && 
            (DateTime.UtcNow - player.CreatedDate.Value).TotalDays < 1)
        {
            indicators.Add("Account created today");
            confidence += 10;
        }

        // Check for suspicious stat patterns
        if (player.GamesPlayed > 0)
        {
            // Bots often have exactly 0 disconnects
            if (player.Disconnects == 0 && player.GamesPlayed > 100)
            {
                indicators.Add("No disconnects in 100+ games");
                confidence += 15;
            }

            // Bots sometimes have identical win/loss in all modes
            if (player.Wins == player.Losses && player.GamesPlayed > 10)
            {
                indicators.Add("Exactly equal wins/losses");
                confidence += 10;
            }
        }

        // Check for suspicious ping patterns
        if (player.Ping == 0)
        {
            indicators.Add("Zero ping");
            confidence += 20;
        }

        // Finalize result
        result.Confidence = Math.Min(100, (int)confidence);
        result.IsBot = result.Confidence >= 60;
        result.Reason = indicators.Count > 0 
            ? string.Join("; ", indicators) 
            : "No bot indicators found";

        return result;
    }

    /// <summary>
    /// Analyze all players in a match
    /// </summary>
    public MatchBotAnalysis AnalyzeMatch(int matchId, IEnumerable<BotCheckPlayerInfo> players)
    {
        var analysis = new MatchBotAnalysis
        {
            MatchId = matchId,
            AnalyzedAt = DateTime.UtcNow
        };

        foreach (var player in players)
        {
            var result = AnalyzePlayer(player);
            analysis.PlayerResults.Add(result);

            if (result.IsBot)
            {
                analysis.BotCount++;
            }
        }

        analysis.TotalPlayers = analysis.PlayerResults.Count;
        analysis.IsBotMatch = ShouldRejectAsBot(analysis);

        if (analysis.IsBotMatch)
        {
            _logger.LogWarning("Match {MatchId} detected as bot match: {BotCount}/{Total} bots",
                matchId, analysis.BotCount, analysis.TotalPlayers);
        }

        lock (_lock)
        {
            _matchAnalyses[matchId] = analysis;
        }

        return analysis;
    }

    /// <summary>
    /// Get analysis for a match
    /// </summary>
    public MatchBotAnalysis? GetMatchAnalysis(int matchId)
    {
        lock (_lock)
        {
            return _matchAnalyses.TryGetValue(matchId, out var analysis) ? analysis : null;
        }
    }

    /// <summary>
    /// Determine if a match should be rejected as a bot match
    /// </summary>
    private bool ShouldRejectAsBot(MatchBotAnalysis analysis)
    {
        var botMatchEnabled = _config.HonData?.EnableBotMatch ?? true;
        
        // If bot matches are enabled, don't reject
        if (botMatchEnabled)
            return false;

        // Reject if more than half the players are bots
        var botRatio = analysis.TotalPlayers > 0 
            ? (double)analysis.BotCount / analysis.TotalPlayers 
            : 0;

        return botRatio > 0.5;
    }

    /// <summary>
    /// Clear old match analyses (older than specified hours)
    /// </summary>
    public int ClearOldAnalyses(int hoursOld = 24)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hoursOld);
        int removed = 0;

        lock (_lock)
        {
            var toRemove = _matchAnalyses
                .Where(kvp => kvp.Value.AnalyzedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _matchAnalyses.Remove(key);
                removed++;
            }
        }

        return removed;
    }
}

// DTOs

public class BotCheckPlayerInfo
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Disconnects { get; set; }
    public int Ping { get; set; }
    public DateTime? CreatedDate { get; set; }
}

public class BotDetectionResult
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public bool IsBot { get; set; }
    public int Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class MatchBotAnalysis
{
    public int MatchId { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public int TotalPlayers { get; set; }
    public int BotCount { get; set; }
    public bool IsBotMatch { get; set; }
    public List<BotDetectionResult> PlayerResults { get; set; } = new();
}
