using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for BotMatchDetectionService - covers bot pattern matching and match analysis
/// </summary>
public class BotMatchDetectionServiceTests
{
    private readonly Mock<ILogger<BotMatchDetectionService>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly BotMatchDetectionService _service;

    public BotMatchDetectionServiceTests()
    {
        _loggerMock = new Mock<ILogger<BotMatchDetectionService>>();
        _config = new HoNConfiguration();
        _service = new BotMatchDetectionService(_loggerMock.Object, _config);
    }

    #region Pattern Management Tests

    [Fact]
    public void Constructor_LoadsDefaultBotPatterns()
    {
        // Act
        var patterns = _service.GetBotPatterns();

        // Assert
        patterns.Should().NotBeEmpty();
        patterns.Should().Contain("Bot");
        patterns.Should().Contain("AI_");
    }

    [Fact]
    public void AddBotPattern_AddsNewPattern()
    {
        // Arrange
        var pattern = "CustomBot_";

        // Act
        _service.AddBotPattern(pattern);

        // Assert
        _service.GetBotPatterns().Should().Contain(pattern);
    }

    [Fact]
    public void AddBotPattern_IsCaseInsensitive()
    {
        // Arrange
        _service.AddBotPattern("TESTBOT");

        // Act
        var patterns = _service.GetBotPatterns();

        // Assert
        patterns.Should().Contain("TESTBOT");
    }

    #endregion

    #region Whitelist Tests

    [Fact]
    public void AddWhitelistedAccount_AddsAccount()
    {
        // Arrange
        var account = "TrustedPlayer";

        // Act
        _service.AddWhitelistedAccount(account);

        // Assert
        _service.GetWhitelistedAccounts().Should().Contain(account);
    }

    [Fact]
    public void RemoveWhitelistedAccount_RemovesExisting()
    {
        // Arrange
        _service.AddWhitelistedAccount("ToRemove");

        // Act
        var result = _service.RemoveWhitelistedAccount("ToRemove");

        // Assert
        result.Should().BeTrue();
        _service.GetWhitelistedAccounts().Should().NotContain("ToRemove");
    }

    [Fact]
    public void RemoveWhitelistedAccount_ReturnsFalseForNonExistent()
    {
        // Act
        var result = _service.RemoveWhitelistedAccount("NonExistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetWhitelistedAccounts_ReturnsEmptyByDefault()
    {
        // Act
        var accounts = _service.GetWhitelistedAccounts();

        // Assert
        accounts.Should().BeEmpty();
    }

    #endregion

    #region Player Analysis Tests

    [Fact]
    public void AnalyzePlayer_WhitelistedPlayer_ReturnsNotBot()
    {
        // Arrange
        _service.AddWhitelistedAccount("TrustedBot");
        var player = new BotCheckPlayerInfo
        {
            AccountId = 1,
            AccountName = "TrustedBot"
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.IsBot.Should().BeFalse();
        result.Reason.Should().Contain("Whitelisted");
    }

    [Fact]
    public void AnalyzePlayer_NameContainsBotPattern_HighConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 100,
            AccountName = "Bot_Player123"
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeGreaterThanOrEqualTo(40);
        result.Reason.Should().Contain("bot pattern");
    }

    [Fact]
    public void AnalyzePlayer_ZeroAccountId_IncreasesConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 0,
            AccountName = "SomePlayer"
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeGreaterThanOrEqualTo(30);
        result.Reason.Should().Contain("Account ID is 0");
    }

    [Fact]
    public void AnalyzePlayer_ZeroPing_IncreasesConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 100,
            AccountName = "NormalName",
            Ping = 0
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeGreaterThanOrEqualTo(20);
        result.Reason.Should().Contain("Zero ping");
    }

    [Fact]
    public void AnalyzePlayer_NewAccount_IncreasesConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 100,
            AccountName = "NewPlayer",
            CreatedDate = DateTime.UtcNow.AddHours(-1)
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeGreaterThanOrEqualTo(10);
        result.Reason.Should().Contain("created today");
    }

    [Fact]
    public void AnalyzePlayer_NoDisconnectsIn100Games_IncreasesConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 100,
            AccountName = "ReliablePlayer",
            GamesPlayed = 150,
            Disconnects = 0
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeGreaterThanOrEqualTo(15);
        result.Reason.Should().Contain("No disconnects");
    }

    [Fact]
    public void AnalyzePlayer_EqualWinsLosses_IncreasesConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 100,
            AccountName = "BalancedPlayer",
            GamesPlayed = 50,
            Wins = 25,
            Losses = 25
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Reason.Should().Contain("equal wins/losses");
    }

    [Fact]
    public void AnalyzePlayer_NormalPlayer_LowConfidence()
    {
        // Arrange
        var player = new BotCheckPlayerInfo
        {
            AccountId = 12345,
            AccountName = "LegitPlayer",
            Ping = 50,
            GamesPlayed = 100,
            Wins = 55,
            Losses = 45,
            Disconnects = 5,
            CreatedDate = DateTime.UtcNow.AddYears(-1)
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.IsBot.Should().BeFalse();
        result.Confidence.Should().BeLessThan(60);
    }

    [Fact]
    public void AnalyzePlayer_MultipleIndicators_CombinesConfidence()
    {
        // Arrange - multiple bot indicators
        var player = new BotCheckPlayerInfo
        {
            AccountId = 0, // +30
            AccountName = "Bot_Test", // +40
            Ping = 0 // +20
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.IsBot.Should().BeTrue();
        result.Confidence.Should().BeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public void AnalyzePlayer_ConfidenceCappedAt100()
    {
        // Arrange - many bot indicators
        var player = new BotCheckPlayerInfo
        {
            AccountId = 0,
            AccountName = "Bot_Player",
            Ping = 0,
            CreatedDate = DateTime.UtcNow,
            GamesPlayed = 200,
            Disconnects = 0,
            Wins = 100,
            Losses = 100
        };

        // Act
        var result = _service.AnalyzePlayer(player);

        // Assert
        result.Confidence.Should().BeLessThanOrEqualTo(100);
    }

    #endregion

    #region Match Analysis Tests

    [Fact]
    public void AnalyzeMatch_NoBotsDetected_NotBotMatch()
    {
        // Arrange
        var players = new List<BotCheckPlayerInfo>
        {
            new() { AccountId = 1, AccountName = "Player1", Ping = 50 },
            new() { AccountId = 2, AccountName = "Player2", Ping = 60 }
        };

        // Act
        var analysis = _service.AnalyzeMatch(123, players);

        // Assert
        analysis.IsBotMatch.Should().BeFalse();
        analysis.BotCount.Should().Be(0);
        analysis.TotalPlayers.Should().Be(2);
    }

    [Fact]
    public void AnalyzeMatch_SomeBotsDetected_CountsCorrectly()
    {
        // Arrange
        var players = new List<BotCheckPlayerInfo>
        {
            new() { AccountId = 1, AccountName = "Player1", Ping = 50 },
            new() { AccountId = 0, AccountName = "Bot_Enemy", Ping = 0 } // Bot
        };

        // Act
        var analysis = _service.AnalyzeMatch(123, players);

        // Assert
        analysis.BotCount.Should().Be(1);
        analysis.TotalPlayers.Should().Be(2);
    }

    [Fact]
    public void AnalyzeMatch_RecordsTimestamp()
    {
        // Arrange
        var players = new List<BotCheckPlayerInfo>
        {
            new() { AccountId = 1, AccountName = "Player1" }
        };

        // Act
        var before = DateTime.UtcNow;
        var analysis = _service.AnalyzeMatch(123, players);
        var after = DateTime.UtcNow;

        // Assert
        analysis.AnalyzedAt.Should().BeOnOrAfter(before);
        analysis.AnalyzedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void AnalyzeMatch_StoresPlayerResults()
    {
        // Arrange
        var players = new List<BotCheckPlayerInfo>
        {
            new() { AccountId = 1, AccountName = "Player1" },
            new() { AccountId = 2, AccountName = "Player2" },
            new() { AccountId = 3, AccountName = "Player3" }
        };

        // Act
        var analysis = _service.AnalyzeMatch(123, players);

        // Assert
        analysis.PlayerResults.Should().HaveCount(3);
    }

    #endregion

    #region Model Tests

    [Fact]
    public void BotDetectionResult_DefaultValues()
    {
        // Arrange & Act
        var result = new BotDetectionResult();

        // Assert
        result.IsBot.Should().BeFalse();
        result.Confidence.Should().Be(0);
        result.AccountName.Should().BeEmpty();
    }

    [Fact]
    public void BotCheckPlayerInfo_DefaultValues()
    {
        // Arrange & Act
        var info = new BotCheckPlayerInfo();

        // Assert
        info.AccountId.Should().Be(0);
        info.AccountName.Should().BeEmpty();
        info.GamesPlayed.Should().Be(0);
    }

    [Fact]
    public void MatchBotAnalysis_DefaultValues()
    {
        // Arrange & Act
        var analysis = new MatchBotAnalysis();

        // Assert
        analysis.BotCount.Should().Be(0);
        analysis.TotalPlayers.Should().Be(0);
        analysis.IsBotMatch.Should().BeFalse();
        analysis.PlayerResults.Should().BeEmpty();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task AddBotPattern_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _service.AddBotPattern($"Pattern_{index}")));
        }

        await Task.WhenAll(tasks);

        // Assert
        _service.GetBotPatterns().Count.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task AnalyzePlayer_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<BotDetectionResult>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _service.AnalyzePlayer(new BotCheckPlayerInfo
            {
                AccountId = index,
                AccountName = $"Player_{index}"
            })));
        }

        await Task.WhenAll(tasks);

        // Assert - all completed without error
        tasks.Should().AllSatisfy(t => t.Result.Should().NotBeNull());
    }

    #endregion
}
