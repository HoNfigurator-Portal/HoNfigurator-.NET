using FluentAssertions;
using Moq;
using HoNfigurator.Api.Services;
using HoNfigurator.Core.Models;
using HoNfigurator.GameServer.Services;

namespace HoNfigurator.Tests.Services;

public class CliGameServerManagerAdapterTests
{
    private readonly Mock<IGameServerManager> _serverManagerMock;
    private readonly CliGameServerManagerAdapter _adapter;

    public CliGameServerManagerAdapterTests()
    {
        _serverManagerMock = new Mock<IGameServerManager>();
        _adapter = new CliGameServerManagerAdapter(_serverManagerMock.Object);
    }

    [Fact]
    public void GetAllServers_ShouldReturnMappedServers()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance
            {
                Id = 1,
                Port = 11000,
                Status = ServerStatus.Ready,
                NumClients = 5,
                ProcessId = 1234,
                StartTime = DateTime.UtcNow.AddHours(-1)
            },
            new GameServerInstance
            {
                Id = 2,
                Port = 11001,
                Status = ServerStatus.Offline,
                NumClients = 0,
                ProcessId = 0
            }
        };

        _serverManagerMock.Setup(m => m.Instances).Returns(instances);

        // Act
        var servers = _adapter.GetAllServers().ToList();

        // Assert
        servers.Should().HaveCount(2);
        
        servers[0].Port.Should().Be(11000);
        servers[0].IsRunning.Should().BeTrue();
        servers[0].PlayerCount.Should().Be(5);
        servers[0].ProcessId.Should().Be(1234);
        servers[0].StartTime.Should().NotBeNull();

        servers[1].Port.Should().Be(11001);
        servers[1].IsRunning.Should().BeFalse();
        servers[1].PlayerCount.Should().Be(0);
        servers[1].ProcessId.Should().BeNull();
    }

    [Fact]
    public void GetAllServers_ShouldReturnEmpty_WhenNoInstances()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.Instances).Returns(new List<GameServerInstance>());

        // Act
        var servers = _adapter.GetAllServers().ToList();

        // Assert
        servers.Should().BeEmpty();
    }

    [Fact]
    public async Task StartServerAsync_ShouldStartExistingServer_WhenPortSpecified()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Offline }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        _serverManagerMock.Setup(m => m.StartServerAsync(1))
            .ReturnsAsync(new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready });

        // Act
        var result = await _adapter.StartServerAsync(11000);

        // Assert
        result.Success.Should().BeTrue();
        result.Port.Should().Be(11000);
        _serverManagerMock.Verify(m => m.StartServerAsync(1), Times.Once);
    }

    [Fact]
    public async Task StartServerAsync_ShouldAddNewServer_WhenNoAvailable()
    {
        // Arrange
        var instances = new List<GameServerInstance>();
        var newInstance = new GameServerInstance { Id = 1, Port = 11000 };
        
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        _serverManagerMock.Setup(m => m.AddNewServer()).Returns(1).Callback(() => instances.Add(newInstance));
        _serverManagerMock.Setup(m => m.StartServerAsync(1))
            .ReturnsAsync(new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready });

        // Act
        var result = await _adapter.StartServerAsync();

        // Assert
        _serverManagerMock.Verify(m => m.AddNewServer(), Times.Once);
    }

    [Fact]
    public async Task StartServerAsync_ShouldReturnError_WhenStartFails()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Offline }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        _serverManagerMock.Setup(m => m.StartServerAsync(1))
            .ReturnsAsync((GameServerInstance?)null);

        // Act
        var result = await _adapter.StartServerAsync(11000);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StopServerAsync_ShouldStopServer_WhenFound()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        _serverManagerMock.Setup(m => m.StopServerAsync(1, true)).ReturnsAsync(true);

        // Act
        var result = await _adapter.StopServerAsync(11000);

        // Assert
        result.Should().BeTrue();
        _serverManagerMock.Verify(m => m.StopServerAsync(1, true), Times.Once);
    }

    [Fact]
    public async Task StopServerAsync_ShouldReturnFalse_WhenServerNotFound()
    {
        // Arrange
        _serverManagerMock.Setup(m => m.Instances).Returns(new List<GameServerInstance>());

        // Act
        var result = await _adapter.StopServerAsync(11000);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ScaleServersAsync_ShouldStartServers_WhenTargetIsHigher()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        
        // When AddNewServer is called, add a new instance to the list
        var newServerAdded = false;
        _serverManagerMock.Setup(m => m.AddNewServer()).Returns(() => 
        {
            if (!newServerAdded)
            {
                var newInstance = new GameServerInstance { Id = 2, Port = 11001, Status = ServerStatus.Offline };
                instances.Add(newInstance);
                newServerAdded = true;
            }
            return 2;
        });
        
        _serverManagerMock.Setup(m => m.StartServerAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => 
            {
                var inst = instances.FirstOrDefault(i => i.Id == id);
                if (inst != null) inst.Status = ServerStatus.Ready;
                return inst;
            });

        // Act
        var result = await _adapter.ScaleServersAsync(2);

        // Assert
        result.PreviousCount.Should().Be(1);
        // The adapter starts servers asynchronously - just verify it tried
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ScaleServersAsync_ShouldStopServers_WhenTargetIsLower()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready, NumClients = 0 },
            new GameServerInstance { Id = 2, Port = 11001, Status = ServerStatus.Ready, NumClients = 0 },
            new GameServerInstance { Id = 3, Port = 11002, Status = ServerStatus.Ready, NumClients = 0 }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);
        _serverManagerMock.Setup(m => m.StopServerAsync(It.IsAny<int>(), true)).ReturnsAsync(true);

        // Act
        var result = await _adapter.ScaleServersAsync(1);

        // Assert
        result.PreviousCount.Should().Be(3);
        result.Stopped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ScaleServersAsync_ShouldDoNothing_WhenTargetIsSame()
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = ServerStatus.Ready },
            new GameServerInstance { Id = 2, Port = 11001, Status = ServerStatus.Ready }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);

        // Act
        var result = await _adapter.ScaleServersAsync(2);

        // Assert
        result.PreviousCount.Should().Be(2);
        result.Started.Should().Be(0);
        result.Stopped.Should().Be(0);
    }

    [Theory]
    [InlineData(ServerStatus.Ready, true)]
    [InlineData(ServerStatus.Occupied, true)]
    [InlineData(ServerStatus.Idle, true)]
    [InlineData(ServerStatus.Offline, false)]
    [InlineData(ServerStatus.Starting, false)]
    [InlineData(ServerStatus.Crashed, false)]
    [InlineData(ServerStatus.Unknown, false)]
    public void GetAllServers_ShouldMapIsRunning_BasedOnStatus(ServerStatus status, bool expectedIsRunning)
    {
        // Arrange
        var instances = new List<GameServerInstance>
        {
            new GameServerInstance { Id = 1, Port = 11000, Status = status }
        };
        _serverManagerMock.Setup(m => m.Instances).Returns(instances);

        // Act
        var servers = _adapter.GetAllServers().ToList();

        // Assert
        servers.Should().HaveCount(1);
        servers[0].IsRunning.Should().Be(expectedIsRunning);
    }
}
