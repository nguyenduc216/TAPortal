using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;

namespace TAPortal.Web.Services;

// Danh sách dịch vụ (operator 2026-11-16, migration 065): a service done for a customer for one month. Creating one creates
// its tasks from the service's task list (Hợp đồng > Danh mục dịch vụ > Danh sách công việc), in that order, each with its
// checklist template, unit and fee measure code. On each task staff enter quantities day by day (Kéo hóa đơn: +10, +20 ...);
// the history is kept and the total is their sum, to be priced against the service tiers at the end of the month.
// A contract link is reserved (ContractId) and optional until the contract rework.
public sealed record ServicePeriodRow(Guid Id, Guid CustomerId, string CustomerName, string? CustomerCode, Guid ServiceId, string ServiceName, DateOnly Month,
    DateOnly? StartDate, DateOnly? DueDate, Guid? OwnerId, string? OwnerName, int TaskTotal, int TaskDone, int TaskStarted, DateTime CreatedAt)
{
    public int Percent => TaskTotal == 0 ? 0 : (int)Math.Round(TaskDone * 100.0 / TaskTotal);
    public string MonthLabel => ServicePeriodPolicy.MonthLabel(Month);
    public string State(DateOnly today) => ServicePeriodPolicy.State(TaskTotal, TaskDone, TaskStarted, DueDate, today);
}

public sealed record ServicePeriodTask(Guid Id, string TaskNo, string Title, string Status, string? OwnerName, DateOnly? DueDate, byte Progress,
    int ChecklistTotal, int ChecklistDone, string? Unit, string? MetricCode, decimal Quantity, int SortOrder);

public sealed record ServicePeriodDetail(ServicePeriodRow Period, string? Note, IReadOnlyList<ServicePeriodTask> Tasks, bool CanManage)
{
    // Quantities by fee measure code, for the month's fee (next step: price them against the tiers).
    public IReadOnlyList<(string Code, string? Unit, decimal Total)> ByMetric =>
        Tasks.Where(t => t.MetricCode is not null && t.Status != WorkTaskPolicy.Cancelled)
            .GroupBy(t => t.MetricCode!).Select(g => (g.Key, g.Select(t => t.Unit).FirstOrDefault(u => u is not null), g.Sum(t => t.Quantity))).ToList();
}

public sealed record ServicePeriodFilter(DateOnly? Month, Guid? CustomerId, Guid? ServiceId, string? Q, string? State);

public sealed class ServicePeriodInput
{
    public Guid CustomerId { get; set; }
    public Guid ServiceId { get; set; }
    public string? Month { get; set; }            // yyyy-MM
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public Guid? OwnerEmployeeId { get; set; }
    public string? Note { get; set; }
}

public sealed record WorkQuantityEntry(Guid Id, DateOnly EntryDate, decimal Quantity, string? Note, DateTime CreatedAt, Guid? CreatedBy, string? CreatedByName);

public sealed record ServicePeriodResult(WorkOutcome Outcome, Guid? Id = null, string? Message = null);

public static class ServicePeriodPolicy
{
    public const string New = "NEW", InProgress = "IN_PROGRESS", Done = "DONE", Overdue = "OVERDUE", Empty = "EMPTY";
    public const int NoteMax = 1000, QuantityNoteMax = 500;
    public const decimal QuantityMax = 1_000_000_000m;

    public static string MonthLabel(DateOnly month) => $"T{month.Month:00}/{month.Year}";

    public static DateOnly? ParseMonth(string? text) =>
        DateOnly.TryParseExact((text ?? "").Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string State(int total, int done, int started, DateOnly? due, DateOnly today) =>
        total == 0 ? Empty
        : done >= total ? Done
        : due is DateOnly d && d < today ? Overdue
        : done > 0 || started > 0 ? InProgress
        : New;

    public static string StateLabel(string state) => state switch
    {
        New => "Chưa bắt đầu",
        InProgress => "Đang làm",
        Done => "Hoàn thành",
        Overdue => "Quá hạn",
        Empty => "Chưa có công việc",
        _ => state
    };

    // A quantity as typed: "10", "1.200", "-2" (a correction), "2,5".
    public static (decimal? Value, string? Error) ParseQuantity(string? text)
    {
        var s = (text ?? "").Trim().Replace(" ", "");
        if (s.Length == 0) return (null, "Nhập số lượng.");
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d{1,3}(\.\d{3})+(,\d+)?$")) s = s.Replace(".", "").Replace(',', '.');
        else s = s.Replace(',', '.');
        if (!decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return (null, "Số lượng không hợp lệ.");
        v = decimal.Round(v, 2);
        if (v == 0) return (null, "Số lượng phải khác 0 (nhập số âm để trừ bớt khi nhập nhầm).");
        if (Math.Abs(v) > QuantityMax) return (null, "Số lượng quá lớn.");
        return (v, null);
    }

    public static string Number(decimal v) => v.ToString(v == decimal.Truncate(v) ? "#,0" : "#,0.##", ServicePricingPolicy.Vi);
}

public static class ServicePeriodSql
{
    // Who sees a service month: supervisors (@all), its creator and owner, and whoever sees one of its tasks.
    public const string Visible = """
(@all=1 OR p.CreatedBy=@actor OR p.OwnerEmployeeId=@emp OR EXISTS(SELECT 1 FROM dbo.WorkTasks vt WHERE vt.ServicePeriodId=p.Id AND vt.IsDeleted=0
 AND (vt.CreatedBy=@actor OR vt.OwnerEmployeeId=@emp OR EXISTS(SELECT 1 FROM dbo.WorkTaskMembers vm WHERE vm.TaskId=vt.Id AND vm.EmployeeId=@emp AND vm.IsActive=1))))
""";

    private const string Columns = """
 p.Id,p.CustomerId,c.Name,c.Code,p.ServiceId,s.Name,p.PeriodMonth,p.StartDate,p.DueDate,p.OwnerEmployeeId,e.FullName,
 (SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.ServicePeriodId=p.Id AND t.IsDeleted=0 AND t.Status<>'CANCELLED'),
 (SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.ServicePeriodId=p.Id AND t.IsDeleted=0 AND t.Status='DONE'),
 (SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.ServicePeriodId=p.Id AND t.IsDeleted=0 AND t.Status='IN_PROGRESS'),
 p.CreatedAt
FROM dbo.ServicePeriods p JOIN dbo.Customers c ON c.Id=p.CustomerId JOIN dbo.Services s ON s.Id=p.ServiceId LEFT JOIN dbo.Employees e ON e.Id=p.OwnerEmployeeId
""";

    public const string List = "SELECT TOP 500" + Columns + "WHERE p.IsDeleted=0 AND " + Visible + """
 AND (@month IS NULL OR p.PeriodMonth=@month) AND (@customer IS NULL OR p.CustomerId=@customer) AND (@service IS NULL OR p.ServiceId=@service)
 AND (@q IS NULL OR c.Name LIKE @q ESCAPE '\' OR c.Code LIKE @q ESCAPE '\' OR s.Name LIKE @q ESCAPE '\')
ORDER BY p.PeriodMonth DESC, c.Name, s.SortOrder, s.Name;
""";

    public const string Detail = "SELECT" + Columns + "WHERE p.Id=@id AND p.IsDeleted=0 AND " + Visible + """
;
SELECT Note FROM dbo.ServicePeriods WHERE Id=@id;
SELECT t.Id,t.TaskNo,t.Title,t.Status,e.FullName,t.DueDate,t.Progress,
 (SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0 AND i.IsDone=1),
 t.Unit,t.MetricCode,ISNULL((SELECT SUM(q.Quantity) FROM dbo.WorkTaskQuantities q WHERE q.TaskId=t.Id AND q.IsDeleted=0),0),ISNULL(t.ServiceSortOrder,9999)
FROM dbo.WorkTasks t LEFT JOIN dbo.Employees e ON e.Id=t.OwnerEmployeeId
WHERE t.ServicePeriodId=@id AND t.IsDeleted=0 ORDER BY ISNULL(t.ServiceSortOrder,9999), t.CreatedAt;
""";

    public const string Exists = "SELECT COUNT(1) FROM dbo.ServicePeriods WITH (UPDLOCK, HOLDLOCK) WHERE CustomerId=@customer AND ServiceId=@service AND PeriodMonth=@month AND IsDeleted=0;";
    public const string Customer = "SELECT Name FROM dbo.Customers WHERE Id=@id AND IsDeleted=0;";
    public const string Service = "SELECT Name FROM dbo.Services WHERE Id=@id AND IsDeleted=0 AND IsActive=1;";
    public const string Owner = "SELECT COUNT(1) FROM dbo.Employees WHERE Id=@id AND IsDeleted=0 AND IsActive=1;";
    public const string Insert = """
INSERT dbo.ServicePeriods(Id,CustomerId,ServiceId,PeriodMonth,StartDate,DueDate,OwnerEmployeeId,Note,CreatedBy)
VALUES(@id,@customer,@service,@month,@start,@due,@owner,@note,@by);
""";
    // The service's task list with what each new task copies from the work catalogue.
    public const string ServiceTasks = """
SELECT k.Id,k.Name,k.Description,k.ChecklistText,k.Unit,k.MetricCode,st.SortOrder
FROM dbo.ServiceTasks st JOIN dbo.WorkTaskCatalog k ON k.Id=st.CatalogId AND k.IsDeleted=0
WHERE st.ServiceId=@service ORDER BY st.SortOrder, k.Name;
""";
    public const string CatalogEntry = "SELECT Id,Name,Description,ChecklistText,Unit,MetricCode,0 FROM dbo.WorkTaskCatalog WHERE Id=@id AND IsDeleted=0;";
    public const string LinkTask = "UPDATE dbo.WorkTasks SET ServicePeriodId=@period,ServiceSortOrder=@sort,Unit=@unit,MetricCode=@metric WHERE Id=@id;";
    public const string NextSort = "SELECT ISNULL(MAX(ServiceSortOrder),0)+10 FROM dbo.WorkTasks WHERE ServicePeriodId=@period AND IsDeleted=0;";
    public const string Head = "SELECT CustomerId,ServiceId,PeriodMonth,StartDate,DueDate,OwnerEmployeeId,CreatedBy FROM dbo.ServicePeriods WITH (UPDLOCK) WHERE Id=@id AND IsDeleted=0;";
    // A service month is removed only while none of its work has started (no status change, no quantity).
    public const string Started = """
SELECT COUNT(1) FROM dbo.WorkTasks t WHERE t.ServicePeriodId=@id AND t.IsDeleted=0
 AND (t.Status<>'NEW' OR EXISTS(SELECT 1 FROM dbo.WorkTaskQuantities q WHERE q.TaskId=t.Id AND q.IsDeleted=0)
      OR EXISTS(SELECT 1 FROM dbo.WorkTaskChecklistItems i WHERE i.TaskId=t.Id AND i.IsDeleted=0 AND i.IsDone=1));
""";
    public const string Delete = """
UPDATE dbo.WorkTasks SET IsDeleted=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE ServicePeriodId=@id AND IsDeleted=0;
UPDATE dbo.ServicePeriods SET IsDeleted=1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;
""";

    // ---- Quantities -----------------------------------------------------------------------------------------------------
    public const string Quantities = """
SELECT q.Id,q.EntryDate,q.Quantity,q.Note,q.CreatedAt,q.CreatedBy,u.DisplayName FROM dbo.WorkTaskQuantities q LEFT JOIN dbo.Users u ON u.Id=q.CreatedBy
WHERE q.TaskId=@task AND q.IsDeleted=0 ORDER BY q.EntryDate DESC, q.CreatedAt DESC;
""";
    public const string AddQuantity = """
INSERT dbo.WorkTaskQuantities(TaskId,EntryDate,Quantity,Note,CreatedBy) VALUES(@task,@date,@qty,@note,@by);
INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy) VALUES(@task,'EDIT',@log,@by);
UPDATE dbo.WorkTasks SET LastActivityAt=SYSUTCDATETIME() WHERE Id=@task;
""";
    public const string QuantityOwner = "SELECT CreatedBy,Quantity,EntryDate FROM dbo.WorkTaskQuantities WHERE Id=@entry AND TaskId=@task AND IsDeleted=0;";
    public const string DeleteQuantity = """
UPDATE dbo.WorkTaskQuantities SET IsDeleted=1,DeletedAt=SYSUTCDATETIME(),DeletedBy=@by WHERE Id=@entry AND TaskId=@task AND IsDeleted=0;
IF @@ROWCOUNT=1 INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy) VALUES(@task,'EDIT',@log,@by);
""";
}

public interface IServicePeriodStore
{
    Task<IReadOnlyList<ServicePeriodRow>> ListAsync(Guid actor, ServicePeriodFilter filter);
    Task<ServicePeriodDetail?> GetAsync(Guid actor, Guid id);
    Task<ServicePeriodResult> CreateAsync(Guid actor, ServicePeriodInput input);
    Task<ServicePeriodResult> AddTaskAsync(Guid actor, Guid periodId, Guid catalogId);
    Task<ServicePeriodResult> DeleteAsync(Guid actor, Guid id);
    // Quantities of a task (the caller has checked the task is visible). Null when unavailable (migration 065 not run).
    Task<IReadOnlyList<WorkQuantityEntry>?> QuantitiesAsync(Guid taskId);
    Task<string?> AddQuantityAsync(Guid actor, Guid taskId, DateOnly date, decimal quantity, string? unit, string? note);
    Task<string?> DeleteQuantityAsync(Guid actor, Guid taskId, Guid entryId, bool canEditTask);
}

public sealed class SqlServicePeriodStore(IConfiguration configuration, AdminAuditOptions audit, ILogger<SqlServicePeriodStore> logger) : IServicePeriodStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    private sealed record Who(Guid Actor, Guid? Employee, bool All, bool Assign, bool Create);

    private static async Task<Who> Actor(SqlConnection cn, SqlTransaction? tx, Guid actor)
    {
        Guid? emp = null;
        await using (var cmd = Command(cn, tx, WorkTaskSql.ActorEmployee, [("@actor", actor)]))
        await using (var r = await cmd.ExecuteReaderAsync())
            if (await r.ReadAsync()) emp = r.GetGuid(0);
        return new(actor, emp,
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksViewAll),
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksAssign),
            await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, AdminPermissions.TasksCreate));
    }

    private static (string, object)[] Scope(Who w) => [("@all", w.All), ("@actor", w.Actor), ("@emp", (object?)w.Employee ?? DBNull.Value)];

    public async Task<IReadOnlyList<ServicePeriodRow>> ListAsync(Guid actor, ServicePeriodFilter filter)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var w = await Actor(cn, null, actor);
        var q = string.IsNullOrWhiteSpace(filter.Q) ? null : "%" + filter.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
        await using var cmd = Command(cn, null, ServicePeriodSql.List, [.. Scope(w), ("@month", Date(filter.Month)), ("@customer", Db(filter.CustomerId)),
            ("@service", Db(filter.ServiceId)), ("@q", (object?)q ?? DBNull.Value)]);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<ServicePeriodRow>();
        while (await r.ReadAsync()) list.Add(Row(r));
        return list;
    }

    public async Task<ServicePeriodDetail?> GetAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        var w = await Actor(cn, null, actor);
        await using var cmd = Command(cn, null, ServicePeriodSql.Detail, [.. Scope(w), ("@id", id)]);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var row = Row(r);
        await r.NextResultAsync();
        string? note = null;
        if (await r.ReadAsync()) note = r.IsDBNull(0) ? null : r.GetString(0);
        await r.NextResultAsync();
        var tasks = new List<ServicePeriodTask>();
        while (await r.ReadAsync())
            tasks.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), S(r, 4), DO(r, 5), r.GetByte(6), r.GetInt32(7), r.GetInt32(8),
                S(r, 9), S(r, 10), r.GetDecimal(11), r.GetInt32(12)));
        return new(row, note, tasks, w.Assign || (w.Create && row.OwnerId == w.Employee && w.Employee is not null));
    }

    public async Task<ServicePeriodResult> CreateAsync(Guid actor, ServicePeriodInput input)
    {
        if (ServicePeriodPolicy.ParseMonth(input.Month) is not DateOnly month) return new(WorkOutcome.Invalid, Message: "Chọn tháng thực hiện.");
        if (input.CustomerId == Guid.Empty) return new(WorkOutcome.Invalid, Message: "Chọn khách hàng.");
        if (input.ServiceId == Guid.Empty) return new(WorkOutcome.Invalid, Message: "Chọn dịch vụ.");
        var start = input.StartDate is DateTime s ? DateOnly.FromDateTime(s) : month;
        var due = input.DueDate is DateTime d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        if (due < start) return new(WorkOutcome.Invalid, Message: "Hạn hoàn thành phải từ ngày bắt đầu trở đi.");
        var note = ServicePricingPolicy.CleanText(input.Note, ServicePeriodPolicy.NoteMax);
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var w = await Actor(cn, tx, actor);
            var owner = input.OwnerEmployeeId ?? w.Employee;
            if (owner is null) return new(WorkOutcome.Invalid, Message: "Chọn người phụ trách.");
            if (!w.Assign && !(w.Create && owner == w.Employee)) return new(WorkOutcome.Forbidden);
            if (await Scalar(cn, tx, ServicePeriodSql.Customer, ("@id", input.CustomerId)) is not string customer) return new(WorkOutcome.Invalid, Message: "Khách hàng không còn trong danh sách.");
            if (await Scalar(cn, tx, ServicePeriodSql.Service, ("@id", input.ServiceId)) is not string service) return new(WorkOutcome.Invalid, Message: "Dịch vụ không còn dùng.");
            if (Convert.ToInt32(await Scalar(cn, tx, ServicePeriodSql.Owner, ("@id", owner.Value))) == 0) return new(WorkOutcome.Invalid, Message: "Người phụ trách không còn làm việc.");
            if (Convert.ToInt32(await Scalar(cn, tx, ServicePeriodSql.Exists, ("@customer", input.CustomerId), ("@service", input.ServiceId), ("@month", Date(month)))) > 0)
                return new(WorkOutcome.Conflict, Message: $"{customer} đã có dịch vụ \"{service}\" tháng {ServicePeriodPolicy.MonthLabel(month)}.");
            var id = Guid.NewGuid();
            await Exec(cn, tx, ServicePeriodSql.Insert, ("@id", id), ("@customer", input.CustomerId), ("@service", input.ServiceId), ("@month", Date(month)),
                ("@start", Date(start)), ("@due", Date(due)), ("@owner", owner.Value), ("@note", (object?)note ?? DBNull.Value), ("@by", actor));
            var entries = await Entries(cn, tx, ServicePeriodSql.ServiceTasks, ("@service", input.ServiceId));
            var created = 0;
            foreach (var e in entries)
            {
                if (await NewTask(cn, tx, actor, id, e, input.CustomerId, owner.Value, start, due, $"Tạo từ dịch vụ \"{service}\" — kỳ {ServicePeriodPolicy.MonthLabel(month)}", (created + 1) * 10) is null)
                    return new(WorkOutcome.Invalid, Message: "Chưa cấu hình dãy số mã công việc (TASK_CODE).");
                created++;
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "SERVICE_PERIOD_CREATED", "dbo.ServicePeriods", id.ToString(), "{}",
                AuditSql.Json(("customerId", input.CustomerId), ("serviceId", input.ServiceId), ("month", month.ToString("yyyy-MM")), ("tasks", created)), null);
            await tx.CommitAsync();
            return new(WorkOutcome.Saved, id, created == 0 ? "Dịch vụ chưa có danh sách công việc; thêm công việc ở trang này hoặc ở Danh mục dịch vụ." : $"Đã tạo {created} công việc.");
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return new(WorkOutcome.Conflict, Message: "Khách hàng này đã có dịch vụ đó trong tháng.");
        }
        catch (AuditUnavailableException)
        {
            return new(WorkOutcome.Invalid, Message: "Không ghi được nhật ký audit nên chưa tạo. Liên hệ quản trị hệ thống.");
        }
    }

    public async Task<ServicePeriodResult> AddTaskAsync(Guid actor, Guid periodId, Guid catalogId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        var w = await Actor(cn, tx, actor);
        Guid customer, owner; DateOnly month; DateOnly? start, due;
        await using (var cmd = Command(cn, tx, ServicePeriodSql.Head, [("@id", periodId)]))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return new(WorkOutcome.NotFound);
            customer = r.GetGuid(0); month = DateOnly.FromDateTime(r.GetDateTime(2)); start = DO(r, 3); due = DO(r, 4);
            owner = r.IsDBNull(5) ? (w.Employee ?? Guid.Empty) : r.GetGuid(5);
        }
        if (!w.Assign && !(w.Create && owner == w.Employee)) return new(WorkOutcome.Forbidden);
        var entry = (await Entries(cn, tx, ServicePeriodSql.CatalogEntry, ("@id", (object)catalogId))).FirstOrDefault();
        if (entry is null) return new(WorkOutcome.Invalid, Message: "Đầu việc không còn trong danh mục.");
        var sort = Convert.ToInt32(await Scalar(cn, tx, ServicePeriodSql.NextSort, ("@period", periodId)));
        var id = await NewTask(cn, tx, actor, periodId, entry, customer, owner, start ?? month, due, $"Thêm vào dịch vụ kỳ {ServicePeriodPolicy.MonthLabel(month)}", sort);
        if (id is null) return new(WorkOutcome.Invalid, Message: "Chưa cấu hình dãy số mã công việc (TASK_CODE).");
        await tx.CommitAsync();
        return new(WorkOutcome.Saved, id);
    }

    public async Task<ServicePeriodResult> DeleteAsync(Guid actor, Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        var w = await Actor(cn, tx, actor);
        if (!w.Assign) return new(WorkOutcome.Forbidden);
        if (Convert.ToInt32(await Scalar(cn, tx, ServicePeriodSql.Started, ("@id", id))) > 0)
            return new(WorkOutcome.Invalid, Message: "Đã có công việc bắt đầu làm (đổi trạng thái, tích checklist hoặc nhập số lượng) nên không xóa được.");
        if (await Exec(cn, tx, ServicePeriodSql.Delete, ("@id", id), ("@by", actor)) == 0) return new(WorkOutcome.NotFound);
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "SERVICE_PERIOD_DELETED", "dbo.ServicePeriods", id.ToString(), "{}", "{}", null);
        await tx.CommitAsync();
        return new(WorkOutcome.Saved);
    }

    public async Task<IReadOnlyList<WorkQuantityEntry>?> QuantitiesAsync(Guid taskId)
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = Command(cn, null, ServicePeriodSql.Quantities, [("@task", taskId)]);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<WorkQuantityEntry>();
            while (await r.ReadAsync())
                list.Add(new(r.GetGuid(0), DateOnly.FromDateTime(r.GetDateTime(1)), r.GetDecimal(2), S(r, 3), r.GetDateTime(4), r.IsDBNull(5) ? null : r.GetGuid(5), S(r, 6)));
            return list;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Task quantities unavailable.");
            return null;
        }
    }

    public async Task<string?> AddQuantityAsync(Guid actor, Guid taskId, DateOnly date, decimal quantity, string? unit, string? note)
    {
        var text = $"Nhập số lượng: {(quantity > 0 ? "+" : "")}{ServicePeriodPolicy.Number(quantity)}{(unit is null ? "" : " " + unit)} (ngày {date:dd/MM/yyyy})" + (note is null ? "" : " — " + note);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        await Exec(cn, tx, ServicePeriodSql.AddQuantity, ("@task", taskId), ("@date", Date(date)), ("@qty", quantity), ("@note", (object?)note ?? DBNull.Value),
            ("@log", text.Length > 4000 ? text[..4000] : text), ("@by", actor));
        await tx.CommitAsync();
        return null;
    }

    public async Task<string?> DeleteQuantityAsync(Guid actor, Guid taskId, Guid entryId, bool canEditTask)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        Guid? by; decimal qty; DateOnly date;
        await using (var cmd = Command(cn, tx, ServicePeriodSql.QuantityOwner, [("@entry", entryId), ("@task", taskId)]))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return "Không tìm thấy lần nhập này.";
            by = r.IsDBNull(0) ? null : r.GetGuid(0); qty = r.GetDecimal(1); date = DateOnly.FromDateTime(r.GetDateTime(2));
        }
        if (by != actor && !canEditTask) return "Chỉ người đã nhập hoặc người có quyền sửa công việc được xóa lần nhập này.";
        await Exec(cn, tx, ServicePeriodSql.DeleteQuantity, ("@entry", entryId), ("@task", taskId), ("@by", actor),
            ("@log", $"Xóa lần nhập số lượng {ServicePeriodPolicy.Number(qty)} (ngày {date:dd/MM/yyyy})"));
        await tx.CommitAsync();
        return null;
    }

    private sealed record Entry(Guid Id, string Name, string? Description, string? Checklist, string? Unit, string? Metric, int Sort);

    private static async Task<List<Entry>> Entries(SqlConnection cn, SqlTransaction tx, string sql, params (string, object)[] args)
    {
        var list = new List<Entry>();
        await using var cmd = Command(cn, tx, sql, args);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), r.GetInt32(6)));
        return list;
    }

    // One task of a service month: catalogue name, checklist template, unit and fee measure code.
    private static async Task<Guid?> NewTask(SqlConnection cn, SqlTransaction tx, Guid actor, Guid period, Entry e, Guid customer, Guid owner,
        DateOnly start, DateOnly? due, string log, int sort)
    {
        var no = await NextTaskNo(cn, tx);
        if (no is null) return null;
        var id = Guid.NewGuid();
        await Exec(cn, tx, WorkTaskSql.Insert, ("@id", id), ("@no", no), ("@title", e.Name), ("@desc", (object?)e.Description ?? DBNull.Value), ("@customer", customer),
            ("@contract", DBNull.Value), ("@owner", owner), ("@priority", "NORMAL"), ("@start", Date(start)), ("@due", Date(due)), ("@template", DBNull.Value),
            ("@period", DBNull.Value), ("@by", actor));
        await Exec(cn, tx, ServicePeriodSql.LinkTask, ("@id", id), ("@period", period), ("@sort", sort), ("@unit", (object?)e.Unit ?? DBNull.Value), ("@metric", (object?)e.Metric ?? DBNull.Value));
        foreach (var line in WorkTaskPolicy.ChecklistLines(e.Checklist)) await Exec(cn, tx, WorkTaskSql.AddChecklist, ("@id", id), ("@title", line), ("@by", actor));
        await Exec(cn, tx, WorkTaskSql.Activity, ("@id", id), ("@kind", "CREATED"), ("@body", log), ("@from", DBNull.Value), ("@to", DBNull.Value), ("@by", actor), ("@imp", DBNull.Value));
        return id;
    }

    private static async Task<string?> NextTaskNo(SqlConnection cn, SqlTransaction tx)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            string? no = null;
            await using (var cmd = Command(cn, tx, WorkTaskSql.NextTaskNo, [("@seq", WorkTaskPolicy.TaskCodeSequence)]))
            await using (var r = await cmd.ExecuteReaderAsync())
                if (await r.ReadAsync()) no = (S(r, 0) ?? "") + r.GetInt64(1).ToString().PadLeft(r.GetInt32(2), '0');
            if (no is null) return null;
            if (Convert.ToInt32(await Scalar(cn, tx, WorkTaskSql.TaskNoTaken, ("@no", no))) == 0) return no;
        }
        return null;
    }

    private static ServicePeriodRow Row(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), S(r, 3), r.GetGuid(4), r.GetString(5),
        DateOnly.FromDateTime(r.GetDateTime(6)), DO(r, 7), DO(r, 8), r.IsDBNull(9) ? null : r.GetGuid(9), S(r, 10), r.GetInt32(11), r.GetInt32(12), r.GetInt32(13), r.GetDateTime(14));

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateOnly? DO(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    private static object Db(Guid? v) => (object?)v ?? DBNull.Value;
    private sealed record DateValue(DateTime Value);
    private static object Date(DateOnly? d) => d is DateOnly x ? new DateValue(x.ToDateTime(TimeOnly.MinValue)) : DBNull.Value;

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, (string Name, object Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args)
        {
            if (value is string s) cmd.Parameters.Add(name, name is "@seq" or "@no" or "@priority" or "@kind" or "@metric" ? SqlDbType.VarChar : SqlDbType.NVarChar, Math.Max(s.Length, 1)).Value = s;
            else if (value is DateValue d) cmd.Parameters.Add(name, SqlDbType.Date).Value = d.Value;
            else if (value is decimal m) { var p = cmd.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 18; p.Scale = 2; p.Value = m; }
            else if (value is DBNull && name is "@body" or "@from" or "@to" or "@note" or "@desc") cmd.Parameters.Add(name, SqlDbType.NVarChar, 1).Value = DBNull.Value;
            else cmd.Parameters.AddWithValue(name, value);
        }
        return cmd;
    }

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
}
