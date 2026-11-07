namespace TAPortal.Web.Services;

public enum WorkOutcome { Saved, NoChange, NotFound, Forbidden, Conflict, Invalid, NoEmployee, ChecklistIncomplete }

public enum WorkFrequency { Monthly, Quarterly, Yearly }

public sealed record WorkPeriod(string Key, string Label, DateOnly DueDate, DateOnly CreateFrom);

// Rules of Admin → Công việc (operator 2026-11-07), shared by the SQL store, the controller and the tests:
// - a task has one responsible owner plus co-workers; it is visible only to them, to its creator and to TASKS.VIEW_ALL
//   (supervisors). Everyone who can see a task may change its status, tick its checklist and comment; nobody else sees it.
// - editing the task itself (title, dates, owner, co-workers, cancel) is for its creator or a supervisor holding TASKS.ASSIGN;
//   with only TASKS.CREATE the owner must be oneself.
// - no approval step: NEW → IN_PROGRESS → DONE (reopen allowed); CANCELLED by an editor. DONE needs a checklist with every item ticked.
public static class WorkTaskPolicy
{
    public const string New = "NEW", InProgress = "IN_PROGRESS", Done = "DONE", Cancelled = "CANCELLED";
    public const string TaskCodeSequence = "TASK_CODE";
    public const int TitleMax = 250, DescriptionMax = 4000, ChecklistMax = 500, CommentMax = 4000, ChecklistItemsMax = 50;

    public static readonly IReadOnlyList<string> Statuses = [New, InProgress, Done, Cancelled];
    public static readonly IReadOnlyList<string> Priorities = ["LOW", "NORMAL", "HIGH", "URGENT"];

    public static string StatusLabel(string? s) => s switch
    {
        New => "Mới", InProgress => "Đang làm", Done => "Hoàn thành", Cancelled => "Đã hủy", _ => s ?? ""
    };

    public static string StatusTone(string? s) => s switch
    {
        New => "info", InProgress => "warning", Done => "success", Cancelled => "neutral", _ => "neutral"
    };

    public static string PriorityLabel(string? p) => p switch
    {
        "LOW" => "Thấp", "NORMAL" => "Bình thường", "HIGH" => "Cao", "URGENT" => "Khẩn", _ => p ?? ""
    };

    public static string PriorityTone(string? p) => p switch { "URGENT" => "danger", "HIGH" => "warning", _ => "neutral" };

    public static bool IsOpen(string status) => status is New or InProgress;

    public static bool CanSee(bool viewAll, bool isOwner, bool isMember, bool isCreator) => viewAll || isOwner || isMember || isCreator;

    public static bool CanEdit(bool viewAll, bool canAssign, bool isCreator) => isCreator || viewAll && canAssign;

    // A non-editor (owner or co-worker) moves between NEW / IN_PROGRESS / DONE; only an editor cancels or restores a cancelled task.
    public static bool CanSetStatus(string from, string to, bool canEdit) =>
        Statuses.Contains(to) && from != to && (canEdit || from != Cancelled && to != Cancelled);

    // DONE needs at least one checklist item and every item ticked ("mỗi công việc phải có checklist" — operator 2026-11-07).
    public static WorkOutcome CheckDone(int total, int done) => total > 0 && done == total ? WorkOutcome.Saved : WorkOutcome.ChecklistIncomplete;

    public static byte Progress(string status, int total, int done) =>
        status == Done ? (byte)100 : total == 0 ? (byte)0 : (byte)Math.Clamp(done * 100 / total, 0, 100);

    // Overdue: still open and the due date has passed. Due soon: open, due within 3 days.
    public static bool IsOverdue(string status, DateOnly? due, DateOnly today) => IsOpen(status) && due is DateOnly d && d < today;
    public static bool IsDueSoon(string status, DateOnly? due, DateOnly today) => IsOpen(status) && due is DateOnly d && d >= today && d <= today.AddDays(3);

    public static IReadOnlyList<string> ChecklistLines(string? text) =>
        (text ?? "").Split('\n').Select(l => l.Trim().TrimStart('-', '*', '•').Trim()).Where(l => l.Length > 0)
            .Select(l => l.Length > ChecklistMax ? l[..ChecklistMax] : l).Take(ChecklistItemsMax).ToList();

    public static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    // ---- My work ordering ---------------------------------------------------------------------------------------
    public const string SortDue = "due", SortActivity = "activity";

    // "Hạn gần nhất": overdue and nearest due dates first, tasks without a due date last; ties by latest activity.
    // "Tương tác gần nhất": latest activity first.
    public static IEnumerable<T> Order<T>(IEnumerable<T> tasks, string? sort, Func<T, DateOnly?> due, Func<T, DateTime> activity) =>
        sort == SortActivity
            ? tasks.OrderByDescending(activity)
            : tasks.OrderBy(t => due(t) is null).ThenBy(t => due(t) ?? DateOnly.MaxValue).ThenByDescending(activity);

    // ---- Recurring templates ------------------------------------------------------------------------------------
    public static string FrequencyCode(WorkFrequency f) => f switch { WorkFrequency.Monthly => "MONTHLY", WorkFrequency.Quarterly => "QUARTERLY", _ => "YEARLY" };

    public static WorkFrequency? ParseFrequency(string? code) => code switch
    {
        "MONTHLY" => WorkFrequency.Monthly, "QUARTERLY" => WorkFrequency.Quarterly, "YEARLY" => WorkFrequency.Yearly, _ => null
    };

    public static string FrequencyLabel(string? code) => code switch
    {
        "MONTHLY" => "Hàng tháng", "QUARTERLY" => "Hàng quý", "YEARLY" => "Hàng năm", _ => code ?? ""
    };

    // The period containing `date`: its first day, key and label. Monthly "2026-10" / "T10/2026", quarterly "2026-Q4" /
    // "Q4/2026", yearly "2026" / "2026".
    public static (DateOnly Start, DateOnly End, string Key, string Label) PeriodOf(WorkFrequency f, DateOnly date) => f switch
    {
        WorkFrequency.Monthly => (new(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1), $"{date.Year:D4}-{date.Month:D2}", $"T{date.Month}/{date.Year}"),
        WorkFrequency.Quarterly => Quarter(date),
        _ => (new(date.Year, 1, 1), new(date.Year, 12, 31), $"{date.Year:D4}", $"{date.Year}")
    };

    private static (DateOnly, DateOnly, string, string) Quarter(DateOnly date)
    {
        var q = (date.Month - 1) / 3 + 1;
        var start = new DateOnly(date.Year, (q - 1) * 3 + 1, 1);
        return (start, start.AddMonths(3).AddDays(-1), $"{date.Year:D4}-Q{q}", $"Q{q}/{date.Year}");
    }

    // Due date of a period: day `dueDay` (clamped to the month length) of the period's last month + `monthOffset`.
    // Example: VAT return of October due on the 20th of the next month → monthly, offset 1, day 20 → 20/11.
    public static DateOnly DueDateOf(DateOnly periodEnd, int dueDay, int monthOffset)
    {
        var month = new DateOnly(periodEnd.Year, periodEnd.Month, 1).AddMonths(monthOffset);
        return new(month.Year, month.Month, Math.Min(Math.Max(dueDay, 1), DateTime.DaysInMonth(month.Year, month.Month)));
    }

    // Periods whose task should exist on `today`: the current period, the two before and the next one, keeping those whose
    // creation window has opened (due - leadDays <= today), whose due date is on or after the template start and its creation
    // day (no backlog of old periods), and that start before the template end.
    public static IReadOnlyList<WorkPeriod> DuePeriods(WorkFrequency f, int dueDay, int monthOffset, int leadDays, DateOnly start, DateOnly? end, DateOnly createdOn, DateOnly today)
    {
        var step = f switch { WorkFrequency.Monthly => 1, WorkFrequency.Quarterly => 3, _ => 12 };
        var result = new List<WorkPeriod>();
        // Look back far enough that a period due up to 12 months after its end still gets its task on time.
        for (var i = -(12 / step + 2); i <= 1; i++)
        {
            var p = PeriodOf(f, today.AddMonths(i * step));
            var due = DueDateOf(p.End, dueDay, monthOffset);
            var from = due.AddDays(-leadDays);
            if (from > today || due < start || due < createdOn || end is DateOnly e && p.Start > e) continue;
            if (result.All(r => r.Key != p.Key)) result.Add(new(p.Key, p.Label, due, from));
        }
        return result;
    }
}
