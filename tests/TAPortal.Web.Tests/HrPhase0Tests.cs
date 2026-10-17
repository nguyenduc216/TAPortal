using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using TAPortal.Web.Components.Pages;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001
public sealed class HrPhase0Tests
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Employee = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static EmployeeAccessState Employed => new(Employee, true, false, "ACTIVE", Today.AddYears(-1), null);
    private static EmployeeOrganizationAssignment Assignment(Guid branch, DateOnly from, DateOnly? to = null) =>
        new(Guid.NewGuid(), Employee, branch, Guid.NewGuid(), Guid.NewGuid(), from, to, true, true, true);

    [Theory]
    [InlineData("RESIGNED")]
    [InlineData("TERMINATED")]
    [InlineData("SUSPENDED")]
    [InlineData("UNKNOWN")]
    [InlineData(null)]
    public void IneligibleEmployment_DeniesCrmEvenWithCurrentAssignment(string? status)
    {
        var result = EmployeeAssignmentRules.Resolve([Employed with { StatusCode = status }],
            [Assignment(Guid.NewGuid(), Today.AddDays(-10))], Today);
        Assert.True(result.HasEmployeeLink);
        Assert.False(result.CanAccessCustomers);
        Assert.Null(result.CurrentOrganization);
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("PROBATION")]
    [InlineData("MATERNITY")]
    public void AllowedEmploymentStatuses_ResolveOrganization(string status)
    {
        var row = Assignment(Guid.NewGuid(), Today);
        Assert.Equal(row, EmployeeAssignmentRules.Resolve([Employed with { StatusCode = status }], [row], Today).CurrentOrganization);
    }

    [Fact]
    public void Transfer_RemovesOldOrganizationWithoutModifyingHistory()
    {
        var old = Assignment(Guid.NewGuid(), Today.AddMonths(-2), Today.AddDays(-1));
        var next = Assignment(Guid.NewGuid(), Today);
        var history = new[] { old, next };
        Assert.Equal(old, EmployeeAssignmentRules.Resolve([Employed], history, Today.AddDays(-1)).CurrentOrganization);
        Assert.Equal(next, EmployeeAssignmentRules.Resolve([Employed], history, Today).CurrentOrganization);
        Assert.Equal(old.ValidTo, history[0].ValidTo);
        Assert.Equal(2, history.Length);
    }

    [Fact]
    public void Overlap_NoAssignmentOrInvalidHierarchy_DoesNotGrantOrganizationScope()
    {
        var row = Assignment(Guid.NewGuid(), Today);
        Assert.Null(EmployeeAssignmentRules.Resolve([Employed], [row, row with { Id = Guid.NewGuid() }], Today).CurrentOrganization);
        Assert.Null(EmployeeAssignmentRules.Resolve([Employed], [row, row with { Id = Guid.NewGuid(), OrganizationIsValid = false }], Today).CurrentOrganization);
        foreach (var invalid in new[] { row with { OrganizationIsValid = false }, row with { IsCurrent = false },
            row with { IsPrimary = false }, row with { EmployeeId = Guid.NewGuid() }, row with { ValidFrom = Today.AddDays(1) } })
            Assert.Null(EmployeeAssignmentRules.Resolve([Employed], [invalid], Today).CurrentOrganization);
        Assert.Null(EmployeeAssignmentRules.Resolve([Employed], [], Today).CurrentOrganization);
    }

    [Fact]
    public void DisabledDeletedEndedOrFutureEmployee_DeniesCrm()
    {
        foreach (var state in new[] { Employed with { IsActive = false }, Employed with { IsDeleted = true },
            Employed with { EndDate = Today.AddDays(-1) }, Employed with { StartDate = Today.AddDays(1) } })
            Assert.False(EmployeeAssignmentRules.Resolve([state], [], Today).CanAccessCustomers);
        Assert.True(EmployeeAssignmentRules.IsEmployed(Employed with { EndDate = Today }, Today));
    }

    [Fact]
    public void NonEmployeeAccountsRemainCompatible_WhileAmbiguousEmployeeLinksFailClosed()
    {
        Assert.True(EmployeeAssignmentRules.Resolve([], [], Today).CanAccessCustomers);
        Assert.False(EmployeeAssignmentRules.Resolve([Employed, Employed with { Id = Guid.NewGuid() }], [], Today).CanAccessCustomers);
        Assert.True(EmployeeAssignmentRules.Resolve([Employed, Employed with { Id = Guid.NewGuid(), IsDeleted = true }], [], Today).CanAccessCustomers);
    }

    [Fact]
    public void DepartmentCreate_BindsChosenBranch_AndMissingBranchShowsValidation()
    {
        using var context = Context(out var service);
        var branch = new HrBranchOption(Guid.NewGuid(), "TEST", "Synthetic branch");
        service.HrSchema = new(true, true, false, [branch]);
        service.Permissions.Add("HR.DEPARTMENT.CREATE");
        var page = context.RenderComponent<HrDepartments>();
        page.Find("input[placeholder='Mã']").Change("OPS");
        page.Find("input[placeholder='Tên']").Change("Operations");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Vui lòng chọn chi nhánh", page.Markup));
        page.Find("select[aria-label='Chi nhánh phòng ban']").Change(branch.Id.ToString());
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Đã tạo phòng ban mới", page.Markup));
        Assert.Equal(branch.Id, service.LastDepartmentCreate!.BranchId);
    }

    [Fact]
    public void TeamForms_OmitDescriptionWhenSchemaDoesNotSupportIt()
    {
        using var context = Context(out var service);
        service.HrSchema = new(true, true, false, []);
        service.Permissions.UnionWith(["HR.TEAM.CREATE", "HR.TEAM.UPDATE"]);
        var department = Guid.NewGuid();
        service.Teams.Add(FakePortalService.Team(department, "TEST", "Synthetic team"));
        service.DepartmentsOptions.Add(new(department, "OPS", "Operations"));
        var page = context.RenderComponent<HrTeams>();
        Assert.Empty(page.FindAll("input[placeholder='Mô tả']"));
        page.Find(".js-edit-team").Click();
        Assert.Empty(page.FindAll("input[placeholder='Mô tả']"));
    }

    private static TestContext Context(out FakePortalService service)
    {
        var context = new TestContext();
        service = new();
        context.Services.AddSingleton<IBlazorPortalService>(service);
        context.Services.AddSingleton(new GlobalLoadingService());
        var auth = context.AddTestAuthorization();
        auth.SetAuthorized("phase0");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, FakePortalService.UserId.ToString()));
        return context;
    }
}
