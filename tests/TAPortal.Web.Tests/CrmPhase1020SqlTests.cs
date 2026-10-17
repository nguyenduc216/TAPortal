using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-020
public sealed class CrmPhase1020SqlTests
{
    private static Guid Id(int n) => Guid.Parse($"01900000-0000-4000-8000-{n:000000000000}");

    private static (PortalDb db, string connectionString) OpenFixture()
    {
        var root = new DirectoryInfo(TestRepositoryRoot.Resolve());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "TAPortal.Web", "TAPortal.Web.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root!.FullName, "src", "TAPortal.Web", "appsettings.json")).Build();
        return (new PortalDb(config, NullLogger<PortalDb>.Instance), config["Database:ConnectionString"]!);
    }

    [Fact]
    public async Task ExistingSystemAdminHasEligibleHrAndRealCrmAccess()
    {
        if (Environment.GetEnvironmentVariable("TAPORTAL_CRM_020_SQL") != "1") return;
        var (db, connectionString) = OpenFixture();
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        Assert.Equal("TAPortalSys", cn.Database);
        var admin = Guid.Parse("3894fe22-4ccd-4dd5-8d84-c1c610022ae2");
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.Users u JOIN dbo.UserRoles ur ON ur.UserId=u.Id JOIN dbo.Roles r ON r.Id=ur.RoleId JOIN dbo.Employees e ON e.UserId=u.Id JOIN dbo.EmployeeStatuses s ON s.Id=e.CurrentStatusId JOIN dbo.EmployeeAssignments a ON a.EmployeeId=e.Id WHERE u.Id=@admin AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' AND r.Code='SYS_ADMIN' AND r.IsSystem=1 AND r.IsActive=1 AND r.IsDeleted=0 AND e.Id=@employee AND e.EmployeeCode='TA_DEV_CRM_020_ADMIN' AND e.IsActive=1 AND e.IsDeleted=0 AND s.Code='ACTIVE' AND a.Id=@assignment AND a.IsCurrent=1 AND a.IsPrimary=1", cn);
        cmd.Parameters.AddWithValue("@admin", admin);
        cmd.Parameters.AddWithValue("@employee", Guid.Parse("02000000-0000-4000-8000-000000000021"));
        cmd.Parameters.AddWithValue("@assignment", Guid.Parse("02000000-0000-4000-8000-000000000031"));
        Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        Assert.NotNull(await db.GetCrmRelationshipDetailAsync(Id(41), admin));
        Assert.Contains(await db.GetCustomersAsync(admin), c => c.Id == Id(41));
    }

    [Fact]
    public async Task FixturePasswordInitializationUsesApplicationPasswordService()
    {
        if (Environment.GetEnvironmentVariable("TAPORTAL_CRM_020_INIT_PASSWORD") != "1") return;
        var password = Environment.GetEnvironmentVariable("TAPORTAL_CRM_020_PASSWORD");
        Assert.True(PasswordPolicy.Validate(password).IsValid, "Set TAPORTAL_CRM_020_PASSWORD outside the repository to a password satisfying the application policy.");
        var (db, connectionString) = OpenFixture();
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        Assert.Equal("TAPortalSys", cn.Database);
        await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.Users u JOIN dbo.Employees e ON e.UserId=u.Id JOIN dbo.EmployeeAssignments a ON a.EmployeeId=e.Id WHERE u.Id=@id AND u.Username='TA_DEV_CRM_019_U1' AND e.Id=@employee AND e.EmployeeCode='TA_DEV_CRM_019_E1' AND a.Id=@assignment AND a.IsCurrent=1 AND a.IsPrimary=1 AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE'", cn);
        check.Parameters.AddWithValue("@id", Id(11)); check.Parameters.AddWithValue("@employee", Id(21)); check.Parameters.AddWithValue("@assignment", Id(31));
        Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));
        Assert.True(await db.UpdatePasswordAsync(Id(11), new PasswordService().Hash(password!)));
        var auth = await db.GetAuthUserAsync("TA_DEV_CRM_019_U1");
        Assert.NotNull(auth);
        Assert.Equal(Id(11), auth.Id);
        Assert.True(new PasswordService().Verify(password!, auth.PasswordHash));
    }

    [Fact]
    public async Task UnprivilegedAndInvalidTargetsAreDeniedByRealService()
    {
        if (Environment.GetEnvironmentVariable("TAPORTAL_CRM_020_SQL") != "1") return;
        var (db, connectionString) = OpenFixture();
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        Assert.Equal("TAPortalSys", cn.Database);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Assert.Empty(await db.GetCustomersAsync(Id(12)));
        Assert.Null(await db.GetCrmRelationshipDetailAsync(Id(41), Id(12)));
        Assert.Equal(CrmRelationWriteResult.Forbidden, await db.CreateCustomerBranchShareAsync(new(Id(41), Id(3), today, null), Id(12)));
        Assert.Equal(CrmRelationWriteResult.Forbidden, await db.CreateCustomerServiceTeamAsync(new(Id(41), Id(5), today, null), Id(13)));
        Assert.Equal(CrmRelationWriteResult.Forbidden, await db.CreateCustomerEmployeeAssignmentAsync(new(Id(41), Id(21), today, null, "OWNER"), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerEmployeeAssignmentAsync(new(Id(41), Guid.Empty, today, null, "OWNER"), Id(11)));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerBranchShareAsync(new(Id(41), Guid.Empty, today, null), Id(11)));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerServiceTeamAsync(new(Id(41), Guid.Empty, today, null), Id(11)));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerEmployeeAssignmentAsync(new(Id(41), Guid.NewGuid(), today, null, "OWNER"), Id(11)));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerBranchShareAsync(new(Id(41), Guid.NewGuid(), today, null), Id(11)));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerServiceTeamAsync(new(Id(41), Guid.NewGuid(), today, null), Id(11)));
        Assert.Equal(CrmRelationWriteResult.NotFound, await db.RevokeCustomerBranchShareAsync(new(Guid.NewGuid(), new byte[8]), Id(11)));
    }
}