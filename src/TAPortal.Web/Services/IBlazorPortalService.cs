using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008
// Narrow component-facing contract keeps SQL ownership in PortalDb while enabling behavior-level component tests.
public interface IBlazorPortalService
{
    Task<bool> HasFunctionActionAsync(Guid userId, string actionKey);
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId);
    Task<IReadOnlyList<DepartmentRow>> GetDepartmentsAsync();
    Task<DepartmentWriteResult> CreateDepartmentAsync(DepartmentCreateVm vm, Guid? userId);
    Task<DepartmentWriteResult> UpdateDepartmentAsync(DepartmentUpdateVm vm, byte[] rowVersion, Guid? userId);
    Task<DepartmentWriteResult> SetDepartmentActiveAsync(Guid id, bool isActive, byte[] rowVersion, Guid? userId);
    Task<DepartmentWriteResult> SoftDeleteDepartmentAsync(Guid id, byte[] rowVersion, Guid? userId);
    Task<IReadOnlyList<TeamRow>> GetTeamsAsync();
    Task<(IReadOnlyList<TeamDepartmentOption> Departments, IReadOnlyList<TeamParentOption> Teams)> GetTeamOptionsAsync();
    Task<TeamWriteResult> CreateTeamAsync(TeamCreateVm vm, Guid? userId);
    Task<TeamWriteResult> UpdateTeamAsync(TeamUpdateVm vm, byte[] rowVersion, Guid? userId);
    Task<TeamWriteResult> SetTeamActiveAsync(Guid id, bool active, byte[] rowVersion, Guid? userId);
    Task<TeamWriteResult> SoftDeleteTeamAsync(Guid id, byte[] rowVersion, Guid? userId);
    Task<List<PortalRole>> GetRolesAsync();
    Task<IReadOnlyList<PortalMenuNode>> GetNavigationMenuTreeAsync(Guid userId);
}
