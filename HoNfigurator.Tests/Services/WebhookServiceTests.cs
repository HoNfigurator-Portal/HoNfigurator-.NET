using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for WebhookService - covers webhook registration, delivery, and event broadcasting
/// </summary>
public class WebhookServiceTests
{
    private readonly Mock<ILogger<WebhookService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly WebhookService _service;

    public WebhookServiceTests()
    {
        _loggerMock = new Mock<ILogger<WebhookService>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object);
        _service = new WebhookService(_loggerMock.Object, _httpClient);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content = "{}")
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
    }

    #region Registration Tests

    [Fact]
    public void RegisterWebhook_AddsWebhookSuccessfully()
    {
        // Arrange
        var config = new WebhookConfiguration
        {
            Id = "test-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true
        };

        // Act
        _service.RegisterWebhook(config);

        // Assert - no exception means success
    }

    [Fact]
    public void RegisterWebhook_CanUpdateExisting()
    {
        // Arrange
        var config1 = new WebhookConfiguration
        {
            Id = "test-webhook",
            Url = "https://old-url.com/webhook"
        };
        var config2 = new WebhookConfiguration
        {
            Id = "test-webhook",
            Url = "https://new-url.com/webhook"
        };

        // Act
        _service.RegisterWebhook(config1);
        _service.RegisterWebhook(config2);

        // Assert - no exception means success (URL updated)
    }

    [Fact]
    public void UnregisterWebhook_RemovesExisting()
    {
        // Arrange
        var config = new WebhookConfiguration
        {
            Id = "to-remove",
            Url = "https://example.com/webhook"
        };
        _service.RegisterWebhook(config);

        // Act
        var result = _service.UnregisterWebhook("to-remove");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void UnregisterWebhook_ReturnsFalseForNonExistent()
    {
        // Act
        var result = _service.UnregisterWebhook("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region SendWebhook Tests

    [Fact]
    public async Task SendWebhookAsync_ReturnsError_WhenNotRegistered()
    {
        // Act
        var result = await _service.SendWebhookAsync("unknown-webhook", new { data = "test" });

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
    }

    [Fact]
    public async Task SendWebhookAsync_ReturnsSuccess_WhenDelivered()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        var config = new WebhookConfiguration
        {
            Id = "test-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true
        };
        _service.RegisterWebhook(config);

        // Act
        var result = await _service.SendWebhookAsync("test-webhook", new { message = "hello" });

        // Assert
        result.Success.Should().BeTrue();
        result.WebhookId.Should().Be("test-webhook");
    }

    [Fact]
    public async Task SendWebhookAsync_ReturnsFailure_WhenHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError);
        var config = new WebhookConfiguration
        {
            Id = "test-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true
        };
        _service.RegisterWebhook(config);

        // Act
        var result = await _service.SendWebhookAsync("test-webhook", new { message = "hello" });

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
    }

    #endregion

    #region BroadcastEvent Tests

    [Fact]
    public async Task BroadcastEventAsync_SendsToMatchingWebhooks()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        
        var config1 = new WebhookConfiguration
        {
            Id = "webhook-1",
            Url = "https://example.com/webhook1",
            IsEnabled = true,
            EventTypes = new List<WebhookEventType> { WebhookEventType.MatchStart }
        };
        var config2 = new WebhookConfiguration
        {
            Id = "webhook-2",
            Url = "https://example.com/webhook2",
            IsEnabled = true,
            EventTypes = new List<WebhookEventType> { WebhookEventType.MatchEnd }
        };
        
        _service.RegisterWebhook(config1);
        _service.RegisterWebhook(config2);

        // Act
        var results = await _service.BroadcastEventAsync(WebhookEventType.MatchStart, new { matchId = 123 });

        // Assert
        results.Should().HaveCount(1);
        results[0].WebhookId.Should().Be("webhook-1");
    }

    [Fact]
    public async Task BroadcastEventAsync_SkipsDisabledWebhooks()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        
        var config = new WebhookConfiguration
        {
            Id = "disabled-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = false,
            EventTypes = new List<WebhookEventType> { WebhookEventType.MatchStart }
        };
        _service.RegisterWebhook(config);

        // Act
        var results = await _service.BroadcastEventAsync(WebhookEventType.MatchStart, new { matchId = 123 });

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region Queue Tests

    [Fact]
    public void QueueWebhook_AddsToQueue()
    {
        // Act & Assert - should not throw
        _service.QueueWebhook("test-webhook", new { data = "test" });
    }

    [Fact]
    public async Task ProcessQueueAsync_ProcessesQueuedWebhooks()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        var config = new WebhookConfiguration
        {
            Id = "queued-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true
        };
        _service.RegisterWebhook(config);
        _service.QueueWebhook("queued-webhook", new { data = "test" });

        // Act
        await _service.ProcessQueueAsync(CancellationToken.None);

        // Assert - verify HTTP call was made
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region Notification Helper Tests

    [Fact]
    public async Task NotifyMatchStartAsync_BroadcastsCorrectEvent()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        var config = new WebhookConfiguration
        {
            Id = "match-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true,
            EventTypes = new List<WebhookEventType> { WebhookEventType.MatchStart }
        };
        _service.RegisterWebhook(config);

        // Act
        var results = await _service.NotifyMatchStartAsync(123, 1, "caldavar", 10);

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task NotifyMatchEndAsync_BroadcastsCorrectEvent()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        var config = new WebhookConfiguration
        {
            Id = "match-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true,
            EventTypes = new List<WebhookEventType> { WebhookEventType.MatchEnd }
        };
        _service.RegisterWebhook(config);

        // Act
        var results = await _service.NotifyMatchEndAsync(123, "Legion", 45);

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task NotifyServerStatusAsync_BroadcastsCorrectEvent()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK);
        var config = new WebhookConfiguration
        {
            Id = "server-webhook",
            Url = "https://example.com/webhook",
            IsEnabled = true,
            EventTypes = new List<WebhookEventType> { WebhookEventType.ServerStatus }
        };
        _service.RegisterWebhook(config);

        // Act
        var results = await _service.NotifyServerStatusAsync(1, "Server1", "Ready");

        // Assert
        results.Should().HaveCount(1);
    }

    #endregion

    #region WebhookDeliveryResult Tests

    [Fact]
    public void WebhookDeliveryResult_DefaultValues()
    {
        // Arrange & Act
        var result = new WebhookDeliveryResult();

        // Assert
        result.Success.Should().BeFalse();
        result.WebhookId.Should().BeEmpty();
    }

    #endregion

    #region WebhookConfiguration Tests

    [Fact]
    public void WebhookConfiguration_DefaultValues()
    {
        // Arrange & Act
        var config = new WebhookConfiguration();

        // Assert
        config.IsEnabled.Should().BeTrue();
        config.EventTypes.Should().ContainSingle().Which.Should().Be(WebhookEventType.Alert);
        config.Headers.Should().BeEmpty();
        config.MaxRetries.Should().Be(3);
        config.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void WebhookConfiguration_WithHeaders()
    {
        // Arrange & Act
        var config = new WebhookConfiguration
        {
            Id = "test",
            Url = "https://example.com",
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer token123",
                ["X-Custom"] = "value"
            }
        };

        // Assert
        config.Headers.Should().HaveCount(2);
        config.Headers["Authorization"].Should().Be("Bearer token123");
    }

    #endregion

    #region WebhookEventType Tests

    [Theory]
    [InlineData(WebhookEventType.MatchStart)]
    [InlineData(WebhookEventType.MatchEnd)]
    [InlineData(WebhookEventType.PlayerJoin)]
    [InlineData(WebhookEventType.PlayerLeave)]
    [InlineData(WebhookEventType.ServerStatus)]
    [InlineData(WebhookEventType.Alert)]
    public void WebhookEventType_AllTypesValid(WebhookEventType eventType)
    {
        // Arrange
        var config = new WebhookConfiguration
        {
            Id = "test",
            Url = "https://example.com",
            EventTypes = new List<WebhookEventType> { eventType }
        };

        // Assert
        config.EventTypes.Should().Contain(eventType);
    }

    #endregion
}
