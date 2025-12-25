using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Auth;

/// <summary>
/// Role-Based Access Control (RBAC) system with SQLite persistence.
/// Port of Python HoNfigurator-Central RolesDatabase.
/// </summary>
public class RolesDatabase : IDisposable
{
    private readonly ILogger<RolesDatabase> _logger;
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    private bool _disposed;

    public RolesDatabase(ILogger<RolesDatabase> logger, string? dbPath = null)
    {
        _logger = logger;
        _dbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "config", "roles.db");
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            -- Permissions table
            CREATE TABLE IF NOT EXISTS permissions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT UNIQUE NOT NULL,
                description TEXT,
                category TEXT DEFAULT 'general'
            );

            -- Roles table
            CREATE TABLE IF NOT EXISTS roles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT UNIQUE NOT NULL,
                description TEXT,
                is_system BOOLEAN DEFAULT 0,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            -- Role-Permission mapping
            CREATE TABLE IF NOT EXISTS role_permissions (
                role_id INTEGER NOT NULL,
                permission_id INTEGER NOT NULL,
                PRIMARY KEY (role_id, permission_id),
                FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE,
                FOREIGN KEY (permission_id) REFERENCES permissions(id) ON DELETE CASCADE
            );

            -- Users table
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                discord_id TEXT,
                role_id INTEGER,
                is_active BOOLEAN DEFAULT 1,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                last_login DATETIME,
                FOREIGN KEY (role_id) REFERENCES roles(id)
            );

            -- API Keys table
            CREATE TABLE IF NOT EXISTS api_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                key_hash TEXT UNIQUE NOT NULL,
                name TEXT NOT NULL,
                user_id INTEGER,
                permissions TEXT, -- JSON array of permission names
                expires_at DATETIME,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                last_used DATETIME,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );
        ";
        cmd.ExecuteNonQuery();

        // Seed default permissions and roles
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        // Check if already seeded
        using var checkCmd = _connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM permissions";
        var count = Convert.ToInt32(checkCmd.ExecuteScalar());
        
        if (count > 0) return;

        _logger.LogInformation("Seeding default permissions and roles...");

        // Insert default permissions
        var permissions = new[]
        {
            // Server Management
            (Permission.ViewServers, "View server status and info", "servers"),
            (Permission.StartServer, "Start game servers", "servers"),
            (Permission.StopServer, "Stop game servers", "servers"),
            (Permission.RestartServer, "Restart game servers", "servers"),
            (Permission.ScaleServers, "Add/remove servers", "servers"),
            
            // Configuration
            (Permission.ViewConfig, "View configuration", "config"),
            (Permission.EditConfig, "Edit configuration", "config"),
            
            // Players
            (Permission.ViewPlayers, "View player info", "players"),
            (Permission.KickPlayer, "Kick players from server", "players"),
            (Permission.BanPlayer, "Ban players", "players"),
            (Permission.UnbanPlayer, "Unban players", "players"),
            
            // Replays
            (Permission.ViewReplays, "View and download replays", "replays"),
            (Permission.DeleteReplays, "Delete replays", "replays"),
            (Permission.UploadReplays, "Upload replays to master", "replays"),
            
            // System
            (Permission.ViewLogs, "View system logs", "system"),
            (Permission.ViewMetrics, "View system metrics", "system"),
            (Permission.ManageFilebeat, "Manage Filebeat service", "system"),
            
            // Admin
            (Permission.ManageUsers, "Manage users", "admin"),
            (Permission.ManageRoles, "Manage roles and permissions", "admin"),
            (Permission.ManageApiKeys, "Manage API keys", "admin"),
            (Permission.FullAccess, "Full administrative access", "admin")
        };

        foreach (var (perm, desc, cat) in permissions)
        {
            AddPermission(perm.ToString(), desc, cat);
        }

        // Create default roles
        var adminRoleId = CreateRole("Admin", "Full system administrator", isSystem: true);
        var moderatorRoleId = CreateRole("Moderator", "Server moderator", isSystem: true);
        var viewerRoleId = CreateRole("Viewer", "Read-only access", isSystem: true);

        // Assign permissions to Admin (all)
        if (adminRoleId > 0)
        {
            foreach (var perm in Enum.GetValues<Permission>())
            {
                AssignPermissionToRole(adminRoleId, perm.ToString());
            }
        }

        // Assign permissions to Moderator
        if (moderatorRoleId > 0)
        {
            var modPerms = new[] 
            { 
                Permission.ViewServers, Permission.StartServer, Permission.StopServer,
                Permission.RestartServer, Permission.ViewConfig, Permission.ViewPlayers,
                Permission.KickPlayer, Permission.BanPlayer, Permission.UnbanPlayer,
                Permission.ViewReplays, Permission.ViewLogs, Permission.ViewMetrics
            };
            foreach (var perm in modPerms)
            {
                AssignPermissionToRole(moderatorRoleId, perm.ToString());
            }
        }

        // Assign permissions to Viewer
        if (viewerRoleId > 0)
        {
            var viewerPerms = new[] 
            { 
                Permission.ViewServers, Permission.ViewConfig, Permission.ViewPlayers,
                Permission.ViewReplays, Permission.ViewLogs, Permission.ViewMetrics
            };
            foreach (var perm in viewerPerms)
            {
                AssignPermissionToRole(viewerRoleId, perm.ToString());
            }
        }

        _logger.LogInformation("Default permissions and roles seeded");
    }

    #region Permissions

    public void AddPermission(string name, string? description = null, string? category = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO permissions (name, description, category) 
            VALUES (@name, @desc, @cat)";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@desc", description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", category ?? "general");
        cmd.ExecuteNonQuery();
    }

    public List<PermissionInfo> GetAllPermissions()
    {
        var permissions = new List<PermissionInfo>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, category FROM permissions ORDER BY category, name";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            permissions.Add(new PermissionInfo
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Category = reader.GetString(3)
            });
        }
        return permissions;
    }

    #endregion

    #region Roles

    public int CreateRole(string name, string? description = null, bool isSystem = false)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO roles (name, description, is_system) 
            VALUES (@name, @desc, @system)
            ON CONFLICT(name) DO NOTHING";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@desc", description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@system", isSystem ? 1 : 0);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT id FROM roles WHERE name = @name";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public bool UpdateRole(int roleId, string? name = null, string? description = null)
    {
        using var cmd = _connection.CreateCommand();
        var updates = new List<string>();
        
        if (name != null)
        {
            updates.Add("name = @name");
            cmd.Parameters.AddWithValue("@name", name);
        }
        if (description != null)
        {
            updates.Add("description = @desc");
            cmd.Parameters.AddWithValue("@desc", description);
        }
        
        if (updates.Count == 0) return false;
        
        cmd.CommandText = $"UPDATE roles SET {string.Join(", ", updates)} WHERE id = @id AND is_system = 0";
        cmd.Parameters.AddWithValue("@id", roleId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteRole(int roleId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM roles WHERE id = @id AND is_system = 0";
        cmd.Parameters.AddWithValue("@id", roleId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteRole(string roleName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM roles WHERE name = @name AND is_system = 0";
        cmd.Parameters.AddWithValue("@name", roleName);
        return cmd.ExecuteNonQuery() > 0;
    }

    public RoleInfo? GetRole(int roleId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, is_system, created_at FROM roles WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", roleId);
        
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var role = new RoleInfo
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsSystem = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4)
            };
            role.Permissions = GetRolePermissions(role.Id);
            return role;
        }
        return null;
    }

    public RoleInfo? GetRole(string roleName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM roles WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", roleName);
        var id = cmd.ExecuteScalar();
        return id != null ? GetRole(Convert.ToInt32(id)) : null;
    }

    public List<RoleInfo> GetAllRoles()
    {
        var roles = new List<RoleInfo>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, is_system, created_at FROM roles ORDER BY name";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var role = new RoleInfo
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsSystem = reader.GetBoolean(3),
                CreatedAt = reader.GetDateTime(4)
            };
            roles.Add(role);
        }

        // Load permissions for each role
        foreach (var role in roles)
        {
            role.Permissions = GetRolePermissions(role.Id);
        }

        return roles;
    }

    public List<string> GetRolePermissions(int roleId)
    {
        var permissions = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT p.name FROM permissions p
            JOIN role_permissions rp ON p.id = rp.permission_id
            WHERE rp.role_id = @roleId";
        cmd.Parameters.AddWithValue("@roleId", roleId);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            permissions.Add(reader.GetString(0));
        }
        return permissions;
    }

    public void AssignPermissionToRole(int roleId, string permissionName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO role_permissions (role_id, permission_id)
            SELECT @roleId, id FROM permissions WHERE name = @perm";
        cmd.Parameters.AddWithValue("@roleId", roleId);
        cmd.Parameters.AddWithValue("@perm", permissionName);
        cmd.ExecuteNonQuery();
    }

    public void RemovePermissionFromRole(int roleId, string permissionName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM role_permissions 
            WHERE role_id = @roleId 
            AND permission_id = (SELECT id FROM permissions WHERE name = @perm)";
        cmd.Parameters.AddWithValue("@roleId", roleId);
        cmd.Parameters.AddWithValue("@perm", permissionName);
        cmd.ExecuteNonQuery();
    }

    public void SetRolePermissions(int roleId, IEnumerable<string> permissions)
    {
        // Remove all current permissions
        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM role_permissions WHERE role_id = @roleId";
        deleteCmd.Parameters.AddWithValue("@roleId", roleId);
        deleteCmd.ExecuteNonQuery();

        // Add new permissions
        foreach (var perm in permissions)
        {
            AssignPermissionToRole(roleId, perm);
        }
    }

    #endregion

    #region Users

    public int CreateUser(string username, string passwordHash, string? discordId = null, int? roleId = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (username, password_hash, discord_id, role_id) 
            VALUES (@username, @hash, @discord, @roleId);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        cmd.Parameters.AddWithValue("@discord", discordId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@roleId", roleId ?? (object)DBNull.Value);
        
        try
        {
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : -1;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint
        {
            return -1;
        }
    }

    public UserRecord? GetUser(string username)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT u.id, u.username, u.password_hash, u.discord_id, u.role_id, 
                   u.is_active, u.created_at, u.last_login, r.name as role_name
            FROM users u
            LEFT JOIN roles r ON u.role_id = r.id
            WHERE u.username = @username COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@username", username);
        
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var user = new UserRecord
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                DiscordId = reader.IsDBNull(3) ? null : reader.GetString(3),
                RoleId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsActive = reader.GetBoolean(5),
                CreatedAt = reader.GetDateTime(6),
                LastLogin = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                RoleName = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
            
            // Load permissions
            if (user.RoleId.HasValue)
            {
                user.Permissions = GetRolePermissions(user.RoleId.Value);
            }
            
            return user;
        }
        return null;
    }

    public UserRecord? GetUserByDiscordId(string discordId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT username FROM users WHERE discord_id = @discordId";
        cmd.Parameters.AddWithValue("@discordId", discordId);
        var username = cmd.ExecuteScalar() as string;
        return username != null ? GetUser(username) : null;
    }

    public List<UserRecord> GetAllUsers()
    {
        var users = new List<UserRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT u.id, u.username, u.password_hash, u.discord_id, u.role_id, 
                   u.is_active, u.created_at, u.last_login, r.name as role_name
            FROM users u
            LEFT JOIN roles r ON u.role_id = r.id
            ORDER BY u.username";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            users.Add(new UserRecord
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                DiscordId = reader.IsDBNull(3) ? null : reader.GetString(3),
                RoleId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsActive = reader.GetBoolean(5),
                CreatedAt = reader.GetDateTime(6),
                LastLogin = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                RoleName = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }
        return users;
    }

    public bool UpdateUser(int userId, string? passwordHash = null, string? discordId = null, 
        int? roleId = null, bool? isActive = null)
    {
        var updates = new List<string>();
        using var cmd = _connection.CreateCommand();
        
        if (passwordHash != null)
        {
            updates.Add("password_hash = @hash");
            cmd.Parameters.AddWithValue("@hash", passwordHash);
        }
        if (discordId != null)
        {
            updates.Add("discord_id = @discord");
            cmd.Parameters.AddWithValue("@discord", discordId);
        }
        if (roleId.HasValue)
        {
            updates.Add("role_id = @roleId");
            cmd.Parameters.AddWithValue("@roleId", roleId.Value);
        }
        if (isActive.HasValue)
        {
            updates.Add("is_active = @active");
            cmd.Parameters.AddWithValue("@active", isActive.Value ? 1 : 0);
        }
        
        if (updates.Count == 0) return false;
        
        cmd.CommandText = $"UPDATE users SET {string.Join(", ", updates)} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteUser(int userId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void UpdateLastLogin(int userId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    public bool HasPermission(int userId, Permission permission)
    {
        return HasPermission(userId, permission.ToString());
    }

    public bool HasPermission(int userId, string permissionName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM users u
            JOIN role_permissions rp ON u.role_id = rp.role_id
            JOIN permissions p ON rp.permission_id = p.id
            WHERE u.id = @userId AND (p.name = @perm OR p.name = @fullAccess)";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@perm", permissionName);
        cmd.Parameters.AddWithValue("@fullAccess", Permission.FullAccess.ToString());
        
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    #endregion

    #region API Keys

    public string CreateApiKey(string name, int? userId = null, IEnumerable<string>? permissions = null, 
        DateTime? expiresAt = null)
    {
        var key = GenerateApiKey();
        var keyHash = HashApiKey(key);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO api_keys (key_hash, name, user_id, permissions, expires_at) 
            VALUES (@hash, @name, @userId, @perms, @expires)";
        cmd.Parameters.AddWithValue("@hash", keyHash);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@perms", permissions != null ? JsonSerializer.Serialize(permissions) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", expiresAt ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();

        return key; // Return unhashed key to show to user once
    }

    public ApiKeyInfo? ValidateApiKey(string key)
    {
        var keyHash = HashApiKey(key);
        
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, user_id, permissions, expires_at, created_at 
            FROM api_keys 
            WHERE key_hash = @hash AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)";
        cmd.Parameters.AddWithValue("@hash", keyHash);
        
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            // Update last_used
            using var updateCmd = _connection.CreateCommand();
            updateCmd.CommandText = "UPDATE api_keys SET last_used = CURRENT_TIMESTAMP WHERE id = @id";
            updateCmd.Parameters.AddWithValue("@id", reader.GetInt32(0));
            updateCmd.ExecuteNonQuery();

            return new ApiKeyInfo
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UserId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Permissions = reader.IsDBNull(3) ? new List<string>() : 
                    JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                CreatedAt = reader.GetDateTime(5)
            };
        }
        return null;
    }

    public bool RevokeApiKey(int keyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM api_keys WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", keyId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<ApiKeyInfo> GetApiKeys(int? userId = null)
    {
        var keys = new List<ApiKeyInfo>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = userId.HasValue
            ? "SELECT id, name, user_id, permissions, expires_at, created_at, last_used FROM api_keys WHERE user_id = @userId"
            : "SELECT id, name, user_id, permissions, expires_at, created_at, last_used FROM api_keys";
        
        if (userId.HasValue)
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            keys.Add(new ApiKeyInfo
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UserId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Permissions = reader.IsDBNull(3) ? new List<string>() : 
                    JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new List<string>(),
                ExpiresAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                CreatedAt = reader.GetDateTime(5),
                LastUsed = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            });
        }
        return keys;
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return "hfg_" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashApiKey(string key)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes);
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

#region Enums and Models

/// <summary>
/// System permissions enum
/// </summary>
public enum Permission
{
    // Server Management
    ViewServers,
    StartServer,
    StopServer,
    RestartServer,
    ScaleServers,
    
    // Configuration
    ViewConfig,
    EditConfig,
    
    // Players
    ViewPlayers,
    KickPlayer,
    BanPlayer,
    UnbanPlayer,
    
    // Replays
    ViewReplays,
    DeleteReplays,
    UploadReplays,
    
    // System
    ViewLogs,
    ViewMetrics,
    ManageFilebeat,
    
    // Admin
    ManageUsers,
    ManageRoles,
    ManageApiKeys,
    FullAccess
}

public class PermissionInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "general";
}

public class RoleInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class UserRecord
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class ApiKeyInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public List<string> Permissions { get; set; } = new();
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsed { get; set; }
}

#endregion
