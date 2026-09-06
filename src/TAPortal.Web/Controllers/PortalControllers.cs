using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

[Authorize]
public sealed class HomeController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetDashboardAsync());
}

[Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.VIEW")]
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
        if (await db.UsernameExistsAsync(vm.Username)) ModelState.AddModelError(nameof(vm.Username), "Tên đăng nhập đã tồn tại.");
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        Guid? createdBy = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        await db.CreateUserAsync(vm, passwords.Hash(vm.Password), createdBy);
        TempData["Success"] = "Đã tạo người dùng mới.";
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.VIEW")]
public sealed class RolesController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetRolesAsync());
}

[Authorize]
public sealed class CustomersController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetCustomersAsync());
}

[Authorize(Policy = PermissionPolicies.Prefix + "SYSTEM.MENUS.MANAGE")]
public sealed class MenusController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetMenusAsync());
}
