using System.Text;
using System.Text.RegularExpressions;

namespace HoNfigurator.Core.Protocol;

/// <summary>
/// Game server packet types sent from game server to manager (local protocol, 0x40-0x4A range)
/// </summary>
public enum GameServerPacketType : byte
{
    ServerAnnounce = 0x40,
    ServerClosed = 0x41,
    ServerStatus = 0x42,
    LongFrame = 0x43,
    LobbyCreated = 0x44,
    LobbyClosed = 0x45,
    ServerBeingUsed = 0x46,
    ServerConnection = 0x47,
    CowStatsSubmission = 0x48,
    CowAnnounce = 0x49,
    ReplayUpdate = 0x4A
}

/// <summary>
/// Chat server packet types for manager-chat communication.
/// Maps to NEXUS ChatProtocol ServerManager/ChatServer commands.
/// </summary>
public enum ManagerChatPacketType : ushort
{
    // Chat Server to Server Manager (0x1700 range) - ChatServerToServerManager in NEXUS
    ChatHandshakeAccepted = 0x1700,  // NET_CHAT_SM_ACCEPT
    ChatHandshakeRejected = 0x1701,  // NET_CHAT_SM_REJECT
    ChatRemoteCommand = 0x1702,      // NET_CHAT_SM_REMOTE_COMMAND
    ChatPolicies = 0x1703,           // NET_CHAT_SM_OPTIONS
    ChatReplayRequest = 0x1704,      // NET_CHAT_SM_UPLOAD_REQUEST
    ChatShutdownNotice = 0x0400,     // Shutdown notice
    ChatHeartbeatReceived = 0x2A01,  // NET_CHAT_PONG
    
    // Server Manager to Chat Server (0x1600 range) - ServerManagerToChatServer in NEXUS
    MgrHandshakeRequest = 0x1600,    // NET_CHAT_SM_CONNECT
    MgrDisconnect = 0x1601,          // NET_CHAT_SM_DISCONNECT
    MgrServerInfoUpdate = 0x1602,    // NET_CHAT_SM_STATUS
    MgrReplayResponse = 0x1603,      // NET_CHAT_SM_UPLOAD_UPDATE
    MgrSendingHeartbeat = 0x2A00     // NET_CHAT_PING
}

/// <summary>
/// Game-Chat server packet types.
/// Maps to NEXUS ChatProtocol GameServer/ChatServer commands.
/// </summary>
public enum GameChatPacketType : ushort
{
    // Game Server to Chat Server (0x0500 range) - GameServerToChatServer in NEXUS
    GameLogin = 0x0500,              // NET_CHAT_GS_CONNECT
    GameServerClosed = 0x0501,       // NET_CHAT_GS_DISCONNECT
    GameSendServerInfo = 0x0502,     // NET_CHAT_GS_STATUS
    GameAnnounceMatch = 0x0503,      // NET_CHAT_GS_ANNOUNCE_MATCH
    GameAbandonMatch = 0x0504,       // NET_CHAT_GS_ABANDON_MATCH
    GameMatchStarted = 0x0505,       // NET_CHAT_GS_MATCH_STARTED
    GameRemindPlayer = 0x0506,       // NET_CHAT_GS_REMIND_PLAYER
    GameNotIdle = 0x0508,            // NET_CHAT_GS_NOT_IDLE
    GameMatchAborted = 0x0509,       // NET_CHAT_GS_MATCH_ABORTED
    GamePlayerConnection = 0x0513,   // NET_CHAT_GS_CLIENT_AUTH_RESULT
    GameStatSubmission = 0x0514,     // NET_CHAT_GS_STAT_SUBMISSION_RESULT
    GameMatchEnded = 0x0515,         // NET_CHAT_GS_MATCH_ENDED
    GameMatchOngoing = 0x0516,       // NET_CHAT_GS_MATCH_ONGOING
    
    // Chat Server to Game Server (0x1500 range) - ChatServerToGameServer in NEXUS
    ChatLogonResponse = 0x1500,      // NET_CHAT_GS_ACCEPT
    ChatLogonRejected = 0x1501,      // NET_CHAT_GS_REJECT
    ChatCreateMatch = 0x1502,        // NET_CHAT_GS_CREATE_MATCH
    ChatEndMatch = 0x1503,           // NET_CHAT_GS_END_MATCH
    ChatRemoteCommand = 0x1504,      // NET_CHAT_GS_REMOTE_COMMAND
    ChatOptions = 0x1505,            // NET_CHAT_GS_OPTIONS
    
    // Bidirectional
    GameSendHeartbeat = 0x2A00,      // NET_CHAT_PING
    ChatHeartbeatReceived = 0x2A01   // NET_CHAT_PONG
}

/// <summary>
/// Player info extracted from server status packets
/// </summary>
public record PlayerInfo
{
    public int AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int MinPing { get; init; }
    public int AvgPing { get; init; }
    public int MaxPing { get; init; }
}

/// <summary>
/// Server status data extracted from status packets
/// </summary>
public record ServerStatusData
{
    public byte Status { get; init; }
    public uint Uptime { get; init; }
    public double CpuCoreUtil { get; init; }
    public int NumClients { get; init; }
    public bool MatchStarted { get; init; }
    public int GamePhase { get; init; }
    public List<PlayerInfo> Players { get; init; } = new();
}

/// <summary>
/// Lobby info extracted from lobby created packets
/// </summary>
public record LobbyInfo
{
    public int MatchId { get; init; }
    public string Map { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
}

/// <summary>
/// Replay request data from chat server
/// </summary>
public record ReplayRequestData
{
    public int AccountId { get; init; }
    public int MatchId { get; init; }
    public string Extension { get; init; } = string.Empty;
    public string FileHost { get; init; } = string.Empty;
    public string Directory { get; init; } = string.Empty;
    public byte UploadToFtb { get; init; }
    public byte UploadToS3 { get; init; }
    public string DownloadLink { get; init; } = string.Empty;
}

/// <summary>
/// Binary packet reader utilities
/// </summary>
public static class PacketReader
{
    public static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BitConverter.ToInt32(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BitConverter.ToUInt32(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    public static short ReadInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BitConverter.ToInt16(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BitConverter.ToUInt16(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    public static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = data[offset];
        offset += 1;
        return value;
    }

    public static string ReadNullTerminatedString(ReadOnlySpan<byte> data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }
        var str = Encoding.UTF8.GetString(data.Slice(start, offset - start));
        if (offset < data.Length) offset++; // Skip null terminator
        return str;
    }
}

/// <summary>
/// Binary packet writer utilities
/// </summary>
public static class PacketWriter
{
    public static void WriteInt32(List<byte> buffer, int value)
    {
        buffer.AddRange(BitConverter.GetBytes(value));
    }

    public static void WriteUInt32(List<byte> buffer, uint value)
    {
        buffer.AddRange(BitConverter.GetBytes(value));
    }

    public static void WriteInt16(List<byte> buffer, short value)
    {
        buffer.AddRange(BitConverter.GetBytes(value));
    }

    public static void WriteUInt16(List<byte> buffer, ushort value)
    {
        buffer.AddRange(BitConverter.GetBytes(value));
    }

    public static void WriteByte(List<byte> buffer, byte value)
    {
        buffer.Add(value);
    }

    public static void WriteNullTerminatedString(List<byte> buffer, string value)
    {
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0); // Null terminator
    }

    public static byte[] BuildPacket(ushort packetType, byte[] data)
    {
        var packet = new List<byte>();
        var totalLen = (ushort)(data.Length + 2); // +2 for packet type
        WriteUInt16(packet, totalLen);
        WriteUInt16(packet, packetType);
        packet.AddRange(data);
        return packet.ToArray();
    }
}

/// <summary>
/// Parser for game server packets (0x40-0x4A)
/// </summary>
public class GameServerPacketParser
{
    private readonly int _serverId;
    private readonly Action<string, string>? _logger;
    
    public event Action<int>? OnServerAnnounce;
    public event Action? OnServerClosed;
    public event Action<ServerStatusData>? OnServerStatus;
    public event Action<int>? OnLongFrame;
    public event Action<LobbyInfo>? OnLobbyCreated;
    public event Action? OnLobbyClosed;
    public event Action? OnServerConnection;
    public event Action<int>? OnCowAnnounce;
    
    public GameServerPacketParser(int serverId, Action<string, string>? logger = null)
    {
        _serverId = serverId;
        _logger = logger;
    }
    
    public void HandlePacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length == 0) return;
        
        var packetType = (GameServerPacketType)packet[0];
        
        try
        {
            switch (packetType)
            {
                case GameServerPacketType.ServerAnnounce:
                    ParseServerAnnounce(packet);
                    break;
                case GameServerPacketType.ServerClosed:
                    ParseServerClosed(packet);
                    break;
                case GameServerPacketType.ServerStatus:
                    ParseServerStatus(packet);
                    break;
                case GameServerPacketType.LongFrame:
                    ParseLongFrame(packet);
                    break;
                case GameServerPacketType.LobbyCreated:
                    ParseLobbyCreated(packet);
                    break;
                case GameServerPacketType.LobbyClosed:
                    ParseLobbyClosed(packet);
                    break;
                case GameServerPacketType.ServerConnection:
                    ParseServerConnection(packet);
                    break;
                case GameServerPacketType.CowAnnounce:
                    ParseCowAnnounce(packet);
                    break;
                default:
                    Log("debug", string.Format("Server #{0} - Unhandled packet type: 0x{1:X2}", _serverId, (byte)packetType));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("error", string.Format("Server #{0} - Error parsing packet 0x{1:X2}: {2}", _serverId, (byte)packetType, ex.Message));
        }
    }
    
    private void ParseServerAnnounce(ReadOnlySpan<byte> packet)
    {
        // 0x40: Server announce - port follows
        var port = BitConverter.ToInt32(packet.Slice(1, 4));
        Log("debug", string.Format("Server #{0} - Announced on port {1}", _serverId, port));
        OnServerAnnounce?.Invoke(port);
    }
    
    private void ParseServerClosed(ReadOnlySpan<byte> packet)
    {
        // 0x41: Server closed
        Log("debug", string.Format("Server #{0} - Closed", _serverId));
        OnServerClosed?.Invoke();
    }
    
    private void ParseServerStatus(ReadOnlySpan<byte> packet)
    {
        // 0x42: Server status - 54 bytes fixed + player data
        if (packet.Length < 54)
        {
            Log("warn", string.Format("Server #{0} - Status packet too short: {1} bytes", _serverId, packet.Length));
            return;
        }
        
        var status = new ServerStatusData
        {
            Status = packet[1],
            Uptime = BitConverter.ToUInt32(packet.Slice(2, 4)),
            CpuCoreUtil = BitConverter.ToUInt32(packet.Slice(6, 4)) / 100.0,
            NumClients = packet[10],
            MatchStarted = packet[11] != 0,
            GamePhase = packet[40]
        };
        
        // Parse players if present (packet > 54 bytes)
        var players = new List<PlayerInfo>();
        if (packet.Length > 54)
        {
            var data = packet.Slice(53).ToArray();
            var numPlayers = data[0];
            
            // Find IP addresses using regex pattern
            var ipPattern = new Regex(@"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
            var dataStr = Encoding.Latin1.GetString(data);
            var matches = ipPattern.Matches(dataStr);
            
            int cursor = 1;
            foreach (Match match in matches)
            {
                if (cursor + 4 > data.Length) break;
                
                var accountId = BitConverter.ToInt32(data, cursor - 4 + match.Index);
                
                // Parse null-terminated strings
                int offset = match.Index;
                var ip = ReadNullString(data, ref offset);
                var name = ReadNullString(data, ref offset);
                var location = ReadNullString(data, ref offset);
                
                // Parse ping stats
                var minPing = (offset + 2 <= data.Length) ? BitConverter.ToUInt16(data, offset) : 0;
                var avgPing = (offset + 4 <= data.Length) ? BitConverter.ToUInt16(data, offset + 2) : 0;
                var maxPing = (offset + 6 <= data.Length) ? BitConverter.ToUInt16(data, offset + 4) : 0;
                
                players.Add(new PlayerInfo
                {
                    AccountId = accountId,
                    Name = name,
                    Location = location,
                    IpAddress = ip,
                    MinPing = minPing,
                    AvgPing = avgPing,
                    MaxPing = maxPing
                });
            }
        }
        
        var statusWithPlayers = status with { Players = players };
        OnServerStatus?.Invoke(statusWithPlayers);
    }
    
    private void ParseLongFrame(ReadOnlySpan<byte> packet)
    {
        // 0x43: Long frame - skipped frame time in ms
        var skippedMs = BitConverter.ToUInt16(packet.Slice(1, 2));
        Log("debug", string.Format("Server #{0} - Skipped frame: {1}ms", _serverId, skippedMs));
        OnLongFrame?.Invoke(skippedMs);
    }
    
    private void ParseLobbyCreated(ReadOnlySpan<byte> packet)
    {
        // 0x44: Lobby created
        var matchId = BitConverter.ToInt32(packet.Slice(1, 4));
        
        var data = packet.Slice(6).ToArray();
        var strings = new List<string>();
        int offset = 0;
        
        while (offset < data.Length && strings.Count < 3)
        {
            var str = ReadNullString(data, ref offset);
            strings.Add(str);
        }
        
        var lobby = new LobbyInfo
        {
            MatchId = matchId,
            Map = strings.Count > 0 ? strings[0] : "",
            Name = strings.Count > 1 ? strings[1] : "",
            Mode = strings.Count > 2 ? strings[2] : ""
        };
        
        Log("debug", string.Format("Server #{0} - Lobby created: Match {1}, Map {2}", _serverId, matchId, lobby.Map));
        OnLobbyCreated?.Invoke(lobby);
    }
    
    private void ParseLobbyClosed(ReadOnlySpan<byte> packet)
    {
        // 0x45: Lobby closed
        Log("debug", string.Format("Server #{0} - Lobby closed", _serverId));
        OnLobbyClosed?.Invoke();
    }
    
    private void ParseServerConnection(ReadOnlySpan<byte> packet)
    {
        // 0x47: Player connecting
        Log("debug", string.Format("Server #{0} - Player connection", _serverId));
        OnServerConnection?.Invoke();
    }
    
    private void ParseCowAnnounce(ReadOnlySpan<byte> packet)
    {
        // 0x49: Fork status response
        var port = BitConverter.ToUInt16(packet.Slice(1, 2));
        Log("debug", string.Format("Server #{0} - Cow announce on port {1}", _serverId, port));
        OnCowAnnounce?.Invoke(port);
    }
    
    private static string ReadNullString(byte[] data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }
        var str = Encoding.UTF8.GetString(data, start, offset - start);
        if (offset < data.Length) offset++;
        return str;
    }
    
    private void Log(string level, string message)
    {
        _logger?.Invoke(level, message);
    }
}

/// <summary>
/// Parser for manager-chat server packets
/// </summary>
public class ManagerChatPacketParser
{
    private readonly Action<string, string>? _logger;
    
    public event Action? OnHandshakeAccepted;
    public event Action<string>? OnHandshakeRejected;
    public event Action<ReplayRequestData>? OnReplayRequest;
    public event Action? OnShutdownNotice;
    public event Action? OnHeartbeatReceived;
    public event Action<string>? OnRemoteCommand;
    public event Action<Dictionary<string, string>>? OnOptionsReceived;
    
    public ManagerChatPacketParser(Action<string, string>? logger = null)
    {
        _logger = logger;
    }
    
    public async Task HandlePacketAsync(ushort packetType, ReadOnlyMemory<byte> packetData, string direction)
    {
        var prefix = direction == "sending" ? ">>> [MGR|CHATSV]" : "<<< [MGR|CHATSV]";
        
        try
        {
            switch ((ManagerChatPacketType)packetType)
            {
                case ManagerChatPacketType.ChatHandshakeAccepted:
                    // NET_CHAT_SM_ACCEPT (0x1700)
                    Log("debug", string.Format("{0} - [NET_CHAT_SM_ACCEPT] Handshake accepted", prefix));
                    OnHandshakeAccepted?.Invoke();
                    break;
                    
                case ManagerChatPacketType.ChatHandshakeRejected:
                    // NET_CHAT_SM_REJECT (0x1701)
                    var rejectReason = ParseRejectReason(packetData.Span);
                    Log("warn", string.Format("{0} - [NET_CHAT_SM_REJECT] Handshake rejected: {1}", prefix, rejectReason));
                    OnHandshakeRejected?.Invoke(rejectReason);
                    break;
                    
                case ManagerChatPacketType.ChatRemoteCommand:
                    // NET_CHAT_SM_REMOTE_COMMAND (0x1702)
                    var command = ParseRemoteCommand(packetData.Span);
                    Log("debug", string.Format("{0} - [NET_CHAT_SM_REMOTE_COMMAND] Command: {1}", prefix, command));
                    OnRemoteCommand?.Invoke(command);
                    break;
                    
                case ManagerChatPacketType.ChatPolicies:
                    // NET_CHAT_SM_OPTIONS (0x1703)
                    var options = ParseOptions(packetData.Span);
                    Log("debug", string.Format("{0} - [NET_CHAT_SM_OPTIONS] Received {1} options", prefix, options.Count));
                    OnOptionsReceived?.Invoke(options);
                    break;
                    
                case ManagerChatPacketType.ChatReplayRequest:
                    // NET_CHAT_SM_UPLOAD_REQUEST (0x1704)
                    var replayRequest = ParseReplayRequest(packetData.Span);
                    Log("debug", string.Format("{0} - [NET_CHAT_SM_UPLOAD_REQUEST] Replay request: Match {1}", prefix, replayRequest.MatchId));
                    OnReplayRequest?.Invoke(replayRequest);
                    break;
                    
                case ManagerChatPacketType.ChatShutdownNotice:
                    Log("debug", string.Format("{0} - Shutdown notice", prefix));
                    OnShutdownNotice?.Invoke();
                    break;
                    
                case ManagerChatPacketType.ChatHeartbeatReceived:
                    // NET_CHAT_PONG (0x2A01)
                    Log("debug", string.Format("{0} - [NET_CHAT_PONG] Heartbeat received", prefix));
                    OnHeartbeatReceived?.Invoke();
                    break;
                    
                default:
                    Log("warn", string.Format("{0} - Unhandled packet: 0x{1:X4}", prefix, packetType));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("error", string.Format("{0} - Error: {1}", prefix, ex.Message));
        }
    }
    
    private string ParseRejectReason(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "Unknown";
        
        var reader = new ChatBufferReader(data);
        return reader.HasMore ? reader.ReadString() : "Unknown";
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
            // Options parsing can be variable, ignore errors
        }
        
        return options;
    }
    
    private ReplayRequestData ParseReplayRequest(ReadOnlySpan<byte> data)
    {
        var accountId = BitConverter.ToInt32(data.Slice(2, 4));
        var matchId = BitConverter.ToInt32(data.Slice(6, 4));
        
        var remaining = data.Slice(10).ToArray();
        int offset = 0;
        
        var extension = ReadNullString(remaining, ref offset);
        var fileHost = ReadNullString(remaining, ref offset);
        var directory = ReadNullString(remaining, ref offset);
        var uploadToFtb = offset < remaining.Length ? remaining[offset++] : (byte)0;
        var uploadToS3 = offset < remaining.Length ? remaining[offset++] : (byte)0;
        var downloadLink = ReadNullString(remaining, ref offset);
        
        return new ReplayRequestData
        {
            AccountId = accountId,
            MatchId = matchId,
            Extension = extension,
            FileHost = fileHost,
            Directory = directory,
            UploadToFtb = uploadToFtb,
            UploadToS3 = uploadToS3,
            DownloadLink = downloadLink
        };
    }
    
    private static string ReadNullString(byte[] data, ref int offset)
    {
        var start = offset;
        while (offset < data.Length && data[offset] != 0)
        {
            offset++;
        }
        var str = Encoding.UTF8.GetString(data, start, offset - start);
        if (offset < data.Length) offset++;
        return str;
    }
    
    private void Log(string level, string message)
    {
        _logger?.Invoke(level, message);
    }
}
