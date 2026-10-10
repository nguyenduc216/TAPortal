using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

public sealed record AuthUserSnapshot(Guid Id, string Username, string DisplayName, string? Email, string? Phone, string PasswordHash, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed class PortalDb
{
    private sealed class MenuAuthorizationRow(PortalMenu menu)
    {
        public PortalMenu Menu { get; } = menu;
        public bool HasMappings { get; set; }
        public HashSet<string> PermissionCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _connectionString;
    private readonly ILogger<PortalDb> _logger;

    public PortalDb(IConfiguration configuration, ILogger<PortalDb> logger)
    {
        _logger = logger;
        _connectionString = configuration["Database:ConnectionString"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Missing Database:ConnectionString. Configure it through appsettings or Database__ConnectionString.");
        }
    }

    private SqlConnection Open() => new(_connectionString);

    public async Task<bool> HasPermissionAsync(Guid userId, string permissionCode)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
        // User DENY overrides user/role ALLOW; inactive principals and permissions fail closed.
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT
    CASE
        WHEN EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='DENY') THEN 'DENY'
        WHEN EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='ALLOW') THEN 'ALLOW'
        ELSE NULL
    END AS UserEffect,
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.UserRoles ur
        JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
        JOIN dbo.RolePermissions rp ON rp.RoleId=r.Id AND rp.PermissionId=p.Id
        WHERE ur.UserId=u.Id
    ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS RoleAllowed
FROM dbo.Users u
JOIN dbo.Permissions p ON p.Code=@permission AND p.IsDeleted=0 AND p.IsActive=1
WHERE u.Id=@userId AND u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE';
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@permission", permissionCode.Trim());
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return false;
        var userEffect = rd.IsDBNull(0) ? null : rd.GetString(0);
        return AuthorizationSemantics.IsPermissionEffective(rd.GetBoolean(1), userEffect);
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
        // Single effective-permission projection for navigation and FunctionAction mappings.
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT p.Code
FROM dbo.Users u
CROSS JOIN dbo.Permissions p
WHERE u.Id=@userId AND u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE'
  AND p.IsDeleted=0 AND p.IsActive=1
  AND NOT EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='DENY')
  AND (
      EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='ALLOW')
      OR EXISTS (
          SELECT 1 FROM dbo.UserRoles ur
          JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.IsDeleted=0 AND r.IsActive=1
          JOIN dbo.RolePermissions rp ON rp.RoleId=r.Id AND rp.PermissionId=p.Id
          WHERE ur.UserId=u.Id
      )
  );
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@userId", userId);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) permissions.Add(rd.GetString(0));
        return permissions;
    }

    public async Task<bool> HasFunctionActionAsync(Guid userId, string actionKey)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
        // Unknown/unmapped actions fail closed; any mapped effective permission authorizes a known action.
        var effectivePermissions = await GetEffectivePermissionsAsync(userId);
        if (effectivePermissions.Count == 0 || string.IsNullOrWhiteSpace(actionKey)) return false;

        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT DISTINCT p.Code
FROM dbo.FunctionActions fa
JOIN dbo.FunctionActionPermissions fap ON fap.FunctionActionId=fa.Id
JOIN dbo.Permissions p ON p.Id=fap.PermissionId AND p.IsDeleted=0 AND p.IsActive=1
WHERE fa.IsDeleted=0 AND fa.IsActive=1 AND (fa.UiKey=@actionKey OR fa.EndpointKey=@actionKey);
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@actionKey", actionKey.Trim());
        var mappedPermissions = new List<string>();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) mappedPermissions.Add(rd.GetString(0));
        return AuthorizationSemantics.HasAnyMappedPermission(effectivePermissions, mappedPermissions, mappedPermissions.Count > 0, false);
    }

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

        var permissions = await GetEffectivePermissionsAsync(id);
        return user with { Roles = roles, Permissions = permissions.ToList() };
    }

    public async Task TouchLoginAsync(Guid userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET LastLoginAt=SYSUTCDATETIME() WHERE Id=@id", cn);
        cmd.Parameters.AddWithValue("@id", userId);
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number == 229)
        {
            _logger.LogWarning(ex, "Unable to update LastLoginAt for user {UserId} because the database principal lacks UPDATE permission.", userId);
        }
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

    public async Task<IReadOnlyList<PortalMenuNode>> GetNavigationMenuTreeAsync(Guid userId)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
        // Unprotected menus are visible; protected menus require any configured effective permission.
        var effectivePermissions = await GetEffectivePermissionsAsync(userId);
        var rows = new Dictionary<Guid, MenuAuthorizationRow>();
        await using var cn = Open(); await cn.OpenAsync();

        const string sql = """
SELECT m.Id,m.ParentId,m.Code,m.Name,m.Icon,m.Route,m.SortOrder,m.IsVisible,m.IsActive,mp.PermissionId,p.Code
FROM dbo.Menus m
LEFT JOIN dbo.MenuPermissions mp ON mp.MenuId=m.Id
LEFT JOIN dbo.Permissions p ON p.Id=mp.PermissionId AND p.IsDeleted=0 AND p.IsActive=1
WHERE m.IsDeleted=0 AND m.IsVisible=1 AND m.IsActive=1
ORDER BY m.SortOrder,m.Name,m.Id;
""";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var id = rd.GetGuid(0);
            if (!rows.TryGetValue(id, out var row))
            {
                row = new MenuAuthorizationRow(new(
                    id,
                    rd.IsDBNull(1) ? null : rd.GetGuid(1),
                    rd.GetString(2),
                    rd.GetString(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? null : rd.GetString(5),
                    rd.GetInt32(6),
                    rd.GetBoolean(7),
                    rd.GetBoolean(8)));
                rows.Add(id, row);
            }
            if (!rd.IsDBNull(9)) row.HasMappings = true;
            if (!rd.IsDBNull(10)) row.PermissionCodes.Add(rd.GetString(10));
        }
        var authorizedMenus = rows.Values
            .Where(x => AuthorizationSemantics.HasAnyMappedPermission(effectivePermissions, x.PermissionCodes, x.HasMappings, true))
            .Select(x => x.Menu);
        return BuildMenuTree(authorizedMenus);
    }

    internal static IReadOnlyList<PortalMenuNode> BuildMenuTree(IEnumerable<PortalMenu> menus)
    {
        var ordered = menus.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToList();
        var byParent = ordered.ToLookup(x => x.ParentId);
        var knownIds = ordered.Select(x => x.Id).ToHashSet();
        var visiting = new HashSet<Guid>();

        PortalMenuNode? Build(PortalMenu menu)
        {
            if (!visiting.Add(menu.Id)) return null;
            var children = byParent[menu.Id].Select(Build).Where(x => x is not null).Cast<PortalMenuNode>().ToList();
            visiting.Remove(menu.Id);
            if (string.IsNullOrWhiteSpace(menu.Route) && children.Count == 0) return null;
            return new PortalMenuNode(menu, children);
        }

        return ordered
            .Where(x => x.ParentId is null || !knownIds.Contains(x.ParentId.Value))
            .Select(Build)
            .Where(x => x is not null)
            .Cast<PortalMenuNode>()
            .ToList();
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

    // FEATURE_ID: TAPORTAL-HR-MASTER-DATA
    // CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-001
    // PortalDb remains the single SQL owner for the Department master lifecycle.
    public async Task<IReadOnlyList<DepartmentRow>> GetDepartmentsAsync()
    {
        var list = new List<DepartmentRow>();
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT Id,Code,Name,Description,SortOrder,IsActive,CreatedAt,UpdatedAt,RowVersion
FROM dbo.Departments
WHERE IsDeleted=0
ORDER BY SortOrder,Name,Code;
""";
        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            list.Add(new(
                rd.GetGuid(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                rd.GetInt32(4), rd.GetBoolean(5), rd.GetDateTime(6), rd.IsDBNull(7) ? null : rd.GetDateTime(7),
                Convert.ToBase64String((byte[])rd[8])));
        }
        return list;
    }

    public async Task<bool> DepartmentCodeExistsAsync(string code, Guid? excludingId = null)
    {
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = "SELECT COUNT(1) FROM dbo.Departments WHERE Code=@code AND IsDeleted=0 AND (@excludingId IS NULL OR Id<>@excludingId);";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@code", System.Data.SqlDbType.VarChar, DepartmentRules.CodeMaxLength).Value = DepartmentRules.NormalizeCode(code);
        cmd.Parameters.Add("@excludingId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)excludingId ?? DBNull.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<DepartmentWriteResult> CreateDepartmentAsync(DepartmentCreateVm vm, Guid? userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
INSERT dbo.Departments(Id,Code,Name,Description,SortOrder,IsActive,IsDeleted,CreatedBy)
VALUES(NEWID(),@code,@name,@description,@sortOrder,@isActive,0,@userId);
""";
        await using var cmd = DepartmentCommand(sql, cn, vm, userId);
        try { await cmd.ExecuteNonQueryAsync(); return DepartmentWriteResult.Success; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return DepartmentWriteResult.DuplicateCode; }
    }

    public async Task<DepartmentWriteResult> UpdateDepartmentAsync(DepartmentUpdateVm vm, byte[] rowVersion, Guid? userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
UPDATE dbo.Departments
SET Code=@code,Name=@name,Description=@description,SortOrder=@sortOrder,IsActive=@isActive,
    UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId
WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0;
""";
        await using var cmd = DepartmentCommand(sql, cn, vm, userId);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = vm.Id;
        cmd.Parameters.Add("@rowVersion", System.Data.SqlDbType.Timestamp, 8).Value = rowVersion;
        try { return await cmd.ExecuteNonQueryAsync() == 1 ? DepartmentWriteResult.Success : DepartmentWriteResult.ConcurrencyConflict; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return DepartmentWriteResult.DuplicateCode; }
    }

    public async Task<DepartmentWriteResult> SetDepartmentActiveAsync(Guid id, bool isActive, byte[] rowVersion, Guid? userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
UPDATE dbo.Departments SET IsActive=@isActive,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId
WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0;
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = id;
        cmd.Parameters.Add("@isActive", System.Data.SqlDbType.Bit).Value = isActive;
        cmd.Parameters.Add("@rowVersion", System.Data.SqlDbType.Timestamp, 8).Value = rowVersion;
        cmd.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)userId ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync() == 1 ? DepartmentWriteResult.Success : DepartmentWriteResult.ConcurrencyConflict;
    }

    public async Task<DepartmentWriteResult> SoftDeleteDepartmentAsync(Guid id, byte[] rowVersion, Guid? userId)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        const string dependencySql = """
DECLARE @HasDependency bit=0;
IF OBJECT_ID(N'dbo.Teams',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Teams',N'DepartmentId') IS NOT NULL
    EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.Teams WHERE DepartmentId=@id) SET @found=1;',N'@id uniqueidentifier,@found bit OUTPUT',@id,@HasDependency OUTPUT;
IF @HasDependency=0 AND OBJECT_ID(N'dbo.EmployeeAssignments',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.EmployeeAssignments',N'DepartmentId') IS NOT NULL
    EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.EmployeeAssignments WHERE DepartmentId=@id) SET @found=1;',N'@id uniqueidentifier,@found bit OUTPUT',@id,@HasDependency OUTPUT;
SELECT @HasDependency;
""";
        await using (var dependency = new SqlCommand(dependencySql, cn, tx))
        {
            dependency.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = id;
            if (Convert.ToBoolean(await dependency.ExecuteScalarAsync()))
            {
                await tx.RollbackAsync();
                return DepartmentWriteResult.HasDependencies;
            }
        }
        const string deleteSql = """
UPDATE dbo.Departments
SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME(),DeletedBy=@userId,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId
WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0;
""";
        await using var cmd = new SqlCommand(deleteSql, cn, tx);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.UniqueIdentifier).Value = id;
        cmd.Parameters.Add("@rowVersion", System.Data.SqlDbType.Timestamp, 8).Value = rowVersion;
        cmd.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)userId ?? DBNull.Value;
        if (await cmd.ExecuteNonQueryAsync() != 1)
        {
            await tx.RollbackAsync();
            return DepartmentWriteResult.ConcurrencyConflict;
        }
        await tx.CommitAsync();
        return DepartmentWriteResult.Success;
    }

    private static SqlCommand DepartmentCommand(string sql, SqlConnection cn, DepartmentCreateVm vm, Guid? userId)
    {
        var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@code", System.Data.SqlDbType.VarChar, DepartmentRules.CodeMaxLength).Value = DepartmentRules.NormalizeCode(vm.Code);
        cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, DepartmentRules.NameMaxLength).Value = vm.Name.Trim();
        cmd.Parameters.Add("@description", System.Data.SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(vm.Description) ? DBNull.Value : vm.Description.Trim();
        cmd.Parameters.Add("@sortOrder", System.Data.SqlDbType.Int).Value = vm.SortOrder;
        cmd.Parameters.Add("@isActive", System.Data.SqlDbType.Bit).Value = vm.IsActive;
        cmd.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value = (object?)userId ?? DBNull.Value;
        return cmd;
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
