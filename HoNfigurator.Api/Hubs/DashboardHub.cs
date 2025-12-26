using Microsoft.AspNetCore.SignalR;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Notifications;
using HoNfigurator.Core.Charts;
using HoNfigurator.Core.Connectors;
using HoNfigurator.GameServer.Services;

namespace HoNfigurator.Api.Hubs;

public interface IDashboardClient
{
    Task ReceiveStatus(ServerStatusResponse status);
    Task ReceiveServerUpdate(GameServerInstance instance);
    Task ReceiveLog(string message, string level);
    Task ReceiveNotification(string title, string message, string type);
    Task ReceiveCommandResult(CommandResult result);
    Task ReceiveLogUpdate(string[] lines);
    Task ReceiveAlert(Notification notification);
    Task ReceiveChartUpdate(string chartType, object data);
    Task ReceiveManagementPortalStatus(ManagementPortalStatus status);
    Task ReceiveEvent(GameEventDto gameEvent);
    Task ReceiveMqttMessage(MqttMessageDto message);
    Task ReceiveMetricsUpdate(MetricsUpdateDto metrics);
}

/// <summary>
/// Game event DTO for SignalR transmission
/// </summary>
public record GameEventDto
{
    public string Id { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public int ServerId { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, object> Data { get; init; } = new();
    public bool IsMqttPublishable { get; init; }
}

/// <summary>
/// MQTT message DTO for SignalR transmission
/// </summary>
public record MqttMessageDto
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Topic { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool Sent { get; init; } = true;
}

/// <summary>
/// Metrics update DTO for real-time charts
/// </summary>
public record MetricsUpdateDto
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double CpuPercent { get; init; }
    public double MemoryPercent { get; init; }
    public long MemoryUsedMb { get; init; }
    public int ActiveServers { get; init; }
    public int TotalPlayers { get; init; }
    public Dictionary<int, ServerMetricsDto> ServerMetrics { get; init; } = new();
}

/// <summary>
/// Per-server metrics DTO
/// </summary>
public record ServerMetricsDto
{
    public int ServerId { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryMb { get; init; }
    public int PlayerCount { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Management portal connection status for dashboard
/// </summary>
public record ManagementPortalStatus
{
    public bool Enabled { get; init; }
    public bool Connected { get; init; }
    public bool Registered { get; init; }
    public string? ServerName { get; init; }
    public string? PortalUrl { get; init; }
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

public record CommandResult
{
    public bool Success { get; init; }
    public string[] Output { get; init; } = Array.Empty<string>();
}

public class DashboardHub : Hub<IDashboardClient>
{
    private readonly IGameServerManager _serverManager;
    private readonly IManagementPortalConnector _portalConnector;
    private readonly IMqttHandler _mqttHandler;
    private readonly HoNConfiguration _config;
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(
        IGameServerManager serverManager,
        IManagementPortalConnector portalConnector,
        IMqttHandler mqttHandler,
        HoNConfiguration config,
        ILogger<DashboardHub> logger)
    {
        _serverManager = serverManager;
        _portalConnector = portalConnector;
        _mqttHandler = mqttHandler;
        _config = config;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        var status = _serverManager.GetStatus();
        await Clients.Caller.ReceiveStatus(status);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task RequestStatus()
    {
        var status = _serverManager.GetStatus();
        await Clients.Caller.ReceiveStatus(status);
    }

    public async Task StartServer(int id)
    {
        _logger.LogInformation("Client requested to start server {Id}", id);
        var instance = await _serverManager.StartServerAsync(id);
        if (instance != null)
        {
            await Clients.All.ReceiveServerUpdate(instance);
            await Clients.Caller.ReceiveNotification("Server Started", string.Format("Server #{0} is starting...", id), "success");
        }
    }

    public async Task StopServer(int id)
    {
        _logger.LogInformation("Client requested to stop server {Id}", id);
        var success = await _serverManager.StopServerAsync(id);
        if (success)
        {
            var status = _serverManager.GetStatus();
            var instance = status.Instances.FirstOrDefault(i => i.Id == id);
            if (instance != null)
            {
                await Clients.All.ReceiveServerUpdate(instance);
            }
            await Clients.Caller.ReceiveNotification("Server Stopped", string.Format("Server #{0} has been stopped", id), "info");
        }
    }

    public async Task RestartServer(int id)
    {
        _logger.LogInformation("Client requested to restart server {Id}", id);
        await Clients.Caller.ReceiveNotification("Restarting", string.Format("Server #{0} is restarting...", id), "warning");
        await _serverManager.RestartServerAsync(id);
        var status = _serverManager.GetStatus();
        var instance = status.Instances.FirstOrDefault(i => i.Id == id);
        if (instance != null)
        {
            await Clients.All.ReceiveServerUpdate(instance);
            await Clients.Caller.ReceiveNotification("Server Restarted", string.Format("Server #{0} has been restarted", id), "success");
        }
    }

    public async Task StartAllServers()
    {
        _logger.LogInformation("Client requested to start all servers");
        await Clients.Caller.ReceiveNotification("Starting All", "Starting all servers...", "info");
        await _serverManager.StartAllServersAsync();
        var status = _serverManager.GetStatus();
        await Clients.All.ReceiveStatus(status);
        await Clients.Caller.ReceiveNotification("All Started", "All servers have been started", "success");
    }

    public async Task StopAllServers()
    {
        _logger.LogInformation("Client requested to stop all servers");
        await Clients.Caller.ReceiveNotification("Stopping All", "Stopping all servers...", "warning");
        await _serverManager.StopAllServersAsync();
        var status = _serverManager.GetStatus();
        await Clients.All.ReceiveStatus(status);
        await Clients.Caller.ReceiveNotification("All Stopped", "All servers have been stopped", "info");
    }

    public async Task RestartAllServers()
    {
        _logger.LogInformation("Client requested to restart all servers");
        await Clients.Caller.ReceiveNotification("Restarting All", "Restarting all servers...", "warning");
        await _serverManager.StopAllServersAsync();
        await Task.Delay(1000);
        await _serverManager.StartAllServersAsync();
        var status = _serverManager.GetStatus();
        await Clients.All.ReceiveStatus(status);
        await Clients.Caller.ReceiveNotification("All Restarted", "All servers have been restarted", "success");
    }

    public async Task AddServer()
    {
        _logger.LogInformation("Client requested to add a server");
        // In a real implementation, this would add a new server instance
        await Clients.Caller.ReceiveNotification("Add Server", "Server addition requires configuration update", "info");
        var result = new CommandResult
        {
            Success = true,
            Output = new[] { "Server addition is configured through config/config.json", "Increase total_servers and restart the application" }
        };
        await Clients.Caller.ReceiveCommandResult(result);
    }

    public async Task ExecuteCommand(string command)
    {
        _logger.LogInformation("Client executing command: {Command}", command);
        var output = new List<string>();
        var cmd = command?.ToLower().Trim() ?? "";

        if (cmd == "help")
        {
            output.Add("Available commands:");
            output.Add("  status      - Show server status");
            output.Add("  list        - List all server instances");
            output.Add("  startup N   - Start server N (or 'all')");
            output.Add("  shutdown N  - Stop server N (or 'all')");
            output.Add("  restart N   - Restart server N (or 'all')");
            output.Add("  add         - Add a new server instance");
            output.Add("  config      - Show configuration path");
            output.Add("  version     - Show version info");
            output.Add("  portal      - Show management portal status");
            output.Add("  portal register - Register with portal");
            output.Add("  mqtt        - Show MQTT broker status");
            output.Add("  mqtt connect - Connect to MQTT broker");
            output.Add("  mqtt disconnect - Disconnect from MQTT broker");
            output.Add("  mqtt test   - Publish test message to MQTT");
            output.Add("  help        - Show this help");
        }
        else if (cmd == "status")
        {
            var status = _serverManager.GetStatus();
            output.Add(string.Format("Server Name: {0}", status.ServerName));
            output.Add(string.Format("Version: {0}", status.Version));
            output.Add(string.Format("Total Servers: {0}", status.TotalServers));
            output.Add(string.Format("Online Servers: {0}", status.OnlineServers));
            output.Add(string.Format("Total Players: {0}", status.TotalPlayers));
            output.Add(string.Format("Master Server: {0}", status.MasterServerConnected ? "Connected" : "Disconnected"));
            output.Add(string.Format("Chat Server: {0}", status.ChatServerConnected ? "Connected" : "Disconnected"));
            output.Add(string.Format("CPU: {0}%", status.SystemStats?.CpuUsagePercent ?? 0));
            output.Add(string.Format("Memory: {0} MB", status.SystemStats?.UsedMemoryMb ?? 0));
            
            // Add portal status
            output.Add(string.Format("Portal: {0}", _portalConnector.IsEnabled 
                ? (_portalConnector.IsRegistered ? "Registered" : "Enabled") 
                : "Disabled"));
            
            // Add MQTT status
            output.Add(string.Format("MQTT: {0}", _mqttHandler.IsEnabled 
                ? (_mqttHandler.IsConnected ? "Connected" : "Enabled") 
                : "Disabled"));
        }
        else if (cmd == "list")
        {
            var status = _serverManager.GetStatus();
            output.Add("Server Instances:");
            output.Add("-------------------");
            foreach (var instance in status.Instances)
            {
                output.Add(string.Format("  #{0} | {1} | Port {2} | {3}/{4} players", 
                    instance.Id, instance.StatusString, instance.Port, instance.NumClients, instance.MaxClients));
            }
        }
        else if (cmd == "startup all")
        {
            await StartAllServers();
            output.Add("Starting all servers...");
        }
        else if (cmd.StartsWith("startup ") && int.TryParse(cmd.Substring(8).Trim(), out int startId))
        {
            await StartServer(startId);
            output.Add(string.Format("Starting server #{0}...", startId));
        }
        else if (cmd == "shutdown all")
        {
            await StopAllServers();
            output.Add("Stopping all servers...");
        }
        else if (cmd.StartsWith("shutdown ") && int.TryParse(cmd.Substring(9).Trim(), out int stopId))
        {
            await StopServer(stopId);
            output.Add(string.Format("Stopping server #{0}...", stopId));
        }
        else if (cmd == "restart all")
        {
            await RestartAllServers();
            output.Add("Restarting all servers...");
        }
        else if (cmd.StartsWith("restart ") && int.TryParse(cmd.Substring(8).Trim(), out int restartId))
        {
            await RestartServer(restartId);
            output.Add(string.Format("Restarting server #{0}...", restartId));
        }
        else if (cmd == "config")
        {
            output.Add("Configuration: config/config.json");
            output.Add("Use the Config tab in the dashboard to edit.");
        }
        else if (cmd == "version")
        {
            output.Add("HoNfigurator .NET 10");
            output.Add("Version: 1.0.0-dotnet");
            output.Add("Runtime: .NET 10.0");
        }
        else if (cmd == "add")
        {
            await AddServer();
            output.Add("See notification for details.");
        }
        else if (cmd == "portal" || cmd == "portal status")
        {
            output.Add("=== Management Portal Status ===");
            output.Add(string.Format("Enabled: {0}", _portalConnector.IsEnabled));
            if (_portalConnector.IsEnabled)
            {
                output.Add(string.Format("Registered: {0}", _portalConnector.IsRegistered));
                output.Add(string.Format("Server Name: {0}", _portalConnector.ServerName ?? "N/A"));
                output.Add("Portal URL: https://management.honfigurator.app:3001");
                var isConnected = await _portalConnector.PingManagementPortalAsync();
                output.Add(string.Format("Connection: {0}", isConnected ? "OK" : "Failed"));
            }
            else
            {
                output.Add("Portal integration is disabled in configuration.");
                output.Add("Set ManagementPortal:Enabled = true in appsettings.json");
            }
        }
        else if (cmd == "portal register")
        {
            if (!_portalConnector.IsEnabled)
            {
                output.Add("Error: Portal integration is disabled.");
                output.Add("Enable it in appsettings.json first.");
            }
            else if (_portalConnector.IsRegistered)
            {
                output.Add("Server is already registered with the portal.");
                output.Add(string.Format("Server Name: {0}", _portalConnector.ServerName));
            }
            else
            {
                output.Add("Attempting to register with management portal...");
                var regResult = await _portalConnector.RegisterServerAsync();
                if (regResult?.Success == true)
                {
                    output.Add("Registration successful!");
                    output.Add(string.Format("Server Name: {0}", regResult.ServerName));
                    output.Add(string.Format("Server Address: {0}", regResult.ServerAddress));
                }
                else
                {
                    output.Add(string.Format("Registration failed: {0}", regResult?.Error ?? regResult?.Message ?? "Unknown error"));
                }
            }
        }
        else if (cmd == "mqtt" || cmd == "mqtt status")
        {
            output.Add("=== MQTT Broker Status ===");
            output.Add(string.Format("Enabled: {0}", _mqttHandler.IsEnabled));
            if (_mqttHandler.IsEnabled)
            {
                output.Add(string.Format("Connected: {0}", _mqttHandler.IsConnected));
                
                // Get broker address from config
                var portalSettings = _config.ApplicationData?.ManagementPortal;
                var mqttSettings = _config.ApplicationData?.Mqtt;
                
                if (portalSettings?.Enabled == true)
                {
                    output.Add(string.Format("Broker: {0}:{1}", portalSettings.MqttHost, portalSettings.MqttPort));
                    output.Add(string.Format("TLS: {0}", portalSettings.MqttUseTls ? "Enabled" : "Disabled"));
                }
                else if (mqttSettings?.Enabled == true)
                {
                    output.Add(string.Format("Broker: {0}:{1}", mqttSettings.Host, mqttSettings.Port));
                    output.Add(string.Format("TLS: {0}", mqttSettings.UseTls ? "Enabled" : "Disabled"));
                }
                
                output.Add("");
                output.Add("MQTT Topics:");
                output.Add("  honfigurator/server/{id}/status - Server status events");
                output.Add("  honfigurator/server/{id}/match  - Match events");
                output.Add("  honfigurator/server/{id}/player - Player events");
                output.Add("  honfigurator/manager/status     - Manager events");
            }
            else
            {
                output.Add("MQTT integration is disabled in configuration.");
                output.Add("Enable ManagementPortal or Mqtt section in appsettings.json");
            }
        }
        else if (cmd == "mqtt connect")
        {
            if (!_mqttHandler.IsEnabled)
            {
                output.Add("Error: MQTT is disabled in configuration.");
            }
            else if (_mqttHandler.IsConnected)
            {
                output.Add("MQTT broker is already connected.");
            }
            else
            {
                output.Add("Attempting to connect to MQTT broker...");
                var connected = await _mqttHandler.ConnectAsync();
                output.Add(connected ? "Successfully connected to MQTT broker." : "Failed to connect to MQTT broker.");
            }
        }
        else if (cmd == "mqtt disconnect")
        {
            if (!_mqttHandler.IsConnected)
            {
                output.Add("MQTT broker is not connected.");
            }
            else
            {
                output.Add("Disconnecting from MQTT broker...");
                await _mqttHandler.DisconnectAsync();
                output.Add("Disconnected from MQTT broker.");
            }
        }
        else if (cmd == "mqtt test")
        {
            if (!_mqttHandler.IsEnabled)
            {
                output.Add("Error: MQTT is disabled in configuration.");
            }
            else if (!_mqttHandler.IsConnected)
            {
                output.Add("Error: Not connected to MQTT broker. Use 'mqtt connect' first.");
            }
            else
            {
                output.Add("Publishing test message to MQTT broker...");
                var testMessage = new
                {
                    event_type = "test",
                    server_name = _config.HonData.ServerName,
                    message = "Test message from HoNfigurator Console",
                    timestamp = DateTime.UtcNow,
                    source = "dashboard_console"
                };
                await _mqttHandler.PublishJsonAsync("test", testMessage);
                output.Add("Test message published successfully.");
                output.Add(string.Format("Topic: {0}/test", _config.ApplicationData?.Mqtt?.TopicPrefix ?? "honfigurator"));
            }
        }
        else if (!string.IsNullOrWhiteSpace(cmd))
        {
            output.Add(string.Format("Unknown command: {0}", cmd));
            output.Add("Type 'help' for available commands.");
        }

        var result = new CommandResult { Success = true, Output = output.ToArray() };
        await Clients.Caller.ReceiveCommandResult(result);
    }

    public async Task SendMessage(int serverId, string message)
    {
        _logger.LogInformation("Sending message to server {Id}: {Message}", serverId, message);
        // In real implementation, this would send to the game server
        await Clients.Caller.ReceiveNotification("Message Sent", 
            string.Format("Message sent to server #{0}: {1}", serverId, message), "success");
    }

    /// <summary>
    /// Request management portal connection status
    /// </summary>
    public async Task RequestManagementPortalStatus()
    {
        var status = new ManagementPortalStatus
        {
            Enabled = _portalConnector.IsEnabled,
            Connected = _portalConnector.IsEnabled && await _portalConnector.PingManagementPortalAsync(),
            Registered = _portalConnector.IsRegistered,
            ServerName = _portalConnector.ServerName,
            PortalUrl = _portalConnector.IsEnabled ? "https://management.honfigurator.app:3001" : null,
            LastUpdated = DateTime.UtcNow
        };
        await Clients.Caller.ReceiveManagementPortalStatus(status);
    }

    /// <summary>
    /// Trigger manual registration with management portal
    /// </summary>
    public async Task RegisterWithManagementPortal()
    {
        if (!_portalConnector.IsEnabled)
        {
            await Clients.Caller.ReceiveNotification("Portal Disabled", "Management portal integration is not enabled", "warning");
            return;
        }

        await Clients.Caller.ReceiveNotification("Registering", "Attempting to register with management portal...", "info");
        
        var result = await _portalConnector.RegisterServerAsync();
        
        if (result.Success)
        {
            await Clients.Caller.ReceiveNotification("Registered", $"Successfully registered: {result.ServerName}", "success");
        }
        else
        {
            await Clients.Caller.ReceiveNotification("Registration Failed", result.Message, "error");
        }

        await RequestManagementPortalStatus();
    }

    /// <summary>
    /// Simulate a game event from the dashboard for testing
    /// </summary>
    public async Task SimulateEvent(string eventType, int serverId, Dictionary<string, object>? data = null)
    {
        _logger.LogInformation("Simulating event {EventType} for server {ServerId}", eventType, serverId);
        
        if (!Enum.TryParse<HoNfigurator.Core.Events.GameEventType>(eventType, true, out var parsedType))
        {
            await Clients.Caller.ReceiveNotification("Error", $"Invalid event type: {eventType}", "error");
            return;
        }

        // Note: In a real implementation, this would use the GameEventDispatcher
        var gameEvent = new GameEventDto
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            EventType = parsedType.ToString(),
            ServerId = serverId,
            Timestamp = DateTime.UtcNow,
            Data = data ?? new Dictionary<string, object>(),
            IsMqttPublishable = IsEventMqttPublishable(parsedType)
        };

        await Clients.All.ReceiveEvent(gameEvent);
        await Clients.Caller.ReceiveNotification("Event Simulated", $"{eventType} event dispatched for server #{serverId}", "success");
    }

    /// <summary>
    /// Request recent MQTT messages
    /// </summary>
    public async Task RequestMqttHistory()
    {
        if (!_mqttHandler.IsEnabled)
        {
            await Clients.Caller.ReceiveNotification("MQTT Disabled", "MQTT integration is not enabled", "warning");
            return;
        }

        // Send status update
        await Clients.Caller.ReceiveNotification("MQTT History", "MQTT message history is tracked in real-time", "info");
    }

    /// <summary>
    /// Request current metrics for performance dashboard
    /// </summary>
    public async Task RequestMetrics()
    {
        var status = _serverManager.GetStatus();
        var metrics = new MetricsUpdateDto
        {
            Timestamp = DateTime.UtcNow,
            CpuPercent = status.SystemStats?.CpuUsagePercent ?? 0,
            MemoryPercent = status.SystemStats != null && status.SystemStats.TotalMemoryMb > 0 
                ? (double)status.SystemStats.UsedMemoryMb / status.SystemStats.TotalMemoryMb * 100 
                : 0,
            MemoryUsedMb = status.SystemStats?.UsedMemoryMb ?? 0,
            ActiveServers = status.OnlineServers,
            TotalPlayers = status.TotalPlayers,
            ServerMetrics = status.Instances.ToDictionary(
                i => i.Id,
                i => new ServerMetricsDto
                {
                    ServerId = i.Id,
                    CpuPercent = i.CpuPercent,
                    MemoryMb = i.MemoryMb,
                    PlayerCount = i.NumClients,
                    Status = i.StatusString
                })
        };

        await Clients.Caller.ReceiveMetricsUpdate(metrics);
    }

    private static bool IsEventMqttPublishable(HoNfigurator.Core.Events.GameEventType eventType)
    {
        return eventType switch
        {
            HoNfigurator.Core.Events.GameEventType.ServerStarted or
            HoNfigurator.Core.Events.GameEventType.ServerStopped or
            HoNfigurator.Core.Events.GameEventType.ServerCrashed or
            HoNfigurator.Core.Events.GameEventType.ServerRestarted or
            HoNfigurator.Core.Events.GameEventType.MatchStarted or
            HoNfigurator.Core.Events.GameEventType.MatchEnded or
            HoNfigurator.Core.Events.GameEventType.MatchAborted or
            HoNfigurator.Core.Events.GameEventType.PlayerConnected or
            HoNfigurator.Core.Events.GameEventType.PlayerDisconnected or
            HoNfigurator.Core.Events.GameEventType.PlayerKicked or
            HoNfigurator.Core.Events.GameEventType.PlayerBanned or
            HoNfigurator.Core.Events.GameEventType.FirstBlood or
            HoNfigurator.Core.Events.GameEventType.KongorKilled => true,
            _ => false
        };
    }
}
