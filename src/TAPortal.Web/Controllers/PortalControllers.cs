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
public sealed class UsersController(PortalDb db, PasswordService passwords) : Controller
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
