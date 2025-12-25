using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages webhooks for external notifications and integrations.
/// Port of Python HoNfigurator-Central webhook functionality.
/// </summary>
public class WebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, WebhookConfiguration> _webhooks = new();
    private readonly ConcurrentQueue<QueuedWebhook> _webhookQueue = new();
    private readonly SemaphoreSlim _processorLock = new(1);
    private readonly JsonSerializerOptions _jsonOptions;
    
    public event EventHandler<WebhookDeliveredEventArgs>? WebhookDelivered;

    public WebhookService(ILogger<WebhookService> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Register a webhook endpoint
    /// </summary>
    public void RegisterWebhook(WebhookConfiguration config)
    {
        _webhooks[config.Id] = config;
        _logger.LogInformation("Webhook registered: {Id} -> {Url}", config.Id, config.Url);
    }

    /// <summary>
    /// Unregister a webhook
    /// </summary>
    public bool UnregisterWebhook(string webhookId)
    {
        return _webhooks.TryRemove(webhookId, out _);
    }

    /// <summary>
    /// Send a webhook notification
    /// </summary>
    public async Task<WebhookDeliveryResult> SendWebhookAsync(string webhookId, object payload, CancellationToken cancellationToken = default)
    {
        if (!_webhooks.TryGetValue(webhookId, out var config))
        {
            return new WebhookDeliveryResult
            {
                Success = false,
                WebhookId = webhookId,
                Error = "Webhook not registered"
            };
        }

        return await SendToEndpointAsync(config, payload, cancellationToken);
    }

    /// <summary>
    /// Send webhook to all registered endpoints for an event type
    /// </summary>
    public async Task<List<WebhookDeliveryResult>> BroadcastEventAsync(WebhookEventType eventType, object payload, CancellationToken cancellationToken = default)
    {
        var results = new List<WebhookDeliveryResult>();
        var applicableWebhooks = _webhooks.Values
            .Where(w => w.IsEnabled && w.EventTypes.Contains(eventType))
            .ToList();

        var tasks = applicableWebhooks.Select(async config =>
        {
            var result = await SendToEndpointAsync(config, CreateEventPayload(eventType, payload), cancellationToken);
            return result;
        });

        var completedResults = await Task.WhenAll(tasks);
        results.AddRange(completedResults);

        return results;
    }

    /// <summary>
    /// Queue a webhook for async delivery
    /// </summary>
    public void QueueWebhook(string webhookId, object payload)
    {
        _webhookQueue.Enqueue(new QueuedWebhook
        {
            WebhookId = webhookId,
            Payload = payload,
            QueuedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Process queued webhooks
    /// </summary>
    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await _processorLock.WaitAsync(cancellationToken);

        try
        {
            var batch = new List<QueuedWebhook>();
            while (_webhookQueue.TryDequeue(out var item))
            {
                batch.Add(item);
                if (batch.Count >= 10) break;
            }

            var tasks = batch.Select(item => SendWebhookAsync(item.WebhookId, item.Payload, cancellationToken));
            await Task.WhenAll(tasks);
        }
        finally
        {
            _processorLock.Release();
        }
    }

    /// <summary>
    /// Send match start notification
    /// </summary>
    public Task<List<WebhookDeliveryResult>> NotifyMatchStartAsync(int matchId, int serverId, string mapName, int playerCount, CancellationToken cancellationToken = default)
    {
        return BroadcastEventAsync(WebhookEventType.MatchStart, new
        {
            matchId,
            serverId,
            mapName,
            playerCount,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <summary>
    /// Send match end notification
    /// </summary>
    public Task<List<WebhookDeliveryResult>> NotifyMatchEndAsync(int matchId, string winner, int durationMinutes, CancellationToken cancellationToken = default)
    {
        return BroadcastEventAsync(WebhookEventType.MatchEnd, new
        {
            matchId,
            winner,
            durationMinutes,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <summary>
    /// Send server status change notification
    /// </summary>
    public Task<List<WebhookDeliveryResult>> NotifyServerStatusAsync(int serverId, string status, string? reason = null, CancellationToken cancellationToken = default)
    {
        return BroadcastEventAsync(WebhookEventType.ServerStatus, new
        {
            serverId,
            status,
            reason,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <summary>
    /// Send player ban notification
    /// </summary>
    public Task<List<WebhookDeliveryResult>> NotifyPlayerBanAsync(int accountId, string playerName, string reason, int durationMinutes, CancellationToken cancellationToken = default)
    {
        return BroadcastEventAsync(WebhookEventType.PlayerBan, new
        {
            accountId,
            playerName,
            reason,
            durationMinutes,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <summary>
    /// Send alert notification
    /// </summary>
    public Task<List<WebhookDeliveryResult>> NotifyAlertAsync(string alertType, string message, string? details = null, CancellationToken cancellationToken = default)
    {
        return BroadcastEventAsync(WebhookEventType.Alert, new
        {
            alertType,
            message,
            details,
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <summary>
    /// Get all registered webhooks
    /// </summary>
    public IEnumerable<WebhookConfiguration> GetWebhooks()
    {
        return _webhooks.Values.ToList();
    }

    /// <summary>
    /// Test a webhook endpoint
    /// </summary>
    public async Task<WebhookDeliveryResult> TestWebhookAsync(string webhookId, CancellationToken cancellationToken = default)
    {
        return await SendWebhookAsync(webhookId, new
        {
            test = true,
            message = "This is a test webhook from HoNfigurator",
            timestamp = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task<WebhookDeliveryResult> SendToEndpointAsync(WebhookConfiguration config, object payload, CancellationToken cancellationToken)
    {
        var result = new WebhookDeliveryResult
        {
            WebhookId = config.Id,
            Url = config.Url,
            StartTime = DateTime.UtcNow
        };

        int attempt = 0;
        Exception? lastException = null;

        while (attempt < config.MaxRetries + 1)
        {
            attempt++;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, config.Url);
                
                // Add custom headers
                foreach (var header in config.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Add authentication
                if (!string.IsNullOrEmpty(config.Secret))
                {
                    var signature = ComputeSignature(payload, config.Secret);
                    request.Headers.TryAddWithoutValidation("X-Webhook-Signature", signature);
                }

                // Set content based on format
                if (config.Format == WebhookFormat.Discord)
                {
                    request.Content = JsonContent.Create(WrapForDiscord(payload), options: _jsonOptions);
                }
                else if (config.Format == WebhookFormat.Slack)
                {
                    request.Content = JsonContent.Create(WrapForSlack(payload), options: _jsonOptions);
                }
                else
                {
                    request.Content = JsonContent.Create(payload, options: _jsonOptions);
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

                var response = await _httpClient.SendAsync(request, cts.Token);
                
                result.StatusCode = (int)response.StatusCode;
                result.EndTime = DateTime.UtcNow;
                result.Attempts = attempt;

                if (response.IsSuccessStatusCode)
                {
                    result.Success = true;
                    _logger.LogDebug("Webhook delivered: {Id} -> {Url}", config.Id, config.Url);
                    
                    WebhookDelivered?.Invoke(this, new WebhookDeliveredEventArgs(config.Id, true));
                    return result;
                }

                result.Error = $"HTTP {result.StatusCode}: {response.ReasonPhrase}";
            }
            catch (OperationCanceledException)
            {
                result.Error = "Request timeout";
                lastException = null;
            }
            catch (Exception ex)
            {
                lastException = ex;
                result.Error = ex.Message;
            }

            // Wait before retry
            if (attempt < config.MaxRetries + 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        result.EndTime = DateTime.UtcNow;
        result.Attempts = attempt;
        
        _logger.LogWarning("Webhook delivery failed after {Attempts} attempts: {Id} -> {Url}: {Error}",
            attempt, config.Id, config.Url, result.Error);
        
        WebhookDelivered?.Invoke(this, new WebhookDeliveredEventArgs(config.Id, false));
        return result;
    }

    private object CreateEventPayload(WebhookEventType eventType, object data)
    {
        return new
        {
            eventType = eventType.ToString(),
            data,
            timestamp = DateTime.UtcNow
        };
    }

    private object WrapForDiscord(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        return new
        {
            embeds = new[]
            {
                new
                {
                    title = "HoNfigurator Notification",
                    description = $"```json\n{json[..Math.Min(json.Length, 4000)]}\n```",
                    color = 5814783, // Blue color
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };
    }

    private object WrapForSlack(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        return new
        {
            text = "HoNfigurator Notification",
            blocks = new[]
            {
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = $"```{json[..Math.Min(json.Length, 3000)]}```"
                    }
                }
            }
        };
    }

    private static string ComputeSignature(object payload, string secret)
    {
        var json = JsonSerializer.Serialize(payload);
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var dataBytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        using var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// Internal classes

internal class QueuedWebhook
{
    public string WebhookId { get; set; } = string.Empty;
    public object Payload { get; set; } = null!;
    public DateTime QueuedAt { get; set; }
}

// Enums

public enum WebhookEventType
{
    MatchStart,
    MatchEnd,
    ServerStatus,
    PlayerJoin,
    PlayerLeave,
    PlayerBan,
    Alert,
    Custom
}

public enum WebhookFormat
{
    Json,
    Discord,
    Slack
}

// Configuration

public class WebhookConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<WebhookEventType> EventTypes { get; set; } = new() { WebhookEventType.Alert };
    public Dictionary<string, string> Headers { get; set; } = new();
    public WebhookFormat Format { get; set; } = WebhookFormat.Json;
    public int MaxRetries { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
}

// DTOs

public class WebhookDeliveryResult
{
    public bool Success { get; set; }
    public string WebhookId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class WebhookDeliveredEventArgs : EventArgs
{
    public string WebhookId { get; }
    public bool Success { get; }

    public WebhookDeliveredEventArgs(string webhookId, bool success)
    {
        WebhookId = webhookId;
        Success = success;
    }
}
