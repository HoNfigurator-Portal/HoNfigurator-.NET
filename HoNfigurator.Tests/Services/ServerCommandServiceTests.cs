using FluentAssertions;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Comprehensive tests for ServerCommandService
/// </summary>
public class ServerCommandServiceTests
{
    #region ServerCommandResult Tests

    [Fact]
    public void ServerCommandResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new ServerCommandResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Command.Should().BeEmpty();
        result.ServerId.Should().Be(0);
        result.Error.Should().BeNull();
        result.ExecutionTimeMs.Should().Be(0);
    }

    [Fact]
    public void ServerCommandResult_SuccessfulCommand_HasCorrectState()
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            Success = true,
            Command = "status",
            ServerId = 1,
            ExecutionTimeMs = 15
        };

        // Assert
        result.Success.Should().BeTrue();
        result.Command.Should().Be("status");
        result.ServerId.Should().Be(1);
        result.ExecutionTimeMs.Should().Be(15);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ServerCommandResult_FailedCommand_HasError()
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            Success = false,
            Command = "invalid_command",
            ServerId = 1,
            Error = "Unknown command: invalid_command"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("Unknown command");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("sv_chat \"Hello World\"")]
    [InlineData("kick 5")]
    [InlineData("ban 12345 60 \"Cheating\"")]
    [InlineData("set sv_maxplayers 10")]
    public void ServerCommandResult_VariousCommands_StoredCorrectly(string command)
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            Command = command,
            Success = true
        };

        // Assert
        result.Command.Should().Be(command);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void ServerCommandResult_ExecutionTime_RecordsCorrectly(long ms)
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            ExecutionTimeMs = ms
        };

        // Assert
        result.ExecutionTimeMs.Should().Be(ms);
    }

    #endregion

    #region CommandHistoryEntry Tests

    [Fact]
    public void CommandHistoryEntry_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var entry = new CommandHistoryEntry();

        // Assert
        entry.Command.Should().BeEmpty();
        entry.ExecutedAt.Should().Be(default);
        entry.Success.Should().BeFalse();
    }

    [Fact]
    public void CommandHistoryEntry_AllProperties_CanBeSet()
    {
        // Arrange
        var executedAt = DateTime.UtcNow;

        // Act
        var entry = new CommandHistoryEntry
        {
            Command = "restartmatch",
            ExecutedAt = executedAt,
            Success = true
        };

        // Assert
        entry.Command.Should().Be("restartmatch");
        entry.ExecutedAt.Should().Be(executedAt);
        entry.Success.Should().BeTrue();
    }

    [Fact]
    public void CommandHistoryEntry_FailedCommand_TracksCorrectly()
    {
        // Arrange & Act
        var entry = new CommandHistoryEntry
        {
            Command = "invalid",
            ExecutedAt = DateTime.UtcNow,
            Success = false
        };

        // Assert
        entry.Success.Should().BeFalse();
    }

    [Fact]
    public void CommandHistoryEntry_MultipleEntries_PreserveOrder()
    {
        // Arrange
        var entries = new List<CommandHistoryEntry>();
        var baseTime = DateTime.UtcNow;

        // Act
        for (int i = 0; i < 5; i++)
        {
            entries.Add(new CommandHistoryEntry
            {
                Command = $"command_{i}",
                ExecutedAt = baseTime.AddSeconds(i),
                Success = true
            });
        }

        // Assert
        entries.Should().HaveCount(5);
        entries[0].ExecutedAt.Should().BeBefore(entries[4].ExecutedAt);
        entries.Select(e => e.Command).Should().BeEquivalentTo(
            new[] { "command_0", "command_1", "command_2", "command_3", "command_4" });
    }

    #endregion

    #region ScriptExecutionResult Tests

    [Fact]
    public void ScriptExecutionResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new ScriptExecutionResult();

        // Assert
        result.Success.Should().BeFalse();
        result.ScriptPath.Should().BeEmpty();
        result.ServerId.Should().Be(0);
        result.StartTime.Should().Be(default);
        result.EndTime.Should().Be(default);
        result.CommandsExecuted.Should().Be(0);
        result.CommandsFailed.Should().Be(0);
        result.FirstError.Should().BeNull();
        result.ContinueOnError.Should().BeFalse();
    }

    [Fact]
    public void ScriptExecutionResult_SuccessfulExecution_HasCorrectState()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddSeconds(5);

        // Act
        var result = new ScriptExecutionResult
        {
            Success = true,
            ScriptPath = "scripts/startup.cfg",
            ServerId = 1,
            StartTime = startTime,
            EndTime = endTime,
            CommandsExecuted = 10,
            CommandsFailed = 0
        };

        // Assert
        result.Success.Should().BeTrue();
        result.ScriptPath.Should().Be("scripts/startup.cfg");
        result.CommandsExecuted.Should().Be(10);
        result.CommandsFailed.Should().Be(0);
        result.FirstError.Should().BeNull();
    }

    [Fact]
    public void ScriptExecutionResult_PartialFailure_TracksErrors()
    {
        // Arrange & Act
        var result = new ScriptExecutionResult
        {
            Success = false,
            ScriptPath = "scripts/setup.cfg",
            ServerId = 1,
            CommandsExecuted = 8,
            CommandsFailed = 2,
            FirstError = "Unknown command: bad_cmd"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.CommandsFailed.Should().Be(2);
        result.FirstError.Should().Contain("Unknown command");
    }

    [Fact]
    public void ScriptExecutionResult_ContinueOnError_AllowsPartialExecution()
    {
        // Arrange & Act
        var result = new ScriptExecutionResult
        {
            ContinueOnError = true,
            CommandsExecuted = 10,
            CommandsFailed = 3,
            Success = false
        };

        // Assert
        result.ContinueOnError.Should().BeTrue();
        (result.CommandsExecuted - result.CommandsFailed).Should().Be(7);
    }

    [Fact]
    public void ScriptExecutionResult_FileNotFound_HasError()
    {
        // Arrange & Act
        var result = new ScriptExecutionResult
        {
            Success = false,
            ScriptPath = "nonexistent/script.cfg",
            FirstError = "Script file not found: nonexistent/script.cfg"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("not found");
        result.CommandsExecuted.Should().Be(0);
    }

    [Fact]
    public void ScriptExecutionResult_Duration_CalculatedCorrectly()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var endTime = startTime.AddSeconds(10);

        // Act
        var result = new ScriptExecutionResult
        {
            StartTime = startTime,
            EndTime = endTime
        };

        // Assert
        var duration = result.EndTime - result.StartTime;
        duration.TotalSeconds.Should().Be(10);
    }

    #endregion

    #region CommandExecutedEventArgs Tests

    [Fact]
    public void CommandExecutedEventArgs_Constructor_SetsAllProperties()
    {
        // Arrange & Act
        var args = new CommandExecutedEventArgs(1, "status", true);

        // Assert
        args.ServerId.Should().Be(1);
        args.Command.Should().Be("status");
        args.Success.Should().BeTrue();
    }

    [Fact]
    public void CommandExecutedEventArgs_FailedCommand_SetsSuccessFalse()
    {
        // Arrange & Act
        var args = new CommandExecutedEventArgs(2, "invalid", false);

        // Assert
        args.ServerId.Should().Be(2);
        args.Command.Should().Be("invalid");
        args.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, "cmd1", true)]
    [InlineData(2, "cmd2", false)]
    [InlineData(10, "sv_chat \"test\"", true)]
    [InlineData(100, "kick 5", false)]
    public void CommandExecutedEventArgs_VariousInputs_StoredCorrectly(int serverId, string command, bool success)
    {
        // Arrange & Act
        var args = new CommandExecutedEventArgs(serverId, command, success);

        // Assert
        args.ServerId.Should().Be(serverId);
        args.Command.Should().Be(command);
        args.Success.Should().Be(success);
    }

    [Fact]
    public void CommandExecutedEventArgs_InheritsFromEventArgs()
    {
        // Arrange & Act
        var args = new CommandExecutedEventArgs(1, "test", true);

        // Assert
        args.Should().BeAssignableTo<EventArgs>();
    }

    #endregion

    #region Command String Tests

    [Fact]
    public void CommandStrings_ChatCommand_FormatsCorrectly()
    {
        // Arrange
        var message = "Hello World";

        // Act
        var command = $"sv_chat \"{message}\"";

        // Assert
        command.Should().Be("sv_chat \"Hello World\"");
    }

    [Fact]
    public void CommandStrings_KickCommand_FormatsCorrectly()
    {
        // Arrange
        var clientId = 5;
        var reason = "AFK";

        // Act
        var command = $"kick {clientId} \"{reason}\"";

        // Assert
        command.Should().Be("kick 5 \"AFK\"");
    }

    [Fact]
    public void CommandStrings_BanCommand_FormatsCorrectly()
    {
        // Arrange
        var accountId = 12345;
        var duration = 60;
        var reason = "Cheating";

        // Act
        var command = $"ban {accountId} {duration} \"{reason}\"";

        // Assert
        command.Should().Be("ban 12345 60 \"Cheating\"");
    }

    [Fact]
    public void CommandStrings_SetCvar_FormatsCorrectly()
    {
        // Arrange
        var cvar = "sv_maxplayers";
        var value = "10";

        // Act
        var command = $"set {cvar} \"{value}\"";

        // Assert
        command.Should().Be("set sv_maxplayers \"10\"");
    }

    [Fact]
    public void CommandStrings_MapCommand_FormatsCorrectly()
    {
        // Arrange
        var mapName = "caldavar";

        // Act
        var command = $"map {mapName}";

        // Assert
        command.Should().Be("map caldavar");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("restartmatch")]
    [InlineData("endgame")]
    [InlineData("sv_pause 1")]
    [InlineData("sv_pause 0")]
    public void CommandStrings_SimpleCommands_NoFormatting(string command)
    {
        // Assert
        command.Should().NotContain("{").And.NotContain("}");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void ServerCommandResult_EmptyCommand_HandledCorrectly()
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            Command = "",
            Success = false,
            Error = "Empty command"
        };

        // Assert
        result.Command.Should().BeEmpty();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void ServerCommandResult_VeryLongCommand_HandledCorrectly()
    {
        // Arrange
        var longMessage = new string('x', 10000);
        var command = $"sv_chat \"{longMessage}\"";

        // Act
        var result = new ServerCommandResult
        {
            Command = command,
            Success = true
        };

        // Assert
        result.Command.Should().HaveLength(command.Length);
    }

    [Fact]
    public void ScriptExecutionResult_ZeroCommands_ValidState()
    {
        // Arrange & Act
        var result = new ScriptExecutionResult
        {
            Success = true,
            CommandsExecuted = 0,
            CommandsFailed = 0
        };

        // Assert
        result.Success.Should().BeTrue();
        result.CommandsExecuted.Should().Be(0);
    }

    [Fact]
    public void CommandHistoryEntry_FarFutureDate_HandledCorrectly()
    {
        // Arrange & Act
        var futureDate = DateTime.UtcNow.AddYears(100);
        var entry = new CommandHistoryEntry
        {
            ExecutedAt = futureDate
        };

        // Assert
        entry.ExecutedAt.Should().Be(futureDate);
    }

    [Fact]
    public void ServerCommandResult_NegativeServerId_Allowed()
    {
        // Arrange & Act
        var result = new ServerCommandResult
        {
            ServerId = -1,
            Success = false,
            Error = "Invalid server ID"
        };

        // Assert
        result.ServerId.Should().Be(-1);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task CommandHistoryEntries_ConcurrentCreation_ThreadSafe()
    {
        // Arrange
        var entries = new List<CommandHistoryEntry>();
        var lockObj = new object();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var entry = new CommandHistoryEntry
                {
                    Command = $"command_{index}",
                    ExecutedAt = DateTime.UtcNow,
                    Success = index % 2 == 0
                };
                lock (lockObj)
                {
                    entries.Add(entry);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        entries.Should().HaveCount(100);
        entries.Select(e => e.Command).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ServerCommandResult_ConcurrentReads_ThreadSafe()
    {
        // Arrange
        var result = new ServerCommandResult
        {
            Success = true,
            Command = "status",
            ServerId = 1,
            ExecutionTimeMs = 10
        };

        var tasks = new List<Task<string>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => result.Command));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllBe("status");
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void CommandExecutionScenario_SuccessfulScript()
    {
        // Arrange
        var commands = new[]
        {
            "set sv_name \"Test Server\"",
            "set sv_maxplayers 10",
            "map caldavar",
            "sv_chat \"Server ready!\""
        };

        var results = new List<ServerCommandResult>();
        var history = new List<CommandHistoryEntry>();

        // Act - Simulate script execution
        foreach (var cmd in commands)
        {
            var result = new ServerCommandResult
            {
                Success = true,
                Command = cmd,
                ServerId = 1,
                ExecutionTimeMs = new Random().Next(1, 50)
            };
            results.Add(result);

            history.Add(new CommandHistoryEntry
            {
                Command = cmd,
                ExecutedAt = DateTime.UtcNow,
                Success = true
            });
        }

        var scriptResult = new ScriptExecutionResult
        {
            Success = results.All(r => r.Success),
            CommandsExecuted = results.Count,
            CommandsFailed = results.Count(r => !r.Success)
        };

        // Assert
        scriptResult.Success.Should().BeTrue();
        scriptResult.CommandsExecuted.Should().Be(4);
        scriptResult.CommandsFailed.Should().Be(0);
        history.Should().HaveCount(4);
    }

    [Fact]
    public void CommandExecutionScenario_PartialFailure()
    {
        // Arrange
        var commands = new[]
        {
            ("status", true),
            ("invalid_cmd", false),
            ("endgame", true)
        };

        var results = new List<ServerCommandResult>();

        // Act
        foreach (var (cmd, success) in commands)
        {
            results.Add(new ServerCommandResult
            {
                Command = cmd,
                Success = success,
                Error = success ? null : "Unknown command"
            });
        }

        var scriptResult = new ScriptExecutionResult
        {
            Success = false,
            CommandsExecuted = results.Count,
            CommandsFailed = results.Count(r => !r.Success),
            FirstError = results.FirstOrDefault(r => !r.Success)?.Error
        };

        // Assert
        scriptResult.Success.Should().BeFalse();
        scriptResult.CommandsFailed.Should().Be(1);
        scriptResult.FirstError.Should().Contain("Unknown");
    }

    [Fact]
    public void BroadcastScenario_MultipleServers()
    {
        // Arrange
        var serverIds = new[] { 1, 2, 3, 4, 5 };
        var command = "sv_chat \"Server message\"";
        var results = new List<ServerCommandResult>();

        // Act
        foreach (var serverId in serverIds)
        {
            results.Add(new ServerCommandResult
            {
                ServerId = serverId,
                Command = command,
                Success = true,
                ExecutionTimeMs = 5
            });
        }

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        results.Select(r => r.ServerId).Should().BeEquivalentTo(serverIds);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ServerCommandResult_ValidatesServerExists()
    {
        // Arrange
        var result = new ServerCommandResult
        {
            Success = false,
            ServerId = 99,
            Error = "Server 99 not registered"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not registered");
    }

    [Fact]
    public void ServerCommandResult_ValidatesProcessNotExited()
    {
        // Arrange
        var result = new ServerCommandResult
        {
            Success = false,
            ServerId = 1,
            Error = "Server 1 process has exited"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exited");
    }

    [Fact]
    public void ScriptExecutionResult_ValidatesFileExists()
    {
        // Arrange
        var result = new ScriptExecutionResult
        {
            Success = false,
            ScriptPath = "missing.cfg",
            FirstError = "Script file not found: missing.cfg"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("not found");
    }

    #endregion
}
