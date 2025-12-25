using System.Text.Json.Serialization;

namespace HoNfigurator.Core.Models;

public class HoNConfiguration
{
    [JsonPropertyName("hon_data")]
    public HoNData HonData { get; set; } = new();

    [JsonPropertyName("application_data")]
    public ApplicationData ApplicationData { get; set; } = new();

    // Convenience properties for easier access
    [JsonIgnore]
    public string? SvrLogin => HonData.Login;
    
    [JsonIgnore]
    public string? SvrPassword => HonData.Password;
    
    [JsonIgnore]
    public string? SvrName => HonData.ServerName;
    
    [JsonIgnore]
    public string? SvrLocation => HonData.Location;
    
    [JsonIgnore]
    public string? LocalIp => HonData.LocalIp;
    
    [JsonIgnore]
    public string? ManVersion => HonData.ManVersion;
    
    [JsonIgnore]
    public int? AutoPingRespPort => HonData.AutoPingRespPort;
    
    [JsonIgnore]
    public string? MasterServerUrl => $"http://{HonData.MasterServer}";
}

public class HoNData
{
    [JsonPropertyName("hon_install_directory")]
    public string HonInstallDirectory { get; set; } = string.Empty;

    [JsonPropertyName("hon_home_directory")]
    public string HonHomeDirectory { get; set; } = string.Empty;

    [JsonPropertyName("hon_logs_directory")]
    public string HonLogsDirectory { get; set; } = string.Empty;

    [JsonPropertyName("svr_masterServer")]
    public string MasterServer { get; set; } = "api.kongor.net";

    [JsonPropertyName("svr_chatServer")]
    public string ChatServer { get; set; } = "chat.kongor.net";

    [JsonPropertyName("svr_patchServer")]
    public string PatchServer { get; set; } = "api.kongor.net/patches";

    [JsonPropertyName("svr_login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("svr_password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("svr_name")]
    public string ServerName { get; set; } = "Unknown";

    [JsonPropertyName("svr_override_suffix")]
    public bool OverrideSuffix { get; set; }

    [JsonPropertyName("svr_suffix")]
    public string Suffix { get; set; } = "auto";

    [JsonPropertyName("svr_override_state")]
    public bool OverrideState { get; set; }

    [JsonPropertyName("svr_state")]
    public string State { get; set; } = "auto";

    [JsonPropertyName("svr_location")]
    public string Location { get; set; } = "US";

    [JsonPropertyName("svr_priority")]
    public string Priority { get; set; } = "HIGH";

    [JsonPropertyName("svr_total")]
    public int TotalServers { get; set; }

    [JsonPropertyName("svr_total_per_core")]
    public double TotalPerCore { get; set; } = 1.0;

    [JsonPropertyName("man_enableProxy")]
    public bool EnableProxy { get; set; } = true;

    [JsonPropertyName("svr_noConsole")]
    public bool NoConsole { get; set; } = true;

    [JsonPropertyName("svr_enableBotMatch")]
    public bool EnableBotMatch { get; set; } = true;

    [JsonPropertyName("svr_start_on_launch")]
    public bool StartOnLaunch { get; set; }

    [JsonPropertyName("svr_override_affinity")]
    public bool OverrideAffinity { get; set; }

    [JsonPropertyName("svr_max_start_at_once")]
    public int MaxStartAtOnce { get; set; } = 5;

    [JsonPropertyName("svr_starting_gamePort")]
    public int StartingGamePort { get; set; } = 10001;

    [JsonPropertyName("svr_starting_voicePort")]
    public int StartingVoicePort { get; set; } = 10061;

    [JsonPropertyName("svr_managerPort")]
    public int ManagerPort { get; set; } = 11235;

    [JsonPropertyName("svr_startup_timeout")]
    public int StartupTimeout { get; set; } = 180;

    [JsonPropertyName("svr_api_port")]
    public int ApiPort { get; set; } = 5050;

    [JsonPropertyName("man_use_cowmaster")]
    public bool UseCowmaster { get; set; }

    [JsonPropertyName("svr_restart_between_games")]
    public bool RestartBetweenGames { get; set; }

    [JsonPropertyName("svr_beta_mode")]
    public bool BetaMode { get; set; }

    // Network settings
    [JsonPropertyName("local_ip")]
    public string? LocalIp { get; set; }

    [JsonPropertyName("man_version")]
    public string ManVersion { get; set; } = "4.10.1";

    [JsonPropertyName("auto_ping_resp_port")]
    public int AutoPingRespPort { get; set; } = 10069;

    // Additional settings from Python version
    [JsonPropertyName("svr_ip")]
    public string? ServerIp { get; set; }

    [JsonPropertyName("svr_proxyLocalAddr")]
    public string? ProxyLocalAddr { get; set; }

    [JsonPropertyName("svr_proxyRemoteAddr")]
    public string? ProxyRemoteAddr { get; set; }

    [JsonPropertyName("svr_proxyPort")]
    public int ProxyPort { get; set; } = 1135;
}

public class ApplicationData
{
    [JsonPropertyName("timers")]
    public TimerSettings Timers { get; set; } = new();

    [JsonPropertyName("discord")]
    public DiscordSettings? Discord { get; set; }

    [JsonPropertyName("mqtt")]
    public MqttSettings? Mqtt { get; set; }
}

public class MqttSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 1883;

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("topic_prefix")]
    public string TopicPrefix { get; set; } = "honfigurator";

    [JsonPropertyName("use_tls")]
    public bool UseTls { get; set; } = false;
}

public class DiscordSettings
{
    [JsonPropertyName("owner_id")]
    public string OwnerId { get; set; } = "";

    [JsonPropertyName("bot_token")]
    public string BotToken { get; set; } = "";

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; set; } = true;
}

public class TimerSettings
{
    [JsonPropertyName("manager")]
    public ManagerTimers Manager { get; set; } = new();

    [JsonPropertyName("replay_cleaner")]
    public ReplayCleanerSettings ReplayCleaner { get; set; } = new();
}

public class ManagerTimers
{
    [JsonPropertyName("public_ip_healthcheck")]
    public int PublicIpHealthcheck { get; set; } = 1800;

    [JsonPropertyName("general_healthcheck")]
    public int GeneralHealthcheck { get; set; } = 60;

    [JsonPropertyName("lag_healthcheck")]
    public int LagHealthcheck { get; set; } = 120;

    [JsonPropertyName("check_for_hon_update")]
    public int CheckForHonUpdate { get; set; } = 120;
}

public class ReplayCleanerSettings
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("max_replay_age_days")]
    public int MaxReplayAgeDays { get; set; }

    [JsonPropertyName("max_temp_files_age_days")]
    public int MaxTempFilesAgeDays { get; set; } = 1;
}
