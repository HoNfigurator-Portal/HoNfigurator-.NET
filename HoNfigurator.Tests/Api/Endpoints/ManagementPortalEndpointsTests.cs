using FluentAssertions;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Connectors;
using Moq;
using Xunit;

namespace HoNfigurator.Tests.Api.Endpoints;

public class ManagementPortalEndpointsTests
{
    [Fact]
    public void IManagementPortalConnector_ShouldDefineRequiredMembers()
    {
        // Arrange
        var type = typeof(IManagementPortalConnector);

        // Act
        var properties = type.GetProperties();
        var methods = type.GetMethods();

        // Assert
        properties.Should().Contain(p => p.Name == "IsEnabled");
        properties.Should().Contain(p => p.Name == "IsRegistered");
        properties.Should().Contain(p => p.Name == "ServerName");
        properties.Should().Contain(p => p.Name == "ServerAddress");
        methods.Should().Contain(m => m.Name == "RegisterServerAsync");
        methods.Should().Contain(m => m.Name == "PingManagementPortalAsync");
    }

    [Fact]
    public void RegistrationResult_Success_ShouldHaveCorrectProperties()
    {
        // Arrange & Act
        var result = new RegistrationResult
        {
            Success = true,
            Message = "Registration successful",
            ServerName = "TestServer",
            ServerAddress = "192.168.1.1:10001"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Registration successful");
        result.ServerName.Should().Be("TestServer");
        result.ServerAddress.Should().Be("192.168.1.1:10001");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void RegistrationResult_Failure_ShouldHaveErrorMessage()
    {
        // Arrange & Act
        var result = new RegistrationResult
        {
            Success = false,
            Message = "Registration failed",
            Error = "Connection refused"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Registration failed");
        result.Error.Should().Be("Connection refused");
    }

    [Fact]
    public void ManagementPortalStatus_ShouldHaveAllProperties()
    {
        // Arrange & Act
        var status = new ManagementPortalStatus
        {
            Enabled = true,
            Connected = true,
            Registered = true,
            ServerName = "TestServer",
            PortalUrl = "https://management.honfigurator.app:3001",
            LastUpdated = DateTime.UtcNow
        };

        // Assert
        status.Enabled.Should().BeTrue();
        status.Connected.Should().BeTrue();
        status.Registered.Should().BeTrue();
        status.ServerName.Should().Be("TestServer");
        status.PortalUrl.Should().Be("https://management.honfigurator.app:3001");
        status.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ManagementPortalStatus_DefaultValues_ShouldBeFalse()
    {
        // Arrange & Act
        var status = new ManagementPortalStatus();

        // Assert
        status.Enabled.Should().BeFalse();
        status.Connected.Should().BeFalse();
        status.Registered.Should().BeFalse();
        status.ServerName.Should().BeNull();
    }

    [Fact]
    public async Task MockConnector_RegisterServerAsync_ShouldReturnResult()
    {
        // Arrange
        var mockConnector = new Mock<IManagementPortalConnector>();
        mockConnector.Setup(c => c.RegisterServerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult
            {
                Success = true,
                Message = "Registered",
                ServerName = "MockServer"
            });

        // Act
        var result = await mockConnector.Object.RegisterServerAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.ServerName.Should().Be("MockServer");
    }

    [Fact]
    public async Task MockConnector_PingAsync_ShouldReturnConnectedStatus()
    {
        // Arrange
        var mockConnector = new Mock<IManagementPortalConnector>();
        mockConnector.Setup(c => c.PingManagementPortalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var isConnected = await mockConnector.Object.PingManagementPortalAsync();

        // Assert
        isConnected.Should().BeTrue();
    }

    [Fact]
    public void MockConnector_IsEnabled_ShouldReturnConfiguredValue()
    {
        // Arrange
        var mockConnector = new Mock<IManagementPortalConnector>();
        mockConnector.Setup(c => c.IsEnabled).Returns(true);

        // Act
        var isEnabled = mockConnector.Object.IsEnabled;

        // Assert
        isEnabled.Should().BeTrue();
    }

    [Fact]
    public void MockConnector_IsRegistered_ShouldReturnConfiguredValue()
    {
        // Arrange
        var mockConnector = new Mock<IManagementPortalConnector>();
        mockConnector.Setup(c => c.IsRegistered).Returns(true);

        // Act
        var isRegistered = mockConnector.Object.IsRegistered;

        // Assert
        isRegistered.Should().BeTrue();
    }

    [Fact]
    public void MockConnector_ServerName_ShouldReturnConfiguredValue()
    {
        // Arrange
        var mockConnector = new Mock<IManagementPortalConnector>();
        mockConnector.Setup(c => c.ServerName).Returns("TestPortalServer");

        // Act
        var serverName = mockConnector.Object.ServerName;

        // Assert
        serverName.Should().Be("TestPortalServer");
    }

    [Fact]
    public void RegistrationResult_DefaultValues_ShouldBeInitialized()
    {
        // Arrange & Act
        var result = new RegistrationResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().BeEmpty();
        result.ServerName.Should().BeNullOrEmpty();
        result.ServerAddress.Should().BeNullOrEmpty();
        result.Error.Should().BeNullOrEmpty();
    }
}
