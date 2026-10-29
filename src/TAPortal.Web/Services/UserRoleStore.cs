using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

public sealed record RoleOption(Guid Id, string Code, string Name, bool IsActive);

// Direct per-user override from dbo.UserPermissions (read-only here; DENY removes a permission granted by roles).
public sealed record DirectPermission(string Code, string Name, string Effect);

// State read inside the update transaction. SystemAdminIds = active users currently holding the active SYS_ADMIN role.
public sealed record UserRoleSnapshot(
    Guid UserId, string Username, string DisplayName, bool UserActive, string Status,
    IReadOnlyList<RoleOption> Roles, IReadOnlySet<Guid> AssignedRoleIds, IReadOnlySet<Guid> SystemAdminIds,
    IReadOnlyList<DirectPermission>? DirectPermissions = null)
{
    public bool IsLastSystemAdmin => SystemAdminIds.Count == 1 && SystemAdminIds.Contains(UserId);
    public string Stamp => RoleChangePolicy.Stamp(AssignedRoleIds);
}

public enum RoleChangeOutcome { Updated, NoChange, NotFound, Forbidden, InvalidRole, LastSystemAdmin, Conflict, AuditUnavailable }

public sealed record RoleChangeResult(RoleChangeOutcome Outcome, bool AuditPersisted = false, IReadOnlyList<string>? Added = null, IReadOnlyList<string>? Removed = null);

public sealed record RoleChangePlan(RoleChangeOutcome Outcome, IReadOnlySet<Guid> Final, IReadOnlyList<Guid> Add, IReadOnlyList<Guid> Remove);

// Business rules for AUD-12, independent of storage so they can be tested without SQL.
public static class RoleChangePolicy
{
    public const string SystemAdminCode = "SYS_ADMIN";

    public static string Stamp(IEnumerable<Guid> roleIds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", roleIds.Select(r => r.ToString("N")).Order(StringComparer.Ordinal)))));

    public static RoleChangePlan Evaluate(UserRoleSnapshot target, Guid actorId, IReadOnlyCollection<Guid> requested, string? expectedStamp)
    {
        var none = new HashSet<Guid>();
        // Only an active SYS_ADMIN (re-read from the database, not from the cookie) may change roles.
        if (!target.SystemAdminIds.Contains(actorId))
            return new(RoleChangeOutcome.Forbidden, none, [], []);
        var catalog = target.Roles.ToDictionary(r => r.Id);
        // Unknown, deleted or inactive roles cannot be newly assigned; the client may only choose from the active catalog.
        if (requested.Any(id => !catalog.TryGetValue(id, out var role) || !role.IsActive))
            return new(RoleChangeOutcome.InvalidRole, none, [], []);
        if (!string.Equals(expectedStamp, target.Stamp, StringComparison.Ordinal))
            return new(RoleChangeOutcome.Conflict, none, [], []);

        // Assignments to inactive roles are preserved untouched; this screen only manages active roles.
        var final = new HashSet<Guid>(requested.Concat(target.AssignedRoleIds.Where(id => !catalog.TryGetValue(id, out var r) || !r.IsActive)));
        var add = final.Except(target.AssignedRoleIds).ToList();
        var remove = target.AssignedRoleIds.Except(final).ToList();
        if (add.Count == 0 && remove.Count == 0)
            return new(RoleChangeOutcome.NoChange, final, [], []);

        var sysAdminRoles = target.Roles.Where(r => r.Code == SystemAdminCode && r.IsActive).Select(r => r.Id).ToHashSet();
        var losesSystemAdmin = target.SystemAdminIds.Contains(target.UserId) && !final.Overlaps(sysAdminRoles);
        if (losesSystemAdmin && target.SystemAdminIds.Count(id => id != target.UserId) == 0)
            return new(RoleChangeOutcome.LastSystemAdmin, none, [], []);

        return new(RoleChangeOutcome.Updated, final, add, remove);
    }
}

public interface IUserRoleStore
{
    Task<UserRoleSnapshot?> GetAsync(Guid userId);
    Task<RoleChangeResult> UpdateAsync(Guid actorId, Guid userId, IReadOnlyCollection<Guid> requestedRoleIds, string? expectedStamp, string? ipAddress);
}

// SQL kept public so tests can assert it never touches passwords, usernames or Customer Portal memberships.
public static class UserRoleSql
{
    public const string User = "SELECT Id,Username,DisplayName,IsActive,Status FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id=@u AND IsDeleted=0;";
    public const string Roles = "SELECT Id,Code,Name,IsActive FROM dbo.Roles WHERE IsDeleted=0 ORDER BY IsSystem DESC,Name;";
    public const string Assigned = "SELECT RoleId FROM dbo.UserRoles WITH (UPDLOCK, HOLDLOCK) WHERE UserId=@u;";
    public const string SystemAdmins = """
SELECT DISTINCT u.Id FROM dbo.UserRoles ur WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Roles r ON r.Id=ur.RoleId JOIN dbo.Users u ON u.Id=ur.UserId
WHERE r.Code='SYS_ADMIN' AND r.IsActive=1 AND r.IsDeleted=0 AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE';
""";
    public const string DirectPermissions = "SELECT p.Code,p.Name,up.Effect FROM dbo.UserPermissions up JOIN dbo.Permissions p ON p.Id=up.PermissionId WHERE up.UserId=@u AND p.IsDeleted=0 ORDER BY up.Effect,p.Code;";
    public const string Remove = "DELETE FROM dbo.UserRoles WHERE UserId=@u AND RoleId=@r;";
    public const string Add = "INSERT dbo.UserRoles(UserId,RoleId,CreatedBy) VALUES(@u,@r,@by);";
    // Shared audit statement (TAPortal.Shared.AuditSql); kept here so the SQL-text guard test covers it.
    public const string Audit = AuditSql.Insert;
}

public sealed class SqlUserRoleStore(IConfiguration configuration, AdminAuditOptions auditOptions) : IUserRoleStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<UserRoleSnapshot?> GetAsync(Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var snapshot = await Read(cn, tx, userId);
        await tx.CommitAsync();
        return snapshot;
    }

    public async Task<RoleChangeResult> UpdateAsync(Guid actorId, Guid userId, IReadOnlyCollection<Guid> requestedRoleIds, string? expectedStamp, string? ipAddress)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        // Serializable + UPDLOCK on the target's roles and on every SYS_ADMIN assignment: concurrent edits serialize,
        // so two admins cannot each remove a different "other" SYS_ADMIN and leave none.
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var target = await Read(cn, tx, userId);
            if (target is null)
                return new(RoleChangeOutcome.NotFound);
            var plan = RoleChangePolicy.Evaluate(target, actorId, requestedRoleIds, expectedStamp);
            if (plan.Outcome != RoleChangeOutcome.Updated)
                return new(plan.Outcome);

            foreach (var role in plan.Remove)
                await Exec(cn, tx, UserRoleSql.Remove, ("@u", userId), ("@r", role));
            foreach (var role in plan.Add)
                await Exec(cn, tx, UserRoleSql.Add, ("@u", userId), ("@r", role), ("@by", actorId));

            string Codes(IEnumerable<Guid> ids) => string.Join(",", ids.Select(id => target.Roles.FirstOrDefault(r => r.Id == id)?.Code ?? id.ToString()).Order(StringComparer.Ordinal));
            var written = await AuditSql.WriteOrThrowAsync(cn, tx, auditOptions.RequireDurableAudit, actorId, "USER_ROLES_UPDATED", "dbo.Users", userId.ToString(),
                AuditSql.Json(("roles", Codes(target.AssignedRoleIds))), AuditSql.Json(("roles", Codes(plan.Final))), ipAddress);
            await tx.CommitAsync();
            return new(RoleChangeOutcome.Updated, written.Written, plan.Add.Select(id => Codes([id])).ToList(), plan.Remove.Select(id => Codes([id])).ToList());
        }
        catch (AuditUnavailableException)
        {
            return new(RoleChangeOutcome.AuditUnavailable); // nothing committed: the transaction rolls back on dispose
        }
        catch (SqlException ex) when (ex.Number is 1205 or 2601 or 2627 or 547)
        {
            // Deadlock victim, concurrent insert of the same pair, or a role/user deleted meanwhile.
            return new(RoleChangeOutcome.Conflict);
        }
    }

    private static async Task<UserRoleSnapshot?> Read(SqlConnection cn, SqlTransaction tx, Guid userId)
    {
        Guid id; string username, display, status; bool active;
        await using (var cmd = new SqlCommand(UserRoleSql.User, cn, tx))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            (id, username, display, active, status) = (r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetString(4));
        }
        var roles = new List<RoleOption>();
        await using (var cmd = new SqlCommand(UserRoleSql.Roles, cn, tx))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) roles.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        var assigned = await Ids(cn, tx, UserRoleSql.Assigned, userId);
        var admins = await Ids(cn, tx, UserRoleSql.SystemAdmins, null);
        var direct = new List<DirectPermission>();
        await using (var cmd = new SqlCommand(UserRoleSql.DirectPermissions, cn, tx))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) direct.Add(new(r.GetString(0), r.GetString(1), r.GetString(2)));
        }
        return new(id, username, display, active, status, roles, assigned, admins, direct);
    }

    private static async Task<HashSet<Guid>> Ids(SqlConnection cn, SqlTransaction tx, string sql, Guid? userId)
    {
        var set = new HashSet<Guid>();
        await using var cmd = new SqlCommand(sql, cn, tx);
        if (userId.HasValue) cmd.Parameters.AddWithValue("@u", userId.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetGuid(0));
        return set;
    }

    private static async Task Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
