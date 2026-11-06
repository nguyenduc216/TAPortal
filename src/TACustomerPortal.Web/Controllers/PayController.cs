using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Services;
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
public sealed class PayController(IOrderPageStore pages, ILogoStore logos, IPaymentStore store, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string token)
    {
        if (await Find(token) is not { } page) return NotFound();
        var order = await store.OrderAsync(page.CustomerId, page.OrderId);
        if (order is null) return NotFound();
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
    public async Task<IActionResult> Invoice(string token, PayInvoiceInput input)
    {
        if (await Find(token) is not { } page) return NotFound();
        var order = await store.OrderAsync(page.CustomerId, page.OrderId);
        if (order is null) return NotFound();
        input.TaxCode = TaxCodeRules.Normalize(input.TaxCode);
        input.Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        if (input.TaxCode is not null && !TaxCodeRules.IsValid(input.TaxCode))
            ModelState.AddModelError(nameof(input.TaxCode), "Mã số thuế gồm 10 chữ số (chi nhánh: 10 chữ số-3 chữ số) hoặc 12 chữ số.");
        if (input.TaxCode is not null && input.Name is null)
            ModelState.AddModelError(nameof(input.Name), "Nhập tên đơn vị theo đăng ký thuế để xuất hóa đơn.");
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
        var defaults = new PayInvoiceInput
        {
            TaxCode = page.InvoiceBuyerTaxCode ?? contact?.TaxCode,
            Name = page.InvoiceBuyerName ?? (contact is null ? null : contact.LegalName ?? contact.Name)
        };
        return new PayPageVm(token, page.SellerName, page.SellerTaxCode, sellerLogo is { } s ? LogoImage.Version(s) : null, contact?.Name,
            buyerLogo is { } bl ? LogoImage.Version(bl) : null, order, status, open && order.QrPayload is not null ? QrImage.Svg(order.QrPayload) : null,
            bank?.BankName, order.QrAccount ?? bank?.ReceiveAccount, now,
            order.InvoiceStatus is OrderInvoiceStatus.None or OrderInvoiceStatus.Failed && status is not (OrderStatus.Cancelled or OrderStatus.CancelledPaid),
            page.InvoiceBuyerUpdatedAt, defaults, posted ?? defaults);
    }
}
