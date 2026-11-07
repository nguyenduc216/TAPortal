using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;

namespace TACustomerPortal.Web.Controllers;
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProfileController(ICurrentCustomerContext customer, ICustomerStore store, ILogoStore logos, TimeProvider clock, ILogger<ProfileController> logger) : Controller
{
    // Module PROFILE on the company pages only: "Thông tin cá nhân" and the logo image (sidebar, pay pages) stay open.
    [CustomerModule(CustomerModules.Profile)]
    public async Task<IActionResult> Company()
    {
        var current = await customer.Get();
        if (current is null) return RedirectToAction("SelectCustomer", "Account");
        // The logo table comes from migration 024: until it runs the profile still shows, without the logo.
        try { ViewData["LogoVersion"] = await logos.LogoVersionAsync(current.CustomerId, null) is { } at ? LogoImage.Version(at) : null; }
        catch (SqlException ex) { logger.LogWarning(ex, "Company logo unavailable."); ViewData["LogoUnavailable"] = true; }
        return View(current);
    }

    /// <summary>The company's logo (used on its order payment pages too). PNG, JPG or WEBP up to 1 MB; PROFILE manage right.</summary>
    [HttpPost, CustomerModule(CustomerModules.Profile)]
    public async Task<IActionResult> Logo(IFormFile? logo, bool remove)
    {
        if (await customer.Get() is not { } current) return RedirectToAction("SelectCustomer", "Account");
        if (!CustomerAccess.CanManage(current, CustomerModules.Profile)) return ModuleDenied.Result(CustomerModules.Profile, manage: true, ModelState);
        var actor = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (remove)
        {
            await logos.RemoveLogoAsync(current.CustomerId, null);
            TempData["Success"] = "Đã xóa logo doanh nghiệp.";
            return RedirectToAction(nameof(Company));
        }
        var (image, error) = await LogoUpload.ReadAsync(logo, clock.GetUtcNow().UtcDateTime, HttpContext.RequestAborted);
        if (image is null)
        {
            TempData["Error"] = error ?? "Chọn ảnh logo để tải lên.";
            return RedirectToAction(nameof(Company));
        }
        await logos.SaveLogoAsync(current.CustomerId, null, image, actor);
        TempData["Success"] = "Đã cập nhật logo doanh nghiệp.";
        return RedirectToAction(nameof(Company));
    }

    [HttpGet]
    public async Task<IActionResult> CompanyLogo()
    {
        if (await customer.Get() is not { } current) return NotFound();
        return await logos.LogoAsync(current.CustomerId, null) is { } logo ? PayController.LogoFile(this, logo) : NotFound();
    }

    public async Task<IActionResult> Personal()
    {
        var user = await store.FindUser(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (user is not null) return View(user);
        // The account was disabled or removed meanwhile: end the session instead of failing on a null model.
        await HttpContext.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }
}
