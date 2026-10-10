using System.ComponentModel.DataAnnotations;

namespace TAPortal.Web.Models;

public static class TeamRules
{
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();
    public static bool TryDecodeRowVersion(string? value, out byte[] bytes) => DepartmentRules.TryDecodeRowVersion(value, out bytes);
    public static bool IsParentInDepartment(Guid departmentId, TeamParentOption? parent) => parent is null || parent.DepartmentId==departmentId;

    // FEATURE_ID: TAPORTAL-HR-MASTER-DATA
    // CHANGE_ID: TAPORTAL-20261010-HR-MASTER-DATA-002
    // Pure hierarchy guard used by tests and equivalent database validation.
    public static bool WouldCreateCycle(Guid teamId, Guid? parentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var seen = new HashSet<Guid>();
        var current = parentId;
        while (current.HasValue)
        {
            if (current.Value == teamId || !seen.Add(current.Value)) return true;
            current = parents.TryGetValue(current.Value, out var next) ? next : null;
        }
        return false;
    }
}

public sealed record TeamRow(Guid Id, Guid DepartmentId, string DepartmentCode, string DepartmentName, Guid? ParentTeamId, string? ParentTeamCode, string? ParentTeamName, string Code, string Name, string? Description, int SortOrder, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, string RowVersion)
{ public DateTime LastChangedAt => UpdatedAt ?? CreatedAt; }
public sealed record TeamDepartmentOption(Guid Id, string Code, string Name);
public sealed record TeamParentOption(Guid Id, Guid DepartmentId, string Code, string Name, Guid? ParentTeamId);

public class TeamCreateVm
{
    [Required] public Guid DepartmentId { get; set; }
    public Guid? ParentTeamId { get; set; }
    [Required(ErrorMessage="Vui lòng nhập mã Team."),StringLength(50)] public string Code { get; set; }=string.Empty;
    [Required(ErrorMessage="Vui lòng nhập tên Team."),StringLength(250)] public string Name { get; set; }=string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    [Range(0,9999)] public int SortOrder { get; set; }
    public bool IsActive { get; set; }=true;
}
public sealed class TeamUpdateVm:TeamCreateVm { [Required] public Guid Id { get; set; } [Required] public string RowVersion { get; set; }=string.Empty; }
public sealed class TeamMutationVm { [Required] public Guid Id { get; set; } [Required] public string RowVersion { get; set; }=string.Empty; public bool IsActive { get; set; } }
public sealed record TeamFormVm(TeamCreateVm Value,IReadOnlyList<TeamDepartmentOption> Departments,IReadOnlyList<TeamParentOption> ParentTeams,Guid? EditingId=null);
public sealed record TeamPageVm(IReadOnlyList<TeamRow> Teams,IReadOnlyList<TeamDepartmentOption> Departments,IReadOnlyList<TeamParentOption> ParentTeams,bool CanCreate,bool CanUpdate,bool CanDelete);
public enum TeamWriteResult { Success,DuplicateCode,ConcurrencyConflict,InvalidDepartment,InvalidParent,SelfParent,Cycle,HasDependencies }
