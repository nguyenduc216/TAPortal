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
    public async Task<IActionResult> Details(Guid id, [FromServices] IAuthorizationService authz, [FromServices] IWorkDocumentStore docs, [FromServices] IServicePeriodStore periods)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var detail = await work.GetAsync(actor, id);
        if (detail is null) return NotFound();
        var canCatalog = (await authz.AuthorizeAsync(User, AdminPermissions.Policy.TaskCatalogManage)).Succeeded;
        return View(new WorkDetailsVm(detail, Today, null, canCatalog, await docs.ListAsync(id), actor, await periods.QuantitiesAsync(id)));
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
    public Task<IActionResult> Status(Guid id, string status, string? note, string? returnUrl) => Quick(id, returnUrl, actor => work.SetStatusAsync(actor, id, status ?? "", note));

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
    public Task<IActionResult> ChecklistOrder(Guid id, Guid[] items, string? returnUrl) => Quick(id, returnUrl, actor => work.OrderChecklistAsync(actor, id, items ?? []));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public Task<IActionResult> Comment(Guid id, string body, string? returnUrl) => Quick(id, returnUrl, actor => work.CommentAsync(actor, id, body ?? ""));

    // ---- Tài liệu (operator 2026-11-16, migration 058) -------------------------------------------------------------------
    // Anyone who sees the task adds and reads its documents; the uploader or a task editor changes the note or removes one.
    private const long DocumentUploadLimit = WorkDocumentPolicy.MaxBytes + 1024 * 1024;

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    [RequestSizeLimit(DocumentUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentUploadLimit)]
    public async Task<IActionResult> DocumentUpload(Guid id, IFormFile? file, string? note, [FromServices] IWorkDocumentStore docs)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var task = await work.GetAsync(actor, id);
        if (task is null) return NotFound();
        if (task.Task.Status == WorkTaskPolicy.Cancelled) return DocumentsBack(id, "Công việc đã hủy, không thêm tài liệu được.");
        if (file is null || file.Length == 0) return DocumentsBack(id, "Chọn file cần tải lên.");
        if (file.Length > WorkDocumentPolicy.MaxBytes) return DocumentsBack(id, "File vượt quá 10 MB.");
        byte[] data;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            data = buffer.ToArray();
        }
        var (type, error) = WorkDocumentPolicy.CheckFile(file.FileName, data);
        if (type is null) return DocumentsBack(id, error ?? "File không hợp lệ.");
        var name = ContractPolicy.SafeFileName(file.FileName);
        try { error = await docs.AddAsync(actor, id, name, type, data, WorkDocumentPolicy.Note(note)); }
        catch (SqlException ex) { logger.LogError(ex, "WORK_DOCUMENT_UPLOAD_FAILED task {TaskId}", id); error = StoreUnavailable; }
        if (error is null) logger.LogInformation("Work document added. Actor {ActorId}, task {TaskId}, {Size} bytes.", actor, id, data.Length);
        return DocumentsBack(id, error, error is null ? $"Đã tải lên \"{name}\"." : null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> DocumentNote(Guid id, Guid doc, string? note, [FromServices] IWorkDocumentStore docs)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (await DocumentAccess(actor, id, doc, docs) is not { } allowed) return NotFound();
        if (!allowed) return Forbid();
        var saved = await docs.SetNoteAsync(actor, id, doc, WorkDocumentPolicy.Note(note));
        return DocumentsBack(id, saved ? null : "Không tìm thấy tài liệu.", saved ? "Đã lưu ghi chú tài liệu." : null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> DocumentDelete(Guid id, Guid doc, [FromServices] IWorkDocumentStore docs)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (await DocumentAccess(actor, id, doc, docs) is not { } allowed) return NotFound();
        if (!allowed) return Forbid();
        var deleted = await docs.DeleteAsync(actor, id, doc);
        return DocumentsBack(id, deleted ? null : "Không tìm thấy tài liệu.", deleted ? "Đã xóa tài liệu." : null);
    }

    // Download (attachment, nosniff); ?view=1 shows an image inline (the only type rendered in the page).
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> Document(Guid id, Guid doc, bool view, [FromServices] IWorkDocumentStore docs)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (await work.GetAsync(actor, id) is null) return NotFound();
        var file = await docs.ContentAsync(id, doc);
        if (file is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, max-age=600";
        if (view && file.ContentType.StartsWith("image/", StringComparison.Ordinal)) return File(file.Data, file.ContentType);
        return File(file.Data, file.ContentType, ContractPolicy.SafeFileName(file.FileName));
    }

    // null: task or document not found for this actor; false: seen but neither uploader nor task editor.
    private async Task<bool?> DocumentAccess(Guid actor, Guid id, Guid doc, IWorkDocumentStore docs)
    {
        var task = await work.GetAsync(actor, id);
        if (task is null) return null;
        var found = (await docs.ListAsync(id))?.FirstOrDefault(d => d.Id == doc);
        if (found is null) return null;
        return task.CanEdit || found.CreatedBy == actor;
    }

    private IActionResult DocumentsBack(Guid id, string? error, string? success = null) => TabBack(id, "documents", error, success);

    private IActionResult TabBack(Guid id, string tab, string? error, string? success = null)
    {
        if (error is not null) TempData["Error"] = error;
        if (success is not null) TempData["Success"] = success;
        TempData["TaskTab"] = tab;
        return LocalRedirect(Url.Action(nameof(Details), new { id })! + "#" + tab);
    }

    // ---- Số lượng (operator 2026-11-16, migration 065) --------------------------------------------------------------------
    // Whoever sees the task enters quantities in its unit, as many times as needed until it is done (Kéo hóa đơn: +10 today,
    // +20 tomorrow...); every entry is kept and logged. An entry is removed by its author or a task editor.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> QuantityAdd(Guid id, DateTime? date, string? quantity, string? note, [FromServices] IServicePeriodStore periods)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var task = await work.GetAsync(actor, id);
        if (task is null) return NotFound();
        if (task.Task.Status is WorkTaskPolicy.Done or WorkTaskPolicy.Cancelled)
            return TabBack(id, "quantities", "Công việc đã hoàn thành hoặc đã hủy, không nhập thêm số lượng. Mở lại công việc nếu cần nhập.");
        var (value, error) = ServicePeriodPolicy.ParseQuantity(quantity);
        if (value is null) return TabBack(id, "quantities", error);
        var day = date is DateTime d ? DateOnly.FromDateTime(d) : Today;
        if (day > Today) return TabBack(id, "quantities", "Ngày nhập không được sau hôm nay.");
        if (day < Today.AddYears(-1)) return TabBack(id, "quantities", "Ngày nhập quá xa.");
        var text = ServicePricingPolicy.CleanText(note, ServicePeriodPolicy.QuantityNoteMax);
        try { error = await periods.AddQuantityAsync(actor, id, day, value.Value, task.Service.Unit, text); }
        catch (SqlException ex) { logger.LogError(ex, "WORK_QUANTITY_FAILED task {TaskId}", id); error = StoreUnavailable; }
        return TabBack(id, "quantities", error, error is null ? $"Đã nhập {ServicePeriodPolicy.Number(value.Value)}{(task.Service.Unit is null ? "" : " " + task.Service.Unit)}." : null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TasksView)]
    public async Task<IActionResult> QuantityDelete(Guid id, Guid entry, [FromServices] IServicePeriodStore periods)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var task = await work.GetAsync(actor, id);
        if (task is null) return NotFound();
        string? error;
        try { error = await periods.DeleteQuantityAsync(actor, id, entry, task.CanEdit); }
        catch (SqlException ex) { logger.LogError(ex, "WORK_QUANTITY_DELETE_FAILED task {TaskId}", id); error = StoreUnavailable; }
        return TabBack(id, "quantities", error, error is null ? "Đã xóa lần nhập." : null);
    }

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
