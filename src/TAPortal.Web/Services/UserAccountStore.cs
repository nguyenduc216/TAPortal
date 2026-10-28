using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Single login-eligibility rule used by every authentication path (Admin login/cookie validation,
// Customer Portal login/membership, SYS_ADMIN counting): IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'.
public static class AccountStatusPolicy
{
    public const string ActiveStatus = "ACTIVE";
    public static bool IsLoginEligible(bool isActive, string? status, bool isDeleted) => !isDeleted && isActive && status == ActiveStatus;

    public static string Describe(bool isActive, string? status, bool isDeleted) =>
        isDeleted ? "Đã xóa" : !isActive ? "Đã vô hiệu hóa" : status != ActiveStatus ? $"Bị khóa (trạng thái hệ thống: {status})" : "Hoạt động";
}

public sealed class AdminAuditOptions
{
    // false keeps CP6 behaviour (change allowed, application log warning) until dbo.AuditLogs is verified;
    // set Security__Audit__RequireDurableAudit=true after scripts/007-verify-audit-logs.sql reports PASS.
    public bool RequireDurableAudit { get; set; }
}

public sealed record UserAccountSnapshot(
    Guid Id, string Username, string DisplayName, string? Email, string? Phone, bool IsActive, string Status,
    IReadOnlyList<string> RoleNames, IReadOnlySet<Guid> SystemAdminIds, int ActiveMembershipCount)
{
    public string Stamp => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", DisplayName, Email ?? "", Phone ?? "", IsActive ? "1" : "0", Status))));
    public bool LoginEligible => AccountStatusPolicy.IsLoginEligible(IsActive, Status, false);
}

public enum AccountChangeOutcome { Updated, NotFound, Forbidden, Conflict, SelfDeactivation, SelfDeletion, LastSystemAdmin, DuplicateEmail, AuditUnavailable }

public sealed record AccountChangeResult(AccountChangeOutcome Outcome, bool AuditPersisted = false);

public static class AccountChangePolicy
{
    // Shared by profile/active updates and soft delete. Status is never changed here (no approved meaning for other values).
    public static AccountChangeOutcome Evaluate(UserAccountSnapshot target, Guid actorId, string? expectedStamp, bool remainsLoginEligible, bool isDeletion)
    {
        if (!target.SystemAdminIds.Contains(actorId))
            return AccountChangeOutcome.Forbidden;
        if (!string.Equals(expectedStamp, target.Stamp, StringComparison.Ordinal))
            return AccountChangeOutcome.Conflict;
        if (target.Id == actorId && isDeletion)
            return AccountChangeOutcome.SelfDeletion;
        if (target.Id == actorId && !remainsLoginEligible)
            return AccountChangeOutcome.SelfDeactivation;
        // A user leaving the eligible set while holding SYS_ADMIN must not empty that set.
        if (target.SystemAdminIds.Contains(target.Id) && !remainsLoginEligible && target.SystemAdminIds.Count(id => id != target.Id) == 0)
            return AccountChangeOutcome.LastSystemAdmin;
        return AccountChangeOutcome.Updated;
    }
}

public interface IUserAccountStore
{
    Task<UserAccountSnapshot?> GetAsync(Guid userId);
    Task<AccountChangeResult> UpdateAsync(Guid actorId, UserEditVm vm, string? expectedStamp, string? ipAddress);
    Task<AccountChangeResult> SoftDeleteAsync(Guid actorId, Guid userId, string? expectedStamp, string? ipAddress);
}

public static class AuditSql
{
    // Inserts only when dbo.AuditLogs exists with the expected columns; returns 1 when a row was written.
    public const string Insert = """
IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'UserId') IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'Action') IS NOT NULL
 AND COL_LENGTH(N'dbo.AuditLogs',N'EntityName') IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'EntityId') IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'OldValues') IS NOT NULL
 AND COL_LENGTH(N'dbo.AuditLogs',N'NewValues') IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'IpAddress') IS NOT NULL
BEGIN
 INSERT dbo.AuditLogs(UserId,Action,EntityName,EntityId,OldValues,NewValues,IpAddress) VALUES(@actor,@action,@entity,@target,@old,@new,@ip);
 SELECT CAST(1 AS bit);
END
ELSE SELECT CAST(0 AS bit);
""";

    public static async Task<bool> WriteAsync(SqlConnection cn, SqlTransaction tx, Guid actor, string action, string entity, string target, string oldValues, string newValues, string? ip)
    {
        await using var cmd = new SqlCommand(Insert, cn, tx);
        cmd.Parameters.AddWithValue("@actor", actor);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@entity", entity);
        cmd.Parameters.AddWithValue("@target", target);
        cmd.Parameters.AddWithValue("@old", oldValues);
        cmd.Parameters.AddWithValue("@new", newValues);
        cmd.Parameters.AddWithValue("@ip", (object?)ip ?? DBNull.Value);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public static string Json(params (string Key, object? Value)[] fields) =>
        System.Text.Json.JsonSerializer.Serialize(fields.ToDictionary(f => f.Key, f => f.Value));
}

public static class UserAccountSql
{
    public const string User = "SELECT Id,Username,DisplayName,Email,Phone,IsActive,Status FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id=@u AND IsDeleted=0;";
    public const string Roles = "SELECT r.Name FROM dbo.UserRoles ur JOIN dbo.Roles r ON r.Id=ur.RoleId WHERE ur.UserId=@u AND r.IsDeleted=0 ORDER BY r.Name;";
    public const string SystemAdmins = """
SELECT DISTINCT u.Id FROM dbo.UserRoles ur WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.Roles r ON r.Id=ur.RoleId JOIN dbo.Users u WITH (UPDLOCK, HOLDLOCK) ON u.Id=ur.UserId
WHERE r.Code='SYS_ADMIN' AND r.IsActive=1 AND r.IsDeleted=0 AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE';
""";
    // Customer Portal memberships are only counted for the confirmation screen; they are never modified.
    public const string Memberships = "IF OBJECT_ID(N'dbo.CustomerPortalMemberships',N'U') IS NOT NULL SELECT COUNT(1) FROM dbo.CustomerPortalMemberships WHERE UserId=@u AND IsActive=1 ELSE SELECT 0;";
    public const string EmailTaken = "SELECT COUNT(1) FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE NormalizedEmail=@ne AND IsDeleted=0 AND Id<>@u;";
    public const string Update = "UPDATE dbo.Users SET DisplayName=@name,Email=@email,NormalizedEmail=@ne,Phone=@phone,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@u AND IsDeleted=0;";
    // Soft delete only: the row, role assignments, memberships and history stay for audit; IsActive=0 as a second barrier.
    public const string SoftDelete = """
UPDATE dbo.Users SET IsDeleted=1,IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@u AND IsDeleted=0;
IF COL_LENGTH(N'dbo.Users',N'DeletedAt') IS NOT NULL AND COL_LENGTH(N'dbo.Users',N'DeletedBy') IS NOT NULL
 EXEC sp_executesql N'UPDATE dbo.Users SET DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@u', N'@u uniqueidentifier,@by uniqueidentifier', @u=@u, @by=@by;
""";
}

public sealed class SqlUserAccountStore(IConfiguration configuration, AdminAuditOptions audit) : IUserAccountStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<UserAccountSnapshot?> GetAsync(Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var snapshot = await Read(cn, tx, userId);
        await tx.CommitAsync();
        return snapshot;
    }

    public Task<AccountChangeResult> UpdateAsync(Guid actorId, UserEditVm vm, string? expectedStamp, string? ipAddress) =>
        Change(actorId, vm.Id, expectedStamp, vm.IsActive, false, async (cn, tx, before) =>
        {
            var ne = string.IsNullOrWhiteSpace(vm.Email) ? null : vm.Email.Trim().ToUpperInvariant();
            if (ne != null && Convert.ToInt32(await Scalar(cn, tx, UserAccountSql.EmailTaken, ("@ne", ne), ("@u", vm.Id))) > 0)
                return AccountChangeOutcome.DuplicateEmail;
            await Exec(cn, tx, UserAccountSql.Update, ("@u", vm.Id), ("@name", vm.DisplayName.Trim()), ("@email", Db(vm.Email)), ("@ne", (object?)ne ?? DBNull.Value), ("@phone", Db(vm.Phone)), ("@active", vm.IsActive), ("@by", actorId));
            return AccountChangeOutcome.Updated;
        }, before => ("USER_UPDATED",
            AuditSql.Json(("displayName", before.DisplayName), ("email", before.Email), ("phone", before.Phone), ("isActive", before.IsActive), ("status", before.Status)),
            AuditSql.Json(("displayName", vm.DisplayName.Trim()), ("email", vm.Email?.Trim()), ("phone", vm.Phone?.Trim()), ("isActive", vm.IsActive), ("status", before.Status))), ipAddress);

    public Task<AccountChangeResult> SoftDeleteAsync(Guid actorId, Guid userId, string? expectedStamp, string? ipAddress) =>
        Change(actorId, userId, expectedStamp, false, true, async (cn, tx, _) =>
        {
            await Exec(cn, tx, UserAccountSql.SoftDelete, ("@u", userId), ("@by", actorId));
            return AccountChangeOutcome.Updated;
        }, before => ("USER_SOFT_DELETED",
            AuditSql.Json(("username", before.Username), ("isActive", before.IsActive), ("status", before.Status), ("roles", string.Join(",", before.RoleNames)), ("activeMemberships", before.ActiveMembershipCount)),
            AuditSql.Json(("isDeleted", true), ("isActive", false))), ipAddress);

    private async Task<AccountChangeResult> Change(Guid actorId, Guid userId, string? stamp, bool remainsActive, bool deletion,
        Func<SqlConnection, SqlTransaction, UserAccountSnapshot, Task<AccountChangeOutcome>> apply,
        Func<UserAccountSnapshot, (string Action, string Old, string New)> describe, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        // Serializable + UPDLOCK on the target and on every SYS_ADMIN user/assignment: two admins cannot
        // concurrently deactivate or delete each other and leave no active SYS_ADMIN.
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var before = await Read(cn, tx, userId);
            if (before is null) return new(AccountChangeOutcome.NotFound);
            var remainsEligible = AccountStatusPolicy.IsLoginEligible(remainsActive, before.Status, deletion);
            var outcome = AccountChangePolicy.Evaluate(before, actorId, stamp, remainsEligible, deletion);
            if (outcome != AccountChangeOutcome.Updated) return new(outcome);
            outcome = await apply(cn, tx, before);
            if (outcome != AccountChangeOutcome.Updated) return new(outcome);
            var (action, oldValues, newValues) = describe(before);
            var persisted = await AuditSql.WriteAsync(cn, tx, actorId, action, "dbo.Users", userId.ToString(), oldValues, newValues, ip);
            if (!persisted && audit.RequireDurableAudit) return new(AccountChangeOutcome.AuditUnavailable); // rollback on dispose
            await tx.CommitAsync();
            return new(AccountChangeOutcome.Updated, persisted);
        }
        catch (SqlException ex) when (ex.Number is 1205)
        {
            return new(AccountChangeOutcome.Conflict);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return new(AccountChangeOutcome.DuplicateEmail);
        }
    }

    private static async Task<UserAccountSnapshot?> Read(SqlConnection cn, SqlTransaction tx, Guid userId)
    {
        UserAccountSnapshot? user;
        await using (var cmd = new SqlCommand(UserAccountSql.User, cn, tx))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            user = new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5), r.GetString(6), [], new HashSet<Guid>(), 0);
        }
        var roles = new List<string>();
        await using (var cmd = new SqlCommand(UserAccountSql.Roles, cn, tx))
        {
            cmd.Parameters.AddWithValue("@u", userId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) roles.Add(r.GetString(0));
        }
        var admins = new HashSet<Guid>();
        await using (var cmd = new SqlCommand(UserAccountSql.SystemAdmins, cn, tx))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) admins.Add(r.GetGuid(0));
        var memberships = Convert.ToInt32(await Scalar(cn, tx, UserAccountSql.Memberships, ("@u", userId)));
        return user with { RoleNames = roles, SystemAdminIds = admins, ActiveMembershipCount = memberships };
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
