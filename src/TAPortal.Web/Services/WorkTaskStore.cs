using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;

namespace TAPortal.Web.Services;

// Admin → Công việc (migration 031): dbo.WorkTasks (one owner), dbo.WorkTaskMembers (co-workers), dbo.WorkTaskChecklistItems,
// dbo.WorkTaskActivities (comments + events), dbo.WorkTaskTemplates (recurring) and dbo.NumberSequences (TASK_CODE).
// Who may see / act is decided by WorkTaskPolicy from facts re-read in SQL (the actor's linked employee and TASKS.* permissions),
// never from the cookie or request data.

public sealed record WorkActor(Guid UserId, Guid? EmployeeId, string? EmployeeName, bool ViewAll, bool CanAssign, bool CanCreate)
{
    public bool CanCreateAny => CanCreate || CanAssign;
}

public sealed record WorkTaskRow(
    Guid Id, string TaskNo, string Title, string Status, string Priority, DateOnly? StartDate, DateOnly? DueDate, Guid OwnerId, string OwnerName,
    string? CustomerName, byte Progress, DateTime LastActivityAt, Guid? CreatedBy, int ChecklistTotal, int ChecklistDone, bool IsMember, int MemberCount,
    DateTime? CompletedAt, DateTime CreatedAt, Guid? TemplateId)
{
    public bool IsOpen => WorkTaskPolicy.IsOpen(Status);
}

public sealed record WorkChecklistItem(Guid Id, string Title, bool IsDone, DateTime? DoneAt, string? DoneByName);

public sealed record WorkActivity(Guid Id, string Kind, string? Body, string? StatusFrom, string? StatusTo, DateTime CreatedAt, string? ActorName);

public sealed record WorkTaskCard(WorkTaskRow Task, IReadOnlyList<WorkChecklistItem> Checklist, IReadOnlyList<WorkActivity> Recent, bool CanEdit, bool IsOwner);

public sealed record WorkMember(Guid EmployeeId, string Code, string Name);

public sealed record WorkTaskDetail(
    WorkTaskRow Task, string? Description, Guid? CustomerId, Guid? ContractId, string? ContractLabel, string? CreatedByName, IReadOnlyList<WorkMember> Members,
    IReadOnlyList<WorkChecklistItem> Checklist, IReadOnlyList<WorkActivity> Activities, bool CanEdit, bool IsOwner, string Stamp, string? TemplateTitle);

public sealed record WorkLookupEmployee(Guid Id, string Code, string Name, bool HasAccount);
public sealed record WorkLookupCustomer(Guid Id, string Code, string Name);
public sealed record WorkLookupContract(Guid Id, Guid CustomerId, string ContractNo, string Title);

public sealed record WorkLookups(IReadOnlyList<WorkLookupEmployee> Employees, IReadOnlyList<WorkLookupCustomer> Customers, IReadOnlyList<WorkLookupContract> Contracts)
{
    public static readonly WorkLookups Empty = new([], [], []);
}

public sealed class WorkTaskFilter
{
    public string? Scope { get; set; }          // mine (owner/co-worker) | created | all (supervisors)
    public string? Status { get; set; }         // NEW | IN_PROGRESS | DONE | CANCELLED | open | overdue
    public Guid? Owner { get; set; }
    public Guid? Customer { get; set; }
    public string? Q { get; set; }
}

public sealed class WorkTaskInput
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? OwnerEmployeeId { get; set; }
    public List<Guid> MemberIds { get; set; } = [];
    public string Priority { get; set; } = "NORMAL";
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? ChecklistText { get; set; }   // create only: one item per line
}

public sealed class WorkTemplateInput
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ChecklistText { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? OwnerEmployeeId { get; set; }
    public string Priority { get; set; } = "NORMAL";
    public string Frequency { get; set; } = "MONTHLY";
    public int DueDay { get; set; } = 20;
    public int DueMonthOffset { get; set; } = 1;
    public int LeadDays { get; set; } = 7;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record WorkTemplateRow(
    Guid Id, string Title, string? Description, string? ChecklistText, Guid? CustomerId, string? CustomerName, Guid? ContractId, Guid OwnerId, string OwnerName,
    string Priority, string Frequency, int DueDay, int DueMonthOffset, int LeadDays, DateOnly StartDate, DateOnly? EndDate, bool IsActive, int TaskCount,
    DateTime CreatedAt, string Stamp);

public sealed record WorkReportRow(
    Guid EmployeeId, string Code, string Name, string? Department, int Total, int Open, int Done, int DoneOnTime, int Overdue, int Member, int AvgOpenProgress)
{
    public int OnTimeRate => Done == 0 ? 0 : DoneOnTime * 100 / Done;
}

public sealed record WorkDepartment(Guid Id, string Name);

public sealed record WorkResult(WorkOutcome Outcome, Guid? Id = null, string? Message = null);

public interface IWorkTaskStore
{
    Task<WorkActor> ActorAsync(Guid actor);
    Task<IReadOnlyList<WorkTaskCard>> MyOpenAsync(Guid actor);
    Task<WorkTaskCard?> CardAsync(Guid actor, Guid id);
    Task<IReadOnlyList<WorkTaskRow>> ListAsync(Guid actor, WorkTaskFilter filter);
    Task<WorkTaskDetail?> GetAsync(Guid actor, Guid id);
    Task<WorkLookups> LookupsAsync();
    Task<WorkResult> CreateAsync(Guid actor, WorkTaskInput input);
    Task<WorkResult> UpdateAsync(Guid actor, Guid id, WorkTaskInput input, string stamp);
    Task<WorkResult> DeleteAsync(Guid actor, Guid id);
    Task<WorkResult> SetStatusAsync(Guid actor, Guid id, string status);
    Task<WorkResult> AddChecklistAsync(Guid actor, Guid id, string title);
    Task<WorkResult> SetChecklistAsync(Guid actor, Guid id, Guid itemId, bool done);
    Task<WorkResult> RemoveChecklistAsync(Guid actor, Guid id, Guid itemId);
    Task<WorkResult> CommentAsync(Guid actor, Guid id, string body);
    Task<IReadOnlyList<WorkTemplateRow>> TemplatesAsync();
    Task<WorkTemplateRow?> TemplateAsync(Guid id);
    Task<WorkResult> SaveTemplateAsync(Guid actor, Guid? id, WorkTemplateInput input, string? stamp);
    Task<WorkResult> DeleteTemplateAsync(Guid actor, Guid id);
    Task<int> GenerateAsync(DateOnly today);
    Task<IReadOnlyList<WorkReportRow>> ReportAsync(DateOnly from, DateOnly to, DateOnly today, Guid? department);
    Task<IReadOnlyList<WorkDepartment>> DepartmentsAsync();
}

public static class WorkTaskSql
{
    public const string ActorEmployee = "SELECT TOP 1 Id,FullName FROM dbo.Employees WHERE UserId=@actor AND IsDeleted=0 ORDER BY CreatedAt;";

    // @all=1 for supervisors; otherwise the owner, a co-worker or the creator. @emp NULL (no linked employee) matches nothing.
    public const string Visible = """
(@all=1 OR t.CreatedBy=@actor OR t.OwnerEmployeeId=@emp
 OR EXISTS(SELECT 1 FROM dbo.WorkTaskMembers vm WHERE vm.TaskId=t.Id AND vm.EmployeeId=@emp AND vm.IsActive=1))
""";

    public const string RowColumns = """
t.Id,t.TaskNo,t.Title,t.Status,t.Priority,t.StartDate,t.DueDate,t.OwnerEmployeeId,o.FullName,c.Name,t.Progress,t.LastActivityAt,t.CreatedBy,
(SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0),
(SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0 AND i.IsDone=1),
CASE WHEN EXISTS(SELECT 1 FROM dbo.WorkTaskMembers m WHERE m.TaskId=t.Id AND m.EmployeeId=@emp AND m.IsActive=1) THEN 1 ELSE 0 END,
(SELECT COUNT(1) FROM dbo.WorkTaskMembers m WHERE m.TaskId=t.Id AND m.IsActive=1),
t.CompletedAt,t.CreatedAt,t.TemplateId
FROM dbo.WorkTasks t JOIN dbo.Employees o ON o.Id=t.OwnerEmployeeId LEFT JOIN dbo.Customers c ON c.Id=t.CustomerId
""";

    // "Việc của tôi": open tasks the actor works on (owner or co-worker), with their checklist and latest three comments.
    public const string MyOpen = """
DECLARE @ids TABLE(Id uniqueidentifier PRIMARY KEY);
INSERT @ids(Id) SELECT t.Id FROM dbo.WorkTasks t
WHERE t.IsDeleted=0 AND t.Status IN ('NEW','IN_PROGRESS') AND @emp IS NOT NULL AND (t.OwnerEmployeeId=@emp
 OR EXISTS(SELECT 1 FROM dbo.WorkTaskMembers m WHERE m.TaskId=t.Id AND m.EmployeeId=@emp AND m.IsActive=1));
SELECT
""" + RowColumns + """
 WHERE t.Id IN (SELECT Id FROM @ids);
""" + CardChildren;

    public const string Card = """
DECLARE @ids TABLE(Id uniqueidentifier PRIMARY KEY);
INSERT @ids(Id) SELECT t.Id FROM dbo.WorkTasks t WHERE t.Id=@id AND t.IsDeleted=0 AND
""" + Visible + """
;
SELECT
""" + RowColumns + """
 WHERE t.Id IN (SELECT Id FROM @ids);
""" + CardChildren;

    private const string CardChildren = """
SELECT i.TaskId,i.Id,i.Title,i.IsDone,i.DoneAt,du.DisplayName FROM dbo.WorkTaskChecklistItems i LEFT JOIN dbo.Users du ON du.Id=i.DoneBy
WHERE i.TaskId IN (SELECT Id FROM @ids) AND i.IsDeleted=0 ORDER BY i.SortOrder,i.CreatedAt;
SELECT x.TaskId,x.Id,x.Kind,x.Body,x.StatusFrom,x.StatusTo,x.CreatedAt,x.DisplayName FROM (
 SELECT a.TaskId,a.Id,a.Kind,a.Body,a.StatusFrom,a.StatusTo,a.CreatedAt,u.DisplayName,ROW_NUMBER() OVER (PARTITION BY a.TaskId ORDER BY a.CreatedAt DESC) AS rn
 FROM dbo.WorkTaskActivities a LEFT JOIN dbo.Users u ON u.Id=a.CreatedBy WHERE a.TaskId IN (SELECT Id FROM @ids) AND a.Kind='COMMENT'
) x WHERE x.rn<=3 ORDER BY x.CreatedAt;
""";

    public const string List = """
SELECT TOP 500
""" + RowColumns + """
 WHERE t.IsDeleted=0 AND
""" + Visible + """
 AND (@scope<>'mine' OR t.OwnerEmployeeId=@emp OR EXISTS(SELECT 1 FROM dbo.WorkTaskMembers sm WHERE sm.TaskId=t.Id AND sm.EmployeeId=@emp AND sm.IsActive=1))
 AND (@scope<>'created' OR t.CreatedBy=@actor)
 AND (@status IS NULL OR t.Status=@status OR (@status='open' AND t.Status IN ('NEW','IN_PROGRESS'))
      OR (@status='overdue' AND t.Status IN ('NEW','IN_PROGRESS') AND t.DueDate<@today))
 AND (@owner IS NULL OR t.OwnerEmployeeId=@owner)
 AND (@customer IS NULL OR t.CustomerId=@customer)
 AND (@q IS NULL OR t.Title LIKE @q ESCAPE '\' OR t.TaskNo LIKE @q ESCAPE '\')
ORDER BY CASE WHEN t.Status IN ('NEW','IN_PROGRESS') THEN 0 ELSE 1 END, CASE WHEN t.DueDate IS NULL THEN 1 ELSE 0 END, t.DueDate, t.LastActivityAt DESC;
""";

    public const string Detail = """
SELECT
""" + RowColumns + """
 WHERE t.Id=@id AND t.IsDeleted=0 AND
""" + Visible + """
;
SELECT t.Description,t.CustomerId,t.ContractId,k.ContractNo,k.Title,cu.DisplayName,tp.Title,t.RowVersion FROM dbo.WorkTasks t
LEFT JOIN dbo.CustomerContracts k ON k.Id=t.ContractId LEFT JOIN dbo.Users cu ON cu.Id=t.CreatedBy LEFT JOIN dbo.WorkTaskTemplates tp ON tp.Id=t.TemplateId
WHERE t.Id=@id;
SELECT e.Id,e.EmployeeCode,e.FullName FROM dbo.WorkTaskMembers m JOIN dbo.Employees e ON e.Id=m.EmployeeId WHERE m.TaskId=@id AND m.IsActive=1 ORDER BY e.FullName;
SELECT i.Id,i.Title,i.IsDone,i.DoneAt,du.DisplayName FROM dbo.WorkTaskChecklistItems i LEFT JOIN dbo.Users du ON du.Id=i.DoneBy
WHERE i.TaskId=@id AND i.IsDeleted=0 ORDER BY i.SortOrder,i.CreatedAt;
SELECT a.Id,a.Kind,a.Body,a.StatusFrom,a.StatusTo,a.CreatedAt,u.DisplayName FROM dbo.WorkTaskActivities a LEFT JOIN dbo.Users u ON u.Id=a.CreatedBy
WHERE a.TaskId=@id ORDER BY a.CreatedAt;
""";

    public const string Lookups = """
SELECT e.Id,e.EmployeeCode,e.FullName,CASE WHEN e.UserId IS NULL THEN 0 ELSE 1 END FROM dbo.Employees e WHERE e.IsDeleted=0 AND e.IsActive=1 ORDER BY e.FullName;
SELECT Id,Code,Name FROM dbo.Customers WHERE IsDeleted=0 ORDER BY Name;
SELECT Id,CustomerId,ContractNo,Title FROM dbo.CustomerContracts WHERE IsDeleted=0 AND Status IN ('DRAFT','ACTIVE') ORDER BY ContractNo;
""";

    public const string ForUpdate = """
SELECT t.Status,t.OwnerEmployeeId,t.CreatedBy,t.RowVersion,
 CASE WHEN EXISTS(SELECT 1 FROM dbo.WorkTaskMembers m WHERE m.TaskId=t.Id AND m.EmployeeId=@emp AND m.IsActive=1) THEN 1 ELSE 0 END,
 (SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0 AND i.IsDone=1),
 t.Title,t.Description,t.CustomerId,t.ContractId,t.Priority,t.StartDate,t.DueDate
FROM dbo.WorkTasks t WITH (UPDLOCK, HOLDLOCK) WHERE t.Id=@id AND t.IsDeleted=0;
""";

    public const string ActiveEmployee = "SELECT COUNT(1) FROM dbo.Employees WHERE Id=@e AND IsDeleted=0 AND IsActive=1;";
    public const string CustomerExists = "SELECT COUNT(1) FROM dbo.Customers WHERE Id=@c AND IsDeleted=0;";
    public const string ContractOfCustomer = "SELECT COUNT(1) FROM dbo.CustomerContracts WHERE Id=@k AND IsDeleted=0 AND (@c IS NULL OR CustomerId=@c);";

    // Atomic: concurrent creators never get the same number.
    public const string NextTaskNo = """
UPDATE dbo.NumberSequences SET CurrentValue=CurrentValue+1,UpdatedAt=SYSUTCDATETIME()
OUTPUT inserted.Prefix,inserted.CurrentValue,inserted.Padding
WHERE Code=@seq AND ScopeId IS NULL AND IsActive=1;
""";
    public const string TaskNoTaken = "SELECT COUNT(1) FROM dbo.WorkTasks WHERE TaskNo=@no;";

    public const string Insert = """
INSERT dbo.WorkTasks(Id,TaskNo,Title,Description,CustomerId,ContractId,OwnerEmployeeId,Priority,Status,Progress,StartDate,DueDate,TemplateId,PeriodKey,CreatedBy)
VALUES(@id,@no,@title,@desc,@customer,@contract,@owner,@priority,'NEW',0,@start,@due,@template,@period,@by);
""";
    public const string Update = """
UPDATE dbo.WorkTasks SET Title=@title,Description=@desc,CustomerId=@customer,ContractId=@contract,OwnerEmployeeId=@owner,Priority=@priority,
 StartDate=@start,DueDate=@due,LastActivityAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;
""";
    public const string SoftDelete = "UPDATE dbo.WorkTasks SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string SetStatus = """
UPDATE dbo.WorkTasks SET Status=@status,Progress=@progress,CompletedAt=CASE WHEN @status='DONE' THEN SYSUTCDATETIME() ELSE NULL END,
 LastActivityAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;
""";
    public const string Touch = "UPDATE dbo.WorkTasks SET Progress=@progress,LastActivityAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;";

    public const string MembersOf = "SELECT EmployeeId FROM dbo.WorkTaskMembers WHERE TaskId=@id AND IsActive=1;";
    public const string AddMember = """
IF EXISTS(SELECT 1 FROM dbo.WorkTaskMembers WHERE TaskId=@id AND EmployeeId=@e)
 UPDATE dbo.WorkTaskMembers SET IsActive=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE TaskId=@id AND EmployeeId=@e AND IsActive=0;
ELSE INSERT dbo.WorkTaskMembers(TaskId,EmployeeId,CreatedBy) VALUES(@id,@e,@by);
""";
    public const string RemoveMember = "UPDATE dbo.WorkTaskMembers SET IsActive=0,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE TaskId=@id AND EmployeeId=@e AND IsActive=1;";

    public const string AddChecklist = """
INSERT dbo.WorkTaskChecklistItems(TaskId,Title,SortOrder,CreatedBy)
SELECT @id,@title,ISNULL(MAX(SortOrder),0)+1,@by FROM dbo.WorkTaskChecklistItems WHERE TaskId=@id;
""";
    public const string ChecklistItem = "SELECT Title,IsDone FROM dbo.WorkTaskChecklistItems WITH (UPDLOCK, HOLDLOCK) WHERE Id=@item AND TaskId=@id AND IsDeleted=0;";
    public const string SetChecklist = """
UPDATE dbo.WorkTaskChecklistItems SET IsDone=@done,DoneAt=CASE WHEN @done=1 THEN SYSUTCDATETIME() ELSE NULL END,DoneBy=CASE WHEN @done=1 THEN @by ELSE NULL END
WHERE Id=@item AND TaskId=@id AND IsDeleted=0 AND IsDone<>@done;
""";
    public const string RemoveChecklist = "UPDATE dbo.WorkTaskChecklistItems SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@item AND TaskId=@id AND IsDeleted=0;";
    public const string Counts = "SELECT COUNT(1),SUM(CASE WHEN IsDone=1 THEN 1 ELSE 0 END) FROM dbo.WorkTaskChecklistItems WHERE TaskId=@id AND IsDeleted=0;";

    public const string Activity = "INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,StatusFrom,StatusTo,CreatedBy) VALUES(@id,@kind,@body,@from,@to,@by);";

    // ---- Templates --------------------------------------------------------------------------------------------------
    public const string Templates = """
SELECT p.Id,p.Title,p.Description,p.ChecklistText,p.CustomerId,c.Name,p.ContractId,p.OwnerEmployeeId,o.FullName,p.Priority,p.Frequency,p.DueDay,
 p.DueMonthOffset,p.LeadDays,p.StartDate,p.EndDate,p.IsActive,(SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.TemplateId=p.Id AND t.IsDeleted=0),p.CreatedAt,p.RowVersion
FROM dbo.WorkTaskTemplates p JOIN dbo.Employees o ON o.Id=p.OwnerEmployeeId LEFT JOIN dbo.Customers c ON c.Id=p.CustomerId
WHERE p.IsDeleted=0 AND (@id IS NULL OR p.Id=@id) ORDER BY p.IsActive DESC,p.Title;
""";
    public const string InsertTemplate = """
INSERT dbo.WorkTaskTemplates(Title,Description,ChecklistText,CustomerId,ContractId,OwnerEmployeeId,Priority,Frequency,DueDay,DueMonthOffset,LeadDays,StartDate,EndDate,IsActive,CreatedBy)
OUTPUT inserted.Id VALUES(@title,@desc,@checklist,@customer,@contract,@owner,@priority,@freq,@day,@offset,@lead,@start,@end,@active,@by);
""";
    public const string UpdateTemplate = """
UPDATE dbo.WorkTaskTemplates SET Title=@title,Description=@desc,ChecklistText=@checklist,CustomerId=@customer,ContractId=@contract,OwnerEmployeeId=@owner,
 Priority=@priority,Frequency=@freq,DueDay=@day,DueMonthOffset=@offset,LeadDays=@lead,StartDate=@start,EndDate=@end,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@rv;
""";
    public const string DeleteTemplate = "UPDATE dbo.WorkTaskTemplates SET IsDeleted=1,IsActive=0,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@id AND IsDeleted=0;";
    public const string DueTemplates = """
SELECT p.Id,p.Title,p.Description,p.ChecklistText,p.CustomerId,p.ContractId,p.OwnerEmployeeId,p.Priority,p.Frequency,p.DueDay,p.DueMonthOffset,p.LeadDays,
 p.StartDate,p.EndDate,p.CreatedAt,p.CreatedBy
FROM dbo.WorkTaskTemplates p JOIN dbo.Employees o ON o.Id=p.OwnerEmployeeId AND o.IsDeleted=0
WHERE p.IsDeleted=0 AND p.IsActive=1 AND p.StartDate<=DATEADD(day,p.LeadDays+400,@today);
""";
    public const string PeriodExists = "SELECT COUNT(1) FROM dbo.WorkTasks WITH (UPDLOCK, HOLDLOCK) WHERE TemplateId=@template AND PeriodKey=@period;";

    // ---- Report -------------------------------------------------------------------------------------------------------
    // Per employee, tasks they own whose due date falls in [@from, @to] (or, without a due date, created in that range);
    // Overdue counts every open owned task already past due on @today; Member counts open tasks where they are a co-worker.
    public const string Report = """
SELECT e.Id,e.EmployeeCode,e.FullName,d.Name,
 COUNT(t.Id),
 SUM(CASE WHEN t.Status IN ('NEW','IN_PROGRESS') THEN 1 ELSE 0 END),
 SUM(CASE WHEN t.Status='DONE' THEN 1 ELSE 0 END),
 SUM(CASE WHEN t.Status='DONE' AND (t.DueDate IS NULL OR CAST(DATEADD(hour,7,t.CompletedAt) AS date)<=t.DueDate) THEN 1 ELSE 0 END),
 (SELECT COUNT(1) FROM dbo.WorkTasks x WHERE x.OwnerEmployeeId=e.Id AND x.IsDeleted=0 AND x.Status IN ('NEW','IN_PROGRESS') AND x.DueDate<@today),
 (SELECT COUNT(1) FROM dbo.WorkTaskMembers m JOIN dbo.WorkTasks y ON y.Id=m.TaskId AND y.IsDeleted=0 AND y.Status IN ('NEW','IN_PROGRESS') WHERE m.EmployeeId=e.Id AND m.IsActive=1),
 ISNULL(AVG(CASE WHEN t.Status IN ('NEW','IN_PROGRESS') THEN CAST(t.Progress AS int) END),0)
FROM dbo.Employees e
LEFT JOIN dbo.EmployeeAssignments a ON a.EmployeeId=e.Id AND a.IsPrimary=1 AND a.IsCurrent=1
LEFT JOIN dbo.Departments d ON d.Id=a.DepartmentId
LEFT JOIN dbo.WorkTasks t ON t.OwnerEmployeeId=e.Id AND t.IsDeleted=0 AND t.Status<>'CANCELLED'
 AND ((t.DueDate BETWEEN @from AND @to) OR (t.DueDate IS NULL AND CAST(DATEADD(hour,7,t.CreatedAt) AS date) BETWEEN @from AND @to))
WHERE e.IsDeleted=0 AND (@dept IS NULL OR a.DepartmentId=@dept)
 AND (e.IsActive=1 OR t.Id IS NOT NULL)
GROUP BY e.Id,e.EmployeeCode,e.FullName,d.Name
ORDER BY d.Name,e.FullName;
""";
    public const string Departments = "SELECT Id,Name FROM dbo.Departments WHERE IsDeleted=0 ORDER BY SortOrder,Name;";
}

public sealed class SqlWorkTaskStore(IConfiguration configuration, ILogger<SqlWorkTaskStore> logger) : IWorkTaskStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<WorkActor> ActorAsync(Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        return await Actor(cn, null, actor);
    }

    private static async Task<WorkActor> Actor(SqlConnection cn, SqlTransaction? tx, Guid actor)
    {
        Guid? emp = null; string? name = null;
        await using (var cmd = Command(cn, tx, WorkTaskSql.ActorEmployee, ("@actor", actor)))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync()) (emp, name) = (r.GetGuid(0), r.GetString(1));
        return new(actor, emp, name,
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksViewAll),
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksAssign),
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksCreate));
    }

    public async Task<IReadOnlyList<WorkTaskCard>> MyOpenAsync(Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var a = await Actor(cn, null, actor);
        if (a.EmployeeId is null) return [];
        return await Cards(cn, a, WorkTaskSql.MyOpen);
    }

    public async Task<WorkTaskCard?> CardAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var a = await Actor(cn, null, actor);
        return (await Cards(cn, a, WorkTaskSql.Card, ("@id", id))).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<WorkTaskCard>> Cards(SqlConnection cn, WorkActor a, string sql, params (string, object)[] extra)
    {
        var rows = new List<WorkTaskRow>();
        var items = new Dictionary<Guid, List<WorkChecklistItem>>();
        var recent = new Dictionary<Guid, List<WorkActivity>>();
        await using (var cmd = Command(cn, null, sql, [.. Scope(a), .. extra]))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) rows.Add(Row(r));
            await r.NextResultAsync();
            while (await r.ReadAsync()) Bucket(items, r.GetGuid(0)).Add(new(r.GetGuid(1), r.GetString(2), r.GetBoolean(3), D(r, 4), S(r, 5)));
            await r.NextResultAsync();
            while (await r.ReadAsync()) Bucket(recent, r.GetGuid(0)).Add(new(r.GetGuid(1), r.GetString(2), S(r, 3), S(r, 4), S(r, 5), r.GetDateTime(6), S(r, 7)));
        }
        return rows.Select(t => new WorkTaskCard(t, items.GetValueOrDefault(t.Id) ?? [], recent.GetValueOrDefault(t.Id) ?? [],
            WorkTaskPolicy.CanEdit(a.ViewAll, a.CanAssign, t.CreatedBy == a.UserId), t.OwnerId == a.EmployeeId)).ToList();
    }

    private static List<T> Bucket<T>(Dictionary<Guid, List<T>> map, Guid key) => map.TryGetValue(key, out var l) ? l : map[key] = [];

    public async Task<IReadOnlyList<WorkTaskRow>> ListAsync(Guid actor, WorkTaskFilter filter)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var a = await Actor(cn, null, actor);
        var scope = filter.Scope is "created" or "all" ? filter.Scope : "mine";
        if (scope == "all" && !a.ViewAll) scope = "mine";
        var q = WorkTaskPolicy.Clean(filter.Q, 100);
        var list = new List<WorkTaskRow>();
        await using var cmd = Command(cn, null, WorkTaskSql.List, [.. Scope(a), ("@scope", scope),
            ("@status", (object?)(filter.Status is { } s && (WorkTaskPolicy.Statuses.Contains(s) || s is "open" or "overdue") ? s : null) ?? DBNull.Value),
            ("@owner", Db(filter.Owner)), ("@customer", Db(filter.Customer)), ("@today", new DateParam(WorkClock.Today().ToDateTime(TimeOnly.MinValue))),
            ("@q", q is null ? DBNull.Value : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%")]);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Row(r));
        return list;
    }

    public async Task<WorkTaskDetail?> GetAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var a = await Actor(cn, null, actor);
        await using var cmd = Command(cn, null, WorkTaskSql.Detail, [.. Scope(a), ("@id", id)]);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var row = Row(r);
        await r.NextResultAsync();
        await r.ReadAsync();
        var (desc, customer, contract, contractLabel, createdBy, template, stamp) = (S(r, 0), G(r, 1), G(r, 2),
            r.IsDBNull(3) ? null : $"{r.GetString(3)} — {r.GetString(4)}", S(r, 5), S(r, 6), Convert.ToHexString((byte[])r[7]));
        var members = new List<WorkMember>();
        await r.NextResultAsync();
        while (await r.ReadAsync()) members.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2)));
        var items = new List<WorkChecklistItem>();
        await r.NextResultAsync();
        while (await r.ReadAsync()) items.Add(new(r.GetGuid(0), r.GetString(1), r.GetBoolean(2), D(r, 3), S(r, 4)));
        var activities = new List<WorkActivity>();
        await r.NextResultAsync();
        while (await r.ReadAsync()) activities.Add(new(r.GetGuid(0), r.GetString(1), S(r, 2), S(r, 3), S(r, 4), r.GetDateTime(5), S(r, 6)));
        return new(row, desc, customer, contract, contractLabel, createdBy, members, items, activities,
            WorkTaskPolicy.CanEdit(a.ViewAll, a.CanAssign, row.CreatedBy == a.UserId), row.OwnerId == a.EmployeeId, stamp, template);
    }

    public async Task<WorkLookups> LookupsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var employees = new List<WorkLookupEmployee>(); var customers = new List<WorkLookupCustomer>(); var contracts = new List<WorkLookupContract>();
        await using var cmd = Command(cn, null, WorkTaskSql.Lookups);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) employees.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1));
        await r.NextResultAsync();
        while (await r.ReadAsync()) customers.Add(new(r.GetGuid(0), S(r, 1) ?? "", r.GetString(2)));
        await r.NextResultAsync();
        while (await r.ReadAsync()) contracts.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3)));
        return new(employees, customers, contracts);
    }

    // ---- Writes -------------------------------------------------------------------------------------------------------
    public async Task<WorkResult> CreateAsync(Guid actor, WorkTaskInput input) => await InTransaction(async (cn, tx) =>
    {
        var a = await Actor(cn, tx, actor);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksView) || !a.CanCreateAny) return new(WorkOutcome.Forbidden);
        var check = await CheckInput(cn, tx, a, input);
        if (check is not null) return check;
        var id = Guid.NewGuid();
        var no = await NextTaskNo(cn, tx);
        if (no is null) return new(WorkOutcome.Invalid, Message: "Chưa cấu hình dãy số mã công việc (TASK_CODE).");
        await Exec(cn, tx, WorkTaskSql.Insert, ("@id", id), ("@no", no), ("@title", input.Title.Trim()), ("@desc", Db(WorkTaskPolicy.Clean(input.Description, WorkTaskPolicy.DescriptionMax))),
            ("@customer", Db(input.CustomerId)), ("@contract", Db(input.ContractId)), ("@owner", input.OwnerEmployeeId!.Value), ("@priority", input.Priority),
            ("@start", Date(input.StartDate)), ("@due", Date(input.DueDate)), ("@template", DBNull.Value), ("@period", DBNull.Value), ("@by", actor));
        foreach (var m in Members(input)) await Exec(cn, tx, WorkTaskSql.AddMember, ("@id", id), ("@e", m), ("@by", actor));
        foreach (var line in WorkTaskPolicy.ChecklistLines(input.ChecklistText)) await Exec(cn, tx, WorkTaskSql.AddChecklist, ("@id", id), ("@title", line), ("@by", actor));
        await Log(cn, tx, id, "CREATED", null, null, null, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> UpdateAsync(Guid actor, Guid id, WorkTaskInput input, string stamp) => await InTransaction(async (cn, tx) =>
    {
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null) return new(WorkOutcome.NotFound);
        if (!t.CanEdit(a)) return new(WorkOutcome.Forbidden);
        if (!string.Equals(Convert.ToHexString(t.RowVersion), stamp, StringComparison.OrdinalIgnoreCase)) return new(WorkOutcome.Conflict);
        var check = await CheckInput(cn, tx, a, input);
        if (check is not null) return check;
        var members = Members(input).ToHashSet();
        var current = new HashSet<Guid>();
        await using (var cmd = Command(cn, tx, WorkTaskSql.MembersOf, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) current.Add(r.GetGuid(0));
        var title = input.Title.Trim(); var desc = WorkTaskPolicy.Clean(input.Description, WorkTaskPolicy.DescriptionMax);
        var start = input.StartDate is DateTime s ? DateOnly.FromDateTime(s) : (DateOnly?)null; var due = input.DueDate is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var changed = title != t.Title || desc != t.Description || input.CustomerId != t.CustomerId || input.ContractId != t.ContractId || input.OwnerEmployeeId != t.OwnerId
            || input.Priority != t.Priority || start != t.StartDate || due != t.DueDate;
        if (!changed && members.SetEquals(current)) return new(WorkOutcome.NoChange, id);
        if (await Exec(cn, tx, WorkTaskSql.Update, ("@id", id), ("@rv", t.RowVersion), ("@title", title), ("@desc", Db(desc)), ("@customer", Db(input.CustomerId)),
                ("@contract", Db(input.ContractId)), ("@owner", input.OwnerEmployeeId!.Value), ("@priority", input.Priority), ("@start", Date(input.StartDate)),
                ("@due", Date(input.DueDate)), ("@by", actor)) != 1) return new(WorkOutcome.Conflict);
        foreach (var m in members.Except(current)) await Exec(cn, tx, WorkTaskSql.AddMember, ("@id", id), ("@e", m), ("@by", actor));
        foreach (var m in current.Except(members)) await Exec(cn, tx, WorkTaskSql.RemoveMember, ("@id", id), ("@e", m), ("@by", actor));
        if (input.OwnerEmployeeId != t.OwnerId || !members.SetEquals(current)) await Log(cn, tx, id, "ASSIGN", "Đổi người phụ trách / người cùng làm", null, null, actor);
        if (changed) await Log(cn, tx, id, "EDIT", "Sửa thông tin công việc", null, null, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> DeleteAsync(Guid actor, Guid id) => await InTransaction(async (cn, tx) =>
    {
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null) return new(WorkOutcome.NotFound);
        if (!t.CanEdit(a)) return new(WorkOutcome.Forbidden);
        await Exec(cn, tx, WorkTaskSql.SoftDelete, ("@id", id), ("@by", actor));
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> SetStatusAsync(Guid actor, Guid id, string status) => await InTransaction(async (cn, tx) =>
    {
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null || !t.CanSee(a)) return new(WorkOutcome.NotFound);
        if (t.Status == status) return new(WorkOutcome.NoChange, id);
        if (!WorkTaskPolicy.CanSetStatus(t.Status, status, t.CanEdit(a))) return new(WorkOutcome.Forbidden);
        if (status == WorkTaskPolicy.Done && WorkTaskPolicy.CheckDone(t.Total, t.DoneCount) != WorkOutcome.Saved)
            return new(WorkOutcome.ChecklistIncomplete, id, t.Total == 0
                ? "Cần có checklist công việc trước khi chuyển sang Hoàn thành."
                : $"Còn {t.Total - t.DoneCount} mục checklist chưa hoàn thành.");
        await Exec(cn, tx, WorkTaskSql.SetStatus, ("@id", id), ("@status", status), ("@progress", (int)WorkTaskPolicy.Progress(status, t.Total, t.DoneCount)), ("@by", actor));
        await Log(cn, tx, id, "STATUS", null, t.Status, status, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> AddChecklistAsync(Guid actor, Guid id, string title) => await InTransaction(async (cn, tx) =>
    {
        var text = WorkTaskPolicy.Clean(title, WorkTaskPolicy.ChecklistMax);
        if (text is null) return new(WorkOutcome.Invalid, id, "Vui lòng nhập nội dung mục checklist.");
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null || !t.CanSee(a)) return new(WorkOutcome.NotFound);
        if (t.Status == WorkTaskPolicy.Cancelled) return new(WorkOutcome.Invalid, id, "Công việc đã hủy.");
        if (t.Total >= WorkTaskPolicy.ChecklistItemsMax) return new(WorkOutcome.Invalid, id, $"Mỗi công việc tối đa {WorkTaskPolicy.ChecklistItemsMax} mục checklist.");
        await Exec(cn, tx, WorkTaskSql.AddChecklist, ("@id", id), ("@title", text), ("@by", actor));
        await Log(cn, tx, id, "CHECKLIST", "Thêm: " + text, null, null, actor);
        await AfterChecklist(cn, tx, t, id, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> SetChecklistAsync(Guid actor, Guid id, Guid itemId, bool done) => await InTransaction(async (cn, tx) =>
    {
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null || !t.CanSee(a)) return new(WorkOutcome.NotFound);
        if (t.Status == WorkTaskPolicy.Cancelled) return new(WorkOutcome.Invalid, id, "Công việc đã hủy.");
        string? itemTitle = null;
        await using (var cmd = Command(cn, tx, WorkTaskSql.ChecklistItem, ("@id", id), ("@item", itemId)))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync()) itemTitle = r.GetString(0);
        if (itemTitle is null) return new(WorkOutcome.NotFound);
        if (await Exec(cn, tx, WorkTaskSql.SetChecklist, ("@id", id), ("@item", itemId), ("@done", done), ("@by", actor)) == 0) return new(WorkOutcome.NoChange, id);
        await Log(cn, tx, id, "CHECKLIST", (done ? "Hoàn thành: " : "Bỏ hoàn thành: ") + itemTitle, null, null, actor);
        await AfterChecklist(cn, tx, t, id, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> RemoveChecklistAsync(Guid actor, Guid id, Guid itemId) => await InTransaction(async (cn, tx) =>
    {
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null || !t.CanSee(a)) return new(WorkOutcome.NotFound);
        string? itemTitle = null;
        await using (var cmd = Command(cn, tx, WorkTaskSql.ChecklistItem, ("@id", id), ("@item", itemId)))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync()) itemTitle = r.GetString(0);
        if (itemTitle is null) return new(WorkOutcome.NotFound);
        await Exec(cn, tx, WorkTaskSql.RemoveChecklist, ("@id", id), ("@item", itemId), ("@by", actor));
        await Log(cn, tx, id, "CHECKLIST", "Xóa: " + itemTitle, null, null, actor);
        await AfterChecklist(cn, tx, t, id, actor);
        return new(WorkOutcome.Saved, id);
    });

    public async Task<WorkResult> CommentAsync(Guid actor, Guid id, string body) => await InTransaction(async (cn, tx) =>
    {
        var text = WorkTaskPolicy.Clean(body, WorkTaskPolicy.CommentMax);
        if (text is null) return new(WorkOutcome.Invalid, id, "Vui lòng nhập nội dung trao đổi.");
        var (a, t) = await Load(cn, tx, actor, id);
        if (t is null || !t.CanSee(a)) return new(WorkOutcome.NotFound);
        await Log(cn, tx, id, "COMMENT", text, null, null, actor);
        await Exec(cn, tx, WorkTaskSql.Touch, ("@id", id), ("@progress", (int)WorkTaskPolicy.Progress(t.Status, t.Total, t.DoneCount)), ("@by", actor));
        return new(WorkOutcome.Saved, id);
    });

    // Keeps "DONE ⇒ every checklist item ticked": an unticked or new item reopens a DONE task; the first tick starts a NEW one.
    private static async Task AfterChecklist(SqlConnection cn, SqlTransaction tx, TaskCore t, Guid id, Guid actor)
    {
        int total, done;
        await using (var cmd = Command(cn, tx, WorkTaskSql.Counts, ("@id", id)))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            (total, done) = (r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1));
        }
        var next = t.Status == WorkTaskPolicy.Done && done < total ? WorkTaskPolicy.InProgress
            : t.Status == WorkTaskPolicy.New && done > 0 ? WorkTaskPolicy.InProgress : t.Status;
        if (next != t.Status)
        {
            await Exec(cn, tx, WorkTaskSql.SetStatus, ("@id", id), ("@status", next), ("@progress", (int)WorkTaskPolicy.Progress(next, total, done)), ("@by", actor));
            await Log(cn, tx, id, "STATUS", null, t.Status, next, actor);
        }
        else await Exec(cn, tx, WorkTaskSql.Touch, ("@id", id), ("@progress", (int)WorkTaskPolicy.Progress(next, total, done)), ("@by", actor));
    }

    // Owner rules: TASKS.ASSIGN may pick any active employee as owner and co-workers; TASKS.CREATE alone only oneself, no co-workers.
    private static async Task<WorkResult?> CheckInput(SqlConnection cn, SqlTransaction tx, WorkActor a, WorkTaskInput input)
    {
        input.Priority = WorkTaskPolicy.Priorities.Contains(input.Priority) ? input.Priority : "NORMAL";
        if (string.IsNullOrWhiteSpace(input.Title)) return new(WorkOutcome.Invalid, Message: "Vui lòng nhập tên công việc.");
        if (input.Title.Trim().Length > WorkTaskPolicy.TitleMax) return new(WorkOutcome.Invalid, Message: $"Tên công việc tối đa {WorkTaskPolicy.TitleMax} ký tự.");
        if (input.StartDate is DateTime s && input.DueDate is DateTime d && d.Date < s.Date) return new(WorkOutcome.Invalid, Message: "Hạn hoàn thành phải sau ngày bắt đầu.");
        if (!a.CanAssign)
        {
            if (a.EmployeeId is null) return new(WorkOutcome.NoEmployee, Message: "Tài khoản chưa liên kết hồ sơ nhân viên nên chưa tự tạo việc được.");
            input.OwnerEmployeeId ??= a.EmployeeId;
            if (input.OwnerEmployeeId != a.EmployeeId || Members(input).Any()) return new(WorkOutcome.Forbidden, Message: "Bạn chỉ được tạo việc cho chính mình (chưa có quyền giao việc).");
        }
        if (input.OwnerEmployeeId is not Guid owner) return new(WorkOutcome.Invalid, Message: "Vui lòng chọn người chịu trách nhiệm chính.");
        foreach (var e in Members(input).Append(owner).Distinct())
            if (Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.ActiveEmployee, ("@e", e))) != 1) return new(WorkOutcome.Invalid, Message: "Nhân viên được chọn không còn hoạt động.");
        if (input.CustomerId is Guid c && Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.CustomerExists, ("@c", c))) != 1) return new(WorkOutcome.Invalid, Message: "Khách hàng không tồn tại.");
        if (input.ContractId is Guid k && Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.ContractOfCustomer, ("@k", k), ("@c", Db(input.CustomerId)))) != 1)
            return new(WorkOutcome.Invalid, Message: "Hợp đồng không thuộc khách hàng đã chọn.");
        return null;
    }

    private static IEnumerable<Guid> Members(WorkTaskInput input) => input.MemberIds.Where(m => m != Guid.Empty && m != input.OwnerEmployeeId).Distinct();

    private static async Task<string?> NextTaskNo(SqlConnection cn, SqlTransaction tx)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            string? no = null;
            await using (var cmd = Command(cn, tx, WorkTaskSql.NextTaskNo, ("@seq", WorkTaskPolicy.TaskCodeSequence)))
            await using (var r = await cmd.ExecuteReaderAsync())
                if (await r.ReadAsync()) no = (S(r, 0) ?? "") + r.GetInt64(1).ToString().PadLeft(r.GetInt32(2), '0');
            if (no is null) return null;
            if (Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.TaskNoTaken, ("@no", no))) == 0) return no;
        }
        return null;
    }

    private sealed record TaskCore(string Status, Guid OwnerId, Guid? CreatedBy, byte[] RowVersion, bool IsMember, int Total, int DoneCount,
        string Title, string? Description, Guid? CustomerId, Guid? ContractId, string Priority, DateOnly? StartDate, DateOnly? DueDate)
    {
        public bool CanSee(WorkActor a) => WorkTaskPolicy.CanSee(a.ViewAll, OwnerId == a.EmployeeId, IsMember, CreatedBy == a.UserId);
        public bool CanEdit(WorkActor a) => WorkTaskPolicy.CanEdit(a.ViewAll, a.CanAssign, CreatedBy == a.UserId);
    }

    private static async Task<(WorkActor, TaskCore?)> Load(SqlConnection cn, SqlTransaction tx, Guid actor, Guid id)
    {
        var a = await Actor(cn, tx, actor);
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksView)) return (a, null);
        await using var cmd = Command(cn, tx, WorkTaskSql.ForUpdate, ("@id", id), ("@emp", Db(a.EmployeeId)));
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return (a, null);
        var core = new TaskCore(r.GetString(0), r.GetGuid(1), G(r, 2), (byte[])r[3], r.GetInt32(4) == 1, r.GetInt32(5), r.GetInt32(6), r.GetString(7), S(r, 8),
            G(r, 9), G(r, 10), r.GetString(11), DO(r, 12), DO(r, 13));
        return (a, core.CanSee(a) ? core : null);
    }

    private static Task Log(SqlConnection cn, SqlTransaction tx, Guid id, string kind, string? body, string? from, string? to, Guid? actor) =>
        Exec(cn, tx, WorkTaskSql.Activity, ("@id", id), ("@kind", kind), ("@body", Db(body)), ("@from", Db(from)), ("@to", Db(to)), ("@by", Db(actor)));

    // ---- Templates ------------------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<WorkTemplateRow>> TemplatesAsync() => await ReadTemplates(null);

    public async Task<WorkTemplateRow?> TemplateAsync(Guid id) => (await ReadTemplates(id)).FirstOrDefault();

    private async Task<IReadOnlyList<WorkTemplateRow>> ReadTemplates(Guid? id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var list = new List<WorkTemplateRow>();
        await using var cmd = Command(cn, null, WorkTaskSql.Templates, ("@id", Db(id)));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), S(r, 2), S(r, 3), G(r, 4), S(r, 5), G(r, 6), r.GetGuid(7), r.GetString(8), r.GetString(9), r.GetString(10),
                r.GetByte(11), r.GetByte(12), r.GetInt16(13), DateOnly.FromDateTime(r.GetDateTime(14)), DO(r, 15), r.GetBoolean(16), r.GetInt32(17), r.GetDateTime(18),
                Convert.ToHexString((byte[])r[19])));
        return list;
    }

    public async Task<WorkResult> SaveTemplateAsync(Guid actor, Guid? id, WorkTemplateInput input, string? stamp) => await InTransaction(async (cn, tx) =>
    {
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskTemplatesManage)) return new(WorkOutcome.Forbidden);
        if (WorkTemplatePolicy.Validate(input) is string error) return new(WorkOutcome.Invalid, Message: error);
        if (Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.ActiveEmployee, ("@e", input.OwnerEmployeeId!.Value))) != 1) return new(WorkOutcome.Invalid, Message: "Nhân viên được chọn không còn hoạt động.");
        if (input.CustomerId is Guid c && Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.CustomerExists, ("@c", c))) != 1) return new(WorkOutcome.Invalid, Message: "Khách hàng không tồn tại.");
        if (input.ContractId is Guid k && Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.ContractOfCustomer, ("@k", k), ("@c", Db(input.CustomerId)))) != 1)
            return new(WorkOutcome.Invalid, Message: "Hợp đồng không thuộc khách hàng đã chọn.");
        (string, object)[] args = [("@title", input.Title.Trim()), ("@desc", Db(WorkTaskPolicy.Clean(input.Description, WorkTaskPolicy.DescriptionMax))),
            ("@checklist", Db(WorkTaskPolicy.Clean(input.ChecklistText, 4000))), ("@customer", Db(input.CustomerId)), ("@contract", Db(input.ContractId)),
            ("@owner", input.OwnerEmployeeId!.Value), ("@priority", input.Priority), ("@freq", input.Frequency), ("@day", input.DueDay), ("@offset", input.DueMonthOffset),
            ("@lead", input.LeadDays), ("@start", Date(input.StartDate)), ("@end", Date(input.EndDate)), ("@active", input.IsActive), ("@by", actor)];
        if (id is null) return new(WorkOutcome.Saved, (Guid)(await Scalar(cn, tx, WorkTaskSql.InsertTemplate, args))!);
        byte[] rv;
        try { rv = Convert.FromHexString(stamp ?? ""); } catch (FormatException) { return new(WorkOutcome.Conflict); }
        return await Exec(cn, tx, WorkTaskSql.UpdateTemplate, [.. args, ("@id", id.Value), ("@rv", rv)]) == 1 ? new(WorkOutcome.Saved, id) : new(WorkOutcome.Conflict);
    });

    public async Task<WorkResult> DeleteTemplateAsync(Guid actor, Guid id) => await InTransaction(async (cn, tx) =>
    {
        if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TaskTemplatesManage)) return new(WorkOutcome.Forbidden);
        return await Exec(cn, tx, WorkTaskSql.DeleteTemplate, ("@id", id), ("@by", actor)) == 1 ? new(WorkOutcome.Saved, id) : new(WorkOutcome.NotFound);
    });

    // One task per (template, period); each period in its own transaction so one failure does not block the others.
    public async Task<int> GenerateAsync(DateOnly today)
    {
        var templates = new List<(Guid Id, string Title, string? Desc, string? Checklist, Guid? Customer, Guid? Contract, Guid Owner, string Priority, string Freq,
            int Day, int Offset, int Lead, DateOnly Start, DateOnly? End, DateOnly CreatedOn, Guid? CreatedBy)>();
        await using (var cn = Connection())
        {
            await cn.OpenAsync();
            await using var cmd = Command(cn, null, WorkTaskSql.DueTemplates, ("@today", new DateParam(today.ToDateTime(TimeOnly.MinValue))));
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                templates.Add((r.GetGuid(0), r.GetString(1), S(r, 2), S(r, 3), G(r, 4), G(r, 5), r.GetGuid(6), r.GetString(7), r.GetString(8), r.GetByte(9), r.GetByte(10),
                    r.GetInt16(11), DateOnly.FromDateTime(r.GetDateTime(12)), DO(r, 13), DateOnly.FromDateTime(r.GetDateTime(14).AddHours(7)), G(r, 15)));
        }
        var created = 0;
        foreach (var p in templates)
        {
            if (WorkTaskPolicy.ParseFrequency(p.Freq) is not WorkFrequency f) continue;
            foreach (var period in WorkTaskPolicy.DuePeriods(f, p.Day, p.Offset, p.Lead, p.Start, p.End, p.CreatedOn, today))
            {
                try
                {
                    var made = await InTransaction(async (cn, tx) =>
                    {
                        if (Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.PeriodExists, ("@template", p.Id), ("@period", period.Key))) > 0) return new WorkResult(WorkOutcome.NoChange);
                        var no = await NextTaskNo(cn, tx);
                        if (no is null) return new WorkResult(WorkOutcome.Invalid);
                        var id = Guid.NewGuid();
                        await Exec(cn, tx, WorkTaskSql.Insert, ("@id", id), ("@no", no), ("@title", Trim($"{p.Title} — kỳ {period.Label}", WorkTaskPolicy.TitleMax)),
                            ("@desc", Db(p.Desc)), ("@customer", Db(p.Customer)), ("@contract", Db(p.Contract)), ("@owner", p.Owner), ("@priority", p.Priority),
                            ("@start", new DateParam(period.CreateFrom.ToDateTime(TimeOnly.MinValue))), ("@due", new DateParam(period.DueDate.ToDateTime(TimeOnly.MinValue))),
                            ("@template", p.Id), ("@period", period.Key), ("@by", Db(p.CreatedBy)));
                        foreach (var line in WorkTaskPolicy.ChecklistLines(p.Checklist)) await Exec(cn, tx, WorkTaskSql.AddChecklist, ("@id", id), ("@title", line), ("@by", Db(p.CreatedBy)));
                        await Log(cn, tx, id, "CREATED", "Tự tạo từ việc lặp theo kỳ", null, null, null);
                        return new WorkResult(WorkOutcome.Saved, id);
                    });
                    if (made.Outcome == WorkOutcome.Saved) created++;
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    // Another run created the same period first (UX_WorkTasks_TemplatePeriod).
                }
                catch (SqlException ex)
                {
                    logger.LogError(ex, "WORK_TEMPLATE_GENERATE_FAILED template {TemplateId} period {Period}", p.Id, period.Key);
                }
            }
        }
        return created;
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;

    // ---- Report ---------------------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<WorkReportRow>> ReportAsync(DateOnly from, DateOnly to, DateOnly today, Guid? department)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var list = new List<WorkReportRow>();
        await using var cmd = Command(cn, null, WorkTaskSql.Report, ("@from", new DateParam(from.ToDateTime(TimeOnly.MinValue))), ("@to", new DateParam(to.ToDateTime(TimeOnly.MinValue))),
            ("@today", new DateParam(today.ToDateTime(TimeOnly.MinValue))), ("@dept", Db(department)));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), S(r, 3), r.GetInt32(4), I(r, 5), I(r, 6), I(r, 7), r.GetInt32(8), r.GetInt32(9), I(r, 10)));
        return list;
    }

    public async Task<IReadOnlyList<WorkDepartment>> DepartmentsAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var list = new List<WorkDepartment>();
        await using var cmd = Command(cn, null, WorkTaskSql.Departments);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1)));
        return list;
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------------
    private async Task<WorkResult> InTransaction(Func<SqlConnection, SqlTransaction, Task<WorkResult>> work)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var result = await work(cn, tx);
        if (result.Outcome == WorkOutcome.Saved) await tx.CommitAsync();
        else await tx.RollbackAsync();
        return result;
    }

    private static (string, object)[] Scope(WorkActor a) => [("@actor", a.UserId), ("@emp", Db(a.EmployeeId)), ("@all", a.ViewAll)];

    private static WorkTaskRow Row(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), DO(r, 5), DO(r, 6), r.GetGuid(7),
        r.GetString(8), S(r, 9), r.GetByte(10), r.GetDateTime(11), G(r, 12), r.GetInt32(13), r.GetInt32(14), r.GetInt32(15) == 1, r.GetInt32(16), D(r, 17), r.GetDateTime(18), G(r, 19));

    private static Guid? G(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static DateOnly? DO(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    private static int I(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static object Db(Guid? value) => value is Guid g ? g : DBNull.Value;
    private static object Date(DateTime? value) => value is DateTime d ? new DateParam(d.Date) : DBNull.Value;

    private sealed record DateParam(DateTime Value);

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = Command(cn, tx, sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is DateParam d) cmd.Parameters.Add(name, SqlDbType.Date).Value = d.Value;
            else if (value is byte[] b) cmd.Parameters.Add(name, SqlDbType.Timestamp).Value = b;
            else if (value is string s) cmd.Parameters.Add(name, SqlDbType.NVarChar, Math.Max(s.Length, 1)).Value = s;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }
}

public static class WorkTemplatePolicy
{
    public static string? Validate(WorkTemplateInput i)
    {
        i.Priority = WorkTaskPolicy.Priorities.Contains(i.Priority) ? i.Priority : "NORMAL";
        if (string.IsNullOrWhiteSpace(i.Title)) return "Vui lòng nhập tên việc lặp.";
        if (i.Title.Trim().Length > 200) return "Tên việc lặp tối đa 200 ký tự (phần còn lại dành cho kỳ).";
        if (WorkTaskPolicy.ParseFrequency(i.Frequency) is null) return "Chu kỳ không hợp lệ.";
        if (i.OwnerEmployeeId is null) return "Vui lòng chọn người chịu trách nhiệm chính.";
        if (i.DueDay is < 1 or > 31) return "Ngày đến hạn phải từ 1 đến 31.";
        if (i.DueMonthOffset is < 0 or > 12) return "Số tháng sau kỳ phải từ 0 đến 12.";
        if (i.LeadDays is < 0 or > 120) return "Số ngày tạo trước phải từ 0 đến 120.";
        if (i.StartDate is null) return "Vui lòng chọn ngày bắt đầu áp dụng.";
        if (i.EndDate is DateTime e && e.Date < i.StartDate.Value.Date) return "Ngày kết thúc phải sau ngày bắt đầu.";
        return null;
    }
}

// Vietnam date (UTC+7, no daylight saving) for due dates, "today" and the recurring generator.
public static class WorkClock
{
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public static DateTime Local(DateTime utc) => utc.AddHours(7);
}
