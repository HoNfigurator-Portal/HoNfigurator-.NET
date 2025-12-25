using FluentAssertions;
using HoNfigurator.Core.Auth;
using Microsoft.Extensions.Logging;
using Moq;

namespace HoNfigurator.Tests.Auth;

/// <summary>
/// Tests for RolesDatabase - Role-Based Access Control system
/// </summary>
public class RolesDatabaseTests : IDisposable
{
    private readonly Mock<ILogger<RolesDatabase>> _mockLogger;
    private readonly string _testDbPath;
    private RolesDatabase _db;

    public RolesDatabaseTests()
    {
        _mockLogger = new Mock<ILogger<RolesDatabase>>();
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_roles_{Guid.NewGuid():N}.db");
        _db = new RolesDatabase(_mockLogger.Object, _testDbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    private RolesDatabase CreateFreshDb()
    {
        _db.Dispose();
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
        _db = new RolesDatabase(_mockLogger.Object, _testDbPath);
        return _db;
    }

    #region Permission Tests

    [Fact]
    public void Permission_Enum_ShouldContainExpectedValues()
    {
        var permissions = Enum.GetValues<Permission>();

        permissions.Should().Contain(Permission.ViewServers);
        permissions.Should().Contain(Permission.StartServer);
        permissions.Should().Contain(Permission.StopServer);
        permissions.Should().Contain(Permission.RestartServer);
        permissions.Should().Contain(Permission.ViewConfig);
        permissions.Should().Contain(Permission.EditConfig);
        permissions.Should().Contain(Permission.KickPlayer);
        permissions.Should().Contain(Permission.BanPlayer);
        permissions.Should().Contain(Permission.ManageUsers);
        permissions.Should().Contain(Permission.ManageRoles);
        permissions.Should().Contain(Permission.FullAccess);
    }

    [Fact]
    public void GetAllPermissions_ShouldReturnSeededPermissions()
    {
        var permissions = _db.GetAllPermissions();

        permissions.Should().NotBeEmpty();
        permissions.Should().Contain(p => p.Name == "ViewServers");
        permissions.Should().Contain(p => p.Name == "StartServer");
        permissions.Should().Contain(p => p.Name == "FullAccess");
    }

    [Fact]
    public void GetAllPermissions_ShouldHaveCategories()
    {
        var permissions = _db.GetAllPermissions();

        permissions.Should().Contain(p => p.Category == "servers");
        permissions.Should().Contain(p => p.Category == "config");
        permissions.Should().Contain(p => p.Category == "players");
        permissions.Should().Contain(p => p.Category == "admin");
    }

    [Fact]
    public void AddPermission_ShouldAddCustomPermission()
    {
        _db.AddPermission("CustomPermission", "Test permission", "custom");

        var permissions = _db.GetAllPermissions();
        permissions.Should().Contain(p => p.Name == "CustomPermission" && p.Category == "custom");
    }

    [Fact]
    public void AddPermission_Duplicate_ShouldNotFail()
    {
        _db.AddPermission("TestPerm", "Test");
        _db.AddPermission("TestPerm", "Test"); // Duplicate

        var permissions = _db.GetAllPermissions();
        permissions.Where(p => p.Name == "TestPerm").Should().HaveCount(1);
    }

    #endregion

    #region Role Tests

    [Fact]
    public void GetAllRoles_ShouldReturnDefaultRoles()
    {
        var roles = _db.GetAllRoles();

        roles.Should().Contain(r => r.Name == "Admin" && r.IsSystem);
        roles.Should().Contain(r => r.Name == "Moderator" && r.IsSystem);
        roles.Should().Contain(r => r.Name == "Viewer" && r.IsSystem);
    }

    [Fact]
    public void GetRole_ByName_ShouldReturnRole()
    {
        var role = _db.GetRole("Admin");

        role.Should().NotBeNull();
        role!.Name.Should().Be("Admin");
        role.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void GetRole_NonExistent_ShouldReturnNull()
    {
        var role = _db.GetRole("NonExistentRole");

        role.Should().BeNull();
    }

    [Fact]
    public void CreateRole_ShouldCreateNewRole()
    {
        var roleId = _db.CreateRole("TestRole", "Test description");

        roleId.Should().BeGreaterThan(0);
        
        var role = _db.GetRole(roleId);
        role.Should().NotBeNull();
        role!.Name.Should().Be("TestRole");
        role.Description.Should().Be("Test description");
        role.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void CreateRole_WithSystem_ShouldCreateSystemRole()
    {
        var roleId = _db.CreateRole("SystemTestRole", "System role", isSystem: true);

        var role = _db.GetRole(roleId);
        role.Should().NotBeNull();
        role!.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void UpdateRole_ShouldUpdateNonSystemRole()
    {
        var roleId = _db.CreateRole("UpdateTest", "Original");

        var updated = _db.UpdateRole(roleId, name: "UpdatedName", description: "Updated desc");

        updated.Should().BeTrue();
        var role = _db.GetRole(roleId);
        role!.Name.Should().Be("UpdatedName");
        role.Description.Should().Be("Updated desc");
    }

    [Fact]
    public void UpdateRole_SystemRole_ShouldNotUpdate()
    {
        var adminRole = _db.GetRole("Admin");

        var updated = _db.UpdateRole(adminRole!.Id, name: "HackedAdmin");

        updated.Should().BeFalse();
        var role = _db.GetRole(adminRole.Id);
        role!.Name.Should().Be("Admin");
    }

    [Fact]
    public void DeleteRole_ShouldDeleteNonSystemRole()
    {
        var roleId = _db.CreateRole("DeleteTest");

        var deleted = _db.DeleteRole(roleId);

        deleted.Should().BeTrue();
        _db.GetRole(roleId).Should().BeNull();
    }

    [Fact]
    public void DeleteRole_SystemRole_ShouldNotDelete()
    {
        var adminRole = _db.GetRole("Admin");

        var deleted = _db.DeleteRole(adminRole!.Id);

        deleted.Should().BeFalse();
        _db.GetRole("Admin").Should().NotBeNull();
    }

    [Fact]
    public void DeleteRole_ByName_ShouldDeleteRole()
    {
        _db.CreateRole("DeleteByNameTest");

        var deleted = _db.DeleteRole("DeleteByNameTest");

        deleted.Should().BeTrue();
        _db.GetRole("DeleteByNameTest").Should().BeNull();
    }

    #endregion

    #region Role Permission Tests

    [Fact]
    public void AdminRole_ShouldHaveAllPermissions()
    {
        var role = _db.GetRole("Admin");

        role.Should().NotBeNull();
        role!.Permissions.Should().Contain(Permission.FullAccess.ToString());
    }

    [Fact]
    public void ModeratorRole_ShouldHaveLimitedPermissions()
    {
        var role = _db.GetRole("Moderator");

        role.Should().NotBeNull();
        role!.Permissions.Should().Contain(Permission.ViewServers.ToString());
        role.Permissions.Should().Contain(Permission.BanPlayer.ToString());
        role.Permissions.Should().NotContain(Permission.FullAccess.ToString());
        role.Permissions.Should().NotContain(Permission.ManageUsers.ToString());
    }

    [Fact]
    public void ViewerRole_ShouldHaveOnlyViewPermissions()
    {
        var role = _db.GetRole("Viewer");

        role.Should().NotBeNull();
        role!.Permissions.Should().Contain(Permission.ViewServers.ToString());
        role.Permissions.Should().Contain(Permission.ViewLogs.ToString());
        role.Permissions.Should().NotContain(Permission.StartServer.ToString());
        role.Permissions.Should().NotContain(Permission.BanPlayer.ToString());
    }

    [Fact]
    public void AssignPermissionToRole_ShouldAddPermission()
    {
        var roleId = _db.CreateRole("PermTest");

        _db.AssignPermissionToRole(roleId, Permission.ViewServers.ToString());

        var permissions = _db.GetRolePermissions(roleId);
        permissions.Should().Contain(Permission.ViewServers.ToString());
    }

    [Fact]
    public void RemovePermissionFromRole_ShouldRemovePermission()
    {
        var roleId = _db.CreateRole("RemovePermTest");
        _db.AssignPermissionToRole(roleId, Permission.ViewServers.ToString());
        _db.AssignPermissionToRole(roleId, Permission.ViewConfig.ToString());

        _db.RemovePermissionFromRole(roleId, Permission.ViewServers.ToString());

        var permissions = _db.GetRolePermissions(roleId);
        permissions.Should().NotContain(Permission.ViewServers.ToString());
        permissions.Should().Contain(Permission.ViewConfig.ToString());
    }

    [Fact]
    public void SetRolePermissions_ShouldReplaceAllPermissions()
    {
        var roleId = _db.CreateRole("SetPermTest");
        _db.AssignPermissionToRole(roleId, Permission.ViewServers.ToString());
        _db.AssignPermissionToRole(roleId, Permission.ViewConfig.ToString());

        _db.SetRolePermissions(roleId, new[] { Permission.ViewLogs.ToString(), Permission.ViewMetrics.ToString() });

        var permissions = _db.GetRolePermissions(roleId);
        permissions.Should().NotContain(Permission.ViewServers.ToString());
        permissions.Should().NotContain(Permission.ViewConfig.ToString());
        permissions.Should().Contain(Permission.ViewLogs.ToString());
        permissions.Should().Contain(Permission.ViewMetrics.ToString());
    }

    #endregion

    #region User Tests

    [Fact]
    public void CreateUser_ShouldCreateNewUser()
    {
        var userId = _db.CreateUser("testuser", "hashedpassword");

        userId.Should().BeGreaterThan(0);
        
        var user = _db.GetUser("testuser");
        user.Should().NotBeNull();
        user!.Username.Should().Be("testuser");
        user.PasswordHash.Should().Be("hashedpassword");
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CreateUser_WithRole_ShouldAssignRole()
    {
        var adminRole = _db.GetRole("Admin");
        var userId = _db.CreateUser("adminuser", "hash", roleId: adminRole!.Id);

        var user = _db.GetUser("adminuser");
        user!.RoleId.Should().Be(adminRole.Id);
        user.RoleName.Should().Be("Admin");
        user.Permissions.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateUser_WithDiscordId_ShouldStoreDiscordId()
    {
        var userId = _db.CreateUser("discorduser", "hash", discordId: "123456789");

        var user = _db.GetUser("discorduser");
        user!.DiscordId.Should().Be("123456789");
    }

    [Fact]
    public void CreateUser_Duplicate_ShouldReturnNegative()
    {
        _db.CreateUser("duplicateuser", "hash");

        var result = _db.CreateUser("duplicateuser", "hash2");

        result.Should().Be(-1);
    }

    [Fact]
    public void GetUser_CaseInsensitive_ShouldMatch()
    {
        _db.CreateUser("TestUser", "hash");

        var user = _db.GetUser("TESTUSER");

        user.Should().NotBeNull();
        user!.Username.Should().Be("TestUser");
    }

    [Fact]
    public void GetUserByDiscordId_ShouldReturnUser()
    {
        _db.CreateUser("discordlookup", "hash", discordId: "987654321");

        var user = _db.GetUserByDiscordId("987654321");

        user.Should().NotBeNull();
        user!.Username.Should().Be("discordlookup");
    }

    [Fact]
    public void GetUserByDiscordId_NotFound_ShouldReturnNull()
    {
        var user = _db.GetUserByDiscordId("nonexistent");

        user.Should().BeNull();
    }

    [Fact]
    public void GetAllUsers_ShouldReturnAllUsers()
    {
        _db.CreateUser("user1", "hash1");
        _db.CreateUser("user2", "hash2");
        _db.CreateUser("user3", "hash3");

        var users = _db.GetAllUsers();

        users.Should().HaveCountGreaterThanOrEqualTo(3);
        users.Should().Contain(u => u.Username == "user1");
        users.Should().Contain(u => u.Username == "user2");
        users.Should().Contain(u => u.Username == "user3");
    }

    [Fact]
    public void UpdateUser_Password_ShouldUpdatePassword()
    {
        var userId = _db.CreateUser("updatepwuser", "oldhash");

        var updated = _db.UpdateUser(userId, passwordHash: "newhash");

        updated.Should().BeTrue();
        var user = _db.GetUser("updatepwuser");
        user!.PasswordHash.Should().Be("newhash");
    }

    [Fact]
    public void UpdateUser_Role_ShouldUpdateRole()
    {
        var userId = _db.CreateUser("updateroleuser", "hash");
        var adminRole = _db.GetRole("Admin");

        _db.UpdateUser(userId, roleId: adminRole!.Id);

        var user = _db.GetUser("updateroleuser");
        user!.RoleId.Should().Be(adminRole.Id);
    }

    [Fact]
    public void UpdateUser_Deactivate_ShouldDeactivateUser()
    {
        var userId = _db.CreateUser("deactivateuser", "hash");

        _db.UpdateUser(userId, isActive: false);

        var user = _db.GetUser("deactivateuser");
        user!.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DeleteUser_ShouldRemoveUser()
    {
        var userId = _db.CreateUser("deleteuser", "hash");

        var deleted = _db.DeleteUser(userId);

        deleted.Should().BeTrue();
        _db.GetUser("deleteuser").Should().BeNull();
    }

    [Fact]
    public void UpdateLastLogin_ShouldUpdateTimestamp()
    {
        var userId = _db.CreateUser("loginuser", "hash");
        var userBefore = _db.GetUser("loginuser");
        userBefore!.LastLogin.Should().BeNull();

        _db.UpdateLastLogin(userId);

        var userAfter = _db.GetUser("loginuser");
        userAfter!.LastLogin.Should().NotBeNull();
    }

    #endregion

    #region Permission Check Tests

    [Fact]
    public void HasPermission_WithPermission_ShouldReturnTrue()
    {
        var modRole = _db.GetRole("Moderator");
        var userId = _db.CreateUser("permcheckuser", "hash", roleId: modRole!.Id);

        var hasViewServers = _db.HasPermission(userId, Permission.ViewServers);

        hasViewServers.Should().BeTrue();
    }

    [Fact]
    public void HasPermission_WithoutPermission_ShouldReturnFalse()
    {
        var viewerRole = _db.GetRole("Viewer");
        var userId = _db.CreateUser("nopermuser", "hash", roleId: viewerRole!.Id);

        var hasStartServer = _db.HasPermission(userId, Permission.StartServer);

        hasStartServer.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_AdminWithFullAccess_ShouldReturnTrueForAnything()
    {
        var adminRole = _db.GetRole("Admin");
        var userId = _db.CreateUser("fullaccessuser", "hash", roleId: adminRole!.Id);

        _db.HasPermission(userId, Permission.ViewServers).Should().BeTrue();
        _db.HasPermission(userId, Permission.ManageUsers).Should().BeTrue();
        _db.HasPermission(userId, Permission.EditConfig).Should().BeTrue();
    }

    [Fact]
    public void HasPermission_StringOverload_ShouldWork()
    {
        var modRole = _db.GetRole("Moderator");
        var userId = _db.CreateUser("stringpermuser", "hash", roleId: modRole!.Id);

        var hasPerm = _db.HasPermission(userId, "ViewServers");

        hasPerm.Should().BeTrue();
    }

    #endregion

    #region API Key Tests

    [Fact]
    public void CreateApiKey_ShouldReturnKey()
    {
        var key = _db.CreateApiKey("TestKey");

        key.Should().NotBeNullOrEmpty();
        key.Should().StartWith("hfg_");
    }

    [Fact]
    public void ValidateApiKey_ValidKey_ShouldReturnInfo()
    {
        var key = _db.CreateApiKey("ValidTestKey");

        var keyInfo = _db.ValidateApiKey(key);

        keyInfo.Should().NotBeNull();
        keyInfo!.Name.Should().Be("ValidTestKey");
    }

    [Fact]
    public void ValidateApiKey_InvalidKey_ShouldReturnNull()
    {
        var keyInfo = _db.ValidateApiKey("invalid_key_12345");

        keyInfo.Should().BeNull();
    }

    [Fact]
    public void CreateApiKey_WithUser_ShouldAssociateUser()
    {
        var userId = _db.CreateUser("apikeyuser", "hash");
        var key = _db.CreateApiKey("UserKey", userId: userId);

        var keyInfo = _db.ValidateApiKey(key);
        keyInfo!.UserId.Should().Be(userId);
    }

    [Fact]
    public void CreateApiKey_WithPermissions_ShouldStorePermissions()
    {
        var perms = new[] { "ViewServers", "ViewConfig" };
        var key = _db.CreateApiKey("PermKey", permissions: perms);

        var keyInfo = _db.ValidateApiKey(key);
        keyInfo!.Permissions.Should().BeEquivalentTo(perms);
    }

    [Fact]
    public void CreateApiKey_WithExpiration_ShouldStoreExpiration()
    {
        var expiry = DateTime.UtcNow.AddDays(30);
        var key = _db.CreateApiKey("ExpiringKey", expiresAt: expiry);

        var keyInfo = _db.ValidateApiKey(key);
        keyInfo!.ExpiresAt.Should().BeCloseTo(expiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ValidateApiKey_ExpiredKey_ShouldReturnNull()
    {
        var expiry = DateTime.UtcNow.AddDays(-1); // Already expired
        var key = _db.CreateApiKey("ExpiredKey", expiresAt: expiry);

        var keyInfo = _db.ValidateApiKey(key);

        keyInfo.Should().BeNull();
    }

    [Fact]
    public void RevokeApiKey_ShouldInvalidateKey()
    {
        var key = _db.CreateApiKey("RevokeKey");
        var keyInfo = _db.ValidateApiKey(key);
        keyInfo.Should().NotBeNull();

        var revoked = _db.RevokeApiKey(keyInfo!.Id);

        revoked.Should().BeTrue();
        _db.ValidateApiKey(key).Should().BeNull();
    }

    [Fact]
    public void GetApiKeys_ShouldReturnAllKeys()
    {
        _db.CreateApiKey("Key1");
        _db.CreateApiKey("Key2");
        _db.CreateApiKey("Key3");

        var keys = _db.GetApiKeys();

        keys.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void GetApiKeys_FilterByUser_ShouldReturnUserKeys()
    {
        var userId = _db.CreateUser("apikeyfilteruser", "hash");
        _db.CreateApiKey("UserKey1", userId: userId);
        _db.CreateApiKey("UserKey2", userId: userId);
        _db.CreateApiKey("OtherKey"); // No user

        var userKeys = _db.GetApiKeys(userId);

        userKeys.Should().HaveCount(2);
        userKeys.Should().OnlyContain(k => k.UserId == userId);
    }

    [Fact]
    public void ValidateApiKey_ShouldUpdateLastUsed()
    {
        var key = _db.CreateApiKey("LastUsedKey");

        _db.ValidateApiKey(key);

        var keys = _db.GetApiKeys();
        var keyInfo = keys.FirstOrDefault(k => k.Name == "LastUsedKey");
        keyInfo!.LastUsed.Should().NotBeNull();
    }

    #endregion

    #region Model Tests

    [Fact]
    public void PermissionInfo_ShouldHaveCorrectDefaults()
    {
        var info = new PermissionInfo();

        info.Id.Should().Be(0);
        info.Name.Should().BeEmpty();
        info.Description.Should().BeNull();
        info.Category.Should().Be("general");
    }

    [Fact]
    public void RoleInfo_ShouldHaveCorrectDefaults()
    {
        var info = new RoleInfo();

        info.Id.Should().Be(0);
        info.Name.Should().BeEmpty();
        info.IsSystem.Should().BeFalse();
        info.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void UserRecord_ShouldHaveCorrectDefaults()
    {
        var record = new UserRecord();

        record.Id.Should().Be(0);
        record.Username.Should().BeEmpty();
        record.IsActive.Should().BeTrue();
        record.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void ApiKeyInfo_ShouldHaveCorrectDefaults()
    {
        var info = new ApiKeyInfo();

        info.Id.Should().Be(0);
        info.Name.Should().BeEmpty();
        info.Permissions.Should().BeEmpty();
        info.UserId.Should().BeNull();
        info.ExpiresAt.Should().BeNull();
    }

    #endregion

    #region Database Initialization Tests

    [Fact]
    public void Constructor_ShouldCreateDatabaseFile()
    {
        File.Exists(_testDbPath).Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldSeedDefaultData()
    {
        var permissions = _db.GetAllPermissions();
        var roles = _db.GetAllRoles();

        permissions.Should().NotBeEmpty();
        roles.Should().NotBeEmpty();
    }

    [Fact]
    public void SecondInitialization_ShouldNotDuplicateSeedData()
    {
        // Get counts before
        var permCountBefore = _db.GetAllPermissions().Count;
        var roleCountBefore = _db.GetAllRoles().Count;

        // Create new instance (which will re-initialize)
        _db.Dispose();
        _db = new RolesDatabase(_mockLogger.Object, _testDbPath);

        var permCountAfter = _db.GetAllPermissions().Count;
        var roleCountAfter = _db.GetAllRoles().Count;

        permCountAfter.Should().Be(permCountBefore);
        roleCountAfter.Should().Be(roleCountBefore);
    }

    [Fact]
    public void Dispose_ShouldBeSafe()
    {
        var db = new RolesDatabase(_mockLogger.Object, Path.Combine(Path.GetTempPath(), $"dispose_test_{Guid.NewGuid():N}.db"));
        
        // Should not throw
        db.Dispose();
        db.Dispose(); // Double dispose should be safe
    }

    #endregion
}
