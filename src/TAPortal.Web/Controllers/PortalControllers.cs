using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

[Authorize(Policy = "SystemAdminOnly")]
public sealed class HomeController(IDashboardStore dashboard, AdminAuditOptions audit, AdminLoginThrottleOptions throttle, ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index()
    {
        DashboardSnapshot? data = null;
        try
        {
            data = await dashboard.LoadAsync();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            // Never show exception details; the page renders an explicit "data unavailable" state instead of zeros.
            logger.LogError(ex, "Dashboard data could not be loaded.");
        }
        return View(new DashboardPageVm(data, audit.RequireDurableAudit, throttle.AccountPermitLimit, throttle.IpPermitLimit, throttle.Window));
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorVm(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }
}

[Authorize(Policy = "SystemAdminOnly")]
public sealed class UsersController(PortalDb db, PasswordService passwords, IUserRoleStore roleStore, IUserAccountStore accounts, ILogger<UsersController> logger) : Controller
{
    public async Task<IActionResult> Index() => await IndexView();

    // The list view needs the role catalogue (create modal) and each user's role names; loaded here instead of in the view.
    private async Task<IActionResult> IndexView()
    {
        var users = await db.GetUsersAsync();
        ViewData["Roles"] = await db.GetRolesAsync();
        ViewData["RoleMap"] = await db.GetUserRoleMapAsync();
        return View(nameof(Index), users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.CREATE")]
    public async Task<IActionResult> Create(UserCreateVm vm)
    {
        var policy = PasswordPolicy.Validate(vm.Password);
        if (!policy.IsValid) ModelState.AddModelError(nameof(vm.Password), policy.Error);
        if (!string.IsNullOrWhiteSpace(vm.Username) && await db.UsernameExistsAsync(vm.Username)) ModelState.AddModelError(nameof(vm.Username), "Tên đăng nhập đã tồn tại.");
        if (ModelState.IsValid && await db.EmailInUseAsync(vm.Email, null)) ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác.");
        if (ModelState.IsValid)
        {
            try
            {
                await db.CreateUserAsync(vm, passwords.Hash(vm.Password), ActorId(User));
                TempData["Success"] = "Đã tạo người dùng mới.";
                return RedirectToAction(nameof(Index));
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập/email đã tồn tại hoặc vai trò không hợp lệ.");
            }
            catch (AuditUnavailableException)
            {
                ModelState.AddModelError(string.Empty, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên tài khoản chưa được tạo. Liên hệ quản trị cơ sở dữ liệu.");
            }
        }
        // Re-render instead of redirecting so the entered values (except the password) are kept and the modal reopens.
        Response.StatusCode = StatusCodes.Status400BadRequest;
        ViewData["CreateUser"] = vm;
        ViewData["CreateUserErrors"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        return await IndexView();
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ViewBag.Account = account;
        return View(new UserEditVm { Id = account.Id, Username = account.Username, DisplayName = account.DisplayName, Email = account.Email, Phone = account.Phone, Status = account.Status, IsActive = account.IsActive, Stamp = account.Stamp });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, UserEditVm vm)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        vm.Id = id;
        if (ModelState.IsValid)
        {
            var result = await accounts.UpdateAsync(actor, vm, vm.Stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
            switch (result.Outcome)
            {
                case AccountChangeOutcome.Updated:
                    LogAudit("updated", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã cập nhật thông tin người dùng." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Index));
                case AccountChangeOutcome.NotFound: return NotFound();
                case AccountChangeOutcome.Forbidden: return Forbid();
                case AccountChangeOutcome.DuplicateEmail: ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác."); break;
                case AccountChangeOutcome.SelfDeactivation: ModelState.AddModelError(nameof(vm.IsActive), "Không thể tự vô hiệu hóa tài khoản đang đăng nhập."); break;
                default: ModelState.AddModelError(string.Empty, AccountMessage(result.Outcome)); break;
            }
        }
        // Reload the authoritative state; a stale stamp is replaced so the next save compares against current data.
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ViewBag.Account = account;
        vm.Username = account.Username;
        vm.Status = account.Status;
        vm.Stamp = account.Stamp;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.DELETE")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await accounts.GetAsync(id);
        return account is null ? NotFound() : View(account);
    }

    // Soft delete only. Binds just the concurrency stamp.
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.DELETE")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, [FromForm] string? stamp)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        var result = await accounts.SoftDeleteAsync(actor, id, stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
        switch (result.Outcome)
        {
            case AccountChangeOutcome.Updated:
                LogAudit("soft-deleted", actor, id, result.AuditPersisted);
                TempData["Success"] = "Đã xóa (mềm) người dùng. Tài khoản không thể đăng nhập; dữ liệu lịch sử được giữ nguyên." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Index));
            case AccountChangeOutcome.NotFound: return NotFound();
            case AccountChangeOutcome.Forbidden: return Forbid();
        }
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ModelState.AddModelError(string.Empty, AccountMessage(result.Outcome));
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Delete), account);
    }

    private static string AccountMessage(AccountChangeOutcome outcome) => outcome switch
    {
        AccountChangeOutcome.SelfDeletion => "Không thể tự xóa tài khoản đang đăng nhập.",
        AccountChangeOutcome.SelfDeactivation => "Không thể tự vô hiệu hóa tài khoản đang đăng nhập.",
        AccountChangeOutcome.LastSystemAdmin => "Không thể vô hiệu hóa hoặc xóa quản trị viên hệ thống (SYS_ADMIN) hoạt động cuối cùng.",
        AccountChangeOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        AccountChangeOutcome.DuplicateEmail => "Email đã được sử dụng bởi tài khoản khác.",
        _ => "Người dùng vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và thực hiện lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";

    private void LogAudit(string action, Guid actor, Guid target, bool persisted)
    {
        logger.LogInformation("User {Action}. Actor {ActorId}, target {UserId}, audit persisted {AuditPersisted}.", action, actor, target, persisted);
        if (!persisted) logger.LogWarning("dbo.AuditLogs unavailable; user change for {UserId} recorded only in application logs.", target);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.MANAGE")]
    public async Task<IActionResult> Roles(Guid id)
    {
        var snapshot = await roleStore.GetAsync(id);
        return snapshot is null ? NotFound() : View(snapshot);
    }

    // Binds only the selected role ids and the concurrency stamp; every other posted field is ignored.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.MANAGE")]
    public async Task<IActionResult> Roles(Guid id, [FromForm] List<Guid>? roleIds, [FromForm] string? stamp)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        if (!ModelState.IsValid)
            return await RolesError(id, "Danh sách vai trò gửi lên không hợp lệ.");
        var result = await roleStore.UpdateAsync(actor, id, (roleIds ?? []).Distinct().ToList(), stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
        switch (result.Outcome)
        {
            case RoleChangeOutcome.NotFound:
                return NotFound();
            case RoleChangeOutcome.Forbidden:
                return Forbid();
            case RoleChangeOutcome.NoChange:
                TempData["Success"] = "Vai trò không thay đổi.";
                return RedirectToAction(nameof(Roles), new { id });
            case RoleChangeOutcome.Updated:
                logger.LogInformation("User roles updated. Actor {ActorId}, target {UserId}, added {Added}, removed {Removed}, audit persisted {AuditPersisted}.", actor, id, string.Join(",", result.Added ?? []), string.Join(",", result.Removed ?? []), result.AuditPersisted);
                if (!result.AuditPersisted)
                    logger.LogWarning("dbo.AuditLogs unavailable; role change for {UserId} recorded only in application logs.", id);
                TempData["Success"] = "Đã cập nhật vai trò. Quyền mới có hiệu lực từ request kế tiếp của người dùng." + (result.AuditPersisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).");
                return RedirectToAction(nameof(Roles), new { id });
            case RoleChangeOutcome.InvalidRole:
                return await RolesError(id, "Vai trò không hợp lệ hoặc đã ngừng hoạt động.");
            case RoleChangeOutcome.LastSystemAdmin:
                return await RolesError(id, "Không thể thu hồi vai trò Quản trị hệ thống (SYS_ADMIN) của quản trị viên hoạt động cuối cùng.");
            case RoleChangeOutcome.AuditUnavailable:
                return await RolesError(id, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi vai trò đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.");
            default:
                return await RolesError(id, "Vai trò của người dùng vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và lưu lại.");
        }
    }

    private async Task<IActionResult> RolesError(Guid id, string message)
    {
        var snapshot = await roleStore.GetAsync(id);
        if (snapshot is null) return NotFound();
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Roles), snapshot);
    }

    internal static Guid? ActorId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

[Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.VIEW")]
public sealed class RolesController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetRolesAsync());
}

[Authorize(Policy = "SystemAdminOnly")]
public sealed class CustomersController(PortalDb db, ICustomerAdminStore customers) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetCustomersAsync());

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.CREATE")]
    public IActionResult Create() => View("Edit", new CustomerEditVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.CREATE")]
    public async Task<IActionResult> Create(CustomerEditVm vm)
    {
        vm.Id = Guid.Empty;
        if (ModelState.IsValid && Apply(await customers.CreateAsync(vm, UsersController.ActorId(User)), vm))
        {
            TempData["Success"] = "Đã tạo khách hàng mới.";
            return RedirectToAction(nameof(Index));
        }
        vm.Id = Guid.Empty;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var vm = await customers.GetAsync(id);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, CustomerEditVm vm)
    {
        var current = await customers.GetAsync(id);
        if (current is null) return NotFound();
        vm.Id = id;
        vm.Code = current.Code;
        vm.Status = current.Status;
        ModelState.Remove(nameof(vm.Code));
        if (ModelState.IsValid)
        {
            var result = await customers.UpdateAsync(vm, UsersController.ActorId(User));
            if (result.Outcome == CustomerChangeOutcome.NotFound) return NotFound();
            if (Apply(result, vm))
            {
                TempData["Success"] = "Đã cập nhật thông tin khách hàng.";
                return RedirectToAction(nameof(Index));
            }
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }

    private bool Apply(CustomerChangeResult result, CustomerEditVm vm)
    {
        switch (result.Outcome)
        {
            case CustomerChangeOutcome.Saved: return true;
            case CustomerChangeOutcome.DuplicateCode: ModelState.AddModelError(nameof(vm.Code), "Mã khách hàng đã tồn tại."); break;
            case CustomerChangeOutcome.InvalidTaxCode: ModelState.AddModelError(nameof(vm.TaxCode), "Mã số thuế phải gồm 10 chữ số, hoặc 10 chữ số + \"-\" + 3 chữ số đối với đơn vị phụ thuộc/chi nhánh (ví dụ 0101234567-001)."); break;
            case CustomerChangeOutcome.AuditUnavailable: ModelState.AddModelError(string.Empty, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu."); break;
            case CustomerChangeOutcome.DuplicateTaxCode: ModelState.AddModelError(nameof(vm.TaxCode), "Mã số thuế đã được sử dụng bởi khách hàng khác" + (result.ConflictingCustomerCode is null ? "." : $" (mã KH {result.ConflictingCustomerCode}).")); break;
            default: ModelState.AddModelError(string.Empty, "Không thể lưu khách hàng."); break;
        }
        return false;
    }
}

[Authorize(Policy = PermissionPolicies.Prefix + "SYSTEM.MENUS.MANAGE")]
public sealed class MenusController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetMenusAsync());
}
