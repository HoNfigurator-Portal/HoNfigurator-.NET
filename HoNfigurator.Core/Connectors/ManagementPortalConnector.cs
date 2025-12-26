using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Connectors;

/// <summary>
/// Service for integrating with management.honfigurator.app
/// Handles registration, health checks, and remote management communication
/// </summary>
public interface IManagementPortalConnector
{
    bool IsRegistered { get; }
    bool IsEnabled { get; }
    string? ServerName { get; }
    string? ServerAddress { get; }
    
    Task<RegistrationResult> RegisterServerAsync(CancellationToken cancellationToken = default);
    Task<bool> PingManagementPortalAsync(CancellationToken cancellationToken = default);
    Task<DiscordUserInfo?> GetDiscordUserInfoAsync(string discordId, CancellationToken cancellationToken = default);
    Task ReportServerStatusAsync(ServerStatusReport status, CancellationToken cancellationToken = default);
    Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of management portal connector
/// Based on HoNfigurator-Central Python integration
/// </summary>
public class ManagementPortalConnector : IManagementPortalConnector, IDisposable
{
    private readonly ILogger<ManagementPortalConnector> _logger;
    private readonly HoNConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly ManagementPortalSettings _settings;
    private bool _isRegistered;
    private bool _disposed;

    public bool IsRegistered => _isRegistered;
    public bool IsEnabled => _settings.Enabled;
    public string? ServerName => _config.HonData?.ServerName;
    public string? ServerAddress => _config.HonData?.ServerIp;
    
    private string PortalUrl => _settings.PortalUrl;
    private string ApiUrl => $"{_settings.PortalUrl}/api";

    public ManagementPortalConnector(ILogger<ManagementPortalConnector> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        _settings = config.ApplicationData?.ManagementPortal ?? new ManagementPortalSettings();
        
        var handler = CreateHttpHandler();
        
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoNfigurator-dotnet/1.0");
        
        // Add API key if configured
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _settings.ApiKey);
        }
        
        _logger.LogInformation("ManagementPortalConnector initialized (Enabled: {Enabled}, URL: {Url})", 
            _settings.Enabled, _settings.PortalUrl);
    }

    private HttpClientHandler CreateHttpHandler()
    {
        var handler = new HttpClientHandler();
        
        // Load client certificate if configured (for mutual TLS)
        if (!string.IsNullOrEmpty(_settings.ClientCertificatePath) && File.Exists(_settings.ClientCertificatePath))
        {
            try
            {
                X509Certificate2 clientCert;
                
                if (!string.IsNullOrEmpty(_settings.ClientKeyPath) && File.Exists(_settings.ClientKeyPath))
                {
                    // Load certificate with separate key file
                    clientCert = X509Certificate2.CreateFromPemFile(_settings.ClientCertificatePath, _settings.ClientKeyPath);
                }
                else
                {
                    // Load certificate (may include private key)
                    clientCert = X509CertificateLoader.LoadCertificateFromFile(_settings.ClientCertificatePath);
                }
                
                handler.ClientCertificates.Add(clientCert);
                _logger.LogInformation("Loaded client certificate for management portal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load client certificate");
            }
        }
        
        // Configure CA certificate validation if specified
        if (!string.IsNullOrEmpty(_settings.CaCertificatePath) && File.Exists(_settings.CaCertificatePath))
        {
            handler.ServerCertificateCustomValidationCallback = ValidateServerCertificate;
        }
        else
        {
            // For development/testing - allow self-signed certificates
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }
        
        return handler;
    }

    private bool ValidateServerCertificate(HttpRequestMessage message, X509Certificate2? cert, 
        X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if (cert == null)
            return false;

        // Load CA certificate
        try
        {
            var caCert = X509CertificateLoader.LoadCertificateFromFile(_settings.CaCertificatePath!);
            
            using var customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.ExtraStore.Add(caCert);
            customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            
            return customChain.Build(cert);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate validation failed");
            return false;
        }
    }

    /// <summary>
    /// Register server with management portal using auto-registration
    /// </summary>
    public async Task<RegistrationResult> RegisterServerAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return new RegistrationResult
            {
                Success = false,
                Message = "Management portal integration is disabled"
            };
        }

        try
        {
            var serverIp = _config.HonData?.ServerIp;
            var apiPort = _config.HonData?.ApiPort ?? 5000;
            var serverName = _config.HonData?.ServerName;
            var discordUserId = _settings.DiscordUserId;

            if (string.IsNullOrEmpty(serverIp) || string.IsNullOrEmpty(serverName))
            {
                return new RegistrationResult
                {
                    Success = false,
                    Message = "Server IP or name not configured"
                };
            }

            // If we already have an API key, just validate connection
            if (!string.IsNullOrEmpty(_settings.ApiKey))
            {
                var validated = await ValidateConnectionAsync(cancellationToken);
                if (validated)
                {
                    _isRegistered = true;
                    return new RegistrationResult
                    {
                        Success = true,
                        Message = "Server validated with existing API key",
                        ServerName = serverName,
                        ServerAddress = $"{serverIp}:{apiPort}"
                    };
                }
            }

            // Try auto-registration if Discord User ID is configured
            if (!string.IsNullOrEmpty(discordUserId))
            {
                _logger.LogInformation("Attempting auto-registration with Discord User ID: {DiscordId}", discordUserId);
                
                var autoRegisterRequest = new
                {
                    discord_user_id = discordUserId,
                    server_name = serverName,
                    ip_address = serverIp,
                    api_port = apiPort,
                    region = "Unknown",
                    version = _config.HonData?.ManVersion
                };

                var response = await _httpClient.PostAsJsonAsync(
                    $"{ApiUrl}/auto-register", 
                    autoRegisterRequest, 
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AutoRegisterResponse>(cancellationToken: cancellationToken);
                    
                    if (result?.Success == true && !string.IsNullOrEmpty(result.ApiKey))
                    {
                        // Store API key for future use
                        _settings.ApiKey = result.ApiKey;
                        _settings.ServerId = result.ServerId;
                        
                        // Add API key header for future requests
                        if (!_httpClient.DefaultRequestHeaders.Contains("X-Api-Key"))
                        {
                            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", result.ApiKey);
                        }
                        
                        _isRegistered = true;
                        
                        var regType = result.IsNewRegistration ? "registered" : "reconnected";
                        _logger.LogInformation("Server auto-{Type} successfully! API Key: {ApiKey}", 
                            regType, result.ApiKey[..8] + "...");
                        
                        return new RegistrationResult
                        {
                            Success = true,
                            Message = result.Message,
                            ServerName = serverName,
                            ServerAddress = $"{serverIp}:{apiPort}",
                            ApiKey = result.ApiKey,
                            ServerId = result.ServerId
                        };
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Auto-registration failed: {StatusCode} - {Error}", response.StatusCode, error);
                }
            }

            // Fallback to legacy ping-based registration
            _logger.LogInformation("Falling back to legacy registration method");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PortalUrl}/api/public/ping");
            request.Headers.Add("Selected-Server", serverIp);
            request.Headers.Add("Selected-Port", apiPort.ToString());

            var pingResponse = await _httpClient.SendAsync(request, cancellationToken);
            
            if (pingResponse.IsSuccessStatusCode)
            {
                _isRegistered = true;
                _logger.LogInformation("Server registered via legacy method");
                
                return new RegistrationResult
                {
                    Success = true,
                    Message = "Server registered successfully (legacy)",
                    ServerName = serverName,
                    ServerAddress = $"{serverIp}:{apiPort}"
                };
            }
            else
            {
                var content = await pingResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to register with management portal: {StatusCode} - {Content}", 
                    pingResponse.StatusCode, content);
                
                return new RegistrationResult
                {
                    Success = false,
                    Message = $"Registration failed: {pingResponse.StatusCode}",
                    Error = content
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering with management portal");
            return new RegistrationResult
            {
                Success = false,
                Message = "Registration error",
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Validate connection to management portal
    /// </summary>
    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PortalUrl}/api/public/ping");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Management portal connection validated");
                return true;
            }
            
            _logger.LogWarning("Management portal connection failed: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate management portal connection");
            return false;
        }
    }

    /// <summary>
    /// Ping management portal to verify connectivity
    /// </summary>
    public async Task<bool> PingManagementPortalAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return false;

        try
        {
            var serverIp = _config.HonData?.ServerIp;
            var apiPort = _config.HonData?.ApiPort ?? 5000;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{PortalUrl}/api/public/ping");
            request.Headers.Add("Selected-Server", serverIp ?? "");
            request.Headers.Add("Selected-Port", apiPort.ToString());

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping to management portal failed");
            return false;
        }
    }

    /// <summary>
    /// Get Discord user information from management portal
    /// </summary>
    public async Task<DiscordUserInfo?> GetDiscordUserInfoAsync(string discordId, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return null;

        try
        {
            var url = $"{PortalUrl}/api-ui/getDiscordUsername/{discordId}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var data = JsonSerializer.Deserialize<DiscordUserInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return data;
            }
            
            _logger.LogWarning("Failed to get Discord user info for ID {DiscordId}", discordId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Discord user info for ID {DiscordId}", discordId);
            return null;
        }
    }

    /// <summary>
    /// Report server status to management portal
    /// </summary>
    public async Task ReportServerStatusAsync(ServerStatusReport status, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return;

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiUrl}/status",
                status,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to report server status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reporting server status");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}

#region DTOs

/// <summary>
/// Result of server registration with management portal
/// </summary>
public class RegistrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ServerName { get; set; }
    public string? ServerAddress { get; set; }
    public string? Error { get; set; }
    public string? ApiKey { get; set; }
    public string? ServerId { get; set; }
}

/// <summary>
/// Response from auto-registration endpoint
/// </summary>
public class AutoRegisterResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("server_id")]
    public string? ServerId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("is_new_registration")]
    public bool IsNewRegistration { get; set; }
}

/// <summary>
/// Discord user information from management portal
/// </summary>
public class DiscordUserInfo
{
    public string? Id { get; set; }
    public string? Username { get; set; }
    public string? Discriminator { get; set; }
    public string? Avatar { get; set; }
    public string? GlobalName { get; set; }
}

/// <summary>
/// Server status report to send to management portal
/// </summary>
public class ServerStatusReport
{
    public string ServerName { get; set; } = string.Empty;
    public string ServerIp { get; set; } = string.Empty;
    public int ApiPort { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalServers { get; set; }
    public int RunningServers { get; set; }
    public int PlayersOnline { get; set; }
    public string? HonVersion { get; set; }
    public string? HonfiguratorVersion { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // Detailed data
    public List<GameInstanceStatus>? Instances { get; set; }
    public SystemStatsInfo? SystemStats { get; set; }
    public bool MasterServerConnected { get; set; }
    public bool ChatServerConnected { get; set; }
}

/// <summary>
/// Status of a single game server instance
/// </summary>
public class GameInstanceStatus
{
    public int InstanceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Status { get; set; } = "Offline";
    public int NumClients { get; set; }
    public long? MatchId { get; set; }
    public string? GamePhase { get; set; }
    public string? Map { get; set; }
    public string? GameMode { get; set; }
    public DateTime? StartTime { get; set; }
}

/// <summary>
/// System resource statistics
/// </summary>
public class SystemStatsInfo
{
    [JsonPropertyName("cpu_percent")]
    public double CpuPercent { get; set; }
    
    [JsonPropertyName("cpu_count")]
    public int CpuCount { get; set; }
    
    [JsonPropertyName("memory_percent")]
    public double MemoryPercent { get; set; }
    
    [JsonPropertyName("total_memory_mb")]
    public long TotalMemoryMb { get; set; }
    
    [JsonPropertyName("used_memory_mb")]
    public long UsedMemoryMb { get; set; }
    
    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; set; }
    
    /// <summary>
    /// Servers per CPU core setting (0.5, 1, 2, or 3)
    /// </summary>
    [JsonPropertyName("svr_total_per_core")]
    public double SvrTotalPerCore { get; set; } = 1.0;
    
    /// <summary>
    /// Maximum allowed servers (calculated from CPU count and svr_total_per_core)
    /// </summary>
    [JsonPropertyName("max_allowed_servers")]
    public int MaxAllowedServers { get; set; }
    
    /// <summary>
    /// Configured target number of servers (svr_total)
    /// </summary>
    [JsonPropertyName("svr_total")]
    public int SvrTotal { get; set; }
}

#endregion
