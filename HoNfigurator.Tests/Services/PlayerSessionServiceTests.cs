using FluentAssertions;
using HoNfigurator.Core.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Services;

public class PlayerSessionServiceTests
{
    private readonly Mock<ILogger<PlayerSessionService>> _loggerMock;
    private readonly PlayerSessionService _service;

    public PlayerSessionServiceTests()
    {
        _loggerMock = new Mock<ILogger<PlayerSessionService>>();
        _service = new PlayerSessionService(_loggerMock.Object);
    }

    #region CreateSession Tests

    [Fact]
    public void CreateSession_ShouldCreateNewSession()
    {
        var session = _service.CreateSession(123, "TestPlayer", "cookie123", "192.168.1.1");

        session.Should().NotBeNull();
        session.AccountId.Should().Be(123);
        session.AccountName.Should().Be("TestPlayer");
        session.Cookie.Should().Be("cookie123");
        session.IpAddress.Should().Be("192.168.1.1");
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void CreateSession_ShouldIncrementPlayerCount()
    {
        _service.PlayerCount.Should().Be(0);
        
        _service.CreateSession(1, "Player1", "c1", "1.1.1.1");
        _service.PlayerCount.Should().Be(1);
        
        _service.CreateSession(2, "Player2", "c2", "2.2.2.2");
        _service.PlayerCount.Should().Be(2);
    }

    [Fact]
    public void CreateSession_ShouldReplaceExistingSession()
    {
        _service.CreateSession(123, "Player1", "cookie1", "1.1.1.1");
        _service.CreateSession(123, "Player1Updated", "cookie2", "2.2.2.2");
        
        _service.PlayerCount.Should().Be(1);
        var session = _service.GetSession(123);
        session!.AccountName.Should().Be("Player1Updated");
        session.Cookie.Should().Be("cookie2");
    }

    [Fact]
    public void CreateSession_ShouldFireEvent()
    {
        PlayerSession? createdSession = null;
        _service.OnSessionCreated += s => createdSession = s;

        var session = _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");

        createdSession.Should().BeSameAs(session);
    }

    #endregion

    #region GetSession Tests

    [Fact]
    public void GetSession_ShouldReturnSession_WhenExists()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        var session = _service.GetSession(123);
        
        session.Should().NotBeNull();
        session!.AccountId.Should().Be(123);
    }

    [Fact]
    public void GetSession_ShouldReturnNull_WhenNotExists()
    {
        var session = _service.GetSession(999);
        
        session.Should().BeNull();
    }

    [Fact]
    public void GetSessionByCookie_ShouldReturnSession_WhenExists()
    {
        _service.CreateSession(123, "TestPlayer", "uniquecookie", "1.1.1.1");
        
        var session = _service.GetSessionByCookie("uniquecookie");
        
        session.Should().NotBeNull();
        session!.AccountId.Should().Be(123);
    }

    [Fact]
    public void GetSessionByCookie_ShouldReturnNull_WhenNotExists()
    {
        var session = _service.GetSessionByCookie("nonexistent");
        
        session.Should().BeNull();
    }

    #endregion

    #region GetAllSessions Tests

    [Fact]
    public void GetAllSessions_ShouldReturnAllSessions()
    {
        _service.CreateSession(1, "Player1", "c1", "1.1.1.1");
        _service.CreateSession(2, "Player2", "c2", "2.2.2.2");
        _service.CreateSession(3, "Player3", "c3", "3.3.3.3");
        
        var sessions = _service.GetAllSessions();
        
        sessions.Should().HaveCount(3);
    }

    [Fact]
    public void GetAllSessions_ShouldReturnEmptyList_WhenNoSessions()
    {
        var sessions = _service.GetAllSessions();
        
        sessions.Should().BeEmpty();
    }

    #endregion

    #region RemoveSession Tests

    [Fact]
    public void RemoveSession_ShouldRemoveSession()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        _service.RemoveSession(123);
        
        _service.PlayerCount.Should().Be(0);
        _service.GetSession(123).Should().BeNull();
    }

    [Fact]
    public void RemoveSession_ShouldFireEvent()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        PlayerSession? removedSession = null;
        _service.OnSessionRemoved += s => removedSession = s;
        
        _service.RemoveSession(123);
        
        removedSession.Should().NotBeNull();
        removedSession!.AccountId.Should().Be(123);
    }

    [Fact]
    public void RemoveSession_ShouldDoNothing_WhenNotExists()
    {
        // Should not throw
        _service.RemoveSession(999);
    }

    #endregion

    #region ClearAllSessions Tests

    [Fact]
    public void ClearAllSessions_ShouldRemoveAllSessions()
    {
        _service.CreateSession(1, "Player1", "c1", "1.1.1.1");
        _service.CreateSession(2, "Player2", "c2", "2.2.2.2");
        
        _service.ClearAllSessions();
        
        _service.PlayerCount.Should().Be(0);
    }

    [Fact]
    public void ClearAllSessions_ShouldFireEventsForEach()
    {
        _service.CreateSession(1, "Player1", "c1", "1.1.1.1");
        _service.CreateSession(2, "Player2", "c2", "2.2.2.2");
        
        var removedCount = 0;
        _service.OnSessionRemoved += _ => removedCount++;
        
        _service.ClearAllSessions();
        
        removedCount.Should().Be(2);
    }

    #endregion

    #region UpdateActivity Tests

    [Fact]
    public void UpdateActivity_ShouldUpdateLastActivityAt()
    {
        var session = _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        var originalActivity = session.LastActivityAt;
        
        // Wait a tiny bit to ensure time difference
        Thread.Sleep(10);
        
        _service.UpdateActivity(123);
        
        var updatedSession = _service.GetSession(123);
        updatedSession!.LastActivityAt.Should().BeAfter(originalActivity);
    }

    #endregion

    #region AssignToMatch Tests

    [Fact]
    public void AssignToMatch_ShouldUpdateMatchInfo()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        _service.AssignToMatch(123, matchId: 456, team: 1, slot: 3);
        
        var session = _service.GetSession(123);
        session!.CurrentMatchId.Should().Be(456);
        session.Team.Should().Be(1);
        session.Slot.Should().Be(3);
    }

    #endregion

    #region UpdateStats Tests

    [Fact]
    public void UpdateStats_ShouldUpdateStatistics()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        _service.UpdateStats(123, s =>
        {
            s.Kills = 5;
            s.Deaths = 2;
            s.Assists = 10;
        });
        
        var session = _service.GetSession(123);
        session!.Kills.Should().Be(5);
        session.Deaths.Should().Be(2);
        session.Assists.Should().Be(10);
    }

    [Fact]
    public void UpdateStats_ShouldFireEvent()
    {
        _service.CreateSession(123, "TestPlayer", "cookie", "1.1.1.1");
        
        PlayerSession? updatedSession = null;
        _service.OnStatsUpdated += s => updatedSession = s;
        
        _service.UpdateStats(123, s => s.Kills++);
        
        updatedSession.Should().NotBeNull();
    }

    #endregion

    #region GetInactivePlayers Tests

    [Fact]
    public void GetInactivePlayers_ShouldReturnInactivePlayers()
    {
        var session1 = _service.CreateSession(1, "Active", "c1", "1.1.1.1");
        var session2 = _service.CreateSession(2, "Inactive", "c2", "2.2.2.2");
        
        // Make session2 appear old by updating session1
        Thread.Sleep(50);
        _service.UpdateActivity(1);
        
        var inactive = _service.GetInactivePlayers(TimeSpan.FromMilliseconds(30));
        
        inactive.Should().HaveCount(1);
        inactive[0].AccountId.Should().Be(2);
    }

    #endregion

    #region AuthenticatePlayer Tests

    [Fact]
    public async Task AuthenticatePlayerAsync_ShouldFail_WhenSessionNotFound()
    {
        var result = await _service.AuthenticatePlayerAsync(999, "cookie");
        
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Session not found");
    }

    [Fact]
    public async Task AuthenticatePlayerAsync_ShouldFail_WhenCookieMismatch()
    {
        _service.CreateSession(123, "TestPlayer", "correctcookie", "1.1.1.1");
        
        var result = await _service.AuthenticatePlayerAsync(123, "wrongcookie");
        
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid cookie");
    }

    [Fact]
    public async Task AuthenticatePlayerAsync_ShouldSucceed_WhenCookieMatches()
    {
        _service.CreateSession(123, "TestPlayer", "cookie123", "1.1.1.1");
        
        var result = await _service.AuthenticatePlayerAsync(123, "cookie123");
        
        result.Success.Should().BeTrue();
        result.AccountId.Should().Be(123);
        result.AccountName.Should().Be("TestPlayer");
        
        var session = _service.GetSession(123);
        session!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticatePlayerAsync_ShouldFireEvent_OnSuccess()
    {
        _service.CreateSession(123, "TestPlayer", "cookie123", "1.1.1.1");
        
        PlayerSession? authenticatedSession = null;
        _service.OnSessionAuthenticated += s => authenticatedSession = s;
        
        await _service.AuthenticatePlayerAsync(123, "cookie123");
        
        authenticatedSession.Should().NotBeNull();
    }

    #endregion
}

public class PlayerSessionExtensionsTests
{
    [Fact]
    public void GetKda_ShouldCalculateCorrectly_WithDeaths()
    {
        var session = new PlayerSession
        {
            Kills = 10,
            Deaths = 5,
            Assists = 15
        };
        
        session.GetKda().Should().Be(5.0); // (10 + 15) / 5
    }

    [Fact]
    public void GetKda_ShouldReturnKillsPlusAssists_WhenNoDeaths()
    {
        var session = new PlayerSession
        {
            Kills = 10,
            Deaths = 0,
            Assists = 5
        };
        
        session.GetKda().Should().Be(15.0); // 10 + 5
    }

    [Fact]
    public void GetCreepScore_ShouldSumKillsAndDenies()
    {
        var session = new PlayerSession
        {
            CreepKills = 150,
            CreepDenies = 30
        };
        
        session.GetCreepScore().Should().Be(180);
    }

    [Fact]
    public void ToSummaryString_ShouldFormatCorrectly()
    {
        var session = new PlayerSession
        {
            AccountId = 123,
            AccountName = "TestPlayer",
            Kills = 5,
            Deaths = 3,
            Assists = 7,
            CreepKills = 100,
            CreepDenies = 20
        };
        
        var summary = session.ToSummaryString();
        
        summary.Should().Contain("TestPlayer");
        summary.Should().Contain("123");
        summary.Should().Contain("5/3/7");
        summary.Should().Contain("120"); // CS
    }

    [Fact]
    public void GetSessionDuration_ShouldCalculateCorrectly()
    {
        var session = new PlayerSession
        {
            ConnectedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        
        var duration = session.GetSessionDuration();
        
        duration.TotalMinutes.Should().BeApproximately(5, 0.1);
    }
}
