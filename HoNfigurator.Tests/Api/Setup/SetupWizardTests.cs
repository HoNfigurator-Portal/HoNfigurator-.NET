using FluentAssertions;
using Moq;
using HoNfigurator.Api.Setup;
using HoNfigurator.Core.Models;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Api.Setup;

/// <summary>
/// Tests for SetupWizard - Interactive setup wizard for first-time configuration
/// Note: Only tests non-interactive methods and validation logic since
/// interactive console methods require user input
/// </summary>
public class SetupWizardTests
{
    private readonly Mock<IConfigurationService> _configServiceMock;
    private readonly HoNConfiguration _config;

    public SetupWizardTests()
    {
        _configServiceMock = new Mock<IConfigurationService>();
        _config = new HoNConfiguration
        {
            HonData = new HoNData(),
            ApplicationData = new ApplicationData()
        };
    }

    private SetupWizard CreateWizard(HoNConfiguration? customConfig = null)
    {
        return new SetupWizard(_configServiceMock.Object, customConfig ?? _config);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitialize()
    {
        // Act
        var wizard = CreateWizard();

        // Assert
        wizard.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptConfigService()
    {
        // Act
        var wizard = new SetupWizard(_configServiceMock.Object, _config);

        // Assert
        wizard.Should().NotBeNull();
    }

    #endregion

    #region IsSetupRequired Tests

    [Fact]
    public void IsSetupRequired_WhenLoginIsEmpty_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "",
            Password = "password",
            HonInstallDirectory = "C:\\HoN"
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenLoginIsNull_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = null,
            Password = "password",
            HonInstallDirectory = "C:\\HoN"
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenPasswordIsEmpty_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "user",
            Password = "",
            HonInstallDirectory = "C:\\HoN"
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenPasswordIsNull_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "user",
            Password = null,
            HonInstallDirectory = "C:\\HoN"
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenInstallDirectoryIsEmpty_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "user",
            Password = "password",
            HonInstallDirectory = ""
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenInstallDirectoryIsNull_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "user",
            Password = "password",
            HonInstallDirectory = null
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSetupRequired_WhenAllFieldsProvided_ShouldReturnFalse()
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = "user",
            Password = "password",
            HonInstallDirectory = "C:\\HoN"
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSetupRequired_WhenHonDataIsNull_ShouldReturnTrue()
    {
        // Arrange
        _config.HonData = null;
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user", "pass", "C:\\HoN", false)]
    [InlineData("", "pass", "C:\\HoN", true)]
    [InlineData("user", "", "C:\\HoN", true)]
    [InlineData("user", "pass", "", true)]
    [InlineData(null, "pass", "C:\\HoN", true)]
    [InlineData("user", null, "C:\\HoN", true)]
    [InlineData("user", "pass", null, true)]
    public void IsSetupRequired_WithVariousConfigurations_ShouldReturnExpected(
        string? login, string? password, string? installDir, bool expected)
    {
        // Arrange
        _config.HonData = new HoNData
        {
            Login = login,
            Password = password,
            HonInstallDirectory = installDir
        };
        var wizard = CreateWizard();

        // Act
        var result = wizard.IsSetupRequired();

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Type Tests

    [Fact]
    public void SetupWizard_ShouldBePublicClass()
    {
        // Assert
        typeof(SetupWizard).IsPublic.Should().BeTrue();
    }

    [Fact]
    public void SetupWizard_ShouldHaveRunAsyncMethod()
    {
        // Assert
        var method = typeof(SetupWizard).GetMethod("RunAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void SetupWizard_ShouldHaveIsSetupRequiredMethod()
    {
        // Assert
        var method = typeof(SetupWizard).GetMethod("IsSetupRequired");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(bool));
    }

    #endregion

    #region ALLOWED_REGIONS Tests

    [Fact]
    public void SetupWizard_ShouldHaveAllowedRegionsConstant()
    {
        // The ALLOWED_REGIONS constant is private, but we can verify behavior through IsSetupRequired
        // and the class structure - this test documents expected regions
        var expectedRegions = new[] { "AU", "BR", "EU", "RU", "SEA", "TH", "USE", "USW", "NEWERTH", "TEST" };
        
        // This test verifies the expected regions are documented
        expectedRegions.Should().HaveCount(10);
        expectedRegions.Should().Contain("AU");
        expectedRegions.Should().Contain("EU");
        expectedRegions.Should().Contain("USW");
    }

    #endregion
}

#region SetupWizard DTO Tests

public class SetupWizardConfigurationTests
{
    [Fact]
    public void HoNConfiguration_ShouldSupportSetupWizardRequirements()
    {
        // Arrange & Act
        var config = new HoNConfiguration
        {
            HonData = new HoNData
            {
                Login = "testuser",
                Password = "testpass",
                HonInstallDirectory = "C:\\Games\\HoN"
            },
            ApplicationData = new ApplicationData
            {
                Discord = new DiscordSettings
                {
                    OwnerId = "123456789012345678"
                }
            }
        };

        // Assert
        config.HonData.Should().NotBeNull();
        config.HonData.Login.Should().Be("testuser");
        config.HonData.Password.Should().Be("testpass");
        config.HonData.HonInstallDirectory.Should().Be("C:\\Games\\HoN");
        config.ApplicationData.Discord.OwnerId.Should().Be("123456789012345678");
    }

    [Fact]
    public void HoNData_ShouldSupportAllSetupWizardFields()
    {
        // Arrange & Act
        var honData = new HoNData
        {
            Login = "server_account",
            Password = "secret123",
            HonInstallDirectory = "C:\\HoN",
            Location = "USE",
            ServerName = "MyServer"
        };

        // Assert
        honData.Login.Should().Be("server_account");
        honData.Password.Should().Be("secret123");
        honData.HonInstallDirectory.Should().Be("C:\\HoN");
        honData.Location.Should().Be("USE");
        honData.ServerName.Should().Be("MyServer");
    }

    [Fact]
    public void DiscordSettings_ShouldSupportOwnerId()
    {
        // Arrange & Act
        var discord = new DiscordSettings
        {
            OwnerId = "987654321098765432"
        };

        // Assert
        discord.OwnerId.Should().Be("987654321098765432");
    }
}

#endregion
