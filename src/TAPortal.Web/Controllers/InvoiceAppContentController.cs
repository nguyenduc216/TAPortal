using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;

namespace TAPortal.Web.Controllers;

// Admin → Hóa đơn tự động (operator 2026-11-12): the emails the Customer Portal sends (login details of a new account, password
// reset) and its terms of service. Texts are stored in dbo.PortalContent (migration 036); without a saved row the built-in
// default applies. Every save is audited. Catalogue: INVOICEAPP.CONTENT.VIEW / EDIT.
[Authorize(Policy = AdminPermissions.Policy.InvoiceAppContentView)]
public sealed class InvoiceAppContentController(IPortalContentStore store, IConfiguration configuration, ILogger<InvoiceAppContentController> logger) : Controller
{
    public const string StoreUnavailable = "Chưa lưu được (bảng dbo.PortalContent của migration 036 chưa có hoặc thiếu quyền). Hệ thống vẫn dùng nội dung mặc định. Chi tiết đã ghi vào nhật ký ứng dụng.";

    /// <summary>The Customer Portal's address: the logo and links in email previews.</summary>
    private string CustomerUrl => (configuration["CustomerPortal:PublicBaseUrl"] is { Length: > 0 } u ? u : "https://hoadon.tuvangiaiphapta.vn").TrimEnd('/');

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    public async Task<IActionResult> Index() => View(new InvoiceAppEmailsVm(await store.AllAsync()));

    public async Task<IActionResult> Email(string id)
    {
        if (PortalEmailTemplates.Find(id) is not { } spec) return NotFound();
        var saved = await store.GetAsync(spec.Code);
        return View(EmailVm(spec, saved, saved?.Subject ?? spec.DefaultSubject, saved?.Body ?? spec.DefaultBody, []));
    }

    // command "preview" shows the unsaved text; "save" validates and stores it; "reset" goes back to the default.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.InvoiceAppContentEdit)]
    public async Task<IActionResult> Email(string id, string? subject, string? body, string? command)
    {
        if (PortalEmailTemplates.Find(id) is not { } spec) return NotFound();
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        subject = subject?.Trim();
        body = body?.Replace("\r\n", "\n").Trim();
        if (command == "reset") return await Reset(spec.Code, actor, nameof(Email), new { id });
        var errors = PortalEmailTemplates.Validate(spec, subject, body);
        if (command != "save" || errors.Count > 0)
            return View(EmailVm(spec, await store.GetAsync(spec.Code), subject ?? "", body ?? "", command == "save" ? errors : []));
        try
        {
            await store.SaveAsync(spec.Code, subject, body!, actor, Ip);
            TempData["Success"] = "Đã lưu mẫu email \"" + spec.Name + "\". Email gửi từ bây giờ dùng nội dung mới.";
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogError(ex, "PORTAL_CONTENT_SAVE_FAILED {Code}", spec.Code);
            TempData["Error"] = StoreUnavailable;
        }
        return RedirectToAction(nameof(Email), new { id });
    }

    public async Task<IActionResult> Terms()
    {
        var saved = await store.GetAsync(PortalContentCodes.Terms);
        return View(new InvoiceAppTermsVm(saved?.Body ?? PortalTermsDefault.Text, saved, []));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.InvoiceAppContentEdit)]
    public async Task<IActionResult> Terms(string? body, string? command)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        body = body?.Replace("\r\n", "\n").Trim();
        if (command == "reset") return await Reset(PortalContentCodes.Terms, actor, nameof(Terms), null);
        var errors = PortalTerms.Validate(body);
        if (command != "save" || errors.Count > 0)
            return View(new InvoiceAppTermsVm(body ?? "", await store.GetAsync(PortalContentCodes.Terms), command == "save" ? errors : []));
        try
        {
            await store.SaveAsync(PortalContentCodes.Terms, null, body!, actor, Ip);
            TempData["Success"] = "Đã lưu điều khoản dịch vụ. Trang đăng ký hiển thị bản mới ngay.";
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogError(ex, "PORTAL_CONTENT_SAVE_FAILED {Code}", PortalContentCodes.Terms);
            TempData["Error"] = StoreUnavailable;
        }
        return RedirectToAction(nameof(Terms));
    }

    private async Task<IActionResult> Reset(string code, Guid actor, string action, object? route)
    {
        try
        {
            TempData["Success"] = await store.ResetAsync(code, actor, Ip) ? "Đã khôi phục nội dung mặc định." : "Đang dùng nội dung mặc định.";
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogError(ex, "PORTAL_CONTENT_RESET_FAILED {Code}", code);
            TempData["Error"] = StoreUnavailable;
        }
        return RedirectToAction(action, route);
    }

    private InvoiceAppEmailVm EmailVm(EmailTemplateSpec spec, PortalContentEntry? saved, string subject, string body, IReadOnlyList<string> errors)
    {
        var samples = PortalEmailTemplates.Samples(spec);
        samples["DiaChiDangNhap"] = CustomerUrl + "/account/login";
        if (samples.ContainsKey("LienKet")) samples["LienKet"] = CustomerUrl + "/account/reset-password?token=...";
        var preview = PortalEmailRenderer.Render(spec, subject, body, samples, CustomerUrl);
        return new(spec, saved, subject, body, errors, preview);
    }
}

public sealed record InvoiceAppEmailsVm(IReadOnlyDictionary<string, PortalContentEntry> Saved);
public sealed record InvoiceAppEmailVm(EmailTemplateSpec Spec, PortalContentEntry? Saved, string Subject, string Body, IReadOnlyList<string> Errors, RenderedEmail Preview);
public sealed record InvoiceAppTermsVm(string Body, PortalContentEntry? Saved, IReadOnlyList<string> Errors);
