using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Admin → Công việc. "Việc của tôi" (/work) is every employee's landing page: open tasks they own or work on, with quick
// updates (status, checklist, comment) posted from the card itself (wwwroot/js/ta-work.js swaps the returned card; without
// JavaScript the same forms redirect back). Visibility and every write are re-checked in SQL by SqlWorkTaskStore.
[Authorize]
public sealed class WorkController(IWorkTaskStore work, ILogger<WorkController> logger) : Controller
{
    internal const string StoreUnavailable = "Không đọc được dữ liệu công việc. Chi tiết đã ghi vào nhật ký ứng dụng.";
    internal const string CardHeader = "X-TA-Card";

    private static DateOnly Today => WorkClock.Today();

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Index(string? sort)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        sort = sort == WorkTaskPolicy.SortActivity ? WorkTaskPolicy.SortActivity : WorkTaskPolicy.SortDue;
        try
        {
            var a = await work.ActorAsync(actor);
            var cards = WorkTaskPolicy.Order(await work.MyOpenAsync(actor), sort, c => c.Task.DueDate, c => c.Task.LastActivityAt).ToList();
            return View(new WorkDashboardVm(a, cards, sort, Today, null));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WORK_UNAVAILABLE");
            return View(new WorkDashboardVm(null, [], sort, Today, StoreUnavailable));
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Tasks([FromQuery] WorkTaskFilter filter)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        try
        {
            var a = await work.ActorAsync(actor);
            filter.Scope ??= a.ViewAll ? "all" : "mine";
            return View(new WorkListVm(a, await work.ListAsync(actor, filter), filter, await work.LookupsAsync(), Today, null));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WORK_UNAVAILABLE");
            return View(new WorkListVm(null, null, filter, WorkLookups.Empty, Today, StoreUnavailable));
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Details(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var detail = await work.GetAsync(actor, id);
        return detail is null ? NotFound() : View(new WorkDetailsVm(detail, Today, null));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Create(Guid? customerId, Guid? contractId)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var a = await work.ActorAsync(actor);
        if (!a.CanCreateAny) return Forbid();
        var input = new WorkTaskInput { OwnerEmployeeId = a.EmployeeId, CustomerId = customerId, ContractId = contractId, StartDate = DateTime.Today };
        return View("Edit", new WorkEditVm(null, input, a, await work.LookupsAsync(), null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] WorkTaskInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await work.CreateAsync(actor, input);
        if (result.Outcome == WorkOutcome.Saved)
        {
            logger.LogInformation("Work task created. Actor {ActorId}, task {TaskId}.", actor, result.Id);
            TempData["Success"] = "Đã tạo công việc.";
            return RedirectToAction(nameof(Details), new { id = result.Id });
        }
        if (result.Outcome == WorkOutcome.Forbidden && result.Message is null) return Forbid();
        ModelState.AddModelError(string.Empty, result.Message ?? Message(result.Outcome));
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new WorkEditVm(null, input, await work.ActorAsync(actor), await work.LookupsAsync(), null));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var d = await work.GetAsync(actor, id);
        if (d is null) return NotFound();
        if (!d.CanEdit) return Forbid();
        var t = d.Task;
        var input = new WorkTaskInput
        {
            Title = t.Title, Description = d.Description, CustomerId = d.CustomerId, ContractId = d.ContractId, OwnerEmployeeId = t.OwnerId,
            MemberIds = d.Members.Select(m => m.EmployeeId).ToList(), Priority = t.Priority,
            StartDate = t.StartDate?.ToDateTime(TimeOnly.MinValue), DueDate = t.DueDate?.ToDateTime(TimeOnly.MinValue)
        };
        return View(new WorkEditVm(id, input, await work.ActorAsync(actor), await work.LookupsAsync(), d.Stamp));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] WorkTaskInput input, string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await work.UpdateAsync(actor, id, input, stamp ?? "");
        switch (result.Outcome)
        {
            case WorkOutcome.NotFound: return NotFound();
            case WorkOutcome.Forbidden when result.Message is null: return Forbid();
            case WorkOutcome.Saved or WorkOutcome.NoChange:
                TempData["Success"] = result.Outcome == WorkOutcome.Saved ? "Đã lưu công việc." : "Thông tin công việc không thay đổi.";
                return RedirectToAction(nameof(Details), new { id });
        }
        ModelState.AddModelError(string.Empty, result.Message ?? Message(result.Outcome));
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new WorkEditVm(id, input, await work.ActorAsync(actor), await work.LookupsAsync(), stamp));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await work.DeleteAsync(actor, id);
        if (result.Outcome == WorkOutcome.NotFound) return NotFound();
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        TempData["Success"] = "Đã xóa công việc.";
        return RedirectToAction(nameof(Tasks));
    }

    // ---- Quick updates (dashboard card or details page) ------------------------------------------------------------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> Status(Guid id, string status, string? returnUrl) => Quick(id, returnUrl, actor => work.SetStatusAsync(actor, id, status ?? ""));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> ChecklistAdd(Guid id, string title, string? returnUrl) => Quick(id, returnUrl, actor => work.AddChecklistAsync(actor, id, title ?? ""));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> ChecklistSet(Guid id, Guid item, bool done, string? returnUrl) => Quick(id, returnUrl, actor => work.SetChecklistAsync(actor, id, item, done));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> ChecklistRemove(Guid id, Guid item, string? returnUrl) => Quick(id, returnUrl, actor => work.RemoveChecklistAsync(actor, id, item));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> Comment(Guid id, string body, string? returnUrl) => Quick(id, returnUrl, actor => work.CommentAsync(actor, id, body ?? ""));

    private async Task<IActionResult> Quick(Guid id, string? returnUrl, Func<Guid, Task<WorkResult>> act)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var asCard = Request.Headers[CardHeader] == "1";
        WorkResult result;
        try { result = await act(actor); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WORK_QUICK_UPDATE_FAILED task {TaskId}", id);
            result = new(WorkOutcome.Invalid, id, StoreUnavailable);
        }
        if (result.Outcome == WorkOutcome.NotFound) return asCard ? NotFound("Không tìm thấy công việc hoặc bạn không có quyền xem.") : NotFound();
        var error = result.Outcome is WorkOutcome.Saved or WorkOutcome.NoChange ? null : result.Message ?? Message(result.Outcome);
        var back = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action(nameof(Details), new { id })!;
        if (asCard)
        {
            var card = await work.CardAsync(actor, id);
            if (card is null) return NotFound("Không tìm thấy công việc hoặc bạn không có quyền xem.");
            return PartialView("_TaskCard", new WorkCardVm(card, Today, error, back));
        }
        if (error is not null) TempData["Error"] = error;
        return LocalRedirect(back);
    }

    // ---- Recurring templates ---------------------------------------------------------------------------------------------
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> Templates()
    {
        try { return View(new WorkTemplatesVm(await work.TemplatesAsync(), null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WORK_UNAVAILABLE");
            return View(new WorkTemplatesVm(null, StoreUnavailable));
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> TemplateCreate(Guid? customerId) =>
        View("TemplateEdit", new WorkTemplateEditVm(null, new WorkTemplateInput { CustomerId = customerId, StartDate = DateTime.Today }, await work.LookupsAsync(), null));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> TemplateCreate([Bind(Prefix = "Input")] WorkTemplateInput input) => await SaveTemplate(null, input, null);

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> TemplateEdit(Guid id)
    {
        var t = await work.TemplateAsync(id);
        if (t is null) return NotFound();
        var input = new WorkTemplateInput
        {
            Title = t.Title, Description = t.Description, ChecklistText = t.ChecklistText, CustomerId = t.CustomerId, ContractId = t.ContractId,
            OwnerEmployeeId = t.OwnerId, Priority = t.Priority, Frequency = t.Frequency, DueDay = t.DueDay, DueMonthOffset = t.DueMonthOffset,
            LeadDays = t.LeadDays, StartDate = t.StartDate.ToDateTime(TimeOnly.MinValue), EndDate = t.EndDate?.ToDateTime(TimeOnly.MinValue), IsActive = t.IsActive
        };
        return View(new WorkTemplateEditVm(id, input, await work.LookupsAsync(), t.Stamp));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> TemplateEdit(Guid id, [Bind(Prefix = "Input")] WorkTemplateInput input, string? stamp) => await SaveTemplate(id, input, stamp);

    private async Task<IActionResult> SaveTemplate(Guid? id, WorkTemplateInput input, string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await work.SaveTemplateAsync(actor, id, input, stamp);
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkOutcome.Saved)
        {
            var created = await work.GenerateAsync(Today);
            TempData["Success"] = "Đã lưu việc lặp." + (created > 0 ? $" Đã tạo {created} công việc cho kỳ hiện tại." : "");
            return RedirectToAction(nameof(Templates));
        }
        ModelState.AddModelError(string.Empty, result.Message ?? Message(result.Outcome));
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("TemplateEdit", new WorkTemplateEditVm(id, input, await work.LookupsAsync(), stamp));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> TemplateDelete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await work.DeleteTemplateAsync(actor, id);
        if (result.Outcome == WorkOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkOutcome.NotFound) return NotFound();
        TempData["Success"] = "Đã xóa việc lặp (các công việc đã tạo vẫn giữ nguyên).";
        return RedirectToAction(nameof(Templates));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskTemplatesManage)]
    public async Task<IActionResult> Generate()
    {
        var created = await work.GenerateAsync(Today);
        TempData["Success"] = created > 0 ? $"Đã tạo {created} công việc theo kỳ." : "Không có kỳ nào cần tạo thêm công việc.";
        return RedirectToAction(nameof(Templates));
    }

    // ---- Report ------------------------------------------------------------------------------------------------------------
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TaskReportView)]
    public async Task<IActionResult> Report(DateTime? from, DateTime? to, Guid? department)
    {
        var today = Today;
        var f = from is DateTime a ? DateOnly.FromDateTime(a) : new DateOnly(today.Year, today.Month, 1);
        var t = to is DateTime b ? DateOnly.FromDateTime(b) : f.AddMonths(1).AddDays(-1);
        if (t < f) (f, t) = (t, f);
        try { return View(new WorkReportVm(await work.ReportAsync(f, t, today, department), f, t, department, await work.DepartmentsAsync(), today, null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WORK_UNAVAILABLE");
            return View(new WorkReportVm(null, f, t, department, [], today, StoreUnavailable));
        }
    }

    internal static string Message(WorkOutcome outcome) => outcome switch
    {
        WorkOutcome.Conflict => "Công việc vừa được người khác cập nhật. Vui lòng tải lại trang và thử lại.",
        WorkOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        WorkOutcome.NoEmployee => "Tài khoản chưa liên kết hồ sơ nhân viên.",
        WorkOutcome.ChecklistIncomplete => "Cần hoàn thành tất cả mục checklist trước khi chuyển sang Hoàn thành.",
        _ => "Dữ liệu không hợp lệ."
    };
}
