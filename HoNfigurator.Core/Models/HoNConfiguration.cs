using System.Text.Json.Serialization;

namespace HoNfigurator.Core.Models;

public class HoNConfiguration
{
    [JsonPropertyName("hon_data")]
    public HoNData HonData { get; set; } = new();

    [JsonPropertyName("application_data")]
    public ApplicationData ApplicationData { get; set; } = new();

    [JsonPropertyName("health_monitoring")]
    public HealthMonitoringConfiguration? HealthMonitoring { get; set; }

    [JsonPropertyName("server_lifecycle")]
    public ServerLifecycleConfiguration? ServerLifecycle { get; set; }

    [JsonPropertyName("cpu_affinity")]
    public CpuAffinityConfiguration? CpuAffinity { get; set; }

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

    [JsonPropertyName("auto_scaling")]
    public AutoScalingSettings? AutoScaling { get; set; }

    [JsonPropertyName("replay_upload")]
    public ReplayUploadSettings? ReplayUpload { get; set; }

    [JsonPropertyName("storage")]
    public StorageConfiguration? Storage { get; set; }

    [JsonPropertyName("filebeat")]
    public FilebeatConfiguration? Filebeat { get; set; }

    [JsonPropertyName("github")]
    public GitHubConfiguration? GitHub { get; set; }

    [JsonPropertyName("certificates")]
    public CertificatesConfiguration? Certificates { get; set; }

    [JsonPropertyName("disk_monitoring")]
    public DiskMonitoringConfiguration? DiskMonitoring { get; set; }
}

public class StorageConfiguration
{
    [JsonPropertyName("primary_path")]
    public string PrimaryPath { get; set; } = "replays";

    [JsonPropertyName("archive_path")]
    public string? ArchivePath { get; set; }

    [JsonPropertyName("logs_path")]
    public string LogsPath { get; set; } = "logs";

    [JsonPropertyName("archive_after_days")]
    public int ArchiveAfterDays { get; set; } = 7;

    [JsonPropertyName("retention_days")]
    public int RetentionDays { get; set; } = 90;

    [JsonPropertyName("auto_relocate")]
    public bool AutoRelocate { get; set; } = false;

    [JsonPropertyName("auto_cleanup")]
    public bool AutoCleanup { get; set; } = false;
}

public class FilebeatConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("install_path")]
    public string InstallPath { get; set; } = "C:\\Program Files\\Filebeat";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "8.11.0";

    [JsonPropertyName("elasticsearch_host")]
    public string ElasticsearchHost { get; set; } = "localhost:9200";

    [JsonPropertyName("elasticsearch_username")]
    public string? ElasticsearchUsername { get; set; }

    [JsonPropertyName("elasticsearch_password")]
    public string? ElasticsearchPassword { get; set; }

    [JsonPropertyName("log_paths")]
    public List<string> LogPaths { get; set; } = new() { "logs/*.log" };

    [JsonPropertyName("index_prefix")]
    public string IndexPrefix { get; set; } = "honfigurator";

    [JsonPropertyName("auto_start")]
    public bool AutoStart { get; set; } = true;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = "production";
}

public class GitHubConfiguration
{
    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "HoNfigurator/HoNfigurator-dotnet";

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("auto_update")]
    public bool AutoUpdate { get; set; } = false;

    [JsonPropertyName("check_updates_on_startup")]
    public bool CheckUpdatesOnStartup { get; set; } = true;

    [JsonPropertyName("preferred_branch")]
    public string PreferredBranch { get; set; } = "main";
}

public class ReplayUploadSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "local";

    [JsonPropertyName("connection_string")]
    public string ConnectionString { get; set; } = "";

    [JsonPropertyName("container_name")]
    public string ContainerName { get; set; } = "replays";

    [JsonPropertyName("base_path")]
    public string BasePath { get; set; } = "";

    [JsonPropertyName("auto_upload_on_match_end")]
    public bool AutoUploadOnMatchEnd { get; set; } = true;

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; } = 3;

    [JsonPropertyName("retry_delay_seconds")]
    public int RetryDelaySeconds { get; set; } = 5;

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = "";
}

public class AutoScalingSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("min_servers")]
    public int MinServers { get; set; } = 1;

    [JsonPropertyName("max_servers")]
    public int MaxServers { get; set; } = 10;

    [JsonPropertyName("scale_up_threshold")]
    public int ScaleUpThreshold { get; set; } = 80;

    [JsonPropertyName("scale_down_threshold")]
    public int ScaleDownThreshold { get; set; } = 20;

    [JsonPropertyName("cooldown_seconds")]
    public int CooldownSeconds { get; set; } = 300;

    [JsonPropertyName("check_interval_seconds")]
    public int CheckIntervalSeconds { get; set; } = 60;

    [JsonPropertyName("min_ready_servers")]
    public int MinReadyServers { get; set; } = 1;
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

    [JsonPropertyName("notification_channel_id")]
    public string NotificationChannelId { get; set; } = "";

    [JsonPropertyName("enable_notifications")]
    public bool EnableNotifications { get; set; } = true;

    [JsonPropertyName("notify_match_start")]
    public bool NotifyMatchStart { get; set; } = true;

    [JsonPropertyName("notify_match_end")]
    public bool NotifyMatchEnd { get; set; } = true;

    [JsonPropertyName("notify_player_join_leave")]
    public bool NotifyPlayerJoinLeave { get; set; } = false;
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

public class CertificatesConfiguration
{
    [JsonPropertyName("base_path")]
    public string BasePath { get; set; } = "certs";

    [JsonPropertyName("step_cli_path")]
    public string? StepCliPath { get; set; }

    [JsonPropertyName("default_name")]
    public string DefaultName { get; set; } = "server";

    [JsonPropertyName("ca_url")]
    public string? CaUrl { get; set; }

    [JsonPropertyName("ca_fingerprint")]
    public string? CaFingerprint { get; set; }

    [JsonPropertyName("auto_renew")]
    public bool AutoRenew { get; set; } = true;

    [JsonPropertyName("renew_threshold_days")]
    public int RenewThresholdDays { get; set; } = 30;
}

public class DiskMonitoringConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("warning_threshold")]
    public int WarningThreshold { get; set; } = 80;

    [JsonPropertyName("critical_threshold")]
    public int CriticalThreshold { get; set; } = 95;

    [JsonPropertyName("check_interval_minutes")]
    public int CheckIntervalMinutes { get; set; } = 15;

    [JsonPropertyName("monitored_paths")]
    public List<MonitoredPath> MonitoredPaths { get; set; } = new();

    [JsonPropertyName("alert_cooldown_minutes")]
    public int AlertCooldownMinutes { get; set; } = 60;
}

public class MonitoredPath
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("max_size_gb")]
    public long MaxSizeGb { get; set; }
}

public class HealthMonitoringConfiguration
{
    [JsonPropertyName("auto_ping_enabled")]
    public bool AutoPingEnabled { get; set; } = true;

    [JsonPropertyName("auto_ping_interval_ms")]
    public int AutoPingIntervalMs { get; set; } = 30000;

    [JsonPropertyName("max_consecutive_failures")]
    public int MaxConsecutiveFailures { get; set; } = 3;

    [JsonPropertyName("auto_restart_on_unhealthy")]
    public bool AutoRestartOnUnhealthy { get; set; } = true;

    [JsonPropertyName("ping_timeout_ms")]
    public int PingTimeoutMs { get; set; } = 5000;
}

public class ServerLifecycleConfiguration
{
    [JsonPropertyName("periodic_restart_enabled")]
    public bool PeriodicRestartEnabled { get; set; } = true;

    [JsonPropertyName("min_uptime_hours")]
    public int MinUptimeHours { get; set; } = 24;

    [JsonPropertyName("max_uptime_hours")]
    public int MaxUptimeHours { get; set; } = 48;

    [JsonPropertyName("check_interval_minutes")]
    public int CheckIntervalMinutes { get; set; } = 5;

    [JsonPropertyName("max_wait_for_game_minutes")]
    public int MaxWaitForGameMinutes { get; set; } = 60;

    [JsonPropertyName("restart_window_start_hour")]
    public int? RestartWindowStartHour { get; set; }

    [JsonPropertyName("restart_window_end_hour")]
    public int? RestartWindowEndHour { get; set; }
}

public class CpuAffinityConfiguration
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("cores_per_server")]
    public int CoresPerServer { get; set; } = 1;

    [JsonPropertyName("start_core")]
    public int StartCore { get; set; } = 0;

    [JsonPropertyName("reserved_cores")]
    public int ReservedCores { get; set; } = 2;

    [JsonPropertyName("auto_assign_on_start")]
    public bool AutoAssignOnStart { get; set; } = true;

    [JsonPropertyName("set_priority")]
    public bool SetPriority { get; set; } = false;

    [JsonPropertyName("priority_level")]
    public string PriorityLevel { get; set; } = "Normal";
}
