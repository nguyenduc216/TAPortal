using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace TAPortal.Shared;
public sealed class MembershipInput
{
    public Guid CustomerId { get; set; }
    public Guid? UserId { get; set; }

    [Required, RegularExpression("OWNER|ADMIN|VIEWER")]
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
    // MVP 01 deliberately has no delegated Agent scope. Never accept actor identity from request data.
    private static async Task RequireSystemAdmin(SqlConnection cn, SqlTransaction? tx, Guid actor)
    {
        await using var cmd = new SqlCommand("""
SELECT COUNT(*) FROM dbo.Users u
JOIN dbo.UserRoles ur ON ur.UserId=u.Id
JOIN dbo.Roles r ON r.Id=ur.RoleId
WHERE u.Id=@actor AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE'
AND r.Code='SYS_ADMIN' AND r.IsActive=1 AND r.IsDeleted=0;
""", cn, tx);
        cmd.Parameters.AddWithValue("@actor", actor);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 0)
            throw new UnauthorizedAccessException("Only active SYS_ADMIN users can manage customer portal accounts.");
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
        await tx.CommitAsync();
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
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
