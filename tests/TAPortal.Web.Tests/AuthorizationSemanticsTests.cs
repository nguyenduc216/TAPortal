using TAPortal.Web.Security;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class AuthorizationSemanticsTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, "ALLOW", true)]
    [InlineData(true, "ALLOW", true)]
    [InlineData(true, "DENY", false)]
    [InlineData(false, "DENY", false)]
    [InlineData(false, null, false)]
    public void EffectivePermission_UsesDenyThenUserAllowThenRoleAllow(bool roleAllowed, string? userEffect, bool expected) =>
        Assert.Equal(expected, AuthorizationSemantics.IsPermissionEffective(roleAllowed, userEffect));

    [Fact]
    public void FunctionAction_UnknownMappingFailsClosed()
    {
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AUTH.USERS.VIEW" };

        Assert.False(AuthorizationSemantics.HasAnyMappedPermission(effective, Array.Empty<string>(), false, false));
    }

    [Fact]
    public void FunctionAction_MappedEffectivePermissionGrantsAccess()
    {
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AUTH.USERS.VIEW" };

        Assert.True(AuthorizationSemantics.HasAnyMappedPermission(effective, new[] { "AUTH.USERS.VIEW" }, true, false));
    }

    [Fact]
    public void MenuFiltering_AllowsUnmappedAndRejectsUnauthorizedMappedMenu()
    {
        var effective = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CRM.CUSTOMERS.VIEW" };

        Assert.True(AuthorizationSemantics.HasAnyMappedPermission(effective, Array.Empty<string>(), false, true));
        Assert.True(AuthorizationSemantics.HasAnyMappedPermission(effective, new[] { "CRM.CUSTOMERS.VIEW" }, true, true));
        Assert.False(AuthorizationSemantics.HasAnyMappedPermission(effective, new[] { "AUTH.USERS.VIEW" }, true, true));
    }

    [Fact]
    public void DepartmentAction_RequiresMappedDepartmentPermission()
    {
        var unrelated = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HR.DEPARTMENT.VIEW" };
        Assert.False(AuthorizationSemantics.HasAnyMappedPermission(unrelated, new[] { "HR.DEPARTMENT.DELETE" }, true, false));
    }
}
