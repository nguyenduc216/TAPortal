using System.Diagnostics;
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

    [AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorVm(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-009 — render an anonymous, branded response while preserving the original HTTP status.
    [AllowAnonymous]
    [HttpGet("/home/status-code")]
    public IActionResult StatusCodePage(int code)
    {
        var safeCode = code is >= 400 and <= 599 ? code : StatusCodes.Status404NotFound;
        Response.StatusCode = safeCode;
        return View("StatusCode", safeCode);
    }
}

[Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Users.Index")]
public sealed class UsersController(PortalDb db, PasswordService passwords) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetUsersAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Users.Create")]
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

[Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Roles.Index")]
public sealed class RolesController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetRolesAsync());
}

[Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Customers.Index")]
public sealed class CustomersController(IBlazorPortalService db) : Controller
{
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — preserve MVC ownership and distinguish permission, loading failures and empty results.
    public async Task<IActionResult> Index()
    {
        if (!await Allowed("Customers.Index")) return Forbid();
        try
        {
            ViewData["CanCreate"] = await Allowed("Customers.Create");
            return View(await db.GetCustomersAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
        }
        catch { ViewData["LoadError"] = "Không thể tải danh sách khách hàng. Vui lòng tải lại."; return View(new List<CustomerRow>()); }
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — anti-forgery plus current dynamic action checks; typed DTO prevents overposting audit fields.
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Customers.Create")]
    public async Task<IActionResult> Create(CustomerCreateVm vm)
    {
        if (!await Allowed("Customers.Index") || !await Allowed("Customers.Create")) return Forbid();
        CustomerRules.Normalize(vm);
        ModelState.Clear(); TryValidateModel(vm);
        if (!ModelState.IsValid) return BadRequest(new { message = "Vui lòng kiểm tra thông tin khách hàng.", errors = ModelState.Where(x => x.Value!.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()) });
        try
        {
            var result = await db.CreateCustomerAsync(vm, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            if (result == CustomerWriteResult.Forbidden) return Forbid();
            if (result != CustomerWriteResult.Success) return BadRequest(new { message = CustomerRules.Message(result) });
            TempData["Success"] = CustomerRules.Message(result);
            return Json(new { success = true });
        }
        catch { return StatusCode(500, new { message = CustomerRules.Message(CustomerWriteResult.Failed) }); }
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — export uses current VIEW authorization and the same search projection, with no SQL writes.
    [HttpGet]
    public async Task<IActionResult> Export(string? search)
    {
        if (!await Allowed("Customers.Index")) return Forbid();
        try
        {
            Response.Headers.CacheControl = "no-store";
            // PortalDb applies search in SQL; the final filter preserves compatibility with existing service test doubles.
            var rows = CustomerRules.Filter(await db.GetCustomersAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), search), search);
            return File(CustomerWorkbook.Create(rows), CustomerWorkbook.ContentType, $"Khach-hang-{DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)):yyyyMMdd}.xlsx");
        }
        catch { return StatusCode(500, new { message = "Không thể xuất dữ liệu lúc này. Vui lòng thử lại." }); }
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — identity always comes from the authenticated principal, never form input.
    private async Task<bool> Allowed(string action) => User.Identity?.IsAuthenticated == true && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await db.HasFunctionActionAsync(id, action);
}

[Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Menus.Index")]
public sealed class MenusController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetMenusAsync());
}
