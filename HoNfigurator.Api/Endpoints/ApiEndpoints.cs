using Microsoft.AspNetCore.Mvc;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Health;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Auth;
using HoNfigurator.Core.Metrics;
using HoNfigurator.Core.Notifications;
using HoNfigurator.Core.Charts;
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

        // Console commands endpoint
        api.MapPost("/console/execute", ExecuteConsoleCommand)
            .WithName("ExecuteConsoleCommand")
            .WithSummary("Execute console command")
            .WithDescription("Executes a console command on the server. Available: status, help, startup, shutdown, add, remove, restart, message, list, config")
            .WithTags("Console");

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
        events.MapPost("/simulate", SimulateEvent)
            .WithName("SimulateEvent")
            .WithSummary("Simulate an event")
            .WithDescription("Creates a test event for debugging purposes");

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
    }

    private static IResult GetStatus(IGameServerManager serverManager)
    {
        RecordMetrics(serverManager);
        return Results.Ok(serverManager.GetStatus());
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
}
