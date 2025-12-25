using System.Net.Sockets;
using HoNfigurator.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Connectors;

/// <summary>
/// Match data for creating an arranged match (sent by chat server)
/// </summary>
public record ArrangedMatchData
{
    public int MatchId { get; init; }
    public string Map { get; init; } = "caldavar";
    public string GameMode { get; init; } = "normal";
    public ArrangedMatchType MatchType { get; init; } = ArrangedMatchType.Matchmaking;
    public List<ArrangedMatchPlayer> Team1 { get; init; } = new();
    public List<ArrangedMatchPlayer> Team2 { get; init; } = new();
    public Dictionary<string, string> Options { get; init; } = new();
}

/// <summary>
/// Player info for arranged match
/// </summary>
public record ArrangedMatchPlayer
{
    public int AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Slot { get; init; }
    public bool IsReady { get; init; }
}

/// <summary>
/// Result of client authentication with master server
/// </summary>
public record ClientAuthResult
{
    public int AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Manages connection from Game Server to Chat Server.
/// Implements game server protocol (0x0500-0x1500 range) per NEXUS ChatProtocol.
/// This is used by individual game server instances to communicate with the chat server.
/// </summary>
public interface IGameServerChatConnector
{
    bool IsConnected { get; }
    int? ServerId { get; }
    
    /// <summary>
    /// Connects to the chat server
    /// </summary>
    Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnects from the chat server
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Sends login request (NET_CHAT_GS_CONNECT 0x0500)
    /// </summary>
    Task<bool> SendLoginAsync(int serverId, string sessionId, int port);
    
    /// <summary>
    /// Sends server status update (NET_CHAT_GS_STATUS 0x0502)
    /// </summary>
    Task SendStatusAsync(NexusServerStatus status, int playerCount = 0, int? matchId = null);
    
    /// <summary>
    /// Announces match ready to players (NET_CHAT_GS_ANNOUNCE_MATCH 0x0503)
    /// </summary>
    Task SendAnnounceMatchAsync(int matchId, List<int> playerAccountIds);
    
    /// <summary>
    /// Reports match started (NET_CHAT_GS_MATCH_STARTED 0x0505)
    /// </summary>
    Task SendMatchStartedAsync(int matchId);
    
    /// <summary>
    /// Reports match ended (NET_CHAT_GS_MATCH_ENDED 0x0515)
    /// </summary>
    Task SendMatchEndedAsync(int matchId, int winningTeam);
    
    /// <summary>
    /// Reports client authentication result (NET_CHAT_GS_CLIENT_AUTH_RESULT 0x0513)
    /// </summary>
    Task SendClientAuthResultAsync(int accountId, bool success);
    
    /// <summary>
    /// Sends heartbeat (NET_CHAT_PING 0x2A00)
    /// </summary>
    Task SendHeartbeatAsync();
    
    // Events
    event Action? OnConnected;
    event Action? OnDisconnected;
    event Action? OnLoginAccepted;
    event Action<string>? OnLoginRejected;
    event Action<ArrangedMatchData>? OnCreateMatchRequest;
    event Action<int>? OnEndMatchRequest;
    event Action<string>? OnRemoteCommand;
    event Action<Dictionary<string, string>>? OnOptionsReceived;
    event Action? OnHeartbeatReceived;
}

public class GameServerChatConnector : IGameServerChatConnector, IDisposable
{
    private readonly ILogger<GameServerChatConnector> _logger;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private CancellationTokenSource? _keepaliveCts;
    private Task? _readTask;
    private Task? _keepaliveTask;
    private bool _disposed;
    private bool _loginAccepted;

    public bool IsConnected => _client?.Connected ?? false;
    public int? ServerId { get; private set; }

    // Events
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action? OnLoginAccepted;
    public event Action<string>? OnLoginRejected;
    public event Action<ArrangedMatchData>? OnCreateMatchRequest;
    public event Action<int>? OnEndMatchRequest;
    public event Action<string>? OnRemoteCommand;
    public event Action<Dictionary<string, string>>? OnOptionsReceived;
    public event Action? OnHeartbeatReceived;

    public GameServerChatConnector(ILogger<GameServerChatConnector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogWarning("Already connected to chat server");
            return true;
        }

        try
        {
            _client = new TcpClient();
            _client.ReceiveTimeout = 30000;
            _client.SendTimeout = 30000;
            _client.NoDelay = true;

            _logger.LogInformation("Game server connecting to chat server at {Host}:{Port}", host, port);
            await _client.ConnectAsync(host, port, cancellationToken);

            _stream = _client.GetStream();
            _loginAccepted = false;

            // Start reading packets
            _readCts = new CancellationTokenSource();
            _readTask = ReadPacketsAsync(_readCts.Token);

            _logger.LogInformation("Game server connected to chat server");
            OnConnected?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to chat server");
            await DisconnectAsync();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        // Stop keepalive
        if (_keepaliveCts != null)
        {
            await _keepaliveCts.CancelAsync();
            if (_keepaliveTask != null)
            {
                try { await _keepaliveTask; } catch { }
            }
        }

        // Stop reading
        if (_readCts != null)
        {
            await _readCts.CancelAsync();
            if (_readTask != null)
            {
                try { await _readTask; } catch { }
            }
        }

        // Send disconnect if connected
        if (_stream != null && IsConnected)
        {
            try
            {
                var buffer = new ChatBuffer();
                buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_DISCONNECT);
                var packet = buffer.BuildWithLengthPrefix();
                await _stream.WriteAsync(packet);
            }
            catch { }
        }

        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        _loginAccepted = false;
        ServerId = null;

        _logger.LogInformation("Game server disconnected from chat server");
        OnDisconnected?.Invoke();
    }

    public async Task<bool> SendLoginAsync(int serverId, string sessionId, int port)
    {
        if (_stream == null || !IsConnected)
        {
            _logger.LogError("Cannot send login: not connected");
            return false;
        }

        try
        {
            ServerId = serverId;

            // NET_CHAT_GS_CONNECT (0x0500): [command:2][server_id:4][session:string][port:2][protocol_version:4]
            var buffer = new ChatBuffer();
            buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_CONNECT);
            buffer.WriteInt32(serverId);
            buffer.WriteString(sessionId);
            buffer.WriteUInt16((ushort)port);
            buffer.WriteUInt32(ChatProtocol.CHAT_PROTOCOL_EXTERNAL_VERSION);

            var packet = buffer.BuildWithLengthPrefix();
            await _stream.WriteAsync(packet);
            await _stream.FlushAsync();

            _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_CONNECT] Sent login - ServerID: {ServerId}, Port: {Port}", serverId, port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send login");
            return false;
        }
    }

    public async Task SendStatusAsync(NexusServerStatus status, int playerCount = 0, int? matchId = null)
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        // NET_CHAT_GS_STATUS (0x0502): [command:2][status:1][player_count:1][match_id:4?]
        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_STATUS);
        buffer.WriteUInt8((byte)status);
        buffer.WriteUInt8((byte)Math.Min(playerCount, 255));
        
        if (matchId.HasValue)
        {
            buffer.WriteInt32(matchId.Value);
        }

        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_STATUS] Status: {Status}, Players: {PlayerCount}", status, playerCount);
    }

    public async Task SendAnnounceMatchAsync(int matchId, List<int> playerAccountIds)
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        // NET_CHAT_GS_ANNOUNCE_MATCH (0x0503): [command:2][match_id:4][player_count:1][account_ids:4*N]
        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_ANNOUNCE_MATCH);
        buffer.WriteInt32(matchId);
        buffer.WriteUInt8((byte)playerAccountIds.Count);
        
        foreach (var accountId in playerAccountIds)
        {
            buffer.WriteInt32(accountId);
        }

        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_ANNOUNCE_MATCH] Match: {MatchId}, Players: {PlayerCount}", matchId, playerAccountIds.Count);
    }

    public async Task SendMatchStartedAsync(int matchId)
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        // NET_CHAT_GS_MATCH_STARTED (0x0505): [command:2][match_id:4]
        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_MATCH_STARTED);
        buffer.WriteInt32(matchId);

        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_MATCH_STARTED] Match: {MatchId}", matchId);
    }

    public async Task SendMatchEndedAsync(int matchId, int winningTeam)
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        // NET_CHAT_GS_MATCH_ENDED (0x0515): [command:2][match_id:4][winning_team:1]
        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_MATCH_ENDED);
        buffer.WriteInt32(matchId);
        buffer.WriteUInt8((byte)winningTeam);

        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_MATCH_ENDED] Match: {MatchId}, Winner: Team {WinningTeam}", matchId, winningTeam);
    }

    public async Task SendClientAuthResultAsync(int accountId, bool success)
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        // NET_CHAT_GS_CLIENT_AUTH_RESULT (0x0513): [command:2][account_id:4][success:1]
        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.GameServerToChatServer.NET_CHAT_GS_CLIENT_AUTH_RESULT);
        buffer.WriteInt32(accountId);
        buffer.WriteBool(success);

        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_GS_CLIENT_AUTH_RESULT] Account: {AccountId}, Success: {Success}", accountId, success);
    }

    public async Task SendHeartbeatAsync()
    {
        if (_stream == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected to chat server");
        }

        var buffer = new ChatBuffer();
        buffer.WriteCommand(ChatProtocol.Bidirectional.NET_CHAT_PING);
        var packet = buffer.BuildWithLengthPrefix();
        await _stream.WriteAsync(packet);
        await _stream.FlushAsync();

        _logger.LogDebug(">>> [GS|CHAT] [NET_CHAT_PING] Sent heartbeat");
    }

    private void StartKeepalive()
    {
        _keepaliveCts?.Cancel();
        _keepaliveCts = new CancellationTokenSource();
        _keepaliveTask = KeepaliveLoopAsync(_keepaliveCts.Token);
    }

    private async Task KeepaliveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                // Wait 15 seconds
                for (int i = 0; i < 15; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    await Task.Delay(1000, cancellationToken);
                }

                if (_stream != null && IsConnected)
                {
                    await SendHeartbeatAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Keepalive failed");
                break;
            }
        }
    }

    private async Task ReadPacketsAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[2];

        while (!cancellationToken.IsCancellationRequested && IsConnected)
        {
            try
            {
                // Read packet length (2 bytes, little endian)
                var bytesRead = await _stream!.ReadAsync(lengthBuffer, 0, 2, cancellationToken);
                if (bytesRead < 2)
                {
                    _logger.LogWarning("Chat server connection closed (incomplete length read)");
                    break;
                }

                var packetLen = BitConverter.ToUInt16(lengthBuffer, 0);

                // Read full packet data
                var packetData = new byte[packetLen];
                var totalRead = 0;
                while (totalRead < packetLen)
                {
                    var chunk = await _stream.ReadAsync(packetData, totalRead, packetLen - totalRead, cancellationToken);
                    if (chunk == 0)
                    {
                        _logger.LogWarning("Chat server connection closed during packet read");
                        break;
                    }
                    totalRead += chunk;
                }

                if (totalRead < packetLen) break;

                // Extract packet type (first 2 bytes of data)
                var packetType = BitConverter.ToUInt16(packetData, 0);

                // Handle the packet
                await HandlePacketAsync(packetType, packetData.AsMemory(2));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                _logger.LogWarning("Chat server connection reset");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading packet from chat server");
                break;
            }
        }

        // Connection lost - notify
        if (!cancellationToken.IsCancellationRequested)
        {
            _ = Task.Run(() => OnDisconnected?.Invoke());
        }
    }

    private async Task HandlePacketAsync(ushort packetType, ReadOnlyMemory<byte> data)
    {
        var prefix = "<<< [GS|CHATSV]";

        try
        {
            switch (packetType)
            {
                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_ACCEPT:
                    _logger.LogDebug("{Prefix} [NET_CHAT_GS_ACCEPT] Login accepted", prefix);
                    _loginAccepted = true;
                    OnLoginAccepted?.Invoke();
                    StartKeepalive();
                    break;

                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_REJECT:
                    var rejectReason = ParseRejectReason(data.Span);
                    _logger.LogWarning("{Prefix} [NET_CHAT_GS_REJECT] Login rejected: {Reason}", prefix, rejectReason);
                    OnLoginRejected?.Invoke(rejectReason);
                    break;

                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_CREATE_MATCH:
                    var matchData = ParseCreateMatchRequest(data.Span);
                    _logger.LogDebug("{Prefix} [NET_CHAT_GS_CREATE_MATCH] Match: {MatchId}", prefix, matchData.MatchId);
                    OnCreateMatchRequest?.Invoke(matchData);
                    break;

                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_END_MATCH:
                    var matchId = ParseEndMatchRequest(data.Span);
                    _logger.LogDebug("{Prefix} [NET_CHAT_GS_END_MATCH] Match: {MatchId}", prefix, matchId);
                    OnEndMatchRequest?.Invoke(matchId);
                    break;

                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_REMOTE_COMMAND:
                    var command = ParseRemoteCommand(data.Span);
                    _logger.LogDebug("{Prefix} [NET_CHAT_GS_REMOTE_COMMAND] Command: {Command}", prefix, command);
                    OnRemoteCommand?.Invoke(command);
                    break;

                case ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_OPTIONS:
                    var options = ParseOptions(data.Span);
                    _logger.LogDebug("{Prefix} [NET_CHAT_GS_OPTIONS] Received {Count} options", prefix, options.Count);
                    OnOptionsReceived?.Invoke(options);
                    break;

                case ChatProtocol.Bidirectional.NET_CHAT_PONG:
                    _logger.LogDebug("{Prefix} [NET_CHAT_PONG] Heartbeat received", prefix);
                    OnHeartbeatReceived?.Invoke();
                    break;

                default:
                    _logger.LogDebug("{Prefix} Unhandled packet: 0x{PacketType:X4}", prefix, packetType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Prefix} Error handling packet 0x{PacketType:X4}", prefix, packetType);
        }
    }

    private string ParseRejectReason(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "Unknown";
        var reader = new ChatBufferReader(data);
        return reader.HasMore ? reader.ReadString() : "Unknown";
    }

    private ArrangedMatchData ParseCreateMatchRequest(ReadOnlySpan<byte> data)
    {
        var reader = new ChatBufferReader(data);
        
        var matchData = new ArrangedMatchData
        {
            MatchId = reader.ReadInt32(),
            Map = reader.ReadString(),
            GameMode = reader.ReadString(),
            MatchType = (ArrangedMatchType)reader.ReadUInt8()
        };

        // Parse teams
        var team1Count = reader.ReadUInt8();
        var team1 = new List<ArrangedMatchPlayer>();
        for (int i = 0; i < team1Count; i++)
        {
            team1.Add(new ArrangedMatchPlayer
            {
                AccountId = reader.ReadInt32(),
                Name = reader.ReadString(),
                Slot = reader.ReadUInt8()
            });
        }

        var team2Count = reader.ReadUInt8();
        var team2 = new List<ArrangedMatchPlayer>();
        for (int i = 0; i < team2Count; i++)
        {
            team2.Add(new ArrangedMatchPlayer
            {
                AccountId = reader.ReadInt32(),
                Name = reader.ReadString(),
                Slot = reader.ReadUInt8()
            });
        }

        return matchData with { Team1 = team1, Team2 = team2 };
    }

    private int ParseEndMatchRequest(ReadOnlySpan<byte> data)
    {
        var reader = new ChatBufferReader(data);
        return reader.ReadInt32();
    }

    private string ParseRemoteCommand(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return string.Empty;
        var reader = new ChatBufferReader(data);
        return reader.HasMore ? reader.ReadString() : string.Empty;
    }

    private Dictionary<string, string> ParseOptions(ReadOnlySpan<byte> data)
    {
        var options = new Dictionary<string, string>();
        if (data.Length == 0) return options;

        try
        {
            var reader = new ChatBufferReader(data);
            while (reader.HasMore)
            {
                var key = reader.ReadString();
                if (string.IsNullOrEmpty(key)) break;
                var value = reader.HasMore ? reader.ReadString() : string.Empty;
                options[key] = value;
            }
        }
        catch
        {
            // Options parsing can be variable
        }

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _keepaliveCts?.Cancel();
        _keepaliveCts?.Dispose();
        _readCts?.Cancel();
        _readCts?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
    }
}
