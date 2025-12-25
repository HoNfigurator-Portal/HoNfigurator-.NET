using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages server presets for quick configuration deployment.
/// Port of Python HoNfigurator-Central preset functionality.
/// </summary>
public class ServerPresetService
{
    private readonly ILogger<ServerPresetService> _logger;
    private readonly ConcurrentDictionary<string, ServerPreset> _presets = new();
    private readonly string _presetsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public ServerPresetService(ILogger<ServerPresetService> logger)
    {
        _logger = logger;
        _presetsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator", "presets");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        Directory.CreateDirectory(_presetsDirectory);
        LoadPresets();
    }

    /// <summary>
    /// Create a new preset from current server configuration
    /// </summary>
    public async Task<ServerPreset> CreatePresetAsync(string name, string description, GameServerConfiguration serverConfig, CancellationToken cancellationToken = default)
    {
        var preset = new ServerPreset
        {
            Id = GeneratePresetId(name),
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            Configuration = CloneConfiguration(serverConfig)
        };

        _presets[preset.Id] = preset;
        await SavePresetAsync(preset, cancellationToken);

        _logger.LogInformation("Created preset: {Name} ({Id})", name, preset.Id);
        return preset;
    }

    /// <summary>
    /// Apply a preset to a server configuration
    /// </summary>
    public PresetApplyResult ApplyPreset(string presetId, GameServerConfiguration targetConfig)
    {
        if (!_presets.TryGetValue(presetId, out var preset))
        {
            return new PresetApplyResult
            {
                Success = false,
                Error = $"Preset not found: {presetId}"
            };
        }

        var result = new PresetApplyResult
        {
            PresetId = presetId,
            PresetName = preset.Name
        };

        try
        {
            // Apply configuration values
            targetConfig.ServerName = preset.Configuration.ServerName;
            targetConfig.MaxPlayers = preset.Configuration.MaxPlayers;
            targetConfig.GameMode = preset.Configuration.GameMode;
            targetConfig.MapRotation = new List<string>(preset.Configuration.MapRotation);
            targetConfig.AllowedHeroes = new List<string>(preset.Configuration.AllowedHeroes);
            targetConfig.BannedHeroes = new List<string>(preset.Configuration.BannedHeroes);
            targetConfig.Password = preset.Configuration.Password;
            targetConfig.IsPrivate = preset.Configuration.IsPrivate;
            
            // Apply custom cvars
            targetConfig.CustomCvars.Clear();
            foreach (var cvar in preset.Configuration.CustomCvars)
            {
                targetConfig.CustomCvars[cvar.Key] = cvar.Value;
            }

            result.Success = true;
            result.AppliedSettings = GetAppliedSettingsList(preset.Configuration);
            
            _logger.LogInformation("Applied preset {Name} to server configuration", preset.Name);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Failed to apply preset {Name}", preset.Name);
        }

        return result;
    }

    /// <summary>
    /// Update an existing preset
    /// </summary>
    public async Task<bool> UpdatePresetAsync(string presetId, ServerPreset updatedPreset, CancellationToken cancellationToken = default)
    {
        if (!_presets.ContainsKey(presetId))
            return false;

        updatedPreset.Id = presetId;
        updatedPreset.UpdatedAt = DateTime.UtcNow;
        
        _presets[presetId] = updatedPreset;
        await SavePresetAsync(updatedPreset, cancellationToken);

        _logger.LogInformation("Updated preset: {Id}", presetId);
        return true;
    }

    /// <summary>
    /// Delete a preset
    /// </summary>
    public async Task<bool> DeletePresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        if (!_presets.TryRemove(presetId, out _))
            return false;

        var filePath = GetPresetFilePath(presetId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        _logger.LogInformation("Deleted preset: {Id}", presetId);
        return true;
    }

    /// <summary>
    /// Get a preset by ID
    /// </summary>
    public ServerPreset? GetPreset(string presetId)
    {
        return _presets.TryGetValue(presetId, out var preset) ? preset : null;
    }

    /// <summary>
    /// List all presets
    /// </summary>
    public IEnumerable<ServerPreset> ListPresets()
    {
        return _presets.Values
            .OrderBy(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// Search presets by name or tag
    /// </summary>
    public IEnumerable<ServerPreset> SearchPresets(string query)
    {
        var lowerQuery = query.ToLowerInvariant();
        
        return _presets.Values
            .Where(p => p.Name.ToLowerInvariant().Contains(lowerQuery) ||
                        p.Description.ToLowerInvariant().Contains(lowerQuery) ||
                        p.Tags.Any(t => t.ToLowerInvariant().Contains(lowerQuery)))
            .ToList();
    }

    /// <summary>
    /// Export a preset to a file
    /// </summary>
    public async Task<bool> ExportPresetAsync(string presetId, string filePath, CancellationToken cancellationToken = default)
    {
        if (!_presets.TryGetValue(presetId, out var preset))
            return false;

        var json = JsonSerializer.Serialize(preset, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        _logger.LogInformation("Exported preset {Id} to {Path}", presetId, filePath);
        return true;
    }

    /// <summary>
    /// Import a preset from a file
    /// </summary>
    public async Task<ServerPreset?> ImportPresetAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var preset = JsonSerializer.Deserialize<ServerPreset>(json, _jsonOptions);

            if (preset == null)
                return null;

            // Generate new ID to avoid conflicts
            preset.Id = GeneratePresetId(preset.Name);
            preset.ImportedAt = DateTime.UtcNow;

            _presets[preset.Id] = preset;
            await SavePresetAsync(preset, cancellationToken);

            _logger.LogInformation("Imported preset from {Path}: {Id}", filePath, preset.Id);
            return preset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import preset from {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Clone a preset
    /// </summary>
    public async Task<ServerPreset?> ClonePresetAsync(string presetId, string newName, CancellationToken cancellationToken = default)
    {
        if (!_presets.TryGetValue(presetId, out var original))
            return null;

        var clone = new ServerPreset
        {
            Id = GeneratePresetId(newName),
            Name = newName,
            Description = $"Clone of {original.Name}",
            CreatedAt = DateTime.UtcNow,
            Tags = new List<string>(original.Tags),
            Configuration = CloneConfiguration(original.Configuration)
        };

        _presets[clone.Id] = clone;
        await SavePresetAsync(clone, cancellationToken);

        _logger.LogInformation("Cloned preset {Original} to {Clone}", original.Name, clone.Name);
        return clone;
    }

    /// <summary>
    /// Get built-in presets
    /// </summary>
    public IEnumerable<ServerPreset> GetBuiltInPresets()
    {
        return new List<ServerPreset>
        {
            new()
            {
                Id = "builtin-casual",
                Name = "Casual Game",
                Description = "Relaxed settings for casual play",
                IsBuiltIn = true,
                Tags = new() { "casual", "beginner" },
                Configuration = new GameServerConfiguration
                {
                    ServerName = "Casual Server",
                    MaxPlayers = 10,
                    GameMode = "normal",
                    CustomCvars = new Dictionary<string, string>
                    {
                        ["sv_allheroaccess"] = "1",
                        ["sv_noleaver"] = "0"
                    }
                }
            },
            new()
            {
                Id = "builtin-ranked",
                Name = "Ranked Match",
                Description = "Competitive settings for ranked play",
                IsBuiltIn = true,
                Tags = new() { "ranked", "competitive" },
                Configuration = new GameServerConfiguration
                {
                    ServerName = "Ranked Server",
                    MaxPlayers = 10,
                    GameMode = "normal",
                    CustomCvars = new Dictionary<string, string>
                    {
                        ["sv_allheroaccess"] = "1",
                        ["sv_noleaver"] = "1"
                    }
                }
            },
            new()
            {
                Id = "builtin-midwars",
                Name = "Mid Wars",
                Description = "Mid Wars game mode",
                IsBuiltIn = true,
                Tags = new() { "midwars", "action" },
                Configuration = new GameServerConfiguration
                {
                    ServerName = "Mid Wars Server",
                    MaxPlayers = 10,
                    GameMode = "midwars",
                    MapRotation = new() { "midwars" },
                    CustomCvars = new Dictionary<string, string>
                    {
                        ["sv_allheroaccess"] = "1"
                    }
                }
            },
            new()
            {
                Id = "builtin-custom",
                Name = "Custom Game",
                Description = "Custom game with flexible settings",
                IsBuiltIn = true,
                Tags = new() { "custom", "flexible" },
                Configuration = new GameServerConfiguration
                {
                    ServerName = "Custom Server",
                    MaxPlayers = 10,
                    GameMode = "custom"
                }
            }
        };
    }

    private void LoadPresets()
    {
        foreach (var file in Directory.GetFiles(_presetsDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<ServerPreset>(json, _jsonOptions);
                
                if (preset != null)
                {
                    _presets[preset.Id] = preset;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load preset from {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} presets", _presets.Count);
    }

    private async Task SavePresetAsync(ServerPreset preset, CancellationToken cancellationToken)
    {
        var filePath = GetPresetFilePath(preset.Id);
        var json = JsonSerializer.Serialize(preset, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private string GetPresetFilePath(string presetId)
    {
        return Path.Combine(_presetsDirectory, $"{presetId}.json");
    }

    private static string GeneratePresetId(string name)
    {
        var safeName = new string(name
            .ToLowerInvariant()
            .Replace(' ', '-')
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());
        
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"{safeName}-{suffix}";
    }

    private static GameServerConfiguration CloneConfiguration(GameServerConfiguration source)
    {
        return new GameServerConfiguration
        {
            ServerName = source.ServerName,
            MaxPlayers = source.MaxPlayers,
            GameMode = source.GameMode,
            MapRotation = new List<string>(source.MapRotation),
            AllowedHeroes = new List<string>(source.AllowedHeroes),
            BannedHeroes = new List<string>(source.BannedHeroes),
            Password = source.Password,
            IsPrivate = source.IsPrivate,
            CustomCvars = new Dictionary<string, string>(source.CustomCvars)
        };
    }

    private static List<string> GetAppliedSettingsList(GameServerConfiguration config)
    {
        var settings = new List<string>
        {
            $"ServerName: {config.ServerName}",
            $"MaxPlayers: {config.MaxPlayers}",
            $"GameMode: {config.GameMode}"
        };

        if (config.MapRotation.Count > 0)
            settings.Add($"Maps: {string.Join(", ", config.MapRotation)}");
        
        if (config.CustomCvars.Count > 0)
            settings.Add($"CustomCvars: {config.CustomCvars.Count} settings");

        return settings;
    }
}

// DTOs

public class ServerPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public bool IsBuiltIn { get; set; }
    public List<string> Tags { get; set; } = new();
    public GameServerConfiguration Configuration { get; set; } = new();
}

public class GameServerConfiguration
{
    public string ServerName { get; set; } = "HoN Server";
    public int MaxPlayers { get; set; } = 10;
    public string GameMode { get; set; } = "normal";
    public List<string> MapRotation { get; set; } = new();
    public List<string> AllowedHeroes { get; set; } = new();
    public List<string> BannedHeroes { get; set; } = new();
    public string? Password { get; set; }
    public bool IsPrivate { get; set; }
    public Dictionary<string, string> CustomCvars { get; set; } = new();
}

public class PresetApplyResult
{
    public bool Success { get; set; }
    public string PresetId { get; set; } = string.Empty;
    public string PresetName { get; set; } = string.Empty;
    public List<string> AppliedSettings { get; set; } = new();
    public string? Error { get; set; }
}
