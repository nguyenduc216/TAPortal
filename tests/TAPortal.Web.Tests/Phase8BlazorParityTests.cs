using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using TAPortal.Web.Components.Pages;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008
public sealed class Phase8BlazorParityTests : TestContext
{
    private readonly FakePortalService _db = new();
    private readonly GlobalLoadingService _loading = new();

    public Phase8BlazorParityTests()
    {
        Services.AddSingleton<IBlazorPortalService>(_db);
        Services.AddSingleton(_loading);
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("phase8");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, FakePortalService.UserId.ToString()));
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify real Department edit binding and successful service submission.
    [Fact]
    public void Departments_EditSuccess_BindsServerIdentityAndReloads()
    {
        _db.Permissions.UnionWith(["HR.DEPARTMENT.CREATE", "HR.DEPARTMENT.UPDATE"]);
        _db.Departments.Add(FakePortalService.Department("OPS", "Operations"));
        var cut = RenderComponent<HrDepartments>();
        cut.WaitForAssertion(() => Assert.Contains("Operations", cut.Markup));
        cut.Find(".js-edit-department").Click();
        cut.Find("[aria-label='Tên phòng ban sửa']").Change("Operations 2");
        cut.Find("[data-testid='department-edit'] form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Đã cập nhật phòng ban.", cut.Markup));
        Assert.Equal(1, _db.DepartmentUpdates);
        Assert.Equal("Operations 2", _db.LastDepartmentUpdate!.Name);
        Assert.Equal(_db.Departments[0].Id, _db.LastDepartmentUpdate.Id);
        Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify conflict remains visible and edit data is retained.
    [Fact]
    public void Departments_EditConflict_StaysInEditMode()
    {
        _db.Permissions.Add("HR.DEPARTMENT.UPDATE");
        _db.DepartmentUpdateResult = DepartmentWriteResult.ConcurrencyConflict;
        _db.Departments.Add(FakePortalService.Department("OPS", "Operations"));
        var cut = RenderComponent<HrDepartments>();
        cut.WaitForAssertion(() => cut.Find(".js-edit-department"));
        cut.Find(".js-edit-department").Click();
        cut.Find("[data-testid='department-edit'] form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Dữ liệu đã thay đổi", cut.Markup));
        Assert.NotNull(cut.Find("[data-testid='department-edit']"));
        Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify authorization is checked again when the Department write is submitted.
    [Fact]
    public void Departments_EditPermissionRevoked_BlocksWrite()
    {
        _db.Permissions.Add("HR.DEPARTMENT.UPDATE");
        _db.Departments.Add(FakePortalService.Department("OPS", "Operations"));
        var cut = RenderComponent<HrDepartments>();
        cut.WaitForAssertion(() => cut.Find(".js-edit-department"));
        cut.Find(".js-edit-department").Click();
        _db.DeniedActions.Add("Departments.Update");
        cut.Find("[data-testid='department-edit'] form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("không có quyền cập nhật", cut.Markup));
        Assert.Equal(0, _db.DepartmentUpdates);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify DataAnnotations prevents an invalid Department edit from reaching persistence.
    [Fact]
    public void Departments_EditValidation_BlocksEmptyName()
    {
        _db.Permissions.Add("HR.DEPARTMENT.UPDATE"); _db.Departments.Add(FakePortalService.Department("OPS", "Operations"));
        var cut=RenderComponent<HrDepartments>(); cut.WaitForAssertion(()=>cut.Find(".js-edit-department")); cut.Find(".js-edit-department").Click();
        cut.Find("[aria-label='Tên phòng ban sửa']").Change(""); cut.Find("[data-testid='department-edit'] form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Vui lòng nhập tên phòng ban",cut.Markup));
        Assert.Equal(0,_db.DepartmentUpdates);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify database exceptions remain generic and loading is cleaned up.
    [Fact]
    public void Departments_EditDatabaseError_IsSanitized()
    {
        _db.Permissions.Add("HR.DEPARTMENT.UPDATE"); _db.ThrowDepartmentUpdate=true; _db.Departments.Add(FakePortalService.Department("OPS","Operations"));
        var cut=RenderComponent<HrDepartments>(); cut.WaitForAssertion(()=>cut.Find(".js-edit-department")); cut.Find(".js-edit-department").Click(); cut.Find("[data-testid='department-edit'] form").Submit();
        cut.WaitForAssertion(()=>Assert.Contains("Không thể cập nhật phòng ban lúc này",cut.Markup));
        Assert.DoesNotContain("sensitive SQL",cut.Markup); Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify Team parent choices are scoped to the selected Department and exclude self.
    [Fact]
    public void Teams_Edit_ParentOptionsAreDepartmentScopedAndExcludeSelf()
    {
        _db.Permissions.Add("HR.TEAM.UPDATE");
        var dep = Guid.NewGuid(); var foreign = Guid.NewGuid(); var team = FakePortalService.Team(dep, "DEV", "Developers");
        _db.Teams.Add(team);
        _db.DepartmentsOptions.Add(new(dep, "IT", "IT"));
        _db.ParentOptions.Add(new(team.Id, dep, "DEV", "Developers", null));
        _db.ParentOptions.Add(new(Guid.NewGuid(), dep, "PLAT", "Platform", null));
        _db.ParentOptions.Add(new(Guid.NewGuid(), foreign, "FOREIGN", "Foreign", null));
        var cut = RenderComponent<HrTeams>();
        cut.WaitForAssertion(() => cut.Find(".js-edit-team"));
        cut.Find(".js-edit-team").Click();
        var edit = cut.Find("[data-testid='team-edit']");
        Assert.Contains("PLAT", edit.TextContent);
        Assert.DoesNotContain("FOREIGN", edit.TextContent);
        Assert.DoesNotContain(">DEV – Developers<", edit.InnerHtml);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify the existing client-side Department list filter still changes rendered rows.
    [Fact]
    public void Teams_DepartmentFilter_FiltersListing()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();_db.Teams.Add(FakePortalService.Team(first,"ONE","One"));_db.Teams.Add(FakePortalService.Team(second,"TWO","Two"));
        _db.DepartmentsOptions.Add(new(first,"D1","One"));_db.DepartmentsOptions.Add(new(second,"D2","Two"));
        var cut=RenderComponent<HrTeams>();cut.WaitForAssertion(()=>Assert.Contains("TWO",cut.Markup));
        cut.Find(".ta-toolbar select").Change(first.ToString());
        cut.WaitForAssertion(()=>Assert.DoesNotContain("<code>TWO</code>",cut.Markup));
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify typed hierarchy failures are presented by the real Team component.
    [Theory]
    [InlineData(TeamWriteResult.InvalidParent, "cùng phòng ban")]
    [InlineData(TeamWriteResult.SelfParent, "chính nó")]
    [InlineData(TeamWriteResult.Cycle, "vòng lặp")]
    [InlineData(TeamWriteResult.ConcurrencyConflict, "Dữ liệu đã thay đổi")]
    public void Teams_EditFailure_ShowsSpecificMessage(TeamWriteResult result, string message)
    {
        _db.Permissions.Add("HR.TEAM.UPDATE"); _db.TeamUpdateResult = result;
        var dep = Guid.NewGuid(); _db.Teams.Add(FakePortalService.Team(dep, "DEV", "Developers")); _db.DepartmentsOptions.Add(new(dep, "IT", "IT"));
        var cut = RenderComponent<HrTeams>();
        cut.WaitForAssertion(() => cut.Find(".js-edit-team")); cut.Find(".js-edit-team").Click();
        cut.Find("[data-testid='team-edit'] form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(message, cut.Markup));
        Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify role load errors are visible, retryable and clean loading state.
    [Fact]
    public void Roles_ErrorRetry_RecoversToSuccess()
    {
        _db.RoleFailuresRemaining = 1;
        _db.Roles.Add(new(Guid.NewGuid(), "ADMIN", "Administrator", null, true, true));
        var cut = RenderComponent<Roles>();
        cut.WaitForAssertion(() => Assert.Contains("Không thể tải vai trò", cut.Markup));
        Assert.False(_loading.IsBusy);
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Contains("Administrator", cut.Markup));
        Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify menu load errors are visible, retryable and support the empty state.
    [Fact]
    public void Menus_ErrorRetry_RecoversToEmpty()
    {
        _db.MenuFailuresRemaining = 1;
        var cut = RenderComponent<Menus>();
        cut.WaitForAssertion(() => Assert.Contains("Không thể tải menu", cut.Markup));
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Contains("Không có menu khả dụng", cut.Markup));
        Assert.False(_loading.IsBusy);
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — verify authorization denial is distinct from an ordinary data-load error.
    [Fact]
    public void Roles_PermissionDenied_DoesNotRenderRetryError()
    {
        _db.DeniedActions.Add("Roles.Index");
        var cut=RenderComponent<Roles>();
        cut.WaitForAssertion(()=>Assert.Contains("không có quyền truy cập",cut.Markup));
        Assert.DoesNotContain("Thử lại",cut.Markup); Assert.DoesNotContain("Không thể tải",cut.Markup);
    }
}

// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008 — deterministic in-memory test double prevents component tests from contacting SQL.
internal sealed class FakePortalService : IBlazorPortalService
{
    internal CrmRelationshipDetail? RelationshipDetail { get; set; }
    internal CrmRelationWriteResult RelationResult { get; set; } = CrmRelationWriteResult.Success;
    internal int RelationWrites { get; private set; }
    internal TaskCompletionSource<CrmRelationWriteResult>? RelationPending { get; set; }
    public Task<CrmRelationshipDetail?> GetCrmRelationshipDetailAsync(Guid customerId, Guid actorId) =>
        Task.FromResult(RelationshipDetail?.Customer.Id == customerId && actorId == UserId ? RelationshipDetail : null);
    private async Task<CrmRelationWriteResult> WriteRelationAsync() { RelationWrites++; return RelationPending is null ? RelationResult : await RelationPending.Task; }
    public Task<CrmRelationWriteResult> CreateCustomerEmployeeAssignmentAsync(CrmRelationWriteRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> CreateCustomerBranchShareAsync(CrmRelationWriteRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> CreateCustomerServiceTeamAsync(CrmRelationWriteRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> ChangeCustomerEmployeeAssignmentAsync(CrmRelationChangeRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> ChangeCustomerBranchShareAsync(CrmRelationChangeRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> ChangeCustomerServiceTeamAsync(CrmRelationChangeRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> RevokeCustomerEmployeeAssignmentAsync(CrmRelationRevokeRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> RevokeCustomerBranchShareAsync(CrmRelationRevokeRequest request, Guid actorId) => WriteRelationAsync();
    public Task<CrmRelationWriteResult> RevokeCustomerServiceTeamAsync(CrmRelationRevokeRequest request, Guid actorId) => WriteRelationAsync();
    // CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001 — schema variants stay isolated from SQL in component tests.
    internal HrSchemaOptions HrSchema { get; set; } = new(false, false, true, []);
    internal DepartmentCreateVm? LastDepartmentCreate { get; private set; }
    public Task<HrSchemaOptions> GetHrSchemaOptionsAsync() => Task.FromResult(HrSchema);
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — isolated customer writes never contact production SQL.
    internal List<CustomerRow> Customers { get; } = [];
    internal int CustomerWrites { get; private set; }
    internal bool CustomerReadFails { get; set; }
    internal TaskCompletionSource<CustomerWriteResult>? CustomerSavePending { get; set; }
    internal CustomerWriteResult CustomerResult { get; set; } = CustomerWriteResult.Success;
    public Task<List<CustomerRow>> GetCustomersAsync(Guid userId) => CustomerReadFails ? throw new InvalidOperationException("private database detail") : Task.FromResult(Customers.ToList());
    public async Task<CustomerWriteResult> CreateCustomerAsync(CustomerCreateVm vm, Guid userId)
    {
        CustomerWrites++;
        var result = CustomerSavePending is null ? CustomerResult : await CustomerSavePending.Task;
        if (result == CustomerWriteResult.Success) Customers.Add(new(Guid.NewGuid(), vm.Code, vm.Name, vm.TaxCode, vm.Email, vm.Phone, vm.Status));
        return result;
    }
    internal static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal HashSet<string> Permissions { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> DeniedActions { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<DepartmentRow> Departments { get; } = [];
    internal List<TeamRow> Teams { get; } = [];
    internal List<TeamDepartmentOption> DepartmentsOptions { get; } = [];
    internal List<TeamParentOption> ParentOptions { get; } = [];
    internal List<PortalRole> Roles { get; } = [];
    internal int DepartmentUpdates { get; private set; }
    internal DepartmentUpdateVm? LastDepartmentUpdate { get; private set; }
    internal DepartmentWriteResult DepartmentUpdateResult { get; set; } = DepartmentWriteResult.Success;
    internal bool ThrowDepartmentUpdate { get; set; }
    internal TeamWriteResult TeamUpdateResult { get; set; } = TeamWriteResult.Success;
    internal int RoleFailuresRemaining { get; set; }
    internal int MenuFailuresRemaining { get; set; }
    internal static DepartmentRow Department(string code, string name) => new(Guid.NewGuid(), code, name, null, 0, true, DateTime.UtcNow, null, Convert.ToBase64String(new byte[8]));
    internal static TeamRow Team(Guid departmentId, string code, string name) => new(Guid.NewGuid(), departmentId, "IT", "IT", null, null, null, code, name, null, 0, true, DateTime.UtcNow, null, Convert.ToBase64String(new byte[8]));
    public Task<bool> HasFunctionActionAsync(Guid userId, string actionKey) => Task.FromResult(!DeniedActions.Contains(actionKey));
    public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId) => Task.FromResult<IReadOnlySet<string>>(Permissions);
    public Task<IReadOnlyList<DepartmentRow>> GetDepartmentsAsync() => Task.FromResult<IReadOnlyList<DepartmentRow>>(Departments);
    public Task<DepartmentWriteResult> CreateDepartmentAsync(DepartmentCreateVm vm, Guid? userId) { LastDepartmentCreate = vm; return Task.FromResult(HrSchema.DepartmentHasBranch && !HrSchema.Branches.Any(x => x.Id == vm.BranchId) ? DepartmentWriteResult.InvalidBranch : DepartmentWriteResult.Success); }
    public Task<DepartmentWriteResult> UpdateDepartmentAsync(DepartmentUpdateVm vm, byte[] rowVersion, Guid? userId) { DepartmentUpdates++; LastDepartmentUpdate = vm; if(ThrowDepartmentUpdate)throw new InvalidOperationException("sensitive SQL detail"); return Task.FromResult(DepartmentUpdateResult); }
    public Task<DepartmentWriteResult> SetDepartmentActiveAsync(Guid id, bool isActive, byte[] rowVersion, Guid? userId) => Task.FromResult(DepartmentWriteResult.Success);
    public Task<DepartmentWriteResult> SoftDeleteDepartmentAsync(Guid id, byte[] rowVersion, Guid? userId) => Task.FromResult(DepartmentWriteResult.Success);
    public Task<IReadOnlyList<TeamRow>> GetTeamsAsync() => Task.FromResult<IReadOnlyList<TeamRow>>(Teams);
    public Task<(IReadOnlyList<TeamDepartmentOption> Departments, IReadOnlyList<TeamParentOption> Teams)> GetTeamOptionsAsync() => Task.FromResult(((IReadOnlyList<TeamDepartmentOption>)DepartmentsOptions, (IReadOnlyList<TeamParentOption>)ParentOptions));
    public Task<TeamWriteResult> CreateTeamAsync(TeamCreateVm vm, Guid? userId) => Task.FromResult(TeamWriteResult.Success);
    public Task<TeamWriteResult> UpdateTeamAsync(TeamUpdateVm vm, byte[] rowVersion, Guid? userId) => Task.FromResult(TeamUpdateResult);
    public Task<TeamWriteResult> SetTeamActiveAsync(Guid id, bool active, byte[] rowVersion, Guid? userId) => Task.FromResult(TeamWriteResult.Success);
    public Task<TeamWriteResult> SoftDeleteTeamAsync(Guid id, byte[] rowVersion, Guid? userId) => Task.FromResult(TeamWriteResult.Success);
    public Task<List<PortalRole>> GetRolesAsync() { if (RoleFailuresRemaining-- > 0) throw new InvalidOperationException("sensitive detail"); return Task.FromResult(Roles); }
    public Task<IReadOnlyList<PortalMenuNode>> GetNavigationMenuTreeAsync(Guid userId) { if (MenuFailuresRemaining-- > 0) throw new InvalidOperationException("sensitive detail"); return Task.FromResult<IReadOnlyList<PortalMenuNode>>([]); }
}
