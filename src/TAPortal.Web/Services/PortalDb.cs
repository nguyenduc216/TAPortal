using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

public sealed record AuthUserSnapshot(Guid Id, string Username, string DisplayName, string? Email, string? Phone, string PasswordHash, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed class PortalDb
{
    private readonly string _connectionString;

    public PortalDb(IConfiguration configuration)
    {
        _connectionString = configuration["Database:ConnectionString"]
            ?? throw new InvalidOperationException("Missing Database:ConnectionString. Configure it through appsettings or Database__ConnectionString.");
    }

    private SqlConnection Open() => new(_connectionString);

    public async Task<AuthUserSnapshot?> GetAuthUserAsync(string username)
    {
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT TOP 1 Id,Username,DisplayName,Email,Phone,PasswordHash
FROM dbo.Users
WHERE NormalizedUsername=@u AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE';
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@u", username.Trim().ToUpperInvariant());
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        var id = rd.GetGuid(0);
        var user = new AuthUserSnapshot(
            id,
            rd.GetString(1),
            rd.GetString(2),
            rd.IsDBNull(3) ? null : rd.GetString(3),
            rd.IsDBNull(4) ? null : rd.GetString(4),
            rd.IsDBNull(5) ? string.Empty : rd.GetString(5),
            Array.Empty<string>(),
            Array.Empty<string>());
        await rd.CloseAsync();

        var roles = new List<string>();
        const string roleSql = """
SELECT r.Code FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId
WHERE ur.UserId=@id AND r.IsDeleted=0 AND r.IsActive=1;
""";
        await using (var roleCmd = new SqlCommand(roleSql, cn))
        {
            roleCmd.Parameters.AddWithValue("@id", id);
            await using var rr = await roleCmd.ExecuteReaderAsync();
            while (await rr.ReadAsync()) roles.Add(rr.GetString(0));
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string permSql = """
SELECT DISTINCT p.Code
FROM dbo.UserRoles ur
JOIN dbo.RolePermissions rp ON rp.RoleId=ur.RoleId
JOIN dbo.Permissions p ON p.Id=rp.PermissionId
WHERE ur.UserId=@id AND p.IsDeleted=0 AND p.IsActive=1
UNION
SELECT p.Code
FROM dbo.UserPermissions up
JOIN dbo.Permissions p ON p.Id=up.PermissionId
WHERE up.UserId=@id AND up.Effect='ALLOW' AND p.IsDeleted=0 AND p.IsActive=1;
""";
        await using (var permCmd = new SqlCommand(permSql, cn))
        {
            permCmd.Parameters.AddWithValue("@id", id);
            await using var pr = await permCmd.ExecuteReaderAsync();
            while (await pr.ReadAsync()) permissions.Add(pr.GetString(0));
        }

        const string denySql = """
SELECT p.Code FROM dbo.UserPermissions up
JOIN dbo.Permissions p ON p.Id=up.PermissionId
WHERE up.UserId=@id AND up.Effect='DENY' AND p.IsDeleted=0;
""";
        await using (var denyCmd = new SqlCommand(denySql, cn))
        {
            denyCmd.Parameters.AddWithValue("@id", id);
            await using var dr = await denyCmd.ExecuteReaderAsync();
            while (await dr.ReadAsync()) permissions.Remove(dr.GetString(0));
        }

        return user with { Roles = roles, Permissions = permissions.ToList() };
    }

    public async Task TouchLoginAsync(Guid userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET LastLoginAt=SYSUTCDATETIME() WHERE Id=@id", cn);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Users WHERE NormalizedUsername=@u AND IsDeleted=0", cn);
        cmd.Parameters.AddWithValue("@u", username.Trim().ToUpperInvariant());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<Guid> CreateUserAsync(UserCreateVm vm, string passwordHash, Guid? createdBy = null)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var id = Guid.NewGuid();
        const string sql = """
INSERT dbo.Users(Id,Username,NormalizedUsername,Email,NormalizedEmail,Phone,DisplayName,PasswordHash,Status,IsActive,CreatedBy)
VALUES(@id,@username,@nu,@email,@ne,@phone,@name,@hash,'ACTIVE',1,@createdBy);
""";
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@username", vm.Username.Trim());
            cmd.Parameters.AddWithValue("@nu", vm.Username.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@email", (object?)vm.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ne", string.IsNullOrWhiteSpace(vm.Email) ? DBNull.Value : vm.Email.Trim().ToUpperInvariant());
            cmd.Parameters.AddWithValue("@phone", (object?)vm.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@name", vm.DisplayName.Trim());
            cmd.Parameters.AddWithValue("@hash", passwordHash);
            cmd.Parameters.AddWithValue("@createdBy", (object?)createdBy ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        if (vm.RoleId.HasValue)
        {
            await using var roleCmd = new SqlCommand("INSERT dbo.UserRoles(UserId,RoleId,CreatedBy) VALUES(@u,@r,@by)", cn, tx);
            roleCmd.Parameters.AddWithValue("@u", id);
            roleCmd.Parameters.AddWithValue("@r", vm.RoleId.Value);
            roleCmd.Parameters.AddWithValue("@by", (object?)createdBy ?? DBNull.Value);
            await roleCmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return id;
    }

    public async Task UpdatePasswordAsync(Guid userId, string passwordHash)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@h,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@id WHERE Id=@id", cn);
        cmd.Parameters.AddWithValue("@h", passwordHash);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PortalUser>> GetUsersAsync()
    {
        var list = new List<PortalUser>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,Username,DisplayName,Email,Phone,Status,IsActive FROM dbo.Users WHERE IsDeleted=0 ORDER BY DisplayName", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.IsDBNull(3)?null:rd.GetString(3),rd.IsDBNull(4)?null:rd.GetString(4),rd.GetString(5),rd.GetBoolean(6)));
        return list;
    }

    public async Task<List<PortalRole>> GetRolesAsync()
    {
        var list = new List<PortalRole>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,Code,Name,Description,IsSystem,IsActive FROM dbo.Roles WHERE IsDeleted=0 ORDER BY IsSystem DESC,Name", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.IsDBNull(3)?null:rd.GetString(3),rd.GetBoolean(4),rd.GetBoolean(5)));
        return list;
    }

    public async Task<List<PortalMenu>> GetMenusAsync()
    {
        var list = new List<PortalMenu>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,ParentId,Code,Name,Icon,Route,SortOrder,IsVisible,IsActive FROM dbo.Menus WHERE IsDeleted=0 ORDER BY SortOrder,Name", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.IsDBNull(1)?null:rd.GetGuid(1),rd.GetString(2),rd.GetString(3),rd.IsDBNull(4)?null:rd.GetString(4),rd.IsDBNull(5)?null:rd.GetString(5),rd.GetInt32(6),rd.GetBoolean(7),rd.GetBoolean(8)));
        return list;
    }

    public async Task<List<CustomerRow>> GetCustomersAsync()
    {
        var list = new List<CustomerRow>();
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,Code,Name,TaxCode,Email,Phone,Status FROM dbo.Customers WHERE IsDeleted=0 ORDER BY Name", cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.IsDBNull(3)?null:rd.GetString(3),rd.IsDBNull(4)?null:rd.GetString(4),rd.IsDBNull(5)?null:rd.GetString(5),rd.GetString(6)));
        return list;
    }

    public async Task<DashboardVm> GetDashboardAsync()
    {
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
SELECT
 (SELECT COUNT(1) FROM dbo.Customers WHERE IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.Users WHERE IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.Users WHERE IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'),
 (SELECT COUNT(1) FROM dbo.Roles WHERE IsDeleted=0);
""";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        await rd.ReadAsync();
        return new(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.GetInt32(3));
    }

    public async Task EnsureAdminAsync(string passwordHash)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

        var roleId = Guid.NewGuid();
        const string roleSql = """
IF NOT EXISTS(SELECT 1 FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0)
 INSERT dbo.Roles(Id,Code,Name,Description,IsSystem,IsActive) VALUES(@rid,'SYS_ADMIN',N'Quản trị hệ thống',N'Quản trị toàn bộ hệ thống',1,1);
SELECT TOP 1 Id FROM dbo.Roles WHERE Code='SYS_ADMIN' AND IsDeleted=0;
""";
        await using (var roleCmd = new SqlCommand(roleSql, cn, tx))
        {
            roleCmd.Parameters.AddWithValue("@rid", roleId);
            roleId = (Guid)(await roleCmd.ExecuteScalarAsync())!;
        }

        Guid userId;
        await using (var find = new SqlCommand("SELECT TOP 1 Id FROM dbo.Users WHERE NormalizedUsername='ADMIN' AND IsDeleted=0", cn, tx))
        {
            var existing = await find.ExecuteScalarAsync();
            if (existing is Guid id) userId = id;
            else
            {
                userId = Guid.NewGuid();
                const string userSql = """
INSERT dbo.Users(Id,Username,NormalizedUsername,Email,NormalizedEmail,DisplayName,PasswordHash,Status,IsActive)
VALUES(@id,'admin','ADMIN','admin@ta.local','ADMIN@TA.LOCAL',N'T.A Administrator',@hash,'ACTIVE',1);
""";
                await using var add = new SqlCommand(userSql, cn, tx);
                add.Parameters.AddWithValue("@id", userId);
                add.Parameters.AddWithValue("@hash", passwordHash);
                await add.ExecuteNonQueryAsync();
            }
        }

        await using (var ur = new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.UserRoles WHERE UserId=@u AND RoleId=@r) INSERT dbo.UserRoles(UserId,RoleId) VALUES(@u,@r)", cn, tx))
        {
            ur.Parameters.AddWithValue("@u", userId); ur.Parameters.AddWithValue("@r", roleId); await ur.ExecuteNonQueryAsync();
        }
        await using (var rp = new SqlCommand("INSERT dbo.RolePermissions(RoleId,PermissionId) SELECT @r,p.Id FROM dbo.Permissions p WHERE p.IsDeleted=0 AND NOT EXISTS(SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId=@r AND x.PermissionId=p.Id)", cn, tx))
        {
            rp.Parameters.AddWithValue("@r", roleId); await rp.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
