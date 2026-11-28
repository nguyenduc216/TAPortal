using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.SoftDreams;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

// Hóa đơn điện tử (operator request 2026-11-06 MISA meInvoice, 2026-11-23 Viettel SInvoice): the company connects its own
// provider account, picks the template (mẫu số + ký hiệu) and turns on automatic invoices for paid QR orders. Calls go through
// T.A's e-invoice partner with the company's merchant; the provider password is passed on once and never stored. Same flow as
// the Admin "Hóa đơn Sandbox". The page also shows what still stands between the company and fully automatic signed invoices.
// SoftDreams EasyInvoice (operator 2026-11-26) is called directly: the login is checked with one read-only call and the
// password is kept encrypted (IInvoiceSecretProtector), because EasyInvoice needs it on every call.
public sealed class EInvoiceController(ICurrentCustomerContext customer, IPaymentStore payments, IOrderInvoiceStore store, ITingeeInvoiceApi invoices,
    TimeProvider clock, ILogger<EInvoiceController> logger, IEasyInvoiceApi? easyInvoice = null, IInvoiceSecretProtector? secrets = null) : CustomerAreaController(customer)
{
    /// <param name="env">LIVE / DEMO: open the connect form for that SoftDreams environment.</param>
    public async Task<IActionResult> Index(string? env = null)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (env is SavedInvoiceTemplate.Live or SavedInvoiceTemplate.Demo)
            return await Page(m, settings, account: new EInvoiceAccountInput { Provider = CustomerInvoiceProviders.SoftDreamsCode, TaxCode = settings.ProviderTaxCode ?? m.TaxCode,
                Username = env == Environment(settings) ? settings.ProviderUsername : null, Demo = env == SavedInvoiceTemplate.Demo }, connect: true);
        return await Page(m, settings);
    }

    [HttpPost]
    public async Task<IActionResult> Connect([Bind(Prefix = "Account")] EInvoiceAccountInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        input.TaxCode = input.TaxCode?.Trim();
        input.Username = input.Username?.Trim();
        input.Provider = string.IsNullOrWhiteSpace(input.Provider) ? settings.Provider : input.Provider.Trim();
        if (!CustomerInvoiceProviders.IsSupported(input.Provider)) ModelState.AddModelError("Account.Provider", "Chọn phần mềm hóa đơn điện tử.");
        var provider = CustomerInvoiceProviders.Get(input.Provider);
        var keep = new EInvoiceAccountInput { Provider = provider.Code, TaxCode = input.TaxCode, Username = input.Username };
        if (await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
            return await Page(m, settings, account: keep, connect: true, error: "Doanh nghiệp chưa được kích hoạt dịch vụ thanh toán.");
        if (!invoices.IsConfigured) return await Page(m, settings, account: keep, connect: true, error: "Dịch vụ hóa đơn chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        if (!provider.ViaPartner && (input.Username?.Contains(':') == true || input.Password?.Contains(':') == true))
            ModelState.AddModelError("Account.Password", $"Tên đăng nhập và mật khẩu {provider.Short} không được chứa dấu \":\" khi kết nối qua API. Đổi mật khẩu trên {provider.Short} rồi kết nối lại.");
        if (!ModelState.IsValid) return await Page(m, settings, account: keep, connect: true, status: StatusCodes.Status400BadRequest);
        if (!provider.ViaPartner) return await ConnectDirect(m, settings, provider, merchantId, input, keep);
        // Another provider is another account at the partner: create it, then remove the old one.
        var sameProvider = settings.Provider == provider.Code;
        long accountId;
        try
        {
            accountId = await invoices.SaveAccountAsync(new TingeeInvoiceAccountRequest
            {
                Id = sameProvider ? settings.ProviderAccountId : null,
                MerchantId = merchantId,
                Provider = provider.Code,
                TaxCode = input.TaxCode!,
                Username = input.Username!,
                Password = input.Password!,
                IsDefault = true
            });
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "EINVOICE_CONNECT_FAILED customer={Customer}", m.CustomerId);
            return await Page(m, settings, account: keep, connect: true, error: $"Kết nối {provider.Name} không thành công. " + OrderInvoicer.Reason(ex, provider));
        }
        if (!sameProvider && settings.ProviderAccountId is long oldAccount && oldAccount != accountId)
        {
            try { await invoices.DeleteAccountAsync(merchantId, oldAccount); }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
            {
                logger.LogWarning(ex, "EINVOICE_OLD_ACCOUNT_NOT_REMOVED customer={Customer} account={Account}", m.CustomerId, oldAccount);
            }
        }
        // Another provider or tax code is another seller: its templates are different.
        var sameSeller = sameProvider && string.Equals(settings.ProviderTaxCode, input.TaxCode, StringComparison.Ordinal);
        await store.SaveSettingsAsync(settings with
        {
            Provider = provider.Code, ProviderAccountId = accountId, ProviderTaxCode = input.TaxCode, ProviderUsername = input.Username,
            ConnectedAt = clock.GetUtcNow().UtcDateTime, ProviderSecret = null,
            InvoicePattern = sameSeller ? settings.InvoicePattern : null, InvoiceSeries = sameSeller ? settings.InvoiceSeries : null,
            AutoIssueEnabled = sameSeller && settings.AutoIssueEnabled
        }, Actor);
        logger.LogInformation("EINVOICE_CONNECTED customer={Customer} provider={Provider} account={Account} update={Update}", m.CustomerId, provider.Code, accountId, settings.ProviderAccountId is not null);
        Flash(sameSeller && settings.HasTemplate ? $"Đã kết nối tài khoản {provider.Name}." : $"Đã kết nối tài khoản {provider.Name}. Bước tiếp theo: chọn mẫu hóa đơn.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// SoftDreams EasyInvoice: no account at the partner. The login is checked by looking up one unknown invoice key (read
    /// only: nothing is created), then kept encrypted. A previous partner account (MISA, Viettel) is removed.
    /// </summary>
    private async Task<IActionResult> ConnectDirect(CustomerMembership m, CustomerInvoiceSettings settings, CustomerInvoiceProvider provider, long merchantId,
        EInvoiceAccountInput input, EInvoiceAccountInput keep)
    {
        if (easyInvoice is null || secrets is null)
            return await Page(m, settings, account: keep, connect: true, error: "Dịch vụ hóa đơn chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        var login = new EasyInvoiceCredentials(input.Username!, input.Password!, input.TaxCode!, input.Demo);
        try { await easyInvoice.GetByIkeysAsync(login, ["KIEMTRA-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()]); }
        // Production answers the check with "Mẫu không khả dụng" (111) once the login is accepted: the account is fine, the
        // templates come next (each environment keeps its own list).
        catch (EasyInvoiceApiException ex) when (EasyInvoiceResponse.IsPatternUnavailable(ex))
        {
            logger.LogInformation("EINVOICE_CONNECT_PATTERN_UNAVAILABLE customer={Customer} provider={Provider} demo={Demo}", m.CustomerId, provider.Code, input.Demo);
        }
        catch (Exception ex) when (ex is EasyInvoiceApiException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "EINVOICE_CONNECT_FAILED customer={Customer} provider={Provider}", m.CustomerId, provider.Code);
            return await Page(m, settings, account: keep, connect: true, error: $"Kết nối {provider.Name} không thành công. " + OrderInvoicer.Reason(ex, provider));
        }
        if (settings.Info.ViaPartner && settings.ProviderAccountId is long oldAccount)
        {
            try { await invoices.DeleteAccountAsync(merchantId, oldAccount); }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
            {
                logger.LogWarning(ex, "EINVOICE_OLD_ACCOUNT_NOT_REMOVED customer={Customer} account={Account}", m.CustomerId, oldAccount);
            }
        }
        var sameSeller = settings.Provider == provider.Code && string.Equals(settings.ProviderTaxCode, input.TaxCode, StringComparison.Ordinal);
        // Demo and production are kept side by side: the login in use is saved for its environment before it is replaced.
        await KeepActiveConnection(m, settings);
        var environment = input.Demo ? SavedInvoiceTemplate.Demo : SavedInvoiceTemplate.Live;
        var sameEnvironment = sameSeller && settings.IsConnected && Environment(settings) == environment;
        var envDefault = (await store.SavedTemplatesAsync(m.CustomerId, provider.Code, environment)).FirstOrDefault(t => t.IsDefault);
        var secret = secrets.Protect(new EasyInvoiceSecret(input.Password!, input.Demo));
        var connectedAt = clock.GetUtcNow().UtcDateTime;
        var pattern = sameEnvironment ? settings.InvoicePattern : envDefault?.Pattern;
        var series = sameEnvironment ? settings.InvoiceSeries : envDefault?.Series;
        await store.SaveSettingsAsync(settings with
        {
            Provider = provider.Code, ProviderAccountId = null, ProviderTaxCode = input.TaxCode, ProviderUsername = input.Username,
            ProviderSecret = secret, ConnectedAt = connectedAt, InvoicePattern = pattern, InvoiceSeries = series,
            AutoIssueEnabled = sameSeller && settings.AutoIssueEnabled
        }, Actor);
        await store.SaveConnectionAsync(m.CustomerId, new(provider.Code, environment, input.TaxCode, input.Username, secret, connectedAt), Actor);
        settings = settings with { InvoicePattern = pattern, InvoiceSeries = series };
        logger.LogInformation("EINVOICE_CONNECTED customer={Customer} provider={Provider} demo={Demo}", m.CustomerId, provider.Code, input.Demo);
        Flash(sameSeller && settings.HasTemplate ? $"Đã kết nối tài khoản {provider.Name}." : $"Đã kết nối tài khoản {provider.Name}. Bước tiếp theo: nhập mẫu số và ký hiệu hóa đơn.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Disconnect()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (settings.Info.ViaPartner && settings.ProviderAccountId is long accountId && await payments.MerchantIdAsync(m.CustomerId) is long merchantId)
        {
            try { await invoices.DeleteAccountAsync(merchantId, accountId); }
            catch (TingeeApiException ex) when (ex.Code == "1057") { /* already gone at the partner */ }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
            {
                logger.LogWarning(ex, "EINVOICE_DISCONNECT_FAILED customer={Customer}", m.CustomerId);
                Flash($"Chưa gỡ được kết nối {settings.Info.Name}. " + OrderInvoicer.Reason(ex, settings.Info), error: true);
                return RedirectToAction(nameof(Index));
            }
        }
        if (!settings.Info.ViaPartner) await store.DeleteConnectionAsync(m.CustomerId, settings.Provider, Environment(settings));
        await store.SaveSettingsAsync(settings with { ProviderAccountId = null, ProviderSecret = null, InvoicePattern = null, InvoiceSeries = null, AutoIssueEnabled = false, ConnectedAt = null }, Actor);
        logger.LogInformation("EINVOICE_DISCONNECTED customer={Customer}", m.CustomerId);
        Flash($"Đã gỡ kết nối {settings.Info.Name} và tắt xuất hóa đơn tự động.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Templates registered on the company's provider account; picking one fills mẫu số and ký hiệu.</summary>
    [HttpPost]
    public async Task<IActionResult> Templates()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (!settings.Info.HasTemplates)
            return await Page(m, settings, error: $"{settings.Info.Name} không cho lấy danh sách mẫu qua kết nối này: nhập mẫu số và ký hiệu đã đăng ký (VD mẫu số 1, ký hiệu C26TAA).");
        if (!settings.IsConnected || await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
            return await Page(m, settings, error: $"Kết nối tài khoản {settings.Info.Name} trước.");
        try
        {
            var templates = await invoices.GetTemplatesAsync(merchantId, settings.ProviderAccountId);
            if (templates.Count == 0) return await Page(m, settings, error: $"Tài khoản {settings.Info.Short} chưa có mẫu hóa đơn nào đang sử dụng. Nhập mẫu số và ký hiệu theo {settings.Info.Name}.");
            return await Page(m, settings, templates: templates);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "EINVOICE_TEMPLATES_FAILED customer={Customer}", m.CustomerId);
            return await Page(m, settings, error: "Chưa lấy được mẫu hóa đơn. " + OrderInvoicer.Reason(ex, settings.Info));
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
        if (input.AutoIssueEnabled && !settings.IsConnected) ModelState.AddModelError(string.Empty, $"Kết nối tài khoản {settings.Info.Name} trước khi bật xuất hóa đơn tự động.");
        if (input.AutoIssueEnabled && input.Pattern is null) ModelState.AddModelError(string.Empty, "Chọn mẫu hóa đơn trước khi bật xuất hóa đơn tự động.");
        if (!ModelState.IsValid) return await Page(m, settings, template: input, status: StatusCodes.Status400BadRequest);
        var now = clock.GetUtcNow().UtcDateTime;
        await store.SaveSettingsAsync(settings with
        {
            InvoicePattern = input.Pattern, InvoiceSeries = input.Series, AutoIssueEnabled = input.AutoIssueEnabled, SendEmail = input.SendEmail,
            // Orders paid before automatic invoices were turned on are never invoiced automatically (issue them from the order).
            AutoIssueFrom = input.AutoIssueEnabled ? (settings.AutoIssueEnabled ? settings.AutoIssueFrom ?? now : now) : null,
            IssueMode = input.IssueMode,
            // Choosing "đã có chữ ký số từ xa" is the company's confirmation that it bought the provider's remote (HSM) signature: kept with who and when.
            HsmConfirmedAt = input.IssueMode == InvoiceIssueModes.Signed ? settings.HsmConfirmedAt ?? now : settings.HsmConfirmedAt,
            HsmConfirmedBy = input.IssueMode == InvoiceIssueModes.Signed ? settings.HsmConfirmedBy ?? Actor : settings.HsmConfirmedBy
        }, Actor);
        if (input.Pattern is string savedPattern && input.Series is string savedSeries)
            await store.SaveTemplateAsync(m.CustomerId, settings.Provider, Environment(settings), savedPattern, savedSeries, Actor, makeDefault: true);
        logger.LogInformation("EINVOICE_SETTINGS_SAVED customer={Customer} mode={Mode} email={Email} hsmConfirmed={Hsm}", m.CustomerId, input.IssueMode, input.SendEmail,
            input.IssueMode == InvoiceIssueModes.Signed && settings.HsmConfirmedAt is null);
        Flash(input.IssueMode switch
        {
            InvoiceIssueModes.Signed => "Đã lưu. Đơn hàng có \"tự động xuất hóa đơn\" được xuất và ký số từ xa ngay khi thanh toán đủ.",
            InvoiceIssueModes.Draft => $"Đã lưu. Đơn hàng có \"tự động xuất hóa đơn\" được tạo hóa đơn nháp trên {settings.Info.Short} khi thanh toán đủ, chờ duyệt và ký số thủ công.",
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
        if (!settings.Info.ViaPartner)
        {
            Flash($"Xem thử hóa đơn nháp chưa có cho {settings.Info.Name}. Xuất thử một đơn hàng nhỏ để kiểm tra mẫu.", error: true);
            return RedirectToAction(nameof(Index));
        }
        if (!settings.IsReady || await payments.MerchantIdAsync(m.CustomerId) is not long merchantId)
        {
            Flash($"Kết nối {settings.Info.Name} và lưu mẫu hóa đơn trước khi xem thử.", error: true);
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
            Flash("Chưa tạo được hóa đơn nháp. " + OrderInvoicer.Reason(ex, settings.Info), error: true);
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Adds a template (mẫu số + ký hiệu) to the list of the provider and environment in use; optionally makes it the default.</summary>
    [HttpPost]
    public async Task<IActionResult> AddTemplate(string? pattern, string? series, bool makeDefault = false)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        pattern = string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
        series = string.IsNullOrWhiteSpace(series) ? null : series.Trim().ToUpperInvariant();
        if (!settings.IsConnected) Flash($"Kết nối tài khoản {settings.Info.Name} trước khi thêm mẫu hóa đơn.", error: true);
        else if (pattern is null || series is null || !System.Text.RegularExpressions.Regex.IsMatch(pattern, "^[A-Za-z0-9/._-]{1,50}$")
            || !System.Text.RegularExpressions.Regex.IsMatch(series, "^[A-Za-z0-9/._-]{1,20}$"))
            Flash("Nhập mẫu số và ký hiệu hợp lệ (chữ, số và / . _ -), VD mẫu số 1, ký hiệu C26TAA.", error: true);
        else
        {
            makeDefault |= !settings.HasTemplate;
            await store.SaveTemplateAsync(m.CustomerId, settings.Provider, Environment(settings), pattern, series, Actor, makeDefault);
            if (makeDefault) await store.SaveSettingsAsync(settings with { InvoicePattern = pattern, InvoiceSeries = series }, Actor);
            Flash(makeDefault ? $"Đã thêm mẫu {pattern}{series} và dùng làm mặc định." : $"Đã thêm mẫu {pattern}{series} vào danh sách.");
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// SoftDreams: switches between the demo and the production system. The login in use is kept for its environment; the
    /// other environment's saved login and default template take its place.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SwitchEnvironment(string environment)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        if (settings.Info.ViaPartner || environment is not (SavedInvoiceTemplate.Live or SavedInvoiceTemplate.Demo)) return BadRequest();
        var label = EnvironmentLabel(environment);
        if (settings.IsConnected && Environment(settings) == environment)
        {
            Flash($"Đang dùng {label}.");
            return RedirectToAction(nameof(Index));
        }
        var target = (await store.ConnectionsAsync(m.CustomerId, settings.Provider)).FirstOrDefault(c => c.Environment == environment && c.ProviderSecret is { Length: > 0 });
        if (target is null)
        {
            Flash($"Chưa kết nối {label}. Bấm Kết nối ở {label} để nhập tài khoản.", error: true);
            return RedirectToAction(nameof(Index));
        }
        await KeepActiveConnection(m, settings);
        var envDefault = (await store.SavedTemplatesAsync(m.CustomerId, settings.Provider, environment)).FirstOrDefault(t => t.IsDefault);
        await store.SaveSettingsAsync(settings with
        {
            ProviderAccountId = null, ProviderTaxCode = target.ProviderTaxCode, ProviderUsername = target.ProviderUsername, ProviderSecret = target.ProviderSecret,
            ConnectedAt = target.ConnectedAt, InvoicePattern = envDefault?.Pattern, InvoiceSeries = envDefault?.Series
        }, Actor);
        logger.LogInformation("EINVOICE_ENVIRONMENT_SWITCHED customer={Customer} provider={Provider} environment={Environment}", m.CustomerId, settings.Provider, environment);
        Flash(envDefault is null ? $"Đã chuyển sang {label}. Thêm mẫu hóa đơn cho môi trường này trước khi xuất hóa đơn." : $"Đã chuyển sang {label}, mẫu mặc định {envDefault.Pattern}{envDefault.Series}.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Saves the SoftDreams login in use for its environment, so switching away and back does not ask for it again.</summary>
    private async Task KeepActiveConnection(CustomerMembership m, CustomerInvoiceSettings settings)
    {
        if (settings.Info.ViaPartner || !settings.IsConnected) return;
        var environment = Environment(settings);
        await store.SaveConnectionAsync(m.CustomerId, new(settings.Provider, environment, settings.ProviderTaxCode, settings.ProviderUsername, settings.ProviderSecret,
            settings.ConnectedAt), Actor);
        if (settings.HasTemplate)
            await store.SaveTemplateAsync(m.CustomerId, settings.Provider, environment, settings.InvoicePattern!, settings.InvoiceSeries!, Actor, makeDefault: true);
    }

    public static string EnvironmentLabel(string environment) => environment == SavedInvoiceTemplate.Demo ? "môi trường thử (demo)" : "môi trường chính thức";

    /// <summary>Removes a saved template from the list; the template in use (the default) cannot be removed.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteTemplate(Guid templateId)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        var saved = await store.SavedTemplatesAsync(m.CustomerId, settings.Provider, Environment(settings));
        if (saved.FirstOrDefault(t => t.Id == templateId) is not { } template) return NotFound();
        if (template.Pattern == settings.InvoicePattern && template.Series == settings.InvoiceSeries)
            Flash("Không xóa được mẫu đang dùng mặc định. Chọn mẫu khác làm mặc định và lưu trước.", error: true);
        else
        {
            await store.DeleteTemplateAsync(m.CustomerId, templateId);
            Flash($"Đã xóa mẫu {template.Pattern}{template.Series} khỏi danh sách.");
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>LIVE, or DEMO when the company is connected to SoftDreams' test system: templates of one never apply to the other.</summary>
    private string Environment(CustomerInvoiceSettings settings) =>
        !settings.Info.ViaPartner && secrets?.Unprotect(settings.ProviderSecret)?.Demo == true ? SavedInvoiceTemplate.Demo : SavedInvoiceTemplate.Live;

    private async Task<IActionResult> Page(CustomerMembership m, CustomerInvoiceSettings settings, EInvoiceAccountInput? account = null, EInvoiceTemplateInput? template = null,
        IReadOnlyList<TingeeInvoiceTemplate>? templates = null, bool connect = false, string? error = null, int? status = null)
    {
        if (error is not null) ModelState.AddModelError(string.Empty, PartnerText.Scrub(error));
        if (status is int code) Response.StatusCode = code;
        else if (error is not null) Response.StatusCode = StatusCodes.Status400BadRequest;
        account ??= new EInvoiceAccountInput { Provider = settings.Provider, TaxCode = settings.ProviderTaxCode ?? m.TaxCode, Username = settings.ProviderUsername,
            Demo = !settings.Info.ViaPartner && secrets?.Unprotect(settings.ProviderSecret)?.Demo == true };
        template ??= new EInvoiceTemplateInput { Pattern = settings.InvoicePattern, Series = settings.InvoiceSeries, AutoIssueEnabled = settings.AutoIssueEnabled, SendEmail = settings.SendEmail, IssueMode = settings.Mode };
        var linked = await payments.MerchantIdAsync(m.CustomerId) is not null;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TingeeSignature.VietnamOffset).DateTime);
        var readiness = InvoiceReadiness.Check(settings, today, settings.IsConnected ? await store.SigningHealthAsync(m.CustomerId) : InvoiceSigningHealth.None);
        // The saved list for this provider and environment, with the template in use first when it was set before the list existed.
        var environment = Environment(settings);
        var saved = settings.IsConnected ? (await store.SavedTemplatesAsync(m.CustomerId, settings.Provider, environment)).ToList() : [];
        if (settings.HasTemplate && !saved.Any(t => t.Pattern == settings.InvoicePattern && t.Series == settings.InvoiceSeries))
            saved.Insert(0, new(Guid.Empty, settings.Provider, environment, settings.InvoicePattern!, settings.InvoiceSeries!, settings.ConnectedAt ?? DateTime.UtcNow));
        IReadOnlyList<InvoiceEnvironmentVm>? environments = null;
        if (!settings.Info.ViaPartner)
        {
            var kept = await store.ConnectionsAsync(m.CustomerId, settings.Provider);
            environments = new[] { SavedInvoiceTemplate.Live, SavedInvoiceTemplate.Demo }.Select(code =>
            {
                if (settings.IsConnected && environment == code) return new InvoiceEnvironmentVm(code, true, true, settings.ProviderUsername, settings.ConnectedAt);
                var other = kept.FirstOrDefault(c => c.Environment == code && c.ProviderSecret is { Length: > 0 });
                return new InvoiceEnvironmentVm(code, other is not null, false, other?.ProviderUsername, other?.ConnectedAt);
            }).ToList();
        }
        return View("Index", new EInvoiceVm(m, CanManage(m), linked, invoices.IsConfigured, settings, account, template, templates, connect || !settings.IsConnected, readiness,
            saved, environment, environments));
    }
}
