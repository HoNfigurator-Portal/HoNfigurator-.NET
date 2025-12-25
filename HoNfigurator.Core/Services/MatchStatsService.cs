using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Service for submitting match statistics to the master server.
/// Port of Python's resubmit_match_stats functionality.
/// </summary>
public interface IMatchStatsService
{
    Task<SubmitResult> SubmitMatchStatsAsync(MatchStats stats);
    Task<SubmitResult> ResubmitPendingStatsAsync();
    void QueueStats(MatchStats stats);
}

public class MatchStats
{
    public long MatchId { get; set; }
    public string? ServerName { get; set; }
    public int ServerPort { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Map { get; set; }
    public string? GameMode { get; set; }
    public int WinningTeam { get; set; }  // 1 = Legion, 2 = Hellbourne
    public List<PlayerStats> Players { get; set; } = new();
    public Dictionary<string, object> Extra { get; set; } = new();
}

public class PlayerStats
{
    public int AccountId { get; set; }
    public string? Nickname { get; set; }
    public int Team { get; set; }
    public int HeroId { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int CreepKills { get; set; }
    public int CreepDenies { get; set; }
    public int NeutralKills { get; set; }
    public int BuildingDamage { get; set; }
    public int HeroDamage { get; set; }
    public int GoldEarned { get; set; }
    public int XpEarned { get; set; }
    public List<int> Items { get; set; } = new();
    public bool Disconnected { get; set; }
    public int DisconnectTime { get; set; }
}

public class SubmitResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int Submitted { get; set; }
    public int Failed { get; set; }
    public string? Response { get; set; }
}

public class MatchStatsService : IMatchStatsService
{
    private readonly ILogger<MatchStatsService> _logger;
    private readonly HoNConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly Queue<MatchStats> _pendingStats = new();
    private readonly string _statsFilePath;
    private readonly object _lock = new();

    public MatchStatsService(ILogger<MatchStatsService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Configure stats storage directory
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator", "stats");
        Directory.CreateDirectory(dataDir);
        _statsFilePath = Path.Combine(dataDir, "pending_stats.json");

        // Load any pending stats from disk
        LoadPendingStats();
    }

    public void QueueStats(MatchStats stats)
    {
        lock (_lock)
        {
            _pendingStats.Enqueue(stats);
            SavePendingStats();
        }
        
        _logger.LogInformation("Queued match stats for match {MatchId}", stats.MatchId);
    }

    public async Task<SubmitResult> SubmitMatchStatsAsync(MatchStats stats)
    {
        var result = new SubmitResult();

        try
        {
            var masterServer = _config.HonData.MasterServer ?? "api.kongor.net";
            var url = $"https://{masterServer}/client_requester.php";

            // Build stats payload like Python version
            var payload = new Dictionary<string, object>
            {
                ["f"] = "submit_stats",
                ["match_id"] = stats.MatchId,
                ["server_name"] = stats.ServerName ?? "",
                ["server_port"] = stats.ServerPort,
                ["start_time"] = new DateTimeOffset(stats.StartTime).ToUnixTimeSeconds(),
                ["end_time"] = new DateTimeOffset(stats.EndTime).ToUnixTimeSeconds(),
                ["duration"] = (int)stats.Duration.TotalSeconds,
                ["map"] = stats.Map ?? "caldavar",
                ["game_mode"] = stats.GameMode ?? "normal",
                ["winning_team"] = stats.WinningTeam,
                ["player_count"] = stats.Players.Count
            };

            // Add player stats
            for (int i = 0; i < stats.Players.Count; i++)
            {
                var p = stats.Players[i];
                var prefix = $"player_{i}_";
                payload[$"{prefix}account_id"] = p.AccountId;
                payload[$"{prefix}nickname"] = p.Nickname ?? "";
                payload[$"{prefix}team"] = p.Team;
                payload[$"{prefix}hero_id"] = p.HeroId;
                payload[$"{prefix}kills"] = p.Kills;
                payload[$"{prefix}deaths"] = p.Deaths;
                payload[$"{prefix}assists"] = p.Assists;
                payload[$"{prefix}creep_kills"] = p.CreepKills;
                payload[$"{prefix}creep_denies"] = p.CreepDenies;
                payload[$"{prefix}neutral_kills"] = p.NeutralKills;
                payload[$"{prefix}building_damage"] = p.BuildingDamage;
                payload[$"{prefix}hero_damage"] = p.HeroDamage;
                payload[$"{prefix}gold"] = p.GoldEarned;
                payload[$"{prefix}xp"] = p.XpEarned;
                payload[$"{prefix}items"] = string.Join(",", p.Items);
                payload[$"{prefix}disconnected"] = p.Disconnected ? 1 : 0;
            }

            // Add extra data
            foreach (var kvp in stats.Extra)
            {
                payload[$"extra_{kvp.Key}"] = kvp.Value;
            }

            // Convert to form data
            var formData = payload.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "");

            var content = new FormUrlEncodedContent(formData);
            
            _logger.LogDebug("Submitting stats for match {MatchId} to {Url}", stats.MatchId, url);
            
            var response = await _httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && !responseText.Contains("error"))
            {
                result.Success = true;
                result.Submitted = 1;
                result.Response = responseText;
                _logger.LogInformation("Successfully submitted stats for match {MatchId}", stats.MatchId);
            }
            else
            {
                result.Error = $"Server returned: {responseText}";
                result.Failed = 1;
                _logger.LogWarning("Failed to submit stats for match {MatchId}: {Response}", 
                    stats.MatchId, responseText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting stats for match {MatchId}", stats.MatchId);
            result.Error = ex.Message;
            result.Failed = 1;
        }

        return result;
    }

    public async Task<SubmitResult> ResubmitPendingStatsAsync()
    {
        var result = new SubmitResult();
        var toProcess = new List<MatchStats>();

        lock (_lock)
        {
            while (_pendingStats.Count > 0)
            {
                toProcess.Add(_pendingStats.Dequeue());
            }
        }

        if (toProcess.Count == 0)
        {
            _logger.LogInformation("No pending stats to submit");
            result.Success = true;
            return result;
        }

        _logger.LogInformation("Resubmitting {Count} pending match stats", toProcess.Count);

        var failedStats = new List<MatchStats>();

        foreach (var stats in toProcess)
        {
            var submitResult = await SubmitMatchStatsAsync(stats);
            
            if (submitResult.Success)
            {
                result.Submitted++;
            }
            else
            {
                result.Failed++;
                failedStats.Add(stats);
            }

            // Small delay between submissions
            await Task.Delay(500);
        }

        // Re-queue failed submissions
        lock (_lock)
        {
            foreach (var stats in failedStats)
            {
                _pendingStats.Enqueue(stats);
            }
            SavePendingStats();
        }

        result.Success = result.Failed == 0;
        if (result.Failed > 0)
        {
            result.Error = $"{result.Failed} stats failed to submit and will be retried";
        }

        _logger.LogInformation("Stats submission complete: {Submitted} submitted, {Failed} failed",
            result.Submitted, result.Failed);

        return result;
    }

    private void LoadPendingStats()
    {
        try
        {
            if (File.Exists(_statsFilePath))
            {
                var json = File.ReadAllText(_statsFilePath);
                var stats = JsonSerializer.Deserialize<List<MatchStats>>(json);
                
                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        _pendingStats.Enqueue(stat);
                    }
                    _logger.LogInformation("Loaded {Count} pending stats from disk", stats.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pending stats from disk");
        }
    }

    private void SavePendingStats()
    {
        try
        {
            var stats = _pendingStats.ToList();
            var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(_statsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save pending stats to disk");
        }
    }
}
