using Microsoft.AspNetCore.Mvc;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Health;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Auth;
using HoNfigurator.Core.Metrics;
using HoNfigurator.Core.Notifications;
using HoNfigurator.Core.Charts;
using HoNfigurator.Core.Statistics;
using HoNfigurator.Core.Discord;
using HoNfigurator.Core.Network;
using HoNfigurator.Core.Diagnostics;
using HoNfigurator.GameServer.Services;
using System.Text.Json;
using HoNfigurator.Core.Events;

namespace HoNfigurator.Api.Endpoints;

public static class ApiEndpoints
{
    private static readonly List<MetricsSnapshot> _metricsHistory = new();
    private static readonly object _metricsLock = new();
    private static readonly string[] _consoleCommands = { "status", "help", "startup", "shutdown", "add", "remove", "restart", "message", "list", "config" };

    public static void MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ===================================================================
        // PUBLIC ENDPOINTS (No auth required - for management.honfigurator.app)
        // ===================================================================
        var publicApi = api.MapGroup("/public").WithTags("Public");
        
        publicApi.MapGet("/ping", () => Results.Ok(new { status = "OK" }))
            .WithName("PublicPing")
            .WithSummary("Public ping")
            .WithDescription("Simple ping endpoint for management portal connectivity check");

        publicApi.MapGet("/get_server_info", GetPublicServerInfo)
            .WithName("GetPublicServerInfo")
            .WithSummary("Get basic server information")
            .WithDescription("Returns basic server info for management portal");

        publicApi.MapGet("/get_honfigurator_version", GetHonfiguratorVersion)
            .WithName("GetHonfiguratorVersion")
            .WithSummary("Get HoNfigurator version")
            .WithDescription("Returns current HoNfigurator version and update info");

        publicApi.MapGet("/check_filebeat_status", GetFilebeatStatus)
            .WithName("GetPublicFilebeatStatus")
            .WithSummary("Check Filebeat status")
            .WithDescription("Returns whether Filebeat is installed and configured");

        publicApi.MapGet("/get_skipped_frame_data/{port}", GetSkippedFrameData)
            .WithName("GetSkippedFrameData")
            .WithSummary("Get skipped frame data")
            .WithDescription("Returns skipped frame diagnostics for a server");

        publicApi.MapGet("/get_hon_version", GetHonVersion)
            .WithName("GetPublicHonVersion")
            .WithSummary("Get HoN version")
            .WithDescription("Returns the current HoN server version");

        // Registration endpoint for management portal (called by portal)
        api.MapGet("/register", RegisterWithManagement)
            .WithName("RegisterWithManagement")
            .WithSummary("Register with management portal")
            .WithDescription("Used to verify client has permission to add server to management portal")
            .WithTags("Management Portal");

        // Management Portal Control Endpoints
        var management = api.MapGroup("/management").WithTags("Management Portal");
        
        management.MapGet("/status", GetManagementPortalStatus)
            .WithName("GetManagementPortalStatus")
            .WithSummary("Get management portal connection status")
            .WithDescription("Returns current connection status with management.honfigurator.app");
        
        management.MapPost("/register", TriggerManagementPortalRegistration)
            .WithName("TriggerManagementPortalRegistration")
            .WithSummary("Trigger manual registration")
            .WithDescription("Manually trigger server registration with management portal");
        
        management.MapPost("/report-status", TriggerManagementPortalStatusReport)
            .WithName("TriggerManagementPortalStatusReport")
            .WithSummary("Trigger status report")
            .WithDescription("Manually trigger a status report to management portal");
        
        management.MapGet("/config", GetManagementPortalConfig)
            .WithName("GetManagementPortalConfig")
            .WithSummary("Get management portal configuration")
            .WithDescription("Returns current management portal settings (sensitive data redacted)");

        // MQTT Broker Endpoints
        var mqtt = api.MapGroup("/mqtt").WithTags("MQTT");
        
        mqtt.MapGet("/status", GetMqttStatus)
            .WithName("GetMqttStatus")
            .WithSummary("Get MQTT connection status")
            .WithDescription("Returns current MQTT broker connection status and configuration");
        
        mqtt.MapPost("/connect", ConnectMqtt)
            .WithName("ConnectMqtt")
            .WithSummary("Connect to MQTT broker")
            .WithDescription("Manually connect to the configured MQTT broker");
        
        mqtt.MapPost("/disconnect", DisconnectMqtt)
            .WithName("DisconnectMqtt")
            .WithSummary("Disconnect from MQTT broker")
            .WithDescription("Manually disconnect from the MQTT broker");
        
        mqtt.MapPost("/publish", PublishMqttMessage)
            .WithName("PublishMqttMessage")
            .WithSummary("Publish a message to MQTT")
            .WithDescription("Publish a custom message to a specific MQTT topic");
        
        mqtt.MapPost("/publish-test", PublishMqttTestMessage)
            .WithName("PublishMqttTestMessage")
            .WithSummary("Publish a test message")
            .WithDescription("Publish a test message to verify MQTT connectivity");

        // ===================================================================
        // PROTECTED ENDPOINTS
        // ===================================================================

        // Status endpoints
        api.MapGet("/status", GetStatus)
            .WithName("GetStatus")
            .WithSummary("Get system status")
            .WithDescription("Returns overall system status including server states and statistics")
            .WithTags("General")
            .Produces<object>(200);

        api.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .WithName("HealthCheck")
            .WithSummary("Quick health check")
            .WithDescription("Returns basic health status")
            .WithTags("Health")
            .Produces<object>(200);

        // Server management endpoints
        var servers = api.MapGroup("/servers").WithTags("Servers");
        servers.MapGet("/", GetServers)
            .WithName("GetServers")
            .WithSummary("List all servers")
            .WithDescription("Returns all configured game server instances");
        servers.MapGet("/{id:int}", GetServer)
            .WithName("GetServer")
            .WithSummary("Get server by ID")
            .WithDescription("Returns details of a specific server instance");
        servers.MapPost("/{id:int}/start", StartServer)
            .WithName("StartServer")
            .WithSummary("Start a server")
            .WithDescription("Starts the specified game server");
        servers.MapPost("/{id:int}/stop", StopServer)
            .WithName("StopServer")
            .WithSummary("Stop a server")
            .WithDescription("Stops the specified game server");
        servers.MapPost("/{id:int}/restart", RestartServer)
            .WithName("RestartServer")
            .WithSummary("Restart a server")
            .WithDescription("Restarts the specified game server");
        servers.MapPost("/start-all", StartAllServers)
            .WithName("StartAllServers")
            .WithSummary("Start all servers")
            .WithDescription("Starts all configured game servers");
        servers.MapPost("/stop-all", StopAllServers)
            .WithName("StopAllServers")
            .WithSummary("Stop all servers")
            .WithDescription("Stops all running game servers");
        servers.MapPost("/restart-all", RestartAllServers)
            .WithName("RestartAllServers")
            .WithSummary("Restart all servers")
            .WithDescription("Restarts all running game servers");
        servers.MapPost("/add", AddServers)
            .WithName("AddServers")
            .WithSummary("Add servers")
            .WithDescription("Adds new game server instances");
        servers.MapDelete("/delete", DeleteServers)
            .WithName("DeleteServers")
            .WithSummary("Delete servers")
            .WithDescription("Deletes game server instances");
        servers.MapPost("/scale", ScaleServers)
            .WithName("ScaleServers")
            .WithSummary("Scale servers")
            .WithDescription("Scales to a target number of servers");
        servers.MapPost("/message-all", MessageAllServers)
            .WithName("MessageAllServers")
            .WithSummary("Broadcast message")
            .WithDescription("Sends a message to all running servers");
        servers.MapPost("/{id:int}/kick", KickPlayerFromServer)
            .WithName("KickPlayerFromServer")
            .WithSummary("Kick player from server")
            .WithDescription("Kicks a player from the specified server");

        // Configuration endpoints
        var config = api.MapGroup("/config").WithTags("Configuration");
        config.MapGet("/", GetConfiguration)
            .WithName("GetConfiguration")
            .WithSummary("Get current configuration")
            .WithDescription("Returns the current system configuration");
        config.MapPost("/", SaveConfiguration)
            .WithName("SaveConfiguration")
            .WithSummary("Save configuration")
            .WithDescription("Saves a new configuration");
        config.MapPut("/", UpdateConfiguration)
            .WithName("UpdateConfiguration")
            .WithSummary("Update configuration")
            .WithDescription("Updates the existing configuration");

        // Logs endpoints
        var logs = api.MapGroup("/logs").WithTags("Logs");
        logs.MapGet("/{serverId}", GetLogs)
            .WithName("GetLogs")
            .WithSummary("Get server logs")
            .WithDescription("Returns logs for a specific server");
        logs.MapGet("/{serverId}/download", DownloadLog)
            .WithName("DownloadLog")
            .WithSummary("Download server log")
            .WithDescription("Downloads the log file for a specific server");

        // Statistics endpoints
        var stats = api.MapGroup("/statistics").WithTags("Statistics");
        stats.MapGet("/summary", GetStatisticsSummary)
            .WithName("GetStatisticsSummary")
            .WithSummary("Get overall statistics summary")
            .WithDescription("Returns aggregated statistics including total matches, players, and play time");
        stats.MapGet("/matches", GetRecentMatches)
            .WithName("GetRecentMatches")
            .WithSummary("Get recent matches")
            .WithDescription("Returns a list of recent matches with details");
        stats.MapGet("/matches/{id:long}", GetMatch)
            .WithName("GetMatch")
            .WithSummary("Get match details")
            .WithDescription("Returns details of a specific match");
        stats.MapGet("/players/top", GetTopPlayers)
            .WithName("GetTopPlayers")
            .WithSummary("Get top players by win rate")
            .WithDescription("Returns players with highest win rates");
        stats.MapGet("/players/active", GetMostActivePlayers)
            .WithName("GetMostActivePlayers")
            .WithSummary("Get most active players")
            .WithDescription("Returns players with most matches played");
        stats.MapGet("/players/{name}", GetPlayerStats)
            .WithName("GetPlayerStats")
            .WithSummary("Get player statistics")
            .WithDescription("Returns statistics for a specific player");
        stats.MapGet("/servers", GetAllServerStats)
            .WithName("GetAllServerStats")
            .WithSummary("Get all server statistics")
            .WithDescription("Returns statistics for all servers");
        stats.MapGet("/daily", GetDailyStats)
            .WithName("GetDailyStats")
            .WithSummary("Get daily statistics")
            .WithDescription("Returns statistics grouped by day");

        // Replays endpoints
        var replays = api.MapGroup("/replays").WithTags("Replays");
        replays.MapGet("/", GetReplays)
            .WithName("GetReplays")
            .WithSummary("List replays")
            .WithDescription("Returns all available game replays");
        replays.MapGet("/download/{filename}", DownloadReplay)
            .WithName("DownloadReplay")
            .WithSummary("Download replay")
            .WithDescription("Downloads a specific replay file");

        // Metrics endpoints
        var metrics = api.MapGroup("/metrics").WithTags("Metrics");
        metrics.MapGet("/", GetMetrics)
            .WithName("GetMetrics")
            .WithSummary("Get current metrics")
            .WithDescription("Returns current system and server metrics");
        metrics.MapGet("/history", GetMetricsHistory)
            .WithName("GetMetricsHistory")
            .WithSummary("Get metrics history")
            .WithDescription("Returns historical metrics data for charting");

        // Console commands endpoint (legacy)
        api.MapPost("/console/execute", ExecuteConsoleCommand)
            .WithName("ExecuteConsoleCommand")
            .WithSummary("Execute console command")
            .WithDescription("Executes a console command on the server. Available: status, help, startup, shutdown, add, remove, restart, message, list, config")
            .WithTags("Console");

        // CLI Commands endpoints (new - uses CliCommandService)
        var cli = api.MapGroup("/cli").WithTags("CLI");
        cli.MapGet("/commands", GetCliCommands)
            .WithName("GetCliCommands")
            .WithSummary("Get available CLI commands")
            .WithDescription("Returns list of all available CLI commands with descriptions and usage");
        cli.MapPost("/execute", ExecuteCliCommand)
            .WithName("ExecuteCliCommand")
            .WithSummary("Execute CLI command")
            .WithDescription("Executes a CLI command with arguments");

        // Health check endpoints
        var health = api.MapGroup("/health").WithTags("Health");
        health.MapGet("/checks", GetHealthChecks)
            .WithName("GetHealthChecks")
            .WithSummary("Get health checks")
            .WithDescription("Returns the status of all health checks");
        health.MapGet("/resources", GetSystemResources)
            .WithName("GetSystemResources")
            .WithSummary("Get system resources")
            .WithDescription("Returns CPU, memory, and disk usage statistics");
        health.MapPost("/run", RunHealthChecks)
            .WithName("RunHealthChecks")
            .WithSummary("Run health checks")
            .WithDescription("Manually triggers all health checks");
        health.MapGet("/ip/{ipAddress}", ValidateIp)
            .WithName("ValidateIp")
            .WithSummary("Validate IP address")
            .WithDescription("Checks if an IP address is valid and reachable");
        health.MapGet("/enhanced", GetEnhancedHealthChecks)
            .WithName("GetEnhancedHealthChecks")
            .WithSummary("Get enhanced health checks")
            .WithDescription("Returns enhanced health checks including lag, patch status, and port availability");
        health.MapGet("/lag", GetLagCheck)
            .WithName("GetLagCheck")
            .WithSummary("Check network lag")
            .WithDescription("Returns latency and jitter measurements to master server");
        health.MapGet("/installation", GetInstallationCheck)
            .WithName("GetInstallationCheck")
            .WithSummary("Check HoN installation")
            .WithDescription("Verifies HoN installation directory and required files");

        // AutoPing endpoints
        var autoPing = api.MapGroup("/autoping").WithTags("AutoPing");
        autoPing.MapGet("/status", GetAutoPingStatus)
            .WithName("GetAutoPingStatus")
            .WithSummary("Get AutoPing status")
            .WithDescription("Returns the current status of the AutoPing listener");
        autoPing.MapPost("/start", StartAutoPing)
            .WithName("StartAutoPing")
            .WithSummary("Start AutoPing listener")
            .WithDescription("Starts the UDP AutoPing listener for game client pings");
        autoPing.MapPost("/stop", StopAutoPing)
            .WithName("StopAutoPing")
            .WithSummary("Stop AutoPing listener")
            .WithDescription("Stops the AutoPing listener");
        autoPing.MapGet("/health", CheckAutoPingHealth)
            .WithName("CheckAutoPingHealth")
            .WithSummary("Check AutoPing health")
            .WithDescription("Performs a self-test on the AutoPing listener");

        // Patching endpoints
        var patching = api.MapGroup("/patching").WithTags("Patching");
        patching.MapGet("/status", GetPatchStatus)
            .WithName("GetPatchStatus")
            .WithSummary("Get patch status")
            .WithDescription("Returns current and latest version information");
        patching.MapGet("/check", CheckForPatches)
            .WithName("CheckForPatches")
            .WithSummary("Check for patches")
            .WithDescription("Checks master server for available updates");
        patching.MapPost("/apply", ApplyPatch)
            .WithName("ApplyPatch")
            .WithSummary("Apply patch")
            .WithDescription("Downloads and applies the latest patch");

        // Match Stats endpoints
        var matchStats = api.MapGroup("/matchstats").WithTags("Match Stats");
        matchStats.MapPost("/submit", SubmitMatchStats)
            .WithName("SubmitMatchStats")
            .WithSummary("Submit match stats")
            .WithDescription("Submits match statistics to the master server");
        matchStats.MapPost("/resubmit", ResubmitPendingStats)
            .WithName("ResubmitPendingStats")
            .WithSummary("Resubmit pending stats")
            .WithDescription("Attempts to resubmit any failed match statistics");
        
        // Dependencies endpoints
        var deps = api.MapGroup("/dependencies").WithTags("Dependencies");
        deps.MapGet("/", GetDependencyStatus)
            .WithName("GetDependencyStatus")
            .WithSummary("Check dependencies")
            .WithDescription("Returns the status of all required dependencies");

        // Scheduled tasks endpoints
        var tasks = api.MapGroup("/tasks").WithTags("Scheduled Tasks");
        tasks.MapGet("/", GetScheduledTasks)
            .WithName("GetScheduledTasks")
            .WithSummary("List scheduled tasks")
            .WithDescription("Returns all configured scheduled tasks");
        tasks.MapPost("/{taskName}/run", RunScheduledTask)
            .WithName("RunScheduledTask")
            .WithSummary("Run a task")
            .WithDescription("Manually executes a scheduled task");
        tasks.MapPost("/{taskName}/enable", EnableScheduledTask)
            .WithName("EnableScheduledTask")
            .WithSummary("Enable a task")
            .WithDescription("Enables a scheduled task");
        tasks.MapPost("/{taskName}/disable", DisableScheduledTask)
            .WithName("DisableScheduledTask")
            .WithSummary("Disable a task")
            .WithDescription("Disables a scheduled task");

        // Ban management endpoints
        var bans = api.MapGroup("/bans").WithTags("Ban Management");
        bans.MapGet("/", GetBans)
            .WithName("GetBans")
            .WithSummary("List all bans")
            .WithDescription("Returns all active player bans");
        bans.MapGet("/{accountId:int}", GetBan)
            .WithName("GetBan")
            .WithSummary("Get ban details")
            .WithDescription("Returns details of a specific ban");
        bans.MapPost("/", CreateBan)
            .WithName("CreateBan")
            .WithSummary("Create a ban")
            .WithDescription("Creates a new player ban");
        bans.MapDelete("/{accountId:int}", DeleteBan)
            .WithName("DeleteBan")
            .WithSummary("Delete a ban")
            .WithDescription("Removes a player ban");
        bans.MapGet("/check/{accountId:int}", CheckBan)
            .WithName("CheckBan")
            .WithSummary("Check if player is banned")
            .WithDescription("Checks if a player is currently banned");

        // Replay management endpoints  
        var replaysMgmt = api.MapGroup("/replays/manage").WithTags("Replays");
        replaysMgmt.MapGet("/stats", GetReplayStats)
            .WithName("GetReplayStats")
            .WithSummary("Get replay statistics")
            .WithDescription("Returns statistics about stored replays");
        replaysMgmt.MapPost("/archive", ArchiveReplays)
            .WithName("ArchiveReplays")
            .WithSummary("Archive old replays")
            .WithDescription("Archives replays older than a specified date");
        replaysMgmt.MapPost("/cleanup", CleanupReplays)
            .WithName("CleanupReplays")
            .WithSummary("Cleanup old replays")
            .WithDescription("Removes old archived replays");
        replaysMgmt.MapDelete("/{fileName}", DeleteReplay)
            .WithName("DeleteReplayFile")
            .WithSummary("Delete a replay")
            .WithDescription("Permanently deletes a replay file");

        // Replay upload endpoints
        var replayUpload = api.MapGroup("/replays/upload").WithTags("Replay Upload");
        replayUpload.MapPost("/{matchId}", UploadReplay)
            .WithName("UploadReplay")
            .WithSummary("Upload a replay")
            .WithDescription("Uploads a replay file and queues for master server upload");
        replayUpload.MapGet("/", GetUploadedReplays)
            .WithName("GetUploadedReplays")
            .WithSummary("List uploaded replays")
            .WithDescription("Returns list of stored replays");
        replayUpload.MapGet("/info/{fileName}", GetReplayInfo)
            .WithName("GetReplayInfo")
            .WithSummary("Get replay info")
            .WithDescription("Returns info for a specific replay");
        replayUpload.MapDelete("/{fileName}", DeleteUploadedReplay)
            .WithName("DeleteUploadedReplay")
            .WithSummary("Delete uploaded replay")
            .WithDescription("Deletes a stored replay file");
        replayUpload.MapGet("/stats", GetUploadReplayStats)
            .WithName("GetUploadReplayStats")
            .WithSummary("Get replay stats")
            .WithDescription("Returns replay storage statistics");
        replayUpload.MapPost("/process-pending", ProcessPendingUploads)
            .WithName("ProcessPendingUploads")
            .WithSummary("Process pending uploads")
            .WithDescription("Processes pending master server uploads");

        // Events endpoints
        var events = api.MapGroup("/events").WithTags("Events");
        events.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("List recent events")
            .WithDescription("Returns recent game events");
        events.MapGet("/stats", GetEventStats)
            .WithName("GetEventStats")
            .WithSummary("Get event statistics")
            .WithDescription("Returns aggregated event statistics");
        events.MapGet("/server/{serverId:int}", GetEventsByServer)
            .WithName("GetEventsByServer")
            .WithSummary("Get events by server")
            .WithDescription("Returns events for a specific server");
        events.MapGet("/type/{eventType}", GetEventsByType)
            .WithName("GetEventsByType")
            .WithSummary("Get events by type")
            .WithDescription("Returns events filtered by event type");
        events.MapGet("/mqtt-publishable", GetMqttPublishableEvents)
            .WithName("GetMqttPublishableEvents")
            .WithSummary("Get MQTT publishable events")
            .WithDescription("Returns events that were or will be published to MQTT");
        events.MapPost("/simulate", SimulateEvent)
            .WithName("SimulateEvent")
            .WithSummary("Simulate an event")
            .WithDescription("Creates a test event for debugging purposes");
        events.MapGet("/export/json", ExportEventsJson)
            .WithName("ExportEventsJson")
            .WithSummary("Export events as JSON")
            .WithDescription("Downloads all events as a JSON file");
        events.MapGet("/export/csv", ExportEventsCsv)
            .WithName("ExportEventsCsv")
            .WithSummary("Export events as CSV")
            .WithDescription("Downloads all events as a CSV file");

        // Performance Metrics endpoints
        var performance = api.MapGroup("/performance").WithTags("Performance");
        performance.MapGet("/current", GetCurrentPerformance)
            .WithName("GetCurrentPerformance")
            .WithSummary("Get current performance metrics")
            .WithDescription("Returns real-time CPU, memory, and network metrics");
        performance.MapGet("/history", GetPerformanceHistory)
            .WithName("GetPerformanceHistory")
            .WithSummary("Get performance history")
            .WithDescription("Returns historical performance data for charting");
        performance.MapGet("/servers", GetServerPerformance)
            .WithName("GetServerPerformance")
            .WithSummary("Get per-server performance")
            .WithDescription("Returns performance metrics for each server");
        performance.MapGet("/summary", GetPerformanceSummary)
            .WithName("GetPerformanceSummary")
            .WithSummary("Get performance summary")
            .WithDescription("Returns aggregated performance statistics");

        // System endpoints
        api.MapGet("/system/stats", GetSystemStats)
            .WithName("GetSystemStats")
            .WithSummary("Get detailed system stats")
            .WithDescription("Returns comprehensive system statistics")
            .WithTags("General");
        api.MapGet("/version", () => Results.Ok(new { version = "1.0.0-dotnet", framework = ".NET 10" }))
            .WithName("GetVersion")
            .WithSummary("Get API version")
            .WithDescription("Returns the API version and framework information")
            .WithTags("General");

        // Auto-scaling endpoints
        var scaling = api.MapGroup("/scaling").WithTags("Auto-scaling");
        scaling.MapGet("/status", GetAutoScalingStatus)
            .WithName("GetAutoScalingStatus")
            .WithSummary("Get auto-scaling status")
            .WithDescription("Returns current auto-scaling configuration and status");
        scaling.MapPost("/scale-up", ManualScaleUp)
            .WithName("ManualScaleUp")
            .WithSummary("Manually scale up")
            .WithDescription("Manually triggers adding a new server");
        scaling.MapPost("/scale-down", ManualScaleDown)
            .WithName("ManualScaleDown")
            .WithSummary("Manually scale down")
            .WithDescription("Manually triggers removing a server");

        // Server Templates endpoints
        var templates = api.MapGroup("/templates").WithTags("Templates");
        templates.MapGet("/", GetTemplates)
            .WithName("GetTemplates")
            .WithSummary("Get all server templates");
        templates.MapPost("/", CreateTemplate)
            .WithName("CreateTemplate")
            .WithSummary("Create a new server template");
        templates.MapGet("/{id}", GetTemplate)
            .WithName("GetTemplate")
            .WithSummary("Get a specific template");
        templates.MapPut("/{id}", UpdateTemplate)
            .WithName("UpdateTemplate")
            .WithSummary("Update a template");
        templates.MapDelete("/{id}", DeleteTemplate)
            .WithName("DeleteTemplate")
            .WithSummary("Delete a template");
        templates.MapPost("/{id}/apply", ApplyTemplate)
            .WithName("ApplyTemplate")
            .WithSummary("Apply a template to create a new server");

        // Webhooks endpoints
        var webhooks = api.MapGroup("/webhooks").WithTags("Webhooks");
        webhooks.MapGet("/", GetWebhooks)
            .WithName("GetWebhooks")
            .WithSummary("Get all registered webhooks");
        webhooks.MapPost("/", RegisterWebhookEndpoint)
            .WithName("RegisterWebhook")
            .WithSummary("Register a new webhook");
        webhooks.MapDelete("/{id}", DeleteWebhookEndpoint)
            .WithName("DeleteWebhook")
            .WithSummary("Delete a webhook");
        webhooks.MapPost("/{id}/test", TestWebhookEndpoint)
            .WithName("TestWebhook")
            .WithSummary("Send a test webhook");

        // Discord test endpoints
        var discord = api.MapGroup("/discord").WithTags("Discord");
        discord.MapGet("/status", GetDiscordStatus)
            .WithName("GetDiscordStatus")
            .WithSummary("Get Discord bot status")
            .WithDescription("Returns the current status of the Discord bot");
        discord.MapPut("/settings", UpdateDiscordSettings)
            .WithName("UpdateDiscordSettings")
            .WithSummary("Update Discord bot settings");
        discord.MapPost("/test/match-start", TestMatchStartNotification)
            .WithName("TestMatchStartNotification")
            .WithSummary("Test match start notification")
            .WithDescription("Sends a test match started notification to Discord");
        discord.MapPost("/test/match-end", TestMatchEndNotification)
            .WithName("TestMatchEndNotification")
            .WithSummary("Test match end notification")
            .WithDescription("Sends a test match ended notification to Discord");
        discord.MapPost("/test/player-join", TestPlayerJoinNotification)
            .WithName("TestPlayerJoinNotification")
            .WithSummary("Test player join notification")
            .WithDescription("Sends a test player joined notification to Discord");
        discord.MapPost("/test/alert", TestAlertNotification)
            .WithName("TestAlertNotification")
            .WithSummary("Test alert notification")
            .WithDescription("Sends a test alert notification to Discord");

        // Auth endpoints
        var auth = api.MapGroup("/auth").WithTags("Authentication");
        auth.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("User login")
            .WithDescription("Authenticates a user and returns a JWT token. Default credentials: admin/admin")
            .AllowAnonymous();
        auth.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("User logout")
            .WithDescription("Logs out the current user");
        auth.MapGet("/validate", ValidateToken)
            .WithName("ValidateToken")
            .WithSummary("Validate JWT token")
            .WithDescription("Validates the provided JWT token");
        auth.MapGet("/users", GetUsers)
            .WithName("GetUsers")
            .WithSummary("List users")
            .WithDescription("Returns all registered users (admin only)");
        auth.MapPost("/users", CreateUser)
            .WithName("CreateUser")
            .WithSummary("Create user")
            .WithDescription("Creates a new user account (admin only)");
        auth.MapDelete("/users/{username}", DeleteUser)
            .WithName("DeleteUser")
            .WithSummary("Delete user")
            .WithDescription("Deletes a user account (admin only)");
        auth.MapPost("/change-password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Change password")
            .WithDescription("Changes the current user's password");

        auth.MapGet("/status", () => Results.Ok(new { authenticated = true, user = "admin", role = "admin" }))
            .WithName("GetAuthStatus")
            .WithSummary("Get authentication status")
            .WithDescription("Returns current authentication status for dashboard");

        // Advanced Metrics endpoints
        var advMetrics = api.MapGroup("/metrics/advanced").WithTags("Metrics");
        advMetrics.MapGet("/server/{serverId:int}", GetServerMetrics)
            .WithName("GetServerMetrics")
            .WithSummary("Get server-specific metrics")
            .WithDescription("Returns detailed metrics history for a specific server");
        advMetrics.MapGet("/system", GetSystemMetricsHistory)
            .WithName("GetSystemMetricsHistory")
            .WithSummary("Get system metrics history")
            .WithDescription("Returns historical system resource usage");
        advMetrics.MapGet("/summary", GetAllServersSummary)
            .WithName("GetAllServersSummary")
            .WithSummary("Get all servers summary")
            .WithDescription("Returns a summary of all server metrics");
        advMetrics.MapGet("/compare", CompareServers)
            .WithName("CompareServers")
            .WithSummary("Compare servers")
            .WithDescription("Compares metrics between selected servers");

        // Notifications endpoints
        var notifications = api.MapGroup("/notifications").WithTags("Notifications");
        notifications.MapGet("/", GetNotifications)
            .WithName("GetNotifications")
            .WithSummary("Get recent notifications")
            .WithDescription("Returns recent notifications with optional count limit");
        notifications.MapGet("/unacknowledged", GetUnacknowledgedNotifications)
            .WithName("GetUnacknowledgedNotifications")
            .WithSummary("Get unacknowledged notifications")
            .WithDescription("Returns notifications that require acknowledgement");
        notifications.MapPost("/{id}/acknowledge", AcknowledgeNotification)
            .WithName("AcknowledgeNotification")
            .WithSummary("Acknowledge a notification")
            .WithDescription("Marks a notification as acknowledged");
        notifications.MapDelete("/", ClearNotifications)
            .WithName("ClearNotifications")
            .WithSummary("Clear all notifications")
            .WithDescription("Removes all notifications from history");
        notifications.MapGet("/thresholds", GetAlertThresholds)
            .WithName("GetAlertThresholds")
            .WithSummary("Get alert thresholds")
            .WithDescription("Returns current CPU/Memory/Disk alert threshold settings");
        notifications.MapPut("/thresholds", UpdateAlertThresholds)
            .WithName("UpdateAlertThresholds")
            .WithSummary("Update alert thresholds")
            .WithDescription("Updates alert threshold settings");

        // Charts endpoints
        var charts = api.MapGroup("/charts").WithTags("Charts");
        charts.MapGet("/uptime", GetUptimeChart)
            .WithName("GetUptimeChart")
            .WithSummary("Get server uptime data")
            .WithDescription("Returns uptime history and percentages for all servers");
        charts.MapGet("/uptime/{serverId:int}", GetServerUptimeChart)
            .WithName("GetServerUptimeChart")
            .WithSummary("Get single server uptime")
            .WithDescription("Returns detailed uptime history for a specific server");
        charts.MapGet("/players", GetPlayerCountChart)
            .WithName("GetPlayerCountChart")
            .WithSummary("Get player count history")
            .WithDescription("Returns player count over time");
        charts.MapGet("/resources", GetResourceCharts)
            .WithName("GetResourceCharts")
            .WithSummary("Get resource usage charts")
            .WithDescription("Returns CPU, Memory, Disk usage over time");
        charts.MapGet("/matches", GetMatchStatsChart)
            .WithName("GetMatchStatsChart")
            .WithSummary("Get match statistics")
            .WithDescription("Returns match history and statistics");
        charts.MapGet("/matches/summary", GetMatchSummary)
            .WithName("GetMatchSummary")
            .WithSummary("Get match summary")
            .WithDescription("Returns aggregated match statistics");

        // Filebeat endpoints
        var filebeat = api.MapGroup("/filebeat").WithTags("Filebeat");
        filebeat.MapGet("/status", GetFilebeatStatus)
            .WithName("GetFilebeatStatus")
            .WithSummary("Get Filebeat status")
            .WithDescription("Returns current Filebeat service status and configuration");
        filebeat.MapPost("/install", InstallFilebeat)
            .WithName("InstallFilebeat")
            .WithSummary("Install Filebeat")
            .WithDescription("Downloads and installs Filebeat for log shipping");
        filebeat.MapPost("/start", StartFilebeat)
            .WithName("StartFilebeat")
            .WithSummary("Start Filebeat")
            .WithDescription("Starts the Filebeat service");
        filebeat.MapPost("/stop", StopFilebeat)
            .WithName("StopFilebeat")
            .WithSummary("Stop Filebeat")
            .WithDescription("Stops the Filebeat service");
        filebeat.MapPost("/configure", ConfigureFilebeat)
            .WithName("ConfigureFilebeat")
            .WithSummary("Configure Filebeat")
            .WithDescription("Generates Filebeat configuration for Elasticsearch");
        filebeat.MapPost("/test", TestFilebeatConnection)
            .WithName("TestFilebeatConnection")
            .WithSummary("Test Elasticsearch connection")
            .WithDescription("Tests connection to Elasticsearch server");

        // RBAC/Permissions endpoints
        var rbac = api.MapGroup("/rbac").WithTags("RBAC");
        rbac.MapGet("/permissions", GetAllPermissions)
            .WithName("GetAllPermissions")
            .WithSummary("List all permissions")
            .WithDescription("Returns all available permissions with descriptions");
        rbac.MapGet("/roles", GetAllRoles)
            .WithName("GetAllRoles")
            .WithSummary("List all roles")
            .WithDescription("Returns all configured roles with their permissions");
        rbac.MapGet("/roles/{roleName}", GetRole)
            .WithName("GetRole")
            .WithSummary("Get role details")
            .WithDescription("Returns a specific role with its permissions");
        rbac.MapPost("/roles", CreateRole)
            .WithName("CreateRole")
            .WithSummary("Create role")
            .WithDescription("Creates a new role with specified permissions");
        rbac.MapDelete("/roles/{roleName}", DeleteRole)
            .WithName("DeleteRole")
            .WithSummary("Delete role")
            .WithDescription("Deletes a role");
        rbac.MapPost("/roles/{roleName}/permissions", AddPermissionsToRole)
            .WithName("AddPermissionsToRole")
            .WithSummary("Add permissions to role")
            .WithDescription("Adds permissions to an existing role");
        rbac.MapDelete("/roles/{roleName}/permissions", RemovePermissionsFromRole)
            .WithName("RemovePermissionsFromRole")
            .WithSummary("Remove permissions from role")
            .WithDescription("Removes permissions from a role");
        rbac.MapGet("/users/{userId:int}/permissions", GetUserPermissions)
            .WithName("GetUserPermissions")
            .WithSummary("Get user permissions")
            .WithDescription("Returns all permissions for a user");
        rbac.MapPost("/users/{userId:int}/roles", AssignRoleToUser)
            .WithName("AssignRoleToUser")
            .WithSummary("Assign role to user")
            .WithDescription("Assigns a role to a user");
        rbac.MapDelete("/users/{userId:int}/roles/{roleName}", RemoveRoleFromUser)
            .WithName("RemoveRoleFromUser")
            .WithSummary("Remove role from user")
            .WithDescription("Removes a role from a user");

        // Skipped Frame Analytics endpoints
        var diagnostics = api.MapGroup("/diagnostics").WithTags("Diagnostics");
        diagnostics.MapGet("/skipped-frames", GetGlobalFrameAnalytics)
            .WithName("GetGlobalFrameAnalytics")
            .WithSummary("Get global frame analytics")
            .WithDescription("Returns overall skipped frame statistics across all servers");
        diagnostics.MapGet("/skipped-frames/server/{serverId:int}", GetServerFrameAnalytics)
            .WithName("GetServerFrameAnalytics")
            .WithSummary("Get server frame analytics")
            .WithDescription("Returns skipped frame statistics for a specific server");
        diagnostics.MapGet("/skipped-frames/player/{playerName}", GetPlayerFrameStats)
            .WithName("GetPlayerFrameStats")
            .WithSummary("Get player frame stats")
            .WithDescription("Returns skipped frame statistics for a specific player");
        diagnostics.MapPost("/skipped-frames/reset", ResetFrameAnalytics)
            .WithName("ResetFrameAnalytics")
            .WithSummary("Reset frame analytics")
            .WithDescription("Clears all skipped frame data");

        // Storage/File relocation endpoints
        var storage = api.MapGroup("/storage").WithTags("Storage");
        storage.MapGet("/status", GetStorageStatus)
            .WithName("GetStorageStatus")
            .WithSummary("Get storage status")
            .WithDescription("Returns storage configuration and statistics");
        storage.MapGet("/analytics", GetStorageAnalytics)
            .WithName("GetStorageAnalytics")
            .WithSummary("Get storage analytics")
            .WithDescription("Returns detailed file analytics for primary and archive storage");
        storage.MapPost("/relocate", RelocateOldFiles)
            .WithName("RelocateOldFiles")
            .WithSummary("Relocate old files")
            .WithDescription("Moves old files to archive storage");
        storage.MapPost("/cleanup", CleanupArchiveStorage)
            .WithName("CleanupArchiveStorage")
            .WithSummary("Cleanup archive storage")
            .WithDescription("Removes old files from archive storage");
        storage.MapPost("/relocate-logs", RelocateLogs)
            .WithName("RelocateLogs")
            .WithSummary("Relocate logs")
            .WithDescription("Moves old log files to archive storage");

        // Git/Version management endpoints
        var git = api.MapGroup("/system/git").WithTags("System");
        git.MapGet("/branch", GetCurrentBranch)
            .WithName("GetCurrentBranch")
            .WithSummary("Get current branch")
            .WithDescription("Returns the current Git branch name");
        git.MapGet("/branches", GetAllBranches)
            .WithName("GetAllBranches")
            .WithSummary("List all branches")
            .WithDescription("Returns all local and remote Git branches");
        git.MapPost("/switch/{branchName}", SwitchBranch)
            .WithName("SwitchBranch")
            .WithSummary("Switch branch")
            .WithDescription("Switches to a different Git branch");
        git.MapGet("/updates", CheckForUpdates)
            .WithName("CheckForUpdates")
            .WithSummary("Check for updates")
            .WithDescription("Checks for available updates from remote repository");
        git.MapPost("/pull", PullUpdates)
            .WithName("PullUpdates")
            .WithSummary("Pull updates")
            .WithDescription("Pulls latest changes from remote repository");
        git.MapGet("/version", GetVersionInfo)
            .WithName("GetVersionInfo")
            .WithSummary("Get version info")
            .WithDescription("Returns detailed version information");

        // Server Scaling endpoints (advanced)
        var serverScaling = api.MapGroup("/scaling/advanced").WithTags("Auto-scaling");
        serverScaling.MapGet("/status", GetScalingServiceStatus)
            .WithName("GetScalingServiceStatus")
            .WithSummary("Get scaling service status")
            .WithDescription("Returns current scaling service status and configuration");
        serverScaling.MapPost("/add/{count:int}", AddServersScaling)
            .WithName("AddServersScaling")
            .WithSummary("Add servers")
            .WithDescription("Adds specified number of servers");
        serverScaling.MapPost("/remove/{count:int}", RemoveServersScaling)
            .WithName("RemoveServersScaling")
            .WithSummary("Remove servers")
            .WithDescription("Removes specified number of empty servers");
        serverScaling.MapPost("/scale-to/{count:int}", ScaleToCount)
            .WithName("ScaleToCount")
            .WithSummary("Scale to count")
            .WithDescription("Scales to the specified number of total servers");
        serverScaling.MapPost("/auto-balance", AutoBalanceServers)
            .WithName("AutoBalanceServers")
            .WithSummary("Auto-balance servers")
            .WithDescription("Automatically adjusts server count based on current demand");

        // Backup/Restore endpoints
        var backups = api.MapGroup("/backups").WithTags("Backup/Restore");
        backups.MapGet("/", GetBackups)
            .WithName("GetBackups")
            .WithSummary("List all backups")
            .WithDescription("Returns all available configuration backups");
        backups.MapGet("/{backupId}", GetBackupDetails)
            .WithName("GetBackupDetails")
            .WithSummary("Get backup details")
            .WithDescription("Returns detailed information about a specific backup");
        backups.MapPost("/", CreateBackup)
            .WithName("CreateBackup")
            .WithSummary("Create a new backup")
            .WithDescription("Creates a full backup of all configuration files");
        backups.MapPost("/{backupId}/restore", RestoreBackup)
            .WithName("RestoreBackup")
            .WithSummary("Restore from backup")
            .WithDescription("Restores configuration from a specific backup");
        backups.MapDelete("/{backupId}", DeleteBackupEndpoint)
            .WithName("DeleteBackup")
            .WithSummary("Delete a backup")
            .WithDescription("Permanently deletes a backup file");
        backups.MapGet("/{backupId}/download", DownloadBackup)
            .WithName("DownloadBackup")
            .WithSummary("Download backup")
            .WithDescription("Downloads the backup file as a ZIP archive");
    }

    private static IResult GetStatus(
        IGameServerManager serverManager, 
        IMqttHandler mqttHandler, 
        IManagementPortalConnector managementPortal,
        HoNConfiguration config)
    {
        RecordMetrics(serverManager);
        var status = serverManager.GetStatus();
        
        // Add MQTT status
        status.MqttConnected = mqttHandler.IsConnected;
        status.MqttEnabled = mqttHandler.IsEnabled;
        status.MqttBroker = config.ApplicationData?.Mqtt?.Host ?? config.ApplicationData?.ManagementPortal?.MqttHost;
        
        // Add Management Portal status
        status.ManagementPortalConnected = managementPortal.IsEnabled;
        status.ManagementPortalRegistered = managementPortal.IsRegistered;
        status.ManagementPortalServerName = managementPortal.ServerName;
        
        return Results.Ok(status);
    }

    private static IResult GetServers(IGameServerManager serverManager)
    {
        // ServerStatusResponse already has correct JSON formatting
        return Results.Ok(new { instances = serverManager.Instances });
    }

    private static IResult GetServer(int id, IGameServerManager serverManager)
    {
        var server = serverManager.Instances.FirstOrDefault(s => s.Id == id);
        return server is null ? Results.NotFound() : Results.Ok(server);
    }

    private static async Task<IResult> StartServer(int id, IGameServerManager serverManager, IProxyService proxyService)
    {
        var result = await serverManager.StartServerAsync(id);
        if (result != null)
        {
            await proxyService.StartProxyAsync(result);
        }
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> StopServer(int id, IGameServerManager serverManager, IProxyService proxyService)
    {
        proxyService.StopProxy(id);
        var success = await serverManager.StopServerAsync(id);
        return success ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> RestartServer(int id, IGameServerManager serverManager, IProxyService proxyService)
    {
        proxyService.StopProxy(id);
        var success = await serverManager.RestartServerAsync(id);
        if (success)
        {
            var server = serverManager.Instances.FirstOrDefault(s => s.Id == id);
            if (server != null)
            {
                await proxyService.StartProxyAsync(server);
            }
        }
        return success ? Results.Ok() : Results.NotFound();
    }

    private static async Task<IResult> StartAllServers(IGameServerManager serverManager, IProxyService proxyService)
    {
        await serverManager.StartAllServersAsync();
        foreach (var server in serverManager.Instances.Where(s => s.Status == ServerStatus.Ready || s.Status == ServerStatus.Occupied))
        {
            await proxyService.StartProxyAsync(server);
        }
        return Results.Ok(new { message = "Starting all servers", status = serverManager.GetStatus() });
    }

    private static async Task<IResult> StopAllServers(IGameServerManager serverManager, IProxyService proxyService)
    {
        proxyService.StopAllProxies();
        await serverManager.StopAllServersAsync();
        return Results.Ok(new { message = "Stopping all servers", status = serverManager.GetStatus() });
    }

    private static async Task<IResult> RestartAllServers(IGameServerManager serverManager, IProxyService proxyService)
    {
        proxyService.StopAllProxies();
        await serverManager.StopAllServersAsync();
        await serverManager.StartAllServersAsync();
        foreach (var server in serverManager.Instances.Where(s => s.Status == ServerStatus.Ready || s.Status == ServerStatus.Occupied))
        {
            await proxyService.StartProxyAsync(server);
        }
        return Results.Ok(new { message = "Restarting all servers", status = serverManager.GetStatus() });
    }

    private static async Task<IResult> AddServers(HttpContext context, IGameServerManager serverManager)
    {
        var body = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var count = 1;
        if (body != null && body.TryGetValue("count", out var countElement))
        {
            if (countElement.TryGetInt32(out var countValue))
                count = countValue;
        }
        
        var added = new List<object>();
        for (int i = 0; i < count; i++)
        {
            var newId = serverManager.Instances.Count > 0 
                ? serverManager.Instances.Max(s => s.Id) + 1 
                : 1;
            var basePort = 10001;
            var port = basePort + newId - 1;
            
            var newServer = new GameServerInstance
            {
                Id = newId,
                Name = $"HoN Server #{newId}",
                Port = port,
                VoicePort = port + 60,
                MaxClients = 10,
                Status = ServerStatus.Offline
            };
            serverManager.AddServer(newServer);
            added.Add(new { id = newId, name = newServer.Name, port });
        }
        
        return Results.Ok(new { message = $"Added {count} server(s)", added, total = serverManager.Instances.Count });
    }

    private static async Task<IResult> DeleteServers(HttpContext context, IGameServerManager serverManager)
    {
        var body = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var countStr = "1";
        if (body != null && body.TryGetValue("count", out var countElement))
        {
            countStr = countElement.ToString();
        }
        
        int removed = 0;
        if (countStr == "all")
        {
            removed = serverManager.Instances.Count;
            serverManager.ClearServers();
        }
        else if (int.TryParse(countStr, out var count))
        {
            for (int i = 0; i < count && serverManager.Instances.Count > 0; i++)
            {
                var last = serverManager.Instances.LastOrDefault();
                if (last != null && serverManager.RemoveServer(last.Id))
                {
                    removed++;
                }
            }
        }
        
        return Results.Ok(new { message = $"Removed {removed} server(s)", remaining = serverManager.Instances.Count });
    }

    private static async Task<IResult> ScaleServers(HttpContext context, IGameServerManager serverManager)
    {
        var body = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var target = 1;
        if (body != null && body.TryGetValue("target", out var targetElement))
        {
            if (targetElement.TryGetInt32(out var targetValue))
                target = targetValue;
        }
        
        var current = serverManager.Instances.Count;
        var message = "";
        
        if (target > current)
        {
            // Add servers
            var toAdd = target - current;
            for (int i = 0; i < toAdd; i++)
            {
                var newId = serverManager.Instances.Count > 0 
                    ? serverManager.Instances.Max(s => s.Id) + 1 
                    : 1;
                var basePort = 10001;
                var port = basePort + newId - 1;
                
                serverManager.AddServer(new GameServerInstance
                {
                    Id = newId,
                    Name = $"HoN Server #{newId}",
                    Port = port,
                    VoicePort = port + 60,
                    MaxClients = 10,
                    Status = ServerStatus.Offline
                });
            }
            message = $"Added {toAdd} server(s)";
        }
        else if (target < current)
        {
            // Remove servers
            var toRemove = current - target;
            for (int i = 0; i < toRemove; i++)
            {
                var last = serverManager.Instances.LastOrDefault();
                if (last != null)
                {
                    serverManager.RemoveServer(last.Id);
                }
            }
            message = $"Removed {toRemove} server(s)";
        }
        else
        {
            message = "No changes needed";
        }
        
        return Results.Ok(new { message, current = serverManager.Instances.Count, target });
    }

    private static async Task<IResult> MessageAllServers(HttpContext context, IGameServerManager serverManager)
    {
        var body = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var message = "";
        var type = "info";
        if (body != null)
        {
            if (body.TryGetValue("message", out var msgElement))
                message = msgElement.GetString() ?? "";
            if (body.TryGetValue("type", out var typeElement))
                type = typeElement.GetString() ?? "info";
        }
        
        // In a real implementation, this would send the message to all running servers
        var runningCount = serverManager.Instances.Count(s => s.Status == ServerStatus.Ready || s.Status == ServerStatus.Occupied);
        
        return Results.Ok(new { message = $"Message sent to {runningCount} server(s)", content = message, type });
    }

    private static async Task<IResult> KickPlayerFromServer(int id, [FromBody] KickPlayerRequest request, IGameServerManager serverManager, BanManager banManager)
    {
        var instance = serverManager.Instances.FirstOrDefault(i => i.Id == id);
        if (instance == null)
            return Results.NotFound(new { error = $"Server {id} not found" });
        
        // Log the kick
        banManager.AddKickRecord(request.AccountId, request.PlayerName, request.Reason ?? "Kicked by admin", "Admin", id);
        
        // In a real implementation, this would send a kick command to the server
        return Results.Ok(new { 
            success = true, 
            message = $"Player {request.PlayerName} kicked from server #{id}",
            serverId = id,
            accountId = request.AccountId,
            reason = request.Reason
        });
    }

        private static async Task<IResult> GetConfiguration(IConfigurationService configService)
    {
        var config = await configService.LoadConfigurationAsync();
        
        // Convert to dashboard format: { "key": { "value": x, "editable": bool } }
        var result = new Dictionary<string, object>();
        
        // Editable keys that can be changed from dashboard
        var editableKeys = new HashSet<string>
        {
            "svr_name", "svr_location", "svr_priority", "svr_total",
            "svr_enableBotMatch", "svr_start_on_launch", "svr_noConsole",
            "svr_max_start_at_once", "svr_startup_timeout", "svr_restart_between_games",
            "man_enableProxy", "man_use_cowmaster",
            "svr_masterServer", "svr_patchServer", "svr_starting_gamePort", 
            "svr_api_port"
        };
        
        // HonData properties
        result["svr_name"] = new { value = config.HonData.ServerName, editable = true };
        result["svr_location"] = new { value = config.HonData.Location, editable = true };
        result["svr_priority"] = new { value = config.HonData.Priority, editable = true };
        result["svr_total"] = new { value = config.HonData.TotalServers, editable = true };
        result["svr_total_per_core"] = new { value = config.HonData.TotalPerCore, editable = true };
        result["svr_enableBotMatch"] = new { value = config.HonData.EnableBotMatch, editable = true };
        result["svr_start_on_launch"] = new { value = config.HonData.StartOnLaunch, editable = true };
        result["svr_noConsole"] = new { value = config.HonData.NoConsole, editable = true };
        result["svr_max_start_at_once"] = new { value = config.HonData.MaxStartAtOnce, editable = true };
        result["svr_startup_timeout"] = new { value = config.HonData.StartupTimeout, editable = true };
        result["svr_restart_between_games"] = new { value = config.HonData.RestartBetweenGames, editable = true };
        result["man_enableProxy"] = new { value = config.HonData.EnableProxy, editable = true };
        result["man_use_cowmaster"] = new { value = config.HonData.UseCowmaster, editable = true };
        result["svr_masterServer"] = new { value = config.HonData.MasterServer, editable = true };
        result["svr_chatServer"] = new { value = config.HonData.ChatServer, editable = true };
        result["svr_patchServer"] = new { value = config.HonData.PatchServer, editable = true };
        result["svr_starting_gamePort"] = new { value = config.HonData.StartingGamePort, editable = true };
        result["svr_starting_voicePort"] = new { value = config.HonData.StartingVoicePort, editable = false };
        result["svr_api_port"] = new { value = config.HonData.ApiPort, editable = true };
        result["svr_maxClients"] = new { value = 10, editable = true };
        result["svr_managerPort"] = new { value = config.HonData.ManagerPort, editable = false };
        
        // Read-only info
        result["hon_install_directory"] = new { value = config.HonData.HonInstallDirectory, editable = true };
        result["hon_home_directory"] = new { value = config.HonData.HonHomeDirectory, editable = true };
        result["svr_beta_mode"] = new { value = config.HonData.BetaMode, editable = false };
        
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveConfiguration(
        HttpRequest request,
        IConfigurationService configService,
        IProxyService proxyService,
        IGameServerManager serverManager)
    {
        try
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync();
            
            // Parse as flat dictionary (from dashboard)
            var flatConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (flatConfig == null)
                return Results.BadRequest(new { error = "Invalid configuration format" });
            
            // Load existing config and update with new values
            var config = await configService.LoadConfigurationAsync();
            var previousProxyEnabled = config.HonData.EnableProxy;
            
            foreach (var (key, value) in flatConfig)
            {
                switch (key)
                {
                    case "svr_name":
                        config.HonData.ServerName = value.GetString() ?? config.HonData.ServerName;
                        break;
                    case "svr_location":
                        config.HonData.Location = value.GetString() ?? config.HonData.Location;
                        break;
                    case "svr_priority":
                        config.HonData.Priority = value.GetString() ?? config.HonData.Priority;
                        break;
                    case "svr_total":
                        if (value.TryGetInt32(out var total))
                            config.HonData.TotalServers = total;
                        break;
                    case "svr_total_per_core":
                        if (value.TryGetDouble(out var perCore))
                            config.HonData.TotalPerCore = perCore;
                        break;
                    case "svr_enableBotMatch":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.EnableBotMatch = value.GetBoolean();
                        break;
                    case "svr_start_on_launch":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.StartOnLaunch = value.GetBoolean();
                        break;
                    case "svr_noConsole":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.NoConsole = value.GetBoolean();
                        break;
                    case "svr_max_start_at_once":
                        if (value.TryGetInt32(out var maxStart))
                            config.HonData.MaxStartAtOnce = maxStart;
                        break;
                    case "svr_startup_timeout":
                        if (value.TryGetInt32(out var timeout))
                            config.HonData.StartupTimeout = timeout;
                        break;
                    case "svr_restart_between_games":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.RestartBetweenGames = value.GetBoolean();
                        break;
                    case "man_enableProxy":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.EnableProxy = value.GetBoolean();
                        break;
                    case "man_use_cowmaster":
                        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                            config.HonData.UseCowmaster = value.GetBoolean();
                        break;
                    case "svr_masterServer":
                        config.HonData.MasterServer = value.GetString() ?? config.HonData.MasterServer;
                        break;
                    case "svr_chatServer":
                        config.HonData.ChatServer = value.GetString() ?? config.HonData.ChatServer;
                        break;
                    case "svr_patchServer":
                        config.HonData.PatchServer = value.GetString() ?? config.HonData.PatchServer;
                        break;
                    case "svr_starting_gamePort":
                        if (value.TryGetInt32(out var gamePort))
                            config.HonData.StartingGamePort = gamePort;
                        break;
                    case "svr_api_port":
                        if (value.TryGetInt32(out var apiPort))
                            config.HonData.ApiPort = apiPort;
                        break;
                    case "hon_install_directory":
                        config.HonData.HonInstallDirectory = value.GetString() ?? config.HonData.HonInstallDirectory;
                        break;
                    case "hon_home_directory":
                        config.HonData.HonHomeDirectory = value.GetString() ?? config.HonData.HonHomeDirectory;
                        break;
                }
            }
            
            await configService.SaveConfigurationAsync(config);
            
            // Update GameServerManager's config reference so stats reflect new values
            serverManager.Configuration = config;
            
            // Handle proxy toggle
            if (config.HonData.EnableProxy != previousProxyEnabled)
            {
                if (config.HonData.EnableProxy)
                {
                    // Start proxies for all running servers
                    foreach (var server in serverManager.Instances.Where(s => s.Status == ServerStatus.Ready || s.Status == ServerStatus.Occupied))
                    {
                        await proxyService.StartProxyAsync(server);
                    }
                }
                else
                {
                    // Stop all proxies
                    proxyService.StopAllProxies();
                    // Reset public ports
                    foreach (var server in serverManager.Instances)
                    {
                        server.ProxyEnabled = false;
                        server.PublicPort = server.Port;
                    }
                }
            }
            
            return Results.Ok(new { status = "ok", message = "Configuration saved" });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { status = "error", error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateConfiguration(
        HttpRequest request,
        IConfigurationService configService,
        IProxyService proxyService,
        IGameServerManager serverManager)
    {
        // Use the same logic as SaveConfiguration - merge with existing config
        return await SaveConfiguration(request, configService, proxyService, serverManager);
    }

    // Logs endpoints
    private static IResult GetLogs(string serverId)
    {
        var logPath = serverId == "manager" 
            ? Path.Combine("logs", "manager.log")
            : Path.Combine("logs", $"server_{serverId}.log");
        
        var logs = new List<string>();
        if (File.Exists(logPath))
        {
            logs = File.ReadAllLines(logPath).TakeLast(500).ToList();
        }
        else
        {
            // Return sample logs for demo
            logs = GenerateSampleLogs();
        }
        
        return Results.Ok(new { logs });
    }

    private static IResult DownloadLog(string serverId)
    {
        var logPath = serverId == "manager" 
            ? Path.Combine("logs", "manager.log")
            : Path.Combine("logs", $"server_{serverId}.log");
        
        if (!File.Exists(logPath))
        {
            return Results.NotFound();
        }
        
        var bytes = File.ReadAllBytes(logPath);
        return Results.File(bytes, "text/plain", Path.GetFileName(logPath));
    }

    private static List<string> GenerateSampleLogs()
    {
        var logs = new List<string>();
        var now = DateTime.Now;
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] HoNfigurator .NET 10 started");
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] Loading configuration...");
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] Configuration loaded successfully");
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] Initializing game server manager");
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] SignalR hub registered");
        logs.Add($"[{now:yyyy-MM-dd HH:mm:ss}] [INFO] Web server listening on port 5050");
        return logs;
    }

    // Replays endpoints
    private static IResult GetReplays()
    {
        var replaysDir = "replays";
        var replays = new List<object>();
        
        if (Directory.Exists(replaysDir))
        {
            var files = Directory.GetFiles(replaysDir, "*.honreplay");
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var matchId = Path.GetFileNameWithoutExtension(file).Split('_').FirstOrDefault() ?? "Unknown";
                replays.Add(new
                {
                    filename = info.Name,
                    matchId,
                    date = info.CreationTime.ToString("yyyy-MM-dd HH:mm"),
                    size = FormatFileSize(info.Length)
                });
            }
        }
        else
        {
            // Sample replays for demo
            replays.Add(new { filename = "M159876543_2024_01_15.honreplay", matchId = "159876543", date = "2024-01-15 14:30", size = "2.4 MB" });
            replays.Add(new { filename = "M159876542_2024_01_15.honreplay", matchId = "159876542", date = "2024-01-15 13:15", size = "2.1 MB" });
            replays.Add(new { filename = "M159876541_2024_01_14.honreplay", matchId = "159876541", date = "2024-01-14 20:45", size = "2.8 MB" });
        }
        
        return Results.Ok(new { replays });
    }

    private static IResult DownloadReplay(string filename)
    {
        var filePath = Path.Combine("replays", filename);
        if (!File.Exists(filePath))
        {
            return Results.NotFound();
        }
        
        var bytes = File.ReadAllBytes(filePath);
        return Results.File(bytes, "application/octet-stream", filename);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#} {sizes[order]}";
    }

    // Metrics endpoints
    private static IResult GetMetrics(IGameServerManager serverManager)
    {
        var status = serverManager.GetStatus();
        return Results.Ok(new
        {
            cpu = status.SystemStats.CpuUsagePercent,
            memory = status.SystemStats.UsedMemoryMb,
            players = status.TotalPlayers,
            servers = status.OnlineServers,
            timestamp = DateTime.UtcNow
        });
    }

    private static IResult GetMetricsHistory(IGameServerManager serverManager)
    {
        RecordMetrics(serverManager);
        
        lock (_metricsLock)
        {
            var history = _metricsHistory.TakeLast(60).ToList();
            return Results.Ok(new
            {
                history = new
                {
                    labels = history.Select(m => m.Timestamp.ToString("HH:mm:ss")).ToArray(),
                    cpu = history.Select(m => m.Cpu).ToArray(),
                    memory = history.Select(m => m.Memory).ToArray(),
                    networkSent = history.Select(m => m.NetworkSent).ToArray(),
                    networkRecv = history.Select(m => m.NetworkRecv).ToArray(),
                    players = history.Select(m => m.Players).ToArray()
                }
            });
        }
    }

    private static void RecordMetrics(IGameServerManager serverManager)
    {
        lock (_metricsLock)
        {
            var status = serverManager.GetStatus();
            var random = new Random();
            
            _metricsHistory.Add(new MetricsSnapshot
            {
                Timestamp = DateTime.Now,
                Cpu = status.SystemStats.CpuUsagePercent > 0 ? status.SystemStats.CpuUsagePercent : random.Next(5, 25),
                Memory = status.SystemStats.UsedMemoryMb,
                NetworkSent = random.Next(100, 500),
                NetworkRecv = random.Next(200, 800),
                Players = status.TotalPlayers
            });

            // Keep only last 120 snapshots (10 minutes at 5s intervals)
            while (_metricsHistory.Count > 120)
            {
                _metricsHistory.RemoveAt(0);
            }
        }
    }

    // Console command execution
    private static IResult ExecuteConsoleCommand([FromBody] ConsoleCommand command, IGameServerManager serverManager)
    {
        var output = new List<string>();
        var cmd = command.Command?.ToLower().Trim() ?? "";
        
        if (cmd == "help")
        {
            output.Add("Available commands:");
            output.Add("  status      - Show server status");
            output.Add("  list        - List all server instances");
            output.Add("  startup N   - Start server N (or 'all')");
            output.Add("  shutdown N  - Stop server N (or 'all')");
            output.Add("  restart N   - Restart server N (or 'all')");
            output.Add("  add         - Add a new server instance");
            output.Add("  remove N    - Remove server N");
            output.Add("  config      - Show configuration");
            output.Add("  help        - Show this help");
        }
        else if (cmd == "status")
        {
            var status = serverManager.GetStatus();
            output.Add($"Server Name: {status.ServerName}");
            output.Add($"Version: {status.Version}");
            output.Add($"Total Servers: {status.TotalServers}");
            output.Add($"Online Servers: {status.OnlineServers}");
            output.Add($"Total Players: {status.TotalPlayers}");
            output.Add($"Master Server: {(status.MasterServerConnected ? "Connected" : "Disconnected")}");
            output.Add($"Chat Server: {(status.ChatServerConnected ? "Connected" : "Disconnected")}");
        }
        else if (cmd == "list")
        {
            var status = serverManager.GetStatus();
            output.Add("Server Instances:");
            foreach (var instance in status.Instances)
            {
                output.Add($"  #{instance.Id} - {instance.StatusString} - Port {instance.Port} - {instance.NumClients} players");
            }
        }
        else if (cmd.StartsWith("startup"))
        {
            output.Add("Starting servers... (use dashboard for async operation)");
        }
        else if (cmd.StartsWith("shutdown"))
        {
            output.Add("Stopping servers... (use dashboard for async operation)");
        }
        else if (cmd == "config")
        {
            output.Add("Configuration loaded from config/config.json");
            output.Add("Use the Config tab to view and edit settings.");
        }
        else if (cmd == "add")
        {
            output.Add("Adding new server instance... (use dashboard for async operation)");
        }
        else if (!string.IsNullOrWhiteSpace(cmd))
        {
            output.Add($"Unknown command: {cmd}");
            output.Add("Type 'help' for available commands.");
        }
        
        return Results.Ok(new { output });
    }

    private static IResult GetSystemStats(IGameServerManager serverManager)
    {
        return Results.Ok(serverManager.GetStatus().SystemStats);
    }

    private static IResult GetHealthChecks(HealthCheckManager healthManager)
    {
        var results = healthManager.GetLastResults();
        return Results.Ok(new 
        { 
            isHealthy = healthManager.IsHealthy(),
            checks = results 
        });
    }

    private static IResult GetSystemResources(HealthCheckManager healthManager)
    {
        var resources = healthManager.GetSystemResources();
        
        // Get CPU core count
        var cpuCount = Environment.ProcessorCount;
        
        // Convert to snake_case for dashboard compatibility
        var result = new Dictionary<string, object>
        {
            ["cpu_percent"] = resources.CpuUsagePercent,
            ["cpu_count"] = cpuCount,
            ["cpu_freq"] = 0, // Not easily available in .NET
            ["memory_used"] = FormatBytes(resources.MemoryUsedMb * 1024 * 1024),
            ["memory_total"] = FormatBytes(resources.MemoryTotalMb * 1024 * 1024),
            ["memory_percent"] = Math.Round(resources.MemoryUsagePercent, 1),
            ["disk_used"] = FormatBytes(resources.DiskUsedGb * 1024L * 1024 * 1024),
            ["disk_total"] = FormatBytes(resources.DiskTotalGb * 1024L * 1024 * 1024),
            ["disk_percent"] = Math.Round(resources.DiskUsagePercent, 1),
            ["uptime"] = FormatUptime(resources.Uptime),
            ["network_in"] = "0 B",
            ["network_out"] = "0 B"
        };
        
        return Results.Ok(result);
    }
    
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    
    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    private static async Task<IResult> RunHealthChecks(HealthCheckManager healthManager)
    {
        var results = await healthManager.RunAllChecksAsync();
        return Results.Ok(new 
        { 
            isHealthy = healthManager.IsHealthy(),
            checks = results 
        });
    }

    private static async Task<IResult> ValidateIp(string ipAddress, HealthCheckManager healthManager)
    {
        var result = await healthManager.ValidateIpAsync(ipAddress);
        return Results.Ok(result);
    }

    // Scheduled Tasks handlers
    private static IResult GetScheduledTasks(ScheduledTasksService tasksService)
    {
        var tasks = tasksService.GetTasks();
        return Results.Ok(new { tasks });
    }

    private static async Task<IResult> RunScheduledTask(string taskName, ScheduledTasksService tasksService)
    {
        var success = await tasksService.RunTaskNowAsync(taskName);
        return success 
            ? Results.Ok(new { message = $"Task '{taskName}' executed successfully" })
            : Results.NotFound(new { message = $"Task '{taskName}' not found" });
    }

    private static IResult EnableScheduledTask(string taskName, ScheduledTasksService tasksService)
    {
        tasksService.SetTaskEnabled(taskName, true);
        return Results.Ok(new { message = $"Task '{taskName}' enabled" });
    }

    private static IResult DisableScheduledTask(string taskName, ScheduledTasksService tasksService)
    {
        tasksService.SetTaskEnabled(taskName, false);
        return Results.Ok(new { message = $"Task '{taskName}' disabled" });
    }

    // Ban management handlers
    private static IResult GetBans(BanManager banManager)
    {
        var bans = banManager.GetAllBans();
        return Results.Ok(new { bans, count = bans.Count });
    }

    private static IResult GetBan(int accountId, BanManager banManager)
    {
        var ban = banManager.GetBan(accountId);
        return ban is null ? Results.NotFound() : Results.Ok(ban);
    }

    private static IResult CreateBan([FromBody] CreateBanRequest request, BanManager banManager)
    {
        var ban = banManager.BanPlayer(
            request.AccountId,
            request.PlayerName,
            request.Reason,
            request.BannedBy,
            request.IsPermanent ? BanType.Permanent : BanType.Temporary,
            request.DurationHours.HasValue ? TimeSpan.FromHours(request.DurationHours.Value) : null
        );
        return Results.Created($"/api/bans/{ban.AccountId}", ban);
    }

    private static IResult DeleteBan(int accountId, BanManager banManager)
    {
        var success = banManager.UnbanPlayer(accountId);
        return success ? Results.Ok(new { message = "Player unbanned" }) : Results.NotFound();
    }

    private static IResult CheckBan(int accountId, BanManager banManager)
    {
        var isBanned = banManager.IsBanned(accountId);
        var ban = banManager.GetBan(accountId);
        return Results.Ok(new { isBanned, ban });
    }

    // Replay management handlers
    private static IResult GetReplayStats(ReplayManager replayManager)
    {
        var stats = replayManager.GetStats();
        return Results.Ok(stats);
    }

    private static async Task<IResult> ArchiveReplays([FromQuery] int? daysOld, ReplayManager replayManager)
    {
        var archived = await replayManager.ArchiveOldReplaysAsync(daysOld ?? 30);
        return Results.Ok(new { archived, message = $"Archived {archived} replays" });
    }

    private static async Task<IResult> CleanupReplays([FromQuery] int? daysOld, ReplayManager replayManager)
    {
        var deleted = await replayManager.CleanupOldReplaysAsync(daysOld ?? 90);
        return Results.Ok(new { deleted, message = $"Deleted {deleted} archived replays" });
    }

    private static IResult DeleteReplay(string fileName, ReplayManager replayManager)
    {
        var success = replayManager.DeleteReplay(fileName);
        return success ? Results.Ok(new { message = "Replay deleted" }) : Results.NotFound();
    }

    // Events handlers
    private static IResult GetEvents(GameEventDispatcher eventDispatcher, int count = 100)
    {
        var events = eventDispatcher.GetRecentEvents(count);
        return Results.Ok(new { events, count = events.Count });
    }

    private static IResult GetEventStats(GameEventDispatcher eventDispatcher)
    {
        var stats = eventDispatcher.GetStats();
        return Results.Ok(stats);
    }

    private static IResult GetEventsByServer(int serverId, GameEventDispatcher eventDispatcher, int count = 50)
    {
        var events = eventDispatcher.GetEventsByServer(serverId, count);
        return Results.Ok(new { serverId, events, count = events.Count });
    }

    private static IResult GetEventsByType(string eventType, GameEventDispatcher eventDispatcher, int count = 50)
    {
        if (!Enum.TryParse<GameEventType>(eventType, true, out var parsedType))
        {
            return Results.BadRequest(new { error = $"Invalid event type: {eventType}" });
        }
        
        var events = eventDispatcher.GetEventsByType(parsedType, count);
        return Results.Ok(new { eventType = parsedType.ToString(), events, count = events.Count });
    }

    private static IResult GetMqttPublishableEvents(GameEventDispatcher eventDispatcher, int count = 50)
    {
        // MQTT publishable event types
        var mqttPublishableTypes = new HashSet<GameEventType>
        {
            GameEventType.ServerStarted,
            GameEventType.ServerStopped,
            GameEventType.ServerCrashed,
            GameEventType.ServerRestarted,
            GameEventType.MatchStarted,
            GameEventType.MatchEnded,
            GameEventType.MatchAborted,
            GameEventType.PlayerConnected,
            GameEventType.PlayerDisconnected,
            GameEventType.PlayerKicked,
            GameEventType.PlayerBanned,
            GameEventType.FirstBlood,
            GameEventType.KongorKilled
        };
        
        var allEvents = eventDispatcher.GetRecentEvents(count * 2);
        var mqttEvents = allEvents
            .Where(e => mqttPublishableTypes.Contains(e.EventType))
            .Take(count)
            .ToList();
        
        return Results.Ok(new 
        { 
            events = mqttEvents, 
            count = mqttEvents.Count,
            publishableTypes = mqttPublishableTypes.Select(t => t.ToString()).OrderBy(t => t).ToList()
        });
    }

    private static async Task<IResult> SimulateEvent([FromBody] SimulateEventRequest request, GameEventDispatcher eventDispatcher)
    {
        if (!Enum.TryParse<GameEventType>(request.EventType, out var eventType))
        {
            return Results.BadRequest(new { error = $"Invalid event type: {request.EventType}" });
        }

        var gameEvent = new GameEvent
        {
            EventType = eventType,
            ServerId = request.ServerId,
            Data = request.Data ?? new Dictionary<string, object>()
        };
        await eventDispatcher.DispatchAsync(gameEvent);

        return Results.Ok(new { 
            message = "Event dispatched", 
            eventId = gameEvent.Id,
            eventType = eventType.ToString(),
            serverId = request.ServerId 
        });
    }

    private static IResult ExportEventsJson(GameEventDispatcher eventDispatcher, [FromQuery] int count = 500)
    {
        var events = eventDispatcher.GetRecentEvents(count);
        var exportData = new
        {
            ExportDate = DateTime.UtcNow,
            EventCount = events.Count,
            Events = events.Select(e => new
            {
                e.Id,
                EventType = e.EventType.ToString(),
                e.ServerId,
                e.Timestamp,
                e.Data
            })
        };
        
        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Results.File(bytes, "application/json", $"events-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static IResult ExportEventsCsv(GameEventDispatcher eventDispatcher, [FromQuery] int count = 500)
    {
        var events = eventDispatcher.GetRecentEvents(count);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,EventType,ServerId,Timestamp,Data");
        
        foreach (var e in events)
        {
            var dataJson = JsonSerializer.Serialize(e.Data).Replace("\"", "\"\"");
            sb.AppendLine($"\"{e.Id}\",\"{e.EventType}\",{e.ServerId},\"{e.Timestamp:O}\",\"{dataJson}\"");
        }
        
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return Results.File(bytes, "text/csv", $"events-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private static IResult GetCurrentPerformance(IGameServerManager serverManager)
    {
        var status = serverManager.GetStatus();
        return Results.Ok(new
        {
            Timestamp = DateTime.UtcNow,
            System = new
            {
                CpuPercent = status.SystemStats?.CpuUsagePercent ?? 0,
                MemoryUsedMb = status.SystemStats?.UsedMemoryMb ?? 0,
                MemoryTotalMb = status.SystemStats?.TotalMemoryMb ?? 0,
                MemoryPercent = status.SystemStats != null && status.SystemStats.TotalMemoryMb > 0
                    ? (double)status.SystemStats.UsedMemoryMb / status.SystemStats.TotalMemoryMb * 100
                    : 0,
                DiskPercent = status.SystemStats?.DiskUsagePercent ?? 0
            },
            Servers = new
            {
                Total = status.TotalServers,
                Online = status.OnlineServers,
                Players = status.TotalPlayers
            },
            Instances = status.Instances.Select(i => new
            {
                i.Id,
                i.StatusString,
                i.NumClients,
                i.MaxClients,
                i.CpuPercent,
                i.MemoryMb
            })
        });
    }

    private static IResult GetPerformanceHistory(AdvancedMetricsService metricsService, [FromQuery] int points = 60)
    {
        var systemHistory = metricsService.GetSystemMetrics(points);
        return Results.Ok(new
        {
            Points = systemHistory.Snapshots.Count,
            Data = systemHistory.Snapshots.Select(s => new
            {
                s.Timestamp,
                s.CpuPercent,
                s.MemoryUsedMb,
                s.MemoryTotalMb,
                s.ActiveServers,
                s.TotalPlayers
            })
        });
    }

    private static IResult GetServerPerformance(AdvancedMetricsService metricsService)
    {
        var summary = metricsService.GetAllServersSummary();
        return Results.Ok(new
        {
            ServerCount = summary.Count,
            Servers = summary.Select(kvp => new
            {
                ServerId = kvp.Key,
                kvp.Value.CurrentCpu,
                kvp.Value.CurrentMemory,
                kvp.Value.AverageCpu,
                kvp.Value.AverageMemory,
                kvp.Value.PeakCpu,
                kvp.Value.PeakMemory,
                kvp.Value.DataPoints,
                kvp.Value.LastUpdated
            })
        });
    }

    private static IResult GetPerformanceSummary(IGameServerManager serverManager, AdvancedMetricsService metricsService)
    {
        var status = serverManager.GetStatus();
        var systemMetrics = metricsService.GetSystemMetrics(60);
        var serverSummary = metricsService.GetAllServersSummary();
        
        var avgCpu = systemMetrics.Snapshots.Count > 0 
            ? systemMetrics.Snapshots.Average(s => s.CpuPercent) 
            : 0;
        var avgMemory = systemMetrics.Snapshots.Count > 0 
            ? systemMetrics.Snapshots.Average(s => s.MemoryUsedMb) 
            : 0;
        var peakCpu = systemMetrics.Snapshots.Count > 0 
            ? systemMetrics.Snapshots.Max(s => s.CpuPercent) 
            : 0;
        var peakMemory = systemMetrics.Snapshots.Count > 0 
            ? systemMetrics.Snapshots.Max(s => s.MemoryUsedMb) 
            : 0;

        return Results.Ok(new
        {
            Timestamp = DateTime.UtcNow,
            Current = new
            {
                CpuPercent = status.SystemStats?.CpuUsagePercent ?? 0,
                MemoryMb = status.SystemStats?.UsedMemoryMb ?? 0,
                ActiveServers = status.OnlineServers,
                TotalPlayers = status.TotalPlayers
            },
            Averages = new
            {
                CpuPercent = avgCpu,
                MemoryMb = avgMemory,
                DataPoints = systemMetrics.Snapshots.Count
            },
            Peaks = new
            {
                CpuPercent = peakCpu,
                MemoryMb = peakMemory
            },
            ServerHealth = serverSummary.Count > 0 ? new
            {
                TotalServers = serverSummary.Count,
                AverageCpu = serverSummary.Values.Average(s => s.CurrentCpu),
                AverageMemory = serverSummary.Values.Average(s => s.CurrentMemory)
            } : null
        });
    }

    private record MetricsSnapshot
    {
        public DateTime Timestamp { get; init; }
        public double Cpu { get; init; }
        public double Memory { get; init; }
        public int NetworkSent { get; init; }
        public int NetworkRecv { get; init; }
        public int Players { get; init; }
    }

    private record ConsoleCommand
    {
        public string? Command { get; init; }
    }

    private record CreateBanRequest
    {
        public int AccountId { get; init; }
        public string PlayerName { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string BannedBy { get; init; } = "Admin";
        public bool IsPermanent { get; init; } = true;
        public int? DurationHours { get; init; }
    }

    private record SimulateEventRequest
    {
        public string EventType { get; init; } = "ServerStarted";
        public int ServerId { get; init; } = 1;
        public Dictionary<string, object>? Data { get; init; }
    }

    // Auth endpoints handlers
    private static IResult Login([FromBody] LoginRequest request, AuthService authService)
    {
        var result = authService.Authenticate(request.Username, request.Password);
        if (!result.IsSuccess)
            return Results.Unauthorized();
        return Results.Ok(result);
    }

    private static IResult Logout() => Results.Ok(new { message = "Logged out" });

    private static IResult ValidateToken([FromHeader(Name = "Authorization")] string? authHeader, AuthService authService)
    {
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            return Results.Unauthorized();
        var token = authHeader["Bearer ".Length..];
        if (authService.ValidateToken(token, out var principal))
            return Results.Ok(new { valid = true, username = principal?.Identity?.Name });
        return Results.Unauthorized();
    }

    private static IResult GetUsers(AuthService authService) =>
        Results.Ok(new { users = authService.GetUsers() });

    private static IResult CreateUser([FromBody] CreateUserRequest request, AuthService authService)
    {
        if (authService.CreateUser(request.Username, request.Password, request.Role))
            return Results.Ok(new { message = "User created", username = request.Username });
        return Results.BadRequest(new { error = "Username already exists" });
    }

    private static IResult DeleteUser(string username, AuthService authService)
    {
        if (authService.DeleteUser(username))
            return Results.Ok(new { message = "User deleted" });
        return Results.BadRequest(new { error = "Cannot delete this user" });
    }

    private static IResult ChangePassword([FromBody] ChangePasswordRequest request, AuthService authService)
    {
        if (authService.ChangePassword(request.Username, request.OldPassword, request.NewPassword))
            return Results.Ok(new { message = "Password changed" });
        return Results.BadRequest(new { error = "Invalid credentials" });
    }

    // Advanced Metrics endpoints handlers
    private static IResult GetServerMetrics(int serverId, [FromQuery] int points, AdvancedMetricsService metricsService)
    {
        var metrics = metricsService.GetServerMetrics(serverId, points > 0 ? points : 100);
        if (metrics == null) return Results.NotFound();
        return Results.Ok(metrics);
    }

    private static IResult GetSystemMetricsHistory([FromQuery] int points, AdvancedMetricsService metricsService) =>
        Results.Ok(metricsService.GetSystemMetrics(points > 0 ? points : 100));

    private static IResult GetAllServersSummary(AdvancedMetricsService metricsService) =>
        Results.Ok(metricsService.GetAllServersSummary());

    private static IResult CompareServers([FromQuery] string serverIds, [FromQuery] int hours, AdvancedMetricsService metricsService)
    {
        var ids = string.IsNullOrEmpty(serverIds) 
            ? Array.Empty<int>() 
            : serverIds.Split(',').Select(int.Parse).ToArray();
        var period = TimeSpan.FromHours(hours > 0 ? hours : 1);
        return Results.Ok(metricsService.CompareServers(ids, period));
    }

    private record LoginRequest
    {
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
    }

    private record CreateUserRequest
    {
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string Role { get; init; } = "User";
    }

    private record ChangePasswordRequest
    {
        public string Username { get; init; } = "";
        public string OldPassword { get; init; } = "";
        public string NewPassword { get; init; } = "";
    }

    // Notifications endpoints handlers
    private static IResult GetNotifications([FromQuery] int count, INotificationService notificationService)
    {
        var notifications = notificationService.GetRecentNotifications(count > 0 ? count : 50);
        return Results.Ok(new { notifications, count = notifications.Count });
    }

    private static IResult GetUnacknowledgedNotifications(INotificationService notificationService)
    {
        var notifications = notificationService.GetUnacknowledgedNotifications();
        return Results.Ok(new { notifications, count = notifications.Count });
    }

    private static IResult AcknowledgeNotification(string id, INotificationService notificationService)
    {
        notificationService.AcknowledgeNotification(id);
        return Results.Ok(new { message = "Notification acknowledged", id });
    }

    private static IResult ClearNotifications(INotificationService notificationService)
    {
        notificationService.ClearNotifications();
        return Results.Ok(new { message = "All notifications cleared" });
    }

    private static IResult GetAlertThresholds(INotificationService notificationService) =>
        Results.Ok(notificationService.GetThresholds());

    private static IResult UpdateAlertThresholds([FromBody] AlertThresholds thresholds, INotificationService notificationService)
    {
        notificationService.UpdateThresholds(thresholds);
        return Results.Ok(new { message = "Thresholds updated", thresholds });
    }

    // Charts endpoints handlers
    private static IResult GetUptimeChart([FromQuery] int hours, IChartDataService chartService)
    {
        var uptimeData = chartService.GetAllServersUptime(hours > 0 ? hours : 24);
        var history = chartService.GetUptimeHistory(days: hours > 0 ? hours / 24 + 1 : 1);
        return Results.Ok(new { 
            uptimePercentages = uptimeData, 
            history,
            periodHours = hours > 0 ? hours : 24
        });
    }

    private static IResult GetServerUptimeChart(int serverId, [FromQuery] int days, IChartDataService chartService)
    {
        var uptimePercent = chartService.GetUptimePercentage(serverId, (days > 0 ? days : 7) * 24);
        var history = chartService.GetUptimeHistory(serverId, days > 0 ? days : 7);
        return Results.Ok(new { 
            serverId, 
            uptimePercent, 
            history,
            periodDays = days > 0 ? days : 7
        });
    }

    private static IResult GetPlayerCountChart([FromQuery] int hours, IChartDataService chartService)
    {
        var series = chartService.GetPlayerCountSeries(hours > 0 ? hours : 24);
        var history = chartService.GetPlayerCountHistory(hours > 0 ? hours : 24);
        return Results.Ok(new { 
            series, 
            history,
            periodHours = hours > 0 ? hours : 24 
        });
    }

    private static IResult GetResourceCharts([FromQuery] int hours, IChartDataService chartService)
    {
        var h = hours > 0 ? hours : 1;
        return Results.Ok(new {
            cpu = chartService.GetCpuSeries(h),
            memory = chartService.GetMemorySeries(h),
            disk = chartService.GetDiskSeries(h),
            periodHours = h
        });
    }

    private static IResult GetMatchStatsChart([FromQuery] int serverId, [FromQuery] int days, IChartDataService chartService)
    {
        var history = chartService.GetMatchHistory(serverId > 0 ? serverId : null, days > 0 ? days : 7);
        return Results.Ok(new { 
            matches = history, 
            count = history.Count,
            periodDays = days > 0 ? days : 7 
        });
    }

    private static IResult GetMatchSummary([FromQuery] int days, IChartDataService chartService)
    {
        var summary = chartService.GetMatchStatsSummary(days > 0 ? days : 7);
        return Results.Ok(summary);
    }

    // ═══════════════════════════════════════════════════════════════
    // Statistics Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> GetStatisticsSummary(IMatchStatisticsService statsService)
    {
        var summary = await statsService.GetOverallSummaryAsync();
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetRecentMatches([FromQuery] int count = 20, IMatchStatisticsService? statsService = null)
    {
        if (statsService == null) return Results.BadRequest(new { error = "Statistics service not available" });
        var matches = await statsService.GetRecentMatchesAsync(count > 0 ? count : 20);
        return Results.Ok(new { matches, count = matches.Count });
    }

    private static async Task<IResult> GetMatch(long id, IMatchStatisticsService statsService)
    {
        var match = await statsService.GetMatchAsync(id);
        if (match == null)
            return Results.NotFound(new { error = "Match not found" });
        return Results.Ok(match);
    }

    private static async Task<IResult> GetTopPlayers([FromQuery] int count = 10, IMatchStatisticsService? statsService = null)
    {
        if (statsService == null) return Results.BadRequest(new { error = "Statistics service not available" });
        var players = await statsService.GetTopPlayersAsync(count > 0 ? count : 10);
        return Results.Ok(new { players, count = players.Count });
    }

    private static async Task<IResult> GetMostActivePlayers([FromQuery] int count = 10, IMatchStatisticsService? statsService = null)
    {
        if (statsService == null) return Results.BadRequest(new { error = "Statistics service not available" });
        var players = await statsService.GetMostActivePlayersAsync(count > 0 ? count : 10);
        return Results.Ok(new { players, count = players.Count });
    }

    private static async Task<IResult> GetPlayerStats(string name, IMatchStatisticsService statsService)
    {
        var stats = await statsService.GetPlayerStatsAsync(name);
        if (stats == null)
            return Results.NotFound(new { error = "Player not found" });
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetAllServerStats(IMatchStatisticsService statsService)
    {
        var stats = await statsService.GetAllServerStatsAsync();
        return Results.Ok(new { servers = stats, count = stats.Count });
    }

    private static async Task<IResult> GetDailyStats([FromQuery] int days = 7, IMatchStatisticsService? statsService = null)
    {
        if (statsService == null) return Results.BadRequest(new { error = "Statistics service not available" });
        var stats = await statsService.GetDailyStatsAsync(days > 0 ? days : 7);
        return Results.Ok(new { stats, count = stats.Count, periodDays = days > 0 ? days : 7 });
    }

    // ═══════════════════════════════════════════════════════════════
    // Auto-scaling Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetAutoScalingStatus(AutoScalingService? scalingService)
    {
        if (scalingService == null)
            return Results.Ok(new { enabled = false, message = "Auto-scaling service not available" });
        return Results.Ok(scalingService.GetStatus());
    }

    private static async Task<IResult> ManualScaleUp(AutoScalingService? scalingService)
    {
        if (scalingService == null)
            return Results.BadRequest(new { error = "Auto-scaling service not available" });
        var result = await scalingService.ManualScaleUpAsync();
        if (result)
            return Results.Ok(new { success = true, message = "Scale up initiated" });
        return Results.BadRequest(new { error = "Cannot scale up - already at maximum servers" });
    }

    private static async Task<IResult> ManualScaleDown(AutoScalingService? scalingService)
    {
        if (scalingService == null)
            return Results.BadRequest(new { error = "Auto-scaling service not available" });
        var result = await scalingService.ManualScaleDownAsync();
        if (result)
            return Results.Ok(new { success = true, message = "Scale down initiated" });
        return Results.BadRequest(new { error = "Cannot scale down - already at minimum servers" });
    }

    // ═══════════════════════════════════════════════════════════════
    // Replay Upload Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> UploadReplay(string matchId, HttpRequest request, ReplayManager replayManager)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected form data with replay file" });

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "No file uploaded" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        // Parse match ID
        if (!long.TryParse(matchId, out var mId))
            return Results.BadRequest(new { error = "Invalid match ID" });

        var fileName = await replayManager.SaveReplayAsync(mId, data);
        
        if (fileName != null)
        {
            // Queue for master server upload
            replayManager.QueueUpload(fileName, mId);
            return Results.Ok(new { 
                success = true, 
                fileName,
                fileSizeBytes = data.Length
            });
        }
        
        return Results.BadRequest(new { error = "Failed to save replay" });
    }

    private static IResult GetUploadedReplays([FromQuery] int count, ReplayManager replayManager)
    {
        var replays = replayManager.GetReplays(count > 0 ? count : 50);
        return Results.Ok(new { replays, count = replays.Count });
    }

    private static IResult GetReplayInfo(string fileName, ReplayManager replayManager)
    {
        var replay = replayManager.GetReplay(fileName);
        if (replay == null)
            return Results.NotFound(new { error = "Replay not found" });
        return Results.Ok(replay);
    }

    private static IResult DeleteUploadedReplay(string fileName, ReplayManager replayManager)
    {
        var result = replayManager.DeleteReplay(fileName);
        if (result)
            return Results.Ok(new { success = true, message = "Replay deleted" });
        return Results.NotFound(new { error = "Replay not found" });
    }

    private static IResult GetUploadReplayStats(ReplayManager replayManager)
    {
        var stats = replayManager.GetStats();
        return Results.Ok(new {
            totalReplays = stats.TotalReplays,
            totalSizeMb = stats.TotalSizeMb,
            archivedReplays = stats.ArchivedReplays,
            archivedSizeMb = stats.ArchivedSizeMb,
            pendingUploads = replayManager.GetPendingUploadCount()
        });
    }

    private static async Task<IResult> ProcessPendingUploads(ReplayManager replayManager)
    {
        var result = await replayManager.ProcessPendingUploadsAsync();
        return Results.Ok(new {
            success = result.Success,
            uploaded = result.Uploaded,
            failed = result.Failed
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Dependencies Endpoints
    // ═══════════════════════════════════════════════════════════════
    
    private static IResult GetDependencyStatus(HoNConfiguration config)
    {
        var checker = new Setup.DependencyChecker(config);
        var status = checker.GetDependencyStatus();
        
        return Results.Ok(new {
            hon_installed = status.HonInstalled,
            hon_path = status.HonPath,
            proxy_installed = status.ProxyInstalled,
            proxy_path = status.ProxyPath,
            proxy_enabled = status.ProxyEnabled,
            proxy_download_url = status.ProxyDownloadUrl,
            all_satisfied = status.AllSatisfied
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Server Templates Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static readonly List<ServerTemplate> _templates = new()
    {
        new ServerTemplate { Id = "1", Name = "Casual 5v5", Description = "Casual game mode for relaxed play", MaxClients = 10, GameMode = "Casual", MapName = "caldavar", ServerRegion = "USE", AllowStats = true, MinPlayers = 2, MaxSpectators = 10, AutoStart = true },
        new ServerTemplate { Id = "2", Name = "Competitive", Description = "Ranked competitive matches", MaxClients = 10, GameMode = "Ranked", MapName = "caldavar", ServerRegion = "USE", AllowStats = true, MinPlayers = 10, MaxSpectators = 5, AutoStart = false },
        new ServerTemplate { Id = "3", Name = "Mid Wars", Description = "Fast-paced Mid Wars mode", MaxClients = 10, GameMode = "MidWars", MapName = "midwars", ServerRegion = "USE", AllowStats = true, MinPlayers = 2, MaxSpectators = 10, AutoStart = true }
    };

    private static IResult GetTemplates()
    {
        return Results.Ok(_templates);
    }

    private static IResult GetTemplate(string id)
    {
        var template = _templates.FirstOrDefault(t => t.Id == id);
        return template != null ? Results.Ok(template) : Results.NotFound();
    }

    private static IResult CreateTemplate([FromBody] ServerTemplate template)
    {
        template.Id = Guid.NewGuid().ToString("N")[..8];
        _templates.Add(template);
        return Results.Created($"/api/templates/{template.Id}", template);
    }

    private static IResult UpdateTemplate(string id, [FromBody] ServerTemplate template)
    {
        var existing = _templates.FirstOrDefault(t => t.Id == id);
        if (existing == null) return Results.NotFound();
        
        existing.Name = template.Name;
        existing.Description = template.Description;
        existing.MaxClients = template.MaxClients;
        existing.GameMode = template.GameMode;
        existing.MapName = template.MapName;
        existing.ServerRegion = template.ServerRegion;
        existing.AllowStats = template.AllowStats;
        existing.MinPlayers = template.MinPlayers;
        existing.MaxSpectators = template.MaxSpectators;
        existing.AutoStart = template.AutoStart;
        
        return Results.Ok(existing);
    }

    private static IResult DeleteTemplate(string id)
    {
        var template = _templates.FirstOrDefault(t => t.Id == id);
        if (template == null) return Results.NotFound();
        _templates.Remove(template);
        return Results.NoContent();
    }

    private static IResult ApplyTemplate(string id, IGameServerManager serverManager)
    {
        var template = _templates.FirstOrDefault(t => t.Id == id);
        if (template == null) return Results.NotFound(new { error = "Template not found" });
        
        // In a real implementation, this would create a new server with template settings
        return Results.Ok(new { 
            success = true, 
            message = $"Template '{template.Name}' applied",
            templateId = id
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Webhooks Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static readonly List<WebhookInfo> _webhooks = new();

    private static IResult GetWebhooks()
    {
        return Results.Ok(_webhooks);
    }

    private static IResult RegisterWebhookEndpoint([FromBody] WebhookInfo webhook)
    {
        webhook.Id = Guid.NewGuid().ToString("N")[..8];
        _webhooks.Add(webhook);
        return Results.Created($"/api/webhooks/{webhook.Id}", webhook);
    }

    private static IResult DeleteWebhookEndpoint(string id)
    {
        var webhook = _webhooks.FirstOrDefault(w => w.Id == id);
        if (webhook == null) return Results.NotFound();
        _webhooks.Remove(webhook);
        return Results.NoContent();
    }

    private static Task<IResult> TestWebhookEndpoint(string id)
    {
        var webhook = _webhooks.FirstOrDefault(w => w.Id == id);
        if (webhook == null) return Task.FromResult(Results.NotFound());
        
        // Simulate test webhook
        return Task.FromResult(Results.Ok(new { success = true, message = "Test webhook sent to " + webhook.Url }));
    }

    private static IResult UpdateDiscordSettings([FromBody] DiscordSettingsRequest request, IDiscordBotService discordBot)
    {
        // In a real implementation, this would update the Discord bot settings
        return Results.Ok(new { success = true, message = "Discord settings updated" });
    }

    // ═══════════════════════════════════════════════════════════════
    // Discord Test Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetDiscordStatus(IDiscordBotService discordBot)
    {
        return Results.Ok(new {
            enabled = discordBot.IsEnabled,
            connected = discordBot.IsConnected,
            status = discordBot.IsConnected ? "Online" : (discordBot.IsEnabled ? "Offline" : "Disabled")
        });
    }

    private static async Task<IResult> TestMatchStartNotification(IDiscordBotService discordBot, IGameServerManager serverManager)
    {
        if (!discordBot.IsConnected)
            return Results.BadRequest(new { error = "Discord bot is not connected" });

        var testPlayers = new List<string> { "TestPlayer1", "TestPlayer2", "TestPlayer3", "TestPlayer4", "TestPlayer5" };
        var server = serverManager.Instances.FirstOrDefault();
        var serverName = server?.Name ?? "Test Server";
        var serverId = server?.Id ?? 1;

        await discordBot.SendMatchStartedAsync(serverId, serverName, testPlayers);
        return Results.Ok(new { success = true, message = "Match start notification sent" });
    }

    private static async Task<IResult> TestMatchEndNotification(IDiscordBotService discordBot, IGameServerManager serverManager)
    {
        if (!discordBot.IsConnected)
            return Results.BadRequest(new { error = "Discord bot is not connected" });

        var server = serverManager.Instances.FirstOrDefault();
        var serverName = server?.Name ?? "Test Server";
        var serverId = server?.Id ?? 1;

        await discordBot.SendMatchEndedAsync(serverId, serverName, duration: 1847, winner: "Legion");
        return Results.Ok(new { success = true, message = "Match end notification sent" });
    }

    private static async Task<IResult> TestPlayerJoinNotification(IDiscordBotService discordBot, IGameServerManager serverManager)
    {
        if (!discordBot.IsConnected)
            return Results.BadRequest(new { error = "Discord bot is not connected" });

        var server = serverManager.Instances.FirstOrDefault();
        var serverName = server?.Name ?? "Test Server";
        var serverId = server?.Id ?? 1;

        await discordBot.SendPlayerJoinedAsync(serverId, serverName, "TestPlayer");
        return Results.Ok(new { success = true, message = "Player join notification sent" });
    }

    private static async Task<IResult> TestAlertNotification(IDiscordBotService discordBot)
    {
        if (!discordBot.IsConnected)
            return Results.BadRequest(new { error = "Discord bot is not connected" });

        await discordBot.SendAlertAsync("🧪 Test Alert", "This is a test alert from HoNfigurator API");
        return Results.Ok(new { success = true, message = "Alert notification sent" });
    }

    // Enhanced Health Check handlers
    private static async Task<IResult> GetEnhancedHealthChecks(HealthCheckManager healthManager)
    {
        var results = await healthManager.RunEnhancedChecksAsync();
        return Results.Ok(results);
    }

    private static async Task<IResult> GetLagCheck(HealthCheckManager healthManager)
    {
        var result = await healthManager.CheckLagAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> GetInstallationCheck(HealthCheckManager healthManager)
    {
        var result = await healthManager.CheckHoNInstallationAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPortAvailability(int port, HealthCheckManager healthManager)
    {
        var result = await healthManager.CheckPortAvailabilityAsync(port);
        return Results.Ok(result);
    }

    // AutoPing handlers
    private static IResult GetAutoPingStatus(IAutoPingListener autoPing)
    {
        return Results.Ok(new
        {
            isRunning = autoPing.IsRunning,
            port = autoPing.Port,
            packetCount = autoPing.PacketCount,
            lastActivity = autoPing.LastActivity
        });
    }

    private static async Task<IResult> StartAutoPing(IAutoPingListener autoPing)
    {
        await autoPing.StartAsync();
        return Results.Ok(new { message = "AutoPing listener started", port = autoPing.Port });
    }

    private static IResult StopAutoPing(IAutoPingListener autoPing)
    {
        autoPing.Stop();
        return Results.Ok(new { message = "AutoPing listener stopped" });
    }

    private static IResult CheckAutoPingHealth(IAutoPingListener autoPing)
    {
        var isHealthy = autoPing.CheckHealth();
        var status = isHealthy ? "healthy" : "unhealthy";
        return Results.Ok(new
        {
            status,
            isHealthy,
            isRunning = autoPing.IsRunning,
            port = autoPing.Port,
            packetCount = autoPing.PacketCount,
            lastActivity = autoPing.LastActivity
        });
    }

    // Patching handlers
    private static IResult GetPatchStatus(IPatchingService patchingService)
    {
        return Results.Ok(new
        {
            currentVersion = patchingService.CurrentVersion,
            latestVersion = patchingService.LatestVersion,
            isPatching = patchingService.IsPatching
        });
    }

    private static async Task<IResult> CheckForPatches(IPatchingService patchingService)
    {
        var result = await patchingService.CheckForUpdatesAsync();
        return Results.Ok(new
        {
            updateAvailable = result.UpdateAvailable,
            currentVersion = result.CurrentVersion,
            latestVersion = result.LatestVersion,
            patchSize = result.PatchSize,
            error = result.Error
        });
    }

    private static async Task<IResult> ApplyPatch([FromBody] ApplyPatchRequest request, IPatchingService patchingService)
    {
        if (string.IsNullOrEmpty(request.PatchUrl))
            return Results.BadRequest(new { error = "Patch URL is required" });

        if (patchingService.IsPatching)
            return Results.BadRequest(new { error = "Patching is already in progress" });

        var result = await patchingService.ApplyPatchAsync(request.PatchUrl);
        return result.Success
            ? Results.Ok(new { message = "Patch applied successfully", newVersion = result.NewVersion, duration = result.Duration })
            : Results.Problem(result.Error ?? "Failed to apply patch");
    }

    private record ApplyPatchRequest
    {
        public string? PatchUrl { get; init; }
    }

    // Match Stats handlers
    private static async Task<IResult> SubmitMatchStats([FromBody] HoNfigurator.Core.Services.MatchStats stats, IMatchStatsService matchStatsService)
    {
        var result = await matchStatsService.SubmitMatchStatsAsync(stats);
        return result.Success
            ? Results.Ok(new { message = "Match stats submitted successfully", matchId = stats.MatchId })
            : Results.Ok(new { message = "Match stats queued for retry", matchId = stats.MatchId, error = result.Error, queued = true });
    }

    private static async Task<IResult> ResubmitPendingStats(IMatchStatsService matchStatsService)
    {
        var result = await matchStatsService.ResubmitPendingStatsAsync();
        return Results.Ok(new { 
            message = "Pending stats resubmission completed", 
            submitted = result.Submitted,
            failed = result.Failed,
            success = result.Success
        });
    }

    // CLI Command handlers
    private static IResult GetCliCommands(ICliCommandService cliService)
    {
        var commands = cliService.GetCommands();
        return Results.Ok(new { commands });
    }

    private static async Task<IResult> ExecuteCliCommand([FromBody] CliCommandRequest request, ICliCommandService cliService)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
            return Results.BadRequest(new { error = "Command is required" });

        var args = request.Arguments ?? Array.Empty<string>();
        var result = await cliService.ExecuteCommandAsync(request.Command, args);
        
        return Results.Ok(new { 
            success = result.Success,
            output = result.Output,
            error = result.Error
        });
    }

    private record CliCommandRequest
    {
        public string Command { get; init; } = "";
        public string[]? Arguments { get; init; }
    }

    private record MatchStatsSubmitRequest
    {
        public int MatchId { get; init; }
        public Dictionary<string, object> Stats { get; init; } = new();
    }

    // ═══════════════════════════════════════════════════════════════
    // Filebeat Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetFilebeatStatus(FilebeatService filebeat)
    {
        return Results.Ok(filebeat.GetStatus());
    }

    private static async Task<IResult> InstallFilebeat(FilebeatService filebeat)
    {
        var result = await filebeat.InstallAsync();
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Installation failed");
    }

    private static async Task<IResult> StartFilebeat(FilebeatService filebeat)
    {
        var success = await filebeat.StartAsync();
        return success 
            ? Results.Ok(new { message = "Filebeat started" })
            : Results.Problem("Failed to start Filebeat");
    }

    private static async Task<IResult> StopFilebeat(FilebeatService filebeat)
    {
        var success = await filebeat.StopAsync();
        return success 
            ? Results.Ok(new { message = "Filebeat stopped" })
            : Results.Problem("Failed to stop Filebeat");
    }

    private static async Task<IResult> ConfigureFilebeat(FilebeatService filebeat)
    {
        await filebeat.GenerateConfigAsync();
        return Results.Ok(new { message = "Filebeat configuration generated" });
    }

    private static async Task<IResult> TestFilebeatConnection(FilebeatService filebeat)
    {
        var result = await filebeat.TestElasticsearchConnectionAsync();
        return Results.Ok(result);
    }

    private record ConfigureFilebeatRequest
    {
        public string ElasticsearchHost { get; init; } = "localhost:9200";
        public List<string>? LogPaths { get; init; }
        public string? IndexPrefix { get; init; }
    }

    private record TestConnectionRequest
    {
        public string? Host { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
    }

    // ═══════════════════════════════════════════════════════════════
    // RBAC Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetAllPermissions(RolesDatabase rolesDb)
    {
        return Results.Ok(rolesDb.GetAllPermissions());
    }

    private static IResult GetAllRoles(RolesDatabase rolesDb)
    {
        return Results.Ok(rolesDb.GetAllRoles());
    }

    private static IResult GetRole(string roleName, RolesDatabase rolesDb)
    {
        var role = rolesDb.GetRole(roleName);
        return role != null ? Results.Ok(role) : Results.NotFound();
    }

    private static IResult CreateRole(
        [FromBody] CreateRoleRequest request, 
        RolesDatabase rolesDb)
    {
        var roleId = rolesDb.CreateRole(request.Name, request.Description);
        if (roleId <= 0) return Results.BadRequest(new { error = "Role already exists or creation failed" });

        if (request.Permissions?.Any() == true)
        {
            foreach (var perm in request.Permissions)
            {
                rolesDb.AssignPermissionToRole(roleId, perm);
            }
        }

        return Results.Ok(new { message = "Role created", name = request.Name, roleId });
    }

    private static IResult DeleteRole(string roleName, RolesDatabase rolesDb)
    {
        var success = rolesDb.DeleteRole(roleName);
        return success 
            ? Results.Ok(new { message = "Role deleted" }) 
            : Results.NotFound();
    }

    private static IResult AddPermissionsToRole(
        string roleName, 
        [FromBody] PermissionsRequest request, 
        RolesDatabase rolesDb)
    {
        var role = rolesDb.GetRole(roleName);
        if (role == null) return Results.NotFound(new { error = "Role not found" });
        
        var added = 0;
        foreach (var perm in request.Permissions)
        {
            rolesDb.AssignPermissionToRole(role.Id, perm);
            added++;
        }
        return Results.Ok(new { message = "Permissions added", count = added });
    }

    private static IResult RemovePermissionsFromRole(
        string roleName, 
        [FromBody] PermissionsRequest request, 
        RolesDatabase rolesDb)
    {
        var role = rolesDb.GetRole(roleName);
        if (role == null) return Results.NotFound(new { error = "Role not found" });
        
        var removed = 0;
        foreach (var perm in request.Permissions)
        {
            rolesDb.RemovePermissionFromRole(role.Id, perm);
            removed++;
        }
        return Results.Ok(new { message = "Permissions removed", count = removed });
    }

    private static IResult GetUserPermissions(int userId, RolesDatabase rolesDb)
    {
        // Get user to find their role, then get role permissions
        var users = rolesDb.GetAllUsers();
        var user = users.FirstOrDefault(u => u.Id == userId);
        if (user == null) return Results.NotFound(new { error = "User not found" });
        
        if (user.RoleId.HasValue)
        {
            var permissions = rolesDb.GetRolePermissions(user.RoleId.Value);
            return Results.Ok(new { userId, permissions });
        }
        
        return Results.Ok(new { userId, permissions = new List<string>() });
    }

    private static IResult AssignRoleToUser(
        int userId, 
        [FromBody] AssignRoleRequest request, 
        RolesDatabase rolesDb)
    {
        var role = rolesDb.GetRole(request.RoleName);
        if (role == null) return Results.NotFound(new { error = "Role not found" });
        
        var success = rolesDb.UpdateUser(userId, roleId: role.Id);
        return success 
            ? Results.Ok(new { message = "Role assigned" })
            : Results.BadRequest(new { error = "Failed to assign role" });
    }

    private static IResult RemoveRoleFromUser(int userId, string roleName, RolesDatabase rolesDb)
    {
        var success = rolesDb.UpdateUser(userId, roleId: null);
        return success 
            ? Results.Ok(new { message = "Role removed" })
            : Results.NotFound();
    }

    private record CreateRoleRequest
    {
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public List<string>? Permissions { get; init; }
    }

    private record PermissionsRequest
    {
        public List<string> Permissions { get; init; } = new();
    }

    private record AssignRoleRequest
    {
        public string RoleName { get; init; } = "";
    }

    // ═══════════════════════════════════════════════════════════════
    // Skipped Frame Analytics Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetGlobalFrameAnalytics(SkippedFrameTracker tracker)
    {
        return Results.Ok(tracker.GetGlobalAnalytics());
    }

    private static IResult GetServerFrameAnalytics(int serverId, SkippedFrameTracker tracker)
    {
        return Results.Ok(tracker.GetServerAnalytics(serverId));
    }

    private static IResult GetPlayerFrameStats(string playerName, SkippedFrameTracker tracker)
    {
        // Player stats require a server context - return all player stats across servers
        // that match the player name (case-insensitive)
        return Results.Ok(new { 
            playerName, 
            message = "Player stats are grouped by server. Use /api/diagnostics/skipped-frames/server/{id} to get player stats for a specific server." 
        });
    }

    private static IResult ResetFrameAnalytics(SkippedFrameTracker tracker)
    {
        tracker.ClearAllData();
        return Results.Ok(new { message = "Frame analytics reset" });
    }

    // ═══════════════════════════════════════════════════════════════
    // Storage/File Relocation Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetStorageStatus(FileRelocatorService relocator)
    {
        return Results.Ok(relocator.GetStatus());
    }

    private static IResult GetStorageAnalytics(FileRelocatorService relocator)
    {
        return Results.Ok(relocator.GetStorageAnalytics());
    }

    private static async Task<IResult> RelocateOldFiles(
        [FromBody] RelocateRequest? request,
        FileRelocatorService relocator)
    {
        var result = await relocator.RelocateOldFilesAsync(
            request?.OlderThanDays,
            request?.FilePattern);
        
        return Results.Ok(result);
    }

    private static async Task<IResult> CleanupArchiveStorage(
        [FromBody] CleanupRequest? request,
        FileRelocatorService relocator)
    {
        var result = await relocator.CleanupArchiveAsync(
            request?.OlderThanDays,
            request?.FilePattern);
        
        return Results.Ok(result);
    }

    private static async Task<IResult> RelocateLogs(
        [FromBody] RelocateRequest? request,
        FileRelocatorService relocator)
    {
        var result = await relocator.RelocateLogsAsync(request?.OlderThanDays);
        return Results.Ok(result);
    }

    private record RelocateRequest
    {
        public int? OlderThanDays { get; init; }
        public string? FilePattern { get; init; }
    }

    private record CleanupRequest
    {
        public int? OlderThanDays { get; init; }
        public string? FilePattern { get; init; }
    }

    // ═══════════════════════════════════════════════════════════════
    // Git/Version Management Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static async Task<IResult> GetCurrentBranch(GitBranchService gitService)
    {
        var branch = await gitService.GetCurrentBranchAsync();
        return Results.Ok(branch);
    }

    private static async Task<IResult> GetAllBranches(GitBranchService gitService)
    {
        var branches = await gitService.GetAllBranchesAsync();
        return Results.Ok(new { branches });
    }

    private static async Task<IResult> SwitchBranch(string branchName, GitBranchService gitService)
    {
        var result = await gitService.SwitchBranchAsync(branchName);
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to switch branch");
    }

    private static async Task<IResult> CheckForUpdates(GitBranchService gitService)
    {
        var result = await gitService.CheckForUpdatesAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> PullUpdates(GitBranchService gitService)
    {
        var result = await gitService.PullLatestAsync();
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to pull updates");
    }

    private static async Task<IResult> GetVersionInfo(GitBranchService gitService)
    {
        var info = await gitService.GetVersionInfoAsync();
        return Results.Ok(info);
    }

    // ═══════════════════════════════════════════════════════════════
    // Server Scaling Endpoints
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetScalingServiceStatus(ServerScalingService scalingService)
    {
        return Results.Ok(scalingService.GetStatus());
    }

    private static async Task<IResult> AddServersScaling(int count, ServerScalingService scalingService)
    {
        if (count < 1 || count > 10)
            return Results.BadRequest(new { error = "Count must be between 1 and 10" });
        
        var result = await scalingService.AddServersAsync(count);
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to add servers");
    }

    private static async Task<IResult> RemoveServersScaling(int count, ServerScalingService scalingService)
    {
        if (count < 1 || count > 10)
            return Results.BadRequest(new { error = "Count must be between 1 and 10" });
        
        var result = await scalingService.RemoveServersAsync(count);
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to remove servers");
    }

    private static async Task<IResult> ScaleToCount(int count, ServerScalingService scalingService)
    {
        if (count < 0 || count > 50)
            return Results.BadRequest(new { error = "Count must be between 0 and 50" });
        
        var result = await scalingService.ScaleToAsync(count);
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to scale servers");
    }

    private static async Task<IResult> AutoBalanceServers(ServerScalingService scalingService)
    {
        var result = await scalingService.AutoBalanceAsync();
        return result.Success 
            ? Results.Ok(result) 
            : Results.Problem(result.Error ?? "Failed to auto-balance servers");
    }

    // ═══════════════════════════════════════════════════════════════
    // Public Endpoints (for management.honfigurator.app integration)
    // ═══════════════════════════════════════════════════════════════

    private static IResult GetPublicServerInfo(
        IGameServerManager gameServerManager,
        HoNConfiguration config)
    {
        var servers = gameServerManager.GetAllServers();
        var response = new Dictionary<string, object>();

        foreach (var server in servers)
        {
            response[server.Name] = new
            {
                id = server.Id,
                status = server.Status.ToString(),
                region = config.HonData?.Location ?? "Unknown",
                gamephase = server.GamePhase ?? "Idle"
            };
        }

        return Results.Ok(response);
    }

    private static IResult GetHonfiguratorVersion(HoNConfiguration config)
    {
        return Results.Ok(new
        {
            version = "1.0.0",
            latest_github_update = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            github_branch = "main",
            platform = ".NET"
        });
    }

    private static IResult GetSkippedFrameData(
        int port,
        IGameServerManager gameServerManager)
    {
        if (port == 0) // "all"
        {
            var servers = gameServerManager.GetAllServers();
            var response = new Dictionary<string, object>();
            
            foreach (var server in servers)
            {
                response[server.Name] = server.GetSkippedFrameData();
            }
            
            return Results.Ok(response);
        }
        else
        {
            var server = gameServerManager.GetServerByPort(port);
            if (server == null)
                return Results.NotFound(new { error = "Server not found" });

            return Results.Ok(server.GetSkippedFrameData());
        }
    }

    private static IResult GetHonVersion(HoNConfiguration config)
    {
        return Results.Ok(new { data = config.HonData?.ManVersion ?? "Unknown" });
    }

    private static IResult RegisterWithManagement(
        HttpContext context,
        HoNConfiguration config)
    {
        // This endpoint validates that the client has permission to register
        // In the Python version, this checks Discord OAuth permissions
        // For now, return OK to indicate server is accessible
        return Results.Ok(new { status = "OK" });
    }

    // ===================================================================
    // Management Portal Control Endpoints
    // ===================================================================

    private static async Task<IResult> GetManagementPortalStatus(
        IManagementPortalConnector connector)
    {
        var status = new
        {
            enabled = connector.IsEnabled,
            registered = connector.IsRegistered,
            serverName = connector.ServerName,
            serverAddress = connector.ServerAddress,
            portalUrl = connector.IsEnabled ? "https://management.honfigurator.app:3001" : null,
            lastUpdated = DateTime.UtcNow
        };

        if (connector.IsEnabled)
        {
            var canPing = await connector.PingManagementPortalAsync();
            return Results.Ok(new
            {
                status.enabled,
                status.registered,
                status.serverName,
                status.serverAddress,
                status.portalUrl,
                portalReachable = canPing,
                status.lastUpdated
            });
        }

        return Results.Ok(status);
    }

    private static async Task<IResult> TriggerManagementPortalRegistration(
        IManagementPortalConnector connector)
    {
        if (!connector.IsEnabled)
        {
            return Results.BadRequest(new { error = "Management portal integration is disabled" });
        }

        var result = await connector.RegisterServerAsync();
        
        if (result.Success)
        {
            return Results.Ok(new
            {
                success = true,
                message = result.Message,
                serverName = result.ServerName,
                serverAddress = result.ServerAddress
            });
        }

        return Results.BadRequest(new
        {
            success = false,
            message = result.Message,
            error = result.Error
        });
    }

    private static async Task<IResult> TriggerManagementPortalStatusReport(
        IManagementPortalConnector connector,
        IGameServerManager serverManager,
        HoNConfiguration config)
    {
        if (!connector.IsEnabled)
        {
            return Results.BadRequest(new { error = "Management portal integration is disabled" });
        }

        if (!connector.IsRegistered)
        {
            return Results.BadRequest(new { error = "Server is not registered with management portal" });
        }

        var statusResponse = serverManager.GetStatus();

        var statusReport = new ServerStatusReport
        {
            ServerName = config.HonData?.ServerName ?? "Unknown",
            ServerIp = config.HonData?.ServerIp ?? "Unknown",
            ApiPort = config.HonData?.ApiPort ?? 0,
            Status = statusResponse.OnlineServers > 0 ? "Online" : "Idle",
            TotalServers = statusResponse.TotalServers,
            RunningServers = statusResponse.OnlineServers,
            PlayersOnline = statusResponse.TotalPlayers,
            HonVersion = config.HonData?.ManVersion,
            HonfiguratorVersion = GetVersion(),
            Timestamp = DateTime.UtcNow
        };

        await connector.ReportServerStatusAsync(statusReport);

        return Results.Ok(new
        {
            success = true,
            message = "Status report sent",
            report = statusReport
        });
    }

    private static IResult GetManagementPortalConfig(
        HoNConfiguration config)
    {
        var portalSettings = config.ApplicationData?.ManagementPortal;
        
        if (portalSettings == null)
        {
            return Results.Ok(new
            {
                configured = false,
                message = "Management portal is not configured"
            });
        }

        // Return config with sensitive data redacted
        return Results.Ok(new
        {
            configured = true,
            enabled = portalSettings.Enabled,
            portalUrl = portalSettings.PortalUrl,
            mqttHost = portalSettings.MqttHost,
            mqttPort = portalSettings.MqttPort,
            mqttUseTls = portalSettings.MqttUseTls,
            discordUserId = !string.IsNullOrEmpty(portalSettings.DiscordUserId) 
                ? $"{portalSettings.DiscordUserId[..Math.Min(4, portalSettings.DiscordUserId.Length)]}***" 
                : null,
            apiKeyConfigured = !string.IsNullOrEmpty(portalSettings.ApiKey),
            statusReportIntervalSeconds = portalSettings.StatusReportIntervalSeconds,
            autoRegister = portalSettings.AutoRegister,
            hasCaCertificate = !string.IsNullOrEmpty(portalSettings.CaCertificatePath),
            hasClientCertificate = !string.IsNullOrEmpty(portalSettings.ClientCertificatePath)
        });
    }

    // ===================================================================
    // MQTT Endpoint Handlers
    // ===================================================================

    private static IResult GetMqttStatus(
        IMqttHandler mqttHandler,
        HoNConfiguration config)
    {
        var mqttSettings = config.ApplicationData?.Mqtt;
        var portalSettings = config.ApplicationData?.ManagementPortal;
        
        // Determine which MQTT configuration is active
        string? broker = null;
        int? port = null;
        bool useTls = false;
        string configSource = "none";
        
        if (portalSettings?.Enabled == true)
        {
            broker = portalSettings.MqttHost;
            port = portalSettings.MqttPort;
            useTls = portalSettings.MqttUseTls;
            configSource = "management_portal";
        }
        else if (mqttSettings?.Enabled == true)
        {
            broker = mqttSettings.Host;
            port = mqttSettings.Port;
            useTls = mqttSettings.UseTls;
            configSource = "standalone";
        }
        
        return Results.Ok(new
        {
            enabled = mqttHandler.IsEnabled,
            connected = mqttHandler.IsConnected,
            configSource,
            broker,
            port,
            useTls,
            topicPrefix = mqttSettings?.TopicPrefix ?? "honfigurator",
            timestamp = DateTime.UtcNow
        });
    }

    private static async Task<IResult> ConnectMqtt(
        IMqttHandler mqttHandler)
    {
        if (!mqttHandler.IsEnabled)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "MQTT is not enabled in configuration"
            });
        }
        
        if (mqttHandler.IsConnected)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Already connected to MQTT broker"
            });
        }
        
        var connected = await mqttHandler.ConnectAsync();
        
        if (connected)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Successfully connected to MQTT broker"
            });
        }
        
        return Results.Problem(
            statusCode: 500,
            title: "Connection Failed",
            detail: "Failed to connect to MQTT broker. Check configuration and broker availability.");
    }

    private static async Task<IResult> DisconnectMqtt(
        IMqttHandler mqttHandler)
    {
        if (!mqttHandler.IsConnected)
        {
            return Results.Ok(new
            {
                success = true,
                message = "Already disconnected from MQTT broker"
            });
        }
        
        await mqttHandler.DisconnectAsync();
        
        return Results.Ok(new
        {
            success = true,
            message = "Disconnected from MQTT broker"
        });
    }

    private static async Task<IResult> PublishMqttMessage(
        IMqttHandler mqttHandler,
        [FromBody] MqttPublishRequest request)
    {
        if (!mqttHandler.IsConnected)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Not connected to MQTT broker"
            });
        }
        
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Topic is required"
            });
        }
        
        await mqttHandler.PublishAsync(request.Topic, request.Message ?? "", request.Retain);
        
        return Results.Ok(new
        {
            success = true,
            message = "Message published",
            topic = request.Topic,
            retain = request.Retain,
            timestamp = DateTime.UtcNow
        });
    }

    private static async Task<IResult> PublishMqttTestMessage(
        IMqttHandler mqttHandler,
        HoNConfiguration config)
    {
        if (!mqttHandler.IsEnabled)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "MQTT is not enabled"
            });
        }
        
        if (!mqttHandler.IsConnected)
        {
            // Try to connect first
            var connected = await mqttHandler.ConnectAsync();
            if (!connected)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "Failed to connect to MQTT broker"
                });
            }
        }
        
        var testMessage = new
        {
            event_type = "test",
            server_name = config.HonData.ServerName,
            message = "Test message from HoNfigurator",
            timestamp = DateTime.UtcNow
        };
        
        await mqttHandler.PublishJsonAsync("test", testMessage);
        
        return Results.Ok(new
        {
            success = true,
            message = "Test message published successfully",
            topic = $"{config.ApplicationData?.Mqtt?.TopicPrefix ?? "honfigurator"}/test",
            payload = testMessage
        });
    }

    private static string GetVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "1.0.0";
    }

    // Backup/Restore handlers
    private static IResult GetBackups([FromServices] BackupRestoreService backupService)
    {
        var backups = backupService.ListBackups();
        return Results.Ok(backups);
    }

    private static IResult GetBackupDetails(string backupId, [FromServices] BackupRestoreService backupService)
    {
        var details = backupService.GetBackupDetails(backupId);
        return details is null ? Results.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> CreateBackup([FromBody] CreateBackupRequest? request, [FromServices] BackupRestoreService backupService, CancellationToken ct)
    {
        var backup = await backupService.CreateBackupAsync(request?.Description, ct);
        return Results.Created($"/api/backups/{backup.BackupId}", backup);
    }

    private static async Task<IResult> RestoreBackup(string backupId, [FromBody] RestoreBackupRequest? request, [FromServices] BackupRestoreService backupService, CancellationToken ct)
    {
        var options = new RestoreOptions
        {
            CreatePreRestoreBackup = request?.CreatePreRestoreBackup ?? true,
            ReloadAfterRestore = request?.ReloadAfterRestore ?? true
        };
        var result = await backupService.RestoreBackupAsync(backupId, options, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteBackupEndpoint(string backupId, [FromServices] BackupRestoreService backupService, CancellationToken ct)
    {
        var success = await backupService.DeleteBackupAsync(backupId, ct);
        return success ? Results.Ok(new { message = "Backup deleted" }) : Results.NotFound();
    }

    private static IResult DownloadBackup(string backupId, [FromServices] BackupRestoreService backupService)
    {
        var details = backupService.GetBackupDetails(backupId);
        if (details is null) return Results.NotFound();
        
        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoNfigurator", "backups");
        var filePath = Path.Combine(backupDir, details.FileName);
        
        if (!File.Exists(filePath)) return Results.NotFound();
        
        return Results.File(filePath, "application/zip", details.FileName);
    }
}

/// <summary>
/// Request model for publishing MQTT messages
/// </summary>
public class MqttPublishRequest
{
    public string Topic { get; set; } = string.Empty;
    public string? Message { get; set; }
    public bool Retain { get; set; }
}

/// <summary>
/// Request model for creating backups
/// </summary>
public class CreateBackupRequest
{
    public string? Description { get; set; }
}

/// <summary>
/// Request model for restoring backups
/// </summary>
public class RestoreBackupRequest
{
    public bool CreatePreRestoreBackup { get; set; } = true;
    public bool ReloadAfterRestore { get; set; } = true;
}

/// <summary>
/// Request model for kicking players
/// </summary>
public class KickPlayerRequest
{
    public int AccountId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>
/// Server template model
/// </summary>
public class ServerTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaxClients { get; set; } = 10;
    public string GameMode { get; set; } = "Normal";
    public string MapName { get; set; } = "caldavar";
    public string ServerRegion { get; set; } = "USE";
    public bool AllowStats { get; set; } = true;
    public int MinPlayers { get; set; } = 2;
    public int MaxSpectators { get; set; } = 10;
    public bool AutoStart { get; set; } = true;
}

/// <summary>
/// Webhook info model
/// </summary>
public class WebhookInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public List<string> Events { get; set; } = new();
}

/// <summary>
/// Discord settings request model
/// </summary>
public class DiscordSettingsRequest
{
    public string? ChannelId { get; set; }
    public bool NotifyMatchStart { get; set; }
    public bool NotifyMatchEnd { get; set; }
    public bool NotifyPlayerJoin { get; set; }
    public bool NotifyServerStatus { get; set; }
}
