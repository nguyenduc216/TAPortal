using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Admin → Nhân sự → Phòng ban / Nhóm / Chức danh. Each controller declares its own HR.* permissions on every action
// (SafeAdminPermissionTests) and delegates to HrCatalogPages; the SQL store re-checks the permission in its transaction.
[Authorize(Policy = AdminPermissions.Policy.DepartmentsView)]
public sealed class DepartmentsController(IHrCatalogStore store, ILogger<DepartmentsController> logger) : Controller
{
    private HrCatalogPages Pages => new(this, store, HrCatalogs.Department, logger);

    [HttpGet] public Task<IActionResult> Index() => Pages.Index();

    [HttpGet, Authorize(Policy = AdminPermissions.Policy.DepartmentsCreate)]
    public Task<IActionResult> Create() => Pages.CreateForm();

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.DepartmentsCreate)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Create(input);

    [HttpGet] public Task<IActionResult> Edit(Guid id) => Pages.EditForm(id);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.DepartmentsUpdate)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Edit(id, input);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.DepartmentsDelete)]
    public Task<IActionResult> Delete(Guid id, [FromForm] string? stamp) => Pages.Delete(id, stamp);
}

[Authorize(Policy = AdminPermissions.Policy.TeamsView)]
public sealed class TeamsController(IHrCatalogStore store, ILogger<TeamsController> logger) : Controller
{
    private HrCatalogPages Pages => new(this, store, HrCatalogs.Team, logger);

    [HttpGet] public Task<IActionResult> Index() => Pages.Index();

    [HttpGet, Authorize(Policy = AdminPermissions.Policy.TeamsCreate)]
    public Task<IActionResult> Create() => Pages.CreateForm();

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.TeamsCreate)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Create(input);

    [HttpGet] public Task<IActionResult> Edit(Guid id) => Pages.EditForm(id);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.TeamsUpdate)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Edit(id, input);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.TeamsDelete)]
    public Task<IActionResult> Delete(Guid id, [FromForm] string? stamp) => Pages.Delete(id, stamp);
}

[Authorize(Policy = AdminPermissions.Policy.JobTitlesView)]
public sealed class JobTitlesController(IHrCatalogStore store, ILogger<JobTitlesController> logger) : Controller
{
    private HrCatalogPages Pages => new(this, store, HrCatalogs.JobTitle, logger);

    [HttpGet] public Task<IActionResult> Index() => Pages.Index();

    [HttpGet, Authorize(Policy = AdminPermissions.Policy.JobTitlesCreate)]
    public Task<IActionResult> Create() => Pages.CreateForm();

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.JobTitlesCreate)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Create(input);

    [HttpGet] public Task<IActionResult> Edit(Guid id) => Pages.EditForm(id);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.JobTitlesUpdate)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] HrCatalogInput input) => Pages.Edit(id, input);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = AdminPermissions.Policy.JobTitlesDelete)]
    public Task<IActionResult> Delete(Guid id, [FromForm] string? stamp) => Pages.Delete(id, stamp);
}

// Shared page logic of the three catalogue controllers (not a controller: it has no routes or authorization of its own).
public sealed class HrCatalogPages(Controller c, IHrCatalogStore store, HrCatalogInfo info, ILogger logger)
{
    private const string IndexView = "~/Views/HrCatalog/Index.cshtml";
    private const string EditView = "~/Views/HrCatalog/Edit.cshtml";

    public async Task<IActionResult> Index()
    {
        try { return c.View(IndexView, new HrCatalogIndexVm(info, await store.ListAsync(info.Kind), null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "HR_CATALOG_UNAVAILABLE {Kind}", info.Kind);
            return c.View(IndexView, new HrCatalogIndexVm(info, null, EmployeesController.StoreUnavailable));
        }
    }

    public async Task<IActionResult> CreateForm()
    {
        var branches = await store.BranchesAsync();
        var input = new HrCatalogInput { BranchId = branches.Count(b => b.IsActive) == 1 ? branches.First(b => b.IsActive).Id : null };
        return c.View(EditView, new HrCatalogEditVm(info, null, input, branches, await store.DepartmentsAsync(), false));
    }

    public async Task<IActionResult> Create(HrCatalogInput input)
    {
        if (UsersController.ActorId(c.User) is not Guid actor) return c.Forbid();
        if (HrCatalogPolicy.NormalizeCode(input.Code).Length == 0) c.ModelState.AddModelError("Input.Code", "Vui lòng nhập mã.");
        if (info.Kind != HrCatalogKind.JobTitle && input.BranchId is null) c.ModelState.AddModelError("Input.BranchId", "Vui lòng chọn chi nhánh.");
        if (c.ModelState.IsValid)
        {
            var result = await store.CreateAsync(info.Kind, actor, input, Ip);
            if (result.Outcome == HrCatalogOutcome.Forbidden) return c.Forbid();
            if (result.Outcome == HrCatalogOutcome.Saved)
            {
                logger.LogInformation("HR catalogue {Kind} created. Actor {ActorId}, id {Id}, audit persisted {AuditPersisted}.", info.Kind, actor, result.Id, result.AuditPersisted);
                c.TempData["Success"] = $"Đã thêm {info.Singular}." + AuditNotice(result.AuditPersisted);
                return c.RedirectToAction("Index");
            }
            c.ModelState.AddModelError(result.Outcome is HrCatalogOutcome.DuplicateCode or HrCatalogOutcome.InvalidCode ? "Input.Code" : string.Empty, Message(result.Outcome));
        }
        c.Response.StatusCode = StatusCodes.Status400BadRequest;
        return c.View(EditView, new HrCatalogEditVm(info, null, input, await store.BranchesAsync(), await store.DepartmentsAsync(), false));
    }

    // Read-only for accounts without the update permission.
    public async Task<IActionResult> EditForm(Guid id)
    {
        var item = await store.GetAsync(info.Kind, id);
        if (item is null) return c.NotFound();
        return c.View(EditView, new HrCatalogEditVm(info, item, HrCatalogInput.From(item), await store.BranchesAsync(), await store.DepartmentsAsync(), !AdminPermissions.Has(c.User, info.UpdatePermission)));
    }

    public async Task<IActionResult> Edit(Guid id, HrCatalogInput input)
    {
        if (UsersController.ActorId(c.User) is not Guid actor) return c.Forbid();
        c.ModelState.Remove("Input.Code");
        var reload = false;
        if (c.ModelState.IsValid)
        {
            var result = await store.UpdateAsync(info.Kind, actor, id, input, Ip);
            switch (result.Outcome)
            {
                case HrCatalogOutcome.NotFound: return c.NotFound();
                case HrCatalogOutcome.Forbidden: return c.Forbid();
                case HrCatalogOutcome.NoChange:
                    c.TempData["Success"] = "Không có thay đổi.";
                    return c.RedirectToAction("Index");
                case HrCatalogOutcome.Saved:
                    logger.LogInformation("HR catalogue {Kind} updated. Actor {ActorId}, id {Id}, audit persisted {AuditPersisted}.", info.Kind, actor, id, result.AuditPersisted);
                    c.TempData["Success"] = $"Đã lưu {info.Singular}." + AuditNotice(result.AuditPersisted);
                    return c.RedirectToAction("Index");
                default:
                    c.ModelState.AddModelError(string.Empty, Message(result.Outcome));
                    reload = result.Outcome == HrCatalogOutcome.Conflict;
                    break;
            }
        }
        var item = await store.GetAsync(info.Kind, id);
        if (item is null) return c.NotFound();
        if (reload) input = HrCatalogInput.From(item);
        else { input.Code = item.Code; input.BranchId = item.BranchId; input.Stamp = item.Stamp; }
        c.Response.StatusCode = StatusCodes.Status400BadRequest;
        return c.View(EditView, new HrCatalogEditVm(info, item, input, await store.BranchesAsync(), await store.DepartmentsAsync(), false));
    }

    public async Task<IActionResult> Delete(Guid id, string? stamp)
    {
        if (UsersController.ActorId(c.User) is not Guid actor) return c.Forbid();
        var result = await store.DeleteAsync(info.Kind, actor, id, stamp, Ip);
        switch (result.Outcome)
        {
            case HrCatalogOutcome.NotFound: return c.NotFound();
            case HrCatalogOutcome.Forbidden: return c.Forbid();
            case HrCatalogOutcome.Saved:
                logger.LogInformation("HR catalogue {Kind} soft-deleted. Actor {ActorId}, id {Id}, audit persisted {AuditPersisted}.", info.Kind, actor, id, result.AuditPersisted);
                c.TempData["Success"] = $"Đã xóa (mềm) {info.Singular}." + AuditNotice(result.AuditPersisted);
                return c.RedirectToAction("Index");
            default:
                c.TempData["Error"] = Message(result.Outcome);
                return c.RedirectToAction("Edit", new { id });
        }
    }

    private string? Ip => c.HttpContext.Connection.RemoteIpAddress?.ToString();

    private string Message(HrCatalogOutcome outcome) => outcome switch
    {
        HrCatalogOutcome.InvalidCode => $"Mã chỉ gồm chữ in hoa không dấu, chữ số, dấu chấm, gạch ngang, gạch dưới (tối đa {info.CodeLength} ký tự).",
        HrCatalogOutcome.DuplicateCode => info.Kind == HrCatalogKind.Team ? "Mã nhóm đã tồn tại trong chi nhánh này." : $"Mã {info.Singular} đã tồn tại.",
        HrCatalogOutcome.InvalidReference => "Chi nhánh hoặc phòng ban không hợp lệ: phòng ban của nhóm phải thuộc cùng chi nhánh và đang hoạt động.",
        HrCatalogOutcome.InUse => info.Kind == HrCatalogKind.Department
            ? "Không thể xóa: phòng ban còn nhân viên đang làm việc hoặc còn nhóm đang hoạt động. Chuyển nhân viên/nhóm sang nơi khác hoặc ngừng hoạt động phòng ban thay vì xóa."
            : $"Không thể xóa: {info.Singular} còn nhân viên đang làm việc được phân công. Chuyển nhân viên sang nơi khác hoặc ngừng hoạt động thay vì xóa.",
        HrCatalogOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        HrCatalogOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        _ => "Dữ liệu vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và lưu lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";
}
