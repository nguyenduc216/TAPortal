using System.Text.RegularExpressions;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-011C
public sealed class CrmTransactionAuthorizationTests
{
    [Fact]
    public void MutationAuthorizationUsesCallerTransactionAndNeverOpensAnotherConnection()
    {
        var source = Read("src", "TAPortal.Web", "Services", "PortalDb.CrmAuthorization.cs");
        Assert.Contains("SqlTransaction transaction", source);
        Assert.Contains("new SqlCommand(sql, connection, transaction)", source);
        Assert.Contains("transaction.IsolationLevel != IsolationLevel.Serializable", source);
        Assert.DoesNotContain("Open()", source);
        Assert.DoesNotContain("BeginTransaction", source);
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", source);
        Assert.Contains("Customers.Assign", source);
        Assert.Contains("Customers.Share", source);
        Assert.Contains("Customers.Team", source);
        Assert.Contains("(fa.UiKey=@actionKey OR fa.EndpointKey=@actionKey)", source);
    }

    [Fact]
    public void MutationRequiresPermissionAndMatchingCustomerScope()
    {
        var source = Read("src", "TAPortal.Web", "Services", "PortalDb.CrmAuthorization.cs");
        foreach (var relation in new[] { "CustomerEmployeeAssignments", "CustomerServiceTeams", "CustomerBranchShares" })
            Assert.Contains(relation, source);
        foreach (var scope in new[] { "ASSIGNED", "TEAM", "BRANCH", "COMPANY" })
            Assert.Contains($"ScopeType='{scope}'", source);
        Assert.Contains("[deny].Effect='DENY'", source);
        Assert.Contains("up.Effect='DENY'", source);
        Assert.Contains("uds.FunctionCode='Customers'", source);
        Assert.Contains("rds.FunctionCode='Customers'", source);
        Assert.Contains("ScopeReferenceId IS NOT NULL", source);
        Assert.Contains("c.IsDeleted=0 AND c.IsActive=1", source);
        Assert.Contains("es.Code IN ('ACTIVE','PROBATION','MATERNITY')", source);
        Assert.Contains("ea.IsCurrent=1 AND ea.IsPrimary=1", source);
        Assert.DoesNotContain("MAX(valid.Id)", source);
        Assert.Contains("ea.TeamId=st.TeamId", source);
        Assert.DoesNotContain("FunctionCode IN ('Customers','Customers.Index')", source);
    }

    [Fact]
    public void WritersUseMutationAuthorizationAndViewSqlIsUnchanged()
    {
        var writers = Read("src", "TAPortal.Web", "Services", "PortalDb.CrmRelations.cs");
        Assert.Empty(Regex.Matches(writers, @"Task\.FromResult\(CrmRelationWriteResult\.Forbidden\)"));
        Assert.Contains("AuthorizeCustomerMutationAsync(cn, tx, actorId, request.CustomerId, CustomerMutationAction.Assign)", writers);
        var view = Read("src", "TAPortal.Web", "Services", "PortalDb.cs");
        Assert.Contains("uds.FunctionCode IN ('Customers','Customers.Index')", view);
    }

    private static string Read(params string[] parts)
    {
        return File.ReadAllText(TestRepositoryRoot.FilePath(parts));
    }
}