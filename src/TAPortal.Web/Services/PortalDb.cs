using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

public sealed record AuthUserSnapshot(Guid Id, string Username, string DisplayName, string? Email, string? Phone, string PasswordHash, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed partial class PortalDb : IBlazorPortalService
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
        // CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001 — HR retirement also revokes explicit/SYS_ADMIN CRM grants.
        if (permissionCode.StartsWith("CRM.", StringComparison.OrdinalIgnoreCase)
            && !(await GetEmployeeAccessContextAsync(userId)).CanAccessCustomers) return false;
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
        // User DENY overrides user/role ALLOW; inactive principals and permissions fail closed.
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
SELECT
    CASE WHEN EXISTS (
        SELECT 1 FROM dbo.UserRoles ur
        JOIN dbo.Roles sr ON sr.Id=ur.RoleId AND sr.Code='SYS_ADMIN' AND sr.IsSystem=1 AND sr.IsDeleted=0 AND sr.IsActive=1
        WHERE ur.UserId=u.Id
    ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsSuperAdmin,
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
        // FEATURE_ID: TAPORTAL-SECURITY-ADMIN-AUTHORIZATION
        // CHANGE_ID: TAPORTAL-20261012-ADMIN-RBAC-001 — trusted active SYS_ADMIN membership is the only privileged bypass; authentication and account-state checks remain mandatory.
        var userEffect = rd.IsDBNull(1) ? null : rd.GetString(1);
        // CHANGE_ID: TAPORTAL-20261013-CRM-PHASE1-002 — explicit user DENY wins over SYS_ADMIN.
        if (string.Equals(userEffect, "DENY", StringComparison.OrdinalIgnoreCase)) return false;
        if (rd.GetBoolean(0)) return true;
        return AuthorizationSemantics.IsPermissionEffective(rd.GetBoolean(2), userEffect);
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
  AND (
      NOT EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='DENY')
  )
  AND (
      EXISTS (SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles sr ON sr.Id=ur.RoleId AND sr.Code='SYS_ADMIN' AND sr.IsSystem=1 AND sr.IsDeleted=0 AND sr.IsActive=1 WHERE ur.UserId=u.Id)
      OR EXISTS (SELECT 1 FROM dbo.UserPermissions x WHERE x.UserId=u.Id AND x.PermissionId=p.Id AND x.Effect='ALLOW')
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
        await rd.CloseAsync();
        // CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001 — navigation/actions share the same employment gate.
        if (permissions.Any(x => x.StartsWith("CRM.", StringComparison.OrdinalIgnoreCase))
            && !(await GetEmployeeAccessContextAsync(userId)).CanAccessCustomers)
            permissions.RemoveWhere(x => x.StartsWith("CRM.", StringComparison.OrdinalIgnoreCase));
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

    // FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-004
    // Resolve the profile from the authenticated server-side claim; never trust a client-supplied user id.
    public async Task<AuthUserSnapshot?> GetAuthUserByIdAsync(Guid userId)
    {
        await using var cn = Open();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT TOP 1 Username FROM dbo.Users WHERE Id=@id AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'", cn);
        cmd.Parameters.AddWithValue("@id", userId);
        var username = await cmd.ExecuteScalarAsync();
        return username is string value ? await GetAuthUserAsync(value) : null;
    }

    // FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-004
    // Update only explicitly allowed profile fields and scope the write to the authenticated active account.
    public async Task<bool> UpdateProfileAsync(Guid userId, ProfileEditVm model)
    {
        await using var cn = Open();
        await cn.OpenAsync();
        const string sql = """
UPDATE dbo.Users
SET DisplayName=@displayName, Email=@email, NormalizedEmail=@normalizedEmail, Phone=@phone,
    UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@id
WHERE Id=@id AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE';
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@displayName", model.DisplayName.Trim());
        cmd.Parameters.AddWithValue("@email", (object?)NormalizeOptional(model.Email) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@normalizedEmail", (object?)NormalizeOptional(model.Email)?.ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@phone", (object?)NormalizeOptional(model.Phone) ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    // FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-007
    // The mutation itself revalidates active-account state so a disabled circuit cannot win a check/write race.
    public async Task<bool> UpdatePasswordAsync(Guid userId, string passwordHash)
    {
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@h,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@id WHERE Id=@id AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'", cn);
        cmd.Parameters.AddWithValue("@h", passwordHash);
        cmd.Parameters.AddWithValue("@id", userId);
        return await cmd.ExecuteNonQueryAsync() == 1;
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

    // CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-008 — list and export use one parameterized, database-side scope predicate.
    public Task<List<CustomerRow>> GetCustomersAsync(Guid userId) => GetCustomersAsync(userId, null);

    public async Task<List<CustomerRow>> GetCustomersAsync(Guid userId, string? search)
    {
        var list = new List<CustomerRow>();
        // CHANGE_ID: TAPORTAL-20261013-CRM-PHASE1-002 — data scope is never a substitute for the CRM view action.
        // This also preserves the existing HR gate, including retirement/suspension revocation.
        if (!await HasFunctionActionAsync(userId, "Customers.Index")) return list;
        var employeeContext = await GetEmployeeAccessContextAsync(userId);
        if (!employeeContext.CanAccessCustomers) return list;
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
SELECT c.Id,c.Code,c.Name,c.TaxCode,c.Email,c.Phone,c.Status,c.Address,c.LogoUrl
FROM dbo.Customers c
WHERE c.IsDeleted=0 AND EXISTS(
 SELECT 1 FROM dbo.Users u WHERE u.Id=@userId AND u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE'
) AND (
 (EXISTS(SELECT 1 FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 WHERE ur.UserId=@userId)
  OR (
    EXISTS (
      SELECT 1
      FROM dbo.UserDataScopes uds
      JOIN dbo.DataScopes ds ON ds.Id=uds.DataScopeId AND ds.IsActive=1
      WHERE uds.UserId=@userId AND uds.Effect='ALLOW'
        AND uds.FunctionCode IN ('Customers','Customers.Index')
        AND uds.ScopeReferenceId IS NOT NULL
        AND (
          (ds.ScopeType='ASSIGNED' AND EXISTS (
            SELECT 1 FROM dbo.CustomerEmployeeAssignments ca
            JOIN dbo.Employees ae ON ae.Id=ca.EmployeeId AND ae.UserId=@userId AND ae.IsActive=1 AND ae.IsDeleted=0
            JOIN dbo.EmployeeStatuses aes ON aes.Id=ae.CurrentStatusId AND aes.Code IN ('ACTIVE','PROBATION','MATERNITY')
            WHERE ca.CustomerId=c.Id AND ca.EmployeeId=uds.ScopeReferenceId AND ca.IsActive=1
              AND ca.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (ca.ValidTo IS NULL OR ca.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))
              AND (ae.StartDate IS NULL OR ae.StartDate<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) AND (ae.EndDate IS NULL OR ae.EndDate>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))
          ))
          OR (ds.ScopeType='TEAM' AND EXISTS (
            SELECT 1 FROM dbo.CustomerServiceTeams cst JOIN dbo.EmployeeAssignments ea ON ea.TeamId=cst.TeamId
            JOIN dbo.Employees ae ON ae.Id=ea.EmployeeId AND ae.UserId=@userId AND ae.IsActive=1 AND ae.IsDeleted=0
            WHERE cst.CustomerId=c.Id AND cst.TeamId=uds.ScopeReferenceId AND cst.IsActive=1
              AND ea.Id=@organizationAssignmentId
              AND cst.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (cst.ValidTo IS NULL OR cst.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))
          ))
          OR (ds.ScopeType='BRANCH' AND (c.OwnerBranchId=uds.ScopeReferenceId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares cbs WHERE cbs.CustomerId=c.Id AND cbs.BranchId=uds.ScopeReferenceId AND cbs.IsActive=1 AND cbs.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (cbs.ValidTo IS NULL OR cbs.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))))
          OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.Id=c.OwnerBranchId AND b.CompanyId=uds.ScopeReferenceId AND b.IsActive=1 AND b.IsDeleted=0))
        )
    )
    OR EXISTS (
      SELECT 1 FROM dbo.RoleDataScopes rds JOIN dbo.DataScopes ds ON ds.Id=rds.DataScopeId AND ds.IsActive=1
      JOIN dbo.UserRoles ur ON ur.RoleId=rds.RoleId AND ur.UserId=@userId
      JOIN dbo.Roles scopeRole ON scopeRole.Id=ur.RoleId AND scopeRole.IsActive=1 AND scopeRole.IsDeleted=0
      WHERE rds.FunctionCode IN ('Customers','Customers.Index') AND rds.ScopeReferenceId IS NOT NULL
        AND ((ds.ScopeType='ASSIGNED' AND EXISTS (SELECT 1 FROM dbo.CustomerEmployeeAssignments ca JOIN dbo.Employees ae ON ae.Id=ca.EmployeeId AND ae.UserId=@userId AND ae.IsActive=1 AND ae.IsDeleted=0 WHERE ca.CustomerId=c.Id AND ca.EmployeeId=rds.ScopeReferenceId AND ca.IsActive=1 AND ca.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (ca.ValidTo IS NULL OR ca.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))))
          OR (ds.ScopeType='TEAM' AND EXISTS (SELECT 1 FROM dbo.CustomerServiceTeams cst JOIN dbo.EmployeeAssignments ea ON ea.TeamId=cst.TeamId JOIN dbo.Employees ae ON ae.Id=ea.EmployeeId AND ae.UserId=@userId AND ae.IsActive=1 AND ae.IsDeleted=0 WHERE cst.CustomerId=c.Id AND cst.TeamId=rds.ScopeReferenceId AND cst.IsActive=1 AND ea.Id=@organizationAssignmentId AND cst.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (cst.ValidTo IS NULL OR cst.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))))
          OR (ds.ScopeType='BRANCH' AND (c.OwnerBranchId=rds.ScopeReferenceId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares cbs WHERE cbs.CustomerId=c.Id AND cbs.BranchId=rds.ScopeReferenceId AND cbs.IsActive=1 AND cbs.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (cbs.ValidTo IS NULL OR cbs.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))))
          OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.Id=c.OwnerBranchId AND b.CompanyId=rds.ScopeReferenceId AND b.IsActive=1 AND b.IsDeleted=0)))
    )
  )
  ) AND NOT EXISTS (
    SELECT 1 FROM dbo.UserDataScopes [deny] JOIN dbo.DataScopes ds ON ds.Id=[deny].DataScopeId AND ds.IsActive=1
    WHERE [deny].UserId=@userId AND [deny].Effect='DENY' AND [deny].FunctionCode IN ('Customers','Customers.Index') AND [deny].ScopeReferenceId IS NOT NULL
      AND ((ds.ScopeType='BRANCH' AND (c.OwnerBranchId=[deny].ScopeReferenceId OR EXISTS (SELECT 1 FROM dbo.CustomerBranchShares x WHERE x.CustomerId=c.Id AND x.BranchId=[deny].ScopeReferenceId AND x.IsActive=1 AND x.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (x.ValidTo IS NULL OR x.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))))
        OR (ds.ScopeType='COMPANY' AND EXISTS (SELECT 1 FROM dbo.Branches b WHERE b.Id=c.OwnerBranchId AND b.CompanyId=[deny].ScopeReferenceId AND b.IsActive=1 AND b.IsDeleted=0))
        OR (ds.ScopeType='TEAM' AND EXISTS (SELECT 1 FROM dbo.CustomerServiceTeams x WHERE x.CustomerId=c.Id AND x.TeamId=[deny].ScopeReferenceId AND x.IsActive=1 AND x.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (x.ValidTo IS NULL OR x.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())))))
        OR (ds.ScopeType='ASSIGNED' AND EXISTS (SELECT 1 FROM dbo.CustomerEmployeeAssignments x JOIN dbo.Employees ae ON ae.Id=x.EmployeeId AND ae.UserId=@userId AND ae.IsActive=1 AND ae.IsDeleted=0 WHERE x.CustomerId=c.Id AND x.EmployeeId=[deny].ScopeReferenceId AND x.IsActive=1 AND x.ValidFrom<=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) AND (x.ValidTo IS NULL OR x.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))))))
  )
 )
 AND (@search IS NULL OR @search='' OR c.Code LIKE @search OR c.Name LIKE @search OR ISNULL(c.TaxCode,'') LIKE @search OR ISNULL(c.Email,'') LIKE @search OR ISNULL(c.Phone,'') LIKE @search)
 ORDER BY c.Name;
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value=userId;
        // Only the unique, current, primary, organization-valid HR assignment can supply TEAM scope.
        cmd.Parameters.Add("@organizationAssignmentId", System.Data.SqlDbType.UniqueIdentifier).Value =
            (object?)employeeContext.CurrentOrganization?.Id ?? DBNull.Value;
        cmd.Parameters.Add("@search", System.Data.SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{search.Trim()}%";
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.IsDBNull(3)?null:rd.GetString(3),rd.IsDBNull(4)?null:rd.GetString(4),rd.IsDBNull(5)?null:rd.GetString(5),rd.GetString(6)) { Address = rd.IsDBNull(7) ? null : rd.GetString(7), LogoUrl = rd.IsDBNull(8) ? null : rd.GetString(8) });
        await rd.CloseAsync();
        if (list.Count > 0)
        {
            await using var contacts = new SqlCommand("SELECT cc.CustomerId,cc.Name,cc.Role,cc.Email,cc.Phone FROM dbo.CustomerContacts cc JOIN dbo.Customers c ON c.Id=cc.CustomerId WHERE cc.IsDeleted=0 AND c.IsDeleted=0 ORDER BY cc.SortOrder,cc.Name", cn);
            await using var cr = await contacts.ExecuteReaderAsync();
            var lookup = list.ToDictionary(x => x.Id);
            while (await cr.ReadAsync()) if (lookup.TryGetValue(cr.GetGuid(0), out var customer)) customer.Contacts.Add(new CustomerContactVm { Name = cr.GetString(1), Role = cr.IsDBNull(2) ? null : cr.GetString(2), Email = cr.IsDBNull(3) ? null : cr.GetString(3), Phone = cr.IsDBNull(4) ? null : cr.GetString(4) });
        }
        return list;
    }

    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — audited schema, typed parameters, current action authorization and unique-index race protection.
    public async Task<CustomerWriteResult> CreateCustomerAsync(CustomerCreateVm vm, Guid userId)
    {
        if (!await HasFunctionActionAsync(userId, "Customers.Index") || !await HasFunctionActionAsync(userId, "Customers.Create")) return CustomerWriteResult.Forbidden;
        CustomerRules.Normalize(vm);
        if (!CustomerRules.IsValid(vm)) return CustomerWriteResult.Invalid;
        await using var cn = Open(); await cn.OpenAsync();
        const string sql = """
INSERT dbo.Customers(Code,Name,TaxCode,Email,Phone,Address,LogoUrl,Status,IsActive,CreatedBy)
OUTPUT inserted.Id
SELECT @code,@name,@taxCode,@email,@phone,@address,@logoUrl,@status,@isActive,@userId
WHERE EXISTS(SELECT 1 FROM dbo.Users WHERE Id=@userId AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE');
""";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@code", System.Data.SqlDbType.VarChar, 50).Value = vm.Code;
        cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 250).Value = vm.Name;
        cmd.Parameters.Add("@taxCode", System.Data.SqlDbType.VarChar, 50).Value = (object?)vm.TaxCode ?? DBNull.Value;
        cmd.Parameters.Add("@email", System.Data.SqlDbType.NVarChar, 256).Value = (object?)vm.Email ?? DBNull.Value;
        cmd.Parameters.Add("@phone", System.Data.SqlDbType.NVarChar, 50).Value = (object?)vm.Phone ?? DBNull.Value;
        cmd.Parameters.Add("@address", System.Data.SqlDbType.NVarChar, 1000).Value = (object?)vm.Address ?? DBNull.Value;
        cmd.Parameters.Add("@logoUrl", System.Data.SqlDbType.NVarChar, 500).Value = (object?)vm.LogoUrl ?? DBNull.Value;
        cmd.Parameters.Add("@status", System.Data.SqlDbType.VarChar, 30).Value = vm.Status;
        cmd.Parameters.Add("@isActive", System.Data.SqlDbType.Bit).Value = vm.Status == "ACTIVE";
        cmd.Parameters.Add("@userId", System.Data.SqlDbType.UniqueIdentifier).Value = userId;
        try { var created = await cmd.ExecuteScalarAsync(); if (created is not Guid customerId) return CustomerWriteResult.Forbidden; await SaveContactsAsync(cn, customerId, vm.Contacts); return CustomerWriteResult.Success; }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return CustomerWriteResult.DuplicateCode; }
        catch (SqlException) { return CustomerWriteResult.Failed; }
    }

    public async Task<CustomerWriteResult> UpdateCustomerAsync(Guid customerId, CustomerCreateVm vm, Guid userId)
    {
        if (!await HasFunctionActionAsync(userId, "Customers.Index") || !await HasFunctionActionAsync(userId, "Customers.Update")) return CustomerWriteResult.Forbidden;
        CustomerRules.Normalize(vm);
        if (!CustomerRules.IsValid(vm) || ! (await GetCustomersAsync(userId)).Any(x => x.Id == customerId)) return CustomerWriteResult.Invalid;
        await using var cn = Open(); await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using var cmd = new SqlCommand("UPDATE dbo.Customers SET Code=@code,Name=@name,TaxCode=@tax,Email=@email,Phone=@phone,Address=@address,LogoUrl=@logo,Status=@status,IsActive=@active,UpdatedBy=@user,UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND IsDeleted=0", cn, tx);
            cmd.Parameters.AddWithValue("@code", vm.Code); cmd.Parameters.AddWithValue("@name", vm.Name);
            cmd.Parameters.AddWithValue("@tax", (object?)vm.TaxCode ?? DBNull.Value); cmd.Parameters.AddWithValue("@email", (object?)vm.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@phone", (object?)vm.Phone ?? DBNull.Value); cmd.Parameters.AddWithValue("@address", (object?)vm.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@logo", (object?)vm.LogoUrl ?? DBNull.Value); cmd.Parameters.AddWithValue("@status", vm.Status);
            cmd.Parameters.AddWithValue("@active", vm.Status == "ACTIVE"); cmd.Parameters.AddWithValue("@user", userId); cmd.Parameters.AddWithValue("@id", customerId);
            if (await cmd.ExecuteNonQueryAsync() != 1) return CustomerWriteResult.Invalid;
            await SaveContactsAsync(cn, customerId, vm.Contacts, tx);
            await tx.CommitAsync(); return CustomerWriteResult.Success;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return CustomerWriteResult.DuplicateCode; }
        catch (SqlException) { return CustomerWriteResult.Failed; }
    }

    public async Task<CustomerWriteResult> DeleteCustomerAsync(Guid customerId, Guid userId)
    {
        if (!await HasFunctionActionAsync(userId, "Customers.Index") || !await HasFunctionActionAsync(userId, "Customers.Delete")) return CustomerWriteResult.Forbidden;
        if (!(await GetCustomersAsync(userId)).Any(x => x.Id == customerId)) return CustomerWriteResult.Invalid;
        await using var cn = Open(); await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Customers SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@user,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@user WHERE Id=@id AND IsDeleted=0", cn);
        cmd.Parameters.AddWithValue("@user", userId); cmd.Parameters.AddWithValue("@id", customerId);
        try { return await cmd.ExecuteNonQueryAsync() == 1 ? CustomerWriteResult.Success : CustomerWriteResult.Invalid; }
        catch (SqlException) { return CustomerWriteResult.Failed; }
    }

    private static async Task SaveContactsAsync(SqlConnection cn, Guid id, List<CustomerContactVm> contacts, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand("DELETE FROM dbo.CustomerContacts WHERE CustomerId=@id;", cn, tx);
        cmd.Parameters.AddWithValue("@id", id); await cmd.ExecuteNonQueryAsync();
        for (var i = 0; i < contacts.Count; i++)
        {
            await using var insert = new SqlCommand("INSERT dbo.CustomerContacts(CustomerId,Name,Role,Email,Phone,SortOrder) VALUES(@id,@name,@role,@email,@phone,@sort)", cn, tx);
            insert.Parameters.AddWithValue("@id", id); insert.Parameters.AddWithValue("@name", contacts[i].Name);
            insert.Parameters.AddWithValue("@role", (object?)contacts[i].Role ?? DBNull.Value); insert.Parameters.AddWithValue("@email", (object?)contacts[i].Email ?? DBNull.Value);
            insert.Parameters.AddWithValue("@phone", (object?)contacts[i].Phone ?? DBNull.Value); insert.Parameters.AddWithValue("@sort", i);
            await insert.ExecuteNonQueryAsync();
        }
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
        var schema = await ReadHrSchemaAsync(cn);
        if (schema.DepartmentHasBranch && (!vm.BranchId.HasValue || vm.BranchId == Guid.Empty))
            return DepartmentWriteResult.InvalidBranch;
        var sql = schema.DepartmentHasBranch ? """
INSERT dbo.Departments(Id,BranchId,Code,Name,Description,SortOrder,IsActive,IsDeleted,CreatedBy)
SELECT NEWID(),b.Id,@code,@name,@description,@sortOrder,@isActive,0,@userId
FROM dbo.Branches b JOIN dbo.Companies c ON c.Id=b.CompanyId AND c.IsActive=1 AND c.IsDeleted=0
WHERE b.Id=@branchId AND b.IsActive=1 AND b.IsDeleted=0;
""" : """
INSERT dbo.Departments(Id,Code,Name,Description,SortOrder,IsActive,IsDeleted,CreatedBy)
VALUES(NEWID(),@code,@name,@description,@sortOrder,@isActive,0,@userId);
""";
        await using var cmd = DepartmentCommand(sql, cn, vm, userId);
        if (schema.DepartmentHasBranch) cmd.Parameters.Add("@branchId", System.Data.SqlDbType.UniqueIdentifier).Value = vm.BranchId!.Value;
        try { return await cmd.ExecuteNonQueryAsync() == 1 ? DepartmentWriteResult.Success : DepartmentWriteResult.InvalidBranch; }
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
    EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.Teams WHERE DepartmentId=@id AND IsDeleted=0) SET @found=1;',N'@id uniqueidentifier,@found bit OUTPUT',@id,@HasDependency OUTPUT;
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

    // FEATURE_ID: TAPORTAL-HR-MASTER-DATA
    // CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-002
    // Team writes validate active Department and same-Department acyclic ancestry in the write transaction.
    public async Task<IReadOnlyList<TeamRow>> GetTeamsAsync()
    {
        var list=new List<TeamRow>(); await using var cn=Open(); await cn.OpenAsync();
        var schema=await ReadHrSchemaAsync(cn);
        var sql="""
SELECT t.Id,t.DepartmentId,d.Code,d.Name,t.ParentTeamId,p.Code,p.Name,t.Code,t.Name,t.Description,t.SortOrder,t.IsActive,t.CreatedAt,t.UpdatedAt,t.RowVersion
FROM dbo.Teams t LEFT JOIN dbo.Departments d ON d.Id=t.DepartmentId LEFT JOIN dbo.Teams p ON p.Id=t.ParentTeamId
WHERE t.IsDeleted=0 ORDER BY d.SortOrder,d.Name,t.SortOrder,t.Name;
""";
        if(!schema.TeamHasDescription) sql=sql.Replace("t.Description", "CAST(NULL AS nvarchar(1000))");
        await using var cmd=new SqlCommand(sql,cn); await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync()) list.Add(new(rd.GetGuid(0),rd.IsDBNull(1)?Guid.Empty:rd.GetGuid(1),rd.IsDBNull(2)?"—":rd.GetString(2),rd.IsDBNull(3)?"Chưa có phòng ban":rd.GetString(3),rd.IsDBNull(4)?null:rd.GetGuid(4),rd.IsDBNull(5)?null:rd.GetString(5),rd.IsDBNull(6)?null:rd.GetString(6),rd.GetString(7),rd.GetString(8),rd.IsDBNull(9)?null:rd.GetString(9),rd.GetInt32(10),rd.GetBoolean(11),rd.GetDateTime(12),rd.IsDBNull(13)?null:rd.GetDateTime(13),Convert.ToBase64String((byte[])rd[14])));
        return list;
    }

    public async Task<(IReadOnlyList<TeamDepartmentOption> Departments,IReadOnlyList<TeamParentOption> Teams)> GetTeamOptionsAsync()
    {
        var departments=new List<TeamDepartmentOption>(); var teams=new List<TeamParentOption>(); await using var cn=Open(); await cn.OpenAsync();
        await using(var cmd=new SqlCommand("SELECT Id,Code,Name FROM dbo.Departments WHERE IsDeleted=0 AND IsActive=1 ORDER BY SortOrder,Name",cn)) { await using var rd=await cmd.ExecuteReaderAsync(); while(await rd.ReadAsync()) departments.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2))); }
        await using(var cmd=new SqlCommand("SELECT Id,DepartmentId,Code,Name,ParentTeamId FROM dbo.Teams WHERE IsDeleted=0 AND DepartmentId IS NOT NULL ORDER BY SortOrder,Name",cn)) { await using var rd=await cmd.ExecuteReaderAsync(); while(await rd.ReadAsync()) teams.Add(new(rd.GetGuid(0),rd.GetGuid(1),rd.GetString(2),rd.GetString(3),rd.IsDBNull(4)?null:rd.GetGuid(4))); }
        return(departments,teams);
    }

    public async Task<bool> TeamCodeExistsAsync(string code,Guid? excludingId=null)
    { await using var cn=Open(); await cn.OpenAsync(); await using var cmd=new SqlCommand("SELECT COUNT(1) FROM dbo.Teams WHERE Code=@code AND IsDeleted=0 AND (@id IS NULL OR Id<>@id)",cn); cmd.Parameters.Add("@code",System.Data.SqlDbType.VarChar,50).Value=TeamRules.NormalizeCode(code); cmd.Parameters.Add("@id",System.Data.SqlDbType.UniqueIdentifier).Value=(object?)excludingId??DBNull.Value; return Convert.ToInt32(await cmd.ExecuteScalarAsync())>0; }

    public async Task<TeamWriteResult> CreateTeamAsync(TeamCreateVm vm,Guid? userId)
    {
        await using var cn=Open(); await cn.OpenAsync(); await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var schema=await ReadHrSchemaAsync(cn,tx);
        if(!schema.TeamHasDescription&&!string.IsNullOrWhiteSpace(vm.Description)) return TeamWriteResult.UnsupportedDescription;
        var valid=await ValidateTeamHierarchyAsync(cn,tx,null,vm.DepartmentId,vm.ParentTeamId); if(valid!=TeamWriteResult.Success){await tx.RollbackAsync();return valid;}
        if(await TeamCodeExistsInTransactionAsync(cn,tx,vm.Code,null)) return TeamWriteResult.DuplicateCode;
        var sql="INSERT dbo.Teams(Id,DepartmentId,ParentTeamId,Code,Name,Description,SortOrder,IsActive,IsDeleted,CreatedBy) VALUES(NEWID(),@departmentId,@parentId,@code,@name,@description,@sortOrder,@isActive,0,@userId)";
        sql=AdaptTeamWriteSql(sql,schema,false);
        await using var cmd=TeamCommand(sql,cn,tx,vm,userId);
        try{await cmd.ExecuteNonQueryAsync();await tx.CommitAsync();return TeamWriteResult.Success;}catch(SqlException ex) when(ex.Number is 2601 or 2627){await tx.RollbackAsync();return TeamWriteResult.DuplicateCode;}
    }

    public async Task<TeamWriteResult> UpdateTeamAsync(TeamUpdateVm vm,byte[] rowVersion,Guid? userId)
    {
        await using var cn=Open(); await cn.OpenAsync(); await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var schema=await ReadHrSchemaAsync(cn,tx);
        if(!schema.TeamHasDescription&&!string.IsNullOrWhiteSpace(vm.Description)) return TeamWriteResult.UnsupportedDescription;
        var valid=await ValidateTeamHierarchyAsync(cn,tx,vm.Id,vm.DepartmentId,vm.ParentTeamId); if(valid!=TeamWriteResult.Success){await tx.RollbackAsync();return valid;}
        if(await TeamCodeExistsInTransactionAsync(cn,tx,vm.Code,vm.Id)) return TeamWriteResult.DuplicateCode;
        var sql=AdaptTeamWriteSql("UPDATE dbo.Teams SET DepartmentId=@departmentId,ParentTeamId=@parentId,Code=@code,Name=@name,Description=@description,SortOrder=@sortOrder,IsActive=@isActive,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0",schema,true);
        await using var cmd=TeamCommand(sql,cn,tx,vm,userId); cmd.Parameters.Add("@id",System.Data.SqlDbType.UniqueIdentifier).Value=vm.Id; cmd.Parameters.Add("@rowVersion",System.Data.SqlDbType.Timestamp,8).Value=rowVersion;
        try{if(await cmd.ExecuteNonQueryAsync()!=1){await tx.RollbackAsync();return TeamWriteResult.ConcurrencyConflict;}await tx.CommitAsync();return TeamWriteResult.Success;}catch(SqlException ex) when(ex.Number is 2601 or 2627){await tx.RollbackAsync();return TeamWriteResult.DuplicateCode;}
    }

    public async Task<TeamWriteResult> SetTeamActiveAsync(Guid id,bool active,byte[] rowVersion,Guid? userId)
    { await using var cn=Open();await cn.OpenAsync();await using var cmd=new SqlCommand("UPDATE dbo.Teams SET IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0",cn);cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@active",active);cmd.Parameters.AddWithValue("@rowVersion",rowVersion);cmd.Parameters.AddWithValue("@userId",(object?)userId??DBNull.Value);return await cmd.ExecuteNonQueryAsync()==1?TeamWriteResult.Success:TeamWriteResult.ConcurrencyConflict; }

    public async Task<TeamWriteResult> SoftDeleteTeamAsync(Guid id,byte[] rowVersion,Guid? userId)
    {
        await using var cn=Open();await cn.OpenAsync();await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        const string deps="""DECLARE @used bit=CASE WHEN EXISTS(SELECT 1 FROM dbo.Teams WHERE ParentTeamId=@id AND IsDeleted=0) THEN 1 ELSE 0 END; IF @used=0 AND OBJECT_ID(N'dbo.EmployeeAssignments',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.EmployeeAssignments',N'TeamId') IS NOT NULL EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.EmployeeAssignments WHERE TeamId=@id) SET @found=1;',N'@id uniqueidentifier,@found bit OUTPUT',@id,@used OUTPUT; SELECT @used;""";
        await using(var dep=new SqlCommand(deps,cn,tx)){dep.Parameters.AddWithValue("@id",id);if(Convert.ToBoolean(await dep.ExecuteScalarAsync())){await tx.RollbackAsync();return TeamWriteResult.HasDependencies;}}
        await using var cmd=new SqlCommand("UPDATE dbo.Teams SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME(),DeletedBy=@userId,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@userId WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0",cn,tx);cmd.Parameters.AddWithValue("@id",id);cmd.Parameters.AddWithValue("@rowVersion",rowVersion);cmd.Parameters.AddWithValue("@userId",(object?)userId??DBNull.Value);if(await cmd.ExecuteNonQueryAsync()!=1){await tx.RollbackAsync();return TeamWriteResult.ConcurrencyConflict;}await tx.CommitAsync();return TeamWriteResult.Success;
    }

    private static async Task<TeamWriteResult> ValidateTeamHierarchyAsync(SqlConnection cn,SqlTransaction tx,Guid? teamId,Guid departmentId,Guid? parentId)
    {
        if(teamId.HasValue&&parentId==teamId)return TeamWriteResult.SelfParent;
        var schema=await ReadHrSchemaAsync(cn,tx);
        var departmentSql=schema.DepartmentHasBranch ? "SELECT COUNT(1) FROM dbo.Departments d JOIN dbo.Branches b ON b.Id=d.BranchId AND b.IsActive=1 AND b.IsDeleted=0 JOIN dbo.Companies c ON c.Id=b.CompanyId AND c.IsActive=1 AND c.IsDeleted=0 WHERE d.Id=@id AND d.IsDeleted=0 AND d.IsActive=1" : "SELECT COUNT(1) FROM dbo.Departments WHERE Id=@id AND IsDeleted=0 AND IsActive=1";
        await using(var dep=new SqlCommand(departmentSql,cn,tx)){dep.Parameters.AddWithValue("@id",departmentId);if(Convert.ToInt32(await dep.ExecuteScalarAsync())!=1)return TeamWriteResult.InvalidDepartment;}
        // Moving a referenced Team would rewrite the meaning of historical HR assignments.
        if(teamId.HasValue){await using var moved=new SqlCommand("SELECT COUNT(1) FROM dbo.Teams t WHERE t.Id=@team AND (t.DepartmentId IS NULL OR t.DepartmentId<>@department) AND (EXISTS(SELECT 1 FROM dbo.Teams child WHERE child.ParentTeamId=t.Id AND child.IsDeleted=0) OR EXISTS(SELECT 1 FROM dbo.EmployeeAssignments a WHERE a.TeamId=t.Id))",cn,tx);moved.Parameters.AddWithValue("@team",teamId.Value);moved.Parameters.AddWithValue("@department",departmentId);if(Convert.ToInt32(await moved.ExecuteScalarAsync())!=0)return TeamWriteResult.HasDependencies;}
        if(!parentId.HasValue)return TeamWriteResult.Success;
        var parentSql=schema.TeamHasBranch ? "SELECT t.DepartmentId FROM dbo.Teams t JOIN dbo.Departments d ON d.Id=t.DepartmentId AND d.BranchId=t.BranchId WHERE t.Id=@id AND t.IsDeleted=0" : "SELECT DepartmentId FROM dbo.Teams WHERE Id=@id AND IsDeleted=0";
        await using(var parent=new SqlCommand(parentSql,cn,tx)){parent.Parameters.AddWithValue("@id",parentId.Value);var value=await parent.ExecuteScalarAsync();if(value is not Guid parentDepartment)return TeamWriteResult.InvalidParent;if(parentDepartment!=departmentId)return TeamWriteResult.InvalidParent;}
        if(teamId.HasValue){const string cycle=""";WITH Ancestors AS(SELECT Id,ParentTeamId FROM dbo.Teams WHERE Id=@parent AND IsDeleted=0 UNION ALL SELECT t.Id,t.ParentTeamId FROM dbo.Teams t JOIN Ancestors a ON t.Id=a.ParentTeamId WHERE t.IsDeleted=0) SELECT COUNT(1) FROM Ancestors WHERE Id=@team OPTION(MAXRECURSION 32767);""";await using var cmd=new SqlCommand(cycle,cn,tx);cmd.Parameters.AddWithValue("@parent",parentId.Value);cmd.Parameters.AddWithValue("@team",teamId.Value);if(Convert.ToInt32(await cmd.ExecuteScalarAsync())>0)return TeamWriteResult.Cycle;}
        return TeamWriteResult.Success;
    }

    private static SqlCommand TeamCommand(string sql,SqlConnection cn,SqlTransaction tx,TeamCreateVm vm,Guid? userId)
    {var cmd=new SqlCommand(sql,cn,tx);cmd.Parameters.AddWithValue("@departmentId",vm.DepartmentId);cmd.Parameters.AddWithValue("@parentId",(object?)vm.ParentTeamId??DBNull.Value);cmd.Parameters.Add("@code",System.Data.SqlDbType.VarChar,50).Value=TeamRules.NormalizeCode(vm.Code);cmd.Parameters.Add("@name",System.Data.SqlDbType.NVarChar,250).Value=vm.Name.Trim();cmd.Parameters.Add("@description",System.Data.SqlDbType.NVarChar,1000).Value=string.IsNullOrWhiteSpace(vm.Description)?DBNull.Value:vm.Description.Trim();cmd.Parameters.AddWithValue("@sortOrder",vm.SortOrder);cmd.Parameters.AddWithValue("@isActive",vm.IsActive);cmd.Parameters.AddWithValue("@userId",(object?)userId??DBNull.Value);return cmd;}

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
