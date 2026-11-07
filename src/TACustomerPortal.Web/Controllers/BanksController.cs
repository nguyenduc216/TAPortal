using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

// Bank accounts linked through Tingee with the customer's own form (no Tingee SDK): create-va → OTP confirm-va, or an
// asynchronous bank-side confirmation (VCB/TPBank). Calls use T.A's Master Merchant key with the customer's merchantId.
public sealed partial class BanksController(ICurrentCustomerContext customer, IPaymentStore store, ITingeeApi tingee, Microsoft.Extensions.Options.IOptions<TingeeOptions> tingeeOptions, TimeProvider clock, ILogger<BanksController> logger)
    : CustomerAreaController(customer)
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(4);

    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        var merchant = await store.MerchantIdAsync(m.CustomerId);
        return View(new BanksVm(m, merchant is not null, tingee.IsConfigured, CanManage(m), await store.BankAccountsAsync(m.CustomerId)));
    }

    [HttpGet]
    public async Task<IActionResult> Add(string? bin, string? type)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var bank = TingeeBankCatalog.Find(bin);
        type = bank is not null && type is not null && bank.AccountTypes.Contains(type) ? type : null;
        return View(new BankAddVm(m, await store.MerchantIdAsync(m.CustomerId) is not null, tingee.IsConfigured, bank, type, new BankLinkInput { Bin = bank?.Bin, AccountType = type }));
    }

    [HttpPost]
    public async Task<IActionResult> Add(BankLinkInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var merchant = await store.MerchantIdAsync(m.CustomerId);
        var bank = TingeeBankCatalog.Find(input.Bin);
        var type = bank is not null && input.AccountType is not null && bank.AccountTypes.Contains(input.AccountType) ? input.AccountType : null;
        IActionResult Fail(string? message = null)
        {
            if (message is not null) ModelState.AddModelError(string.Empty, PartnerText.Scrub(message));
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new BankAddVm(m, merchant is not null, tingee.IsConfigured, bank, type, input));
        }

        if (bank is null || type is null) return Fail("Vui lòng chọn ngân hàng và loại tài khoản.");
        if (merchant is not long merchantId) return Fail("Doanh nghiệp chưa được kích hoạt dịch vụ thanh toán.");
        if (!tingee.IsConfigured) return Fail("Dịch vụ liên kết ngân hàng chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        var rule = bank.Rule(type);
        Normalize(input);
        Validate(rule, input);
        var webhookUrl = tingeeOptions.Value.EffectiveVaStatusWebhookUrl;
        if (rule.AsyncResult && string.IsNullOrWhiteSpace(webhookUrl))
            ModelState.AddModelError(string.Empty, $"Chưa liên kết được {bank.Name} lúc này vì hệ thống chưa hoàn tất cấu hình. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        if (!ModelState.IsValid) return Fail();

        var notifyRoot = !rule.VaSupported || (rule.SupportsRootNotify && input.NotifyRootAccount);
        var request = new TingeeCreateVaRequest
        {
            MerchantId = merchantId,
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
            Name = $"{m.Code} - {bank.Code}",
            TaxCode = type == TingeeAccountTypes.Personal || rule.TaxCodeAsIdentity(type) ? null : m.TaxCode,
            RedirectUrl = bank.Code == "VCB" && type == TingeeAccountTypes.Personal ? Url.Action(nameof(Index), "Banks", null, Request.Scheme) : null,
            WebhookUrl = rule.AsyncResult ? webhookUrl : null,
            RequestId = bank.Code == "TPB" ? Guid.NewGuid().ToString("N") : null
        };

        TingeeCreateVaResult result;
        try { result = await tingee.CreateVaAsync(request); }
        catch (TingeeApiException ex) { return Fail($"Liên kết không thành công (mã {ex.Code}): {ex.TingeeMessage}{(ex.LogCode is null ? "" : $" — Mã log: {ex.LogCode}")}"); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Tingee unreachable (create-va) for customer {CustomerId}.", m.CustomerId); return Fail("Không kết nối được dịch vụ thanh toán. Vui lòng thử lại sau."); }

        var now = clock.GetUtcNow().UtcDateTime;
        var bankLink = result.AuthorizeLink ?? result.DeepLink;
        string status;
        string? link = null;
        if (result.ConfirmId is not null) { status = BankStatus.PendingOtp; link = result.DeepLink; }
        else if (bankLink is not null) { status = BankStatus.PendingBank; link = bankLink; }
        else status = result.VaAccountNumber is not null || result.Status == "active" ? BankStatus.Active : BankStatus.PendingBank;
        var account = new BankAccount(Guid.Empty, bank.Bin, bank.Code, bank.Name, type, input.AccountNumber, input.AccountName,
            result.VaAccountNumber ?? (notifyRoot ? input.AccountNumber : input.VaAccountNumber), notifyRoot, result.ShopId, status,
            result.ConfirmId, result.ConfirmId is null ? null : now + OtpLifetime, link, null, false, now);
        var id = await store.AddBankAccountAsync(m.CustomerId, account, Actor);
        switch (status)
        {
            case BankStatus.PendingOtp:
                return RedirectToAction(nameof(Confirm), new { id });
            case BankStatus.Active:
                Flash($"Đã liên kết tài khoản {bank.Name}.");
                break;
            default:
                Flash(link is null ? $"Đã gửi yêu cầu liên kết {bank.Name}. Bấm \"Đồng bộ trạng thái\" sau khi ngân hàng xác nhận."
                    : $"Đã gửi yêu cầu liên kết {bank.Name}. Mở liên kết xác thực của ngân hàng trong danh sách để hoàn tất.");
                break;
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Picking a bank logo opens the partner's own linking popup (Bank Link SDK): account type, details and OTP are asked
    /// there. Returns the session URL and the SDK script as JSON; when the popup reports success the page posts Sync, which
    /// adds the new account from the partner's list. The Add form above stays as the fallback without JavaScript.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LinkSession(string? bin)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        IActionResult Fail(string message) => Json(new { ok = false, error = PartnerText.Scrub(message) });
        var bank = TingeeBankCatalog.Find(bin);
        if (bank is null) return Fail("Vui lòng chọn ngân hàng.");
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId) return Fail("Doanh nghiệp chưa được kích hoạt dịch vụ thanh toán.");
        if (!tingee.IsConfigured) return Fail("Dịch vụ liên kết ngân hàng chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        string url;
        try
        {
            url = await tingee.CreateBankLinkSessionAsync(new TingeeBankLinkSessionRequest
            {
                MerchantId = merchantId, Type = TingeeBankLinkTypes.Link, BankName = bank.Code,
                Metadata = JsonSerializer.Serialize(new { source = "ta-customer", customer = m.CustomerId, bank = bank.Code })
            });
        }
        catch (TingeeApiException ex) { return Fail($"Chưa mở được màn hình liên kết (mã {ex.Code}): {ex.TingeeMessage}{(ex.LogCode is null ? "" : $" — Mã log: {ex.LogCode}")}"); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Tingee unreachable (create-bank-link-session) for customer {CustomerId}.", m.CustomerId); return Fail("Không kết nối được dịch vụ thanh toán. Vui lòng thử lại sau."); }
        if (!TingeeOptions.IsBankLinkSdkUrl(url)) return Fail("Dịch vụ liên kết trả về địa chỉ không hợp lệ. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động.");
        logger.LogInformation("BANK_LINK_SESSION customer={CustomerId} bank={Bank}", m.CustomerId, bank.Code);
        return Json(new { ok = true, url, script = tingeeOptions.Value.BankLinkSdkScript });
    }

    [HttpGet]
    public async Task<IActionResult> Confirm(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var account = await store.BankAccountAsync(m.CustomerId, id);
        if (account is null || account.ConfirmId is null || account.Status is not (BankStatus.PendingOtp or BankStatus.PendingDelete)) return NotFound();
        return View(new BankConfirmVm(account, account.Status == BankStatus.PendingDelete));
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(Guid id, [FromForm] string? otp)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var account = await store.BankAccountAsync(m.CustomerId, id);
        if (account is null || account.ConfirmId is null || account.Status is not (BankStatus.PendingOtp or BankStatus.PendingDelete)) return NotFound();
        var deleting = account.Status == BankStatus.PendingDelete;
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId) return NotFound();
        otp = otp?.Trim();
        if (otp is null || !OtpPattern().IsMatch(otp))
        {
            ModelState.AddModelError("otp", "Mã OTP gồm 4–8 chữ số.");
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new BankConfirmVm(account, deleting));
        }
        try
        {
            if (deleting)
            {
                await tingee.ConfirmDeleteVaAsync(merchantId, account.BankBin, account.ConfirmId, otp);
                await store.UpdateBankAccountAsync(m.CustomerId, account with { Status = BankStatus.Deleted, ConfirmId = null, ConfirmExpiresAt = null, LastError = null }, Actor);
                Flash($"Đã hủy liên kết tài khoản {account.BankName}.");
            }
            else
            {
                var confirmed = await tingee.ConfirmVaAsync(merchantId, account.BankBin, account.ConfirmId, otp);
                await store.UpdateBankAccountAsync(m.CustomerId, account with
                {
                    Status = BankStatus.Active, AccountNumber = confirmed.AccountNumber ?? account.AccountNumber,
                    VaAccountNumber = confirmed.VaAccountNumber ?? account.VaAccountNumber, ShopId = confirmed.ShopId ?? account.ShopId,
                    ConfirmId = null, ConfirmExpiresAt = null, ActionLink = null, LastError = null
                }, Actor);
                Flash($"Đã liên kết tài khoản {account.BankName}.");
            }
            return RedirectToAction(nameof(Index));
        }
        catch (TingeeApiException ex) when (ex.Code is "1065" or "1080")
        {
            // OTP session expired or not ours: start over instead of retrying a dead confirmId.
            var reverted = deleting ? account with { Status = BankStatus.Active, ConfirmId = null, ConfirmExpiresAt = null, LastError = "Phiên OTP hủy liên kết đã hết hạn." }
                : account with { Status = BankStatus.Failed, ConfirmId = null, ConfirmExpiresAt = null, LastError = "Phiên OTP đã hết hạn, vui lòng liên kết lại." };
            await store.UpdateBankAccountAsync(m.CustomerId, reverted, Actor);
            Flash(reverted.LastError!, error: true);
            return RedirectToAction(nameof(Index));
        }
        catch (TingeeApiException ex)
        {
            ModelState.AddModelError("otp", PartnerText.Scrub($"Xác nhận không thành công (mã {ex.Code}): {ex.TingeeMessage}"));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Tingee unreachable (confirm) for customer {CustomerId}.", m.CustomerId);
            ModelState.AddModelError("otp", "Không kết nối được dịch vụ thanh toán. Vui lòng thử lại.");
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new BankConfirmVm(account, deleting));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var account = await store.BankAccountAsync(m.CustomerId, id);
        if (account is null) return NotFound();
        if (!account.IsActive || account.VaAccountNumber is null)
        {
            // Never activated on Tingee: only the local request is withdrawn.
            await store.UpdateBankAccountAsync(m.CustomerId, account with { Status = BankStatus.Deleted, ConfirmId = null }, Actor);
            Flash("Đã xóa yêu cầu liên kết.");
            return RedirectToAction(nameof(Index));
        }
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId || !tingee.IsConfigured)
        {
            Flash("Dịch vụ thanh toán chưa sẵn sàng nên chưa thể hủy liên kết.", error: true);
            return RedirectToAction(nameof(Index));
        }
        try
        {
            var result = await tingee.DeleteVaAsync(merchantId, account.BankBin, account.VaAccountNumber);
            var now = clock.GetUtcNow().UtcDateTime;
            if (result.ConfirmId is not null)
            {
                await store.UpdateBankAccountAsync(m.CustomerId, account with { Status = BankStatus.PendingDelete, ConfirmId = result.ConfirmId, ConfirmExpiresAt = now + OtpLifetime }, Actor);
                return RedirectToAction(nameof(Confirm), new { id });
            }
            if (result.AuthorizeLink is not null)
            {
                await store.UpdateBankAccountAsync(m.CustomerId, account with { Status = BankStatus.PendingDelete, ActionLink = result.AuthorizeLink }, Actor);
                Flash("Mở liên kết xác thực của ngân hàng để hoàn tất hủy liên kết.");
                return RedirectToAction(nameof(Index));
            }
            await store.UpdateBankAccountAsync(m.CustomerId, account with { Status = BankStatus.Deleted }, Actor);
            Flash($"Đã hủy liên kết tài khoản {account.BankName}.");
        }
        catch (TingeeApiException ex) { Flash($"Hủy liên kết không thành công (mã {ex.Code}): {ex.TingeeMessage}", error: true); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Tingee unreachable (delete-va)."); Flash("Không kết nối được dịch vụ thanh toán. Vui lòng thử lại.", error: true); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        await store.SetDefaultBankAccountAsync(m.CustomerId, id, Actor);
        Flash("Đã đặt tài khoản nhận tiền mặc định.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Pulls the account list from Tingee to settle bank-side confirmations (until the VA-status webhook is processed) and
    /// adds active accounts linked in the SDK popup that are not listed here yet.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Sync()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId || !tingee.IsConfigured)
        {
            Flash("Dịch vụ thanh toán chưa sẵn sàng.", error: true);
            return RedirectToAction(nameof(Index));
        }
        try
        {
            var remote = (await tingee.GetVaPageAsync(merchantId)).Items;
            var locals = await store.BankAccountsAsync(m.CustomerId);
            var changed = 0;
            var added = 0;
            foreach (var r in remote)
            {
                if (r.Status != "active" || TingeeBankCatalog.Find(r.BankBin) is not { } bank) continue;
                if (locals.Any(l => l.Status != BankStatus.Deleted && l.BankBin == r.BankBin
                    && ((r.VaAccountNumber is not null && l.VaAccountNumber == r.VaAccountNumber) || (r.AccountNumber is not null && l.AccountNumber == r.AccountNumber)))) continue;
                await store.AddBankAccountAsync(m.CustomerId, new BankAccount(Guid.Empty, bank.Bin, bank.Code, bank.Name, r.AccountType ?? TingeeAccountTypes.Personal,
                    r.AccountNumber, r.AccountName, r.VaAccountNumber ?? r.AccountNumber, r.VaAccountNumber is null, r.ShopId, BankStatus.Active,
                    null, null, null, null, false, clock.GetUtcNow().UtcDateTime), Actor);
                added++;
            }
            foreach (var local in locals)
            {
                if (local.Status is not (BankStatus.PendingBank or BankStatus.Active)) continue;
                var match = remote.FirstOrDefault(r => r.BankBin == local.BankBin
                    && ((local.VaAccountNumber is not null && r.VaAccountNumber == local.VaAccountNumber) || (local.AccountNumber is not null && r.AccountNumber == local.AccountNumber)));
                if (match is null) continue;
                var status = match.Status == "active" ? BankStatus.Active : local.Status;
                if (status == local.Status && match.VaAccountNumber == local.VaAccountNumber) continue;
                await store.UpdateBankAccountAsync(m.CustomerId, local with { Status = status, VaAccountNumber = match.VaAccountNumber ?? local.VaAccountNumber, ShopId = match.ShopId ?? local.ShopId, ActionLink = status == BankStatus.Active ? null : local.ActionLink }, Actor);
                changed++;
            }
            Flash(added > 0 ? $"Đã thêm {added} tài khoản mới liên kết{(changed > 0 ? $" và cập nhật trạng thái {changed} tài khoản" : "")}."
                : changed == 0 ? "Trạng thái các tài khoản không có thay đổi." : $"Đã cập nhật trạng thái {changed} tài khoản.");
        }
        catch (TingeeApiException ex) { Flash($"Không đồng bộ được trạng thái (mã {ex.Code}): {ex.TingeeMessage}", error: true); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Tingee unreachable (get-va-paging)."); Flash("Không kết nối được dịch vụ thanh toán. Vui lòng thử lại.", error: true); }
        return RedirectToAction(nameof(Index));
    }

    private static string? Use(TingeeBankRule rule, TingeeBankField field, string? value) =>
        rule.Required.Contains(field) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static void Normalize(BankLinkInput i)
    {
        i.AccountNumber = Clean(i.AccountNumber);
        i.Identity = Clean(i.Identity);
        i.Mobile = Clean(i.Mobile);
        i.VaSuffix = Clean(i.VaSuffix)?.ToUpperInvariant();
        i.VaAccountNumber = Clean(i.VaAccountNumber);
        i.AcbUserId = i.AcbUserId?.Trim();
        i.MerchantName = string.IsNullOrWhiteSpace(i.MerchantName) ? null : i.MerchantName.Trim();
        // Banks expect the holder name in upper case without diacritics.
        i.AccountName = string.IsNullOrWhiteSpace(i.AccountName) ? null : Ascii(i.AccountName).ToUpperInvariant();
    }

    private void Validate(TingeeBankRule rule, BankLinkInput i)
    {
        void Need(TingeeBankField field, string? value, string key, string label, Regex? pattern = null, string? patternMessage = null)
        {
            if (!rule.Required.Contains(field)) return;
            if (string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(key, $"Vui lòng nhập {label}.");
            else if (pattern is not null && !pattern.IsMatch(value)) ModelState.AddModelError(key, patternMessage!);
        }
        Need(TingeeBankField.AccountNumber, i.AccountNumber, nameof(i.AccountNumber), "số tài khoản", AccountPattern(), "Số tài khoản gồm 4–30 chữ số.");
        Need(TingeeBankField.AccountName, i.AccountName, nameof(i.AccountName), "tên chủ tài khoản");
        Need(TingeeBankField.Identity, i.Identity, nameof(i.Identity), "CCCD/MST của chủ tài khoản", IdentityPattern(), "CCCD gồm 12 chữ số hoặc MST gồm 10/13 chữ số.");
        Need(TingeeBankField.Mobile, i.Mobile, nameof(i.Mobile), "số điện thoại nhận OTP", MobilePattern(), "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.");
        Need(TingeeBankField.VaSuffix, i.VaSuffix, nameof(i.VaSuffix), "hậu tố tài khoản ảo", SuffixPattern(), "Hậu tố chỉ gồm chữ không dấu và số (tối đa 16 ký tự), không chứa \"QRD\".");
        Need(TingeeBankField.MerchantName, i.MerchantName, nameof(i.MerchantName), "tên hiển thị khi thanh toán");
        Need(TingeeBankField.VaAccountNumber, i.VaAccountNumber, nameof(i.VaAccountNumber), "số tài khoản ảo đã khai báo tại ngân hàng", AccountPattern(), "Số tài khoản ảo gồm 4–30 chữ số.");
        Need(TingeeBankField.AcbUserId, i.AcbUserId, nameof(i.AcbUserId), "tên đăng nhập ACB Online/Biz");
        if (i.VaSuffix?.Contains("QRD", StringComparison.Ordinal) == true) ModelState.AddModelError(nameof(i.VaSuffix), "Hậu tố không được chứa \"QRD\".");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", "").Trim();

    private static string Ascii(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"^\d{4,8}$")] private static partial Regex OtpPattern();
    [GeneratedRegex(@"^\d{4,30}$")] private static partial Regex AccountPattern();
    [GeneratedRegex(@"^(\d{12}|\d{10}|\d{13})$")] private static partial Regex IdentityPattern();
    [GeneratedRegex(@"^0\d{9}$")] private static partial Regex MobilePattern();
    [GeneratedRegex(@"^[A-Z0-9]{1,16}$")] private static partial Regex SuffixPattern();
}
