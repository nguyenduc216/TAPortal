using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Branding;
using TAPortal.Web.Security;

namespace TAPortal.Web.Controllers;

// Customer Workspace → company logo. The same logo the company manages itself in the Customer Portal (Hồ sơ doanh nghiệp)
// and shows on its order payment pages; T.A staff can set or remove it here. The logos of the company's own customers stay
// with the company (Customer Portal → Khách hàng).
public sealed partial class CustomersController
{
    [HttpGet]
    public async Task<IActionResult> Logo(Guid id, [FromServices] ILogoStore logos)
    {
        if (await customers.GetAsync(id) is null) return NotFound();
        if (await logos.LogoAsync(id, null) is not { } logo) return NotFound();
        Response.Headers.CacheControl = "private, max-age=300";
        return File(logo.Data, logo.ContentType);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Logo(Guid id, IFormFile? logo, bool remove, [FromServices] ILogoStore logos)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        try
        {
            if (remove)
            {
                await logos.RemoveLogoAsync(id, null);
                logger.LogInformation("Customer logo removed for {CustomerId} by {Actor}.", id, actor);
                TempData["Success"] = $"Đã xóa logo của {customer.Name}.";
                return RedirectToAction(nameof(Profile), new { id });
            }
            var (image, error) = await LogoUpload.ReadAsync(logo, DateTime.UtcNow, HttpContext.RequestAborted);
            if (image is null)
            {
                TempData["Error"] = error ?? "Chọn ảnh logo để tải lên.";
                return RedirectToAction(nameof(Profile), new { id });
            }
            await logos.SaveLogoAsync(id, null, image, actor);
            logger.LogInformation("Customer logo updated for {CustomerId} by {Actor}.", id, actor);
            TempData["Success"] = $"Đã cập nhật logo của {customer.Name}.";
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer logo could not be saved for customer {CustomerId}.", id);
            TempData["Error"] = "Không lưu được logo (bảng dbo.CustomerLogos của migration 024). Chi tiết đã ghi vào nhật ký ứng dụng.";
        }
        return RedirectToAction(nameof(Profile), new { id });
    }
}
