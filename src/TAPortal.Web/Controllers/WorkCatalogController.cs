using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Công việc > Danh mục công việc (operator 2026-11-14): the names a task may take. Staff pick from it while typing
// (Work/Edit, Work/TemplateEdit); only holders of TASKS.CATALOG.MANAGE add, rename, hide or remove entries.
[Authorize(Policy = AdminPermissions.Policy.TaskCatalogManage)]
public sealed class WorkCatalogController(IWorkCatalogStore catalog, ILogger<WorkCatalogController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try { return View(await catalog.ListAsync()); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Work catalogue unavailable.");
            ViewData["Unavailable"] = true;
            return View(Array.Empty<WorkCatalogItem>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskCatalogManage)]
    public async Task<IActionResult> Save(Guid? id, [Bind(Prefix = "Input")] WorkCatalogInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        WorkCatalogResult result;
        try { result = await catalog.SaveAsync(actor, id, input); }
        catch (AuditUnavailableException) { result = new(WorkCatalogOutcome.Invalid, "Không ghi được nhật ký audit nên chưa lưu. Liên hệ quản trị hệ thống."); }
        if (result.Outcome == WorkCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkCatalogOutcome.NotFound) return NotFound();
        if (result.Outcome == WorkCatalogOutcome.Saved) TempData["Success"] = id is null ? $"Đã thêm đầu việc \"{WorkCatalogPolicy.Clean(input.Name)}\"." : "Đã lưu đầu việc.";
        else TempData["Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.TaskCatalogManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        WorkCatalogResult result;
        try { result = await catalog.DeleteAsync(actor, id); }
        catch (AuditUnavailableException) { result = new(WorkCatalogOutcome.Invalid, "Không ghi được nhật ký audit nên chưa xóa."); }
        if (result.Outcome == WorkCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == WorkCatalogOutcome.Saved) TempData["Success"] = "Đã xóa đầu việc khỏi danh mục. Các công việc đã tạo vẫn giữ tên cũ.";
        else TempData["Error"] = result.Message ?? "Không tìm thấy đầu việc.";
        return RedirectToAction(nameof(Index));
    }
}
