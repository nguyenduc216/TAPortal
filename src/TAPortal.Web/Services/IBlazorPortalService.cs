using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008
// Narrow component-facing contract keeps SQL ownership in PortalDb while enabling behavior-level component tests.
public interface IBlazorPortalService
{
    Task<HrSchemaOptions> GetHrSchemaOptionsAsync();
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — reuse the existing testable PortalDb boundary for customer UI and MVC.
    Task<List<CustomerRow>> GetCustomersAsync(Guid userId);
    Task<List<CustomerRow>> GetCustomersAsync(Guid userId, string? search) => GetCustomersAsync(userId);
    Task<CustomerWriteResult> CreateCustomerAsync(CustomerCreateVm vm, Guid userId);
    Task<CustomerWriteResult> UpdateCustomerAsync(Guid customerId, CustomerCreateVm vm, Guid userId) => Task.FromResult(CustomerWriteResult.Forbidden);
    Task<CustomerWriteResult> DeleteCustomerAsync(Guid customerId, Guid userId) => Task.FromResult(CustomerWriteResult.Forbidden);
    Task<bool> HasFunctionActionAsync(Guid userId, string actionKey);
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId);
    // Existing harness implementations remain safe: workflow calls fail closed until explicitly implemented.
    Task<CrmRelationWriteResult> CreateCustomerEmployeeAssignmentAsync(CrmRelationWriteRequest request, Guid actorId)
        => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> CreateCustomerBranchShareAsync(CrmRelationWriteRequest request, Guid actorId)
        => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> CreateCustomerServiceTeamAsync(CrmRelationWriteRequest request, Guid actorId)
        => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationshipDetail?> GetCrmRelationshipDetailAsync(Guid customerId, Guid actorId) => Task.FromResult<CrmRelationshipDetail?>(null);
    Task<CrmRelationWriteResult> ChangeCustomerEmployeeAssignmentAsync(CrmRelationChangeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> RevokeCustomerEmployeeAssignmentAsync(CrmRelationRevokeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> ChangeCustomerBranchShareAsync(CrmRelationChangeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> RevokeCustomerBranchShareAsync(CrmRelationRevokeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> ChangeCustomerServiceTeamAsync(CrmRelationChangeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
    Task<CrmRelationWriteResult> RevokeCustomerServiceTeamAsync(CrmRelationRevokeRequest request, Guid actorId) => Task.FromResult(CrmRelationWriteResult.Forbidden);
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
