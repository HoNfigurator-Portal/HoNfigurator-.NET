using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Connectors;

/// <summary>
/// MQTT Handler for publishing server status and events
/// </summary>
public interface IMqttHandler : IDisposable
{
    bool IsConnected { get; }
    bool IsEnabled { get; }
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task PublishAsync(string topic, string message, bool retain = false);
    Task PublishJsonAsync<T>(string topic, T data, bool retain = false);
    
    // Convenience methods for common events
    Task PublishServerStatusAsync(int serverId, string status, object? data = null);
    Task PublishMatchEventAsync(int serverId, string eventType, object? data = null);
    Task PublishPlayerEventAsync(int serverId, string eventType, string playerName, object? data = null);
    Task PublishManagerEventAsync(string eventType, object? data = null);
}

/// <summary>
/// MQTT handler implementation using MQTTnet
/// Supports both local MQTT and management.honfigurator.app integration
/// </summary>
public class MqttHandler : IMqttHandler
{
    private readonly ILogger<MqttHandler> _logger;
    private readonly HoNConfiguration _config;
    private readonly IMqttClient _mqttClient;
    private readonly MqttClientOptions? _options;
    private bool _disposed;

    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    public bool IsEnabled => (_config.ApplicationData?.Mqtt?.Enabled ?? false) 
                          || (_config.ApplicationData?.ManagementPortal?.Enabled ?? false);

    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<string, string>? OnMessagePublished;

    public MqttHandler(ILogger<MqttHandler> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();
        
        // Setup event handlers
        _mqttClient.ConnectedAsync += e =>
        {
            _logger.LogInformation("Connected to MQTT broker");
            OnConnected?.Invoke();
            return Task.CompletedTask;
        };
        
        _mqttClient.DisconnectedAsync += e =>
        {
            if (e.Exception != null)
            {
                _logger.LogWarning(e.Exception, "Disconnected from MQTT broker");
            }
            else
            {
                _logger.LogInformation("Disconnected from MQTT broker");
            }
            OnDisconnected?.Invoke();
            return Task.CompletedTask;
        };
        
        // Determine MQTT settings - prioritize management portal settings if enabled
        _options = BuildMqttOptions();
    }

    private MqttClientOptions? BuildMqttOptions()
    {
        var managementPortal = _config.ApplicationData?.ManagementPortal;
        var mqttSettings = _config.ApplicationData?.Mqtt;
        
        // Use management portal MQTT if enabled
        if (managementPortal?.Enabled == true)
        {
            return BuildManagementPortalMqttOptions(managementPortal);
        }
        
        // Otherwise use standard MQTT settings
        if (mqttSettings?.Enabled == true)
        {
            return BuildStandardMqttOptions(mqttSettings);
        }
        
        return null;
    }

    private MqttClientOptions BuildManagementPortalMqttOptions(ManagementPortalSettings settings)
    {
        var clientId = $"honfigurator-{_config.HonData.ServerName}-{Environment.MachineName}";
        
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(settings.MqttHost, settings.MqttPort)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));
        
        // Management portal uses TLS with Step CA certificates
        if (settings.MqttUseTls)
        {
            optionsBuilder.WithTlsOptions(o =>
            {
                o.WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12 | 
                                   System.Security.Authentication.SslProtocols.Tls13);
                
                // Load client certificate if configured
                if (!string.IsNullOrEmpty(settings.ClientCertificatePath) && 
                    File.Exists(settings.ClientCertificatePath))
                {
                    try
                    {
                        X509Certificate2 clientCert;
                        
                        if (!string.IsNullOrEmpty(settings.ClientKeyPath) && 
                            File.Exists(settings.ClientKeyPath))
                        {
                            clientCert = X509Certificate2.CreateFromPemFile(
                                settings.ClientCertificatePath, 
                                settings.ClientKeyPath);
                        }
                        else
                        {
                            clientCert = X509CertificateLoader.LoadCertificateFromFile(settings.ClientCertificatePath);
                        }
                        
                        o.WithClientCertificates(new X509Certificate2[] { clientCert });
                        _logger.LogInformation("Loaded client certificate for MQTT TLS");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load client certificate for MQTT");
                    }
                }
                
                // CA certificate validation
                if (!string.IsNullOrEmpty(settings.CaCertificatePath) && 
                    File.Exists(settings.CaCertificatePath))
                {
                    o.WithCertificateValidationHandler(context =>
                    {
                        try
                        {
                            var caCert = X509CertificateLoader.LoadCertificateFromFile(settings.CaCertificatePath);
                            using var chain = new X509Chain();
                            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            chain.ChainPolicy.ExtraStore.Add(caCert);
                            chain.ChainPolicy.VerificationFlags = 
                                X509VerificationFlags.AllowUnknownCertificateAuthority;
                            var cert2 = new X509Certificate2(context.Certificate);
                            return chain.Build(cert2);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                }
                else
                {
                    // Allow self-signed certificates for development
                    o.WithCertificateValidationHandler(_ => true);
                }
            });
        }
        
        _logger.LogInformation("Configured MQTT for management portal: {Host}:{Port} (TLS: {Tls})",
            settings.MqttHost, settings.MqttPort, settings.MqttUseTls);
        
        return optionsBuilder.Build();
    }

    private MqttClientOptions BuildStandardMqttOptions(MqttSettings mqttSettings)
    {
        var clientId = $"honfigurator-{_config.HonData.ServerName}-{Environment.MachineName}";
            
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(mqttSettings.Host, mqttSettings.Port)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));
        
        if (!string.IsNullOrEmpty(mqttSettings.Username))
        {
            optionsBuilder.WithCredentials(mqttSettings.Username, mqttSettings.Password ?? "");
        }
        
        if (mqttSettings.UseTls)
        {
            optionsBuilder.WithTlsOptions(o => o.WithCertificateValidationHandler(_ => true));
        }
        
        return optionsBuilder.Build();
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            _logger.LogDebug("MQTT is disabled in configuration");
            return false;
        }

        if (_options == null)
        {
            _logger.LogWarning("MQTT options not configured");
            return false;
        }

        if (IsConnected)
        {
            _logger.LogDebug("Already connected to MQTT broker");
            return true;
        }

        try
        {
            var mqttSettings = _config.ApplicationData?.Mqtt;
            _logger.LogInformation("Connecting to MQTT broker at {Host}:{Port}...", 
                mqttSettings?.Host, mqttSettings?.Port);
            
            await _mqttClient.ConnectAsync(_options, cancellationToken);
            
            // Publish online status
            await PublishManagerEventAsync("online", new { 
                server_name = _config.HonData.ServerName,
                timestamp = DateTime.UtcNow
            });
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MQTT broker");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;
        
        try
        {
            // Publish offline status before disconnecting
            await PublishManagerEventAsync("offline", new { 
                server_name = _config.HonData.ServerName,
                timestamp = DateTime.UtcNow
            });
            
            await _mqttClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from MQTT broker");
        }
    }

    public async Task PublishAsync(string topic, string message, bool retain = false)
    {
        if (!IsConnected)
        {
            _logger.LogDebug("Cannot publish - not connected to MQTT broker");
            return;
        }

        try
        {
            var prefix = _config.ApplicationData?.Mqtt?.TopicPrefix ?? "honfigurator";
            var fullTopic = $"{prefix}/{topic}";
            
            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(fullTopic)
                .WithPayload(message)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retain)
                .Build();
            
            await _mqttClient.PublishAsync(mqttMessage);
            
            _logger.LogDebug("Published to {Topic}: {Message}", fullTopic, 
                message.Length > 100 ? message[..100] + "..." : message);
            OnMessagePublished?.Invoke(fullTopic, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to {Topic}", topic);
        }
    }

    public async Task PublishJsonAsync<T>(string topic, T data, bool retain = false)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        });
        await PublishAsync(topic, json, retain);
    }

    // Convenience methods for common events
    
    public async Task PublishServerStatusAsync(int serverId, string status, object? data = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["event_type"] = status,
            ["server_id"] = serverId,
            ["server_name"] = _config.HonData.ServerName,
            ["timestamp"] = DateTime.UtcNow
        };
        
        if (data != null)
        {
            foreach (var prop in data.GetType().GetProperties())
            {
                payload[ToSnakeCase(prop.Name)] = prop.GetValue(data) ?? "";
            }
        }
        
        await PublishJsonAsync($"server/{serverId}/status", payload);
    }

    public async Task PublishMatchEventAsync(int serverId, string eventType, object? data = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["event_type"] = eventType,
            ["server_id"] = serverId,
            ["server_name"] = _config.HonData.ServerName,
            ["timestamp"] = DateTime.UtcNow
        };
        
        if (data != null)
        {
            foreach (var prop in data.GetType().GetProperties())
            {
                payload[ToSnakeCase(prop.Name)] = prop.GetValue(data) ?? "";
            }
        }
        
        await PublishJsonAsync($"server/{serverId}/match", payload);
    }

    public async Task PublishPlayerEventAsync(int serverId, string eventType, string playerName, object? data = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["event_type"] = eventType,
            ["server_id"] = serverId,
            ["server_name"] = _config.HonData.ServerName,
            ["player_name"] = playerName,
            ["timestamp"] = DateTime.UtcNow
        };
        
        if (data != null)
        {
            foreach (var prop in data.GetType().GetProperties())
            {
                payload[ToSnakeCase(prop.Name)] = prop.GetValue(data) ?? "";
            }
        }
        
        await PublishJsonAsync($"server/{serverId}/player", payload);
    }

    public async Task PublishManagerEventAsync(string eventType, object? data = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["event_type"] = eventType,
            ["server_name"] = _config.HonData.ServerName,
            ["timestamp"] = DateTime.UtcNow
        };
        
        if (data != null)
        {
            foreach (var prop in data.GetType().GetProperties())
            {
                payload[ToSnakeCase(prop.Name)] = prop.GetValue(data) ?? "";
            }
        }
        
        await PublishJsonAsync("manager/status", payload);
    }

    private static string ToSnakeCase(string str)
    {
        return string.Concat(str.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x : x.ToString())).ToLower();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        if (IsConnected)
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        
        _mqttClient?.Dispose();
    }
}

/// <summary>
/// MQTT topic constants for HoNfigurator
/// </summary>
public static class MqttTopics
{
    // Server events
    public const string ServerStatus = "server/{0}/status";
    public const string ServerMatch = "server/{0}/match";
    public const string ServerPlayer = "server/{0}/player";
    
    // Manager events
    public const string ManagerStatus = "manager/status";
    public const string ManagerAlert = "manager/alert";
}

/// <summary>
/// MQTT event types
/// </summary>
public static class MqttEventTypes
{
    // Server status events
    public const string ServerReady = "server_ready";
    public const string ServerOccupied = "server_occupied";
    public const string ServerOffline = "server_offline";
    public const string Heartbeat = "heartbeat";
    
    // Match events
    public const string LobbyCreated = "lobby_created";
    public const string LobbyClosed = "lobby_closed";
    public const string MatchStarted = "match_started";
    public const string MatchEnded = "match_ended";
    
    // Player events
    public const string PlayerJoined = "player_joined";
    public const string PlayerLeft = "player_left";
    public const string PlayerKicked = "player_kicked";
    
    // Manager events
    public const string ManagerOnline = "online";
    public const string ManagerOffline = "offline";
    public const string ManagerShutdown = "shutdown";
}
