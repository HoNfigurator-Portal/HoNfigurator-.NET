using FluentAssertions;
using HoNfigurator.Core.Models;
using System.Text.Json;

namespace HoNfigurator.Tests.Models;

/// <summary>
/// Tests for game server models and data structures
/// </summary>
public class GameServerModelsTests
{
    #region GameServerCommands Tests

    [Fact]
    public void GameServerCommands_CommandLengthBytes_ShouldBeTwoBytes()
    {
        // Arrange & Act
        var bytes = GameServerCommands.CommandLengthBytes;

        // Assert
        bytes.Should().HaveCount(2);
        bytes[0].Should().Be(0x01);
        bytes[1].Should().Be(0x00);
    }

    [Fact]
    public void GameServerCommands_ShutdownBytes_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.ShutdownBytes;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x22); // '"'
    }

    [Fact]
    public void GameServerCommands_RestartBytes_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.RestartBytes;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x23); // '#'
    }

    [Fact]
    public void GameServerCommands_SleepBytes_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.SleepBytes;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x20); // ' '
    }

    [Fact]
    public void GameServerCommands_WakeBytes_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.WakeBytes;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x21); // '!'
    }

    [Fact]
    public void GameServerCommands_MessagePrefixByte_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.MessagePrefixByte;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x24); // '$'
    }

    [Fact]
    public void GameServerCommands_CommandPrefixByte_ShouldBeCorrect()
    {
        // Arrange & Act
        var bytes = GameServerCommands.CommandPrefixByte;

        // Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be(0x25); // '%'
    }

    #endregion

    #region PlayerInfo Tests

    [Fact]
    public void PlayerInfo_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var player = new PlayerInfo();

        // Assert
        player.AccountId.Should().Be(0);
        player.Name.Should().BeEmpty();
        player.Slot.Should().Be(-1);
        player.Psr.Should().BeNull();
        player.Ip.Should().BeEmpty();
        player.Location.Should().BeEmpty();
        player.MinPing.Should().Be(0);
        player.AvgPing.Should().Be(0);
        player.MaxPing.Should().Be(0);
    }

    [Fact]
    public void PlayerInfo_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var player = new PlayerInfo
        {
            AccountId = 12345,
            Name = "TestPlayer",
            Slot = 5,
            Psr = 1750.5,
            Ip = "192.168.1.1",
            Location = "US",
            MinPing = 20,
            AvgPing = 35,
            MaxPing = 100
        };

        // Assert
        player.AccountId.Should().Be(12345);
        player.Name.Should().Be("TestPlayer");
        player.Slot.Should().Be(5);
        player.Psr.Should().Be(1750.5);
        player.Ip.Should().Be("192.168.1.1");
        player.Location.Should().Be("US");
        player.MinPing.Should().Be(20);
        player.AvgPing.Should().Be(35);
        player.MaxPing.Should().Be(100);
    }

    [Fact]
    public void PlayerInfo_Serialization_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var player = new PlayerInfo
        {
            AccountId = 999,
            Name = "JsonTest",
            Slot = 1,
            Psr = 1500.0
        };

        // Act
        var json = JsonSerializer.Serialize(player);

        // Assert
        json.Should().Contain("\"account_id\"");
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"slot\"");
        json.Should().Contain("\"psr\"");
    }

    #endregion

    #region ServerStatus Tests

    [Theory]
    [InlineData(ServerStatus.Offline, 0)]
    [InlineData(ServerStatus.Starting, 1)]
    [InlineData(ServerStatus.Ready, 2)]
    [InlineData(ServerStatus.Occupied, 3)]
    [InlineData(ServerStatus.Idle, 4)]
    [InlineData(ServerStatus.Crashed, 5)]
    [InlineData(ServerStatus.Unknown, 6)]
    public void ServerStatus_EnumValues_ShouldBeCorrect(ServerStatus status, int expected)
    {
        // Assert
        ((int)status).Should().Be(expected);
    }

    [Fact]
    public void ServerStatus_AllValues_ShouldExist()
    {
        // Arrange
        var values = Enum.GetValues<ServerStatus>();

        // Assert
        values.Should().HaveCount(7);
        values.Should().Contain(ServerStatus.Offline);
        values.Should().Contain(ServerStatus.Starting);
        values.Should().Contain(ServerStatus.Ready);
        values.Should().Contain(ServerStatus.Occupied);
        values.Should().Contain(ServerStatus.Idle);
        values.Should().Contain(ServerStatus.Crashed);
        values.Should().Contain(ServerStatus.Unknown);
    }

    #endregion

    #region GameServerInstance Tests

    [Fact]
    public void GameServerInstance_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var instance = new GameServerInstance();

        // Assert
        instance.Id.Should().Be(0);
        instance.Name.Should().BeEmpty();
        instance.Status.Should().Be(ServerStatus.Offline);
        instance.Port.Should().Be(0);
        instance.VoicePort.Should().Be(0);
        instance.NumClients.Should().Be(0);
        instance.MaxClients.Should().Be(10);
        instance.MatchId.Should().BeNull();
        instance.StartTime.Should().BeNull();
        instance.CpuPercent.Should().Be(0);
        instance.MemoryMb.Should().Be(0);
        instance.ProcessId.Should().BeNull();
        instance.GamePhase.Should().BeEmpty();
        instance.Players.Should().BeEmpty();
        instance.PlayersByTeam.Should().NotBeNull();
        instance.Enabled.Should().BeTrue();
        instance.ScheduledShutdown.Should().BeFalse();
        instance.PublicPort.Should().Be(0);
        instance.ProxyEnabled.Should().BeFalse();
    }

    [Fact]
    public void GameServerInstance_StatusString_ShouldReturnUppercaseStatus()
    {
        // Arrange
        var instance = new GameServerInstance { Status = ServerStatus.Ready };

        // Act & Assert
        instance.StatusString.Should().Be("READY");
    }

    [Theory]
    [InlineData(ServerStatus.Offline, "OFFLINE")]
    [InlineData(ServerStatus.Starting, "STARTING")]
    [InlineData(ServerStatus.Ready, "READY")]
    [InlineData(ServerStatus.Occupied, "OCCUPIED")]
    [InlineData(ServerStatus.Idle, "IDLE")]
    [InlineData(ServerStatus.Crashed, "CRASHED")]
    [InlineData(ServerStatus.Unknown, "UNKNOWN")]
    public void GameServerInstance_StatusString_AllStatuses_ShouldBeUppercase(ServerStatus status, string expected)
    {
        // Arrange
        var instance = new GameServerInstance { Status = status };

        // Act & Assert
        instance.StatusString.Should().Be(expected);
    }

    [Fact]
    public void GameServerInstance_Uptime_WhenNoStartTime_ShouldBeZero()
    {
        // Arrange
        var instance = new GameServerInstance { StartTime = null };

        // Act & Assert
        instance.Uptime.Should().Be(0);
    }

    [Fact]
    public void GameServerInstance_Uptime_WhenStartTimeSet_ShouldCalculateSeconds()
    {
        // Arrange
        var instance = new GameServerInstance 
        { 
            StartTime = DateTime.UtcNow.AddMinutes(-5) 
        };

        // Act
        var uptime = instance.Uptime;

        // Assert
        uptime.Should().BeGreaterThanOrEqualTo(299); // ~5 minutes in seconds
        uptime.Should().BeLessThan(310);
    }

    [Fact]
    public void GameServerInstance_Serialization_ShouldExcludeStatus()
    {
        // Arrange
        var instance = new GameServerInstance
        {
            Id = 1,
            Name = "Server1",
            Status = ServerStatus.Ready
        };

        // Act
        var json = JsonSerializer.Serialize(instance);

        // Assert
        json.Should().Contain("\"status\""); // StatusString property
        json.Should().Contain("READY");
    }

    [Fact]
    public void GameServerInstance_WithPlayers_ShouldStorePlayers()
    {
        // Arrange
        var instance = new GameServerInstance();
        var player = new PlayerInfo { AccountId = 1, Name = "Player1" };

        // Act
        instance.Players.Add(player);

        // Assert
        instance.Players.Should().HaveCount(1);
        instance.Players[0].Name.Should().Be("Player1");
    }

    #endregion

    #region ServerStatusResponse Tests

    [Fact]
    public void ServerStatusResponse_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var response = new ServerStatusResponse();

        // Assert
        response.ServerName.Should().BeEmpty();
        response.Version.Should().Be("1.0.0");
        response.MasterServerConnected.Should().BeFalse();
        response.ChatServerConnected.Should().BeFalse();
        response.ManagementPortalConnected.Should().BeFalse();
        response.ManagementPortalRegistered.Should().BeFalse();
        response.ManagementPortalServerName.Should().BeNull();
        response.TotalServers.Should().Be(0);
        response.OnlineServers.Should().Be(0);
        response.TotalPlayers.Should().Be(0);
        response.Instances.Should().BeEmpty();
        response.SystemStats.Should().NotBeNull();
    }

    [Fact]
    public void ServerStatusResponse_WithInstances_ShouldStoreInstances()
    {
        // Arrange
        var response = new ServerStatusResponse();
        var instance = new GameServerInstance { Id = 1, Name = "Server1" };

        // Act
        response.Instances.Add(instance);

        // Assert
        response.Instances.Should().HaveCount(1);
        response.Instances[0].Name.Should().Be("Server1");
    }

    [Fact]
    public void ServerStatusResponse_Serialization_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var response = new ServerStatusResponse
        {
            ServerName = "TestServer",
            MasterServerConnected = true,
            ManagementPortalConnected = true,
            ManagementPortalRegistered = true,
            ManagementPortalServerName = "MyServer",
            TotalServers = 5
        };

        // Act
        var json = JsonSerializer.Serialize(response);

        // Assert
        json.Should().Contain("\"server_name\"");
        json.Should().Contain("\"master_server_connected\"");
        json.Should().Contain("\"management_portal_connected\"");
        json.Should().Contain("\"management_portal_registered\"");
        json.Should().Contain("\"management_portal_server_name\"");
        json.Should().Contain("\"total_servers\"");
    }

    [Fact]
    public void ServerStatusResponse_ManagementPortalProperties_ShouldBeSettable()
    {
        // Arrange & Act
        var response = new ServerStatusResponse
        {
            ManagementPortalConnected = true,
            ManagementPortalRegistered = true,
            ManagementPortalServerName = "TestPortalServer"
        };

        // Assert
        response.ManagementPortalConnected.Should().BeTrue();
        response.ManagementPortalRegistered.Should().BeTrue();
        response.ManagementPortalServerName.Should().Be("TestPortalServer");
    }

    [Fact]
    public void ServerStatusResponse_MqttProperties_ShouldBeSettable()
    {
        // Arrange & Act
        var response = new ServerStatusResponse
        {
            MqttConnected = true,
            MqttEnabled = true,
            MqttBroker = "mqtt.honfigurator.app:8883"
        };

        // Assert
        response.MqttConnected.Should().BeTrue();
        response.MqttEnabled.Should().BeTrue();
        response.MqttBroker.Should().Be("mqtt.honfigurator.app:8883");
    }

    [Fact]
    public void ServerStatusResponse_MqttProperties_DefaultValues_ShouldBeFalseAndNull()
    {
        // Arrange & Act
        var response = new ServerStatusResponse();

        // Assert
        response.MqttConnected.Should().BeFalse();
        response.MqttEnabled.Should().BeFalse();
        response.MqttBroker.Should().BeNull();
    }

    [Fact]
    public void ServerStatusResponse_MqttSerialization_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var response = new ServerStatusResponse
        {
            MqttConnected = true,
            MqttEnabled = true,
            MqttBroker = "mqtt.test.com:1883"
        };

        // Act
        var json = JsonSerializer.Serialize(response);

        // Assert
        json.Should().Contain("\"mqtt_connected\"");
        json.Should().Contain("\"mqtt_enabled\"");
        json.Should().Contain("\"mqtt_broker\"");
    }

    #endregion

    #region PlayersByTeam Tests

    [Fact]
    public void PlayersByTeam_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var teams = new PlayersByTeam();

        // Assert
        teams.Legion.Should().BeEmpty();
        teams.Hellbourne.Should().BeEmpty();
        teams.Spectators.Should().BeEmpty();
    }

    [Fact]
    public void PlayersByTeam_WithPlayers_ShouldStoreInCorrectTeams()
    {
        // Arrange
        var teams = new PlayersByTeam();
        var legionPlayer = new PlayerInfo { AccountId = 1, Name = "Legion1" };
        var hellbournePlayer = new PlayerInfo { AccountId = 2, Name = "Hellbourne1" };
        var spectator = new PlayerInfo { AccountId = 3, Name = "Spectator1" };

        // Act
        teams.Legion.Add(legionPlayer);
        teams.Hellbourne.Add(hellbournePlayer);
        teams.Spectators.Add(spectator);

        // Assert
        teams.Legion.Should().HaveCount(1);
        teams.Hellbourne.Should().HaveCount(1);
        teams.Spectators.Should().HaveCount(1);
    }

    [Fact]
    public void PlayersByTeam_Serialization_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var teams = new PlayersByTeam();
        teams.Legion.Add(new PlayerInfo { Name = "Test" });

        // Act
        var json = JsonSerializer.Serialize(teams);

        // Assert
        json.Should().Contain("\"legion\"");
        json.Should().Contain("\"hellbourne\"");
        json.Should().Contain("\"spectators\"");
    }

    #endregion

    #region SystemStats Tests

    [Fact]
    public void SystemStats_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var stats = new SystemStats();

        // Assert
        stats.CpuUsagePercent.Should().Be(0);
        stats.MemoryUsagePercent.Should().Be(0);
        stats.TotalMemoryMb.Should().Be(0);
        stats.UsedMemoryMb.Should().Be(0);
        stats.DiskUsagePercent.Should().Be(0);
        stats.Uptime.Should().BeEmpty();
        stats.HonProcessCount.Should().Be(0);
        stats.HonTotalMemoryMb.Should().Be(0);
    }

    [Fact]
    public void SystemStats_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var stats = new SystemStats
        {
            CpuUsagePercent = 45.5,
            MemoryUsagePercent = 72.3,
            TotalMemoryMb = 16384,
            UsedMemoryMb = 11800,
            DiskUsagePercent = 55.0,
            Uptime = "5 days, 3 hours",
            HonProcessCount = 4,
            HonTotalMemoryMb = 2048
        };

        // Assert
        stats.CpuUsagePercent.Should().Be(45.5);
        stats.MemoryUsagePercent.Should().Be(72.3);
        stats.TotalMemoryMb.Should().Be(16384);
        stats.UsedMemoryMb.Should().Be(11800);
        stats.DiskUsagePercent.Should().Be(55.0);
        stats.Uptime.Should().Be("5 days, 3 hours");
        stats.HonProcessCount.Should().Be(4);
        stats.HonTotalMemoryMb.Should().Be(2048);
    }

    [Fact]
    public void SystemStats_Serialization_ShouldUseJsonPropertyNames()
    {
        // Arrange
        var stats = new SystemStats
        {
            CpuUsagePercent = 50.0,
            MemoryUsagePercent = 60.0
        };

        // Act
        var json = JsonSerializer.Serialize(stats);

        // Assert
        json.Should().Contain("\"cpu_percent\"");
        json.Should().Contain("\"memory_percent\"");
        json.Should().Contain("\"total_memory_mb\"");
        json.Should().Contain("\"used_memory_mb\"");
        json.Should().Contain("\"disk_percent\"");
        json.Should().Contain("\"hon_process_count\"");
        json.Should().Contain("\"hon_total_memory_mb\"");
    }

    #endregion

    #region Deserialization Tests

    [Fact]
    public void PlayerInfo_Deserialization_ShouldWorkWithJsonPropertyNames()
    {
        // Arrange
        var json = """{"account_id":12345,"name":"TestPlayer","slot":2,"psr":1600.5}""";

        // Act
        var player = JsonSerializer.Deserialize<PlayerInfo>(json);

        // Assert
        player.Should().NotBeNull();
        player!.AccountId.Should().Be(12345);
        player.Name.Should().Be("TestPlayer");
        player.Slot.Should().Be(2);
        player.Psr.Should().Be(1600.5);
    }

    [Fact]
    public void GameServerInstance_Deserialization_ShouldWorkWithJsonPropertyNames()
    {
        // Arrange
        var json = """{"id":1,"name":"Server1","port":11235,"max_clients":10,"enabled":true}""";

        // Act
        var instance = JsonSerializer.Deserialize<GameServerInstance>(json);

        // Assert
        instance.Should().NotBeNull();
        instance!.Id.Should().Be(1);
        instance.Name.Should().Be("Server1");
        instance.Port.Should().Be(11235);
        instance.MaxClients.Should().Be(10);
        instance.Enabled.Should().BeTrue();
    }

    [Fact]
    public void SystemStats_Deserialization_ShouldWorkWithJsonPropertyNames()
    {
        // Arrange
        var json = """{"cpu_percent":45.5,"memory_percent":70.0,"total_memory_mb":16384}""";

        // Act
        var stats = JsonSerializer.Deserialize<SystemStats>(json);

        // Assert
        stats.Should().NotBeNull();
        stats!.CpuUsagePercent.Should().Be(45.5);
        stats.MemoryUsagePercent.Should().Be(70.0);
        stats.TotalMemoryMb.Should().Be(16384);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void PlayerInfo_NullPsr_ShouldSerializeAsNull()
    {
        // Arrange
        var player = new PlayerInfo { AccountId = 1, Psr = null };

        // Act
        var json = JsonSerializer.Serialize(player);

        // Assert
        json.Should().Contain("\"psr\":null");
    }

    [Fact]
    public void GameServerInstance_NullMatchId_ShouldSerializeAsNull()
    {
        // Arrange
        var instance = new GameServerInstance { Id = 1, MatchId = null };

        // Act
        var json = JsonSerializer.Serialize(instance);

        // Assert
        json.Should().Contain("\"match_id\":null");
    }

    [Fact]
    public void GameServerInstance_EmptyPlayers_ShouldSerializeAsEmptyArray()
    {
        // Arrange
        var instance = new GameServerInstance();

        // Act
        var json = JsonSerializer.Serialize(instance);

        // Assert
        json.Should().Contain("\"players\":[]");
    }

    #endregion
}
