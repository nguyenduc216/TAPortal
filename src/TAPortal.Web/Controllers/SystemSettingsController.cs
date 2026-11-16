using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Security;

namespace TAPortal.Web.Controllers;

/// <summary>
/// Operator 2026-11-13: "Cấu hình hệ thống" groups the settings pages that had their own menu entries (email templates and
/// terms of Hóa đơn tự động, password security) as tabs; each tab keeps its own permission. The menu entry opens the first
/// tab the account may see.
/// </summary>
[Authorize(Policy = AdminPermissions.Policy.InvoiceAppContentView)] // SYS_ADMIN (the only one who may open password security) has it too
public sealed class SystemSettingsController : Controller
{
    public IActionResult Index()
    {
        if (AdminPermissions.Has(User, AdminPermissions.InvoiceAppContentView)) return RedirectToAction("Index", "InvoiceAppContent");
        if (AdminPermissions.Has(User, AdminPermissions.RolesManage)) return RedirectToAction("Index", "PasswordSecurity");
        return Forbid();
    }

    /// <summary>True when the account may open at least one tab (the menu shows the entry only then).</summary>
    public static bool CanOpen(System.Security.Claims.ClaimsPrincipal user) =>
        AdminPermissions.Has(user, AdminPermissions.InvoiceAppContentView) || AdminPermissions.Has(user, AdminPermissions.RolesManage);
}
