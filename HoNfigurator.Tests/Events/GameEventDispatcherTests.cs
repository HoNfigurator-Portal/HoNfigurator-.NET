using FluentAssertions;
using HoNfigurator.Core.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Events;

/// <summary>
/// Unit tests for GameEventDispatcher
/// </summary>
public class GameEventDispatcherTests
{
    private readonly Mock<ILogger<GameEventDispatcher>> _mockLogger;

    public GameEventDispatcherTests()
    {
        _mockLogger = new Mock<ILogger<GameEventDispatcher>>();
    }

    private GameEventDispatcher CreateDispatcher()
    {
        return new GameEventDispatcher(_mockLogger.Object);
    }

    #region Handler Registration Tests

    [Fact]
    public void RegisterHandler_ShouldAddHandler()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new TestEventHandler();

        // Act
        dispatcher.RegisterHandler(handler);
        var gameEvent = new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 };
        dispatcher.Dispatch(gameEvent);

        // Assert - handler should receive event
        Thread.Sleep(100); // Wait for async dispatch
        handler.ReceivedEvents.Should().ContainSingle();
    }

    [Fact]
    public void UnregisterHandler_ShouldRemoveHandler()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new TestEventHandler();
        dispatcher.RegisterHandler(handler);

        // Act
        dispatcher.UnregisterHandler(handler);
        var gameEvent = new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 };
        dispatcher.Dispatch(gameEvent);

        // Assert - handler should not receive event
        Thread.Sleep(100);
        handler.ReceivedEvents.Should().BeEmpty();
    }

    [Fact]
    public void RegisterMultipleHandlers_ShouldAllReceiveEvents()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        dispatcher.RegisterHandler(handler1);
        dispatcher.RegisterHandler(handler2);

        // Act
        dispatcher.Dispatch(new GameEvent { EventType = GameEventType.MatchStarted, ServerId = 1 });
        Thread.Sleep(100);

        // Assert
        handler1.ReceivedEvents.Should().ContainSingle();
        handler2.ReceivedEvents.Should().ContainSingle();
    }

    #endregion

    #region Dispatch Tests

    [Fact]
    public async Task DispatchAsync_ShouldCallMatchingHandlers()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new TestEventHandler(GameEventType.ServerStarted, GameEventType.ServerStopped);
        dispatcher.RegisterHandler(handler);

        // Act
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });

        // Assert
        handler.ReceivedEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_ShouldNotCallNonMatchingHandlers()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new TestEventHandler(GameEventType.MatchStarted);
        dispatcher.RegisterHandler(handler);

        // Act
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });

        // Assert
        handler.ReceivedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_HandlerException_ShouldContinueToNextHandler()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var throwingHandler = new ThrowingEventHandler();
        var normalHandler = new TestEventHandler();
        dispatcher.RegisterHandler(throwingHandler);
        dispatcher.RegisterHandler(normalHandler);

        // Act
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });

        // Assert - normal handler should still receive event
        normalHandler.ReceivedEvents.Should().ContainSingle();
    }

    [Fact]
    public void Dispatch_ShouldFireAndForget()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new SlowEventHandler(TimeSpan.FromMilliseconds(100));
        dispatcher.RegisterHandler(handler);

        // Act - should not block
        var sw = System.Diagnostics.Stopwatch.StartNew();
        dispatcher.Dispatch(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        sw.Stop();

        // Assert - should return immediately
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    #endregion

    #region Event History Tests

    [Fact]
    public async Task GetRecentEvents_ShouldReturnDispatchedEvents()
    {
        // Arrange
        var dispatcher = CreateDispatcher();

        // Act
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchEnded, ServerId = 1 });

        // Assert
        var recent = dispatcher.GetRecentEvents(10);
        recent.Should().HaveCount(3);
        recent[0].EventType.Should().Be(GameEventType.MatchEnded); // Most recent first
        recent[2].EventType.Should().Be(GameEventType.ServerStarted);
    }

    [Fact]
    public async Task GetRecentEvents_WithLimit_ShouldLimitResults()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        for (int i = 0; i < 10; i++)
        {
            await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = i });
        }

        // Act
        var recent = dispatcher.GetRecentEvents(5);

        // Assert
        recent.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetEventsByType_ShouldFilterByEventType()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 2 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchEnded, ServerId = 1 });

        // Act
        var startedEvents = dispatcher.GetEventsByType(GameEventType.ServerStarted);

        // Assert
        startedEvents.Should().HaveCount(2);
        startedEvents.Should().AllSatisfy(e => e.EventType.Should().Be(GameEventType.ServerStarted));
    }

    [Fact]
    public async Task GetEventsByServer_ShouldFilterByServerId()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 2 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStopped, ServerId = 2 });

        // Act
        var server1Events = dispatcher.GetEventsByServer(1);

        // Assert
        server1Events.Should().HaveCount(2);
        server1Events.Should().AllSatisfy(e => e.ServerId.Should().Be(1));
    }

    #endregion

    #region Event Stats Tests

    [Fact]
    public async Task GetStats_ShouldReturnCorrectCounts()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 2 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchStarted, ServerId = 1 });
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.MatchEnded, ServerId = 1 });

        // Act
        var stats = dispatcher.GetStats();

        // Assert
        stats.TotalEvents.Should().Be(4);
        stats.EventsByType.Should().ContainKey("ServerStarted").WhoseValue.Should().Be(2);
        stats.EventsByType.Should().ContainKey("MatchStarted").WhoseValue.Should().Be(1);
        stats.EventsByServer.Should().ContainKey(1).WhoseValue.Should().Be(3);
        stats.EventsByServer.Should().ContainKey(2).WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_ShouldTrackTimestamps()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var before = DateTime.UtcNow;
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = 1 });
        Thread.Sleep(10);
        await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStopped, ServerId = 1 });
        var after = DateTime.UtcNow;

        // Act
        var stats = dispatcher.GetStats();

        // Assert
        stats.OldestEvent.Should().BeOnOrAfter(before);
        stats.NewestEvent.Should().BeOnOrBefore(after);
        stats.NewestEvent.Should().BeOnOrAfter(stats.OldestEvent.Value);
    }

    [Fact]
    public void GetStats_WithNoEvents_ShouldReturnEmptyStats()
    {
        // Arrange
        var dispatcher = CreateDispatcher();

        // Act
        var stats = dispatcher.GetStats();

        // Assert
        stats.TotalEvents.Should().Be(0);
        stats.EventsByType.Should().BeEmpty();
        stats.EventsByServer.Should().BeEmpty();
        stats.OldestEvent.Should().BeNull();
        stats.NewestEvent.Should().BeNull();
    }

    #endregion

    #region GameEvent Tests

    [Fact]
    public void GameEvent_GetData_ShouldReturnTypedValue()
    {
        // Arrange
        var gameEvent = new GameEvent
        {
            Data = new Dictionary<string, object>
            {
                ["playerCount"] = 10,
                ["mapName"] = "caldavar",
                ["matchId"] = 12345L
            }
        };

        // Act & Assert
        gameEvent.GetData<int>("playerCount").Should().Be(10);
        gameEvent.GetData<string>("mapName").Should().Be("caldavar");
        gameEvent.GetData<long>("matchId").Should().Be(12345L);
    }

    [Fact]
    public void GameEvent_GetData_WithMissingKey_ShouldReturnDefault()
    {
        // Arrange
        var gameEvent = new GameEvent();

        // Act & Assert
        gameEvent.GetData<int>("missing").Should().Be(0);
        gameEvent.GetData<string>("missing").Should().BeNull();
    }

    [Fact]
    public void GameEvent_GetData_ShouldConvertTypes()
    {
        // Arrange
        var gameEvent = new GameEvent
        {
            Data = new Dictionary<string, object>
            {
                ["intValue"] = "123"
            }
        };

        // Act
        var result = gameEvent.GetData<int>("intValue");

        // Assert
        result.Should().Be(123);
    }

    [Fact]
    public void GameEvent_ShouldGenerateUniqueId()
    {
        // Arrange & Act
        var event1 = new GameEvent();
        var event2 = new GameEvent();

        // Assert
        event1.Id.Should().NotBeNullOrEmpty();
        event2.Id.Should().NotBeNullOrEmpty();
        event1.Id.Should().NotBe(event2.Id);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task DispatchAsync_ConcurrentDispatch_ShouldBeThreadSafe()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        var handler = new CountingEventHandler();
        dispatcher.RegisterHandler(handler);

        // Act
        var tasks = Enumerable.Range(0, 100).Select(i =>
            dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = i }));
        await Task.WhenAll(tasks);

        // Assert
        handler.Count.Should().Be(100);
    }

    [Fact]
    public async Task GetRecentEvents_DuringDispatch_ShouldNotThrow()
    {
        // Arrange
        var dispatcher = CreateDispatcher();
        
        // Act
        var dispatchTask = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await dispatcher.DispatchAsync(new GameEvent { EventType = GameEventType.ServerStarted, ServerId = i });
            }
        });

        var readTask = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var events = dispatcher.GetRecentEvents();
                events.Should().NotBeNull();
            }
        });

        // Assert - should not throw
        await Task.WhenAll(dispatchTask, readTask);
    }

    #endregion

    #region Test Helpers

    private class TestEventHandler : IGameEventHandler
    {
        private readonly HashSet<GameEventType> _handledTypes;
        public List<GameEvent> ReceivedEvents { get; } = new();

        public TestEventHandler(params GameEventType[] types)
        {
            _handledTypes = types.Length > 0 ? new HashSet<GameEventType>(types) : null!;
        }

        public bool CanHandle(GameEventType eventType) => 
            _handledTypes == null || _handledTypes.Contains(eventType);

        public Task HandleAsync(GameEvent gameEvent)
        {
            ReceivedEvents.Add(gameEvent);
            return Task.CompletedTask;
        }
    }

    private class ThrowingEventHandler : IGameEventHandler
    {
        public bool CanHandle(GameEventType eventType) => true;

        public Task HandleAsync(GameEvent gameEvent)
        {
            throw new InvalidOperationException("Test exception");
        }
    }

    private class SlowEventHandler : IGameEventHandler
    {
        private readonly TimeSpan _delay;

        public SlowEventHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        public bool CanHandle(GameEventType eventType) => true;

        public async Task HandleAsync(GameEvent gameEvent)
        {
            await Task.Delay(_delay);
        }
    }

    private class CountingEventHandler : IGameEventHandler
    {
        private int _count;
        public int Count => _count;

        public bool CanHandle(GameEventType eventType) => true;

        public Task HandleAsync(GameEvent gameEvent)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }

    #endregion
}
