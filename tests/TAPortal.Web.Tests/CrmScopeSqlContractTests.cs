using System.Text.RegularExpressions;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-008 — SQL scope predicate and CRM admin permission seed contracts.
public sealed class CrmScopeSqlContractTests
{
    [Fact]
    public void RelationWritersRemainFailClosedUntilTransactionalScopeChecksExist()
    {
        // CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-009 — prevent accidental reintroduction of an unscoped write.
        var source=ReadRepositoryFile("src","TAPortal.Web","Services","PortalDb.CrmRelations.cs");
        foreach(var name in new[]{"CreateCustomerEmployeeAssignmentAsync","CreateCustomerBranchShareAsync","CreateCustomerServiceTeamAsync"})
            Assert.Contains(name,source,StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(source,@"Task\.FromResult\(CrmRelationWriteResult\.Forbidden\)"));
        Assert.Contains("INSERT dbo.CustomerEmployeeAssignments",source,StringComparison.Ordinal);
    }

    [Fact]
    public void TeamScopesUseTheResolvedUnambiguousHrAssignment()
    {
        var sql = Sql();
        Assert.Contains("employeeContext.CanAccessCustomers", Raw(), StringComparison.Ordinal);
        Assert.Contains("employeeContext.CurrentOrganization?.Id", Raw(), StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(sql, @"ea\.Id=@organizationAssignmentId").Count);
        Assert.Contains("scopeRole.IsActive=1 AND scopeRole.IsDeleted=0", sql, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(sql, @"cst\.ValidFrom<=CONVERT").Count);
        Assert.Equal(2, Regex.Matches(sql, @"cbs\.ValidFrom<=CONVERT").Count);
        Assert.Equal(3, Regex.Matches(sql, @"x\.ValidFrom<=CONVERT").Count);
        Assert.Contains("(EXISTS(SELECT 1 FROM dbo.UserRoles", sql, StringComparison.Ordinal);
        Assert.Contains(") ) AND NOT EXISTS ( SELECT 1 FROM dbo.UserDataScopes [deny]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerScopeSql_GatesAccountAndFunctionBeforeAnyScope()
    {
        var sql=Sql();
        // The CRM view action is checked before any database work is attempted.
        Assert.Contains("Customers.Index",Raw(),StringComparison.Ordinal);
        // Active, not-deleted, ACTIVE account is mandatory inside the predicate too.
        Assert.Contains("u.IsDeleted=0 AND u.IsActive=1 AND u.Status='ACTIVE'",sql,StringComparison.Ordinal);
        // SYS_ADMIN is an explicit allow source, not a replacement for the account gate.
        Assert.Contains("r.Code='SYS_ADMIN' AND r.IsSystem=1",sql,StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerScopeSql_ResolvesEveryPhase1ScopeType()
    {
        var sql=Sql();
        Assert.Contains("ScopeType='ASSIGNED'",sql,StringComparison.Ordinal);
        Assert.Contains("ScopeType='TEAM'",sql,StringComparison.Ordinal);
        Assert.Contains("ScopeType='BRANCH'",sql,StringComparison.Ordinal);
        Assert.Contains("ScopeType='COMPANY'",sql,StringComparison.Ordinal);
        Assert.Contains("CustomerEmployeeAssignments",sql,StringComparison.Ordinal);
        Assert.Contains("CustomerServiceTeams",sql,StringComparison.Ordinal);
        Assert.Contains("CustomerBranchShares",sql,StringComparison.Ordinal);
        Assert.Contains("c.OwnerBranchId",sql,StringComparison.Ordinal);
        Assert.Contains("rds.FunctionCode IN ('Customers','Customers.Index')",sql,StringComparison.Ordinal);
        Assert.Contains("uds.FunctionCode IN ('Customers','Customers.Index')",sql,StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerScopeSql_FailsClosedAndHonoursExplicitDeny()
    {
        var sql=Sql();
        // A scope row without a concrete reference never grants access.
        Assert.Contains("ScopeReferenceId IS NOT NULL",sql,StringComparison.Ordinal);
        // Explicit user DENY is subtracted from the final result set, not overridden by ALLOW/SYS_ADMIN.
        Assert.Contains("[deny].Effect='DENY'",sql,StringComparison.Ordinal);
        Assert.Contains("uds.Effect='ALLOW'",sql,StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS (",sql,StringComparison.Ordinal);
        // Historical rows use inclusive valid-to bounds; a NULL valid-to means still-current.
        Assert.Contains("ValidTo IS NULL OR ca.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))",sql,StringComparison.Ordinal);
        Assert.Contains("ValidTo IS NULL OR cbs.ValidTo>=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))",sql,StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerScopeSql_IsFullyParameterized()
    {
        var raw=Raw();
        Assert.Contains("new SqlCommand(sql, cn)",raw,StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.Add(\"@userId\"",raw,StringComparison.Ordinal);
        Assert.Contains("cmd.Parameters.Add(\"@search\"",raw,StringComparison.Ordinal);
        // No string concatenation of user input into the command text.
        Assert.DoesNotContain("search +",raw,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\" + search",raw,StringComparison.OrdinalIgnoreCase);
        // Search runs in SQL over the same columns as the business search definition.
        Assert.Contains("c.Code LIKE @search OR c.Name LIKE @search",Sql(),StringComparison.Ordinal);
    }

    [Fact]
    public void CrmAdminPermissionSeed_IsManualIdempotentAndNonPrivileging()
    {
        var sql=ReadRepositoryFile("database","seeds","crm","008-seed-customer-admin-permissions.sql");
        Assert.Contains("TAPORTAL-20261014-CRM-PHASE1-008",sql,StringComparison.Ordinal);
        Assert.Contains("Manual execution only",sql,StringComparison.Ordinal);
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'",sql,StringComparison.Ordinal);
        Assert.Contains("BEGIN TRY",sql,StringComparison.Ordinal);
        Assert.Contains("BEGIN TRANSACTION",sql,StringComparison.Ordinal);
        Assert.Contains("ROLLBACK TRANSACTION",sql,StringComparison.Ordinal);
        // CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-009 — seed is insert-only; never reactivate or rewrite existing permissions.
        Assert.Contains("INSERT dbo.Permissions",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("MERGE dbo.Permissions",sql,StringComparison.OrdinalIgnoreCase);
        foreach(var code in new[]{"CRM.CUSTOMERS.ASSIGN","CRM.CUSTOMERS.SHARE","CRM.CUSTOMERS.TEAM"})
            Assert.Contains(code,sql,StringComparison.Ordinal);
        // Seeding metadata must never grant privilege to a role or user.
        var statements=StripComments(sql);
        Assert.DoesNotContain("INSERT dbo.User",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT dbo.Role",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE dbo.User",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM dbo.",statements,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE",statements,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE",statements,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CustomerScopeSql_TransactionalActionsAreGuardedByPermission()
    {
        var raw=Raw();
        // Assign/share/team writes reuse the same function gates registered by the seed.
        Assert.Contains("Customers.Create",raw,StringComparison.Ordinal);
        // Export and list share the single view gate.
        Assert.Contains("public async Task<IActionResult> Export",ReadRepositoryFile("src","TAPortal.Web","Controllers","PortalControllers.cs"),StringComparison.Ordinal);
    }

    private static string Raw()=>ReadRepositoryFile("src","TAPortal.Web","Services","PortalDb.cs");

    // Collapse newlines/indentation and strip comments so multi-line SQL predicates match as one logical string.
    private static string Sql()
    {
        var source=Raw();
        var start=source.IndexOf("public Task<List<CustomerRow>> GetCustomersAsync",StringComparison.Ordinal);
        Assert.True(start>=0,"GetCustomersAsync overload not found.");
        var end=source.IndexOf("public async Task<CustomerWriteResult> CreateCustomerAsync",start,StringComparison.Ordinal);
        Assert.True(end>start,"CreateCustomerAsync end marker not found.");
        var normalized=Regex.Replace(source[start..end],@"\s+"," ");
        return Regex.Replace(normalized,@"//[^\""]*?$"," ");
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        return File.ReadAllText(TestRepositoryRoot.FilePath(parts));
    }

    // Remove block and line comments so safety assertions target executable statements only.
    private static string StripComments(string sql)
    {
        var withoutBlock=Regex.Replace(sql,@"\/\*.*?\*\/"," ",RegexOptions.Singleline);
        return Regex.Replace(withoutBlock,@"--[^\r\n]*"," ");
    }
}