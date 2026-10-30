using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Mutually exclusive account buckets; the same rule as AccountStatusPolicy, so Active + Disabled + Locked = CurrentTotal.
public enum AccountBucket { Active, Disabled, Locked, Deleted }

public static class DashboardMetrics
{
    public static AccountBucket Classify(bool isActive, string? status, bool isDeleted) =>
        isDeleted ? AccountBucket.Deleted : !isActive ? AccountBucket.Disabled : status != AccountStatusPolicy.ActiveStatus ? AccountBucket.Locked : AccountBucket.Active;
}

public sealed record UserKpis(int Active, int Disabled, int Locked, int SoftDeleted)
{
    public int CurrentTotal => Active + Disabled + Locked;
}

public sealed record CustomerKpis(int Active, int Inactive)
{
    public int CurrentTotal => Active + Inactive;
}

// Null = table not present/visible (e.g. migration not applied); never shown as zero.
public sealed record MembershipKpis(int EffectiveMemberships, int UsersWithAccess, int RevokedOrIneffective);

public sealed record AuditEntry(DateTime CreatedAtUtc, string Action, string? ActorName);

public sealed record DashboardSnapshot(
    DateTimeOffset LoadedAtUtc, UserKpis Users, CustomerKpis Customers, MembershipKpis? Memberships, int ActiveRoles,
    bool AuditTableVisible, IReadOnlyList<AuditEntry>? RecentAudit);

public interface IDashboardStore
{
    Task<DashboardSnapshot> LoadAsync();
}

// Each KPI is one aggregate over a single table (or one row per membership), so JOINs cannot double count.
public static class DashboardSql
{
    public const string Users = """
SELECT
 SUM(CASE WHEN IsDeleted=0 AND IsActive=1 AND Status='ACTIVE' THEN 1 ELSE 0 END),
 SUM(CASE WHEN IsDeleted=0 AND IsActive=0 THEN 1 ELSE 0 END),
 SUM(CASE WHEN IsDeleted=0 AND IsActive=1 AND (Status IS NULL OR Status<>'ACTIVE') THEN 1 ELSE 0 END),
 SUM(CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END)
FROM dbo.Users;
""";
    public const string Customers = """
SELECT
 SUM(CASE WHEN IsActive=1 AND Status='ACTIVE' THEN 1 ELSE 0 END),
 SUM(CASE WHEN NOT (IsActive=1 AND Status='ACTIVE') THEN 1 ELSE 0 END)
FROM dbo.Customers WHERE IsDeleted=0;
""";
    public const string Roles = "SELECT COUNT(1) FROM dbo.Roles WHERE IsDeleted=0 AND IsActive=1;";
    // Effective = the same conditions the Customer Portal enforces at login (membership, user and customer).
    public const string Memberships = """
IF OBJECT_ID(N'dbo.CustomerPortalMemberships',N'U') IS NULL SELECT CAST(NULL AS int),CAST(NULL AS int),CAST(NULL AS int);
ELSE
SELECT
 SUM(CASE WHEN e.Effective=1 THEN 1 ELSE 0 END),
 COUNT(DISTINCT CASE WHEN e.Effective=1 THEN e.UserId END),
 SUM(CASE WHEN e.Effective=0 THEN 1 ELSE 0 END)
FROM (
 SELECT m.UserId,
  CASE WHEN m.IsActive=1 AND u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE' AND c.IsDeleted=0 AND c.IsActive=1 AND c.Status='ACTIVE' THEN 1 ELSE 0 END AS Effective
 FROM dbo.CustomerPortalMemberships m JOIN dbo.Users u ON u.Id=m.UserId JOIN dbo.Customers c ON c.Id=m.CustomerId
) e;
""";
    // Only action names and actor display names; old/new values are never loaded for the dashboard.
    public const string RecentAudit = """
IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL OR COL_LENGTH(N'dbo.AuditLogs',N'CreatedAt') IS NULL SELECT TOP 0 CAST(NULL AS datetime2),CAST(NULL AS nvarchar(100)),CAST(NULL AS nvarchar(200));
ELSE SELECT TOP 5 a.CreatedAt,a.Action,u.DisplayName FROM dbo.AuditLogs a LEFT JOIN dbo.Users u ON u.Id=a.UserId ORDER BY a.CreatedAt DESC;
""";
    public const string AuditVisible = "SELECT CASE WHEN OBJECT_ID(N'dbo.AuditLogs',N'U') IS NULL THEN 0 ELSE 1 END;";
}

public sealed class SqlDashboardStore(IConfiguration configuration, TimeProvider time, ILogger<SqlDashboardStore> logger) : IDashboardStore
{
    public async Task<DashboardSnapshot> LoadAsync()
    {
        await using var cn = new SqlConnection(configuration["Database:ConnectionString"]);
        await cn.OpenAsync();
        int[] Ints(SqlDataReader r, int n) => Enumerable.Range(0, n).Select(i => r.IsDBNull(i) ? 0 : r.GetInt32(i)).ToArray();

        int[] users, customers;
        await using (var cmd = new SqlCommand(DashboardSql.Users, cn))
        await using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); users = Ints(r, 4); }
        await using (var cmd = new SqlCommand(DashboardSql.Customers, cn))
        await using (var r = await cmd.ExecuteReaderAsync()) { await r.ReadAsync(); customers = Ints(r, 2); }
        var roles = await Scalar(cn, DashboardSql.Roles);

        MembershipKpis? memberships = null;
        await using (var cmd = new SqlCommand(DashboardSql.Memberships, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync() && !r.IsDBNull(1))
                memberships = new(r.IsDBNull(0) ? 0 : r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt32(2));

        var auditVisible = await Scalar(cn, DashboardSql.AuditVisible) == 1;
        IReadOnlyList<AuditEntry>? recent = null;
        if (auditVisible)
        {
            try
            {
                var list = new List<AuditEntry>();
                await using var cmd = new SqlCommand(DashboardSql.RecentAudit, cn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) list.Add(new(r.GetDateTime(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
                recent = list;
            }
            catch (SqlException ex)
            {
                // Typically no SELECT on AuditLogs (least privilege: INSERT only) or a schema that differs; the panel says so.
                logger.LogInformation("Dashboard cannot read dbo.AuditLogs (SQL error {Number}).", ex.Number);
            }
        }

        return new(time.GetUtcNow(), new(users[0], users[1], users[2], users[3]), new(customers[0], customers[1]), memberships, roles, auditVisible, recent);
    }

    private static async Task<int> Scalar(SqlConnection cn, string sql)
    {
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
