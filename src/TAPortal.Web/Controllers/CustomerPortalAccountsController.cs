using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;
[Authorize(Policy = "PERM:CUSTOMERS.PORTAL.MANAGE")]
public sealed class CustomerPortalAccountsController(MembershipAdminStore store, PortalDb db, PasswordService passwords) : Controller
{
    public async Task<IActionResult> Index(Guid? customerId)
    {
        ViewBag.Customers = await db.GetCustomersAsync();
        ViewBag.Users = await db.GetUsersAsync();
        return View(await store.List(customerId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MembershipInput input)
    {
        if (input.CustomerId == Guid.Empty)
            ModelState.AddModelError("", "Vui lòng chọn doanh nghiệp.");
        string? hash = null;
        if (!input.UserId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.DisplayName))
                ModelState.AddModelError("", "Vui lòng nhập tên đăng nhập và họ tên.");
            var policy = PasswordPolicy.Validate(input.Password);
            if (!policy.IsValid)
                ModelState.AddModelError("", policy.Error);
            else
                hash = passwords.Hash(input.Password!);
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await store.Save(input, Actor, hash);
            TempData["Success"] = "Đã cập nhật tài khoản Customer Portal.";
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547 or 51000)
        {
            TempData["Error"] = "Thông tin trùng lặp hoặc tài khoản/doanh nghiệp không hợp lệ.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid userId, Guid customerId, bool active)
    {
        await store.SetActive(userId, customerId, active, Actor);
        return RedirectToAction(nameof(Index));
    }

    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
