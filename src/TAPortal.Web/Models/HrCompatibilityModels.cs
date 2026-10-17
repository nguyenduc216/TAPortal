namespace TAPortal.Web.Models;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261013-CRM-PHASE0-001
public sealed record HrBranchOption(Guid Id, string Code, string Name);
public sealed record HrSchemaOptions(bool DepartmentHasBranch, bool TeamHasBranch, bool TeamHasDescription,
    IReadOnlyList<HrBranchOption> Branches);
public sealed record EmployeeAccessState(Guid Id, bool IsActive, bool IsDeleted, string? StatusCode,
    DateOnly? StartDate, DateOnly? EndDate);
public sealed record EmployeeOrganizationAssignment(Guid Id, Guid EmployeeId, Guid BranchId,
    Guid? DepartmentId, Guid? TeamId, DateOnly ValidFrom, DateOnly? ValidTo,
    bool IsPrimary, bool IsCurrent, bool OrganizationIsValid);
public sealed record EmployeeAccessContext(bool HasEmployeeLink, bool CanAccessCustomers,
    EmployeeOrganizationAssignment? CurrentOrganization);

public static class EmployeeAssignmentRules
{
    // Catalog IsActive means a status can be selected; RESIGNED is an active catalog item too.
    public static bool IsEmployed(EmployeeAccessState employee, DateOnly date) =>
        employee.IsActive && !employee.IsDeleted
        && (employee.StatusCode?.ToUpperInvariant() is "ACTIVE" or "PROBATION" or "MATERNITY")
        && (!employee.StartDate.HasValue || employee.StartDate <= date)
        && (!employee.EndDate.HasValue || employee.EndDate >= date);

    // ValidTo/EndDate are inclusive business dates. Historical rows are never changed by resolution.
    public static EmployeeAccessContext Resolve(IReadOnlyList<EmployeeAccessState> employees,
        IEnumerable<EmployeeOrganizationAssignment> assignments, DateOnly date)
    {
        if (employees.Count == 0) return new(false, true, null); // Preserve non-employee portal accounts.
        var live = employees.Where(x => !x.IsDeleted).ToArray();
        if (live.Length != 1 || !IsEmployed(live[0], date)) return new(true, false, null);
        var current = assignments.Where(x => x.EmployeeId == live[0].Id && x.IsPrimary && x.IsCurrent
            && x.ValidFrom <= date && (!x.ValidTo.HasValue || x.ValidTo >= date))
            .Take(2).ToArray();
        // Ambiguous assignments never pick an arbitrary branch/team and never fall back to UserTeams.
        return new(true, true, current.Length == 1 && current[0].OrganizationIsValid ? current[0] : null);
    }
}
