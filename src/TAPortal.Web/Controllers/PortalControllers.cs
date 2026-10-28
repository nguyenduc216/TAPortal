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
public sealed class HomeController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetDashboardAsync());

    [AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorVm(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }
}

[Authorize(Policy = "SystemAdminOnly")]
public sealed class UsersController(PortalDb db, PasswordService passwords, IUserRoleStore roleStore, ILogger<UsersController> logger) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetUsersAsync());

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
        }
        // Re-render instead of redirecting so the entered values (except the password) are kept and the modal reopens.
        Response.StatusCode = StatusCodes.Status400BadRequest;
        ViewData["CreateUser"] = vm;
        ViewData["CreateUserErrors"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        return View(nameof(Index), await db.GetUsersAsync());
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var vm = await db.GetUserForEditAsync(id);
        if (vm is null) return NotFound();
        ViewBag.Roles = await db.GetUserRoleNamesAsync(id);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, UserEditVm vm)
    {
        var current = await db.GetUserForEditAsync(id);
        if (current is null) return NotFound();
        vm.Id = id;
        vm.Username = current.Username;
        vm.Status = current.Status;
        if (id == ActorId(User) && !vm.IsActive) ModelState.AddModelError(nameof(vm.IsActive), "Không thể tự vô hiệu hóa tài khoản đang đăng nhập.");
        if (ModelState.IsValid && await db.EmailInUseAsync(vm.Email, id)) ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác.");
        if (ModelState.IsValid)
        {
            try
            {
                if (!await db.UpdateUserAsync(vm, ActorId(User))) return NotFound();
                TempData["Success"] = "Đã cập nhật thông tin người dùng.";
                return RedirectToAction(nameof(Index));
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác.");
            }
        }
        ViewBag.Roles = await db.GetUserRoleNamesAsync(id);
        return View(vm);
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
public sealed class CustomersController(PortalDb db) : Controller
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
        if (ModelState.IsValid && await db.CustomerCodeExistsAsync(vm.Code)) ModelState.AddModelError(nameof(vm.Code), "Mã khách hàng đã tồn tại.");
        if (ModelState.IsValid)
        {
            try
            {
                await db.CreateCustomerAsync(vm, UsersController.ActorId(User));
                TempData["Success"] = "Đã tạo khách hàng mới.";
                return RedirectToAction(nameof(Index));
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                ModelState.AddModelError(nameof(vm.Code), "Mã khách hàng đã tồn tại.");
            }
        }
        return View("Edit", vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var vm = await db.GetCustomerForEditAsync(id);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, CustomerEditVm vm)
    {
        var current = await db.GetCustomerForEditAsync(id);
        if (current is null) return NotFound();
        vm.Id = id;
        vm.Code = current.Code;
        vm.Status = current.Status;
        ModelState.Remove(nameof(vm.Code));
        if (ModelState.IsValid)
        {
            if (!await db.UpdateCustomerAsync(vm, UsersController.ActorId(User))) return NotFound();
            TempData["Success"] = "Đã cập nhật thông tin khách hàng.";
            return RedirectToAction(nameof(Index));
        }
        return View(vm);
    }
}

[Authorize(Policy = PermissionPolicies.Prefix + "SYSTEM.MENUS.MANAGE")]
public sealed class MenusController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetMenusAsync());
}
