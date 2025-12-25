using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class ServerPresetServiceTests : IDisposable
{
    private readonly Mock<ILogger<ServerPresetService>> _loggerMock;
    private readonly string _testDirectory;

    public ServerPresetServiceTests()
    {
        _loggerMock = new Mock<ILogger<ServerPresetService>>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ServerPresetTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, recursive: true); } catch { }
        }
    }

    #region DTO Tests

    [Fact]
    public void ServerPreset_DefaultValues()
    {
        // Arrange & Act
        var preset = new ServerPreset();

        // Assert
        preset.Id.Should().BeEmpty();
        preset.Name.Should().BeEmpty();
        preset.Description.Should().BeEmpty();
        preset.CreatedAt.Should().Be(default);
        preset.UpdatedAt.Should().BeNull();
        preset.ImportedAt.Should().BeNull();
        preset.IsBuiltIn.Should().BeFalse();
        preset.Tags.Should().BeEmpty();
        preset.Configuration.Should().NotBeNull();
    }

    [Fact]
    public void ServerPreset_WithValues()
    {
        // Arrange & Act
        var now = DateTime.UtcNow;
        var preset = new ServerPreset
        {
            Id = "test-preset-123",
            Name = "Test Preset",
            Description = "A test preset for unit testing",
            CreatedAt = now,
            UpdatedAt = now.AddHours(1),
            IsBuiltIn = false,
            Tags = new List<string> { "test", "casual" }
        };

        // Assert
        preset.Id.Should().Be("test-preset-123");
        preset.Name.Should().Be("Test Preset");
        preset.Description.Should().Be("A test preset for unit testing");
        preset.Tags.Should().HaveCount(2);
        preset.Tags.Should().Contain("test");
        preset.Tags.Should().Contain("casual");
    }

    [Fact]
    public void GameServerConfiguration_DefaultValues()
    {
        // Arrange & Act
        var config = new GameServerConfiguration();

        // Assert
        config.ServerName.Should().Be("HoN Server");
        config.MaxPlayers.Should().Be(10);
        config.GameMode.Should().Be("normal");
        config.MapRotation.Should().BeEmpty();
        config.AllowedHeroes.Should().BeEmpty();
        config.BannedHeroes.Should().BeEmpty();
        config.Password.Should().BeNull();
        config.IsPrivate.Should().BeFalse();
        config.CustomCvars.Should().BeEmpty();
    }

    [Fact]
    public void GameServerConfiguration_WithCustomValues()
    {
        // Arrange & Act
        var config = new GameServerConfiguration
        {
            ServerName = "Custom Server",
            MaxPlayers = 8,
            GameMode = "midwars",
            MapRotation = new List<string> { "midwars", "caldavar" },
            AllowedHeroes = new List<string> { "hero1", "hero2" },
            BannedHeroes = new List<string> { "hero3" },
            Password = "secret123",
            IsPrivate = true,
            CustomCvars = new Dictionary<string, string>
            {
                ["sv_allheroaccess"] = "1",
                ["sv_noleaver"] = "1"
            }
        };

        // Assert
        config.ServerName.Should().Be("Custom Server");
        config.MaxPlayers.Should().Be(8);
        config.GameMode.Should().Be("midwars");
        config.MapRotation.Should().HaveCount(2);
        config.BannedHeroes.Should().ContainSingle();
        config.Password.Should().Be("secret123");
        config.IsPrivate.Should().BeTrue();
        config.CustomCvars.Should().HaveCount(2);
    }

    [Fact]
    public void PresetApplyResult_DefaultValues()
    {
        // Arrange & Act
        var result = new PresetApplyResult();

        // Assert
        result.Success.Should().BeFalse();
        result.PresetId.Should().BeEmpty();
        result.PresetName.Should().BeEmpty();
        result.AppliedSettings.Should().BeEmpty();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void PresetApplyResult_WithSuccess()
    {
        // Arrange & Act
        var result = new PresetApplyResult
        {
            Success = true,
            PresetId = "casual-abc123",
            PresetName = "Casual Game",
            AppliedSettings = new List<string>
            {
                "ServerName: Casual Server",
                "MaxPlayers: 10",
                "GameMode: normal",
                "CustomCvars: 2 settings"
            }
        };

        // Assert
        result.Success.Should().BeTrue();
        result.AppliedSettings.Should().HaveCount(4);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void PresetApplyResult_WithError()
    {
        // Arrange & Act
        var result = new PresetApplyResult
        {
            Success = false,
            PresetId = "invalid-preset",
            Error = "Preset not found"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Preset not found");
    }

    #endregion

    #region Built-in Presets Tests

    [Fact]
    public void GetBuiltInPresets_ReturnsDefaultPresets()
    {
        // Arrange
        var service = new ServerPresetService(_loggerMock.Object);

        // Act
        var presets = service.GetBuiltInPresets().ToList();

        // Assert
        presets.Should().HaveCountGreaterThanOrEqualTo(4);
        presets.Should().Contain(p => p.Id == "builtin-casual");
        presets.Should().Contain(p => p.Id == "builtin-ranked");
        presets.Should().Contain(p => p.Id == "builtin-midwars");
        presets.Should().Contain(p => p.Id == "builtin-custom");
    }

    [Fact]
    public void GetBuiltInPresets_AllMarkedAsBuiltIn()
    {
        // Arrange
        var service = new ServerPresetService(_loggerMock.Object);

        // Act
        var presets = service.GetBuiltInPresets().ToList();

        // Assert
        presets.Should().AllSatisfy(p => p.IsBuiltIn.Should().BeTrue());
    }

    [Fact]
    public void GetBuiltInPresets_CasualPreset_HasCorrectConfiguration()
    {
        // Arrange
        var service = new ServerPresetService(_loggerMock.Object);

        // Act
        var casual = service.GetBuiltInPresets().First(p => p.Id == "builtin-casual");

        // Assert
        casual.Name.Should().Be("Casual Game");
        casual.Tags.Should().Contain("casual");
        casual.Configuration.ServerName.Should().Be("Casual Server");
        casual.Configuration.CustomCvars.Should().ContainKey("sv_allheroaccess");
    }

    [Fact]
    public void GetBuiltInPresets_RankedPreset_HasStrictSettings()
    {
        // Arrange
        var service = new ServerPresetService(_loggerMock.Object);

        // Act
        var ranked = service.GetBuiltInPresets().First(p => p.Id == "builtin-ranked");

        // Assert
        ranked.Name.Should().Be("Ranked Match");
        ranked.Tags.Should().Contain("competitive");
        ranked.Configuration.CustomCvars["sv_noleaver"].Should().Be("1");
    }

    [Fact]
    public void GetBuiltInPresets_MidWarsPreset_HasCorrectGameMode()
    {
        // Arrange
        var service = new ServerPresetService(_loggerMock.Object);

        // Act
        var midwars = service.GetBuiltInPresets().First(p => p.Id == "builtin-midwars");

        // Assert
        midwars.Configuration.GameMode.Should().Be("midwars");
        midwars.Configuration.MapRotation.Should().Contain("midwars");
    }

    #endregion

    #region Configuration Cloning Tests

    [Fact]
    public void GameServerConfiguration_CanCloneManually()
    {
        // Arrange
        var original = new GameServerConfiguration
        {
            ServerName = "Original",
            MaxPlayers = 8,
            GameMode = "midwars",
            MapRotation = new List<string> { "map1", "map2" },
            CustomCvars = new Dictionary<string, string> { ["key"] = "value" }
        };

        // Act
        var clone = new GameServerConfiguration
        {
            ServerName = original.ServerName,
            MaxPlayers = original.MaxPlayers,
            GameMode = original.GameMode,
            MapRotation = new List<string>(original.MapRotation),
            CustomCvars = new Dictionary<string, string>(original.CustomCvars)
        };

        // Modify original
        original.ServerName = "Modified";
        original.MapRotation.Add("map3");
        original.CustomCvars["new"] = "entry";

        // Assert - Clone should be independent
        clone.ServerName.Should().Be("Original");
        clone.MapRotation.Should().HaveCount(2);
        clone.CustomCvars.Should().HaveCount(1);
    }

    #endregion

    #region Tags Tests

    [Fact]
    public void ServerPreset_Tags_CanBeModified()
    {
        // Arrange
        var preset = new ServerPreset();

        // Act
        preset.Tags.Add("casual");
        preset.Tags.Add("beginner");
        preset.Tags.Add("fun");

        // Assert
        preset.Tags.Should().HaveCount(3);
        preset.Tags.Should().Contain("beginner");
    }

    [Fact]
    public void ServerPreset_Tags_SupportLinqOperations()
    {
        // Arrange
        var preset = new ServerPreset
        {
            Tags = new List<string> { "casual", "competitive", "ranked", "fun" }
        };

        // Act
        var hasCompetitive = preset.Tags.Any(t => t.Contains("compet"));
        var longTags = preset.Tags.Where(t => t.Length > 4).ToList();

        // Assert
        hasCompetitive.Should().BeTrue();
        longTags.Should().HaveCount(3);
    }

    #endregion

    #region CustomCvars Tests

    [Fact]
    public void GameServerConfiguration_CustomCvars_CanAddEntries()
    {
        // Arrange
        var config = new GameServerConfiguration();

        // Act
        config.CustomCvars["sv_allheroaccess"] = "1";
        config.CustomCvars["sv_noleaver"] = "1";
        config.CustomCvars["sv_timeout"] = "300";

        // Assert
        config.CustomCvars.Should().HaveCount(3);
        config.CustomCvars["sv_timeout"].Should().Be("300");
    }

    [Fact]
    public void GameServerConfiguration_CustomCvars_CanUpdateEntries()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            CustomCvars = new Dictionary<string, string> { ["key"] = "original" }
        };

        // Act
        config.CustomCvars["key"] = "updated";

        // Assert
        config.CustomCvars["key"].Should().Be("updated");
    }

    #endregion

    #region Preset Collection Tests

    [Fact]
    public void Presets_CanBeSortedByName()
    {
        // Arrange
        var presets = new List<ServerPreset>
        {
            new() { Name = "Zebra Preset" },
            new() { Name = "Alpha Preset" },
            new() { Name = "Beta Preset" }
        };

        // Act
        var sorted = presets.OrderBy(p => p.Name).ToList();

        // Assert
        sorted[0].Name.Should().Be("Alpha Preset");
        sorted[1].Name.Should().Be("Beta Preset");
        sorted[2].Name.Should().Be("Zebra Preset");
    }

    [Fact]
    public void Presets_CanBeSortedByCreatedAt()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var presets = new List<ServerPreset>
        {
            new() { Name = "Old", CreatedAt = now.AddDays(-10) },
            new() { Name = "New", CreatedAt = now },
            new() { Name = "Middle", CreatedAt = now.AddDays(-5) }
        };

        // Act
        var sorted = presets.OrderByDescending(p => p.CreatedAt).ToList();

        // Assert
        sorted[0].Name.Should().Be("New");
        sorted[1].Name.Should().Be("Middle");
        sorted[2].Name.Should().Be("Old");
    }

    [Fact]
    public void Presets_CanFilterByBuiltIn()
    {
        // Arrange
        var presets = new List<ServerPreset>
        {
            new() { Name = "Built-in 1", IsBuiltIn = true },
            new() { Name = "Custom 1", IsBuiltIn = false },
            new() { Name = "Built-in 2", IsBuiltIn = true },
            new() { Name = "Custom 2", IsBuiltIn = false }
        };

        // Act
        var builtIn = presets.Where(p => p.IsBuiltIn).ToList();
        var custom = presets.Where(p => !p.IsBuiltIn).ToList();

        // Assert
        builtIn.Should().HaveCount(2);
        custom.Should().HaveCount(2);
    }

    #endregion

    #region MapRotation Tests

    [Fact]
    public void GameServerConfiguration_MapRotation_CanAddMaps()
    {
        // Arrange
        var config = new GameServerConfiguration();

        // Act
        config.MapRotation.Add("caldavar");
        config.MapRotation.Add("midwars");
        config.MapRotation.Add("grimmsforest");

        // Assert
        config.MapRotation.Should().HaveCount(3);
        config.MapRotation.Should().ContainInOrder("caldavar", "midwars", "grimmsforest");
    }

    [Fact]
    public void GameServerConfiguration_MapRotation_PreservesOrder()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            MapRotation = new List<string> { "map1", "map2", "map3" }
        };

        // Act & Assert
        config.MapRotation[0].Should().Be("map1");
        config.MapRotation[1].Should().Be("map2");
        config.MapRotation[2].Should().Be("map3");
    }

    #endregion

    #region Hero Lists Tests

    [Fact]
    public void GameServerConfiguration_AllowedHeroes_CanBeDefined()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            AllowedHeroes = new List<string> { "Pyromancer", "Glacius", "Thunderbringer" }
        };

        // Assert
        config.AllowedHeroes.Should().HaveCount(3);
        config.AllowedHeroes.Should().Contain("Glacius");
    }

    [Fact]
    public void GameServerConfiguration_BannedHeroes_CanBeDefined()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            BannedHeroes = new List<string> { "Corrupted Disciple", "Silhouette" }
        };

        // Assert
        config.BannedHeroes.Should().HaveCount(2);
        config.BannedHeroes.Should().Contain("Silhouette");
    }

    [Fact]
    public void GameServerConfiguration_AllowedAndBanned_AreIndependent()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            AllowedHeroes = new List<string> { "hero1", "hero2" },
            BannedHeroes = new List<string> { "hero3", "hero4" }
        };

        // Act
        config.AllowedHeroes.Clear();

        // Assert
        config.BannedHeroes.Should().HaveCount(2);
    }

    #endregion

    #region AppliedSettings Tests

    [Fact]
    public void PresetApplyResult_AppliedSettings_CanTrackChanges()
    {
        // Arrange
        var result = new PresetApplyResult();

        // Act
        result.AppliedSettings.Add("ServerName: Test Server");
        result.AppliedSettings.Add("MaxPlayers: 10");
        result.AppliedSettings.Add("GameMode: normal");

        // Assert
        result.AppliedSettings.Should().HaveCount(3);
        result.AppliedSettings.Should().Contain(s => s.StartsWith("ServerName:"));
    }

    [Fact]
    public void PresetApplyResult_AppliedSettings_CanBeGrouped()
    {
        // Arrange
        var result = new PresetApplyResult
        {
            AppliedSettings = new List<string>
            {
                "ServerName: Test",
                "MaxPlayers: 10",
                "CustomCvars: 5 settings",
                "Maps: 3 maps"
            }
        };

        // Act
        var cvarSettings = result.AppliedSettings.Where(s => s.Contains("Cvar")).ToList();
        var mapSettings = result.AppliedSettings.Where(s => s.Contains("Map")).ToList();

        // Assert
        cvarSettings.Should().HaveCount(1);
        mapSettings.Should().HaveCount(1);
    }

    #endregion

    #region Timestamp Tests

    [Fact]
    public void ServerPreset_Timestamps_AreNullableForOptional()
    {
        // Arrange
        var preset = new ServerPreset
        {
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        preset.CreatedAt.Should().NotBe(default);
        preset.UpdatedAt.Should().BeNull();
        preset.ImportedAt.Should().BeNull();
    }

    [Fact]
    public void ServerPreset_UpdatedAt_TracksMostRecentUpdate()
    {
        // Arrange
        var created = DateTime.UtcNow.AddDays(-5);
        var updated = DateTime.UtcNow;

        var preset = new ServerPreset
        {
            CreatedAt = created,
            UpdatedAt = updated
        };

        // Assert
        preset.UpdatedAt.Should().BeAfter(preset.CreatedAt);
    }

    [Fact]
    public void ServerPreset_ImportedAt_TracksImportTime()
    {
        // Arrange
        var originalCreated = DateTime.UtcNow.AddMonths(-1);
        var imported = DateTime.UtcNow;

        var preset = new ServerPreset
        {
            CreatedAt = originalCreated,
            ImportedAt = imported
        };

        // Assert
        preset.ImportedAt.Should().BeAfter(preset.CreatedAt);
    }

    #endregion

    #region Private Server Tests

    [Fact]
    public void GameServerConfiguration_PrivateServer_RequiresPassword()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            IsPrivate = true,
            Password = "secretpass"
        };

        // Assert
        config.IsPrivate.Should().BeTrue();
        config.Password.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GameServerConfiguration_PublicServer_PasswordOptional()
    {
        // Arrange
        var config = new GameServerConfiguration
        {
            IsPrivate = false,
            Password = null
        };

        // Assert
        config.IsPrivate.Should().BeFalse();
        config.Password.Should().BeNull();
    }

    #endregion
}
