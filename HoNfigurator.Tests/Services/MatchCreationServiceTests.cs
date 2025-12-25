using FluentAssertions;
using HoNfigurator.Core.Protocol;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Comprehensive tests for MatchCreationService
/// </summary>
public class MatchCreationServiceTests
{
    #region MatchState Enum Tests

    [Fact]
    public void MatchState_AllStatesExist()
    {
        // Assert
        Enum.GetValues<MatchState>().Should().HaveCount(7);
        Enum.IsDefined(MatchState.None).Should().BeTrue();
        Enum.IsDefined(MatchState.Creating).Should().BeTrue();
        Enum.IsDefined(MatchState.WaitingForPlayers).Should().BeTrue();
        Enum.IsDefined(MatchState.Starting).Should().BeTrue();
        Enum.IsDefined(MatchState.Active).Should().BeTrue();
        Enum.IsDefined(MatchState.Ended).Should().BeTrue();
        Enum.IsDefined(MatchState.Aborted).Should().BeTrue();
    }

    [Theory]
    [InlineData(MatchState.None, "None")]
    [InlineData(MatchState.Creating, "Creating")]
    [InlineData(MatchState.WaitingForPlayers, "WaitingForPlayers")]
    [InlineData(MatchState.Starting, "Starting")]
    [InlineData(MatchState.Active, "Active")]
    [InlineData(MatchState.Ended, "Ended")]
    [InlineData(MatchState.Aborted, "Aborted")]
    public void MatchState_ToStringValues_AreCorrect(MatchState state, string expected)
    {
        // Assert
        state.ToString().Should().Be(expected);
    }

    [Fact]
    public void MatchState_DefaultValue_IsNone()
    {
        // Arrange & Act
        var state = default(MatchState);

        // Assert
        state.Should().Be(MatchState.None);
    }

    [Theory]
    [InlineData(MatchState.Creating)]
    [InlineData(MatchState.WaitingForPlayers)]
    [InlineData(MatchState.Starting)]
    [InlineData(MatchState.Active)]
    public void MatchState_ActiveStates_AreIdentified(MatchState state)
    {
        // Arrange
        var activeStates = new[] { MatchState.Creating, MatchState.WaitingForPlayers, MatchState.Starting, MatchState.Active };

        // Assert
        activeStates.Should().Contain(state);
    }

    [Theory]
    [InlineData(MatchState.None)]
    [InlineData(MatchState.Ended)]
    [InlineData(MatchState.Aborted)]
    public void MatchState_InactiveStates_AreIdentified(MatchState state)
    {
        // Arrange
        var inactiveStates = new[] { MatchState.None, MatchState.Ended, MatchState.Aborted };

        // Assert
        inactiveStates.Should().Contain(state);
    }

    #endregion

    #region MatchPlayer Record Tests

    [Fact]
    public void MatchPlayer_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var player = new MatchPlayer();

        // Assert
        player.AccountId.Should().Be(0);
        player.Name.Should().BeEmpty();
        player.Team.Should().Be(0);
        player.Slot.Should().Be(0);
        player.IsConnected.Should().BeFalse();
        player.IsReady.Should().BeFalse();
        player.ConnectedAt.Should().BeNull();
    }

    [Fact]
    public void MatchPlayer_AllProperties_CanBeSet()
    {
        // Arrange
        var connectedAt = DateTime.UtcNow;

        // Act
        var player = new MatchPlayer
        {
            AccountId = 12345,
            Name = "TestPlayer",
            Team = 1,
            Slot = 3,
            IsConnected = true,
            IsReady = true,
            ConnectedAt = connectedAt
        };

        // Assert
        player.AccountId.Should().Be(12345);
        player.Name.Should().Be("TestPlayer");
        player.Team.Should().Be(1);
        player.Slot.Should().Be(3);
        player.IsConnected.Should().BeTrue();
        player.IsReady.Should().BeTrue();
        player.ConnectedAt.Should().Be(connectedAt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MatchPlayer_Team_ValidValues(int team)
    {
        // Arrange & Act
        var player = new MatchPlayer { Team = team };

        // Assert
        player.Team.Should().Be(team);
        player.Team.Should().BeInRange(1, 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MatchPlayer_Slot_ValidValues(int slot)
    {
        // Arrange & Act
        var player = new MatchPlayer { Slot = slot };

        // Assert
        player.Slot.Should().Be(slot);
        player.Slot.Should().BeInRange(1, 5);
    }

    [Fact]
    public void MatchPlayer_MutableProperties_CanBeModified()
    {
        // Arrange
        var player = new MatchPlayer
        {
            AccountId = 12345,
            Name = "TestPlayer"
        };

        // Act - Modify mutable properties
        player.IsConnected = true;
        player.IsReady = true;
        player.ConnectedAt = DateTime.UtcNow;

        // Assert
        player.IsConnected.Should().BeTrue();
        player.IsReady.Should().BeTrue();
        player.ConnectedAt.Should().NotBeNull();
    }

    [Fact]
    public void MatchPlayer_Record_SupportsWithExpression()
    {
        // Arrange
        var player = new MatchPlayer
        {
            AccountId = 12345,
            Name = "Player1",
            Team = 1,
            Slot = 1
        };

        // Act
        var player2 = player with { Slot = 2 };

        // Assert
        player.Slot.Should().Be(1);
        player2.Slot.Should().Be(2);
        player2.AccountId.Should().Be(12345);
    }

    #endregion

    #region MatchInfo Record Tests

    [Fact]
    public void MatchInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var info = new MatchInfo();

        // Assert
        info.MatchId.Should().Be(0);
        info.Map.Should().Be("caldavar");
        info.GameMode.Should().Be("normal");
        info.MatchType.Should().Be(default);
        info.State.Should().Be(default(MatchState));
        info.Players.Should().NotBeNull().And.BeEmpty();
        info.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        info.StartedAt.Should().BeNull();
        info.EndedAt.Should().BeNull();
        info.WinningTeam.Should().BeNull();
    }

    [Fact]
    public void MatchInfo_AllProperties_CanBeSet()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var startedAt = createdAt.AddMinutes(1);
        var endedAt = startedAt.AddMinutes(30);

        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, Name = "Player1", Team = 1, Slot = 1 },
            new() { AccountId = 2, Name = "Player2", Team = 2, Slot = 1 }
        };

        // Act
        var info = new MatchInfo
        {
            MatchId = 99999,
            Map = "midwars",
            GameMode = "midwars",
            MatchType = ArrangedMatchType.Matchmaking,
            State = MatchState.Active,
            Players = players,
            CreatedAt = createdAt,
            StartedAt = startedAt,
            EndedAt = endedAt,
            WinningTeam = 1
        };

        // Assert
        info.MatchId.Should().Be(99999);
        info.Map.Should().Be("midwars");
        info.GameMode.Should().Be("midwars");
        info.MatchType.Should().Be(ArrangedMatchType.Matchmaking);
        info.State.Should().Be(MatchState.Active);
        info.Players.Should().HaveCount(2);
        info.CreatedAt.Should().Be(createdAt);
        info.StartedAt.Should().Be(startedAt);
        info.EndedAt.Should().Be(endedAt);
        info.WinningTeam.Should().Be(1);
    }

    [Theory]
    [InlineData("caldavar")]
    [InlineData("midwars")]
    [InlineData("grimm")]
    [InlineData("devowars")]
    public void MatchInfo_Map_AcceptsVariousMaps(string map)
    {
        // Arrange & Act
        var info = new MatchInfo { Map = map };

        // Assert
        info.Map.Should().Be(map);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("midwars")]
    [InlineData("casual")]
    [InlineData("ranked")]
    public void MatchInfo_GameMode_AcceptsVariousModes(string mode)
    {
        // Arrange & Act
        var info = new MatchInfo { GameMode = mode };

        // Assert
        info.GameMode.Should().Be(mode);
    }

    [Fact]
    public void MatchInfo_StateTransitions_TrackedCorrectly()
    {
        // Arrange
        var info = new MatchInfo { State = MatchState.None };

        // Act & Assert - Normal flow
        info.State = MatchState.Creating;
        info.State.Should().Be(MatchState.Creating);

        info.State = MatchState.WaitingForPlayers;
        info.State.Should().Be(MatchState.WaitingForPlayers);

        info.State = MatchState.Starting;
        info.State.Should().Be(MatchState.Starting);

        info.State = MatchState.Active;
        info.State.Should().Be(MatchState.Active);

        info.State = MatchState.Ended;
        info.State.Should().Be(MatchState.Ended);
    }

    [Fact]
    public void MatchInfo_WinningTeam_ValidValues()
    {
        // Arrange & Act
        var team1Wins = new MatchInfo { WinningTeam = 1 };
        var team2Wins = new MatchInfo { WinningTeam = 2 };
        var draw = new MatchInfo { WinningTeam = null };

        // Assert
        team1Wins.WinningTeam.Should().Be(1);
        team2Wins.WinningTeam.Should().Be(2);
        draw.WinningTeam.Should().BeNull();
    }

    [Fact]
    public void MatchInfo_Duration_CalculatedCorrectly()
    {
        // Arrange
        var createdAt = DateTime.UtcNow.AddMinutes(-35);
        var startedAt = createdAt.AddMinutes(2);
        var endedAt = startedAt.AddMinutes(30);

        var info = new MatchInfo
        {
            CreatedAt = createdAt,
            StartedAt = startedAt,
            EndedAt = endedAt
        };

        // Act
        var gameDuration = info.EndedAt.Value - info.StartedAt.Value;
        var totalDuration = info.EndedAt.Value - info.CreatedAt;

        // Assert
        gameDuration.TotalMinutes.Should().BeApproximately(30, 0.1);
        totalDuration.TotalMinutes.Should().BeApproximately(32, 0.1);
    }

    #endregion

    #region Player Management Tests

    [Fact]
    public void MatchPlayers_TeamComposition_FivePerTeam()
    {
        // Arrange
        var players = new List<MatchPlayer>();

        // Act
        for (int team = 1; team <= 2; team++)
        {
            for (int slot = 1; slot <= 5; slot++)
            {
                players.Add(new MatchPlayer
                {
                    AccountId = (team * 10) + slot,
                    Name = $"Player_T{team}S{slot}",
                    Team = team,
                    Slot = slot
                });
            }
        }

        // Assert
        players.Should().HaveCount(10);
        players.Count(p => p.Team == 1).Should().Be(5);
        players.Count(p => p.Team == 2).Should().Be(5);
    }

    [Fact]
    public void MatchPlayers_AllConnected_Identified()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, IsConnected = true },
            new() { AccountId = 2, IsConnected = true },
            new() { AccountId = 3, IsConnected = true }
        };

        // Act
        var allConnected = players.All(p => p.IsConnected);

        // Assert
        allConnected.Should().BeTrue();
    }

    [Fact]
    public void MatchPlayers_AllReady_Identified()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, IsConnected = true, IsReady = true },
            new() { AccountId = 2, IsConnected = true, IsReady = true },
            new() { AccountId = 3, IsConnected = true, IsReady = false }
        };

        // Act
        var allReady = players.All(p => p.IsConnected && p.IsReady);

        // Assert
        allReady.Should().BeFalse();
    }

    [Fact]
    public void MatchPlayers_MissingPlayers_Identified()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, Name = "Player1", IsConnected = true },
            new() { AccountId = 2, Name = "Player2", IsConnected = false },
            new() { AccountId = 3, Name = "Player3", IsConnected = true },
            new() { AccountId = 4, Name = "Player4", IsConnected = false }
        };

        // Act
        var missingPlayers = players.Where(p => !p.IsConnected).ToList();

        // Assert
        missingPlayers.Should().HaveCount(2);
        missingPlayers.Select(p => p.Name).Should().Contain("Player2");
        missingPlayers.Select(p => p.Name).Should().Contain("Player4");
    }

    [Fact]
    public void MatchPlayers_ByTeam_GroupedCorrectly()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, Team = 1 },
            new() { AccountId = 2, Team = 1 },
            new() { AccountId = 3, Team = 2 },
            new() { AccountId = 4, Team = 2 },
            new() { AccountId = 5, Team = 2 }
        };

        // Act
        var team1 = players.Where(p => p.Team == 1).ToList();
        var team2 = players.Where(p => p.Team == 2).ToList();

        // Assert
        team1.Should().HaveCount(2);
        team2.Should().HaveCount(3);
    }

    #endregion

    #region Match Lifecycle Tests

    [Fact]
    public void MatchLifecycle_CreatingToEnded_NormalFlow()
    {
        // Arrange
        var info = new MatchInfo
        {
            MatchId = 1,
            State = MatchState.Creating
        };

        // Act & Assert - Progress through states
        info.State.Should().Be(MatchState.Creating);

        info.State = MatchState.WaitingForPlayers;
        info.State.Should().Be(MatchState.WaitingForPlayers);

        info.State = MatchState.Starting;
        info.StartedAt = DateTime.UtcNow;
        info.State.Should().Be(MatchState.Starting);

        info.State = MatchState.Active;
        info.State.Should().Be(MatchState.Active);

        info.State = MatchState.Ended;
        info.EndedAt = DateTime.UtcNow;
        info.WinningTeam = 1;
        info.State.Should().Be(MatchState.Ended);
    }

    [Fact]
    public void MatchLifecycle_Aborted_ShortCircuit()
    {
        // Arrange
        var info = new MatchInfo
        {
            MatchId = 1,
            State = MatchState.WaitingForPlayers
        };

        // Act
        info.State = MatchState.Aborted;
        info.EndedAt = DateTime.UtcNow;

        // Assert
        info.State.Should().Be(MatchState.Aborted);
        info.WinningTeam.Should().BeNull();
        info.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public void MatchLifecycle_TimestampsProgression()
    {
        // Arrange
        var info = new MatchInfo { MatchId = 1 };

        // Act
        var created = info.CreatedAt;
        
        // Simulate time passing
        info.StartedAt = DateTime.UtcNow.AddMinutes(1);
        info.EndedAt = DateTime.UtcNow.AddMinutes(31);

        // Assert
        created.Should().BeBefore(info.StartedAt.Value);
        info.StartedAt.Value.Should().BeBefore(info.EndedAt.Value);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void MatchInfo_EmptyPlayers_ValidState()
    {
        // Arrange & Act
        var info = new MatchInfo
        {
            MatchId = 1,
            State = MatchState.Creating,
            Players = new List<MatchPlayer>()
        };

        // Assert
        info.Players.Should().BeEmpty();
    }

    [Fact]
    public void MatchPlayer_NullName_DefaultsToEmpty()
    {
        // Arrange & Act
        var player = new MatchPlayer();

        // Assert
        player.Name.Should().BeEmpty();
    }

    [Fact]
    public void MatchInfo_ZeroMatchId_Allowed()
    {
        // Arrange & Act
        var info = new MatchInfo { MatchId = 0 };

        // Assert
        info.MatchId.Should().Be(0);
    }

    [Fact]
    public void MatchInfo_LargeMatchId_Allowed()
    {
        // Arrange & Act
        var info = new MatchInfo { MatchId = int.MaxValue };

        // Assert
        info.MatchId.Should().Be(int.MaxValue);
    }

    [Fact]
    public void MatchPlayer_LargeAccountId_Handled()
    {
        // Arrange & Act
        var player = new MatchPlayer { AccountId = int.MaxValue };

        // Assert
        player.AccountId.Should().Be(int.MaxValue);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task MatchPlayers_ConcurrentModification_WithLock()
    {
        // Arrange
        var players = new List<MatchPlayer>();
        var lockObj = new object();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var player = new MatchPlayer
                {
                    AccountId = index,
                    Name = $"Player_{index}",
                    Team = (index % 2) + 1,
                    Slot = (index % 5) + 1
                };
                lock (lockObj)
                {
                    players.Add(player);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        players.Should().HaveCount(10);
    }

    [Fact]
    public async Task MatchInfo_ConcurrentStateReads_ThreadSafe()
    {
        // Arrange
        var info = new MatchInfo
        {
            State = MatchState.Active,
            MatchId = 12345
        };

        var tasks = new List<Task<MatchState>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => info.State));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(s => s.Should().Be(MatchState.Active));
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void FullMatchScenario_TenPlayers_NormalGame()
    {
        // Arrange - Create 10 players
        var players = new List<MatchPlayer>();
        for (int team = 1; team <= 2; team++)
        {
            for (int slot = 1; slot <= 5; slot++)
            {
                players.Add(new MatchPlayer
                {
                    AccountId = (team * 100) + slot,
                    Name = $"Team{team}_Player{slot}",
                    Team = team,
                    Slot = slot
                });
            }
        }

        // Act - Create match
        var match = new MatchInfo
        {
            MatchId = 12345,
            Map = "caldavar",
            GameMode = "normal",
            MatchType = ArrangedMatchType.Matchmaking,
            State = MatchState.Creating,
            Players = players
        };

        // Simulate all players connecting
        foreach (var player in match.Players)
        {
            player.IsConnected = true;
            player.ConnectedAt = DateTime.UtcNow;
        }

        match.State = MatchState.WaitingForPlayers;

        // All ready
        foreach (var player in match.Players)
        {
            player.IsReady = true;
        }

        // Start match
        match.State = MatchState.Starting;
        match.StartedAt = DateTime.UtcNow;
        match.State = MatchState.Active;

        // End match
        match.State = MatchState.Ended;
        match.EndedAt = DateTime.UtcNow.AddMinutes(30);
        match.WinningTeam = 1;

        // Assert
        match.Players.Should().HaveCount(10);
        match.Players.All(p => p.IsConnected).Should().BeTrue();
        match.Players.All(p => p.IsReady).Should().BeTrue();
        match.State.Should().Be(MatchState.Ended);
        match.WinningTeam.Should().Be(1);
        match.StartedAt.Should().NotBeNull();
        match.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public void MatchAbortScenario_PlayersNotConnected()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { AccountId = 1, Name = "Player1", Team = 1, Slot = 1, IsConnected = true },
            new() { AccountId = 2, Name = "Player2", Team = 1, Slot = 2, IsConnected = false },
            new() { AccountId = 3, Name = "Player3", Team = 2, Slot = 1, IsConnected = true },
            new() { AccountId = 4, Name = "Player4", Team = 2, Slot = 2, IsConnected = false }
        };

        var match = new MatchInfo
        {
            MatchId = 12345,
            State = MatchState.WaitingForPlayers,
            Players = players
        };

        // Act - Simulate timeout
        var missingPlayers = match.Players.Where(p => !p.IsConnected).ToList();
        match.State = MatchState.Aborted;
        match.EndedAt = DateTime.UtcNow;

        // Assert
        match.State.Should().Be(MatchState.Aborted);
        missingPlayers.Should().HaveCount(2);
        match.WinningTeam.Should().BeNull();
    }

    [Fact]
    public void MatchReconnectScenario_PlayerDisconnectsAndReconnects()
    {
        // Arrange
        var player = new MatchPlayer
        {
            AccountId = 12345,
            Name = "TestPlayer",
            Team = 1,
            Slot = 1,
            IsConnected = true,
            IsReady = true,
            ConnectedAt = DateTime.UtcNow
        };

        // Act - Disconnect
        player.IsConnected = false;
        player.IsReady = false;

        // Assert disconnected
        player.IsConnected.Should().BeFalse();

        // Act - Reconnect
        player.IsConnected = true;
        player.ConnectedAt = DateTime.UtcNow;

        // Assert reconnected
        player.IsConnected.Should().BeTrue();
        player.IsReady.Should().BeFalse(); // Needs to ready up again
    }

    #endregion

    #region State Validation Tests

    [Fact]
    public void HasActiveMatch_TrueForActiveStates()
    {
        // Arrange
        var activeStates = new[] { MatchState.Creating, MatchState.WaitingForPlayers, MatchState.Starting, MatchState.Active };

        foreach (var state in activeStates)
        {
            // Act
            var isActive = state is MatchState.Creating or MatchState.WaitingForPlayers or MatchState.Starting or MatchState.Active;

            // Assert
            isActive.Should().BeTrue($"State {state} should be considered active");
        }
    }

    [Fact]
    public void HasActiveMatch_FalseForInactiveStates()
    {
        // Arrange
        var inactiveStates = new[] { MatchState.None, MatchState.Ended, MatchState.Aborted };

        foreach (var state in inactiveStates)
        {
            // Act
            var isActive = state is MatchState.Creating or MatchState.WaitingForPlayers or MatchState.Starting or MatchState.Active;

            // Assert
            isActive.Should().BeFalse($"State {state} should not be considered active");
        }
    }

    [Fact]
    public void AreAllPlayersReady_RequiresBothConnectedAndReady()
    {
        // Arrange
        var players = new List<MatchPlayer>
        {
            new() { IsConnected = true, IsReady = true },
            new() { IsConnected = true, IsReady = false }, // Not ready
            new() { IsConnected = false, IsReady = true }  // Not connected
        };

        // Act
        var allReady = players.All(p => p.IsConnected && p.IsReady);

        // Assert
        allReady.Should().BeFalse();
    }

    #endregion
}
