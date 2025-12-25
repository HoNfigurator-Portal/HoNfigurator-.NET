using System.Text.Json.Serialization;

namespace HoNfigurator.Core.Models;

/// <summary>
/// Manager to Game Server command definitions
/// These commands are sent from the manager to control the game server
/// </summary>
public static class GameServerCommands
{
    // Length prefix for single-byte commands (1 byte)
    public static readonly byte[] CommandLengthBytes = new byte[] { 0x01, 0x00 };
    
    // Command bytes - match Python HoNfigurator-Central
    public static readonly byte[] ShutdownBytes = new byte[] { 0x22 };      // '"' - Graceful shutdown
    public static readonly byte[] RestartBytes = new byte[] { 0x23 };       // '#' - Restart
    public static readonly byte[] SleepBytes = new byte[] { 0x20 };         // ' ' - Sleep (low CPU mode)
    public static readonly byte[] WakeBytes = new byte[] { 0x21 };          // '!' - Wake from sleep
    public static readonly byte[] MessagePrefixByte = new byte[] { 0x24 };  // '$' - Message prefix
    public static readonly byte[] CommandPrefixByte = new byte[] { 0x25 };  // '%' - Console command prefix
}

public class PlayerInfo
{
    [JsonPropertyName("account_id")]
    public int AccountId { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("slot")]
    public int Slot { get; set; } = -1;
    
    [JsonPropertyName("psr")]
    public double? Psr { get; set; }
    
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;
    
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;
    
    [JsonPropertyName("minping")]
    public int MinPing { get; set; }
    
    [JsonPropertyName("avgping")]
    public int AvgPing { get; set; }
    
    [JsonPropertyName("maxping")]
    public int MaxPing { get; set; }
}

public enum ServerStatus
{
    Offline,
    Starting,
    Ready,
    Occupied,
    Idle,
    Crashed,
    Unknown
}

public class GameServerInstance
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("status")]
    public string StatusString => Status.ToString().ToUpper();
    
    [JsonIgnore]
    public ServerStatus Status { get; set; } = ServerStatus.Offline;
    
    [JsonPropertyName("port")]
    public int Port { get; set; }
    
    [JsonPropertyName("voice_port")]
    public int VoicePort { get; set; }
    
    [JsonPropertyName("num_clients")]
    public int NumClients { get; set; }
    
    [JsonPropertyName("max_clients")]
    public int MaxClients { get; set; } = 10;
    
    [JsonPropertyName("match_id")]
    public string? MatchId { get; set; }
    
    [JsonPropertyName("start_time")]
    public DateTime? StartTime { get; set; }
    
    [JsonPropertyName("uptime")]
    public double Uptime => StartTime.HasValue 
        ? (DateTime.UtcNow - StartTime.Value).TotalSeconds 
        : 0;
    
    [JsonPropertyName("cpu_percent")]
    public double CpuPercent { get; set; }
    
    [JsonPropertyName("memory_mb")]
    public double MemoryMb { get; set; }
    
    [JsonPropertyName("pid")]
    public int? ProcessId { get; set; }
    
    [JsonPropertyName("game_phase")]
    public string GamePhase { get; set; } = string.Empty;
    
    [JsonPropertyName("players")]
    public List<PlayerInfo> Players { get; set; } = new();
    
    [JsonPropertyName("players_by_team")]
    public PlayersByTeam PlayersByTeam { get; set; } = new();
    
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    
    [JsonPropertyName("scheduled_shutdown")]
    public bool ScheduledShutdown { get; set; }
    
    [JsonPropertyName("public_port")]
    public int PublicPort { get; set; }
    
    [JsonPropertyName("proxy_enabled")]
    public bool ProxyEnabled { get; set; }
}

public class ServerStatusResponse
{
    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
    
    [JsonPropertyName("master_server_connected")]
    public bool MasterServerConnected { get; set; }
    
    [JsonPropertyName("chat_server_connected")]
    public bool ChatServerConnected { get; set; }
    
    [JsonPropertyName("total_servers")]
    public int TotalServers { get; set; }
    
    [JsonPropertyName("online_servers")]
    public int OnlineServers { get; set; }
    
    [JsonPropertyName("total_players")]
    public int TotalPlayers { get; set; }
    
    [JsonPropertyName("instances")]
    public List<GameServerInstance> Instances { get; set; } = new();
    
    [JsonPropertyName("system_stats")]
    public SystemStats SystemStats { get; set; } = new();
}

public class PlayersByTeam
{
    [JsonPropertyName("legion")]
    public List<PlayerInfo> Legion { get; set; } = new();
    
    [JsonPropertyName("hellbourne")]
    public List<PlayerInfo> Hellbourne { get; set; } = new();
    
    [JsonPropertyName("spectators")]
    public List<PlayerInfo> Spectators { get; set; } = new();
}

public class SystemStats
{
    [JsonPropertyName("cpu_percent")]
    public double CpuUsagePercent { get; set; }
    
    [JsonPropertyName("memory_percent")]
    public double MemoryUsagePercent { get; set; }
    
    [JsonPropertyName("total_memory_mb")]
    public long TotalMemoryMb { get; set; }
    
    [JsonPropertyName("used_memory_mb")]
    public long UsedMemoryMb { get; set; }
    
    [JsonPropertyName("disk_percent")]
    public double DiskUsagePercent { get; set; }
    
    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = string.Empty;
    
    [JsonPropertyName("hon_process_count")]
    public int HonProcessCount { get; set; }
    
    [JsonPropertyName("hon_total_memory_mb")]
    public long HonTotalMemoryMb { get; set; }
}
