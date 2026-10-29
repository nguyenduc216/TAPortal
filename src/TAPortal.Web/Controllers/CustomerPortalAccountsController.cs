using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;
[Authorize(Policy = "SystemAdminOnly")]
public sealed class CustomerPortalAccountsController(MembershipAdminStore store, PortalDb db, PasswordService passwords, ICustomerAdminStore customers) : Controller
{
    // Compatibility: no longer in the sidebar. A per-customer link keeps its context and opens that customer's workspace tab;
    // the cross-customer overview stays reachable for old bookmarks and the dashboard KPI.
    public async Task<IActionResult> Index(Guid? customerId)
    {
        if (customerId is { } id && id != Guid.Empty)
            return RedirectToAction("PortalAccounts", "Customers", new { id });
        ViewBag.Customers = await db.GetCustomersAsync();
        ViewBag.Users = await db.GetUsersAsync();
        ViewBag.CustomerId = customerId;
        try
        {
            return View(await store.List(Actor, customerId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
            return RedirectToAction(nameof(Index), new { customerId = NullIfEmpty(input.CustomerId) });
        }

        try
        {
            await store.Save(input, Actor, hash);
            TempData["Success"] = "Đã cập nhật tài khoản Customer Portal.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (SqlException ex)when (ex.Number is 2601 or 2627 or 547 or 51000)
        {
            TempData["Error"] = "Thông tin trùng lặp hoặc tài khoản/doanh nghiệp không hợp lệ.";
        }
        catch (AuditUnavailableException)
        {
            TempData["Error"] = AuditUnavailableMessage;
        }

        return RedirectToAction(nameof(Index), new { customerId = NullIfEmpty(input.CustomerId) });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(Guid userId, Guid customerId, bool active)
    {
        if (active && await customers.GetAsync(customerId) is not { IsActive: true, Status: "ACTIVE" })
        {
            TempData["Error"] = CustomersController.InactiveCustomerMessage;
            return RedirectToAction(nameof(Index), new { customerId = NullIfEmpty(customerId) });
        }
        try
        {
            await store.SetActive(userId, customerId, active, Actor);
            TempData["Success"] = active ? "Đã kích hoạt lại quyền truy cập Customer Portal." : "Đã thu hồi quyền truy cập Customer Portal của doanh nghiệp này.";
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (AuditUnavailableException)
        {
            TempData["Error"] = AuditUnavailableMessage;
        }

        return RedirectToAction(nameof(Index), new { customerId = NullIfEmpty(customerId) });
    }

    private const string AuditUnavailableMessage = "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.";
    private static Guid? NullIfEmpty(Guid id) => id == Guid.Empty ? null : id;
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
