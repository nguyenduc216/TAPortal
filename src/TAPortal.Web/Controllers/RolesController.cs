using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Admin → Hệ thống → Vai trò & phân quyền. Viewing needs AUTH.ROLES.VIEW; every change needs AUTH.ROLES.MANAGE (and is
// re-checked in SQL by SqlRoleAdminStore). SYS_ADMIN is read-only.
[Authorize(Policy = AdminPermissions.Policy.RolesView)]
public sealed class RolesController(IRoleAdminStore roles, ILogger<RolesController> logger) : Controller
{
    internal const string StoreUnavailable = "Không đọc được dữ liệu vai trò. Chi tiết đã ghi vào nhật ký ứng dụng.";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try { return View(new RolesIndexVm(await roles.ListAsync(), null)); }
        catch (SqlException ex) { logger.LogError(ex, "ROLES_UNAVAILABLE"); return View(new RolesIndexVm([], StoreUnavailable)); }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.RolesManage)]
    public async Task<IActionResult> Create() => View("Edit", new RoleEditVm(null, new RoleInput(), await roles.AvailablePermissionsAsync(), false));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.RolesManage)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] RoleInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (ModelState.IsValid)
        {
            var result = await roles.CreateAsync(actor, input, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (result.Outcome == RoleAdminOutcome.Forbidden) return Forbid();
            if (result.Outcome == RoleAdminOutcome.Saved)
            {
                logger.LogInformation("Role created. Actor {ActorId}, role {RoleId}, audit persisted {AuditPersisted}.", actor, result.Id, result.AuditPersisted);
                TempData["Success"] = "Đã tạo vai trò mới." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Edit), new { id = result.Id });
            }
            AddError(result.Outcome);
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new RoleEditVm(null, input, await roles.AvailablePermissionsAsync(), false));
    }

    // Read-only for viewers and for SYS_ADMIN; editable form for AUTH.ROLES.MANAGE.
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var role = await roles.GetAsync(id);
        if (role is null) return NotFound();
        return View(new RoleEditVm(role, RoleInput.From(role), await roles.AvailablePermissionsAsync(), ReadOnly(role)));
    }

    // Binds only name, description, active flag, ticked catalogue codes and the concurrency stamp (code is immutable).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.RolesManage)]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] RoleInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var reload = false;
        if (ModelState.IsValid)
        {
            var result = await roles.UpdateAsync(actor, id, input, HttpContext.Connection.RemoteIpAddress?.ToString());
            switch (result.Outcome)
            {
                case RoleAdminOutcome.NotFound: return NotFound();
                case RoleAdminOutcome.Forbidden: return Forbid();
                case RoleAdminOutcome.NoChange:
                    TempData["Success"] = "Vai trò không thay đổi.";
                    return RedirectToAction(nameof(Edit), new { id });
                case RoleAdminOutcome.Saved:
                    logger.LogInformation("Role updated. Actor {ActorId}, role {RoleId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã lưu vai trò. Quyền mới có hiệu lực từ request kế tiếp của người dùng." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Edit), new { id });
                default:
                    AddError(result.Outcome);
                    reload = result.Outcome is RoleAdminOutcome.Conflict or RoleAdminOutcome.SystemRole;
                    break;
            }
        }
        // Conflict: show the authoritative state with its new stamp. Validation errors keep the operator's input.
        var role = await roles.GetAsync(id);
        if (role is null) return NotFound();
        if (reload) input = RoleInput.From(role);
        else input.Code = role.Code;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new RoleEditVm(role, input, await roles.AvailablePermissionsAsync(), ReadOnly(role)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await roles.DeleteAsync(actor, id, stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
        switch (result.Outcome)
        {
            case RoleAdminOutcome.NotFound: return NotFound();
            case RoleAdminOutcome.Forbidden: return Forbid();
            case RoleAdminOutcome.Saved:
                logger.LogInformation("Role soft-deleted. Actor {ActorId}, role {RoleId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                TempData["Success"] = "Đã xóa (mềm) vai trò." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Index));
            default:
                TempData["Error"] = Message(result.Outcome);
                return RedirectToAction(nameof(Edit), new { id });
        }
    }

    private bool ReadOnly(RoleDetail role) => role.IsSystemAdmin || !AdminPermissions.Has(User, AdminPermissions.RolesManage);

    private void AddError(RoleAdminOutcome outcome)
    {
        var key = outcome switch
        {
            RoleAdminOutcome.InvalidCode or RoleAdminOutcome.DuplicateCode => "Input.Code",
            RoleAdminOutcome.InvalidPermission => "Input.Permissions",
            _ => string.Empty
        };
        ModelState.AddModelError(key, Message(outcome));
    }

    internal static string Message(RoleAdminOutcome outcome) => outcome switch
    {
        RoleAdminOutcome.SystemRole => "Vai trò Quản trị hệ thống (SYS_ADMIN) luôn có toàn quyền và không thể sửa, ngừng hoạt động hay xóa.",
        RoleAdminOutcome.InvalidCode => "Mã vai trò chỉ gồm chữ in hoa không dấu, chữ số và dấu gạch dưới (2–30 ký tự).",
        RoleAdminOutcome.DuplicateCode => "Mã vai trò đã tồn tại.",
        RoleAdminOutcome.InvalidPermission => "Có quyền không hợp lệ hoặc chưa được khởi tạo trong cơ sở dữ liệu (chạy migration 028).",
        RoleAdminOutcome.InUse => "Vai trò đang được gán cho người dùng: gỡ vai trò khỏi các tài khoản trước khi xóa.",
        RoleAdminOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        RoleAdminOutcome.Forbidden => "Bạn không có quyền quản lý vai trò.",
        _ => "Vai trò vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và lưu lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";
}
