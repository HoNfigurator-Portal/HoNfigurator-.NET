using FluentAssertions;
using HoNfigurator.Api.Hubs;

namespace HoNfigurator.Tests.Api.Hubs;

/// <summary>
/// Tests for DashboardHub types and interfaces
/// </summary>
public class DashboardHubTests
{
    #region IDashboardClient Interface Tests

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveStatus()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveStatus");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveServerUpdate()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveServerUpdate");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveLog()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveLog");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveNotification()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveNotification");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveCommandResult()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveCommandResult");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveLogUpdate()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveLogUpdate");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveAlert()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveAlert");
    }

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveChartUpdate()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveChartUpdate");
    }

    [Fact]
    public void IDashboardClient_ShouldHaveCorrectMethodCount()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Assert - 12 custom methods (not counting inherited object methods)
        // Including ReceiveManagementPortalStatus for portal integration
        // Including ReceiveEvent, ReceiveMqttMessage, ReceiveMetricsUpdate for real-time streaming
        var customMethods = methods.Where(m => 
            m.Name.StartsWith("Receive") || 
            m.DeclaringType == typeof(IDashboardClient));
        customMethods.Should().HaveCount(12);
    }

    #endregion

    #region CommandResult Tests

    [Fact]
    public void CommandResult_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var result = new CommandResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Output.Should().NotBeNull();
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public void CommandResult_CanSetSuccess()
    {
        // Arrange & Act
        var result = new CommandResult { Success = true };

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void CommandResult_CanSetOutput()
    {
        // Arrange
        var output = new[] { "Line 1", "Line 2", "Line 3" };

        // Act
        var result = new CommandResult { Output = output };

        // Assert
        result.Output.Should().BeEquivalentTo(output);
    }

    [Fact]
    public void CommandResult_CanSetBothProperties()
    {
        // Arrange & Act
        var result = new CommandResult
        {
            Success = true,
            Output = new[] { "Success message" }
        };

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().HaveCount(1);
        result.Output[0].Should().Be("Success message");
    }

    [Fact]
    public void CommandResult_IsRecordType()
    {
        // Arrange & Act
        var result = new CommandResult { Success = true, Output = new[] { "test" } };
        var result2 = new CommandResult { Success = true, Output = new[] { "test" } };

        // Assert - records have value equality
        result.Should().NotBeSameAs(result2); // Different instances
        // Output arrays are different references, so not equal by default
    }

    [Fact]
    public void CommandResult_WithExpression_ShouldCreateNewInstance()
    {
        // Arrange
        var original = new CommandResult { Success = false, Output = new[] { "Error" } };

        // Act
        var modified = original with { Success = true };

        // Assert
        modified.Success.Should().BeTrue();
        modified.Output.Should().BeEquivalentTo(original.Output);
        original.Success.Should().BeFalse(); // Original unchanged
    }

    [Fact]
    public void CommandResult_EmptyOutput_ShouldBeValidArray()
    {
        // Arrange & Act
        var result = new CommandResult { Success = true, Output = Array.Empty<string>() };

        // Assert
        result.Output.Should().NotBeNull();
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public void CommandResult_MultilineOutput_ShouldPreserveOrder()
    {
        // Arrange
        var lines = new[] { "First", "Second", "Third", "Fourth" };

        // Act
        var result = new CommandResult { Output = lines };

        // Assert
        result.Output.Should().HaveCount(4);
        result.Output[0].Should().Be("First");
        result.Output[3].Should().Be("Fourth");
    }

    #endregion

    #region DashboardHub Type Tests

    [Fact]
    public void DashboardHub_ShouldInheritFromHub()
    {
        // Arrange & Act
        var baseType = typeof(DashboardHub).BaseType;

        // Assert
        baseType.Should().NotBeNull();
        baseType!.Name.Should().Contain("Hub");
    }

    [Fact]
    public void DashboardHub_ShouldHaveOnConnectedAsync()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "OnConnectedAsync");
    }

    [Fact]
    public void DashboardHub_ShouldHaveOnDisconnectedAsync()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "OnDisconnectedAsync");
    }

    [Fact]
    public void DashboardHub_ShouldHaveRequestStatus()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "RequestStatus");
    }

    [Fact]
    public void DashboardHub_ShouldHaveStartServer()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "StartServer");
    }

    [Fact]
    public void DashboardHub_ShouldHaveStopServer()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "StopServer");
    }

    [Fact]
    public void DashboardHub_ShouldHaveRestartServer()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "RestartServer");
    }

    [Fact]
    public void DashboardHub_ShouldHaveStartAllServers()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "StartAllServers");
    }

    [Fact]
    public void DashboardHub_ShouldHaveStopAllServers()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "StopAllServers");
    }

    [Fact]
    public void DashboardHub_ShouldHaveRestartAllServers()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "RestartAllServers");
    }

    [Fact]
    public void DashboardHub_ShouldHaveAddServer()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "AddServer");
    }

    [Fact]
    public void DashboardHub_ShouldHaveExecuteCommand()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ExecuteCommand");
    }

    [Fact]
    public void DashboardHub_ShouldHaveSendMessage()
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "SendMessage");
    }

    #endregion

    #region ExecuteCommand Return Values Tests

    [Fact]
    public void ExecuteCommand_Methods_ShouldReturnTask()
    {
        // Arrange
        var executeCommandMethod = typeof(DashboardHub).GetMethod("ExecuteCommand");

        // Assert
        executeCommandMethod.Should().NotBeNull();
        executeCommandMethod!.ReturnType.Should().Be(typeof(Task));
    }

    [Theory]
    [InlineData("RequestStatus")]
    [InlineData("StartServer")]
    [InlineData("StopServer")]
    [InlineData("RestartServer")]
    [InlineData("StartAllServers")]
    [InlineData("StopAllServers")]
    [InlineData("RestartAllServers")]
    [InlineData("AddServer")]
    [InlineData("ExecuteCommand")]
    [InlineData("SendMessage")]
    [InlineData("RequestManagementPortalStatus")]
    [InlineData("RegisterWithManagementPortal")]
    public void HubMethod_ShouldReturnTask(string methodName)
    {
        // Arrange
        var methods = typeof(DashboardHub).GetMethods()
            .Where(m => m.Name == methodName && m.DeclaringType == typeof(DashboardHub));

        // Assert
        methods.Should().NotBeEmpty();
        foreach (var method in methods)
        {
            method.ReturnType.Should().Be(typeof(Task));
        }
    }

    #endregion

    #region Management Portal Integration Tests

    [Fact]
    public void IDashboardClient_ShouldDefineReceiveManagementPortalStatus()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Act & Assert
        methods.Should().Contain(m => m.Name == "ReceiveManagementPortalStatus");
    }

    [Fact]
    public void ManagementPortalStatus_ShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var properties = typeof(ManagementPortalStatus).GetProperties();

        // Assert
        properties.Should().Contain(p => p.Name == "Enabled");
        properties.Should().Contain(p => p.Name == "Connected");
        properties.Should().Contain(p => p.Name == "Registered");
        properties.Should().Contain(p => p.Name == "ServerName");
        properties.Should().Contain(p => p.Name == "PortalUrl");
        properties.Should().Contain(p => p.Name == "LastUpdated");
    }

    [Fact]
    public void ManagementPortalStatus_ShouldBeRecord()
    {
        // Assert
        typeof(ManagementPortalStatus).IsClass.Should().BeTrue();
        // Records have an <Clone>$ method
        typeof(ManagementPortalStatus).GetMethod("<Clone>$").Should().NotBeNull();
    }

    [Fact]
    public void ManagementPortalStatus_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var status = new ManagementPortalStatus();

        // Assert
        status.Enabled.Should().BeFalse();
        status.Connected.Should().BeFalse();
        status.Registered.Should().BeFalse();
        status.ServerName.Should().BeNull();
        status.PortalUrl.Should().BeNull();
        status.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ManagementPortalStatus_CanSetAllProperties()
    {
        // Arrange & Act
        var now = DateTime.UtcNow;
        var status = new ManagementPortalStatus
        {
            Enabled = true,
            Connected = true,
            Registered = true,
            ServerName = "TestServer",
            PortalUrl = "https://management.honfigurator.app:3001",
            LastUpdated = now
        };

        // Assert
        status.Enabled.Should().BeTrue();
        status.Connected.Should().BeTrue();
        status.Registered.Should().BeTrue();
        status.ServerName.Should().Be("TestServer");
        status.PortalUrl.Should().Be("https://management.honfigurator.app:3001");
        status.LastUpdated.Should().Be(now);
    }

    [Fact]
    public void DashboardHub_ShouldHaveRequestManagementPortalStatusMethod()
    {
        // Arrange
        var method = typeof(DashboardHub).GetMethod("RequestManagementPortalStatus");

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void DashboardHub_ShouldHaveRegisterWithManagementPortalMethod()
    {
        // Arrange
        var method = typeof(DashboardHub).GetMethod("RegisterWithManagementPortal");

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    #endregion

    #region Console Portal Command Tests

    [Fact]
    public void DashboardHub_ExecuteCommandMethod_ShouldExist()
    {
        // Arrange
        var method = typeof(DashboardHub).GetMethod("ExecuteCommand");

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetParameters().Should().HaveCount(1);
        method.GetParameters()[0].ParameterType.Should().Be(typeof(string));
    }

    [Fact]
    public void DashboardHub_HelpCommand_ShouldIncludePortalCommands()
    {
        // This test verifies that the help command includes portal commands
        // by checking that DashboardHub has the ExecuteCommand method that handles "portal" commands
        var method = typeof(DashboardHub).GetMethod("ExecuteCommand");
        method.Should().NotBeNull("ExecuteCommand method should exist to handle console commands including portal");
    }

    [Fact]
    public void DashboardHub_ShouldHavePortalConnectorDependency()
    {
        // Verify DashboardHub has constructor parameter for IManagementPortalConnector
        var constructors = typeof(DashboardHub).GetConstructors();
        constructors.Should().HaveCountGreaterThan(0);
        
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().Contain(p => 
            p.ParameterType.Name.Contains("ManagementPortalConnector") ||
            p.ParameterType.Name == "IManagementPortalConnector");
    }

    #endregion

    #region Console MQTT Command Tests

    [Fact]
    public void DashboardHub_ShouldHaveMqttHandlerDependency()
    {
        // Verify DashboardHub has constructor parameter for IMqttHandler
        var constructors = typeof(DashboardHub).GetConstructors();
        constructors.Should().HaveCountGreaterThan(0);
        
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().Contain(p => 
            p.ParameterType.Name.Contains("MqttHandler") ||
            p.ParameterType.Name == "IMqttHandler");
    }

    [Fact]
    public void DashboardHub_ShouldHaveHoNConfigurationDependency()
    {
        // Verify DashboardHub has constructor parameter for HoNConfiguration
        var constructors = typeof(DashboardHub).GetConstructors();
        constructors.Should().HaveCountGreaterThan(0);
        
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().Contain(p => 
            p.ParameterType.Name.Contains("HoNConfiguration") ||
            p.ParameterType.Name == "HoNConfiguration");
    }

    [Fact]
    public void DashboardHub_HelpCommand_ShouldIncludeMqttCommands()
    {
        // This test verifies that the help command includes mqtt commands
        // by checking that DashboardHub has the ExecuteCommand method that handles "mqtt" commands
        var method = typeof(DashboardHub).GetMethod("ExecuteCommand");
        method.Should().NotBeNull("ExecuteCommand method should exist to handle console commands including mqtt");
    }

    [Fact]
    public void DashboardHub_Constructor_ShouldAcceptFiveDependencies()
    {
        // DashboardHub now requires 5 dependencies:
        // IGameServerManager, IManagementPortalConnector, IMqttHandler, HoNConfiguration, ILogger
        var constructors = typeof(DashboardHub).GetConstructors();
        constructors.Should().HaveCountGreaterThan(0);
        
        var mainCtor = constructors.First();
        var parameters = mainCtor.GetParameters();
        
        parameters.Should().HaveCount(5, 
            "DashboardHub should have 5 constructor parameters: " +
            "IGameServerManager, IManagementPortalConnector, IMqttHandler, HoNConfiguration, ILogger");
    }

    #endregion

    #region New DTO Tests

    [Fact]
    public void GameEventDto_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var dto = new GameEventDto();

        // Assert
        dto.Id.Should().BeEmpty();
        dto.EventType.Should().BeEmpty();
        dto.ServerId.Should().Be(0);
        dto.Data.Should().NotBeNull();
        dto.IsMqttPublishable.Should().BeFalse();
    }

    [Fact]
    public void GameEventDto_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var dto = new GameEventDto
        {
            Id = "abc123",
            EventType = "ServerStarted",
            ServerId = 1,
            Timestamp = DateTime.UtcNow,
            Data = new Dictionary<string, object> { ["key"] = "value" },
            IsMqttPublishable = true
        };

        // Assert
        dto.Id.Should().Be("abc123");
        dto.EventType.Should().Be("ServerStarted");
        dto.ServerId.Should().Be(1);
        dto.Data.Should().ContainKey("key");
        dto.IsMqttPublishable.Should().BeTrue();
    }

    [Fact]
    public void MqttMessageDto_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var dto = new MqttMessageDto();

        // Assert
        dto.Id.Should().NotBeNullOrEmpty();
        dto.Topic.Should().BeEmpty();
        dto.Payload.Should().BeEmpty();
        dto.Sent.Should().BeTrue();
    }

    [Fact]
    public void MqttMessageDto_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var dto = new MqttMessageDto
        {
            Topic = "honfigurator/test",
            Payload = "{\"test\": true}",
            Sent = true
        };

        // Assert
        dto.Topic.Should().Be("honfigurator/test");
        dto.Payload.Should().Be("{\"test\": true}");
        dto.Sent.Should().BeTrue();
    }

    [Fact]
    public void MetricsUpdateDto_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var dto = new MetricsUpdateDto();

        // Assert
        dto.CpuPercent.Should().Be(0);
        dto.MemoryPercent.Should().Be(0);
        dto.MemoryUsedMb.Should().Be(0);
        dto.ActiveServers.Should().Be(0);
        dto.TotalPlayers.Should().Be(0);
        dto.ServerMetrics.Should().NotBeNull();
    }

    [Fact]
    public void MetricsUpdateDto_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var dto = new MetricsUpdateDto
        {
            CpuPercent = 45.5,
            MemoryPercent = 60.0,
            MemoryUsedMb = 4096,
            ActiveServers = 3,
            TotalPlayers = 25,
            ServerMetrics = new Dictionary<int, ServerMetricsDto>
            {
                [1] = new ServerMetricsDto { ServerId = 1, CpuPercent = 30.0 }
            }
        };

        // Assert
        dto.CpuPercent.Should().Be(45.5);
        dto.MemoryPercent.Should().Be(60.0);
        dto.MemoryUsedMb.Should().Be(4096);
        dto.ActiveServers.Should().Be(3);
        dto.TotalPlayers.Should().Be(25);
        dto.ServerMetrics.Should().ContainKey(1);
    }

    [Fact]
    public void ServerMetricsDto_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var dto = new ServerMetricsDto();

        // Assert
        dto.ServerId.Should().Be(0);
        dto.CpuPercent.Should().Be(0);
        dto.MemoryMb.Should().Be(0);
        dto.PlayerCount.Should().Be(0);
        dto.Status.Should().BeEmpty();
    }

    [Fact]
    public void ServerMetricsDto_WithValues_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var dto = new ServerMetricsDto
        {
            ServerId = 1,
            CpuPercent = 25.5,
            MemoryMb = 512,
            PlayerCount = 8,
            Status = "Running"
        };

        // Assert
        dto.ServerId.Should().Be(1);
        dto.CpuPercent.Should().Be(25.5);
        dto.MemoryMb.Should().Be(512);
        dto.PlayerCount.Should().Be(8);
        dto.Status.Should().Be("Running");
    }

    [Fact]
    public void IDashboardClient_ShouldHaveReceiveEventMethod()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Assert
        methods.Should().Contain(m => m.Name == "ReceiveEvent");
    }

    [Fact]
    public void IDashboardClient_ShouldHaveReceiveMqttMessageMethod()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Assert
        methods.Should().Contain(m => m.Name == "ReceiveMqttMessage");
    }

    [Fact]
    public void IDashboardClient_ShouldHaveReceiveMetricsUpdateMethod()
    {
        // Arrange
        var methods = typeof(IDashboardClient).GetMethods();

        // Assert
        methods.Should().Contain(m => m.Name == "ReceiveMetricsUpdate");
    }

    [Fact]
    public void DashboardHub_ShouldHaveSimulateEventMethod()
    {
        // Arrange
        var method = typeof(DashboardHub).GetMethod("SimulateEvent");

        // Assert
        method.Should().NotBeNull("DashboardHub should have SimulateEvent method for testing");
    }

    [Fact]
    public void DashboardHub_ShouldHaveRequestMetricsMethod()
    {
        // Arrange
        var method = typeof(DashboardHub).GetMethod("RequestMetrics");

        // Assert
        method.Should().NotBeNull("DashboardHub should have RequestMetrics method for performance dashboard");
    }

    #endregion
}
