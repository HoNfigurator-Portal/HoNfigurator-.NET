using Microsoft.AspNetCore.SignalR;
using HoNfigurator.Api.Hubs;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Notifications;
using HoNfigurator.GameServer.Services;

namespace HoNfigurator.Api.Services;

public class StatusBroadcastService : BackgroundService
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hubContext;
    private readonly IGameServerManager _serverManager;
    private readonly IManagementPortalConnector _portalConnector;
    private readonly IMqttHandler _mqttHandler;
    private readonly HoNConfiguration _config;
    private readonly INotificationService _notificationService;
    private readonly ILogger<StatusBroadcastService> _logger;
    private readonly TimeSpan _broadcastInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _portalCheckInterval = TimeSpan.FromSeconds(30);
    private DateTime _lastPortalCheck = DateTime.MinValue;
    private bool _lastPortalConnected;
    private bool _lastMqttConnected;

    public StatusBroadcastService(
        IHubContext<DashboardHub, IDashboardClient> hubContext,
        IGameServerManager serverManager,
        IManagementPortalConnector portalConnector,
        IMqttHandler mqttHandler,
        HoNConfiguration config,
        INotificationService notificationService,
        ILogger<StatusBroadcastService> logger)
    {
        _hubContext = hubContext;
        _serverManager = serverManager;
        _portalConnector = portalConnector;
        _mqttHandler = mqttHandler;
        _config = config;
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Status broadcast service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = _serverManager.GetStatus();
                
                // Populate management portal status
                if (_portalConnector.IsEnabled)
                {
                    status.ManagementPortalConnected = _lastPortalConnected;
                    status.ManagementPortalRegistered = _portalConnector.IsRegistered;
                    status.ManagementPortalServerName = _portalConnector.ServerName;
                }
                
                // Populate MQTT status
                status.MqttEnabled = _mqttHandler.IsEnabled;
                status.MqttConnected = _mqttHandler.IsConnected;
                if (_mqttHandler.IsEnabled)
                {
                    var portalSettings = _config.ApplicationData?.ManagementPortal;
                    var mqttSettings = _config.ApplicationData?.Mqtt;
                    
                    if (portalSettings?.Enabled == true)
                    {
                        status.MqttBroker = $"{portalSettings.MqttHost}:{portalSettings.MqttPort}";
                    }
                    else if (mqttSettings?.Enabled == true)
                    {
                        status.MqttBroker = $"{mqttSettings.Host}:{mqttSettings.Port}";
                    }
                }
                
                // Check for MQTT connection state change
                if (_lastMqttConnected != _mqttHandler.IsConnected)
                {
                    if (_mqttHandler.IsConnected)
                    {
                        _logger.LogInformation("MQTT broker connected");
                        await _hubContext.Clients.All.ReceiveNotification(
                            "MQTT Connected",
                            $"Connected to MQTT broker: {status.MqttBroker}",
                            "success");
                    }
                    else if (_lastMqttConnected)
                    {
                        _logger.LogWarning("MQTT broker connection lost");
                        await _hubContext.Clients.All.ReceiveNotification(
                            "MQTT Disconnected",
                            "Lost connection to MQTT broker",
                            "warning");
                    }
                    _lastMqttConnected = _mqttHandler.IsConnected;
                }
                
                await _hubContext.Clients.All.ReceiveStatus(status);

                // Periodically broadcast portal status
                if (_portalConnector.IsEnabled && 
                    DateTime.UtcNow - _lastPortalCheck > _portalCheckInterval)
                {
                    await BroadcastPortalStatusAsync(stoppingToken);
                    _lastPortalCheck = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting status");
            }

            await Task.Delay(_broadcastInterval, stoppingToken);
        }
    }

    private async Task BroadcastPortalStatusAsync(CancellationToken stoppingToken)
    {
        try
        {
            var isConnected = await _portalConnector.PingManagementPortalAsync(stoppingToken);
            
            var portalStatus = new ManagementPortalStatus
            {
                Enabled = _portalConnector.IsEnabled,
                Connected = isConnected,
                Registered = _portalConnector.IsRegistered,
                ServerName = _portalConnector.ServerName,
                PortalUrl = "https://management.honfigurator.app:3001",
                LastUpdated = DateTime.UtcNow
            };

            await _hubContext.Clients.All.ReceiveManagementPortalStatus(portalStatus);

            // Notify on connection state change via NotificationService
            if (_lastPortalConnected && !isConnected)
            {
                _logger.LogWarning("Management portal connection lost");
                _notificationService.NotifyPortalConnectionChange(false, "Lost connection to management portal");
                await _hubContext.Clients.All.ReceiveNotification(
                    "Portal Disconnected",
                    "Lost connection to management portal",
                    "warning");
            }
            else if (!_lastPortalConnected && isConnected && _lastPortalCheck != DateTime.MinValue)
            {
                _logger.LogInformation("Management portal connection restored");
                _notificationService.NotifyPortalConnectionChange(true);
                await _hubContext.Clients.All.ReceiveNotification(
                    "Portal Connected",
                    "Connection to management portal restored",
                    "success");
            }

            _lastPortalConnected = isConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking portal status");
        }
    }
}
