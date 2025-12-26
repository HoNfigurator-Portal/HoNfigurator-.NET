using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.GameServer.Services;

/// <summary>
/// Background service that manages connection to MasterServer and ChatServer
/// Similar to Python's authentication flow and chat server connection
/// </summary>
public class ConnectionManagerService : BackgroundService
{
    private readonly ILogger<ConnectionManagerService> _logger;
    private readonly IMasterServerConnector _masterServer;
    private readonly IChatServerConnector _chatServer;
    private readonly IGameServerManager _serverManager;
    private readonly HoNConfiguration _config;
    
    private bool _isAuthenticated;
    private bool _isChatConnected;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 10;
    private const int ReconnectDelaySeconds = 30;

    public bool IsAuthenticated => _isAuthenticated;
    public bool IsChatConnected => _isChatConnected;

    public event Action<bool>? OnAuthenticationChanged;
    public event Action<bool>? OnChatConnectionChanged;

    public ConnectionManagerService(
        ILogger<ConnectionManagerService> logger,
        IMasterServerConnector masterServer,
        IChatServerConnector chatServer,
        IGameServerManager serverManager,
        HoNConfiguration config)
    {
        _logger = logger;
        _masterServer = masterServer;
        _chatServer = chatServer;
        _serverManager = serverManager;
        _config = config;

        // Subscribe to connector events (using concrete types)
        if (_masterServer is MasterServerConnector ms)
        {
            ms.OnAuthenticated += () => { 
                _isAuthenticated = true; 
                _serverManager.MasterServerConnected = true;
                OnAuthenticationChanged?.Invoke(true); 
            };
            ms.OnDisconnected += () => { 
                _isAuthenticated = false; 
                _serverManager.MasterServerConnected = false;
                OnAuthenticationChanged?.Invoke(false); 
            };
        }

        if (_chatServer is ChatServerConnector cs)
        {
            cs.OnConnected += () => { 
                _isChatConnected = true; 
                _serverManager.ChatServerConnected = true;
                OnChatConnectionChanged?.Invoke(true); 
            };
            cs.OnDisconnected += HandleChatDisconnected;
            cs.OnHandshakeAccepted += HandleChatHandshakeAccepted;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConnectionManagerService starting...");

        // Wait a bit for the application to fully start
        await Task.Delay(2000, stoppingToken);

        // Check if we have credentials configured
        if (string.IsNullOrEmpty(_config.SvrLogin) || string.IsNullOrEmpty(_config.SvrPassword))
        {
            _logger.LogWarning("Server credentials not configured. Set svr_login and svr_password in config.json");
            return;
        }

        // Main connection loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Authenticate with MasterServer if not authenticated
                if (!_isAuthenticated)
                {
                    await AuthenticateAsync(stoppingToken);
                }

                // Connect to ChatServer if authenticated but not connected
                if (_isAuthenticated && !_isChatConnected && _masterServer.ChatServerHost != null)
                {
                    await ConnectToChatServerAsync(stoppingToken);
                }

                // Wait before next check
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in connection manager loop");
                await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), stoppingToken);
            }
        }

        // Cleanup on shutdown
        _logger.LogInformation("ConnectionManagerService stopping...");
        await _chatServer.DisconnectAsync();
        _masterServer.Disconnect();
    }

    private async Task AuthenticateAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Authenticating with MasterServer as {Username}", _config.SvrLogin);

        var result = await _masterServer.AuthenticateAsync(
            _config.SvrLogin!,
            _config.SvrPassword!,
            stoppingToken);

        if (result.Success)
        {
            _isAuthenticated = true;
            _reconnectAttempts = 0;
            _logger.LogInformation("Authentication successful. Server ID: {ServerId}, Chat: {ChatHost}:{ChatPort}",
                result.ServerId, result.ChatServerHost, result.ChatServerPort);
        }
        else
        {
            _reconnectAttempts++;
            _logger.LogError("Authentication failed: {Error}. Attempt {Attempt}/{Max}",
                result.Error, _reconnectAttempts, MaxReconnectAttempts);

            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                _logger.LogCritical("Max authentication attempts reached. Check your credentials.");
            }

            // Wait before retry
            await Task.Delay(TimeSpan.FromSeconds(ReconnectDelaySeconds), stoppingToken);
        }
    }

    private async Task ConnectToChatServerAsync(CancellationToken stoppingToken)
    {
        string? chatHost;
        int chatPort;
        
        // Use config ChatServer if specified, otherwise use MasterServer response
        if (!string.IsNullOrEmpty(_config.HonData.ChatServer))
        {
            // Parse from config (format: "host:port")
            var parts = _config.HonData.ChatServer.Split(':');
            chatHost = parts[0];
            chatPort = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 55554;
            _logger.LogInformation("Using ChatServer from config: {Host}:{Port}", chatHost, chatPort);
        }
        else if (_masterServer.ChatServerHost != null && _masterServer.ChatServerPort != null)
        {
            chatHost = _masterServer.ChatServerHost;
            chatPort = _masterServer.ChatServerPort.Value;
            _logger.LogInformation("Using ChatServer from MasterServer: {Host}:{Port}", chatHost, chatPort);
        }
        else
        {
            _logger.LogWarning("ChatServer address not available");
            return;
        }

        _logger.LogInformation("Connecting to ChatServer at {Host}:{Port}", chatHost, chatPort);

        var connected = await _chatServer.ConnectAsync(
            chatHost,
            chatPort,
            stoppingToken);

        if (connected && _masterServer.ServerId.HasValue && _masterServer.SessionId != null)
        {
            // Send handshake
            await _chatServer.SendHandshakeAsync(
                _masterServer.ServerId.Value,
                _masterServer.SessionId);
        }
    }

    private void HandleChatDisconnected()
    {
        _isChatConnected = false;
        _serverManager.ChatServerConnected = false;
        OnChatConnectionChanged?.Invoke(false);
        _logger.LogWarning("Disconnected from ChatServer. Will attempt reconnect...");
    }

    private async void HandleChatHandshakeAccepted()
    {
        _logger.LogInformation("ChatServer handshake accepted. Sending server info...");

        if (_masterServer.ServerId.HasValue)
        {
            try
            {
                // Get external IP (or use configured one)
                var ipAddress = _config.LocalIp ?? "0.0.0.0";
                var udpPingPort = _config.AutoPingRespPort ?? 10069;
                var region = _config.SvrLocation ?? "US";
                var serverName = _config.SvrName ?? "HoNfigurator Server";
                var version = _config.ManVersion ?? "4.10.6.0";

                await _chatServer.SendServerInfoAsync(
                    _masterServer.ServerId.Value,
                    _config.SvrLogin!,
                    region,
                    serverName,
                    version,
                    ipAddress,
                    udpPingPort);

                _logger.LogInformation("Server info sent to ChatServer. Ready for game server operations.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send server info to ChatServer");
            }
        }
    }

    /// <summary>
    /// Force reconnection to both servers
    /// </summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Forcing reconnection...");
        
        await _chatServer.DisconnectAsync();
        _masterServer.Disconnect();
        
        _isAuthenticated = false;
        _isChatConnected = false;
        _reconnectAttempts = 0;
    }
}
