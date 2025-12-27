using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Discord;
using HoNfigurator.Core.Statistics;
using HoNfigurator.Core.Services;

namespace HoNfigurator.GameServer.Services;

/// <summary>
/// TCP listener that receives status updates from HoN game servers.
/// Game servers connect to this manager and send periodic status packets.
/// </summary>
public interface IGameServerListener
{
    Task StartAsync(int port);
    Task StopAsync();
    bool IsListening { get; }
    
    /// <summary>
    /// Send a graceful shutdown command to the game server.
    /// This will kick all players and close the server properly.
    /// </summary>
    Task<bool> SendShutdownCommandAsync(int serverId);
    
    /// <summary>
    /// Send a message to all players in the game server.
    /// </summary>
    Task<bool> SendMessageAsync(int serverId, string message);
    
    /// <summary>
    /// Send a console command to the game server.
    /// </summary>
    Task<bool> SendCommandAsync(int serverId, string command);
}

public class GameServerListener : IGameServerListener
{
    private readonly ILogger<GameServerListener> _logger;
    private readonly IGameServerManager _serverManager;
    private readonly IGameLogReader _logReader;
    private readonly HoNConfiguration _config;
    private readonly IMqttHandler? _mqttHandler;
    private readonly IDiscordBotService? _discordBot;
    private readonly IMatchStatisticsService? _statisticsService;
    private readonly ReplayManager? _replayManager;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<int, TcpClient> _clientConnections = new();
    private readonly ConcurrentDictionary<int, bool> _botMatchCheckDone = new();
    private readonly ConcurrentDictionary<int, HashSet<string>> _previousPlayers = new(); // Track players for join/leave events
    private readonly ConcurrentDictionary<int, long> _activeMatchIds = new(); // Track active match IDs for statistics
    private readonly ConcurrentDictionary<int, DateTime> _matchStartTimes = new(); // Track match start times
    
    public bool IsListening { get; private set; }

    public GameServerListener(
        ILogger<GameServerListener> logger, 
        IGameServerManager serverManager, 
        IGameLogReader logReader,
        HoNConfiguration config,
        IMqttHandler? mqttHandler = null,
        IDiscordBotService? discordBot = null,
        IMatchStatisticsService? statisticsService = null,
        ReplayManager? replayManager = null)
    {
        _logger = logger;
        _serverManager = serverManager;
        _logReader = logReader;
        _config = config;
        _mqttHandler = mqttHandler;
        _discordBot = discordBot;
        _statisticsService = statisticsService;
        _replayManager = replayManager;
    }

    public async Task StartAsync(int port)
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        
        try
        {
            _listener.Start();
            IsListening = true;
            _logger.LogInformation("GameServerListener started on port {Port}", port);

            // Accept connections in background
            _ = AcceptClientsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start GameServerListener on port {Port}", port);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsListening) return;

        _cts?.Cancel();
        _listener?.Stop();
        
        // Close all client connections
        foreach (var (id, client) in _clientConnections)
        {
            try { client.Close(); } catch { }
        }
        _clientConnections.Clear();
        
        IsListening = false;
        _logger.LogInformation("GameServerListener stopped");
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Send a graceful shutdown command to the game server.
    /// This kicks all players and closes the server properly.
    /// </summary>
    public async Task<bool> SendShutdownCommandAsync(int serverId)
    {
        if (!_clientConnections.TryGetValue(serverId, out var client) || client == null)
        {
            _logger.LogWarning("Cannot send shutdown command: Server #{Id} not connected", serverId);
            return false;
        }

        try
        {
            var stream = client.GetStream();
            
            // Send length prefix (1 byte) followed by shutdown command (0x22)
            await stream.WriteAsync(GameServerCommands.CommandLengthBytes);
            await stream.WriteAsync(GameServerCommands.ShutdownBytes);
            await stream.FlushAsync();
            
            _logger.LogInformation("Shutdown command sent to Server #{Id}", serverId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shutdown command to Server #{Id}", serverId);
            return false;
        }
    }

    /// <summary>
    /// Send a message to all players in the game server.
    /// </summary>
    public async Task<bool> SendMessageAsync(int serverId, string message)
    {
        if (!_clientConnections.TryGetValue(serverId, out var client) || client == null)
        {
            _logger.LogWarning("Cannot send message: Server #{Id} not connected", serverId);
            return false;
        }

        try
        {
            var stream = client.GetStream();
            
            // Build message packet: prefix byte (0x24) + message + null terminator
            // Use UTF-8 encoding to support international characters (Thai, Chinese, etc.)
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(message);
            var packet = new byte[1 + messageBytes.Length + 1];
            packet[0] = GameServerCommands.MessagePrefixByte[0];
            Array.Copy(messageBytes, 0, packet, 1, messageBytes.Length);
            packet[packet.Length - 1] = 0x00; // null terminator
            
            // Write length (2 bytes, little endian) followed by packet
            var lengthBytes = BitConverter.GetBytes((ushort)packet.Length);
            await stream.WriteAsync(lengthBytes);
            await stream.WriteAsync(packet);
            await stream.FlushAsync();
            
            _logger.LogInformation("Message sent to Server #{Id}: {Message}", serverId, message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to Server #{Id}", serverId);
            return false;
        }
    }

    /// <summary>
    /// Send a console command to the game server (e.g., terminateplayer, serverreset).
    /// </summary>
    public async Task<bool> SendCommandAsync(int serverId, string command)
    {
        if (!_clientConnections.TryGetValue(serverId, out var client) || client == null)
        {
            _logger.LogWarning("Cannot send command: Server #{Id} not connected", serverId);
            return false;
        }

        try
        {
            var stream = client.GetStream();
            
            // Build command packet: prefix byte (0x25) + command + null terminator
            var commandBytes = System.Text.Encoding.UTF8.GetBytes(command);
            var packet = new byte[1 + commandBytes.Length + 1];
            packet[0] = GameServerCommands.CommandPrefixByte[0];
            Array.Copy(commandBytes, 0, packet, 1, commandBytes.Length);
            packet[packet.Length - 1] = 0x00; // null terminator
            
            // Write length (2 bytes, little endian) followed by packet
            var lengthBytes = BitConverter.GetBytes((ushort)packet.Length);
            await stream.WriteAsync(lengthBytes);
            await stream.WriteAsync(packet);
            await stream.FlushAsync();
            
            _logger.LogInformation("Command sent to Server #{Id}: {Command}", serverId, command);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to Server #{Id}", serverId);
            return false;
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                _logger.LogInformation("Game server connected from {Address}:{Port}", 
                    endpoint?.Address, endpoint?.Port);

                // Handle this client in background
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        int? serverId = null;
        var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
        
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            
            // Wait for first packet - should be 0x40 (server announce)
            // Read packet length (2 bytes, little endian)
            var lengthBytes = new byte[2];
            var bytesRead = await ReadExactlyAsync(stream, lengthBytes, 2, ct);
            
            if (bytesRead < 2)
            {
                _logger.LogWarning("Connection closed before receiving first packet");
                return;
            }

            var packetLength = BitConverter.ToUInt16(lengthBytes, 0);
            bytesRead = await ReadExactlyAsync(stream, buffer, packetLength, ct);
            
            if (bytesRead < packetLength)
            {
                _logger.LogWarning("Incomplete first packet");
                return;
            }
            
            var packetType = buffer[0];
            _logger.LogInformation("First packet type: 0x{Type:X2}, length {Length}", packetType, packetLength);
            
            // Must be 0x40 (server announce) first
            if (packetType == 0x40)
            {
                serverId = await HandleServerConnectAsync(buffer, packetLength);
            }
            else
            {
                _logger.LogWarning("Expected 0x40 (server announce) but got 0x{Type:X2}", packetType);
                // Try to find server by process - fallback
                return;
            }
            
            if (!serverId.HasValue)
            {
                _logger.LogWarning("Could not determine server ID from announce packet");
                return;
            }
            
            _clientConnections[serverId.Value] = client;
            _logger.LogInformation("Server #{Id} connected and ready for status updates", serverId);

            // Now process remaining packets
            while (!ct.IsCancellationRequested && client.Connected)
            {
                lengthBytes = new byte[2];
                bytesRead = await ReadExactlyAsync(stream, lengthBytes, 2, ct);
                
                if (bytesRead < 2)
                {
                    _logger.LogDebug("Connection closed - incomplete length header");
                    break;
                }

                packetLength = BitConverter.ToUInt16(lengthBytes, 0);
                
                if (packetLength > buffer.Length)
                {
                    _logger.LogWarning("Packet too large: {Length}", packetLength);
                    continue;
                }

                // Read packet data
                bytesRead = await ReadExactlyAsync(stream, buffer, packetLength, ct);
                
                if (bytesRead < packetLength)
                {
                    _logger.LogDebug("Connection closed - incomplete packet data");
                    break;
                }

                // Process packet
                serverId = await ProcessPacketAsync(buffer, packetLength, serverId);
                
                if (serverId.HasValue && !_clientConnections.ContainsKey(serverId.Value))
                {
                    _clientConnections[serverId.Value] = client;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client connection error");
        }
        finally
        {
            if (serverId.HasValue)
            {
                _clientConnections.TryRemove(serverId.Value, out _);
                _logger.LogInformation("Game server #{Id} disconnected", serverId);
            }
            
            try { client.Close(); } catch { }
        }
    }

    private async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }
        return totalRead;
    }

    private async Task<int?> ProcessPacketAsync(byte[] data, int length, int? currentServerId)
    {
        if (length < 1) return currentServerId;

        var packetType = data[0];
        
        // Only log non-status packets (0x42 is too frequent)
        if (packetType != 0x42)
        {
            _logger.LogInformation("Received packet type 0x{Type:X2}, length {Length}, serverId {ServerId}", 
                packetType, length, currentServerId);
        }

        switch (packetType)
        {
            case 0x40: // Server connect
                return await HandleServerConnectAsync(data, length);
                
            case 0x41: // Server closed
                if (currentServerId.HasValue)
                {
                    await HandleServerClosedAsync(currentServerId.Value);
                }
                return currentServerId;
                
            case 0x42: // Server status update
                if (currentServerId.HasValue)
                {
                    await HandleServerStatusAsync(data, length, currentServerId.Value);
                }
                else
                {
                    _logger.LogWarning("Received status packet but no serverId set");
                }
                return currentServerId;
                
            default:
                _logger.LogInformation("Unknown packet type: 0x{Type:X2}", packetType);
                return currentServerId;
        }
    }

    private async Task<int?> HandleServerConnectAsync(byte[] data, int length)
    {
        // Packet 0x40: Server announce
        // Format: [msg_type:1][port:2 bytes little endian]
        // Python: port = int.from_bytes(packet[1:],byteorder='little')
        if (length < 3) return null;

        // Read port as 2 bytes (UInt16) little endian
        var serverPort = BitConverter.ToUInt16(data, 1);
        _logger.LogInformation("Game server on port {Port} connected to manager", serverPort);
        
        // Find server by port
        var instance = _serverManager.Instances.FirstOrDefault(s => s.Port == serverPort);
        if (instance != null)
        {
            instance.Status = ServerStatus.Ready;
            _logger.LogInformation("Mapped to server #{Id}", instance.Id);
            
            // Publish MQTT event
            if (_mqttHandler != null)
            {
                await _mqttHandler.PublishServerStatusAsync(instance.Id, MqttEventTypes.ServerReady, new
                {
                    Port = instance.Port,
                    Name = instance.Name
                });
            }
            
            return await Task.FromResult<int?>(instance.Id);
        }
        
        _logger.LogWarning("No server instance found for port {Port}", serverPort);
        return null;
    }

    private async Task HandleServerClosedAsync(int serverId)
    {
        _logger.LogInformation("Game server #{Id} closed", serverId);
        
        var instance = _serverManager.Instances.FirstOrDefault(s => s.Id == serverId);
        if (instance != null)
        {
            instance.Status = ServerStatus.Offline;
            instance.NumClients = 0;
            instance.GamePhase = string.Empty;
            
            // Publish MQTT event
            if (_mqttHandler != null)
            {
                await _mqttHandler.PublishServerStatusAsync(serverId, MqttEventTypes.ServerOffline, new
                {
                    Port = instance.Port,
                    Name = instance.Name
                });
            }
        }
        
        // Clear player tracking for this server
        _previousPlayers.TryRemove(serverId, out _);
    }

    private async Task HandleServerStatusAsync(byte[] data, int length, int serverId)
    {
        // Packet 0x42: Server status update
        // Fixed structure (54 bytes minimum):
        // [0] msg_type
        // [1] status
        // [2-6] uptime (4 bytes, little endian)
        // [6-10] server load
        // [10] num_clients
        // [11] match_started
        // [12-40] various stats
        // [40] game_phase
        // [41-53] more stats
        // [53+] player data if num_clients > 0

        if (length < 54) return;

        var instance = _serverManager.Instances.FirstOrDefault(s => s.Id == serverId);
        if (instance == null) return;

        // Parse status fields
        var uptime = BitConverter.ToUInt32(data, 2);
        var cpuLoad = BitConverter.ToUInt32(data, 6) / 100.0;
        var numClients = data[10];
        var matchStarted = data[11];
        var gamePhase = data[40];

        var previousPhase = instance.GamePhase;
        var previousStatus = instance.Status;

        // Update instance
        instance.NumClients = numClients;
        instance.CpuPercent = cpuLoad;
        instance.GamePhase = GetGamePhaseName(gamePhase);
        
        // Update status based on game phase
        if (numClients > 0 || matchStarted > 0)
        {
            instance.Status = ServerStatus.Occupied;
        }
        else if (instance.Status != ServerStatus.Starting)
        {
            instance.Status = ServerStatus.Ready;
        }

        // Publish status change events
        if (_mqttHandler != null && previousStatus != instance.Status)
        {
            var eventType = instance.Status == ServerStatus.Occupied 
                ? MqttEventTypes.ServerOccupied 
                : MqttEventTypes.ServerReady;
            await _mqttHandler.PublishServerStatusAsync(serverId, eventType, new
            {
                Phase = instance.GamePhase,
                NumClients = numClients,
                MatchStarted = matchStarted > 0
            });
        }

        // Parse player data if packet is longer than 54 bytes
        if (length > 54 && numClients > 0)
        {
            instance.Players = ParsePlayerData(data, length);
            // Populate team assignments from game log
            _logReader.PopulateTeams(instance);
            
            // Track player join/leave events
            await TrackPlayerEventsAsync(serverId, instance.Players);
            
            // Check for bot match when game starts
            if (matchStarted > 0 && !_botMatchCheckDone.GetValueOrDefault(serverId, false))
            {
                await CheckBotMatchAsync(instance);
                _botMatchCheckDone[serverId] = true;
                
                var playerNames = instance.Players.Select(p => p.Name).ToList();
                
                // Record match start in statistics
                if (_statisticsService != null)
                {
                    try
                    {
                        var matchId = await _statisticsService.RecordMatchStartAsync(
                            serverId, 
                            instance.Name, 
                            playerNames,
                            instance.GamePhase);
                        _activeMatchIds[serverId] = matchId;
                        _matchStartTimes[serverId] = DateTime.UtcNow;
                        _logger.LogInformation("Match #{MatchId} started on Server #{ServerId}", matchId, serverId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to record match start for Server #{ServerId}", serverId);
                    }
                }
                
                // Send Discord notification for match started
                if (_discordBot != null && _discordBot.IsConnected)
                {
                    try
                    {
                        await _discordBot.SendMatchStartedAsync(serverId, instance.Name, playerNames);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send Discord match started notification");
                    }
                }
                
                // Publish match started event
                if (_mqttHandler != null)
                {
                    await _mqttHandler.PublishMatchEventAsync(serverId, MqttEventTypes.MatchStarted, new
                    {
                        PlayerCount = numClients,
                        Players = playerNames,
                        Phase = instance.GamePhase
                    });
                }
            }
        }
        else if (numClients == 0)
        {
            // Check if we had players before (match ended) - only trigger once
            if (_previousPlayers.TryRemove(serverId, out var prevPlayers) && prevPlayers.Count > 0)
            {
                // Calculate match duration
                var duration = 0;
                if (_matchStartTimes.TryRemove(serverId, out var startTime))
                {
                    duration = (int)(DateTime.UtcNow - startTime).TotalSeconds;
                }
                
                // Record match end in statistics
                if (_statisticsService != null && _activeMatchIds.TryRemove(serverId, out var matchId))
                {
                    try
                    {
                        await _statisticsService.RecordMatchEndAsync(matchId);
                        _logger.LogInformation("Match #{MatchId} ended on Server #{ServerId}, duration: {Duration}s", 
                            matchId, serverId, duration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to record match end for Match #{MatchId}", matchId);
                    }
                }
                
                // Send Discord notification for match ended
                if (_discordBot != null && _discordBot.IsConnected)
                {
                    try
                    {
                        await _discordBot.SendMatchEndedAsync(serverId, instance.Name, duration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send Discord match ended notification");
                    }
                }
                
                // Auto-upload replay if enabled and ReplayManager available
                if (_replayManager != null && _config.ApplicationData?.ReplayUpload?.Enabled == true && 
                    _config.ApplicationData?.ReplayUpload?.AutoUploadOnMatchEnd == true)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Find replay file for this server
                            var replayPath = FindLatestReplay(serverId);
                            if (!string.IsNullOrEmpty(replayPath) && File.Exists(replayPath))
                            {
                                var matchId = _activeMatchIds.TryGetValue(serverId, out var id) ? id : 0;
                                var fileName = Path.GetFileName(replayPath);
                                
                                // Queue for upload to master server
                                _replayManager.QueueUpload(fileName, matchId);
                                    
                                _logger.LogInformation("Replay queued for upload: {FileName}", fileName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to queue replay upload for Server #{ServerId}", serverId);
                        }
                    });
                }
                
                // Publish match ended event
                if (_mqttHandler != null)
                {
                    await _mqttHandler.PublishMatchEventAsync(serverId, MqttEventTypes.MatchEnded, new
                    {
                        PreviousPlayerCount = prevPlayers.Count,
                        DurationSeconds = duration
                    });
                }
            }
            
            instance.Players.Clear();
            instance.PlayersByTeam.Legion.Clear();
            instance.PlayersByTeam.Hellbourne.Clear();
            instance.PlayersByTeam.Spectators.Clear();
            // Reset bot match check flag when server becomes empty
            _botMatchCheckDone[serverId] = false;
        }

        // Log occasionally for debugging
        _logger.LogDebug("Server #{Id}: Phase={Phase}, Clients={Clients}, CPU={Cpu:F1}%", 
            serverId, instance.GamePhase, numClients, cpuLoad);

        await Task.CompletedTask;
    }

    private List<PlayerInfo> ParsePlayerData(byte[] data, int length)
    {
        // Player data starts at byte 53
        // Format: [num_players:1] then for each player:
        // - 4 bytes: account_id (BEFORE IP)
        // - null-terminated string: IP address
        // - null-terminated string: name
        // - null-terminated string: location
        // - 2 bytes each: minping, avgping, maxping
        // - Additional stats

        var players = new List<PlayerInfo>();
        
        if (length <= 54) return players;

        // Copy player data section (from byte 53 onwards)
        var playerData = new byte[length - 53];
        Array.Copy(data, 53, playerData, 0, length - 53);
        
        int numPlayers = playerData[0];
        _logger.LogDebug("Parsing {NumPlayers} players from {Length} bytes of player data", numPlayers, playerData.Length);

        // Use regex to find all IP addresses like Python does
        var ipPattern = new System.Text.RegularExpressions.Regex(@"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
        var dataString = System.Text.Encoding.ASCII.GetString(playerData);
        var matches = ipPattern.Matches(dataString);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (players.Count >= numPlayers) break;

            try
            {
                int ipStart = match.Index;
                
                // Account ID is 4 bytes before IP start
                if (ipStart < 4) continue;
                int accountId = BitConverter.ToInt32(playerData, ipStart - 4);
                
                // Extract IP (find null terminator)
                int ipEnd = Array.IndexOf(playerData, (byte)0, ipStart);
                if (ipEnd < 0) continue;
                string ip = System.Text.Encoding.UTF8.GetString(playerData, ipStart, ipEnd - ipStart);
                
                // Extract name (null-terminated string after IP)
                int nameStart = ipEnd + 1;
                int nameEnd = Array.IndexOf(playerData, (byte)0, nameStart);
                if (nameEnd < 0) continue;
                string name = System.Text.Encoding.UTF8.GetString(playerData, nameStart, nameEnd - nameStart);
                
                // Extract location (null-terminated string after name)
                int locStart = nameEnd + 1;
                int locEnd = Array.IndexOf(playerData, (byte)0, locStart);
                if (locEnd < 0) continue;
                string location = System.Text.Encoding.UTF8.GetString(playerData, locStart, locEnd - locStart);
                
                // Extract pings (3 shorts = 6 bytes after location)
                int pingStart = locEnd + 1;
                if (pingStart + 6 > playerData.Length) continue;
                
                int minPing = BitConverter.ToUInt16(playerData, pingStart);
                int avgPing = BitConverter.ToUInt16(playerData, pingStart + 2);
                int maxPing = BitConverter.ToUInt16(playerData, pingStart + 4);
                
                _logger.LogDebug("Player: {Name}, IP: {IP}, AccountId: {AccountId}, Ping: {Min}/{Avg}/{Max}", 
                    name, ip, accountId, minPing, avgPing, maxPing);
                
                players.Add(new PlayerInfo
                {
                    AccountId = accountId,
                    Ip = ip,
                    Name = name,
                    Location = location,
                    MinPing = minPing,
                    AvgPing = avgPing,
                    MaxPing = maxPing
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse player at IP match {Index}: {Error}", match.Index, ex.Message);
            }
        }
        
        _logger.LogDebug("Parsed {Count} players from status packet", players.Count);
        return players;
    }

    private async Task CheckBotMatchAsync(GameServerInstance instance)
    {
        // Check if bot match is enabled in config
        if (_config.HonData.EnableBotMatch)
        {
            _logger.LogDebug("Bot match allowed, skipping check");
            return;
        }
        
        // Get match info from log
        var matchInfo = _logReader.GetMatchInfo(instance.Id);
        if (matchInfo == null)
        {
            _logger.LogDebug("Could not get match info for server #{Id}", instance.Id);
            return;
        }
        
        if (matchInfo.IsBotMatch)
        {
            _logger.LogWarning("Bot match detected on Server #{Id} but bot matches are disabled! Restarting server...", instance.Id);
            
            // Send warning message to players (if we have ability)
            // For now, just restart the server
            await Task.Delay(5000); // Give players a moment
            
            // Restart the server
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Restarting Server #{Id} due to disallowed bot match", instance.Id);
                    await _serverManager.RestartServerAsync(instance.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart server #{Id} after bot match detection", instance.Id);
                }
            });
        }
    }

    private static string GetGamePhaseName(byte phase)
    {
        return phase switch
        {
            0 => "Idle",
            1 => "Lobby",
            2 => "Hero Select",
            3 => "Loading",
            4 => "Pre-Game",
            5 => "Playing",
            6 => "Ending",
            7 => "Post-Game",
            _ => $"Phase {phase}"
        };
    }

    /// <summary>
    /// Track player join/leave events and publish MQTT notifications
    /// </summary>
    private async Task TrackPlayerEventsAsync(int serverId, List<PlayerInfo> currentPlayers)
    {
        var currentPlayerNames = currentPlayers.Select(p => p.Name).ToHashSet();
        
        if (!_previousPlayers.TryGetValue(serverId, out var previousPlayerNames))
        {
            previousPlayerNames = new HashSet<string>();
        }

        // Find players who joined (in current but not in previous)
        var joinedPlayers = currentPlayerNames.Except(previousPlayerNames).ToList();
        
        // Find players who left (in previous but not in current)
        var leftPlayers = previousPlayerNames.Except(currentPlayerNames).ToList();

        var instance = _serverManager.Instances.FirstOrDefault(s => s.Id == serverId);
        var serverName = instance?.Name ?? $"Server #{serverId}";

        // Publish MQTT events and Discord notifications for joined players
        foreach (var playerName in joinedPlayers)
        {
            var player = currentPlayers.FirstOrDefault(p => p.Name == playerName);
            
            if (_mqttHandler != null)
            {
                await _mqttHandler.PublishPlayerEventAsync(serverId, MqttEventTypes.PlayerJoined, playerName, new
                {
                    AccountId = player?.AccountId ?? 0,
                    Location = player?.Location ?? "",
                    Ping = player?.AvgPing ?? 0
                });
            }
            
            // Send Discord notification
            if (_discordBot != null && _discordBot.IsConnected && 
                _config.ApplicationData?.Discord?.NotifyPlayerJoinLeave == true)
            {
                try
                {
                    await _discordBot.SendPlayerJoinedAsync(serverId, serverName, playerName);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send Discord player joined notification");
                }
            }
            
            _logger.LogInformation("Player joined Server #{ServerId}: {Player}", serverId, playerName);
        }

        // Publish MQTT events and Discord notifications for players who left
        foreach (var playerName in leftPlayers)
        {
            if (_mqttHandler != null)
            {
                await _mqttHandler.PublishPlayerEventAsync(serverId, MqttEventTypes.PlayerLeft, playerName, null);
            }
            
            // Send Discord notification
            if (_discordBot != null && _discordBot.IsConnected && 
                _config.ApplicationData?.Discord?.NotifyPlayerJoinLeave == true)
            {
                try
                {
                    await _discordBot.SendPlayerLeftAsync(serverId, serverName, playerName);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send Discord player left notification");
                }
            }
            
            _logger.LogInformation("Player left Server #{ServerId}: {Player}", serverId, playerName);
        }

        // Update the tracking dictionary
        _previousPlayers[serverId] = currentPlayerNames;
    }

    /// <summary>
    /// Find the latest replay file for a server
    /// </summary>
    private string? FindLatestReplay(int serverId)
    {
        try
        {
            // Look for replay files in the server's replay directory
            var replayDir = Path.Combine(_config.HonData.HonInstallDirectory ?? "", "game", "replays");
            if (!Directory.Exists(replayDir))
            {
                // Try alternative path
                replayDir = Path.Combine(AppContext.BaseDirectory, "replays");
            }
            
            if (!Directory.Exists(replayDir))
            {
                _logger.LogDebug("Replay directory not found: {Path}", replayDir);
                return null;
            }
            
            // Find the most recent .honreplay file
            var replayFiles = Directory.GetFiles(replayDir, "*.honreplay")
                .Union(Directory.GetFiles(replayDir, "*.honReplay"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
                
            if (replayFiles.Count > 0)
            {
                var latestReplay = replayFiles[0];
                _logger.LogDebug("Found latest replay: {Path}", latestReplay);
                return latestReplay;
            }
            
            _logger.LogDebug("No replay files found in {Path}", replayDir);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding replay for Server #{ServerId}", serverId);
            return null;
        }
    }
}
