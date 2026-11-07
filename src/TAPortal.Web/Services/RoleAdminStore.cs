using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Admin → Vai trò & phân quyền. Only the catalogue codes (AdminPermissions) are shown and changed; any other grant of a role
// (e.g. HR.* codes of another system sharing dbo.Permissions) is preserved untouched. SYS_ADMIN is read-only: it always has
// every permission through PermissionHandler, so it is never edited, deactivated or deleted here.
public sealed record RoleListItem(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsActive, int UserCount, int PermissionCount)
{
    public bool IsSystemAdmin => Code == RoleChangePolicy.SystemAdminCode;
}

// PermissionCodes: the role's catalogue codes only. UserCount: users (not deleted) assigned to the role.
public sealed record RoleDetail(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsActive, IReadOnlySet<string> PermissionCodes, int UserCount)
{
    public bool IsSystemAdmin => Code == RoleChangePolicy.SystemAdminCode;
    public string Stamp => RoleAdminPolicy.Stamp(Name, Description, IsActive, PermissionCodes);
}

public sealed class RoleInput
{
    [StringLength(30)]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên vai trò."), StringLength(200, ErrorMessage = "Tên vai trò tối đa 200 ký tự.")]
    public string Name { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<string>? Permissions { get; set; }

    public string? Stamp { get; set; }

    public static RoleInput From(RoleDetail role) => new()
    {
        Code = role.Code, Name = role.Name, Description = role.Description, IsActive = role.IsActive,
        Permissions = role.PermissionCodes.Order(StringComparer.Ordinal).ToList(), Stamp = role.Stamp
    };
}

public enum RoleAdminOutcome { Saved, NoChange, NotFound, Forbidden, SystemRole, InvalidCode, DuplicateCode, InvalidPermission, InUse, Conflict, AuditUnavailable }

public sealed record RoleAdminResult(RoleAdminOutcome Outcome, Guid? Id = null, bool AuditPersisted = false);

// Storage-independent rules, shared by the SQL store and the test fakes.
public static class RoleAdminPolicy
{
    private static readonly Regex CodePattern = new("^[A-Z0-9_]{2,30}$", RegexOptions.CultureInvariant);

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static bool IsValidCode(string code) => CodePattern.IsMatch(code) && code != RoleChangePolicy.SystemAdminCode;

    public static string Stamp(string name, string? description, bool isActive, IEnumerable<string> permissions) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f",
            name, description ?? "", isActive ? "1" : "0", string.Join(",", permissions.Select(p => p.ToUpperInvariant()).Order(StringComparer.Ordinal))))));

    // Requested codes must be catalogue codes that exist (active) in dbo.Permissions; null when any is not.
    public static HashSet<string>? Permissions(IEnumerable<string>? requested, IReadOnlySet<string> available)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in requested ?? [])
        {
            var code = (raw ?? "").Trim();
            if (code.Length == 0) continue;
            if (!AdminPermissions.IsCatalogued(code) || !available.Contains(code)) return null;
            set.Add(AdminPermissions.All.First(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)).Code);
        }
        return set;
    }

    public static RoleAdminOutcome EvaluateCreate(RoleInput input, bool actorAllowed, bool codeTaken, HashSet<string>? permissions)
    {
        if (!actorAllowed) return RoleAdminOutcome.Forbidden;
        var code = NormalizeCode(input.Code);
        if (code == RoleChangePolicy.SystemAdminCode) return RoleAdminOutcome.SystemRole;
        if (!IsValidCode(code)) return RoleAdminOutcome.InvalidCode;
        if (codeTaken) return RoleAdminOutcome.DuplicateCode;
        return permissions is null ? RoleAdminOutcome.InvalidPermission : RoleAdminOutcome.Saved;
    }

    public static RoleAdminOutcome EvaluateUpdate(RoleDetail current, RoleInput input, bool actorAllowed, HashSet<string>? permissions)
    {
        if (!actorAllowed) return RoleAdminOutcome.Forbidden;
        if (current.IsSystemAdmin) return RoleAdminOutcome.SystemRole;
        if (!string.Equals(input.Stamp, current.Stamp, StringComparison.Ordinal)) return RoleAdminOutcome.Conflict;
        if (permissions is null) return RoleAdminOutcome.InvalidPermission;
        var unchanged = current.Name == input.Name.Trim() && (current.Description ?? "") == (input.Description?.Trim() ?? "")
            && current.IsActive == input.IsActive && current.PermissionCodes.SetEquals(permissions);
        return unchanged ? RoleAdminOutcome.NoChange : RoleAdminOutcome.Saved;
    }

    public static RoleAdminOutcome EvaluateDelete(RoleDetail current, string? stamp, bool actorAllowed)
    {
        if (!actorAllowed) return RoleAdminOutcome.Forbidden;
        if (current.IsSystemAdmin) return RoleAdminOutcome.SystemRole;
        if (!string.Equals(stamp, current.Stamp, StringComparison.Ordinal)) return RoleAdminOutcome.Conflict;
        return current.UserCount > 0 ? RoleAdminOutcome.InUse : RoleAdminOutcome.Saved;
    }

    public static string Json(string name, string? description, bool isActive, IEnumerable<string> permissions) =>
        AuditSql.Json(("name", name), ("description", description), ("isActive", isActive), ("permissions", string.Join(",", permissions.Order(StringComparer.Ordinal))));
}

public interface IRoleAdminStore
{
    Task<IReadOnlyList<RoleListItem>> ListAsync();
    Task<RoleDetail?> GetAsync(Guid id);
    // Catalogue codes present and active in dbo.Permissions (a code missing there cannot be granted yet: run migration 028).
    Task<IReadOnlySet<string>> AvailablePermissionsAsync();
    Task<RoleAdminResult> CreateAsync(Guid actorId, RoleInput input, string? ipAddress);
    Task<RoleAdminResult> UpdateAsync(Guid actorId, Guid roleId, RoleInput input, string? ipAddress);
    Task<RoleAdminResult> DeleteAsync(Guid actorId, Guid roleId, string? stamp, string? ipAddress);
}

public static class RoleAdminSql
{
    public const string List = """
SELECT r.Id,r.Code,r.Name,r.Description,r.IsSystem,r.IsActive,
 (SELECT COUNT(1) FROM dbo.UserRoles ur JOIN dbo.Users u ON u.Id=ur.UserId WHERE ur.RoleId=r.Id AND u.IsDeleted=0)
FROM dbo.Roles r WHERE r.IsDeleted=0 ORDER BY r.IsSystem DESC,r.Name;
""";
    public const string ListPermissions = "SELECT rp.RoleId,p.Code FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id=rp.PermissionId JOIN dbo.Roles r ON r.Id=rp.RoleId WHERE r.IsDeleted=0 AND p.IsDeleted=0 AND p.IsActive=1;";
    public const string Role = "SELECT Id,Code,Name,Description,IsSystem,IsActive FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Id=@r AND IsDeleted=0;";
    public const string RolePermissions = "SELECT p.Code FROM dbo.RolePermissions rp WITH (UPDLOCK, HOLDLOCK) JOIN dbo.Permissions p ON p.Id=rp.PermissionId WHERE rp.RoleId=@r AND p.IsDeleted=0 AND p.IsActive=1;";
    public const string RoleUsers = "SELECT COUNT(1) FROM dbo.UserRoles ur WITH (UPDLOCK, HOLDLOCK) JOIN dbo.Users u ON u.Id=ur.UserId WHERE ur.RoleId=@r AND u.IsDeleted=0;";
    public const string Available = "SELECT Id,Code FROM dbo.Permissions WHERE IsDeleted=0 AND IsActive=1;";
    public const string CodeTaken = "SELECT COUNT(1) FROM dbo.Roles WITH (UPDLOCK, HOLDLOCK) WHERE Code=@code AND IsDeleted=0;";
    public const string Insert = "INSERT dbo.Roles(Id,Code,Name,Description,IsSystem,IsActive,CreatedBy) VALUES(@r,@code,@name,@desc,0,@active,@by);";
    public const string Update = "UPDATE dbo.Roles SET Name=@name,Description=@desc,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@r AND IsDeleted=0 AND Code<>'SYS_ADMIN';";
    public const string Grant = "INSERT dbo.RolePermissions(RoleId,PermissionId,CreatedBy) VALUES(@r,@p,@by);";
    public const string Revoke = "DELETE FROM dbo.RolePermissions WHERE RoleId=@r AND PermissionId=@p;";
    // Soft delete only (role row, permission rows and history stay); IsActive=0 as a second barrier.
    public const string SoftDelete = """
UPDATE dbo.Roles SET IsDeleted=1,IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@r AND IsDeleted=0 AND Code<>'SYS_ADMIN';
IF COL_LENGTH(N'dbo.Roles',N'DeletedAt') IS NOT NULL AND COL_LENGTH(N'dbo.Roles',N'DeletedBy') IS NOT NULL
 EXEC sp_executesql N'UPDATE dbo.Roles SET DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@r', N'@r uniqueidentifier,@by uniqueidentifier', @r=@r, @by=@by;
""";
    public const string Audit = AuditSql.Insert;
}

public sealed class SqlRoleAdminStore(IConfiguration configuration, AdminAuditOptions audit) : IRoleAdminStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<IReadOnlyList<RoleListItem>> ListAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var rows = new List<(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsActive, int Users)>();
        await using (var cmd = new SqlCommand(RoleAdminSql.List, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                rows.Add((r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.GetBoolean(5), r.GetInt32(6)));
        var grants = new Dictionary<Guid, HashSet<string>>();
        await using (var cmd = new SqlCommand(RoleAdminSql.ListPermissions, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                if (!AdminPermissions.IsCatalogued(r.GetString(1))) continue;
                if (!grants.TryGetValue(r.GetGuid(0), out var set)) grants[r.GetGuid(0)] = set = new(StringComparer.OrdinalIgnoreCase);
                set.Add(r.GetString(1));
            }
        return rows.Select(x => new RoleListItem(x.Id, x.Code, x.Name, x.Description, x.IsSystem, x.IsActive, x.Users, grants.TryGetValue(x.Id, out var g) ? g.Count : 0)).ToList();
    }

    public async Task<RoleDetail?> GetAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var role = await Read(cn, tx, id);
        await tx.CommitAsync();
        return role;
    }

    public async Task<IReadOnlySet<string>> AvailablePermissionsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return (await Available(cn, null)).Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RoleAdminResult> CreateAsync(Guid actorId, RoleInput input, string? ipAddress)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var code = RoleAdminPolicy.NormalizeCode(input.Code);
            var allowed = await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, AdminPermissions.RolesManage);
            var taken = RoleAdminPolicy.IsValidCode(code) && Convert.ToInt32(await Scalar(cn, tx, RoleAdminSql.CodeTaken, ("@code", code))) > 0;
            var available = await Available(cn, tx);
            var permissions = RoleAdminPolicy.Permissions(input.Permissions, available.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            var outcome = RoleAdminPolicy.EvaluateCreate(input, allowed, taken, permissions);
            if (outcome != RoleAdminOutcome.Saved) return new(outcome);

            var id = Guid.NewGuid();
            await Exec(cn, tx, RoleAdminSql.Insert, ("@r", id), ("@code", code), ("@name", input.Name.Trim()), ("@desc", Db(input.Description)), ("@active", input.IsActive), ("@by", actorId));
            foreach (var p in permissions!)
                await Exec(cn, tx, RoleAdminSql.Grant, ("@r", id), ("@p", available[p]), ("@by", actorId));
            var written = await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, "ROLE_CREATED", "dbo.Roles", id.ToString(), "{}",
                AuditSql.Json(("code", code), ("name", input.Name.Trim()), ("description", input.Description?.Trim()), ("isActive", input.IsActive), ("permissions", string.Join(",", permissions.Order(StringComparer.Ordinal)))), ipAddress);
            await tx.CommitAsync();
            return new(RoleAdminOutcome.Saved, id, written.Written);
        }
        catch (AuditUnavailableException) { return new(RoleAdminOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return new(RoleAdminOutcome.DuplicateCode); }
        catch (SqlException ex) when (ex.Number is 1205) { return new(RoleAdminOutcome.Conflict); }
    }

    public async Task<RoleAdminResult> UpdateAsync(Guid actorId, Guid roleId, RoleInput input, string? ipAddress)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var current = await Read(cn, tx, roleId);
            if (current is null) return new(RoleAdminOutcome.NotFound);
            var allowed = await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, AdminPermissions.RolesManage);
            var available = await Available(cn, tx);
            var permissions = RoleAdminPolicy.Permissions(input.Permissions, available.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            var outcome = RoleAdminPolicy.EvaluateUpdate(current, input, allowed, permissions);
            if (outcome != RoleAdminOutcome.Saved) return new(outcome, roleId);

            await Exec(cn, tx, RoleAdminSql.Update, ("@r", roleId), ("@name", input.Name.Trim()), ("@desc", Db(input.Description)), ("@active", input.IsActive), ("@by", actorId));
            // Only catalogue grants change; every other grant of the role stays as it is.
            foreach (var p in current.PermissionCodes.Where(c => !permissions!.Contains(c)))
                if (available.TryGetValue(p, out var pid)) await Exec(cn, tx, RoleAdminSql.Revoke, ("@r", roleId), ("@p", pid));
            foreach (var p in permissions!.Where(c => !current.PermissionCodes.Contains(c)))
                await Exec(cn, tx, RoleAdminSql.Grant, ("@r", roleId), ("@p", available[p]), ("@by", actorId));
            var written = await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, "ROLE_UPDATED", "dbo.Roles", roleId.ToString(),
                RoleAdminPolicy.Json(current.Name, current.Description, current.IsActive, current.PermissionCodes),
                RoleAdminPolicy.Json(input.Name.Trim(), input.Description?.Trim(), input.IsActive, permissions), ipAddress);
            await tx.CommitAsync();
            return new(RoleAdminOutcome.Saved, roleId, written.Written);
        }
        catch (AuditUnavailableException) { return new(RoleAdminOutcome.AuditUnavailable, roleId); }
        catch (SqlException ex) when (ex.Number is 1205 or 2601 or 2627 or 547) { return new(RoleAdminOutcome.Conflict, roleId); }
    }

    public async Task<RoleAdminResult> DeleteAsync(Guid actorId, Guid roleId, string? stamp, string? ipAddress)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var current = await Read(cn, tx, roleId);
            if (current is null) return new(RoleAdminOutcome.NotFound);
            var allowed = await ActorPermissionSql.HasPermissionAsync(cn, tx, actorId, AdminPermissions.RolesManage);
            var outcome = RoleAdminPolicy.EvaluateDelete(current, stamp, allowed);
            if (outcome != RoleAdminOutcome.Saved) return new(outcome, roleId);
            await Exec(cn, tx, RoleAdminSql.SoftDelete, ("@r", roleId), ("@by", actorId));
            var written = await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actorId, "ROLE_SOFT_DELETED", "dbo.Roles", roleId.ToString(),
                AuditSql.Json(("code", current.Code), ("name", current.Name), ("isActive", current.IsActive), ("permissions", string.Join(",", current.PermissionCodes.Order(StringComparer.Ordinal)))),
                AuditSql.Json(("isDeleted", true), ("isActive", false)), ipAddress);
            await tx.CommitAsync();
            return new(RoleAdminOutcome.Saved, roleId, written.Written);
        }
        catch (AuditUnavailableException) { return new(RoleAdminOutcome.AuditUnavailable, roleId); }
        catch (SqlException ex) when (ex.Number is 1205) { return new(RoleAdminOutcome.Conflict, roleId); }
    }

    private static async Task<RoleDetail?> Read(SqlConnection cn, SqlTransaction tx, Guid id)
    {
        Guid roleId; string code, name; string? description; bool isSystem, isActive;
        await using (var cmd = new SqlCommand(RoleAdminSql.Role, cn, tx))
        {
            cmd.Parameters.AddWithValue("@r", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            (roleId, code, name, description, isSystem, isActive) = (r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.GetBoolean(5));
        }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(RoleAdminSql.RolePermissions, cn, tx))
        {
            cmd.Parameters.AddWithValue("@r", id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                if (AdminPermissions.IsCatalogued(r.GetString(0))) codes.Add(r.GetString(0));
        }
        var users = Convert.ToInt32(await Scalar(cn, tx, RoleAdminSql.RoleUsers, ("@r", id)));
        return new(roleId, code, name, description, isSystem, isActive, codes, users);
    }

    private static async Task<Dictionary<string, Guid>> Available(SqlConnection cn, SqlTransaction? tx)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(RoleAdminSql.Available, cn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (AdminPermissions.IsCatalogued(r.GetString(1))) map[r.GetString(1)] = r.GetGuid(0);
        return map;
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
