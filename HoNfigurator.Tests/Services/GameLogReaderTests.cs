using FluentAssertions;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Tests for GameLogReader - Game log parsing service
/// </summary>
public class GameLogReaderTests : IDisposable
{
    private readonly Mock<ILogger<GameLogReader>> _loggerMock;
    private readonly string _tempDir;
    private readonly string _logsDir;

    public GameLogReaderTests()
    {
        _loggerMock = new Mock<ILogger<GameLogReader>>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"GameLogReaderTests_{Guid.NewGuid():N}");
        _logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(_logsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private GameLogReader CreateService(HoNConfiguration? config = null)
    {
        config ??= CreateTestConfig();
        return new GameLogReader(_loggerMock.Object, config);
    }

    private HoNConfiguration CreateTestConfig()
    {
        return new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonLogsDirectory = _logsDir
            }
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidConfig_ShouldInitialize()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
    }

    #endregion

    #region GetPlayerSlots Tests

    [Fact]
    public void GetPlayerSlots_WithNoLogsDir_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonLogsDirectory = null
            }
        };
        var service = CreateService(config);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetPlayerSlots_WithNonExistentDir_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                HonLogsDirectory = "/nonexistent/dir"
            }
        };
        var service = CreateService(config);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetPlayerSlots_WithNoLogFiles_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetPlayerSlots_WithEmptyLogFile_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var service = CreateService();
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, "", System.Text.Encoding.Unicode);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetPlayerSlots_WithPlayerConnectLog_ShouldExtractSlots()
    {
        // Arrange
        var service = CreateService();
        var logContent = "PLAYER_CONNECT player:0 name:\"TestPlayer\" id:12345 psr:1500\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().ContainKey("TestPlayer");
        result["TestPlayer"].Should().Be(0);
    }

    [Fact]
    public void GetPlayerSlots_WithMultiplePlayers_ShouldExtractAll()
    {
        // Arrange
        var service = CreateService();
        var logContent = 
            "PLAYER_CONNECT player:0 name:\"Player1\" id:1 psr:1500\r\n" +
            "PLAYER_CONNECT player:1 name:\"Player2\" id:2 psr:1600\r\n" +
            "PLAYER_CONNECT player:5 name:\"Player3\" id:3 psr:1700\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().HaveCount(3);
        result["Player1"].Should().Be(0);
        result["Player2"].Should().Be(1);
        result["Player3"].Should().Be(5);
    }

    [Fact]
    public void GetPlayerSlots_ShouldUseMostRecentLogFile()
    {
        // Arrange
        var service = CreateService();
        
        // Create older file
        var oldLogPath = Path.Combine(_logsDir, "Slave1_old.clog");
        File.WriteAllText(oldLogPath, "PLAYER_CONNECT player:0 name:\"OldPlayer\" id:1 psr:1500\r\n", System.Text.Encoding.Unicode);
        File.SetLastWriteTime(oldLogPath, DateTime.Now.AddHours(-1));
        
        // Create newer file
        var newLogPath = Path.Combine(_logsDir, "Slave1_new.clog");
        File.WriteAllText(newLogPath, "PLAYER_CONNECT player:0 name:\"NewPlayer\" id:2 psr:1600\r\n", System.Text.Encoding.Unicode);

        // Act
        var result = service.GetPlayerSlots(1);

        // Assert
        result.Should().ContainKey("NewPlayer");
        result.Should().NotContainKey("OldPlayer");
    }

    [Fact]
    public void GetPlayerSlots_WithDifferentServerId_ShouldUseCorrectPattern()
    {
        // Arrange
        var service = CreateService();
        var log1Path = Path.Combine(_logsDir, "Slave1_test.clog");
        var log2Path = Path.Combine(_logsDir, "Slave2_test.clog");
        File.WriteAllText(log1Path, "PLAYER_CONNECT player:0 name:\"Server1Player\" id:1 psr:1500\r\n", System.Text.Encoding.Unicode);
        File.WriteAllText(log2Path, "PLAYER_CONNECT player:0 name:\"Server2Player\" id:2 psr:1600\r\n", System.Text.Encoding.Unicode);

        // Act
        var result1 = service.GetPlayerSlots(1);
        var result2 = service.GetPlayerSlots(2);

        // Assert
        result1.Should().ContainKey("Server1Player");
        result2.Should().ContainKey("Server2Player");
    }

    #endregion

    #region PopulateTeams Tests

    [Fact]
    public void PopulateTeams_WithEmptyPlayers_ShouldNotThrow()
    {
        // Arrange
        var service = CreateService();
        var instance = new GameServerInstance { Id = 1 };

        // Act
        var act = () => service.PopulateTeams(instance);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void PopulateTeams_ShouldClearExistingTeams()
    {
        // Arrange
        var service = CreateService();
        var instance = new GameServerInstance { Id = 1 };
        instance.PlayersByTeam.Legion.Add(new PlayerInfo { Name = "ExistingPlayer" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Legion.Should().BeEmpty();
    }

    [Fact]
    public void PopulateTeams_WithLegionSlot_ShouldAssignToLegion()
    {
        // Arrange
        var service = CreateService();
        var logContent = "PLAYER_CONNECT player:2 name:\"LegionPlayer\" id:1 psr:1500\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        var instance = new GameServerInstance { Id = 1 };
        instance.Players.Add(new PlayerInfo { Name = "LegionPlayer" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Legion.Should().HaveCount(1);
        instance.PlayersByTeam.Legion[0].Name.Should().Be("LegionPlayer");
        instance.PlayersByTeam.Legion[0].Slot.Should().Be(2);
    }

    [Fact]
    public void PopulateTeams_WithHellbourneSlot_ShouldAssignToHellbourne()
    {
        // Arrange
        var service = CreateService();
        var logContent = "PLAYER_CONNECT player:7 name:\"HellbournePlayer\" id:1 psr:1500\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        var instance = new GameServerInstance { Id = 1 };
        instance.Players.Add(new PlayerInfo { Name = "HellbournePlayer" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Hellbourne.Should().HaveCount(1);
        instance.PlayersByTeam.Hellbourne[0].Name.Should().Be("HellbournePlayer");
        instance.PlayersByTeam.Hellbourne[0].Slot.Should().Be(7);
    }

    [Fact]
    public void PopulateTeams_WithSpectatorSlot_ShouldAssignToSpectators()
    {
        // Arrange
        var service = CreateService();
        var logContent = "PLAYER_CONNECT player:10 name:\"SpectatorPlayer\" id:1 psr:1500\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        var instance = new GameServerInstance { Id = 1 };
        instance.Players.Add(new PlayerInfo { Name = "SpectatorPlayer" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Spectators.Should().HaveCount(1);
        instance.PlayersByTeam.Spectators[0].Name.Should().Be("SpectatorPlayer");
    }

    [Fact]
    public void PopulateTeams_WithNoSlotInfo_ShouldAddToSpectators()
    {
        // Arrange
        var service = CreateService();
        var instance = new GameServerInstance { Id = 1 };
        instance.Players.Add(new PlayerInfo { Name = "UnknownPlayer" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Spectators.Should().HaveCount(1);
    }

    [Fact]
    public void PopulateTeams_ShouldSortBySlot()
    {
        // Arrange
        var service = CreateService();
        var logContent = 
            "PLAYER_CONNECT player:2 name:\"Player2\" id:1 psr:1500\r\n" +
            "PLAYER_CONNECT player:0 name:\"Player0\" id:2 psr:1600\r\n" +
            "PLAYER_CONNECT player:1 name:\"Player1\" id:3 psr:1700\r\n";
        var logPath = Path.Combine(_logsDir, "Slave1_test.clog");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        var instance = new GameServerInstance { Id = 1 };
        instance.Players.Add(new PlayerInfo { Name = "Player2" });
        instance.Players.Add(new PlayerInfo { Name = "Player0" });
        instance.Players.Add(new PlayerInfo { Name = "Player1" });

        // Act
        service.PopulateTeams(instance);

        // Assert
        instance.PlayersByTeam.Legion.Should().HaveCount(3);
        instance.PlayersByTeam.Legion[0].Slot.Should().Be(0);
        instance.PlayersByTeam.Legion[1].Slot.Should().Be(1);
        instance.PlayersByTeam.Legion[2].Slot.Should().Be(2);
    }

    #endregion

    #region GetMatchInfo Tests

    [Fact]
    public void GetMatchInfo_WithNoLogsDir_ShouldReturnNull()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            HonData = new HoNData { HonLogsDirectory = null }
        };
        var service = CreateService(config);

        // Act
        var result = service.GetMatchInfo(1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetMatchInfo_WithNoMatchLogFiles_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetMatchInfo(1);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetMatchInfo_WithMatchLog_ShouldExtractInfo()
    {
        // Arrange
        var service = CreateService();
        var logContent = 
            "INFO_MATCH name:\"caldavar\"\r\n" +
            "INFO_MAP name:\"test_map\"\r\n" +
            "INFO_SETTINGS mode:\"Mode_Normal\"\r\n";
        var logPath = Path.Combine(_logsDir, "M12345.log");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        // Act
        var result = service.GetMatchInfo(1);

        // Assert
        result.Should().NotBeNull();
        result!.Map.Should().Be("caldavar");
        result.Name.Should().Be("test_map");
        result.Mode.Should().Be("normal");
    }

    [Fact]
    public void GetMatchInfo_WithBotMatch_ShouldDetectAsBotMatch()
    {
        // Arrange
        var service = CreateService();
        var logContent = "INFO_SETTINGS mode:\"Mode_BotMatch\"\r\n";
        var logPath = Path.Combine(_logsDir, "M12345.log");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        // Act
        var result = service.GetMatchInfo(1);

        // Assert
        result.Should().NotBeNull();
        result!.Mode.Should().Be("botmatch");
        result.IsBotMatch.Should().BeTrue();
    }

    [Fact]
    public void GetMatchInfo_WithNormalMatch_ShouldNotBeBotMatch()
    {
        // Arrange
        var service = CreateService();
        var logContent = "INFO_SETTINGS mode:\"Mode_Normal\"\r\n";
        var logPath = Path.Combine(_logsDir, "M12345.log");
        File.WriteAllText(logPath, logContent, System.Text.Encoding.Unicode);

        // Act
        var result = service.GetMatchInfo(1);

        // Assert
        result.Should().NotBeNull();
        result!.IsBotMatch.Should().BeFalse();
    }

    #endregion
}

#region MatchInfo DTO Tests

public class MatchInfoDtoTests
{
    [Fact]
    public void MatchInfo_DefaultConstructor_ShouldInitialize()
    {
        // Act
        var info = new MatchInfo();

        // Assert
        info.Mode.Should().BeNull();
        info.Map.Should().BeNull();
        info.Name.Should().BeNull();
        info.IsBotMatch.Should().BeFalse();
    }

    [Fact]
    public void MatchInfo_IsBotMatch_WithBotMatchMode_ShouldReturnTrue()
    {
        // Act
        var info = new MatchInfo { Mode = "botmatch" };

        // Assert
        info.IsBotMatch.Should().BeTrue();
    }

    [Fact]
    public void MatchInfo_IsBotMatch_WithDifferentCase_ShouldReturnTrue()
    {
        // Act
        var info = new MatchInfo { Mode = "BotMatch" };

        // Assert
        info.IsBotMatch.Should().BeTrue();
    }

    [Fact]
    public void MatchInfo_IsBotMatch_WithNormalMode_ShouldReturnFalse()
    {
        // Act
        var info = new MatchInfo { Mode = "normal" };

        // Assert
        info.IsBotMatch.Should().BeFalse();
    }
}

#endregion
