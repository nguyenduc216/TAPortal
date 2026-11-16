using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

public sealed record ServiceCatalogVm(ServiceCatalog Catalog, string Tab, bool Unavailable);
public sealed record ServiceEditVm(Guid? Id, ServiceInput Input, IReadOnlyList<ServiceCategory> Categories, IReadOnlyList<Industry> Industries, string? Error);

// Hợp đồng > Danh mục dịch vụ (operator 2026-11-16, migration 061): service groups, services with their scope of work,
// fee rules and pricing tiers, and industries (set on customers). CONTRACTS.SERVICES.MANAGE only.
[Authorize(Policy = AdminPermissions.Policy.ServiceCatalogManage)]
public sealed class ServiceCatalogController(IServiceCatalogStore store, ILogger<ServiceCatalogController> logger) : Controller
{
    public const string ServicesTab = "services", CategoriesTab = "categories", IndustriesTab = "industries";

    [HttpGet]
    public async Task<IActionResult> Index(string? tab)
    {
        ViewData["Title"] = "Danh mục dịch vụ";
        tab = tab is CategoriesTab or IndustriesTab ? tab : ServicesTab;
        try { return View(new ServiceCatalogVm(await store.ListAsync(), tab, false)); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Service catalogue unavailable.");
            return View(new ServiceCatalogVm(new([], [], []), tab, true));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid? category)
    {
        var catalog = await store.ListAsync();
        if (catalog.Categories.Count == 0)
        {
            TempData["Error"] = "Thêm nhóm dịch vụ trước (tab Nhóm dịch vụ).";
            return RedirectToAction(nameof(Index), new { tab = CategoriesTab });
        }
        var input = new ServiceInput { CategoryId = category ?? catalog.Categories[0].Id, FeeLabel = "Phí dịch vụ", FeeUnit = "đồng/tháng" };
        return EditView(null, input, catalog, null);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var service = await store.GetServiceAsync(id);
        if (service is null) return NotFound();
        return EditView(id, ServiceInput.From(service), await store.ListAsync(), null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid? id, [Bind(Prefix = "Input")] ServiceInput input, string? next)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ServiceCatalogResult result;
        try { result = await store.SaveServiceAsync(actor, id, input); }
        catch (AuditUnavailableException) { result = new(ServiceCatalogOutcome.Invalid, "Không ghi được nhật ký audit nên chưa lưu. Liên hệ quản trị hệ thống."); }
        if (result.Outcome == ServiceCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == ServiceCatalogOutcome.NotFound) return NotFound();
        if (result.Outcome == ServiceCatalogOutcome.Invalid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return EditView(id, input, await store.ListAsync(), result.Message);
        }
        TempData["Success"] = id is null ? $"Đã thêm dịch vụ \"{input.Name.Trim()}\"." : "Đã lưu dịch vụ.";
        return next == "list" ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Edit), new { id = result.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await Guarded(() => store.DeleteServiceAsync(actor, id));
        if (result.Outcome == ServiceCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == ServiceCatalogOutcome.Saved) TempData["Success"] = "Đã xóa dịch vụ.";
        else TempData["Error"] = result.Message ?? "Không tìm thấy dịch vụ.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveCategory(Guid? id, [Bind(Prefix = "Entry")] CatalogEntryInput entry) => SaveEntry(CatalogKind.Category, id, entry);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SaveIndustry(Guid? id, [Bind(Prefix = "Entry")] CatalogEntryInput entry) => SaveEntry(CatalogKind.Industry, id, entry);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeleteCategory(Guid id) => DeleteEntry(CatalogKind.Category, id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DeleteIndustry(Guid id) => DeleteEntry(CatalogKind.Industry, id);

    private async Task<IActionResult> SaveEntry(CatalogKind kind, Guid? id, CatalogEntryInput entry)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await Guarded(() => store.SaveEntryAsync(actor, kind, id, entry));
        if (result.Outcome == ServiceCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == ServiceCatalogOutcome.Saved) TempData["Success"] = id is null ? $"Đã thêm \"{entry.Name.Trim()}\"." : "Đã lưu.";
        else TempData["Error"] = result.Message ?? "Không tìm thấy mục cần sửa.";
        return RedirectToAction(nameof(Index), new { tab = kind == CatalogKind.Category ? CategoriesTab : IndustriesTab });
    }

    private async Task<IActionResult> DeleteEntry(CatalogKind kind, Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await Guarded(() => store.DeleteEntryAsync(actor, kind, id));
        if (result.Outcome == ServiceCatalogOutcome.Forbidden) return Forbid();
        if (result.Outcome == ServiceCatalogOutcome.Saved) TempData["Success"] = "Đã xóa.";
        else TempData["Error"] = result.Message ?? "Không tìm thấy mục cần xóa.";
        return RedirectToAction(nameof(Index), new { tab = kind == CatalogKind.Category ? CategoriesTab : IndustriesTab });
    }

    private static async Task<ServiceCatalogResult> Guarded(Func<Task<ServiceCatalogResult>> act)
    {
        try { return await act(); }
        catch (AuditUnavailableException) { return new(ServiceCatalogOutcome.Invalid, "Không ghi được nhật ký audit nên chưa lưu. Liên hệ quản trị hệ thống."); }
    }

    private ViewResult EditView(Guid? id, ServiceInput input, ServiceCatalog catalog, string? error)
    {
        ViewData["Title"] = id is null ? "Thêm dịch vụ" : "Sửa dịch vụ";
        ViewData["ParentTitle"] = "Danh mục dịch vụ";
        ViewData["ParentController"] = "ServiceCatalog";
        return View("Edit", new ServiceEditVm(id, input, catalog.Categories, catalog.Industries.Where(i => i.IsActive).ToList(), error));
    }
}
