using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

public sealed record ServicePeriodListVm(IReadOnlyList<ServicePeriodRow> Rows, ServicePeriodFilter Filter, string MonthText, IReadOnlyList<WorkLookupCustomer> Customers,
    IReadOnlyList<ServiceRow> Services, DateOnly Today, bool CanCreate, string? Error);
public sealed record ServicePeriodCreateVm(ServicePeriodInput Input, IReadOnlyList<WorkLookupCustomer> Customers, IReadOnlyList<ServiceRow> Services,
    IReadOnlyList<ServiceCategory> Categories, IReadOnlyList<WorkLookupEmployee> Employees, IReadOnlyDictionary<Guid, int> TaskCounts, bool CanAssign, Guid? Self, string? Error);
public sealed record ServicePeriodDetailVm(ServicePeriodDetail Detail, IReadOnlyList<WorkCatalogItem> Catalog, DateOnly Today);

// Công việc > Danh sách dịch vụ (operator 2026-11-16, migration 065): the services done for each customer month by month;
// a service month lists its tasks (made from the service's task list). Visibility and rights are checked in the store
// (SqlServicePeriodStore): supervisors see all, others the months they own or work on; creating needs TASKS.ASSIGN, or
// TASKS.CREATE for one's own month.
[Authorize]
public sealed class ServicePeriodsController(IServicePeriodStore periods, IServiceCatalogStore services, IWorkTaskStore work, IWorkCatalogStore workCatalog,
    ILogger<ServicePeriodsController> logger) : Controller
{
    private static DateOnly Today => WorkClock.Today();

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Index(string? month, Guid? customer, Guid? service, string? q, string? state)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ViewData["Title"] = "Danh sách dịch vụ";
        // Default: this month; "all" lists every month.
        var monthText = month ?? Today.ToString("yyyy-MM");
        var filter = new ServicePeriodFilter(monthText == "all" ? null : ServicePeriodPolicy.ParseMonth(monthText) ?? new DateOnly(Today.Year, Today.Month, 1),
            customer, service, string.IsNullOrWhiteSpace(q) ? null : q.Trim(), string.IsNullOrWhiteSpace(state) ? null : state);
        var canCreate = AdminPermissions.Has(User, AdminPermissions.TasksAssign) || AdminPermissions.Has(User, AdminPermissions.TasksCreate);
        try
        {
            var rows = await periods.ListAsync(actor, filter);
            if (filter.State is string s) rows = rows.Where(r => r.State(Today) == s).ToList();
            var lookups = await work.LookupsAsync();
            return View(new ServicePeriodListVm(rows, filter, monthText, lookups.Customers, (await services.ListAsync()).Services, Today, canCreate, null));
        }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Service months unavailable.");
            return View(new ServicePeriodListVm([], filter, monthText, [], [], Today, false, "Chưa đọc được danh sách dịch vụ (migration 065 đã chạy chưa?)."));
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Create(Guid? customer, Guid? service, string? month)
    {
        if (!AdminPermissions.Has(User, AdminPermissions.TasksAssign) && !AdminPermissions.Has(User, AdminPermissions.TasksCreate)) return Forbid();
        var input = new ServicePeriodInput { CustomerId = customer ?? Guid.Empty, ServiceId = service ?? Guid.Empty, Month = month ?? Today.ToString("yyyy-MM") };
        return await CreateView(input, null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ServicePeriodInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await periods.CreateAsync(actor, input);
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        if (result.Outcome != WorkOutcome.Saved)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return await CreateView(input, result.Message);
        }
        TempData["Success"] = "Đã tạo dịch vụ cho khách hàng. " + result.Message;
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Details(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var detail = await periods.GetAsync(actor, id);
        if (detail is null) return NotFound();
        IReadOnlyList<WorkCatalogItem> catalog;
        try { catalog = detail.CanManage ? await workCatalog.ListAsync() : []; }
        catch (Exception ex) when (ex is SqlException or ArgumentException or InvalidOperationException) { catalog = []; }
        return View(new ServicePeriodDetailVm(detail, catalog, Today));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> AddTask(Guid id, Guid catalogId)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await periods.AddTaskAsync(actor, id, catalogId);
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkOutcome.NotFound) return NotFound();
        if (result.Outcome == WorkOutcome.Saved) TempData["Success"] = "Đã thêm công việc vào dịch vụ.";
        else TempData["Error"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ServicePeriodResult result;
        try { result = await periods.DeleteAsync(actor, id); }
        catch (TAPortal.Shared.AuditUnavailableException) { result = new(WorkOutcome.Invalid, Message: "Không ghi được nhật ký audit nên chưa xóa."); }
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkOutcome.Saved) { TempData["Success"] = "Đã xóa dịch vụ của tháng và các công việc của nó."; return RedirectToAction(nameof(Index)); }
        TempData["Error"] = result.Message ?? "Không tìm thấy dịch vụ.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> CreateView(ServicePeriodInput input, string? error)
    {
        ViewData["Title"] = "Tạo dịch vụ theo tháng";
        ViewData["ParentTitle"] = "Danh sách dịch vụ";
        ViewData["ParentController"] = "ServicePeriods";
        var lookups = await work.LookupsAsync();
        var catalog = await services.ListAsync();
        var counts = new Dictionary<Guid, int>();
        foreach (var s in catalog.Services.Where(s => s.IsActive)) counts[s.Id] = (await services.ServiceTasksAsync(s.Id)).Count;
        Guid? self = null;
        if (UsersController.ActorId(User) is Guid actor) self = (await work.ActorAsync(actor)).EmployeeId;
        input.OwnerEmployeeId ??= self;
        return View("Create", new ServicePeriodCreateVm(input, lookups.Customers, catalog.Services.Where(s => s.IsActive).ToList(), catalog.Categories,
            lookups.Employees, counts, AdminPermissions.Has(User, AdminPermissions.TasksAssign), self, error));
    }
}
