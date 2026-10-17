namespace TAPortal.Web.Models;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-016
public sealed record CrmRelationshipRow(Guid Id, Guid TargetId, string Label, string? Context,
    DateOnly ValidFrom, DateOnly? ValidTo, bool IsActive, string? RoleCode, byte[] RowVersion);
public sealed record CrmTargetOption(Guid Id, string Label, string? Context);
public sealed record CrmRelationshipDetail(CustomerRow Customer,
    IReadOnlyList<CrmRelationshipRow> Employees, IReadOnlyList<CrmRelationshipRow> Branches,
    IReadOnlyList<CrmRelationshipRow> Teams, IReadOnlyList<CrmTargetOption> EmployeeOptions,
    IReadOnlyList<CrmTargetOption> BranchOptions, IReadOnlyList<CrmTargetOption> TeamOptions,
    bool CanAssign, bool CanShare, bool CanTeam);