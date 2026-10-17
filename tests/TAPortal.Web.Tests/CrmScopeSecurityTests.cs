using TAPortal.Web.Models;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261013-CRM-PHASE1-002 — pure security invariants; SQL integration remains intentionally separate.
public sealed class CrmScopeSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitScopeDenyWinsOverAllowAndSysAdmin(bool isSysAdmin)
    {
        var branch = Guid.NewGuid();
        var customer = new CrmCustomerScopeFacts(Guid.NewGuid(), branch, null,
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        CrmScopeGrant[] grants =
        [
            new(CrmScopeType.Branch, branch, CrmScopeEffect.Allow, "Customers.Index"),
            new(CrmScopeType.Branch, branch, CrmScopeEffect.Deny, "Customers.Index")
        ];
        Assert.False(CrmScopeResolver.IsVisible(customer, Employee, grants, "Customers.Index", isSysAdmin, true));
        Assert.True(CrmScopeResolver.IsVisible(customer, Employee, grants.Take(1), "Customers.Index", isSysAdmin, true));
    }

    [Fact]
    public void SysAdminStillRequiresEmploymentEligibilityButNotAnAllowGrant()
    {
        var customer = new CrmCustomerScopeFacts(Guid.NewGuid(), null, null,
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        Assert.True(CrmScopeResolver.IsVisible(customer, Employee, [], "Customers.Index", true, true));
        Assert.False(CrmScopeResolver.IsVisible(customer, Employee, [], "Customers.Index", true, false));
        Assert.False(CrmScopeResolver.IsVisible(customer, Employee, [], "Customers.Index", false, true));
    }

    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Employee = Guid.NewGuid();
    private static EmployeeAccessState State(string status = "ACTIVE") => new(Employee, true, false, status, Today.AddDays(-1), null);
    private static EmployeeOrganizationAssignment Assignment(DateOnly? to = null) => new(Guid.NewGuid(), Employee, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today.AddDays(-1), to, true, true, true);

    [Fact]
    public void DirectAssignmentRequiresCurrentEligibleEmployee()
    {
        Assert.True(EmployeeAssignmentRules.Resolve([State()], [Assignment()], Today).CanAccessCustomers);
        Assert.False(EmployeeAssignmentRules.Resolve([State("SUSPENDED")], [Assignment()], Today).CanAccessCustomers);
        Assert.Null(EmployeeAssignmentRules.Resolve([State()], [Assignment(Today.AddDays(-1))], Today).CurrentOrganization);
    }

    [Fact]
    public void TransferUsesCurrentOrganizationAndAmbiguityFailsClosed()
    {
        var old = Assignment(Today.AddDays(-1)); var next = Assignment();
        Assert.Equal(next, EmployeeAssignmentRules.Resolve([State()], [old, next], Today).CurrentOrganization);
        Assert.Null(EmployeeAssignmentRules.Resolve([State()], [next, next with { Id = Guid.NewGuid() }], Today).CurrentOrganization);
    }

    [Fact]
    public void RetiredAccountsDoNotUseEmployeeScope()
    {
        Assert.True(EmployeeAssignmentRules.Resolve([], [], Today).CanAccessCustomers);
        Assert.False(EmployeeAssignmentRules.Resolve([State("RESIGNED")], [Assignment()], Today).CanAccessCustomers);
    }
}