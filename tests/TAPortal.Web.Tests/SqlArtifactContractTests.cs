using Xunit;

namespace TAPortal.Web.Tests;

public sealed class SqlArtifactContractTests
{
    [Fact]
    public void EffectivePermissionResolver_SysAdminIsAnExplicitAllowSource()
    {
        var source = ReadRepositoryFile("src", "TAPortal.Web", "Services", "PortalDb.cs");
        var start = source.IndexOf("public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<bool> HasFunctionActionAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var resolver = source[start..end];
        Assert.Contains("sr.Code='SYS_ADMIN'", resolver, StringComparison.Ordinal);
        Assert.Contains("sr.IsSystem=1", resolver, StringComparison.Ordinal);
        Assert.Contains("OR EXISTS (SELECT 1 FROM dbo.UserPermissions", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomersCreateMetadataMigration_IsFailClosedIdempotentAndNonPrivileging()
    {
        var sql = ReadRepositoryFile("database", "migrations", "20261012_CUSTOMERS-001_create-action-review.sql");
        Assert.Contains("CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-ACTION-METADATA-004", sql, StringComparison.Ordinal);
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql, StringComparison.Ordinal);
        Assert.Contains("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", sql, StringComparison.Ordinal);
        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.Ordinal);
        Assert.Contains("IF XACT_STATE()<>0 ROLLBACK", sql, StringComparison.Ordinal);
        Assert.Contains("CRM.CUSTOMERS.CREATE", sql, StringComparison.Ordinal);
        Assert.Contains("Customers.Create", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.FunctionActionPermissions", sql, StringComparison.Ordinal);
        Assert.Contains("No Users, UserRoles, UserPermissions, Roles, or RolePermissions rows are changed", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT dbo.User", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.Role", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.User", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomersCreateVerification_IsReadOnlyAndChecksActiveMapping()
    {
        var sql = ReadRepositoryFile("database", "scripts", "verify", "20261012_CUSTOMERS-001_verify.sql");
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Customers.Create", sql, StringComparison.Ordinal);
        Assert.Contains("CRM.CUSTOMERS.CREATE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_GuardsTAPortalSysBeforeMutation()
    {
        var sql = ReadRepositoryFile("database", "migrations", "053-core-dynamic-action-authorization.sql");
        var guard = sql.IndexOf("IF DB_NAME() <> N'TAPortalSys'", StringComparison.Ordinal);
        var transaction = sql.IndexOf("BEGIN TRANSACTION", StringComparison.Ordinal);
        var create = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);

        Assert.True(guard>=0);
        Assert.True(transaction>guard);
        Assert.True(create>guard);
        Assert.Contains("THROW 52120, 'This migration must run against TAPortalSys.'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_IsTextOnlyAndHasFailingSummaryContract()
    {
        var sql = ReadRepositoryFile("database", "scripts", "verify", "053-verify-core-dynamic-action-authorization.sql");

        Assert.Contains("TAPortalSys AUTHORIZATION VERIFY SUMMARY", sql, StringComparison.Ordinal);
        Assert.Contains("OVERALL STATUS:", sql, StringComparison.Ordinal);
        Assert.Contains("RAISERROR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DepartmentMigration_ProtectsUniquenessConcurrencyAndSoftDeleteDependencies()
    {
        var sql = ReadRepositoryFile("database", "migrations", "055-hr-department-master.sql");
        Assert.Contains("IF DB_NAME()<>N'TAPortalSys'", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX UX_Departments_Code_NotDeleted", sql, StringComparison.Ordinal);
        Assert.Contains("CK_Departments_Code_Canonical", sql, StringComparison.Ordinal);
        Assert.Contains("RowVersion rowversion NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("HR.DEPARTMENT.DELETE", sql, StringComparison.Ordinal);
        Assert.Contains("SYS_ADMIN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DepartmentVerify_IsReadOnlyAndFailsOnInvariantErrors()
    {
        var sql = ReadRepositoryFile("database", "scripts", "verify", "055-verify-hr-department.sql");
        Assert.Contains("OVERALL STATUS:", sql, StringComparison.Ordinal);
        Assert.Contains("RAISERROR", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DepartmentWrites_UseRowVersionAndDependencyAwareSoftDelete()
    {
        var source = ReadRepositoryFile("src", "TAPortal.Web", "Services", "PortalDb.cs");
        Assert.Contains("WHERE Id=@id AND RowVersion=@rowVersion AND IsDeleted=0", source, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.Teams',N'DepartmentId')", source, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.EmployeeAssignments',N'DepartmentId')", source, StringComparison.Ordinal);
        Assert.Contains("SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dbo.Departments", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TeamMigration_ProtectsHierarchyAuthorizationAndUniqueness()
    {
        var sql=ReadRepositoryFile("database","migrations","056-hr-team-master.sql");
        Assert.Contains("IF DB_NAME()<>N'TAPortalSys'",sql,StringComparison.Ordinal);
        Assert.Contains("FK_Teams_Departments",sql,StringComparison.Ordinal);
        Assert.Contains("FK_Teams_ParentTeam",sql,StringComparison.Ordinal);
        Assert.Contains("CK_Teams_NotSelfParent",sql,StringComparison.Ordinal);
        Assert.Contains("UX_Teams_Code_NotDeleted",sql,StringComparison.Ordinal);
        Assert.Contains("Teams contains a hierarchy cycle",sql,StringComparison.Ordinal);
        Assert.Contains("HR.TEAM.DELETE",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TeamWrites_UseConcurrencyAndFutureSafeDependencies()
    {
        var source=ReadRepositoryFile("src","TAPortal.Web","Services","PortalDb.cs");
        Assert.Contains("ParentTeamId=@parentId",source,StringComparison.Ordinal);
        Assert.Contains("RowVersion=@rowVersion AND IsDeleted=0",source,StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'dbo.EmployeeAssignments',N'TeamId')",source,StringComparison.Ordinal);
        Assert.Contains("ParentTeamId=@id AND IsDeleted=0",source,StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM dbo.Teams",source,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TeamVerify_IsReadOnlyAndRaisesOnFailures()
    {
        var sql=ReadRepositoryFile("database","scripts","verify","056-verify-hr-team.sql");
        Assert.Contains("OVERALL STATUS:",sql,StringComparison.Ordinal);
        Assert.Contains("RAISERROR",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.",sql,StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TAPortal.sln")))
            directory=directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
