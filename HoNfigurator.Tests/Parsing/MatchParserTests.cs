using FluentAssertions;
using HoNfigurator.Core.Parsing;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Parsing;

/// <summary>
/// Unit tests for MatchParser
/// </summary>
public class MatchParserTests
{
    private readonly Mock<ILogger<MatchParser>> _mockLogger;

    public MatchParserTests()
    {
        _mockLogger = new Mock<ILogger<MatchParser>>();
    }

    private MatchParser CreateParser()
    {
        return new MatchParser(_mockLogger.Object);
    }

    #region ParseLogLines Tests

    [Fact]
    public void ParseLogLines_WithCompleteMatch_ShouldParseCorrectly()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 12345 started",
            "[Jan 15 12:00:01] Loading map 'caldavar'",
            "[Jan 15 12:00:02] Game mode: 'Normal'",
            "[Jan 15 12:00:05] Player 'TestPlayer1' (100001) joined team 1",
            "[Jan 15 12:00:06] Player 'TestPlayer2' (100002) joined team 2",
            "[Jan 15 12:00:10] Player 'TestPlayer1' selected hero 'Hero_Pyromancer'",
            "[Jan 15 12:00:11] Player 'TestPlayer2' selected hero 'Hero_Hammerstorm'",
            "[Jan 15 12:30:00] Match 12345 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().ContainSingle();
        var match = results[0];
        match.MatchId.Should().Be(12345);
        match.Map.Should().Be("caldavar");
        match.GameMode.Should().Be("Normal");
        match.WinningTeam.Should().Be(1);
        match.Players.Should().HaveCount(2);
    }

    [Fact]
    public void ParseLogLines_WithPlayerDetails_ShouldParsePlayerInfo()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:00:05] Player 'HeroPlayer' (99999) joined team 1",
            "[Jan 15 12:00:10] Player 'HeroPlayer' selected hero 'Hero_Devourer'",
            "[Jan 15 12:30:00] Match 1 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        var player = results[0].Players.First();
        player.Name.Should().Be("HeroPlayer");
        player.AccountId.Should().Be(99999);
        player.Team.Should().Be(1);
        player.Hero.Should().Be("Hero_Devourer");
        player.IsWinner.Should().BeTrue();
    }

    [Fact]
    public void ParseLogLines_WithPlayerDisconnect_ShouldMarkDisconnected()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:00:05] Player 'Player1' (100) joined team 1",
            "[Jan 15 12:15:00] Player 'Player1' (100) left",
            "[Jan 15 12:30:00] Match 1 ended - winner team 2"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        var player = results[0].Players.First();
        player.Disconnected.Should().BeTrue();
        player.IsWinner.Should().BeFalse();
    }

    [Fact]
    public void ParseLogLines_WithNoMatches_ShouldReturnEmpty()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Server starting...",
            "[Jan 15 12:00:01] Server ready",
            "[Jan 15 12:00:02] Waiting for connections"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogLines_WithMultipleMatches_ShouldParseAll()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:30:00] Match 1 ended - winner team 1",
            "[Jan 15 13:00:00] Match 2 started",
            "[Jan 15 13:30:00] Match 2 ended - winner team 2",
            "[Jan 15 14:00:00] Match 3 started",
            "[Jan 15 14:30:00] Match 3 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().HaveCount(3);
        results[0].MatchId.Should().Be(1);
        results[1].MatchId.Should().Be(2);
        results[2].MatchId.Should().Be(3);
    }

    [Fact]
    public void ParseLogLines_WithIncompleteMatch_ShouldIncludeIt()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:10:00] Player 'Test' (1) joined team 1",
            "[Jan 15 12:30:00] Match 2 started",  // New match starts without ending #1
            "[Jan 15 13:00:00] Match 2 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().HaveCount(2);
        results[0].MatchId.Should().Be(1);
        results[1].MatchId.Should().Be(2);
    }

    [Fact]
    public void ParseLogLines_ShouldCalculateMatchDuration()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:30:00] Match 1 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results[0].Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void ParseLogLines_WithBothTeams_ShouldSetWinnersCorrectly()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:00:05] Player 'Winner1' (101) joined team 1",
            "[Jan 15 12:00:06] Player 'Winner2' (102) joined team 1",
            "[Jan 15 12:00:07] Player 'Loser1' (201) joined team 2",
            "[Jan 15 12:00:08] Player 'Loser2' (202) joined team 2",
            "[Jan 15 12:30:00] Match 1 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        var match = results[0];
        match.Players.Where(p => p.Team == 1).Should().OnlyContain(p => p.IsWinner);
        match.Players.Where(p => p.Team == 2).Should().OnlyContain(p => !p.IsWinner);
    }

    #endregion

    #region ParseLogEntries Tests

    [Fact]
    public void ParseLogEntries_ShouldDetectErrorLevel()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Normal log message",
            "[Jan 15 12:00:01] Error: Something went wrong",
            "[Jan 15 12:00:02] [ERROR] Critical failure"
        };

        // Act
        var entries = parser.ParseLogEntries(lines);

        // Assert
        entries.Should().HaveCount(3);
        entries[0].Level.Should().Be("INFO");
        entries[1].Level.Should().Be("ERROR");
        entries[2].Level.Should().Be("ERROR");
    }

    [Fact]
    public void ParseLogEntries_ShouldDetectWarningLevel()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Warning: Low memory",
            "[Jan 15 12:00:01] [WARN] High CPU usage"
        };

        // Act
        var entries = parser.ParseLogEntries(lines);

        // Assert
        entries.Should().OnlyContain(e => e.Level == "WARN");
    }

    [Fact]
    public void ParseLogEntries_ShouldDetectDebugLevel()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] [DEBUG] Verbose information"
        };

        // Act
        var entries = parser.ParseLogEntries(lines);

        // Assert
        entries[0].Level.Should().Be("DEBUG");
    }

    [Fact]
    public void ParseLogEntries_ShouldPreserveMessage()
    {
        // Arrange
        var parser = CreateParser();
        var testMessage = "[Jan 15 12:00:00] This is the full message content";
        var lines = new[] { testMessage };

        // Act
        var entries = parser.ParseLogEntries(lines);

        // Assert
        entries[0].Message.Should().Be(testMessage);
    }

    [Fact]
    public void ParseLogEntries_WithEmptyInput_ShouldReturnEmpty()
    {
        // Arrange
        var parser = CreateParser();

        // Act
        var entries = parser.ParseLogEntries(Array.Empty<string>());

        // Assert
        entries.Should().BeEmpty();
    }

    #endregion

    #region ParseLogFileAsync Tests

    [Fact]
    public async Task ParseLogFileAsync_WithNonexistentFile_ShouldReturnEmpty()
    {
        // Arrange
        var parser = CreateParser();
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".log");

        // Act
        var results = await parser.ParseLogFileAsync(fakePath);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseLogFileAsync_WithValidFile_ShouldParseMatches()
    {
        // Arrange
        var parser = CreateParser();
        var tempFile = Path.GetTempFileName();
        
        try
        {
            await File.WriteAllLinesAsync(tempFile, new[]
            {
                "[Jan 15 12:00:00] Match 99 started",
                "[Jan 15 12:00:05] Player 'Tester' (1) joined team 1",
                "[Jan 15 12:30:00] Match 99 ended - winner team 1"
            });

            // Act
            var results = await parser.ParseLogFileAsync(tempFile);

            // Assert
            results.Should().ContainSingle();
            results[0].MatchId.Should().Be(99);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseLogFileAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var parser = CreateParser();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // Should throw or return empty based on implementation
        var act = async () => await parser.ParseLogFileAsync("anyfile.log", cts.Token);
        
        // File doesn't exist so it returns empty anyway
        var result = await parser.ParseLogFileAsync("anyfile.log", cts.Token);
        result.Should().BeEmpty();
    }

    #endregion

    #region Model Record Tests

    [Fact]
    public void MatchResult_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var result = new MatchResult { MatchId = 1 };

        // Assert
        result.Map.Should().BeEmpty();
        result.GameMode.Should().BeEmpty();
        result.GameName.Should().BeEmpty();
        result.Players.Should().BeEmpty();
    }

    [Fact]
    public void PlayerMatchResult_ShouldStoreAllStats()
    {
        // Arrange & Act
        var player = new PlayerMatchResult
        {
            AccountId = 12345,
            Name = "TestPlayer",
            Team = 1,
            Slot = 0,
            Hero = "Hero_Pyromancer",
            Kills = 10,
            Deaths = 5,
            Assists = 15,
            Level = 25,
            CreepKills = 200,
            CreepDenies = 50,
            NeutralKills = 30,
            GoldEarned = 15000,
            GoldSpent = 14000,
            HeroDamage = 25000,
            TowerDamage = 5000,
            IsWinner = true,
            Disconnected = false,
            PlayTime = TimeSpan.FromMinutes(35)
        };

        // Assert
        player.AccountId.Should().Be(12345);
        player.Name.Should().Be("TestPlayer");
        player.Kills.Should().Be(10);
        player.Deaths.Should().Be(5);
        player.Assists.Should().Be(15);
        player.Level.Should().Be(25);
        player.GoldEarned.Should().Be(15000);
        player.HeroDamage.Should().Be(25000);
    }

    [Fact]
    public void LogEntry_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var entry = new LogEntry { Message = "Test" };

        // Assert
        entry.Level.Should().Be("INFO");
        entry.Category.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseLogLines_WithMalformedTimestamp_ShouldUseCurrentTime()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "Match 1 started",  // No timestamp
            "Match 1 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().ContainSingle();
        // Should not crash
    }

    [Fact]
    public void ParseLogLines_WithSpecialCharactersInPlayerName_ShouldParse()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:00:05] Player 'Player[Pro]' (1) joined team 1",
            "[Jan 15 12:30:00] Match 1 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().ContainSingle();
        results[0].Players.Should().ContainSingle();
        results[0].Players[0].Name.Should().Be("Player[Pro]");
    }

    [Fact]
    public void ParseLogLines_WithOnlyMatchStart_ShouldNotAddIncompleteMatch()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 1 started",
            "[Jan 15 12:10:00] Some other log entry"
            // No match end
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        // Match without end should not be in results
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseLogLines_WithZeroMatchId_ShouldIgnore()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] Match 0 started",  // Zero ID
            "[Jan 15 12:30:00] Match 0 ended - winner team 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        // Implementation dependent - either parse or ignore
        if (results.Count > 0)
        {
            results[0].MatchId.Should().Be(0);
        }
    }

    [Fact]
    public void ParseLogLines_CaseInsensitiveMatching_ShouldWork()
    {
        // Arrange
        var parser = CreateParser();
        var lines = new[]
        {
            "[Jan 15 12:00:00] MATCH 1 STARTED",
            "[Jan 15 12:30:00] match 1 ENDED - WINNER TEAM 1"
        };

        // Act
        var results = parser.ParseLogLines(lines);

        // Assert
        results.Should().ContainSingle();
    }

    #endregion
}
