using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations;
using TAPortal.Integrations.Branding;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// Public payment page of a payment collection (/pay/c/{token}): the orders it pays, one QR for what is still due and a
/// countdown. Only the random page token opens it; nothing else of the company is reachable from here.
/// </summary>
[AllowAnonymous, Route("pay/c/{token}"), EnableRateLimiting("pay"), PaymentsDataUnavailable]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CollectionPayController(IPaymentCollectionStore collections, ILogoStore logos, IPaymentStore store, CollectionPayments payments, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string token)
    {
        if (await Find(token) is not { } page || await collections.CollectionAsync(page.CustomerId, page.CollectionId) is not { } c) return NotFound();
        c = await payments.EnsureLiveQrAsync(page.CustomerId, c, QrLiveness.SystemActor);
        var now = clock.GetUtcNow().UtcDateTime;
        var status = CollectionMath.EffectiveStatus(c, now);
        var contact = c.ContactId is Guid ct ? await store.ContactAsync(page.CustomerId, ct) : null;
        var bank = c.BankAccountId is Guid b ? await store.BankAccountAsync(page.CustomerId, b) : null;
        var open = PaymentCollectionStatus.IsOpen(status) && c.Due > 0 && c.QrPayload is not null;
        var sellerLogo = await logos.LogoVersionAsync(page.CustomerId, null);
        var buyerLogo = contact is null ? null : await logos.LogoVersionAsync(page.CustomerId, contact.Id);
        var qrKey = c.QrAccount ?? bank?.ReceiveAccount;
        var qrOnly = qrKey is not null && !qrKey.All(char.IsAsciiDigit) && !string.IsNullOrWhiteSpace(bank?.AccountNumber);
        return View("~/Views/Pay/Collection.cshtml", new CollectionPayVm(token, page.SellerName, sellerLogo is { } s ? LogoImage.Version(s) : null, contact?.Name,
            buyerLogo is { } bl ? LogoImage.Version(bl) : null, c, status, open ? QrImage.Svg(c.QrPayload!) : null, bank?.BankName, qrOnly ? bank!.AccountNumber : qrKey,
            bank?.AccountName, qrOnly, open ? BankAppLinks.Read(c.QrPayload) : null, now));
    }

    /// <summary>Polled by the page while the QR is open.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(string token)
    {
        if (await Find(token) is not { } page || await collections.CollectionAsync(page.CustomerId, page.CollectionId) is not { } c) return NotFound();
        return Json(new { status = CollectionMath.EffectiveStatus(c, clock.GetUtcNow().UtcDateTime), paid = c.PaidAmount, amount = c.Amount });
    }

    /// <summary>side = seller (the company) or buyer (the company's customer on the collection).</summary>
    [HttpGet("logo/{side}")]
    public async Task<IActionResult> Logo(string token, string side)
    {
        if (await Find(token) is not { } page) return NotFound();
        Guid? buyer = null;
        if (side == "buyer")
        {
            if (await collections.CollectionAsync(page.CustomerId, page.CollectionId) is not { ContactId: Guid contact }) return NotFound();
            buyer = contact;
        }
        else if (side != "seller") return NotFound();
        return await logos.LogoAsync(page.CustomerId, buyer) is { } logo ? PayController.LogoFile(this, logo) : NotFound();
    }

    private Task<PayPageCollection?> Find(string token) =>
        token.Length is >= 20 and <= 40 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? collections.FindAsync(token) : Task.FromResult<PayPageCollection?>(null);
}
