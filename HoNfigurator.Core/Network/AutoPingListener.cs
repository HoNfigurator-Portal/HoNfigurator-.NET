using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Network;

/// <summary>
/// UDP listener for AutoPing messages from game clients.
/// Responds to ping requests to allow clients to measure server latency.
/// </summary>
public interface IAutoPingListener
{
    bool IsRunning { get; }
    int Port { get; }
    long PacketCount { get; }
    DateTime LastActivity { get; }
    
    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();
    bool CheckHealth();
}

public class AutoPingListener : IAutoPingListener, IDisposable
{
    private readonly ILogger<AutoPingListener> _logger;
    private readonly HoNConfiguration _config;
    private readonly int _port;
    
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isRunning;
    private long _packetCount;
    private DateTime _lastActivity;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public long PacketCount => _packetCount;
    public DateTime LastActivity => _lastActivity;

    // AutoPing packet structure
    private static readonly byte[] PING_RESPONSE_HEADER = { 0x00, 0x01 };
    
    public AutoPingListener(ILogger<AutoPingListener> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        // Calculate autoping port: starting game port - 1 (+ 10000 if proxy enabled)
        var basePort = config.HonData.StartingGamePort - 1;
        _port = config.HonData.EnableProxy ? basePort + 10000 : basePort;
        
        _lastActivity = DateTime.UtcNow;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _logger.LogWarning("AutoPing listener is already running");
            return;
        }

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _udpClient = new UdpClient(_port);
            _isRunning = true;
            
            _logger.LogInformation("AutoPing listener started on UDP port {Port}", _port);
            
            _listenerTask = ListenAsync(_cts.Token);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start AutoPing listener on port {Port}", _port);
            _isRunning = false;
            throw;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                ProcessPingRequest(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // ICMP port unreachable - ignore
                continue;
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Error receiving AutoPing packet");
                }
            }
        }
        
        _logger.LogInformation("AutoPing listener stopped");
    }

    private void ProcessPingRequest(byte[] data, IPEndPoint remoteEndpoint)
    {
        try
        {
            Interlocked.Increment(ref _packetCount);
            _lastActivity = DateTime.UtcNow;

            // Validate packet - should be at least 2 bytes with proper header
            if (data.Length < 2)
            {
                _logger.LogDebug("Received invalid ping packet (too short) from {Endpoint}", remoteEndpoint);
                return;
            }

            // Build response packet
            // Format: [0x00, 0x01] + original packet data + server info
            var response = BuildPingResponse(data);
            
            _udpClient?.Send(response, response.Length, remoteEndpoint);
            
            _logger.LogDebug("Responded to ping from {Endpoint}, packet #{Count}", 
                remoteEndpoint, _packetCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing ping request from {Endpoint}", remoteEndpoint);
        }
    }

    private byte[] BuildPingResponse(byte[] requestData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        // Response header
        writer.Write(PING_RESPONSE_HEADER);
        
        // Echo back request data (for RTT calculation)
        writer.Write(requestData);
        
        // Add server timestamp
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        writer.Write(timestamp);
        
        // Add server region/location info
        var location = _config.HonData.Location ?? "NEWERTH";
        var locationBytes = Encoding.UTF8.GetBytes(location);
        writer.Write((byte)locationBytes.Length);
        writer.Write(locationBytes);
        
        return ms.ToArray();
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            
            _logger.LogInformation("Stopping AutoPing listener...");
            
            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            
            _isRunning = false;
        }
    }

    public bool CheckHealth()
    {
        if (!_isRunning) return false;
        
        // Check if we've received any packets recently (within last 5 minutes is ok)
        // Note: In low-traffic scenarios, no packets is normal
        var timeSinceLastActivity = DateTime.UtcNow - _lastActivity;
        
        // Try self-test by sending a ping to ourselves
        return SelfTest();
    }

    private bool SelfTest()
    {
        try
        {
            using var testClient = new UdpClient();
            var testData = new byte[] { 0x00, 0x01, 0xDE, 0xAD, 0xBE, 0xEF };
            var endpoint = new IPEndPoint(IPAddress.Loopback, _port);
            
            testClient.Send(testData, testData.Length, endpoint);
            testClient.Client.ReceiveTimeout = 1000; // 1 second timeout
            
            var serverEndpoint = new IPEndPoint(IPAddress.Any, 0);
            var response = testClient.Receive(ref serverEndpoint);
            
            return response.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
