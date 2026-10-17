using System.Text.Json;

namespace TAPortal.Web.Models;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261014-CRM-PHASE1-007
// Application-layer, fail-closed CRM scope semantics. SQL persistence remains in PortalDb.
public enum CrmScopeType { Assigned, Team, Branch, Company }
public enum CrmScopeEffect { Allow, Deny }

public sealed record CrmScopeGrant(CrmScopeType Type, Guid? ReferenceId, CrmScopeEffect Effect, string FunctionCode);
public sealed record CrmCustomerScopeFacts(Guid CustomerId, Guid? OwnerBranchId, Guid? CompanyId,
    IReadOnlySet<Guid> AssignedEmployeeIds, IReadOnlySet<Guid> TeamIds, IReadOnlySet<Guid> SharedBranchIds);

public static class CrmScopeResolver
{
    public static bool IsVisible(CrmCustomerScopeFacts customer, Guid employeeId, IEnumerable<CrmScopeGrant> grants,
        string functionCode, bool isSysAdmin, bool employeeIsEligible)
    {
        if (!employeeIsEligible) return false;
        var relevant = grants.Where(x => x.FunctionCode.Equals(functionCode, StringComparison.OrdinalIgnoreCase)
            && x.ReferenceId.HasValue && x.Type is CrmScopeType.Assigned or CrmScopeType.Team or CrmScopeType.Branch or CrmScopeType.Company).ToArray();
        var allows = relevant.Where(x => x.Effect == CrmScopeEffect.Allow && Matches(x, customer, employeeId)).Any();
        var denied = relevant.Where(x => x.Effect == CrmScopeEffect.Deny && Matches(x, customer, employeeId)).Any();
        return (isSysAdmin || allows) && !denied;
    }

    private static bool Matches(CrmScopeGrant grant, CrmCustomerScopeFacts customer, Guid employeeId) => grant.Type switch
    {
        CrmScopeType.Assigned => grant.ReferenceId == employeeId && customer.AssignedEmployeeIds.Contains(employeeId),
        CrmScopeType.Team => customer.TeamIds.Contains(grant.ReferenceId!.Value),
        CrmScopeType.Branch => customer.OwnerBranchId == grant.ReferenceId || customer.SharedBranchIds.Contains(grant.ReferenceId!.Value),
        CrmScopeType.Company => customer.CompanyId == grant.ReferenceId,
        _ => false
    };
}

public static class CrmRelationRules
{
    public static bool IsValidPeriod(DateOnly from, DateOnly? to) => !to.HasValue || to.Value >= from;
    public static bool Overlaps(DateOnly leftFrom, DateOnly? leftTo, DateOnly rightFrom, DateOnly? rightTo) =>
        leftFrom <= (rightTo ?? DateOnly.MaxValue) && rightFrom <= (leftTo ?? DateOnly.MaxValue);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
}

public sealed record CrmRelationWriteRequest(Guid CustomerId, Guid TargetId, DateOnly ValidFrom, DateOnly? ValidTo, string? RoleCode = null);
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-015
// Relationship identity and target are immutable; Change edits dates and (for
// employee assignments) role only. RowVersion is the eight-byte SQL token.
public sealed record CrmRelationChangeRequest(Guid RelationId, byte[] RowVersion,
    DateOnly ValidFrom, DateOnly? ValidTo, string? RoleCode = null);
public sealed record CrmRelationRevokeRequest(Guid RelationId, byte[] RowVersion);
public enum CrmRelationWriteResult { Success, Forbidden, Invalid, NotFound, Overlap, ConcurrencyConflict, Failed }