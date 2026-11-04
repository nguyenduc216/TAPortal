using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Hóa đơn Sandbox: T.A tests Tingee's e-invoice API with the Admin site's Tingee key, on the same merchant scope as the Tingee
// Sandbox (Direct: T.A's own merchant; Master: a child merchant already linked to a customer). Steps follow Tingee's flow:
// provider account → templates → draft PDF → issue → download / email. Nothing is stored here; provider passwords go to Tingee only.
[Authorize(Policy = "SystemAdminOnly")]
public sealed class TingeeInvoiceSandboxController(
    ITingeeInvoiceApi invoices,
    IOptions<TingeeOptions> options,
    ITingeeSandboxMerchants merchants,
    ITingeeWebhookInbox webhookInbox,
    TimeProvider clock,
    ILogger<TingeeInvoiceSandboxController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(long? merchant) => await Page(merchant);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAccount([Bind(Prefix = "Account")] TingeeInvoiceAccountInput input, long? merchant)
    {
        var keep = new TingeeInvoiceAccountInput { Id = input.Id, Provider = input.Provider, TaxCode = input.TaxCode, Username = input.Username, IsDefault = input.IsDefault };
        if (Refusal() is string refused) return await Page(merchant, accountInput: keep, dialog: TingeeInvoiceSandboxDialogs.Account, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, accountInput: keep, dialog: TingeeInvoiceSandboxDialogs.Account, error: scopeError);
        if (!TingeeInvoiceProviders.IsKnown(input.Provider)) ModelState.AddModelError(string.Empty, "Nhà cung cấp hóa đơn không hợp lệ.");
        if (!ModelState.IsValid) return await Page(merchant, accountInput: keep, dialog: TingeeInvoiceSandboxDialogs.Account, status: StatusCodes.Status400BadRequest);
        try
        {
            var id = await invoices.SaveAccountAsync(new TingeeInvoiceAccountRequest
            {
                Id = input.Id,
                MerchantId = scope.MerchantId,
                Provider = input.Provider!,
                TaxCode = input.TaxCode!.Trim(),
                Username = input.Username!.Trim(),
                Password = input.Password!,
                IsDefault = input.IsDefault
            });
            logger.LogInformation("TINGEE_INVOICE_SANDBOX_ACCOUNT_SAVED merchant={Merchant} provider={Provider} account={Account} update={Update}", scope.MerchantId, input.Provider, id, input.Id is not null);
            TempData["Success"] = $"Đã {(input.Id is null ? "kết nối" : "cập nhật")} tài khoản {TingeeInvoiceProviders.Name(input.Provider)} (ID {id}).";
            return RedirectToAction(nameof(Index), new { merchant });
        }
        catch (Exception ex) when (Explain(ex, "create-or-update-account") is string message)
        {
            return await Page(merchant, accountInput: keep, dialog: TingeeInvoiceSandboxDialogs.Account, error: $"Kết nối tài khoản nhà cung cấp không thành công: {message}");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(long id, long? merchant)
    {
        if (id <= 0) return BadRequest();
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        try
        {
            await invoices.DeleteAccountAsync(scope.MerchantId, id);
            logger.LogInformation("TINGEE_INVOICE_SANDBOX_ACCOUNT_DELETED merchant={Merchant} account={Account}", scope.MerchantId, id);
            TempData["Success"] = $"Đã xóa tài khoản nhà cung cấp ID {id}.";
        }
        catch (Exception ex) when (Explain(ex, "delete-account") is string message) { TempData["Error"] = $"Xóa tài khoản không thành công: {message}"; }
        return RedirectToAction(nameof(Index), new { merchant });
    }

    /// <summary>Lists the templates (mẫu số + ký hiệu) registered at the provider; picking one fills the invoice form.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Templates(long? accountId, int? invoiceNoToCheck, long? merchant)
    {
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        if (invoiceNoToCheck is <= 0) invoiceNoToCheck = null;
        try
        {
            var templates = await invoices.GetTemplatesAsync(scope.MerchantId, accountId, invoiceNoToCheck);
            return await Page(merchant, templatesAccount: accountId ?? 0, templates: templates, invoice: NewInvoice(accountId));
        }
        catch (Exception ex) when (Explain(ex, "invoice-templates") is string message)
        {
            return await Page(merchant, invoice: NewInvoice(accountId), error: $"Lấy mẫu hóa đơn không thành công: {message}");
        }
    }

    /// <summary>Draft only: Tingee returns the PDF preview, nothing is signed or sent to the tax authority.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Draft([Bind(Prefix = "Invoice")] TingeeInvoiceSandboxInput input, long? merchant)
    {
        var (scope, request, problem) = await Prepare(input, merchant);
        if (request is null) return await Page(merchant, invoice: input, error: problem, status: StatusCodes.Status400BadRequest);
        try
        {
            var pdf = await invoices.CreateDraftAsync(request);
            if (string.IsNullOrWhiteSpace(pdf.PdfBase64)) throw new TingeeApiException("EMPTY", "Tingee không trả về file PDF.");
            logger.LogInformation("TINGEE_INVOICE_SANDBOX_DRAFT merchant={Merchant} code={Code} lines={Lines}", scope!.MerchantId, request.InvoiceCode, request.Items.Count);
            return await Page(merchant, invoice: input, draft: new(pdf.InvoiceCode ?? request.InvoiceCode, pdf.PdfBase64));
        }
        catch (Exception ex) when (Explain(ex, "create") is string message)
        {
            return await Page(merchant, invoice: input, error: $"Tạo hóa đơn nháp không thành công: {message}");
        }
    }

    /// <summary>Signs and issues a real invoice at the provider (HSM). Requires the explicit confirmation box.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue([Bind(Prefix = "Invoice")] TingeeInvoiceSandboxInput input, long? merchant)
    {
        if (!input.ConfirmIssue)
            return await Page(merchant, invoice: input, error: "Tích ô xác nhận phát hành: hóa đơn được ký và gửi cơ quan thuế, không xóa được.", status: StatusCodes.Status400BadRequest);
        var (scope, request, problem) = await Prepare(input, merchant);
        if (request is null) return await Page(merchant, invoice: input, error: problem, status: StatusCodes.Status400BadRequest);
        try
        {
            var issued = await invoices.CreateAndIssueAsync(request);
            logger.LogInformation("TINGEE_INVOICE_SANDBOX_ISSUED merchant={Merchant} code={Code} number={Number}", scope!.MerchantId, request.InvoiceCode, issued.InvoiceNumber);
            TempData["Success"] = $"Đã phát hành hóa đơn {issued.InvoiceNumber ?? request.InvoiceCode}.";
            // A fresh code for the next test: re-sending an issued code is refused by Tingee (2034).
            var next = NewInvoice(input.AccountId);
            next.Pattern = input.Pattern;
            next.Series = input.Series;
            return await Page(merchant, invoice: next, issued: issued with { InvoiceCode = issued.InvoiceCode ?? request.InvoiceCode });
        }
        catch (Exception ex) when (Explain(ex, "create-and-issue") is string message)
        {
            input.ConfirmIssue = false;
            return await Page(merchant, invoice: input, error: $"Phát hành hóa đơn không thành công: {message}");
        }
    }

    /// <summary>PDF of an issued invoice by our invoice code; view=true opens it in the browser instead of saving it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Download(string? invoiceCode, long? accountId, bool view, long? merchant)
    {
        invoiceCode = invoiceCode?.Trim();
        if (string.IsNullOrEmpty(invoiceCode)) return await Page(merchant, error: "Nhập mã hóa đơn cần tải.", status: StatusCodes.Status400BadRequest);
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        try
        {
            var pdf = await invoices.DownloadAsync(scope.MerchantId, invoiceCode, accountId);
            if (string.IsNullOrWhiteSpace(pdf.PdfBase64)) throw new TingeeApiException("EMPTY", "Tingee không trả về file PDF.");
            var bytes = Convert.FromBase64String(pdf.PdfBase64);
            return view ? File(bytes, "application/pdf") : File(bytes, "application/pdf", $"{SafeFileName(invoiceCode)}.pdf");
        }
        catch (FormatException) { return await Page(merchant, error: "Tingee trả về PDF không đúng định dạng Base64."); }
        catch (Exception ex) when (Explain(ex, "download") is string message) { return await Page(merchant, error: $"Tải hóa đơn không thành công: {message}"); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendEmail(string? invoiceCode, string? email, long? accountId, long? merchant)
    {
        invoiceCode = invoiceCode?.Trim();
        email = email?.Trim();
        if (string.IsNullOrEmpty(invoiceCode) || string.IsNullOrEmpty(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return await Page(merchant, error: "Nhập mã hóa đơn và email người nhận hợp lệ.", status: StatusCodes.Status400BadRequest);
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        try
        {
            await invoices.SendEmailAsync(scope.MerchantId, invoiceCode, email, accountId: accountId);
            logger.LogInformation("TINGEE_INVOICE_SANDBOX_EMAIL merchant={Merchant} code={Code}", scope.MerchantId, invoiceCode);
            TempData["Success"] = $"Đã yêu cầu gửi hóa đơn {invoiceCode} qua email.";
        }
        catch (Exception ex) when (Explain(ex, "send-email") is string message) { TempData["Error"] = $"Gửi email không thành công: {message}"; }
        return RedirectToAction(nameof(Index), new { merchant });
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private sealed record Scope(long MerchantId, string Label);

    /// <summary>Same scope as the Tingee Sandbox: Direct = T.A's own merchant; Master = a linked child merchant, never a free id.</summary>
    private async Task<(Scope? Scope, IReadOnlyList<TingeeSandboxMerchant> Linked, string? Error)> Resolve(long? merchant)
    {
        if (options.Value.IsDirectMerchant) return (new(0, "Merchant của T.A (chế độ Direct)"), [], null);
        IReadOnlyList<TingeeSandboxMerchant> linked;
        try { linked = await merchants.LinkedAsync(); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "TINGEE_INVOICE_SANDBOX_MERCHANTS_UNREADABLE");
            return (null, [], "Không đọc được bảng dbo.TingeeMerchants (danh sách merchant con).");
        }
        if (linked.Count == 0)
            return (null, linked, "Chưa có merchant con nào: vào Khách hàng → chọn khách hàng → tab Tingee → \"Tạo tài khoản Tingee qua API\", rồi quay lại đây.");
        var pick = merchant is long id ? linked.FirstOrDefault(m => m.MerchantId == id) : linked.Count == 1 ? linked[0] : null;
        return pick is null ? (null, linked, "Chọn merchant con để test (mục 1).")
            : (new(pick.MerchantId, $"{pick.CustomerName} — merchant #{pick.MerchantId}"), linked, null);
    }

    /// <summary>Validates the invoice form and builds the Tingee body; amounts travel as invariant strings as Tingee documents.</summary>
    private async Task<(Scope? Scope, TingeeInvoiceRequest? Request, string? Problem)> Prepare(TingeeInvoiceSandboxInput input, long? merchant)
    {
        if (Refusal() is string refused) return (null, null, refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return (null, null, scopeError);
        var lines = input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "Nhập ít nhất một dòng hàng hóa/dịch vụ.");
        if (lines.Any(l => !TingeeTaxRates.IsKnown(l.TaxRate))) ModelState.AddModelError(string.Empty, "Thuế suất không hợp lệ.");
        if (string.IsNullOrWhiteSpace(input.BuyerName) && string.IsNullOrWhiteSpace(input.BuyerLegalName))
            ModelState.AddModelError(string.Empty, "Nhập tên người mua hoặc tên đơn vị mua (một số nhà cung cấp từ chối hóa đơn thiếu cả hai).");
        if (!ModelState.IsValid) return (scope, null, null);
        return (scope, new TingeeInvoiceRequest
        {
            MerchantId = scope.MerchantId,
            AccountId = input.AccountId is > 0 ? input.AccountId : null,
            InvoicePattern = input.Pattern!.Trim(),
            InvoiceSeries = input.Series!.Trim(),
            InvoiceCode = input.Code!.Trim(),
            InvoiceDate = input.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BuyerTaxCode = Blank(input.BuyerTaxCode),
            BuyerName = Blank(input.BuyerName),
            BuyerLegalName = Blank(input.BuyerLegalName),
            BuyerAddress = Blank(input.BuyerAddress),
            BuyerEmail = Blank(input.BuyerEmail),
            PaymentMethod = input.PaymentMethod,
            Note = Blank(input.Note),
            Items = lines.Select(l => new TingeeInvoiceItem
            {
                ItemName = l.Name!.Trim(),
                UnitName = Blank(l.Unit),
                Quantity = Number(l.Quantity),
                UnitPrice = Number(l.UnitPrice),
                TaxRateCode = l.TaxRate
            }).ToList()
        }, null);
    }

    private TingeeInvoiceSandboxInput NewInvoice(long? accountId = null)
    {
        var now = clock.GetUtcNow().ToOffset(TingeeSignature.VietnamOffset);
        return new TingeeInvoiceSandboxInput
        {
            AccountId = accountId,
            Code = $"TASBX-{now:yyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
            Date = DateOnly.FromDateTime(now.DateTime),
            BuyerName = "Khách hàng thử",
            Note = "Hóa đơn thử nghiệm T.A Sandbox",
            Lines = [new() { Name = "Dịch vụ thử nghiệm", Unit = "Gói", Quantity = 1, UnitPrice = 10000, TaxRate = "10" }]
        };
    }

    private async Task<IActionResult> Page(long? merchant, TingeeInvoiceAccountInput? accountInput = null, long? templatesAccount = null,
        IReadOnlyList<TingeeInvoiceTemplate>? templates = null, TingeeInvoiceSandboxInput? invoice = null, TingeeInvoiceSandboxDraft? draft = null,
        TingeeIssuedInvoice? issued = null, string? error = null, int? status = null, string? dialog = null)
    {
        if (error is not null) ModelState.AddModelError(string.Empty, error);
        var config = Config();
        var (scope, linked, scopeError) = config.Configured ? await Resolve(merchant) : (null, [], null);
        IReadOnlyList<TingeeInvoiceProvider> providers = [];
        IReadOnlyList<TingeeInvoiceAccount>? accounts = null;
        string? accountsError = null;
        if (scope is not null)
        {
            try
            {
                accounts = (await invoices.GetAccountsAsync(scope.MerchantId)).Items;
                providers = await invoices.GetProvidersAsync();
            }
            catch (Exception ex) when (Explain(ex, "get-paging-account") is string message) { accountsError = message; }
        }
        invoice ??= NewInvoice();
        while (invoice.Lines.Count < TingeeInvoiceSandboxInput.LineCount) invoice.Lines.Add(new TingeeInvoiceSandboxLine { Quantity = 1, UnitPrice = 0 });
        if (status is int code) Response.StatusCode = code;
        else if (error is not null) Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Index", new TingeeInvoiceSandboxVm(config, linked, config.Direct ? null : scope?.MerchantId, scope?.Label, scopeError, providers, accounts, accountsError,
            accountInput ?? new TingeeInvoiceAccountInput(), templatesAccount, templates, invoice, draft, issued, dialog));
    }

    private TingeeSandboxConfig Config()
    {
        var o = options.Value;
        var production = Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && string.Equals(u.Host, new Uri(TingeeOptions.ProductionBaseUrl).Host, StringComparison.OrdinalIgnoreCase);
        return new(o.IsConfigured, o.IsDirectMerchant, o.BaseUrl, production, webhookInbox.IsAvailable,
            o.WebhookUrl("payment"), o.WebhookUrl("va-status"), o.WebhookUrl("bank-link"), o.EffectiveVaStatusWebhookUrl, o.MerchantId is > 0 ? o.MerchantId : null);
    }

    private string? Refusal() => options.Value.IsConfigured ? null : "Chưa cấu hình Tingee__ClientId/Tingee__SecretKey cho site Portal.";

    /// <summary>Message for expected Tingee/network failures; null lets anything else propagate as a real error.</summary>
    private string? Explain(Exception ex, string operation)
    {
        string? message;
        switch (ex)
        {
            case TingeeApiException api:
                logger.LogWarning("TINGEE_INVOICE_SANDBOX_REJECTED operation={Operation} code={Code} details={Details}", operation, api.Code, api.Details);
                message = $"Tingee trả mã {api.Code}: {api.TingeeMessage}{Hint(api.Code)}{(string.IsNullOrWhiteSpace(api.Details) ? "" : $" — chi tiết: {api.Details}")}";
                break;
            case TingeeNotConfiguredException: message = "Chưa cấu hình khóa Tingee."; break;
            case HttpRequestException or TaskCanceledException:
                logger.LogWarning(ex, "TINGEE_INVOICE_SANDBOX_UNREACHABLE operation={Operation}", operation);
                message = $"Không kết nối được Tingee ({options.Value.BaseUrl}).";
                break;
            default: return null;
        }
        return message + (TingeeLogCode.Of(ex) is string code ? $" — Mã log: {code} (tra cứu tại Tingee log)" : "");
    }

    /// <summary>What to do for the e-invoice codes Tingee documents.</summary>
    private static string Hint(string code) => code switch
    {
        "2032" => " (chưa có tài khoản nhà cung cấp mặc định: kết nối tài khoản ở mục 2 hoặc chọn tài khoản)",
        "2034" => " (mã hóa đơn này đã phát hành trước đó: dùng mã mới, hoặc tải/gửi email hóa đơn cũ)",
        "2035" => " (hóa đơn chưa được phát hành thành công)",
        "2037" => " (sai thông tin đăng nhập hoặc tài khoản chưa đăng ký hóa đơn đầu ra tại nhà cung cấp)",
        "2038" => " (nhà cung cấp không hỗ trợ loại dòng hàng này)",
        "2007" => " (nhà cung cấp không hỗ trợ chức năng này)",
        "1057" => " (không tìm thấy tài khoản nhà cung cấp theo ID)",
        "1005" => " (HiLo cần \"Số hóa đơn cần kiểm tra\" lớn hơn 0)",
        _ => ""
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Number(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string SafeFileName(string code) => new(code.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').Take(60).ToArray());
}
