using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// ---- Organisation lookups (dbo.Branches, dbo.Departments, dbo.Teams, dbo.JobTitles, dbo.EmployeeStatuses, dbo.EmploymentTypes)
public sealed record BranchOption(Guid Id, string Code, string Name, string? CompanyName, bool IsActive = true);
public sealed record DepartmentOption(Guid Id, Guid BranchId, string Code, string Name, bool IsActive = true);
public sealed record TeamOption(Guid Id, Guid BranchId, Guid? DepartmentId, string Code, string Name, bool IsActive = true);
public sealed record JobTitleOption(Guid Id, string Code, string Name, bool IsActive = true);
public sealed record HrLookup(Guid Id, string Code, string Name, bool IsActive = true);

public sealed record EmployeeLookups(
    IReadOnlyList<BranchOption> Branches, IReadOnlyList<DepartmentOption> Departments, IReadOnlyList<TeamOption> Teams,
    IReadOnlyList<JobTitleOption> JobTitles, IReadOnlyList<HrLookup> Statuses, IReadOnlyList<HrLookup> EmploymentTypes)
{
    public static readonly EmployeeLookups Empty = new([], [], [], [], [], []);
    public string? BranchName(Guid? id) => Branches.FirstOrDefault(b => b.Id == id)?.Name;
    public string? DepartmentName(Guid? id) => Departments.FirstOrDefault(d => d.Id == id)?.Name;
    public string? TeamName(Guid? id) => Teams.FirstOrDefault(t => t.Id == id)?.Name;
    public string? JobTitleName(Guid? id) => JobTitles.FirstOrDefault(j => j.Id == id)?.Name;
    public HrLookup? Status(Guid? id) => Statuses.FirstOrDefault(s => s.Id == id);
}

// ---- Employee rows ---------------------------------------------------------------------------------------------
public sealed record EmployeeListItem(
    Guid Id, string Code, string FullName, Guid? BranchId, string? BranchName, Guid? DepartmentId, string? DepartmentName, string? TeamName,
    string? JobTitleName, string? MobilePhone, string? CompanyPhone, string? CompanyEmail, Guid? StatusId, string? StatusCode, string? StatusName,
    Guid? UserId, string? Username, bool AccountLoginEligible, string? PersonalEmail = null);

// Search on name, code, phones and e-mails; filters on the current primary assignment, status and account link.
public sealed class EmployeeFilter
{
    public string? Q { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? StatusId { get; set; }
    // "linked" | "unlinked" | null
    public string? Account { get; set; }

    public bool HasAccountFilter(out bool linked)
    {
        linked = Account == "linked";
        return Account is "linked" or "unlinked";
    }

    public static string? Pattern(string? q)
    {
        var text = (q ?? "").Trim();
        if (text.Length == 0) return null;
        if (text.Length > 100) text = text[..100];
        // LIKE pattern with ESCAPE '\': user input never acts as a wildcard.
        return "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
    }

    // In-memory equivalent of the SQL filter (used by the test fake and kept beside the SQL for review).
    public bool Matches(EmployeeListItem e)
    {
        var text = (Q ?? "").Trim();
        if (text.Length > 0 && !new[] { e.Code, e.FullName, e.MobilePhone, e.CompanyPhone, e.CompanyEmail, e.PersonalEmail }
                .Any(v => v?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)) return false;
        if (BranchId is { } b && e.BranchId != b) return false;
        if (DepartmentId is { } d && e.DepartmentId != d) return false;
        if (StatusId is { } s && e.StatusId != s) return false;
        if (HasAccountFilter(out var linked) && (e.UserId is not null) != linked) return false;
        return true;
    }
}

public sealed record EmployeeAssignmentView(Guid Id, Guid BranchId, Guid? DepartmentId, Guid? TeamId, Guid? JobTitleId, DateTime ValidFrom, string? Note);

public sealed record EmployeeStatusHistoryRow(DateTime EffectiveDate, string? OldStatusName, string NewStatusName, string? Reason, string? Note, string? ChangedBy, DateTime ChangedAt);

public sealed record EmployeeAssignmentHistoryRow(DateTime ValidFrom, DateTime? ValidTo, bool IsCurrent, string? BranchName, string? DepartmentName, string? TeamName, string? JobTitleName);

public sealed record LinkedAccount(Guid Id, string Username, string DisplayName, string? Email, bool LoginEligible, IReadOnlyList<string> RoleNames);

public sealed record LinkedEmployee(Guid EmployeeId, string Code, string FullName);

// Private: null when the reader may not see private fields (they are then not even read from the database).
public sealed record EmployeeDetail(
    Guid Id, string Code, string FullName, string? Gender, DateTime? DateOfBirth, string? MobilePhone, string? CompanyPhone,
    string? PersonalEmail, string? CompanyEmail, DateTime? StartDate, DateTime? EndDate, Guid? EmploymentTypeId, Guid? StatusId, string? StatusCode,
    string? StatusName, bool IsActive, string? Note, EmployeeAssignmentView? Assignment, EmployeePrivateInput? Private, LinkedAccount? Account,
    string Stamp, IReadOnlyList<EmployeeStatusHistoryRow> StatusHistory, IReadOnlyList<EmployeeAssignmentHistoryRow> AssignmentHistory)
{
    public Guid? UserId => Account?.Id;
}

// ---- Inputs ----------------------------------------------------------------------------------------------------
public sealed class EmployeePrivateInput
{
    [StringLength(50, ErrorMessage = "Số CCCD/CMND tối đa 50 ký tự.")] public string? IdentityNumber { get; set; }
    [DataType(DataType.Date)] public DateTime? IdentityIssueDate { get; set; }
    [StringLength(250, ErrorMessage = "Nơi cấp tối đa 250 ký tự.")] public string? IdentityIssuePlace { get; set; }
    [StringLength(50, ErrorMessage = "MST cá nhân tối đa 50 ký tự.")] public string? PersonalTaxCode { get; set; }
    [StringLength(50, ErrorMessage = "Số BHXH tối đa 50 ký tự.")] public string? SocialInsuranceNumber { get; set; }
    [StringLength(1000, ErrorMessage = "Địa chỉ thường trú tối đa 1000 ký tự.")] public string? PermanentAddress { get; set; }
    [StringLength(1000, ErrorMessage = "Địa chỉ hiện tại tối đa 1000 ký tự.")] public string? CurrentAddress { get; set; }

    public static readonly string[] FieldNames = [nameof(IdentityNumber), nameof(IdentityIssueDate), nameof(IdentityIssuePlace), nameof(PersonalTaxCode), nameof(SocialInsuranceNumber), nameof(PermanentAddress), nameof(CurrentAddress)];

    public EmployeePrivateInput Normalized() => new()
    {
        IdentityNumber = EmployeePolicy.Clean(IdentityNumber), IdentityIssueDate = IdentityIssueDate?.Date, IdentityIssuePlace = EmployeePolicy.Clean(IdentityIssuePlace),
        PersonalTaxCode = EmployeePolicy.Clean(PersonalTaxCode), SocialInsuranceNumber = EmployeePolicy.Clean(SocialInsuranceNumber),
        PermanentAddress = EmployeePolicy.Clean(PermanentAddress), CurrentAddress = EmployeePolicy.Clean(CurrentAddress)
    };

    // Names of the private fields that differ (audit rows record only which fields changed, never the values).
    public IReadOnlyList<string> ChangedFields(EmployeePrivateInput? before)
    {
        var a = before?.Normalized() ?? new EmployeePrivateInput();
        var b = Normalized();
        var changed = new List<string>();
        if (a.IdentityNumber != b.IdentityNumber) changed.Add(nameof(IdentityNumber));
        if (a.IdentityIssueDate != b.IdentityIssueDate) changed.Add(nameof(IdentityIssueDate));
        if (a.IdentityIssuePlace != b.IdentityIssuePlace) changed.Add(nameof(IdentityIssuePlace));
        if (a.PersonalTaxCode != b.PersonalTaxCode) changed.Add(nameof(PersonalTaxCode));
        if (a.SocialInsuranceNumber != b.SocialInsuranceNumber) changed.Add(nameof(SocialInsuranceNumber));
        if (a.PermanentAddress != b.PermanentAddress) changed.Add(nameof(PermanentAddress));
        if (a.CurrentAddress != b.CurrentAddress) changed.Add(nameof(CurrentAddress));
        return changed;
    }
}

// Create and edit. Code, status and the initial assignment are used only on create; afterwards the code is immutable and
// status/assignment change through their own actions (with their own permissions and history).
public sealed class EmployeeInput
{
    [StringLength(50, ErrorMessage = "Mã nhân viên tối đa 50 ký tự.")] public string? Code { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(250, ErrorMessage = "Họ và tên tối đa 250 ký tự.")] public string FullName { get; set; } = "";
    public string? Gender { get; set; }
    [DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
    [StringLength(50, ErrorMessage = "Số di động tối đa 50 ký tự.")] public string? MobilePhone { get; set; }
    [StringLength(50, ErrorMessage = "Số điện thoại công ty tối đa 50 ký tự.")] public string? CompanyPhone { get; set; }
    [EmailAddress(ErrorMessage = "Email cá nhân không hợp lệ."), StringLength(256)] public string? PersonalEmail { get; set; }
    [EmailAddress(ErrorMessage = "Email công ty không hợp lệ."), StringLength(256)] public string? CompanyEmail { get; set; }
    [DataType(DataType.Date)] public DateTime? StartDate { get; set; }
    public Guid? EmploymentTypeId { get; set; }
    [StringLength(4000, ErrorMessage = "Ghi chú tối đa 4000 ký tự.")] public string? Note { get; set; }

    // Create only.
    public Guid? StatusId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? JobTitleId { get; set; }

    // Applied only when the actor may update private fields (the controller clears it otherwise).
    public EmployeePrivateInput? Private { get; set; }

    [ValidateNever] public string? Stamp { get; set; }

    public static EmployeeInput From(EmployeeDetail e) => new()
    {
        Code = e.Code, FullName = e.FullName, Gender = e.Gender, DateOfBirth = e.DateOfBirth, MobilePhone = e.MobilePhone, CompanyPhone = e.CompanyPhone,
        PersonalEmail = e.PersonalEmail, CompanyEmail = e.CompanyEmail, StartDate = e.StartDate, EmploymentTypeId = e.EmploymentTypeId, Note = e.Note,
        StatusId = e.StatusId, BranchId = e.Assignment?.BranchId, DepartmentId = e.Assignment?.DepartmentId, TeamId = e.Assignment?.TeamId,
        JobTitleId = e.Assignment?.JobTitleId, Private = e.Private, Stamp = e.Stamp
    };
}

public sealed class EmployeeAssignmentInput
{
    public Guid? BranchId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? JobTitleId { get; set; }
    [DataType(DataType.Date)] public DateTime? ValidFrom { get; set; }
    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")] public string? Note { get; set; }
    [ValidateNever] public string? Stamp { get; set; }
}

public sealed class EmployeeStatusInput
{
    public Guid? StatusId { get; set; }
    [DataType(DataType.Date)] public DateTime? EffectiveDate { get; set; }
    [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")] public string? Reason { get; set; }
    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")] public string? Note { get; set; }
    [ValidateNever] public string? Stamp { get; set; }
}

public sealed class EmployeeAccountInput
{
    [StringLength(100, ErrorMessage = "Tên đăng nhập tối đa 100 ký tự.")] public string? Username { get; set; }
    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] public string? Email { get; set; }
    public string? Password { get; set; }
    public List<Guid>? RoleIds { get; set; }
    [ValidateNever] public string? Stamp { get; set; }
}

public sealed record EmployeeAccountCandidate(Guid Id, string Username, string DisplayName, string? Email, bool LoginEligible);

public sealed record EmployeeAccountOptions(IReadOnlyList<PortalRole> Roles, IReadOnlyList<EmployeeAccountCandidate> Candidates);

public enum EmployeeOutcome
{
    Saved, NoChange, NotFound, Forbidden, Conflict, DuplicateCode, InvalidCode, InvalidReference, InvalidDate, AuditUnavailable,
    LinkedAccount, AlreadyLinked, NotLinked, AccountTaken, UsernameTaken, EmailTaken, InvalidRole, RoleForbidden
}

public sealed record EmployeeResult(EmployeeOutcome Outcome, Guid? Id = null, bool AuditPersisted = false);

public enum AssignmentPlanKind { NoChange, Insert, UpdateInPlace, CloseAndInsert, InvalidDate }

// ---- Storage-independent rules shared by the SQL store and the test fakes ------------------------------------------
public static class EmployeePolicy
{
    public const string EmployeeCodeSequence = "EMPLOYEE_CODE";
    // Statuses after which the person no longer works for T.A: Employees.IsActive=0 and EndDate set.
    public static readonly IReadOnlySet<string> LeavingStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RESIGNED", "TERMINATED" };
    public static readonly IReadOnlyList<(string Code, string Name)> Genders = [("MALE", "Nam"), ("FEMALE", "Nữ"), ("OTHER", "Khác")];
    private static readonly Regex CodePattern = new("^[A-Z0-9_.-]{1,50}$", RegexOptions.CultureInvariant);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static bool IsValidCode(string code) => CodePattern.IsMatch(code);

    public static bool IsValidGender(string? gender) => Clean(gender) is not { } g || Genders.Any(x => x.Code == g);

    public static string GenderName(string? gender) => Genders.FirstOrDefault(g => g.Code == gender).Name ?? "—";

    public static bool IsLeaving(string? statusCode) => statusCode is not null && LeavingStatuses.Contains(statusCode);

    // Badge class of an EmployeeStatuses code (colours stay in the design system, not in the database value).
    public static string StatusBadge(string? code) => code switch
    {
        "ACTIVE" => "ta-badge--success",
        "PROBATION" => "ta-badge--warning",
        "MATERNITY" => "ta-badge--info",
        "SUSPENDED" => "ta-badge--neutral",
        "RESIGNED" or "TERMINATED" => "ta-badge--danger",
        _ => "ta-badge--neutral"
    };

    // Next free code of dbo.NumberSequences EMPLOYEE_CODE (Prefix + CurrentValue+1 padded), skipping codes in use.
    public static string? NextCode(string? prefix, string? suffix, int padding, long currentValue, IEnumerable<string> existingCodes)
    {
        if (padding is < 1 or > 20) return null;
        var taken = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        for (var n = currentValue + 1; n < currentValue + 10_000; n++)
        {
            var code = (prefix ?? "") + n.ToString().PadLeft(padding, '0') + (suffix ?? "");
            if (!taken.Contains(code)) return code;
        }
        return null;
    }

    // The sequence number a code uses (Prefix + digits + Suffix), so creating "NV000007" by hand moves the sequence to 7.
    public static long? SequenceNumber(string code, string? prefix, string? suffix)
    {
        prefix ??= ""; suffix ??= "";
        if (!code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var digits = code[prefix.Length..(code.Length - suffix.Length)];
        return digits.Length is > 0 and <= 18 && digits.All(char.IsAsciiDigit) ? long.Parse(digits) : null;
    }

    // Branch required (active); department of that branch; team of that branch (and of the department when both are set);
    // job title known. Inactive rows are accepted only when they are the employee's current value (keep).
    public static bool AssignmentIsValid(EmployeeLookups lookups, Guid? branchId, Guid? departmentId, Guid? teamId, Guid? jobTitleId, EmployeeAssignmentView? current = null)
    {
        bool Usable(bool exists, bool active, Guid? id, Guid? currentId) => exists && (active || id == currentId);
        if (branchId is not Guid b) return false;
        var branch = lookups.Branches.FirstOrDefault(x => x.Id == b);
        if (!Usable(branch is not null, branch?.IsActive == true, b, current?.BranchId)) return false;
        if (departmentId is Guid d)
        {
            var dept = lookups.Departments.FirstOrDefault(x => x.Id == d);
            if (!Usable(dept is not null && dept.BranchId == b, dept?.IsActive == true, d, current?.DepartmentId)) return false;
        }
        if (teamId is Guid t)
        {
            var team = lookups.Teams.FirstOrDefault(x => x.Id == t);
            if (team is null || team.BranchId != b || (departmentId is Guid dd && team.DepartmentId is Guid td && td != dd)) return false;
            if (!Usable(true, team.IsActive, t, current?.TeamId)) return false;
        }
        if (jobTitleId is Guid j)
        {
            var job = lookups.JobTitles.FirstOrDefault(x => x.Id == j);
            if (!Usable(job is not null, job?.IsActive == true, j, current?.JobTitleId)) return false;
        }
        return true;
    }

    // EmployeeAssignments keeps one primary current row per employee (UX_EmployeeAssignments_PrimaryCurrent). A change
    // closes it (IsCurrent=0, ValidTo = day before) and inserts the new primary current row; a change effective on the
    // current row's own start date corrects that row instead; an earlier date is refused.
    public static AssignmentPlanKind PlanAssignment(EmployeeAssignmentView? current, EmployeeAssignmentInput input)
    {
        var from = input.ValidFrom?.Date;
        if (from is null) return AssignmentPlanKind.InvalidDate;
        if (current is null) return AssignmentPlanKind.Insert;
        if (current.BranchId == input.BranchId && current.DepartmentId == input.DepartmentId && current.TeamId == input.TeamId && current.JobTitleId == input.JobTitleId)
            return AssignmentPlanKind.NoChange;
        if (from < current.ValidFrom.Date) return AssignmentPlanKind.InvalidDate;
        return from == current.ValidFrom.Date ? AssignmentPlanKind.UpdateInPlace : AssignmentPlanKind.CloseAndInsert;
    }

    // Soft delete is refused while a login account is linked: unlink it first (the account itself is managed on the
    // Users page), so deleting a staff record never leaves an active account silently orphaned.
    public static EmployeeOutcome EvaluateDelete(EmployeeDetail current, string? stamp)
    {
        if (!string.Equals(stamp, current.Stamp, StringComparison.Ordinal)) return EmployeeOutcome.Conflict;
        return current.Account is not null ? EmployeeOutcome.LinkedAccount : EmployeeOutcome.Saved;
    }

    // Roles chosen for a new employee account: active catalogue roles; SYS_ADMIN only by a SYS_ADMIN.
    public static EmployeeOutcome EvaluateRoles(IReadOnlyList<PortalRole> catalog, IEnumerable<Guid>? requested, bool actorIsSystemAdmin)
    {
        foreach (var id in requested ?? [])
        {
            var role = catalog.FirstOrDefault(r => r.Id == id);
            if (role is null || !role.IsActive) return EmployeeOutcome.InvalidRole;
            if (!actorIsSystemAdmin) return EmployeeOutcome.RoleForbidden;   // roles are given by a SYS_ADMIN only
        }
        return EmployeeOutcome.Saved;
    }
}
