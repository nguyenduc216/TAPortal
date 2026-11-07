using Microsoft.Data.SqlClient;
using TACustomerPortal.Web.Security;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Services;

/// <summary>A staff account of the company as listed on "Tài khoản & phân quyền".</summary>
public sealed record CompanyMember(Guid UserId, string Username, string DisplayName, string? Email, string? Phone, string Role, string? Permissions,
    bool IsActive, DateTime CreatedAt, bool SharedWithOtherCompanies = false);

public sealed record NewCompanyMember(string Username, string DisplayName, string? Email, string? Phone, string PasswordHash, string? Permissions);

public enum MemberOutcome { Done, NotFound, Protected, UsernameTaken, EmailTaken, SharedAccount, AuditUnavailable }

/// <summary>
/// Staff accounts of one company (operator decision 2026-11-07). Every call carries the signed-in company's id and only
/// touches that company's memberships; the OWNER membership is never changed here (also refused in SQL).
/// </summary>
public interface ICustomerMemberStore
{
    Task<IReadOnlyList<CompanyMember>> ListAsync(Guid customerId);
    Task<CompanyMember?> GetAsync(Guid customerId, Guid userId);
    Task<(MemberOutcome Outcome, Guid UserId)> CreateAsync(Guid customerId, NewCompanyMember member, Guid actor, string? ip);
    Task<MemberOutcome> SetPermissionsAsync(Guid customerId, Guid userId, string? permissions, Guid actor, string? ip);
    Task<MemberOutcome> SetActiveAsync(Guid customerId, Guid userId, bool active, Guid actor, string? ip);
    Task<MemberOutcome> SetPasswordAsync(Guid customerId, Guid userId, string passwordHash, Guid actor, string? ip);
}

public sealed class SqlCustomerMemberStore(IConfiguration configuration) : ICustomerMemberStore
{
    public const string CreatedAction = "CUSTOMER_MEMBER_CREATED";
    public const string PermissionsAction = "CUSTOMER_MEMBER_PERMISSIONS_CHANGED";
    public const string DeactivatedAction = "CUSTOMER_MEMBER_DEACTIVATED";
    public const string ReactivatedAction = "CUSTOMER_MEMBER_REACTIVATED";
    public const string PasswordAction = "CUSTOMER_MEMBER_PASSWORD_SET";
    private const string Entity = "dbo.CustomerPortalMemberships";

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));

    private const string Select = """
SELECT u.Id,u.Username,u.DisplayName,u.Email,u.Phone,m.PortalRole,m.Permissions,m.IsActive,m.CreatedAt,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships o WHERE o.UserId=m.UserId AND o.CustomerId<>m.CustomerId) THEN 1 ELSE 0 END
FROM dbo.CustomerPortalMemberships m JOIN dbo.Users u ON u.Id=m.UserId
WHERE m.CustomerId=@c AND u.IsDeleted=0
""";

    public async Task<IReadOnlyList<CompanyMember>> ListAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Select + " ORDER BY CASE m.PortalRole WHEN 'OWNER' THEN 0 ELSE 1 END, m.IsActive DESC, u.DisplayName", cn);
        cmd.Parameters.AddWithValue("@c", customerId);
        return await Read(cmd);
    }

    public async Task<CompanyMember?> GetAsync(Guid customerId, Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(Select + " AND m.UserId=@u", cn);
        cmd.Parameters.AddWithValue("@c", customerId);
        cmd.Parameters.AddWithValue("@u", userId);
        return (await Read(cmd)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<CompanyMember>> Read(SqlCommand cmd)
    {
        var list = new List<CompanyMember>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.GetBoolean(7), r.GetDateTime(8), r.GetInt32(9) == 1));
        return list;
    }

    public async Task<(MemberOutcome Outcome, Guid UserId)> CreateAsync(Guid customerId, NewCompanyMember x, Guid actor, string? ip)
    {
        var normalizedUsername = x.Username.ToUpperInvariant();
        var normalizedEmail = x.Email?.ToUpperInvariant();
        return await InTransaction(async (cn, tx) =>
        {
            // A login name is unique across usernames and emails (sign-in and password recovery accept either).
            if (await Scalar(cn, tx, "SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE (NormalizedUsername=@v OR NormalizedEmail=@v) AND IsDeleted=0", ("@v", normalizedUsername)) is not null)
                return (MemberOutcome.UsernameTaken, Guid.Empty);
            if (normalizedEmail is not null && await Scalar(cn, tx, "SELECT 1 FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE (NormalizedUsername=@v OR NormalizedEmail=@v) AND IsDeleted=0", ("@v", normalizedEmail)) is not null)
                return (MemberOutcome.EmailTaken, Guid.Empty);
            var userId = Guid.NewGuid();
            // The manager chose the password: the member changes it at first sign-in.
            await Run(cn, tx, "INSERT dbo.Users(Id,Username,NormalizedUsername,DisplayName,Email,NormalizedEmail,Phone,PasswordHash,MustChangePassword,Status,IsActive,CreatedBy) VALUES(@u,@name,@nname,@display,@email,@nemail,@phone,@hash,1,'ACTIVE',1,@actor)",
                ("@u", userId), ("@name", x.Username), ("@nname", normalizedUsername), ("@display", x.DisplayName), ("@email", x.Email), ("@nemail", normalizedEmail), ("@phone", x.Phone), ("@hash", x.PasswordHash), ("@actor", actor));
            var membershipId = Guid.NewGuid();
            await Run(cn, tx, "INSERT dbo.CustomerPortalMemberships(Id,UserId,CustomerId,PortalRole,Permissions,IsActive,CreatedBy) VALUES(@id,@u,@c,'MEMBER',@p,1,@actor)",
                ("@id", membershipId), ("@u", userId), ("@c", customerId), ("@p", x.Permissions), ("@actor", actor));
            // Ids, role and permissions only: no password data and no contact details.
            await Audit(cn, tx, actor, CreatedAction, membershipId, "{}", AuditSql.Json(("customerId", customerId), ("userId", userId), ("role", CustomerAccess.Member), ("permissions", x.Permissions), ("via", "CUSTOMER_PORTAL")), ip);
            return (MemberOutcome.Done, userId);
        }, error => (error, Guid.Empty));
    }

    public Task<MemberOutcome> SetPermissionsAsync(Guid customerId, Guid userId, string? permissions, Guid actor, string? ip) =>
        Change(customerId, userId, actor, ip, async (cn, tx, id, role, oldPermissions, active) =>
        {
            // Rights set here make the account a MEMBER (an ADMIN or VIEWER set by T.A included).
            await Run(cn, tx, "UPDATE dbo.CustomerPortalMemberships SET PortalRole='MEMBER',Permissions=@p,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id AND PortalRole<>'OWNER'",
                ("@p", permissions), ("@actor", actor), ("@id", id));
            return (PermissionsAction, AuditSql.Json(("role", role), ("permissions", oldPermissions)), AuditSql.Json(("role", CustomerAccess.Member), ("permissions", permissions)));
        });

    public Task<MemberOutcome> SetActiveAsync(Guid customerId, Guid userId, bool active, Guid actor, string? ip) =>
        Change(customerId, userId, actor, ip, async (cn, tx, id, role, permissions, wasActive) =>
        {
            await Run(cn, tx, "UPDATE dbo.CustomerPortalMemberships SET IsActive=@a,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@id AND PortalRole<>'OWNER'",
                ("@a", active), ("@actor", actor), ("@id", id));
            return (active ? ReactivatedAction : DeactivatedAction, AuditSql.Json(("isActive", wasActive)), AuditSql.Json(("isActive", active)));
        });

    public Task<MemberOutcome> SetPasswordAsync(Guid customerId, Guid userId, string passwordHash, Guid actor, string? ip) =>
        Change(customerId, userId, actor, ip, async (cn, tx, id, role, permissions, active) =>
        {
            // An account that also belongs to another company is not this company's to reset (T.A does it).
            if (await Scalar(cn, tx, "SELECT 1 FROM dbo.CustomerPortalMemberships WHERE UserId=@u AND CustomerId<>@c", ("@u", userId), ("@c", customerId)) is not null)
                return null;
            await Run(cn, tx, "UPDATE dbo.Users SET PasswordHash=@h,MustChangePassword=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE Id=@u AND IsDeleted=0",
                ("@h", passwordHash), ("@actor", actor), ("@u", userId));
            return (PasswordAction, "{}", AuditSql.Json(("userId", userId), ("mustChangePassword", true)));
        }, sharedOutcome: MemberOutcome.SharedAccount);

    private delegate Task<(string Action, string Old, string New)?> Mutation(SqlConnection cn, SqlTransaction tx, Guid membershipId, string role, string? permissions, bool active);

    // Locks the company's membership row, refuses the OWNER, applies the change and audits it in one transaction.
    private Task<MemberOutcome> Change(Guid customerId, Guid userId, Guid actor, string? ip, Mutation mutation, MemberOutcome sharedOutcome = MemberOutcome.Protected) =>
        InTransaction(async (cn, tx) =>
        {
            Guid id; string role; string? permissions; bool active;
            await using (var cmd = new SqlCommand("SELECT m.Id,m.PortalRole,m.Permissions,m.IsActive FROM dbo.CustomerPortalMemberships m WITH (UPDLOCK, HOLDLOCK) JOIN dbo.Users u ON u.Id=m.UserId WHERE m.CustomerId=@c AND m.UserId=@u AND u.IsDeleted=0", cn, tx))
            {
                cmd.Parameters.AddWithValue("@c", customerId);
                cmd.Parameters.AddWithValue("@u", userId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync()) return MemberOutcome.NotFound;
                (id, role, permissions, active) = (r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3));
            }
            if (role == CustomerAccess.Owner) return MemberOutcome.Protected;
            if (await mutation(cn, tx, id, role, permissions, active) is not { } audit) return sharedOutcome;
            await Audit(cn, tx, actor, audit.Action, id, audit.Old, audit.New, ip);
            return MemberOutcome.Done;
        }, error => error);

    private async Task<T> InTransaction<T>(Func<SqlConnection, SqlTransaction, Task<T>> work, Func<MemberOutcome, T> failed)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            var result = await work(cn, tx);
            if (IsDone(result)) await tx.CommitAsync(); else await tx.RollbackAsync();
            return result;
        }
        catch (AuditUnavailableException) { await tx.RollbackAsync(); return failed(MemberOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent creation won the race on a unique index of dbo.Users.
            await tx.RollbackAsync();
            return failed(ex.Message.Contains("Email", StringComparison.OrdinalIgnoreCase) ? MemberOutcome.EmailTaken : MemberOutcome.UsernameTaken);
        }
    }

    private static bool IsDone<T>(T result) => result switch
    {
        MemberOutcome o => o == MemberOutcome.Done,
        ValueTuple<MemberOutcome, Guid> t => t.Item1 == MemberOutcome.Done,
        _ => false
    };

    private Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string action, Guid membershipId, string oldValues, string newValues, string? ip) =>
        AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, action, Entity, membershipId.ToString(), oldValues, newValues, ip);

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object? Value)[] values)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task Run(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object? Value)[] values)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
