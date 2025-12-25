using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Health;

/// <summary>
/// Monitors server listener health via UDP ping and provides self-healing capabilities.
/// Port of Python HoNfigurator-Central autoping functionality.
/// </summary>
public class AutoPingHealthService : IDisposable
{
    private readonly ILogger<AutoPingHealthService> _logger;
    private readonly HoNConfiguration _config;
    private readonly Dictionary<int, ServerHealthState> _serverHealthStates = new();
    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    /// <summary>
    /// Event raised when a server listener is detected as unhealthy
    /// </summary>
    public event EventHandler<ServerUnhealthyEventArgs>? ServerUnhealthy;

    /// <summary>
    /// Event raised when a server listener recovers
    /// </summary>
    public event EventHandler<ServerRecoveredEventArgs>? ServerRecovered;

    /// <summary>
    /// Event raised when server restart is recommended
    /// </summary>
    public event EventHandler<RestartRecommendedEventArgs>? RestartRecommended;

    public AutoPingHealthService(ILogger<AutoPingHealthService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Start monitoring all configured servers
    /// </summary>
    public void StartMonitoring(IEnumerable<int> serverPorts)
    {
        if (_cts != null)
        {
            _logger.LogWarning("AutoPing monitoring already running");
            return;
        }

        _cts = new CancellationTokenSource();

        lock (_stateLock)
        {
            _serverHealthStates.Clear();
            foreach (var port in serverPorts)
            {
                _serverHealthStates[port] = new ServerHealthState { Port = port };
            }
        }

        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        _logger.LogInformation("AutoPing health monitoring started for {Count} servers", 
            _serverHealthStates.Count);
    }

    /// <summary>
    /// Stop monitoring
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        
        if (_monitorTask != null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts.Dispose();
        _cts = null;
        _monitorTask = null;

        _logger.LogInformation("AutoPing health monitoring stopped");
    }

    /// <summary>
    /// Add a server to monitoring
    /// </summary>
    public void AddServer(int port)
    {
        lock (_stateLock)
        {
            if (!_serverHealthStates.ContainsKey(port))
            {
                _serverHealthStates[port] = new ServerHealthState { Port = port };
                _logger.LogDebug("Added server port {Port} to health monitoring", port);
            }
        }
    }

    /// <summary>
    /// Remove a server from monitoring
    /// </summary>
    public void RemoveServer(int port)
    {
        lock (_stateLock)
        {
            _serverHealthStates.Remove(port);
            _logger.LogDebug("Removed server port {Port} from health monitoring", port);
        }
    }

    /// <summary>
    /// Get health status for all monitored servers
    /// </summary>
    public IReadOnlyDictionary<int, ServerHealthStatus> GetHealthStatus()
    {
        lock (_stateLock)
        {
            return _serverHealthStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new ServerHealthStatus
                {
                    Port = kvp.Value.Port,
                    IsHealthy = kvp.Value.IsHealthy,
                    ConsecutiveFailures = kvp.Value.ConsecutiveFailures,
                    LastPingTime = kvp.Value.LastSuccessfulPing,
                    LastResponseTime = kvp.Value.LastResponseTime,
                    AverageResponseTime = kvp.Value.GetAverageResponseTime()
                });
        }
    }

    /// <summary>
    /// Get health status for a specific server
    /// </summary>
    public ServerHealthStatus? GetServerHealth(int port)
    {
        lock (_stateLock)
        {
            if (_serverHealthStates.TryGetValue(port, out var state))
            {
                return new ServerHealthStatus
                {
                    Port = state.Port,
                    IsHealthy = state.IsHealthy,
                    ConsecutiveFailures = state.ConsecutiveFailures,
                    LastPingTime = state.LastSuccessfulPing,
                    LastResponseTime = state.LastResponseTime,
                    AverageResponseTime = state.GetAverageResponseTime()
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Manually ping a server
    /// </summary>
    public async Task<PingResult> PingServerAsync(int port, int timeoutMs = 5000)
    {
        return await SendAutoPingAsync("127.0.0.1", port, timeoutMs);
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        var pingIntervalMs = _config.HealthMonitoring?.AutoPingIntervalMs ?? 30000;
        var maxFailures = _config.HealthMonitoring?.MaxConsecutiveFailures ?? 3;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                List<int> portsToCheck;
                lock (_stateLock)
                {
                    portsToCheck = _serverHealthStates.Keys.ToList();
                }

                foreach (var port in portsToCheck)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var result = await SendAutoPingAsync("127.0.0.1", port, 5000);
                    
                    lock (_stateLock)
                    {
                        if (!_serverHealthStates.TryGetValue(port, out var state))
                            continue;

                        var wasHealthy = state.IsHealthy;

                        if (result.Success)
                        {
                            state.RecordSuccess(result.ResponseTime);

                            if (!wasHealthy)
                            {
                                _logger.LogInformation("Server on port {Port} recovered (response: {ResponseTime}ms)", 
                                    port, result.ResponseTime);
                                ServerRecovered?.Invoke(this, new ServerRecoveredEventArgs
                                {
                                    Port = port,
                                    ResponseTime = result.ResponseTime
                                });
                            }
                        }
                        else
                        {
                            state.RecordFailure();

                            if (wasHealthy && state.ConsecutiveFailures >= maxFailures)
                            {
                                _logger.LogWarning("Server on port {Port} is unhealthy after {Failures} consecutive failures", 
                                    port, state.ConsecutiveFailures);
                                
                                ServerUnhealthy?.Invoke(this, new ServerUnhealthyEventArgs
                                {
                                    Port = port,
                                    ConsecutiveFailures = state.ConsecutiveFailures,
                                    LastError = result.Error
                                });

                                // Check if restart is recommended
                                if (state.ConsecutiveFailures >= maxFailures * 2)
                                {
                                    RestartRecommended?.Invoke(this, new RestartRecommendedEventArgs
                                    {
                                        Port = port,
                                        Reason = $"Server unresponsive after {state.ConsecutiveFailures} ping failures"
                                    });
                                }
                            }
                        }
                    }
                }

                await Task.Delay(pingIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoPing monitoring loop");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task<PingResult> SendAutoPingAsync(string host, int port, int timeoutMs)
    {
        var result = new PingResult { Port = port };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var udpClient = new UdpClient();
        
        try
        {
            // Connect to server
            udpClient.Client.ReceiveTimeout = timeoutMs;
            udpClient.Client.SendTimeout = timeoutMs;
            
            var endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            udpClient.Connect(endpoint);

            // Send autoping packet
            // HoN autoping format: 0x01 (ping request)
            var pingPacket = new byte[] { 0x01 };
            await udpClient.SendAsync(pingPacket, pingPacket.Length);

            // Wait for response with timeout
            using var cts = new CancellationTokenSource(timeoutMs);
            
            var receiveTask = udpClient.ReceiveAsync(cts.Token);
            var response = await receiveTask;

            stopwatch.Stop();

            // Validate response (HoN pong is 0x02)
            if (response.Buffer.Length > 0 && response.Buffer[0] == 0x02)
            {
                result.Success = true;
                result.ResponseTime = (int)stopwatch.ElapsedMilliseconds;
            }
            else
            {
                result.Success = false;
                result.Error = "Invalid ping response";
            }
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.Error = $"Socket error: {ex.SocketErrorCode}";
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.Success = false;
            result.Error = "Timeout";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    public void Dispose()
    {
        StopMonitoringAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }
}

// Health state tracking

internal class ServerHealthState
{
    public int Port { get; set; }
    public bool IsHealthy => ConsecutiveFailures < 3;
    public int ConsecutiveFailures { get; private set; }
    public DateTime? LastSuccessfulPing { get; private set; }
    public int LastResponseTime { get; private set; }
    
    private readonly Queue<int> _responseTimes = new();
    private const int MaxResponseTimeHistory = 10;

    public void RecordSuccess(int responseTimeMs)
    {
        ConsecutiveFailures = 0;
        LastSuccessfulPing = DateTime.UtcNow;
        LastResponseTime = responseTimeMs;

        _responseTimes.Enqueue(responseTimeMs);
        while (_responseTimes.Count > MaxResponseTimeHistory)
            _responseTimes.Dequeue();
    }

    public void RecordFailure()
    {
        ConsecutiveFailures++;
    }

    public double GetAverageResponseTime()
    {
        if (_responseTimes.Count == 0) return 0;
        return _responseTimes.Average();
    }
}

// DTOs and Event Args

public class PingResult
{
    public int Port { get; set; }
    public bool Success { get; set; }
    public int ResponseTime { get; set; }
    public string? Error { get; set; }
}

public class ServerHealthStatus
{
    public int Port { get; set; }
    public bool IsHealthy { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastPingTime { get; set; }
    public int LastResponseTime { get; set; }
    public double AverageResponseTime { get; set; }
}

public class ServerUnhealthyEventArgs : EventArgs
{
    public int Port { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
}

public class ServerRecoveredEventArgs : EventArgs
{
    public int Port { get; set; }
    public int ResponseTime { get; set; }
}

public class RestartRecommendedEventArgs : EventArgs
{
    public int Port { get; set; }
    public string Reason { get; set; } = string.Empty;
}
