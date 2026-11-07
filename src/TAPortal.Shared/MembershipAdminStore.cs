using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace TAPortal.Shared;
public sealed class MembershipInput
{
    public Guid CustomerId { get; set; }
    public Guid? UserId { get; set; }

    [Required, RegularExpression("OWNER|ADMIN|VIEWER|MEMBER")]
    public string PortalRole { get; set; } = "VIEWER";

    [StringLength(100)]
    public string? Username { get; set; }

    [StringLength(200)]
    public string? DisplayName { get; set; }

    [EmailAddress, StringLength(256)]
    public string? Email { get; set; }

    [StringLength(256)]
    public string? Password { get; set; }
}

public sealed record MembershipAdminRow(Guid UserId, Guid CustomerId, string DisplayName, string Username, string? Email, string CustomerName, string Role, bool Active, string Status, DateTime? LastLogin);
public sealed class MembershipAdminStore(IConfiguration configuration)
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);
    // Admin portal permission that unlocks this store (SYS_ADMIN always qualifies). Never accept actor identity from request data.
    public const string ManagePermission = "CUSTOMERS.PORTAL.MANAGE";

    // Re-checked in SQL inside the same transaction: SYS_ADMIN, or an active role / direct grant of CUSTOMERS.PORTAL.MANAGE
    // without a direct DENY (ActorPermissionSql). MVP 01 still has no per-customer delegated scope.
    private static async Task RequireSystemAdmin(SqlConnection cn, SqlTransaction? tx, Guid actor)
    {
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, ManagePermission))
            throw new UnauthorizedAccessException("Only active SYS_ADMIN users or users granted CUSTOMERS.PORTAL.MANAGE can manage customer portal accounts.");
    }

    public async Task<List<MembershipAdminRow>> List(Guid actor, Guid? customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await RequireSystemAdmin(cn, null, actor);
        await using var cmd = new SqlCommand("""
SELECT u.Id,c.Id,u.DisplayName,u.Username,u.Email,c.Name,m.PortalRole,m.IsActive,CASE WHEN u.IsDeleted=1 THEN 'DELETED' WHEN u.IsActive=0 THEN 'LOCKED' ELSE u.Status END,u.LastLoginAt
FROM dbo.CustomerPortalMemberships m JOIN dbo.Users u ON u.Id=m.UserId JOIN dbo.Customers c ON c.Id=m.CustomerId
WHERE (@c IS NULL OR c.Id=@c) ORDER BY c.Name,u.DisplayName
""", cn);
        cmd.Parameters.Add("@c", System.Data.SqlDbType.UniqueIdentifier).Value = (object? )customerId ?? DBNull.Value;
        var list = new List<MembershipAdminRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6), r.GetBoolean(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetDateTime(9)));
        return list;
    }

    public async Task Save(MembershipInput input, Guid actor, string? hash)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await RequireSystemAdmin(cn, tx, actor);
        var id = input.UserId ?? Guid.NewGuid();
        if (!input.UserId.HasValue)
        {
            await using var add = new SqlCommand("""
INSERT dbo.Users(Id,Username,NormalizedUsername,DisplayName,Email,NormalizedEmail,PasswordHash,Status,IsActive,CreatedBy)
VALUES(@u,@name,@normal,@display,@email,@ne,@hash,'ACTIVE',1,@actor)
""", cn, tx);
            add.Parameters.AddWithValue("@u", id);
            add.Parameters.AddWithValue("@name", input.Username!.Trim());
            add.Parameters.AddWithValue("@normal", input.Username.Trim().ToUpperInvariant());
            add.Parameters.AddWithValue("@display", input.DisplayName!.Trim());
            add.Parameters.AddWithValue("@email", (object? )input.Email ?? DBNull.Value);
            add.Parameters.AddWithValue("@ne", string.IsNullOrWhiteSpace(input.Email) ? DBNull.Value : input.Email.Trim().ToUpperInvariant());
            add.Parameters.AddWithValue("@hash", hash!);
            add.Parameters.AddWithValue("@actor", actor);
            await add.ExecuteNonQueryAsync();
        }

        string? previousRole = null;
        bool? previousActive = null;
        await using (var prev = new SqlCommand("SELECT PortalRole,IsActive FROM dbo.CustomerPortalMemberships WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@u AND CustomerId=@c", cn, tx))
        {
            prev.Parameters.AddWithValue("@u", id);
            prev.Parameters.AddWithValue("@c", input.CustomerId);
            await using var r = await prev.ExecuteReaderAsync();
            if (await r.ReadAsync()) (previousRole, previousActive) = (r.GetString(0), r.GetBoolean(1));
        }

        await using var cmd = new SqlCommand("""
IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE Id=@u AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE')
 OR NOT EXISTS(SELECT 1 FROM dbo.Customers WHERE Id=@c AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE')
 THROW 51000,'Inactive or missing user/customer',1;
IF EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@u AND CustomerId=@c)
 UPDATE dbo.CustomerPortalMemberships SET PortalRole=@role,IsActive=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE UserId=@u AND CustomerId=@c;
ELSE INSERT dbo.CustomerPortalMemberships(Id,UserId,CustomerId,PortalRole,IsActive,CreatedBy) VALUES(NEWID(),@u,@c,@role,1,@actor);
""", cn, tx);
        cmd.Parameters.AddWithValue("@u", id);
        cmd.Parameters.AddWithValue("@c", input.CustomerId);
        cmd.Parameters.AddWithValue("@role", input.PortalRole);
        cmd.Parameters.AddWithValue("@actor", actor);
        await cmd.ExecuteNonQueryAsync();
        // AUD-36: provisioning (possibly creating the user) is audited in the same transaction; no password data is recorded.
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, "CUSTOMER_PORTAL_MEMBERSHIP_SAVED", "dbo.CustomerPortalMemberships", $"{id}:{input.CustomerId}",
            previousRole is null ? "{}" : AuditSql.Json(("role", previousRole), ("isActive", previousActive)),
            AuditSql.Json(("role", input.PortalRole), ("isActive", true), ("userCreated", !input.UserId.HasValue)), null);
        await tx.CommitAsync();
    }

    /// <summary>
    /// Sets a new password hash and MustChangePassword=1 (migration 015) for a member of this customer. TA staff accounts
    /// (any dbo.UserRoles row) are never reset here. Audited in the same transaction without password data.
    /// Returns false when the user is not a non-staff member of the customer.
    /// </summary>
    public async Task<bool> ResetPassword(Guid userId, Guid customerId, string hash, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await RequireSystemAdmin(cn, tx, actor);
        await using var cmd = new SqlCommand("""
UPDATE u SET PasswordHash=@hash,MustChangePassword=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
FROM dbo.Users u
WHERE u.Id=@u AND u.IsDeleted=0
AND EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships m WHERE m.UserId=u.Id AND m.CustomerId=@c)
AND NOT EXISTS(SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId=u.Id)
""", cn, tx);
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@actor", actor);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@c", customerId);
        if (await cmd.ExecuteNonQueryAsync() != 1) return false;
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, "CUSTOMER_PORTAL_PASSWORD_RESET", "dbo.Users", $"{userId}:{customerId}",
            "{}", AuditSql.Json(("mustChangePassword", true)), null);
        await tx.CommitAsync();
        return true;
    }

    public async Task SetActive(Guid userId, Guid customerId, bool active, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await RequireSystemAdmin(cn, tx, actor);
        await using var cmd = new SqlCommand("UPDATE dbo.CustomerPortalMemberships SET IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE UserId=@u AND CustomerId=@c", cn, tx);
        cmd.Parameters.AddWithValue("@active", active);
        cmd.Parameters.AddWithValue("@actor", actor);
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@c", customerId);
        if (await cmd.ExecuteNonQueryAsync() == 1)
            await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, "CUSTOMER_PORTAL_MEMBERSHIP_ACTIVE_CHANGED", "dbo.CustomerPortalMemberships", $"{userId}:{customerId}",
                AuditSql.Json(("isActive", !active)), AuditSql.Json(("isActive", active)), null);
        await tx.CommitAsync();
    }
}
