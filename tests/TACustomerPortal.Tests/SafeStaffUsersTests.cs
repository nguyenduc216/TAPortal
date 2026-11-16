extern alias admin;

using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Operator 2026-11-14: Quản lý người dùng lists T.A staff accounts only; customer accounts belong to their customer.
public sealed class SafeStaffUsersTests
{
    [Fact]
    public void TheUserListHidesCustomerOnlyAccounts()
    {
        var sql = S.StaffAccountSql.StaffUsers;
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dbo.CustomerPortalMemberships sm WHERE sm.UserId=u.Id)", sql);
        // Staff who also own a customer stay listed: a role, a direct permission or an employee record makes it staff.
        Assert.Contains("OR EXISTS(SELECT 1 FROM dbo.UserRoles sr WHERE sr.UserId=u.Id)", sql);
        Assert.Contains("OR EXISTS(SELECT 1 FROM dbo.UserPermissions sp WHERE sp.UserId=u.Id)", sql);
        Assert.Contains("OR EXISTS(SELECT 1 FROM dbo.Employees se WHERE se.UserId=u.Id AND se.IsDeleted=0)", sql);
        Assert.Contains("WHERE u.IsDeleted=0 AND", sql);
    }

    [Fact]
    public void TheDashboardCountsTheSameStaffAccounts()
    {
        Assert.Contains(S.StaffAccountSql.IsStaff("u"), S.DashboardSql.Users);
        Assert.Contains("FROM dbo.Users u WHERE", S.DashboardSql.Users);
    }
}
