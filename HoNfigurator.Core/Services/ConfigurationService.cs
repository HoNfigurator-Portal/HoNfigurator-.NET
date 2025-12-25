using System.Text.Json;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

public interface IConfigurationService
{
    Task<HoNConfiguration> LoadConfigurationAsync();
    Task SaveConfigurationAsync(HoNConfiguration config);
    Task SaveConfigurationAsync();
    Task ReloadAsync(CancellationToken cancellationToken = default);
    HoNConfiguration Configuration { get; }
}

public class ConfigurationService : IConfigurationService
{
    private readonly string _configPath;
    private HoNConfiguration _configuration = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public HoNConfiguration Configuration => _configuration;

    public ConfigurationService(string configPath)
    {
        _configPath = configPath;
    }

    public async Task<HoNConfiguration> LoadConfigurationAsync()
    {
        if (!File.Exists(_configPath))
        {
            _configuration = new HoNConfiguration();
            await SaveConfigurationAsync(_configuration);
            return _configuration;
        }

        var json = await File.ReadAllTextAsync(_configPath);
        _configuration = JsonSerializer.Deserialize<HoNConfiguration>(json, JsonOptions) ?? new HoNConfiguration();
        return _configuration;
    }

    public async Task SaveConfigurationAsync(HoNConfiguration config)
    {
        _configuration = config;
        await SaveConfigurationAsync();
    }

    public async Task SaveConfigurationAsync()
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_configuration, JsonOptions);
        await File.WriteAllTextAsync(_configPath, json);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadConfigurationAsync();
    }
}
