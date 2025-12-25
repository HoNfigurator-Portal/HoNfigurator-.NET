using HoNfigurator.Api.Endpoints;
using HoNfigurator.Api.Hubs;
using HoNfigurator.Api.Services;
using HoNfigurator.Core.Auth;
using HoNfigurator.Core.Connectors;
using HoNfigurator.Core.Discord;
using HoNfigurator.Core.Events;
using HoNfigurator.Core.Health;
using HoNfigurator.Core.Metrics;
using HoNfigurator.Core.Notifications;
using HoNfigurator.Core.Charts;
using HoNfigurator.Core.Parsing;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Statistics;
using HoNfigurator.GameServer.Services;
using Scalar.AspNetCore;
using HoNfigurator.Core.Models;
using HoNfigurator.Api.Setup;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to respect JsonPropertyName attributes
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null; // Use property names as-is (with JsonPropertyName)
    options.SerializerOptions.WriteIndented = false;
});

// Configuration
var configPath = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
var configService = new ConfigurationService(configPath);
await configService.LoadConfigurationAsync();

// Run setup wizard if needed
var setupWizard = new SetupWizard(configService, configService.Configuration);
if (setupWizard.IsSetupRequired())
{
    await setupWizard.RunAsync();
    // Reload config after setup
    await configService.LoadConfigurationAsync();
}

// Store config for service registration
var config = configService.Configuration;

// Check dependencies
var dependencyChecker = new DependencyChecker(config);
await dependencyChecker.CheckAllDependenciesAsync();

// Add services
builder.Services.AddSingleton<IConfigurationService>(configService);
builder.Services.AddSingleton(config); // Register HoNConfiguration for injection

// Register connectors with interfaces
builder.Services.AddSingleton<IMasterServerConnector>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MasterServerConnector>>();
    var masterServerUrl = config.MasterServerUrl ?? "http://api.kongor.net";
    var version = config.ManVersion ?? "4.10.1";
    return new MasterServerConnector(logger, masterServerUrl, version);
});

builder.Services.AddSingleton<IChatServerConnector>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ChatServerConnector>>();
    return new ChatServerConnector(logger);
});

// Register GameServerManager
builder.Services.AddSingleton<IGameServerManager>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<GameServerManager>>();
    var manager = new GameServerManager(
        logger,
        config.HonData.HonInstallDirectory,
        config.HonData.ServerName
    );

    // Pass configuration to manager
    manager.Configuration = config;

    // Initialize with configured servers
    var totalServers = config.HonData.TotalServers > 0
        ? config.HonData.TotalServers
        : 0; // Don't auto-create servers, let user add them

    manager.Initialize(
        totalServers,
        config.HonData.StartingGamePort,
        config.HonData.StartingVoicePort
    );

    return manager;
});

// Add SignalR
builder.Services.AddSignalR();

// Add HTTP client for external services
builder.Services.AddHttpClient();

// Register GameLogReader for reading game log files
builder.Services.AddSingleton<IGameLogReader, GameLogReader>();

// Register ProxyService for managing proxy processes
builder.Services.AddSingleton<IProxyService, ProxyService>();

// Register MQTT Handler for publishing events
builder.Services.AddSingleton<IMqttHandler>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MqttHandler>>();
    var config = sp.GetRequiredService<HoNConfiguration>();
    return new MqttHandler(logger, config);
});

// Register Discord Bot Service
builder.Services.AddSingleton<IDiscordBotService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<DiscordBotService>>();
    var config = sp.GetRequiredService<HoNConfiguration>();
    var mqttHandler = sp.GetRequiredService<IMqttHandler>();
    return new DiscordBotService(logger, config, mqttHandler);
});

// Register match statistics service (must be before GameServerListener)
builder.Services.AddSingleton<IMatchStatisticsService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<MatchStatisticsService>>();
    return new MatchStatisticsService(logger);
});

// Register replay upload service (must be before GameServerListener)
builder.Services.AddSingleton<IReplayUploadService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ReplayUploadService>>();
    var config = sp.GetRequiredService<HoNConfiguration>();
    return new ReplayUploadService(logger, config);
});

// Register GameServerListener for receiving status updates from game servers
builder.Services.AddSingleton<IGameServerListener>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<GameServerListener>>();
    var serverManager = sp.GetRequiredService<IGameServerManager>();
    var logReader = sp.GetRequiredService<IGameLogReader>();
    var config = sp.GetRequiredService<HoNConfiguration>();
    var mqttHandler = sp.GetRequiredService<IMqttHandler>();
    var discordBot = sp.GetRequiredService<IDiscordBotService>();
    var statisticsService = sp.GetRequiredService<IMatchStatisticsService>();
    var replayUploadService = sp.GetRequiredService<IReplayUploadService>();
    return new GameServerListener(logger, serverManager, logReader, config, mqttHandler, 
        discordBot, statisticsService, replayUploadService);
});

// Register core services
builder.Services.AddSingleton<HealthCheckManager>(sp => new HealthCheckManager(sp.GetRequiredService<ILogger<HealthCheckManager>>(), sp.GetRequiredService<HoNConfiguration>()));
builder.Services.AddSingleton<MatchParser>();
builder.Services.AddSingleton<ScheduledTasksService>();
builder.Services.AddSingleton<CowMasterService>();
builder.Services.AddSingleton<GameEventDispatcher>();
builder.Services.AddSingleton<ReplayManager>();
builder.Services.AddSingleton<BanManager>();
builder.Services.AddSingleton<AuthService>(sp => new AuthService(sp.GetRequiredService<HoNConfiguration>()));
builder.Services.AddSingleton<AdvancedMetricsService>();

// Register notification and chart services
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<IChartDataService, ChartDataService>();

// Add hosted services
builder.Services.AddHostedService<StatusBroadcastService>();
builder.Services.AddHostedService<HealthCheckBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScheduledTasksService>());
builder.Services.AddHostedService<NotificationBroadcastService>();

// Add ConnectionManagerService for auto-connect to MasterServer/ChatServer
builder.Services.AddHostedService<ConnectionManagerService>();

// Register Auto-scaling service (also as hosted service for background processing)
builder.Services.AddSingleton<AutoScalingService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AutoScalingService>>();
    var serverManager = sp.GetRequiredService<IGameServerManager>();
    var config = sp.GetRequiredService<HoNConfiguration>();
    return new AutoScalingService(logger, serverManager, config);
});
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoScalingService>());

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add OpenAPI with Scalar UI
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info.Title = "HoNfigurator API";
        document.Info.Version = "1.0.0";
        document.Info.Description = """
            ## HoNfigurator - Heroes of Newerth Server Manager API

            A high-performance game server management system built with .NET 10.

            ### Features
            - ๐ŸŽฎ **Server Management** - Start, stop, restart game servers
            - ๐Ÿ"Š **Metrics & Monitoring** - Real-time system metrics and health checks
            - ๐Ÿ"' **Authentication** - JWT-based user authentication
            - ๐Ÿ" **Logging** - Comprehensive logging and log retrieval
            - โšก **Events** - Real-time game event tracking
            - ๐Ÿšซ **Ban Management** - Player ban system
            - ๐ŸŽฌ **Replay Management** - Game replay storage and retrieval
            - โฐ **Scheduled Tasks** - Automated maintenance tasks

            ### Authentication
            Use the `/api/auth/login` endpoint to obtain a JWT token.
            Default credentials: `admin` / `admin`
            """;
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Initialize statistics database
var statisticsService = app.Services.GetRequiredService<IMatchStatisticsService>();
await statisticsService.InitializeAsync();
Console.WriteLine("    Statistics database initialized");

// Configure the HTTP request pipeline
// Enable OpenAPI + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("HoNfigurator API")
        .WithTheme(ScalarTheme.BluePlanet)
        .WithDarkMode(true)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithSidebar(true)
        .WithSearchHotKey("k");
});

app.UseCors();

// Serve static files (dashboard)
app.UseStaticFiles();

// Map SignalR hub
app.MapHub<DashboardHub>("/hubs/dashboard");

// Map API endpoints
app.MapApiEndpoints();

// Map root to index.html, handle login page
app.MapGet("/", () => Results.File(
    Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "index.html"),
    "text/html"));

// Run
var port = configService.Configuration.HonData.ApiPort;
app.Urls.Add($"http://0.0.0.0:{port}");
app.Urls.Add($"https://0.0.0.0:{port + 1}");

Console.WriteLine($"""

    ╔═══════════════════════════════════════════════════════════╗
    ║           HoNfigurator - .NET 10 Edition                  ║
    ║═══════════════════════════════════════════════════════════║
    ║  API Server running on:                                   ║
    ║    HTTP:  http://localhost:{port,-5}                         ║
    ║    HTTPS: https://localhost:{port + 1,-5}                        ║
    ║                                                           ║
    ║  Dashboard:   http://localhost:{port,-5}                     ║
    ║  API Docs:    http://localhost:{port}/scalar/v1              ║
    ║  OpenAPI:     http://localhost:{port}/openapi/v1.json        ║
    ║  SignalR Hub: /hubs/dashboard                             ║
    ╚═══════════════════════════════════════════════════════════╝  

    """);

// Start GameServerListener to receive status updates from game servers
var managerPort = configService.Configuration.HonData.ManagerPort;
if (managerPort > 0)
{
    var listener = app.Services.GetRequiredService<IGameServerListener>();
    var serverManager = app.Services.GetRequiredService<IGameServerManager>();
    
    // Connect listener to server manager for graceful shutdown support
    serverManager.SetListener(listener);
    
    await listener.StartAsync(managerPort);
    Console.WriteLine($"    GameServer Listener on port {managerPort}");
}

// Start MQTT connection if enabled
var mqttHandler = app.Services.GetRequiredService<IMqttHandler>();
if (mqttHandler.IsEnabled)
{
    var connected = await mqttHandler.ConnectAsync();
    if (connected)
    {
        Console.WriteLine($"    MQTT connected to {config.ApplicationData?.Mqtt?.Host}:{config.ApplicationData?.Mqtt?.Port}");
    }
    else
    {
        Console.WriteLine("    MQTT connection failed (check configuration)");
    }
}
else
{
    Console.WriteLine("    MQTT disabled (enable in config to use)");
}

// Start Discord bot if enabled
var discordBot = app.Services.GetRequiredService<IDiscordBotService>();
if (discordBot.IsEnabled)
{
    await discordBot.StartAsync();
    Console.WriteLine("    Discord bot starting...");
}
else
{
    Console.WriteLine("    Discord bot disabled (set bot_token in config to use)");
}

app.Run();





