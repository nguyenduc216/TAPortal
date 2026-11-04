using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Tingee Sandbox: T.A tests its Tingee account (ClientId/SecretKey in the Admin site's server configuration) end to end:
// connection, bank account linking with OTP, a test QR whose payment status is polled from Tingee, and the webhooks Tingee
// sends to /hooks/tingee/* on this site. With a Master Merchant key (Tingee__Mode=Master) every call acts on one child
// merchant that is already linked to a customer in Admin; with a normal Merchant key (Direct) on T.A's own merchant.
// Nothing is stored here except what Tingee itself sends to the webhook inbox.
[Authorize(Policy = "SystemAdminOnly")]
public sealed partial class TingeeSandboxController(
    ITingeeApi tingee,
    IOptions<TingeeOptions> options,
    ITingeeSandboxInbox inbox,
    ITingeeSandboxMerchants merchants,
    ITingeeWebhookInbox webhookInbox,
    IHttpClientFactory httpClients,
    TimeProvider clock,
    ILogger<TingeeSandboxController> logger) : Controller
{
    private const int QrLifetimeMinutes = 15;

    [HttpGet]
    public async Task<IActionResult> Index(string? bin, string? type, long? merchant) =>
        await Page(merchant, bankInput: new TingeeSandboxBankInput { Bin = bin, AccountType = type }, dialog: string.IsNullOrEmpty(bin) ? null : TingeeSandboxDialogs.Link);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkBank([Bind(Prefix = "Bank")] TingeeSandboxBankInput input, long? merchant)
    {
        var bank = TingeeBankCatalog.Find(input.Bin);
        var type = bank is not null && input.AccountType is not null && bank.AccountTypes.Contains(input.AccountType) ? input.AccountType : null;
        if (Refusal() is string refused) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, error: scopeError);
        if (options.Value.IsDirectMerchant && options.Value.MerchantId is not > 0) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, error: "Tingee yêu cầu Merchant ID khi liên kết ngân hàng (mã 1096): thêm \"MerchantId\" vào mục Tingee trong appsettings.json, hoặc dùng Mode=Master với merchant con.");
        if (bank is null || type is null) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, error: "Chọn ngân hàng và loại tài khoản.");
        var rule = bank.Rule(type);
        foreach (var field in rule.Required)
            if (string.IsNullOrWhiteSpace(Value(input, field))) ModelState.AddModelError(string.Empty, $"Thiếu {FieldLabel(field)}.");
        var vaStatusUrl = options.Value.VaStatusWebhookUrl;
        if (rule.AsyncResult && string.IsNullOrWhiteSpace(vaStatusUrl))
            ModelState.AddModelError(string.Empty, $"{bank.Name} gửi kết quả liên kết qua webhook: cần cấu hình Tingee__VaStatusWebhookUrl = {AbsoluteHook("va-status")} trước.");
        if (!ModelState.IsValid) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, status: StatusCodes.Status400BadRequest);

        var notifyRoot = !rule.VaSupported || (rule.SupportsRootNotify && input.NotifyRootAccount);
        var request = new TingeeCreateVaRequest
        {
            MerchantId = scope.MerchantId,
            BankBin = bank.Bin,
            AccountType = type,
            AccountNumber = Use(rule, TingeeBankField.AccountNumber, input.AccountNumber),
            AccountName = Use(rule, TingeeBankField.AccountName, input.AccountName),
            Identity = Use(rule, TingeeBankField.Identity, input.Identity),
            Mobile = Use(rule, TingeeBankField.Mobile, input.Mobile),
            VaSuffix = Use(rule, TingeeBankField.VaSuffix, input.VaSuffix),
            MerchantName = Use(rule, TingeeBankField.MerchantName, input.MerchantName),
            VaAccountNumber = Use(rule, TingeeBankField.VaAccountNumber, input.VaAccountNumber),
            AcbUserId = Use(rule, TingeeBankField.AcbUserId, input.AcbUserId),
            IsNotifyAccountNumber = notifyRoot,
            Name = $"TA SANDBOX - {bank.Code}",
            TaxCode = type == TingeeAccountTypes.Personal || rule.TaxCodeAsIdentity(type) || string.IsNullOrWhiteSpace(input.TaxCode) ? null : input.TaxCode.Trim(),
            RedirectUrl = bank.Code == "VCB" && type == TingeeAccountTypes.Personal ? Url.Action(nameof(Index), "TingeeSandbox", new { merchant }, Request.Scheme) : null,
            WebhookUrl = rule.AsyncResult ? vaStatusUrl : null,
            RequestId = bank.Code == "TPB" ? Guid.NewGuid().ToString("N") : null
        };

        TingeeCreateVaResult result;
        try { result = await tingee.CreateVaAsync(request); }
        catch (Exception ex) when (Explain(ex, "create-va") is string message) { return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: input, error: $"Liên kết {bank.Name} không thành công: {message}"); }

        var link = SafeLink(result.AuthorizeLink ?? result.DeepLink);
        TingeeSandboxLinkResult outcome = result.ConfirmId is not null
            ? new(bank.Name, $"{bank.Name} đã gửi mã OTP{(result.OtpMethod is null ? "" : $" ({result.OtpMethod})")}. Nhập OTP để hoàn tất.", link, new(bank.Bin, bank.Name, result.ConfirmId, result.OtpMethod))
            : link is not null
                ? new(bank.Name, $"Mở liên kết xác thực của {bank.Name} để hoàn tất, sau đó bấm \"Kiểm tra kết nối\".", link, null)
                : new(bank.Name, $"Đã gửi yêu cầu liên kết {bank.Name}{(result.VaAccountNumber is null ? "" : $" (tài khoản nhận: {result.VaAccountNumber})")}.", null, null);
        logger.LogInformation("TINGEE_SANDBOX_VA_REQUESTED merchant={Merchant} bank={Bank} type={Type} otp={Otp}", scope.MerchantId, bank.Code, type, result.ConfirmId is not null);
        return await Page(merchant, dialog: TingeeSandboxDialogs.Link, bankInput: new TingeeSandboxBankInput { Bin = bank.Bin, AccountType = type }, linkResult: outcome);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmOtp(string? bankBin, string? confirmId, string? otp, long? merchant)
    {
        var bank = TingeeBankCatalog.Find(bankBin);
        if (bank is null || string.IsNullOrWhiteSpace(confirmId)) return BadRequest();
        if (Refusal() is string refused) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, dialog: TingeeSandboxDialogs.Link, error: scopeError);
        otp = otp?.Trim();
        var pending = new TingeeSandboxLinkResult(bank.Name, "Nhập lại mã OTP.", null, new(bank.Bin, bank.Name, confirmId, null));
        if (otp is null || !OtpPattern().IsMatch(otp))
            return await Page(merchant, dialog: TingeeSandboxDialogs.Link, linkResult: pending, error: "Mã OTP gồm 4–8 chữ số.", status: StatusCodes.Status400BadRequest);
        try
        {
            var confirmed = await tingee.ConfirmVaAsync(scope.MerchantId, bank.Bin, confirmId, otp);
            TempData["Success"] = $"Đã liên kết {bank.Name}{(confirmed.VaAccountNumber is null ? "" : $", tài khoản nhận {confirmed.VaAccountNumber}")}.";
            return RedirectToAction(nameof(Index), new { merchant });
        }
        catch (Exception ex) when (Explain(ex, "confirm-va") is string message)
        {
            return await Page(merchant, dialog: TingeeSandboxDialogs.Link, linkResult: pending, error: $"Xác nhận OTP không thành công: {message}");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQr([Bind(Prefix = "Qr")] TingeeSandboxQrInput input, long? merchant)
    {
        if (Refusal() is string refused) return await Page(merchant, qrInput: input, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, qrInput: input, error: scopeError);
        if (!ModelState.IsValid) return await Page(merchant, qrInput: input, status: StatusCodes.Status400BadRequest);
        var parts = (input.Account ?? "").Split('|');
        var bank = parts.Length == 3 ? TingeeBankCatalog.Find(parts[0]) : null;
        if (bank is null || string.IsNullOrWhiteSpace(parts[1])) return await Page(merchant, qrInput: input, error: "Tài khoản nhận tiền không hợp lệ; bấm \"Kiểm tra kết nối\" rồi chọn lại.");
        var account = parts[1];
        var purpose = string.IsNullOrWhiteSpace(input.Purpose) ? null : input.Purpose.Trim();
        try
        {
            TingeeSandboxQr qr;
            if (input.Type == "static")
            {
                var made = await tingee.GenerateStaticQrAsync(new TingeeStaticQrRequest(bank.Bin, account, input.Amount, purpose));
                if (string.IsNullOrWhiteSpace(made.QrCode)) throw new TingeeApiException("EMPTY", "Tingee không trả về mã QR.");
                qr = new(QrImage.Svg(made.QrCode), made.QrCode, "static", bank.Name, account, input.Amount, purpose, null, null, null);
            }
            else
            {
                var made = await tingee.GenerateDynamicQrAsync(new TingeeDynamicQrRequest
                {
                    MerchantId = scope.MerchantId, BankBin = bank.Bin, VaAccountNumber = account, Amount = input.Amount,
                    QrCodeType = TingeeQrTypes.OneTime, Purpose = purpose, ExpireInMinute = QrLifetimeMinutes
                });
                if (string.IsNullOrWhiteSpace(made.QrCode)) throw new TingeeApiException("EMPTY", "Tingee không trả về mã QR.");
                qr = new(QrImage.Svg(made.QrCode), made.QrCode, "dynamic", bank.Name, account, input.Amount, purpose, made.QrAccount, made.BillId,
                    clock.GetUtcNow().UtcDateTime.AddMinutes(QrLifetimeMinutes));
            }
            logger.LogInformation("TINGEE_SANDBOX_QR merchant={Merchant} type={Type} bank={Bank} amount={Amount}", scope.MerchantId, qr.Type, bank.Code, input.Amount);
            return await Page(merchant, qrInput: input, qr: qr);
        }
        catch (Exception ex) when (Explain(ex, "qr") is string message)
        {
            var hint = input.Type == "dynamic" && parts[2] is { Length: > 0 } t && !TingeeBankCatalog.SupportsDynamicQr(bank.Bin, t)
                ? $" {bank.Name} có thể không hỗ trợ QR động cho loại tài khoản này; thử \"QR tĩnh\"." : "";
            return await Page(merchant, qrInput: input, error: $"Tạo QR không thành công: {message}{hint}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> QrStatus(string? qrAccount, string? billId, long? merchant)
    {
        if (string.IsNullOrWhiteSpace(qrAccount) || string.IsNullOrWhiteSpace(billId)) return BadRequest();
        if (Refusal() is string refused) return Json(new { ok = false, error = refused });
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return Json(new { ok = false, error = scopeError });
        try
        {
            var bill = (await tingee.GetDynamicQrStatusAsync(scope.MerchantId, qrAccount, billId)).BillInfo;
            return Json(new
            {
                ok = true,
                status = bill?.Status,
                amount = bill?.Amount ?? 0,
                paid = bill?.TotalAmountPaid ?? 0,
                payments = bill?.TotalPaymentsCount ?? 0,
                checkedAt = clock.GetUtcNow().ToOffset(TingeeSignature.VietnamOffset).ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            });
        }
        catch (Exception ex) when (Explain(ex, "qr-status") is string message) { return Json(new { ok = false, error = message }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQr(string? qrAccount, string? billId, long? merchant)
    {
        if (string.IsNullOrWhiteSpace(qrAccount) || string.IsNullOrWhiteSpace(billId)) return BadRequest();
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        try { await tingee.DeleteDynamicQrAsync(scope.MerchantId, qrAccount, billId); TempData["Success"] = "Đã hủy QR thử."; }
        catch (Exception ex) when (Explain(ex, "delete-qr") is string message) { TempData["Error"] = $"Hủy QR không thành công: {message}"; }
        return RedirectToAction(nameof(Index), new { merchant });
    }

    /// <summary>Removes a bank link (delete-va); some banks answer with an OTP or a bank page to finish.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVa(string? bankBin, string? vaAccountNumber, long? merchant)
    {
        var bank = TingeeBankCatalog.Find(bankBin);
        if (bank is null || string.IsNullOrWhiteSpace(vaAccountNumber)) return BadRequest();
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        var va = vaAccountNumber.Trim();
        TingeeDeleteVaResult result;
        try { result = await tingee.DeleteVaAsync(scope.MerchantId, bank.Bin, va); }
        catch (Exception ex) when (Explain(ex, "delete-va") is string message)
        {
            return await Page(merchant, error: $"Xóa liên kết {bank.Name} không thành công: {message}", dialog: TingeeSandboxDialogs.Unlink, unlink: new(bank.Bin, bank.Name, va, null, null));
        }
        logger.LogInformation("TINGEE_SANDBOX_VA_DELETE merchant={Merchant} bank={Bank} otp={Otp}", scope.MerchantId, bank.Code, result.ConfirmId is not null);
        var link = SafeLink(result.AuthorizeLink);
        if (result.ConfirmId is not null || link is not null)
            return await Page(merchant, dialog: TingeeSandboxDialogs.Unlink, unlink: new(bank.Bin, bank.Name, va, result.ConfirmId, link));
        TempData["Success"] = $"Đã xóa liên kết {bank.Name} — {va}.";
        return RedirectToAction(nameof(Index), new { merchant });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmDeleteVa(string? bankBin, string? vaAccountNumber, string? confirmId, string? otp, long? merchant)
    {
        var bank = TingeeBankCatalog.Find(bankBin);
        if (bank is null || string.IsNullOrWhiteSpace(vaAccountNumber) || string.IsNullOrWhiteSpace(confirmId)) return BadRequest();
        if (Refusal() is string refused) return await Page(merchant, error: refused);
        var (scope, _, scopeError) = await Resolve(merchant);
        if (scope is null) return await Page(merchant, error: scopeError);
        var pending = new TingeeSandboxUnlink(bank.Bin, bank.Name, vaAccountNumber.Trim(), confirmId, null);
        otp = otp?.Trim();
        if (otp is null || !OtpPattern().IsMatch(otp))
            return await Page(merchant, error: "Mã OTP gồm 4–8 chữ số.", status: StatusCodes.Status400BadRequest, dialog: TingeeSandboxDialogs.Unlink, unlink: pending);
        try
        {
            await tingee.ConfirmDeleteVaAsync(scope.MerchantId, bank.Bin, confirmId, otp);
            TempData["Success"] = $"Đã xóa liên kết {bank.Name} — {pending.VaAccountNumber}.";
            return RedirectToAction(nameof(Index), new { merchant });
        }
        catch (Exception ex) when (Explain(ex, "confirm-delete-va") is string message)
        {
            return await Page(merchant, error: $"Xác nhận OTP không thành công: {message}", dialog: TingeeSandboxDialogs.Unlink, unlink: pending);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Webhooks()
    {
        var (rows, error) = await RecentWebhooks();
        return PartialView("_Webhooks", (rows, error));
    }

    /// <summary>
    /// Posts a bank-link webhook signed with this site's own SecretKey to this site's public URL: proves the public
    /// address, the signature check and the inbox insert work before Tingee is asked to call it. No money is involved.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelfTestWebhook(long? merchant)
    {
        if (!options.Value.IsConfigured) { TempData["Error"] = "Chưa cấu hình Tingee__ClientId/Tingee__SecretKey."; return RedirectToAction(nameof(Index), new { merchant }); }
        var url = AbsoluteHook("bank-link");
        var body = JsonSerializer.Serialize(new { sandboxSelfTest = true, requestedBy = User.Identity?.Name, at = clock.GetUtcNow().UtcDateTime });
        var timestamp = TingeeSignature.Timestamp(clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-request-timestamp", timestamp);
        request.Headers.Add("x-signature", TingeeSignature.Sign(timestamp, body, options.Value.SecretKey));
        request.Headers.Add("x-request-id", "sandbox-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var response = await httpClients.CreateClient(nameof(TingeeSandboxController)).SendAsync(request);
            if (response.IsSuccessStatusCode) TempData["Success"] = $"Webhook thử đã tới {url} và được lưu (HTTP {(int)response.StatusCode}).";
            else TempData["Error"] = $"Webhook thử tới {url} bị từ chối: HTTP {(int)response.StatusCode}. 503 = thiếu key hoặc không ghi được bảng TingeeWebhookInbox; 401 = sai chữ ký/giờ.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "TINGEE_SANDBOX_SELFTEST_UNREACHABLE url={Url}", url);
            TempData["Error"] = $"Server không gọi được chính địa chỉ {url} ({ex.Message}). Kiểm tra DNS/NAT; Tingee gọi từ internet vẫn có thể tới được.";
        }
        return RedirectToAction(nameof(Index), new { merchant });
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private sealed record Scope(long MerchantId, string Label);

    /// <summary>
    /// The merchant every Tingee call acts on. Direct: T.A's own (TingeeApi adds Tingee:MerchantId). Master: one of the child
    /// merchants already linked to a customer in Admin (dbo.TingeeMerchants) — never an arbitrary id from the request.
    /// </summary>
    private async Task<(Scope? Scope, IReadOnlyList<TingeeSandboxMerchant> Linked, string? Error)> Resolve(long? merchant)
    {
        if (options.Value.IsDirectMerchant) return (new(0, "Merchant của T.A (chế độ Direct)"), [], null);
        IReadOnlyList<TingeeSandboxMerchant> linked;
        try { linked = await merchants.LinkedAsync(); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "TINGEE_SANDBOX_MERCHANTS_UNREADABLE");
            return (null, [], "Không đọc được bảng dbo.TingeeMerchants (danh sách merchant con).");
        }
        if (linked.Count == 0)
            return (null, linked, "Chưa có merchant con nào: vào Khách hàng → chọn khách hàng T.A → tab Tingee → \"Tạo tài khoản Tingee qua API\", rồi quay lại đây.");
        var pick = merchant is long id ? linked.FirstOrDefault(m => m.MerchantId == id) : linked.Count == 1 ? linked[0] : null;
        return pick is null ? (null, linked, "Chọn merchant con để test (mục 1).")
            : (new(pick.MerchantId, $"{pick.CustomerName} — merchant #{pick.MerchantId}"), linked, null);
    }

    private async Task<IActionResult> Page(long? merchant, TingeeSandboxBankInput? bankInput = null, TingeeSandboxLinkResult? linkResult = null,
        TingeeSandboxQrInput? qrInput = null, TingeeSandboxQr? qr = null, string? error = null, int? status = null,
        string? dialog = null, TingeeSandboxUnlink? unlink = null)
    {
        if (error is not null) ModelState.AddModelError(string.Empty, error);
        bankInput ??= new TingeeSandboxBankInput();
        var bank = TingeeBankCatalog.Find(bankInput.Bin);
        var type = bank is not null && bankInput.AccountType is not null && bank.AccountTypes.Contains(bankInput.AccountType) ? bankInput.AccountType : null;
        var config = Config();
        var (scope, linked, scopeError) = config.Configured ? await Resolve(merchant) : (null, [], null);
        TingeeSandboxConnection? connection = null;
        if (scope is not null)
        {
            try { connection = new(true, (await tingee.GetVaPageAsync(scope.MerchantId)).Items, null); }
            catch (Exception ex) when (Explain(ex, "get-va-paging") is string message) { connection = new(false, [], message); }
        }
        var (rows, webhookError) = await RecentWebhooks();
        if (status is int code) Response.StatusCode = code;
        else if (error is not null) Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Index", new TingeeSandboxVm(config, linked, config.Direct ? null : scope?.MerchantId, scope?.Label, scopeError, connection, bank, type, bankInput,
            linkResult, qrInput ?? new TingeeSandboxQrInput(), qr, rows, webhookError, dialog, unlink));
    }

    private async Task<(IReadOnlyList<TingeeSandboxWebhook> Rows, string? Error)> RecentWebhooks()
    {
        if (!webhookInbox.IsAvailable) return ([], "Chưa cấu hình kết nối cơ sở dữ liệu cho hộp thư webhook.");
        try { return (await inbox.RecentAsync(30), null); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "TINGEE_SANDBOX_INBOX_UNREADABLE");
            return ([], "Không đọc được bảng dbo.TingeeWebhookInbox (kiểm tra migration 011 và quyền SELECT/INSERT của tài khoản SQL của site Portal).");
        }
    }

    private TingeeSandboxConfig Config()
    {
        var o = options.Value;
        var production = Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var u) && string.Equals(u.Host, new Uri(TingeeOptions.ProductionBaseUrl).Host, StringComparison.OrdinalIgnoreCase);
        return new(o.IsConfigured, o.IsDirectMerchant, o.BaseUrl, production, webhookInbox.IsAvailable,
            AbsoluteHook("payment"), AbsoluteHook("va-status"), AbsoluteHook("bank-link"), o.VaStatusWebhookUrl, o.MerchantId is > 0 ? o.MerchantId : null);
    }

    private string? Refusal()
    {
        var o = options.Value;
        return o.IsConfigured ? null : "Chưa cấu hình Tingee__ClientId/Tingee__SecretKey cho site Portal.";
    }

    private string AbsoluteHook(string kind) => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/hooks/tingee/{kind}";

    /// <summary>Message for expected Tingee/network failures; null lets anything else propagate as a real error.</summary>
    private string? Explain(Exception ex, string operation) =>
        Describe(ex, operation) is string message ? message + (TingeeLogCode.Of(ex) is string code ? $" — Mã log: {code} (tra cứu tại Tingee log)" : "") : null;

    private string? Describe(Exception ex, string operation)
    {
        switch (ex)
        {
            case TingeeApiException api:
                logger.LogWarning("TINGEE_SANDBOX_REJECTED operation={Operation} code={Code} details={Details}", operation, api.Code, api.Details);
                return $"Tingee trả mã {api.Code}: {api.TingeeMessage}{(string.IsNullOrWhiteSpace(api.Details) ? "" : $" — chi tiết: {api.Details}")}";
            case TingeeNotConfiguredException: return "Chưa cấu hình khóa Tingee.";
            case HttpRequestException or TaskCanceledException:
                logger.LogWarning(ex, "TINGEE_SANDBOX_UNREACHABLE operation={Operation}", operation);
                return $"Không kết nối được Tingee ({options.Value.BaseUrl}).";
            default: return null;
        }
    }

    private static string? Use(TingeeBankRule rule, TingeeBankField field, string? value) =>
        rule.Required.Contains(field) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? Value(TingeeSandboxBankInput i, TingeeBankField field) => field switch
    {
        TingeeBankField.AccountNumber => i.AccountNumber,
        TingeeBankField.AccountName => i.AccountName,
        TingeeBankField.Identity => i.Identity,
        TingeeBankField.Mobile => i.Mobile,
        TingeeBankField.VaSuffix => i.VaSuffix,
        TingeeBankField.MerchantName => i.MerchantName,
        TingeeBankField.VaAccountNumber => i.VaAccountNumber,
        TingeeBankField.AcbUserId => i.AcbUserId,
        _ => null
    };

    public static string FieldLabel(TingeeBankField field) => field switch
    {
        TingeeBankField.AccountNumber => "số tài khoản",
        TingeeBankField.AccountName => "tên chủ tài khoản",
        TingeeBankField.Identity => "số CCCD/giấy tờ",
        TingeeBankField.Mobile => "số điện thoại đăng ký ngân hàng",
        TingeeBankField.VaSuffix => "hậu tố tài khoản ảo",
        TingeeBankField.MerchantName => "tên hiển thị",
        TingeeBankField.VaAccountNumber => "số tài khoản ảo",
        TingeeBankField.AcbUserId => "ACB user id",
        _ => field.ToString()
    };

    /// <summary>Bank-supplied links are shown as buttons; only http(s) is allowed.</summary>
    private static string? SafeLink(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : null;

    [GeneratedRegex("^[0-9]{4,8}$")]
    private static partial Regex OtpPattern();
}
