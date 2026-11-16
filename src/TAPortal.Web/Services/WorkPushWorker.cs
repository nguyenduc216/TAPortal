using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Thông báo đẩy cho công việc (operator 2026-11-16: "Tất cả các thông báo", migration 060).
// Every change of a task is already written to dbo.WorkTaskActivities (created, assigned, status, comment, checklist, edit,
// documents - from the pages, the recurring templates and the catalogue). This worker, running inside the portal, reads the
// new rows every few seconds and pushes them to the people of the task (owner, co-workers, creator), never to the person
// who made the change and only to users who turned notifications on. Once a day (from 08:00 Vietnam time) each person gets
// one reminder of their overdue / due today / due tomorrow tasks. dbo.WebPushState remembers where it stopped, so a
// restart neither repeats nor floods (rows older than an hour are skipped).
public sealed record WorkPushActivity(Guid Id, string Kind, string? Body, string? StatusFrom, string? StatusTo, string ActorName,
    Guid TaskId, string TaskNo, string Title, Guid Recipient, DateTime CreatedAt);
public sealed record WorkPushDue(Guid Recipient, int Overdue, int DueToday, int DueTomorrow);

public static class WorkPushPolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15), Settle = TimeSpan.FromSeconds(10), MaxAge = TimeSpan.FromHours(1);
    public const int ReminderHour = 8;

    // One notification per person and task for each batch; the newest change is shown, earlier ones are counted.
    public static IReadOnlyList<(Guid User, WebPushMessage Message)> Messages(IEnumerable<WorkPushActivity> rows) =>
        rows.Where(r => Worth(r))
            .GroupBy(r => (r.Recipient, r.TaskId))
            .Select(g =>
            {
                var list = g.OrderBy(r => r.CreatedAt).ToList();
                var m = Message(list[^1]);
                return (g.Key.Recipient, list.Count == 1 ? m : m with { Body = $"(+{list.Count - 1} cập nhật khác) " + m.Body });
            })
            .ToList();

    // Reordering the checklist is not news.
    public static bool Worth(WorkPushActivity r) => !(r.Kind == "CHECKLIST" && (r.Body ?? "").StartsWith("Đổi thứ tự", StringComparison.Ordinal));

    public static WebPushMessage Message(WorkPushActivity r)
    {
        var who = string.IsNullOrWhiteSpace(r.ActorName) ? "Hệ thống" : r.ActorName.Trim();
        var url = $"/Work/Details/{r.TaskId}";
        var tag = "task-" + r.TaskId.ToString("N");
        var task = $"{r.TaskNo} · {r.Title}";
        var m = r.Kind switch
        {
            "CREATED" => new WebPushMessage($"Việc mới: {r.TaskNo}", $"{r.Title} — {(r.Body is { Length: > 0 } b ? b : "giao bởi " + who)}", url, tag),
            "ASSIGN" => new($"Phân công: {r.TaskNo}", $"{who} cập nhật người phụ trách / người cùng làm: {r.Title}", url, tag),
            "STATUS" => new($"{r.TaskNo}: {WorkTaskPolicy.StatusLabel(r.StatusFrom)} → {WorkTaskPolicy.StatusLabel(r.StatusTo)}", $"{r.Title} — {who}", url, tag),
            "COMMENT" => new($"{who} · {r.TaskNo}", r.Body ?? "", url, tag),
            _ => new($"Cập nhật: {r.TaskNo}", $"{who}: {r.Body ?? "Sửa thông tin công việc"} ({r.Title})", url, tag)
        };
        return m.Body.Length == 0 ? m with { Body = task } : m;
    }

    public static WebPushMessage? Reminder(WorkPushDue d)
    {
        var parts = new List<string>();
        if (d.Overdue > 0) parts.Add($"{d.Overdue} việc quá hạn");
        if (d.DueToday > 0) parts.Add($"{d.DueToday} việc đến hạn hôm nay");
        if (d.DueTomorrow > 0) parts.Add($"{d.DueTomorrow} việc đến hạn ngày mai");
        return parts.Count == 0 ? null
            : new(d.Overdue > 0 ? "Có việc quá hạn cần xử lý" : "Việc sắp đến hạn", string.Join(" · ", parts) + ".", "/Work", "work-due");
    }

    // The first tick from 08:00 Vietnam time of a day not yet reminded.
    public static bool ReminderDue(DateTime utcNow, DateOnly? lastDay)
    {
        var local = utcNow.AddHours(7);
        return local.Hour >= ReminderHour && (lastDay is null || lastDay < DateOnly.FromDateTime(local));
    }
}

public static class WorkPushSql
{
    public const string State = "SELECT LastActivityAt,LastReminderDay FROM dbo.WebPushState WHERE Id=1;";
    public const string InitState = "IF NOT EXISTS (SELECT 1 FROM dbo.WebPushState WHERE Id=1) INSERT dbo.WebPushState(Id,LastActivityAt) VALUES(1,@at);";
    public const string Advance = "UPDATE dbo.WebPushState SET LastActivityAt=@at,UpdatedAt=SYSUTCDATETIME() WHERE Id=1 AND LastActivityAt<@at;";
    public const string ClaimReminder = "UPDATE dbo.WebPushState SET LastReminderDay=@day,UpdatedAt=SYSUTCDATETIME() WHERE Id=1 AND (LastReminderDay IS NULL OR LastReminderDay<@day);";

    // The people of a task who turned notifications on: owner and active co-workers (by their user account) and the creator.
    private const string People = """
 SELECT e.UserId FROM dbo.Employees e WHERE e.Id=t.OwnerEmployeeId AND e.IsDeleted=0 AND e.UserId IS NOT NULL
 UNION SELECT e.UserId FROM dbo.WorkTaskMembers m JOIN dbo.Employees e ON e.Id=m.EmployeeId WHERE m.TaskId=t.Id AND m.IsActive=1 AND e.IsDeleted=0 AND e.UserId IS NOT NULL
""";

    public const string Activities = """
SELECT a.Id,a.Kind,a.Body,a.StatusFrom,a.StatusTo,ISNULL(u.DisplayName,N''),t.Id,t.TaskNo,t.Title,p.UserId,a.CreatedAt
FROM dbo.WorkTaskActivities a
JOIN dbo.WorkTasks t ON t.Id=a.TaskId AND t.IsDeleted=0
LEFT JOIN dbo.Users u ON u.Id=a.CreatedBy
CROSS APPLY (
""" + People + """
 UNION SELECT t.CreatedBy WHERE t.CreatedBy IS NOT NULL
) p
WHERE a.CreatedAt>@from AND a.CreatedAt<=@to AND (a.CreatedBy IS NULL OR p.UserId<>a.CreatedBy)
 AND EXISTS (SELECT 1 FROM dbo.WebPushSubscriptions s WHERE s.UserId=p.UserId)
ORDER BY a.CreatedAt;
""";

    public const string Due = """
SELECT p.UserId,
 SUM(CASE WHEN t.DueDate<@today THEN 1 ELSE 0 END),
 SUM(CASE WHEN t.DueDate=@today THEN 1 ELSE 0 END),
 SUM(CASE WHEN t.DueDate=DATEADD(DAY,1,@today) THEN 1 ELSE 0 END)
FROM dbo.WorkTasks t
CROSS APPLY (
""" + People + """
) p
WHERE t.IsDeleted=0 AND t.Status IN ('NEW','IN_PROGRESS') AND t.DueDate IS NOT NULL AND t.DueDate<=DATEADD(DAY,1,@today)
 AND EXISTS (SELECT 1 FROM dbo.WebPushSubscriptions s WHERE s.UserId=p.UserId)
GROUP BY p.UserId;
""";
}

public sealed class WorkPushWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<WorkPushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(WorkPushPolicy.Interval);
        var quiet = false;
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
                quiet = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or migration 060 not applied yet: log once, retry at the next tick.
                if (!quiet) logger.LogWarning(ex, "WORK_PUSH_WORKER_FAILED");
                quiet = true;
            }
        }
        while (await NextTick(timer, stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var cn = new SqlConnection(configuration["Database:ConnectionString"]);
        await cn.OpenAsync(ct);
        await Exec(cn, WorkPushSql.InitState, ct, ("@at", now));
        DateTime last; DateOnly? lastDay;
        await using (var cmd = Command(cn, WorkPushSql.State))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            last = r.GetDateTime(0);
            lastDay = r.IsDBNull(1) ? null : DateOnly.FromDateTime(r.GetDateTime(1));
        }

        // Rows still being written in an open transaction get a few seconds to commit before the window closes over them.
        var to = now - WorkPushPolicy.Settle;
        var from = last < now - WorkPushPolicy.MaxAge ? now - WorkPushPolicy.MaxAge : last;
        if (to > from)
        {
            var rows = new List<WorkPushActivity>();
            await using (var cmd = Command(cn, WorkPushSql.Activities, ("@from", from), ("@to", to)))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    rows.Add(new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                        r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetGuid(6), r.GetString(7), r.GetString(8), r.GetGuid(9), r.GetDateTime(10)));
            // Moved first: a failing push service must not make the same changes be sent again and again.
            await Exec(cn, WorkPushSql.Advance, ct, ("@at", to));
            if (rows.Count > 0) await SendAsync(WorkPushPolicy.Messages(rows), ct);
        }

        if (WorkPushPolicy.ReminderDue(now, lastDay))
        {
            var today = DateOnly.FromDateTime(now.AddHours(7));
            if (await Exec(cn, WorkPushSql.ClaimReminder, ct, ("@day", today.ToDateTime(TimeOnly.MinValue))) == 1)
            {
                var due = new List<WorkPushDue>();
                await using (var cmd = Command(cn, WorkPushSql.Due, ("@today", today.ToDateTime(TimeOnly.MinValue))))
                await using (var r = await cmd.ExecuteReaderAsync(ct))
                    while (await r.ReadAsync(ct)) due.Add(new(r.GetGuid(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
                await SendAsync(due.Select(d => (d.Recipient, WorkPushPolicy.Reminder(d))).Where(x => x.Item2 is not null).Select(x => (x.Recipient, x.Item2!)).ToList(), ct);
            }
        }
    }

    private async Task SendAsync(IReadOnlyList<(Guid User, WebPushMessage Message)> messages, CancellationToken ct)
    {
        if (messages.Count == 0) return;
        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
        int sent = 0;
        foreach (var (user, message) in messages) sent += (await sender.SendAsync(user, message, ct)).Sent;
        logger.LogInformation("Work push: {Messages} message(s), {Sent} delivered to push services.", messages.Count, sent);
    }

    private static SqlCommand Command(SqlConnection cn, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn) { CommandTimeout = 30 };
        foreach (var (name, value) in args)
            if (value is DateTime d) cmd.Parameters.Add(name, System.Data.SqlDbType.DateTime2).Value = d;
            else cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    private static async Task<int> Exec(SqlConnection cn, string sql, CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, sql, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> NextTick(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); } catch (OperationCanceledException) { return false; }
    }
}
