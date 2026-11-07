using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

// Items is null when the data could not be read (the page shows an explicit "unavailable" state, not an empty list).
public sealed record EmployeesIndexVm(IReadOnlyList<EmployeeListItem>? Items, EmployeeFilter Filter, EmployeeLookups Lookups, string? Error);

// Employee is null on the create screen. EditPrivate: the actor holds both HR.EMPLOYEE.VIEW_PRIVATE and UPDATE_PRIVATE.
public sealed record EmployeeEditVm(EmployeeDetail? Employee, EmployeeInput Input, EmployeeLookups Lookups, bool EditPrivate, string? SuggestedCode)
{
    public bool IsCreate => Employee is null;
}

// Accounts is null when the actor cannot manage the login account (HR.EMPLOYEE.ACCOUNT_LINK).
public sealed record EmployeeDetailsVm(EmployeeDetail Employee, EmployeeLookups Lookups, EmployeeAccountOptions? Accounts, EmployeeAccountInput AccountDraft, IReadOnlyList<string> Errors);

// Prefix: "Input." on the create form, "" on the assignment-change form. Current: the employee's current assignment
// (its inactive rows stay selectable so an unrelated change does not force a new branch/department).
public sealed record EmployeeAssignmentFieldsVm(string Prefix, EmployeeLookups Lookups, Guid? BranchId, Guid? DepartmentId, Guid? TeamId, Guid? JobTitleId, EmployeeAssignmentView? Current);

public sealed record HrCatalogIndexVm(HrCatalogInfo Info, IReadOnlyList<HrCatalogItem>? Items, string? Error);

// Item is null on the create screen.
public sealed record HrCatalogEditVm(HrCatalogInfo Info, HrCatalogItem? Item, HrCatalogInput Input, IReadOnlyList<BranchOption> Branches, IReadOnlyList<DepartmentOption> Departments, bool ReadOnly)
{
    public bool IsCreate => Item is null;
}
