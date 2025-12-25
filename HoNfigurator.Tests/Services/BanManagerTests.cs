using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;

namespace HoNfigurator.Tests.Services;

public class BanManagerTests
{
    private readonly Mock<ILogger<BanManager>> _loggerMock;
    private readonly BanManager _banManager;
    private readonly string _testBansPath;

    public BanManagerTests()
    {
        _loggerMock = new Mock<ILogger<BanManager>>();
        _testBansPath = Path.Combine(Path.GetTempPath(), "HoNfigurator_Tests", $"bans_{Guid.NewGuid()}.json");
        
        // Ensure test directory exists
        var dir = Path.GetDirectoryName(_testBansPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _banManager = new BanManager(_loggerMock.Object, _testBansPath);
    }

    [Fact]
    public void Constructor_ShouldInitialize_WithValidPath()
    {
        // Assert
        _banManager.Should().NotBeNull();
    }

    [Fact]
    public void GetAllBans_ShouldReturnEmptyList_WhenNoBans()
    {
        // Act
        var bans = _banManager.GetAllBans();

        // Assert
        bans.Should().NotBeNull();
        bans.Should().BeEmpty();
    }

    [Fact]
    public void BanPlayer_ShouldAddBan()
    {
        // Act
        var ban = _banManager.BanPlayer(
            accountId: 12345,
            playerName: "TestPlayer",
            reason: "Testing",
            bannedBy: "Admin"
        );

        // Assert
        ban.Should().NotBeNull();
        ban.AccountId.Should().Be(12345);
        ban.PlayerName.Should().Be("TestPlayer");
        ban.Reason.Should().Be("Testing");
        ban.BannedBy.Should().Be("Admin");
    }

    [Fact]
    public void BanPlayer_Permanent_ShouldSetCorrectType()
    {
        // Act
        var ban = _banManager.BanPlayer(
            accountId: 12345,
            playerName: "TestPlayer",
            reason: "Testing",
            bannedBy: "Admin",
            type: BanType.Permanent
        );

        // Assert
        ban.Type.Should().Be(BanType.Permanent);
        ban.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void BanPlayer_Temporary_ShouldSetExpirationTime()
    {
        // Arrange
        var duration = TimeSpan.FromHours(24);

        // Act
        var ban = _banManager.BanPlayer(
            accountId: 12345,
            playerName: "TestPlayer",
            reason: "Testing",
            bannedBy: "Admin",
            type: BanType.Temporary,
            duration: duration
        );

        // Assert
        ban.Type.Should().Be(BanType.Temporary);
        ban.ExpiresAt.Should().NotBeNull();
        ban.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.Add(duration), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IsBanned_ShouldReturnTrue_WhenPlayerIsBanned()
    {
        // Arrange
        _banManager.BanPlayer(12345, "TestPlayer", "Testing", "Admin");

        // Act
        var isBanned = _banManager.IsBanned(12345);

        // Assert
        isBanned.Should().BeTrue();
    }

    [Fact]
    public void IsBanned_ShouldReturnFalse_WhenPlayerNotBanned()
    {
        // Act
        var isBanned = _banManager.IsBanned(99999);

        // Assert
        isBanned.Should().BeFalse();
    }

    [Fact]
    public void GetBan_ShouldReturnBan_WhenExists()
    {
        // Arrange
        _banManager.BanPlayer(12345, "TestPlayer", "Testing", "Admin");

        // Act
        var ban = _banManager.GetBan(12345);

        // Assert
        ban.Should().NotBeNull();
        ban!.AccountId.Should().Be(12345);
    }

    [Fact]
    public void GetBan_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var ban = _banManager.GetBan(99999);

        // Assert
        ban.Should().BeNull();
    }

    [Fact]
    public void UnbanPlayer_ShouldRemoveBan()
    {
        // Arrange
        _banManager.BanPlayer(12345, "TestPlayer", "Testing", "Admin");

        // Act
        var result = _banManager.UnbanPlayer(12345);
        var isBanned = _banManager.IsBanned(12345);

        // Assert
        result.Should().BeTrue();
        isBanned.Should().BeFalse();
    }

    [Fact]
    public void UnbanPlayer_ShouldReturnFalse_WhenPlayerNotBanned()
    {
        // Act
        var result = _banManager.UnbanPlayer(99999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetAllBans_ShouldReturnAllBans()
    {
        // Arrange
        _banManager.BanPlayer(11111, "Player1", "Reason1", "Admin");
        _banManager.BanPlayer(22222, "Player2", "Reason2", "Admin");
        _banManager.BanPlayer(33333, "Player3", "Reason3", "Admin");

        // Act
        var bans = _banManager.GetAllBans();

        // Assert
        bans.Should().HaveCount(3);
    }

    [Fact]
    public void BanPlayer_ShouldUpdateExistingBan()
    {
        // Arrange
        _banManager.BanPlayer(12345, "TestPlayer", "Original Reason", "Admin1");

        // Act
        var updatedBan = _banManager.BanPlayer(12345, "TestPlayer", "Updated Reason", "Admin2");

        // Assert
        updatedBan.Reason.Should().Be("Updated Reason");
        updatedBan.BannedBy.Should().Be("Admin2");
        _banManager.GetAllBans().Should().HaveCount(1);
    }
}

public class BanRecordTests
{
    [Fact]
    public void BanRecord_ShouldInitialize_WithDefaults()
    {
        // Act
        var ban = new BanRecord();

        // Assert
        ban.AccountId.Should().Be(0);
        ban.PlayerName.Should().BeEmpty();
        ban.Reason.Should().BeEmpty();
        ban.BannedBy.Should().BeEmpty();
        ban.Type.Should().Be(BanType.Permanent);
        ban.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void BanRecord_IsExpired_ShouldReturnFalse_ForPermanentBan()
    {
        // Arrange
        var ban = new BanRecord
        {
            Type = BanType.Permanent
        };

        // Act & Assert
        ban.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void BanRecord_IsExpired_ShouldReturnTrue_ForExpiredTemporaryBan()
    {
        // Arrange
        var ban = new BanRecord
        {
            Type = BanType.Temporary,
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired 1 hour ago
        };

        // Act & Assert
        ban.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void BanRecord_IsExpired_ShouldReturnFalse_ForActiveTemporaryBan()
    {
        // Arrange
        var ban = new BanRecord
        {
            Type = BanType.Temporary,
            ExpiresAt = DateTime.UtcNow.AddHours(1) // Expires in 1 hour
        };

        // Act & Assert
        ban.IsExpired.Should().BeFalse();
    }
}

public class BanTypeEnumTests
{
    [Fact]
    public void BanType_ShouldHaveCorrectValues()
    {
        // Assert
        ((int)BanType.Permanent).Should().Be(0);
        ((int)BanType.Temporary).Should().Be(1);
    }
}
