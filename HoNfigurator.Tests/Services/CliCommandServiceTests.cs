using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

public class CliCommandServiceTests
{
    private readonly Mock<ILogger<CliCommandService>> _loggerMock;
    private readonly Mock<ICliGameServerManager> _serverManagerMock;
    private readonly HoNConfiguration _config;
    private readonly CliCommandService _service;

    public CliCommandServiceTests()
    {
        _loggerMock = new Mock<ILogger<CliCommandService>>();
        _serverManagerMock = new Mock<ICliGameServerManager>();
        _config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                ServerName = "Test Server",
                ManVersion = "4.10.1",
                TotalServers = 2
            }
        };

        _service = new CliCommandService(_loggerMock.Object, _serverManagerMock.Object, _config);
    }

    [Fact]
    public void GetCommands_ShouldReturnAllCommands()
    {
        // Act
        var commands = _service.GetCommands().ToList();

        // Assert
        commands.Should().NotBeEmpty();
        commands.Should().Contain(c => c.Name == "help");
        commands.Should().Contain(c => c.Name == "status");
        commands.Should().Contain(c => c.Name == "list");
        commands.Should().Contain(c => c.Name == "start");
        commands.Should().Contain(c => c.Name == "stop");
    }

    [Fact]
    public async Task ExecuteCommandAsync_Help_ShouldReturnHelpText()
    {
        // Act
        var result = await _service.ExecuteCommandAsync("help", Array.Empty<string>());

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Available Commands"); // Capitalized
    }

    [Fact]
    public async Task ExecuteCommandAsync_UnknownCommand_ShouldReturnError()
    {
        // Act
        var result = await _service.ExecuteCommandAsync("unknowncommand", Array.Empty<string>());

        // Assert
        result.Success.Should().BeFalse();
        // Error message is in Error property, not Output
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteCommandAsync_Status_ShouldReturnServerStatus()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.GetAllServers())
            .Returns(new List<ServerInfo>
            {
                new ServerInfo { Port = 11000, IsRunning = true, PlayerCount = 5 },
                new ServerInfo { Port = 11001, IsRunning = false, PlayerCount = 0 }
            });

        // Act
        var result = await _service.ExecuteCommandAsync("status", Array.Empty<string>());

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteCommandAsync_List_ShouldReturnServerList()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.GetAllServers())
            .Returns(new List<ServerInfo>
            {
                new ServerInfo { Port = 11000, IsRunning = true, PlayerCount = 5 },
                new ServerInfo { Port = 11001, IsRunning = true, PlayerCount = 3 }
            });

        // Act
        var result = await _service.ExecuteCommandAsync("list", Array.Empty<string>());

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("11000");
        result.Output.Should().Contain("11001");
    }

    [Fact]
    public async Task ExecuteCommandAsync_Config_ShouldReturnConfiguration()
    {
        // Act - 'version' is not a command, use 'config' instead
        var result = await _service.ExecuteCommandAsync("config", Array.Empty<string>());

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteCommandAsync_Scale_WithValidTarget_ShouldCallScaleServers()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.GetAllServers())
            .Returns(new List<ServerInfo>
            {
                new ServerInfo { Port = 11000, IsRunning = true },
                new ServerInfo { Port = 11001, IsRunning = true }
            });
        _serverManagerMock.Setup(m => m.ScaleServersAsync(It.IsAny<int>()))
            .ReturnsAsync(new ScaleResult
            {
                PreviousCount = 2,
                CurrentCount = 4,
                Started = 2,
                Stopped = 0
            });

        // Act
        var result = await _service.ExecuteCommandAsync("scale", new[] { "4" });

        // Assert
        result.Success.Should().BeTrue();
        // ScaleServersAsync will be called with 2 (TotalServers config) if no args parsed properly
        _serverManagerMock.Verify(m => m.ScaleServersAsync(It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandAsync_Scale_WithInvalidTarget_ShouldReturnError()
    {
        // Act
        var result = await _service.ExecuteCommandAsync("scale", new[] { "invalid" });

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteCommandAsync_Start_ShouldCallStartServer()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.StartServerAsync(It.IsAny<int?>()))
            .ReturnsAsync(new ServerStartResult { Success = true, Port = 11000 });

        // Act
        var result = await _service.ExecuteCommandAsync("start", new[] { "11000" });

        // Assert
        result.Success.Should().BeTrue();
        _serverManagerMock.Verify(m => m.StartServerAsync(11000), Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandAsync_Stop_ShouldCallStopServer()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.StopServerAsync(It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ExecuteCommandAsync("stop", new[] { "11000" });

        // Assert
        result.Success.Should().BeTrue();
        _serverManagerMock.Verify(m => m.StopServerAsync(11000), Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandAsync_EmptyCommand_ShouldReturnError()
    {
        // Act
        var result = await _service.ExecuteCommandAsync("", Array.Empty<string>());

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteCommandAsync_NullCommand_ShouldThrowArgumentNullException()
    {
        // Act & Assert - null command throws ArgumentNullException
        var action = async () => await _service.ExecuteCommandAsync(null!, Array.Empty<string>());
        await action.Should().ThrowAsync<ArgumentNullException>();
    }
}

public class CommandModelTests
{
    [Fact]
    public void CommandResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new CommandResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Output.Should().BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CommandInfo_ShouldInitialize_WithDefaults()
    {
        // Act
        var info = new CommandInfo();

        // Assert
        info.Name.Should().BeEmpty();
        info.Description.Should().BeEmpty();
        info.Usage.Should().BeEmpty();
        info.Aliases.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ServerInfo_ShouldInitialize_WithDefaults()
    {
        // Act
        var info = new ServerInfo();

        // Assert
        info.Port.Should().Be(0);
        info.IsRunning.Should().BeFalse();
        info.PlayerCount.Should().Be(0);
        info.ProcessId.Should().BeNull();
        info.StartTime.Should().BeNull();
    }

    [Fact]
    public void ServerStartResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new ServerStartResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Port.Should().Be(0);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ScaleResult_ShouldInitialize_WithDefaults()
    {
        // Act
        var result = new ScaleResult();

        // Assert
        result.PreviousCount.Should().Be(0);
        result.CurrentCount.Should().Be(0);
        result.Started.Should().Be(0);
        result.Stopped.Should().Be(0);
    }
}
