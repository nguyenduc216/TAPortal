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

/// <summary>Visual employee picker (avatar + name, tick to choose): radio for the owner, checkboxes for co-workers.</summary>
public sealed record PeoplePickerVm(string Name, bool Multiple, IReadOnlyList<WorkLookupEmployee> People, IReadOnlyCollection<Guid> Selected, string Label, bool Required = false, string? Role = null)
{
    private static readonly int Tones = 6;

    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var last = words[^1][..1];
        return (words.Length > 1 ? words[^2][..1] + last : last).ToUpperInvariant();
    }

    /// <summary>A stable avatar colour per person (same person, same colour on every page).</summary>
    public static int Tone(Guid id) => (int)((uint)id.GetHashCode() % (uint)Tones);
}

// Tên công việc chosen from Danh mục công việc (operator 2026-11-14): typing suggests entries; with an empty catalogue the
// field is a plain text box.
public sealed record CatalogTitleVm(string Id, string? Value, int MaxLength, IReadOnlyList<WorkCatalogEntry> Catalog);
