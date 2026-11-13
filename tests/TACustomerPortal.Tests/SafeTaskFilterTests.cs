extern alias admin;

using admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Operator 2026-11-13: the task list filter popup (several statuses, employees and customers, a date range on a chosen
// date) and the customer staff assignment (one manager, many executors) behind "customers of an employee".
public sealed class SafeTaskFilterTests
{
    private static readonly Guid E1 = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid E2 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid C1 = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void OldSingleValueLinksFoldIntoTheMultiValueFilter()
    {
        var f = new WorkTaskFilter { Status = "overdue", Owner = E1, Customer = C1, Statuses = ["DONE", "nonsense", "DONE"], Employees = [E2, Guid.Empty] };
        var (statuses, employees, customers, dateField) = f.Normalized();
        Assert.Equal(["DONE", "overdue"], statuses.Order());
        Assert.Equal([E2, E1], employees);
        Assert.Equal([C1], customers);
        Assert.Equal("due", dateField);
        Assert.Equal(3, f.ActiveCount());
    }

    [Fact]
    public void TheDateFieldIsOneOfThreeAndADateRangeCountsAsOneFilter()
    {
        Assert.Equal("start", new WorkTaskFilter { DateField = "start" }.Normalized().DateField);
        Assert.Equal("done", new WorkTaskFilter { DateField = "done" }.Normalized().DateField);
        Assert.Equal("due", new WorkTaskFilter { DateField = "x; DROP" }.Normalized().DateField);
        Assert.Equal(1, new WorkTaskFilter { From = new DateTime(2026, 11, 1), To = new DateTime(2026, 11, 30) }.ActiveCount());
        Assert.Equal(0, new WorkTaskFilter().ActiveCount());
    }

    [Fact]
    public void TheListQueryFiltersByListsAndTheChosenDate()
    {
        Assert.Contains("@statuses LIKE '%,'+t.Status+',%'", WorkTaskSql.List);
        Assert.Contains("fm.EmployeeId", WorkTaskSql.List);
        Assert.Contains("@customers LIKE", WorkTaskSql.List);
        Assert.Contains("CASE @dateField WHEN 'start' THEN t.StartDate WHEN 'done'", WorkTaskSql.List);
        Assert.DoesNotContain("@owner", WorkTaskSql.List);
    }

    [Fact]
    public void CustomersOfAnEmployeeComeFromAssignmentTasksAndContracts()
    {
        Assert.Contains("IF OBJECT_ID(N'dbo.CustomerStaff', N'U') IS NOT NULL", WorkTaskSql.Lookups);
        Assert.Contains("FROM dbo.CustomerStaff s", WorkTaskSql.Lookups);
        Assert.Contains("'TASK' FROM dbo.WorkTaskMembers m", WorkTaskSql.Lookups);
        Assert.Contains("'CONTRACT' FROM dbo.CustomerContracts k", WorkTaskSql.Lookups);
    }

    [Fact]
    public void TheManagerIsNeverAlsoAnExecutor()
    {
        Assert.Equal([E2], CustomerStaffPolicy.Executors(E1, [E1, E2, E2, Guid.Empty]));
        Assert.Equal([E1, E2], CustomerStaffPolicy.Executors(null, [E1, E2]));
    }

    [Fact]
    public void TheMigrationAllowsOneManagerPerCustomer()
    {
        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "041-customer-staff.sql"));
        Assert.Contains("CREATE UNIQUE INDEX UX_CustomerStaff_Manager ON dbo.CustomerStaff(CustomerId) WHERE Role = 'MANAGER'", sql);
        Assert.Contains("CHECK (Role IN ('MANAGER','EXECUTOR'))", sql);
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
    }
}
