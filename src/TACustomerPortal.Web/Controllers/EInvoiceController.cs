using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

// Hóa đơn điện tử (operator request 2026-11-06, MISA meInvoice only): the company connects its own MISA account, picks the
// template (mẫu số + ký hiệu) and turns on automatic invoices for paid QR orders. Calls go through T.A's e-invoice partner
// with the company's merchant; the MISA password is passed on once and never stored. Same flow as the Admin "Hóa đơn Sandbox".
public sealed class EInvoiceController(ICurrentCustomerContext customer, IPaymentStore payments, IOrderInvoiceStore store, ITingeeInvoiceApi invoices,
    TimeProvider clock, ILogger<EInvoiceController> logger) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return await Page(m, await store.SettingsAsync(m.CustomerId));
    }

    [HttpPost]
    public async Task<IActionResult> Connect([Bind(Prefix = "Account")] EInvoiceAccountInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        input.TaxCode = input.TaxCode?.Trim();
        input.Username = input.Username?.Trim();
        var keep = new EInvoiceAccountInput { TaxCode = input.TaxCode, Username = input.Username };
        if (await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
            return await Page(m, settings, account: keep, connect: true, error: "Doanh nghiệp chưa được kích hoạt dịch vụ thanh toán.");
        if (!invoices.IsConfigured) return await Page(m, settings, account: keep, connect: true, error: "Dịch vụ hóa đơn chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        if (!ModelState.IsValid) return await Page(m, settings, account: keep, connect: true, status: StatusCodes.Status400BadRequest);
        long accountId;
        try
        {
            accountId = await invoices.SaveAccountAsync(new TingeeInvoiceAccountRequest
            {
                Id = settings.ProviderAccountId,
                MerchantId = merchantId,
                Provider = TingeeInvoiceProviders.Misa,
                TaxCode = input.TaxCode!,
                Username = input.Username!,
                Password = input.Password!,
                IsDefault = true
            });
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "EINVOICE_CONNECT_FAILED customer={Customer}", m.CustomerId);
            return await Page(m, settings, account: keep, connect: true, error: "Kết nối MISA meInvoice không thành công. " + OrderInvoicer.Reason(ex));
        }
        // Another tax code is another seller: its templates are different.
        var sameSeller = string.Equals(settings.ProviderTaxCode, input.TaxCode, StringComparison.Ordinal);
        await store.SaveSettingsAsync(settings with
        {
            Provider = TingeeInvoiceProviders.Misa, ProviderAccountId = accountId, ProviderTaxCode = input.TaxCode, ProviderUsername = input.Username,
            ConnectedAt = clock.GetUtcNow().UtcDateTime,
            InvoicePattern = sameSeller ? settings.InvoicePattern : null, InvoiceSeries = sameSeller ? settings.InvoiceSeries : null,
            AutoIssueEnabled = sameSeller && settings.AutoIssueEnabled
        }, Actor);
        logger.LogInformation("EINVOICE_CONNECTED customer={Customer} account={Account} update={Update}", m.CustomerId, accountId, settings.ProviderAccountId is not null);
        Flash(sameSeller && settings.HasTemplate ? "Đã kết nối tài khoản MISA meInvoice." : "Đã kết nối tài khoản MISA meInvoice. Bước tiếp theo: chọn mẫu hóa đơn.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Disconnect()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (settings.ProviderAccountId is long accountId && await payments.MerchantIdAsync(m.CustomerId) is long merchantId)
        {
            try { await invoices.DeleteAccountAsync(merchantId, accountId); }
            catch (TingeeApiException ex) when (ex.Code == "1057") { /* already gone at the partner */ }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
            {
                logger.LogWarning(ex, "EINVOICE_DISCONNECT_FAILED customer={Customer}", m.CustomerId);
                Flash("Chưa gỡ được kết nối MISA meInvoice. " + OrderInvoicer.Reason(ex), error: true);
                return RedirectToAction(nameof(Index));
            }
        }
        await store.SaveSettingsAsync(settings with { ProviderAccountId = null, InvoicePattern = null, InvoiceSeries = null, AutoIssueEnabled = false, ConnectedAt = null }, Actor);
        logger.LogInformation("EINVOICE_DISCONNECTED customer={Customer}", m.CustomerId);
        Flash("Đã gỡ kết nối MISA meInvoice và tắt xuất hóa đơn tự động.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Templates registered on the company's MISA account; picking one fills mẫu số and ký hiệu.</summary>
    [HttpPost]
    public async Task<IActionResult> Templates()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (!settings.IsConnected || await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
            return await Page(m, settings, error: "Kết nối tài khoản MISA meInvoice trước.");
        try
        {
            var templates = await invoices.GetTemplatesAsync(merchantId, settings.ProviderAccountId);
            if (templates.Count == 0) return await Page(m, settings, error: "Tài khoản MISA chưa có mẫu hóa đơn nào đang sử dụng. Nhập mẫu số và ký hiệu theo MISA.");
            return await Page(m, settings, templates: templates);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "EINVOICE_TEMPLATES_FAILED customer={Customer}", m.CustomerId);
            return await Page(m, settings, error: "Chưa lấy được mẫu hóa đơn. " + OrderInvoicer.Reason(ex));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Save([Bind(Prefix = "Template")] EInvoiceTemplateInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (input.Choice?.Split('|') is [var pickedPattern, var pickedSeries])
        {
            (input.Pattern, input.Series) = (pickedPattern, pickedSeries);
            ModelState.Remove("Template.Pattern");
            ModelState.Remove("Template.Series");
            foreach (var (key, value, max) in new[] { ("Template.Pattern", pickedPattern, 50), ("Template.Series", pickedSeries, 20) })
                if (!System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), $"^[A-Za-z0-9/._-]{{1,{max}}}$")) ModelState.AddModelError(key, "Mẫu hóa đơn đã chọn không hợp lệ.");
        }
        input.Pattern = string.IsNullOrWhiteSpace(input.Pattern) ? null : input.Pattern.Trim();
        input.Series = string.IsNullOrWhiteSpace(input.Series) ? null : input.Series.Trim().ToUpperInvariant();
        if ((input.Pattern is null) != (input.Series is null)) ModelState.AddModelError(string.Empty, "Nhập đủ cả mẫu số và ký hiệu hóa đơn.");
        input.IssueMode ??= input.AutoIssueEnabled ? InvoiceIssueModes.Signed : InvoiceIssueModes.None;
        if (!InvoiceIssueModes.IsValid(input.IssueMode)) ModelState.AddModelError(string.Empty, "Chọn cách xuất hóa đơn tự động.");
        input.AutoIssueEnabled = input.IssueMode is InvoiceIssueModes.Signed or InvoiceIssueModes.Draft;
        if (input.AutoIssueEnabled && !settings.IsConnected) ModelState.AddModelError(string.Empty, "Kết nối tài khoản MISA meInvoice trước khi bật xuất hóa đơn tự động.");
        if (input.AutoIssueEnabled && input.Pattern is null) ModelState.AddModelError(string.Empty, "Chọn mẫu hóa đơn trước khi bật xuất hóa đơn tự động.");
        if (!ModelState.IsValid) return await Page(m, settings, template: input, status: StatusCodes.Status400BadRequest);
        var now = clock.GetUtcNow().UtcDateTime;
        await store.SaveSettingsAsync(settings with
        {
            InvoicePattern = input.Pattern, InvoiceSeries = input.Series, AutoIssueEnabled = input.AutoIssueEnabled, SendEmail = input.SendEmail,
            // Orders paid before automatic invoices were turned on are never invoiced automatically (issue them from the order).
            AutoIssueFrom = input.AutoIssueEnabled ? (settings.AutoIssueEnabled ? settings.AutoIssueFrom ?? now : now) : null,
            IssueMode = input.IssueMode,
            // Choosing "đã mua HSM" is the company's confirmation that it bought MISA's HSM signature: kept with who and when.
            HsmConfirmedAt = input.IssueMode == InvoiceIssueModes.Signed ? settings.HsmConfirmedAt ?? now : settings.HsmConfirmedAt,
            HsmConfirmedBy = input.IssueMode == InvoiceIssueModes.Signed ? settings.HsmConfirmedBy ?? Actor : settings.HsmConfirmedBy
        }, Actor);
        logger.LogInformation("EINVOICE_SETTINGS_SAVED customer={Customer} mode={Mode} email={Email} hsmConfirmed={Hsm}", m.CustomerId, input.IssueMode, input.SendEmail,
            input.IssueMode == InvoiceIssueModes.Signed && settings.HsmConfirmedAt is null);
        Flash(input.IssueMode switch
        {
            InvoiceIssueModes.Signed => "Đã lưu. Đơn hàng có \"tự động xuất hóa đơn\" được xuất và ký số bằng HSM ngay khi thanh toán đủ.",
            InvoiceIssueModes.Draft => "Đã lưu. Đơn hàng có \"tự động xuất hóa đơn\" được tạo hóa đơn nháp trên MISA khi thanh toán đủ, chờ duyệt và ký số thủ công.",
            _ => "Đã lưu cài đặt hóa đơn điện tử. Không xuất hóa đơn tự động."
        });
        return RedirectToAction(nameof(Index));
    }

    /// <summary>A draft with a sample line, to check the template: nothing is signed or sent to the tax authority.</summary>
    [HttpPost]
    public async Task<IActionResult> Preview()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (!settings.IsReady || await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
        {
            Flash("Kết nối MISA meInvoice và lưu mẫu hóa đơn trước khi xem thử.", error: true);
            return RedirectToAction(nameof(Index));
        }
        var now = clock.GetUtcNow();
        var job = new OrderInvoiceJob(m.CustomerId, Guid.NewGuid(), "XEMTHU", "Hóa đơn nháp xem thử", $"XEMTHU-{now.ToOffset(TingeeSignature.VietnamOffset):yyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            1, merchantId, null, "Khách hàng xem thử", null, null, null, null, null, [new(1, "Hàng hóa / dịch vụ xem thử", "Gói", 1, 100_000, "10")]);
        try
        {
            var pdf = await invoices.CreateDraftAsync(OrderInvoiceRules.Build(job, settings, DateOnly.FromDateTime(now.ToOffset(TingeeSignature.VietnamOffset).DateTime)));
            if (string.IsNullOrWhiteSpace(pdf.PdfBase64)) throw new TingeeApiException("EMPTY", "Không nhận được file PDF.");
            return File(Convert.FromBase64String(pdf.PdfBase64), "application/pdf");
        }
        catch (FormatException) { Flash("File PDF xem thử không đúng định dạng.", error: true); }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "EINVOICE_PREVIEW_FAILED customer={Customer}", m.CustomerId);
            Flash("Chưa tạo được hóa đơn nháp. " + OrderInvoicer.Reason(ex), error: true);
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Page(CustomerMembership m, CustomerInvoiceSettings settings, EInvoiceAccountInput? account = null, EInvoiceTemplateInput? template = null,
        IReadOnlyList<TingeeInvoiceTemplate>? templates = null, bool connect = false, string? error = null, int? status = null)
    {
        if (error is not null) ModelState.AddModelError(string.Empty, PartnerText.Scrub(error));
        if (status is int code) Response.StatusCode = code;
        else if (error is not null) Response.StatusCode = StatusCodes.Status400BadRequest;
        account ??= new EInvoiceAccountInput { TaxCode = settings.ProviderTaxCode ?? m.TaxCode, Username = settings.ProviderUsername };
        template ??= new EInvoiceTemplateInput { Pattern = settings.InvoicePattern, Series = settings.InvoiceSeries, AutoIssueEnabled = settings.AutoIssueEnabled, SendEmail = settings.SendEmail, IssueMode = settings.Mode };
        var linked = await payments.MerchantIdAsync(m.CustomerId) is not null;
        return View("Index", new EInvoiceVm(m, CanManage(m), linked, invoices.IsConfigured, settings, account, template, templates, connect || !settings.IsConnected));
    }
}
