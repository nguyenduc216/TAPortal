using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;
using TAPortal.Integrations.Branding;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// Public payment page of one order (/pay/{token}), for the company's customer to scan: both logos, the order, the tax code
/// and invoice name (which the payer may correct until the invoice is issued), the QR, the amount and a countdown. Only the
/// random page token opens it; nothing else of the company is reachable from here.
/// </summary>
[AllowAnonymous, Route("pay/{token}"), EnableRateLimiting("pay"), PaymentsDataUnavailable]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class PayController(IOrderPageStore pages, ILogoStore logos, IPaymentStore store, OrderPayments payments, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string token)
    {
        if (await Find(token) is not { } page) return NotFound();
        var order = await store.OrderAsync(page.CustomerId, page.OrderId);
        if (order is null) return NotFound();
        // A QR that disappeared at the bank is replaced before the payer sees it (operator 2026-11-13).
        order = await payments.EnsureLiveQrAsync(page.CustomerId, order, QrLiveness.SystemActor);
        return View(await Vm(token, page, order, null));
    }

    /// <summary>Polled by the page while the QR is open (same shape as the portal's order status).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(string token)
    {
        if (await Find(token) is not { } page || await store.OrderAsync(page.CustomerId, page.OrderId) is not { } order) return NotFound();
        return Json(new { status = OrderMath.EffectiveStatus(order, clock.GetUtcNow().UtcDateTime), paid = order.PaidAmount, amount = order.Amount });
    }

    /// <summary>side = seller (the company) or buyer (the company's customer on the order).</summary>
    [HttpGet("logo/{side}")]
    public async Task<IActionResult> Logo(string token, string side)
    {
        if (await Find(token) is not { } page) return NotFound();
        Guid? buyer = null;
        if (side == "buyer")
        {
            if (await store.OrderAsync(page.CustomerId, page.OrderId) is not { ContactId: Guid contact }) return NotFound();
            buyer = contact;
        }
        else if (side != "seller") return NotFound();
        return await logos.LogoAsync(page.CustomerId, buyer) is { } logo ? LogoFile(this, logo) : NotFound();
    }

    [HttpPost("invoice"), EnableRateLimiting("lookup")]
    public async Task<IActionResult> Invoice(string token, PayInvoiceInput input, [FromServices] ITaxCodeLookup lookup)
    {
        if (await Find(token) is not { } page) return NotFound();
        var order = await store.OrderAsync(page.CustomerId, page.OrderId);
        if (order is null) return NotFound();
        if (!await BuyerMayEdit(page, order))
        {
            TempData["Error"] = "Thông tin xuất hóa đơn của đơn hàng này do người bán cung cấp, không thể thay đổi tại đây.";
            return Redirect(Url.Action(nameof(Index), new { token })! + "#invoice");
        }
        // Operator 2026-11-06: only a tax code found in the business register is saved, with its registered name.
        input.TaxCode = TaxCodeRules.Normalize(input.TaxCode);
        ModelState.Clear();
        if (input.TaxCode is null) ModelState.AddModelError(nameof(input.TaxCode), "Nhập mã số thuế để xuất hóa đơn.");
        else if (!TaxCodeRules.IsLookupable(input.TaxCode)) ModelState.AddModelError(nameof(input.TaxCode), "Mã số thuế doanh nghiệp gồm 10 chữ số (chi nhánh: 10 chữ số-3 chữ số).");
        else
        {
            var found = await lookup.LookupAsync(input.TaxCode, HttpContext.RequestAborted);
            if (found.Info is { } info) input.Name = info.Name;
            else ModelState.AddModelError(nameof(input.TaxCode), found.Status == TaxCodeLookupStatus.NotFound
                ? "Không tìm thấy mã số thuế này trong đăng ký kinh doanh. Vui lòng kiểm tra lại."
                : "Tạm thời chưa tra cứu được mã số thuế. Vui lòng thử lại sau ít phút.");
        }
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("Index", await Vm(token, page, order, input));
        }
        var saved = await pages.SaveInvoiceBuyerAsync(token, input.TaxCode, input.Name);
        TempData[saved ? "Success" : "Error"] = saved ? "Đã lưu thông tin xuất hóa đơn." : "Hóa đơn của đơn hàng này đã được xuất hoặc đơn đã hủy: không thể thay đổi thông tin.";
        return Redirect(Url.Action(nameof(Index), new { token })! + "#invoice");
    }

    /// <summary>Logo response shared with the signed-in pages: the stored type only, never sniffed, cached briefly.</summary>
    internal static IActionResult LogoFile(ControllerBase controller, LogoImage logo)
    {
        controller.Response.Headers.CacheControl = "private, max-age=300";
        controller.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return controller.File(logo.Data, logo.ContentType);
    }

    /// <summary>
    /// The payer may give the invoice details only for an order created without them: no buyer, or a buyer without a tax
    /// code. A buyer the seller recorded with a tax code is invoiced as recorded.
    /// </summary>
    private async Task<bool> BuyerMayEdit(PayPageOrder page, Order order) =>
        order.ContactId is not Guid c || (await store.ContactAsync(page.CustomerId, c))?.TaxCode is null;

    private Task<PayPageOrder?> Find(string token) =>
        token.Length is >= 20 and <= 40 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? pages.FindAsync(token) : Task.FromResult<PayPageOrder?>(null);

    private async Task<PayPageVm> Vm(string token, PayPageOrder page, Order order, PayInvoiceInput? posted)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var status = OrderMath.EffectiveStatus(order, now);
        var contact = order.ContactId is Guid c ? await store.ContactAsync(page.CustomerId, c) : null;
        var bank = order.BankAccountId is Guid b ? await store.BankAccountAsync(page.CustomerId, b) : null;
        var open = status is OrderStatus.Awaiting or OrderStatus.Partial;
        var sellerLogo = await logos.LogoVersionAsync(page.CustomerId, null);
        var buyerLogo = contact is null ? null : await logos.LogoVersionAsync(page.CustomerId, contact.Id);
        var qrKey = order.QrAccount ?? bank?.ReceiveAccount;
        var qrOnly = qrKey is not null && !qrKey.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(bank?.AccountNumber);
        var defaults = new PayInvoiceInput
        {
            TaxCode = page.InvoiceBuyerTaxCode ?? contact?.TaxCode,
            Name = page.InvoiceBuyerName ?? (contact is null ? null : contact.LegalName ?? contact.Name)
        };
        return new PayPageVm(token, page.SellerName, page.SellerTaxCode, sellerLogo is { } s ? LogoImage.Version(s) : null, contact?.Name,
            buyerLogo is { } bl ? LogoImage.Version(bl) : null, order, status, open && order.QrPayload is not null ? QrImage.Svg(order.QrPayload) : null,
            bank?.BankName, qrOnly ? bank!.AccountNumber : qrKey, now,
            order.InvoiceStatus is OrderInvoiceStatus.None or OrderInvoiceStatus.Failed && status is not (OrderStatus.Cancelled or OrderStatus.CancelledPaid)
                && contact?.TaxCode is null,
            page.InvoiceBuyerUpdatedAt, defaults, posted ?? defaults, open ? BankAppLinks.Read(order.QrPayload) : null, bank?.AccountName, qrOnly);
    }
}
