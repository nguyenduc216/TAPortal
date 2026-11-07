using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

public sealed record WorkDashboardVm(WorkActor? Actor, IReadOnlyList<WorkTaskCard> Cards, string Sort, DateOnly Today, string? Error);

public sealed record WorkListVm(WorkActor? Actor, IReadOnlyList<WorkTaskRow>? Tasks, WorkTaskFilter Filter, WorkLookups Lookups, DateOnly Today, string? Error);

public sealed record WorkDetailsVm(WorkTaskDetail Detail, DateOnly Today, string? Error);

public sealed record WorkEditVm(Guid? Id, WorkTaskInput Input, WorkActor Actor, WorkLookups Lookups, string? Stamp);

public sealed record WorkTemplatesVm(IReadOnlyList<WorkTemplateRow>? Templates, string? Error);

public sealed record WorkTemplateEditVm(Guid? Id, WorkTemplateInput Input, WorkLookups Lookups, string? Stamp);

public sealed record WorkReportVm(IReadOnlyList<WorkReportRow>? Rows, DateOnly From, DateOnly To, Guid? Department, IReadOnlyList<WorkDepartment> Departments,
    DateOnly Today, string? Error);

// One task card on "Việc của tôi"; Error is shown inside the card after a quick update that was refused.
public sealed record WorkCardVm(WorkTaskCard Card, DateOnly Today, string? Error, string ReturnUrl);
