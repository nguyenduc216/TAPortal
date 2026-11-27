using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// "Thu gộp công nợ" (operator 2026-11-27): choose a buyer, tick its unpaid orders (in the order the money pays them),
/// optionally ask for less than the total, and get one QR and one payment link. Everyone in the company sees them;
/// creating, re-issuing a QR and cancelling need OWNER/ADMIN, as for orders.
/// </summary>
public sealed class CollectionsController(ICurrentCustomerContext customer, IPaymentStore store, IPaymentCollectionStore collections, CollectionPayments payments, TimeProvider clock)
    : CustomerAreaController(customer)
{
    public const int MaxOrders = 100;

    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new CollectionsVm(m, CanManage(m), await store.MerchantIdAsync(m.CustomerId) is not null, await collections.CollectionsAsync(m.CustomerId), clock.GetUtcNow().UtcDateTime));
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid? contactId)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        var input = new CollectionInput { ContactId = contactId, QrExpireMinutes = settings.DefaultQrExpireMinutes };
        return await CreateView(m, input, selectAll: true);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CollectionInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        input.QrExpireMinutes = QrLifetime.Apply(ModelState, nameof(input.QrExpireMinutes), input.QrExpireMinutes, input.QrExpireDays, input.QrExpireHours, input.QrExpireMins);
        var merchant = await store.MerchantIdAsync(m.CustomerId);
        if (merchant is null) ModelState.AddModelError(string.Empty, "Doanh nghiệp chưa được kích hoạt dịch vụ thanh toán.");
        var bank = input.BankAccountId is Guid bankId ? await store.BankAccountAsync(m.CustomerId, bankId) : null;
        if (bank is null || !bank.IsActive || bank.ReceiveAccount is null) ModelState.AddModelError(nameof(input.BankAccountId), "Vui lòng chọn tài khoản nhận tiền đang hoạt động.");
        var contact = input.ContactId is Guid contactId ? await store.ContactAsync(m.CustomerId, contactId) : null;
        if (contact is null) ModelState.AddModelError(nameof(input.ContactId), "Chọn khách hàng cần thu công nợ.");

        var now = clock.GetUtcNow().UtcDateTime;
        var open = await collections.OpenCollectionsByOrderAsync(m.CustomerId);
        var debts = contact is null ? [] : (await store.OrdersAsync(m.CustomerId)).Where(o => o.ContactId == contact.Id && CollectionMath.IsDebt(o, now)).ToDictionary(o => o.Id);
        var chosen = input.OrderIds.Distinct().ToList();
        if (chosen.Count == 0) ModelState.AddModelError(nameof(input.OrderIds), "Chọn ít nhất một đơn hàng còn nợ.");
        else if (chosen.Count > MaxOrders) ModelState.AddModelError(nameof(input.OrderIds), $"Tối đa {MaxOrders} đơn trong một phiếu thu.");
        else if (chosen.Any(id => !debts.ContainsKey(id))) ModelState.AddModelError(nameof(input.OrderIds), "Có đơn hàng không còn nợ hoặc không thuộc khách hàng này. Vui lòng chọn lại.");
        else if (chosen.FirstOrDefault(open.ContainsKey) is var busy && busy != Guid.Empty)
            ModelState.AddModelError(nameof(input.OrderIds), $"Đơn {debts[busy].OrderNo} đã nằm trong phiếu thu {open[busy].CollectionNo} đang mở.");
        var total = chosen.Where(debts.ContainsKey).Sum(id => debts[id].Remaining);
        var amount = input.Amount ?? total;
        if (ModelState.IsValid && (amount <= 0 || amount > total || decimal.Truncate(amount) != amount))
            ModelState.AddModelError(nameof(input.Amount), $"Số tiền thu là số đồng, từ 1 ₫ đến tổng còn nợ {PaymentText.Money(total)}.");
        if (!ModelState.IsValid || merchant is not long merchantId || bank is null || contact is null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return await CreateView(m, input, selectAll: false);
        }

        var created = await collections.CreateAsync(m.CustomerId, new NewCollection(contact.Id, bank.Id, string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            amount, input.QrExpireMinutes, chosen), Actor);
        if (created.Error is not null)
        {
            ModelState.AddModelError(nameof(input.OrderIds), created.Error);
            Response.StatusCode = StatusCodes.Status409Conflict;
            return await CreateView(m, input, selectAll: false);
        }
        var collection = await collections.CollectionAsync(m.CustomerId, created.Id);
        var (_, error) = await payments.IssueQrAsync(m.CustomerId, merchantId, collection!, bank, input.QrExpireMinutes, Actor);
        Flash(error is null ? $"Đã tạo phiếu thu {created.CollectionNo} ({chosen.Count} đơn, {PaymentText.Money(amount)}) và mã QR thanh toán."
            : $"Đã lưu phiếu thu {created.CollectionNo} nhưng chưa tạo được QR: {error}", error: error is not null);
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var c = await collections.CollectionAsync(m.CustomerId, id);
        if (c is null) return NotFound();
        c = await payments.EnsureLiveQrAsync(m.CustomerId, c, Actor);
        var now = clock.GetUtcNow().UtcDateTime;
        var status = CollectionMath.EffectiveStatus(c, now);
        var bank = c.BankAccountId is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        var contact = c.ContactId is Guid ct ? await store.ContactAsync(m.CustomerId, ct) : null;
        var qr = c.QrPayload is not null && PaymentCollectionStatus.IsOpen(status) && c.Due > 0 ? QrImage.Svg(c.QrPayload) : null;
        var banks = (await store.BankAccountsAsync(m.CustomerId)).Where(x => x.IsActive && x.ReceiveAccount is not null).ToList();
        var token = await collections.TokenAsync(m.CustomerId, c.Id);
        var payUrl = token is null ? null : Url.Action("Index", "CollectionPay", new { token }, Request.Scheme);
        return View(new CollectionDetailsVm(m, CanManage(m), c, status, contact, bank, qr, now, banks, payUrl, await collections.PaymentsAsync(m.CustomerId, c.Id)));
    }

    /// <summary>Lightweight status for the details page to poll while the QR is open.</summary>
    [HttpGet]
    public async Task<IActionResult> Status(Guid id)
    {
        if (await Current() is not { } m) return Unauthorized();
        if (await collections.CollectionAsync(m.CustomerId, id) is not { } c) return NotFound();
        return Json(new { status = CollectionMath.EffectiveStatus(c, clock.GetUtcNow().UtcDateTime), paid = c.PaidAmount, amount = c.Amount });
    }

    /// <summary>A new QR for what is still due (after expiry, or to change its lifetime or receiving account); the link stays the same.</summary>
    [HttpPost]
    public async Task<IActionResult> IssueQr(Guid id, Guid? bankAccountId, int? qrExpireDays, int? qrExpireHours, int? qrExpireMins)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var c = await collections.CollectionAsync(m.CustomerId, id);
        if (c is null) return NotFound();
        if (!PaymentCollectionStatus.IsOpen(c.Status) || c.Due <= 0)
        {
            Flash("Chỉ tạo QR cho phiếu thu đang mở và còn tiền phải thu.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        ModelState.Clear();
        var minutes = QrLifetime.Apply(ModelState, nameof(CollectionInput.QrExpireMinutes), c.QrExpireMinutes, qrExpireDays, qrExpireHours, qrExpireMins);
        if (!ModelState.IsValid || qrExpireDays is < 0 or > 30 || qrExpireHours is < 0 or > 23 || qrExpireMins is < 0 or > 59)
        {
            Flash(QrLifetime.RangeMessage + " Số giờ từ 0 đến 23, số phút từ 0 đến 59.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var chosen = bankAccountId ?? c.BankAccountId;
        var bank = chosen is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        if (bank is null || !bank.IsActive || bank.ReceiveAccount is null || await store.MerchantIdAsync(m.CustomerId) is not long merchantId)
        {
            Flash("Tài khoản nhận tiền không còn hoạt động. Vui lòng chọn tài khoản khác.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var (updated, error) = await payments.IssueQrAsync(m.CustomerId, merchantId, c, bank, minutes, Actor);
        Flash(error ?? $"Đã tạo QR mới cho số tiền còn lại {PaymentText.Money(c.Due)}, hiệu lực đến {PaymentText.VietnamTime(updated.ExpiresAt)}. Link thanh toán giữ nguyên.", error: error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var c = await collections.CollectionAsync(m.CustomerId, id);
        if (c is null) return NotFound();
        var error = await payments.CancelAsync(m.CustomerId, c, Actor);
        Flash(error ?? $"Đã hủy phiếu thu {c.CollectionNo}. Các đơn hàng giữ nguyên số tiền đã thu.", error: error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> CreateView(CustomerMembership m, CollectionInput input, bool selectAll)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var contacts = (await store.ContactsAsync(m.CustomerId)).ToDictionary(c => c.Id);
        var debts = (await store.OrdersAsync(m.CustomerId)).Where(o => o.ContactId is Guid c && contacts.ContainsKey(c) && CollectionMath.IsDebt(o, now)).ToList();
        var debtors = debts.GroupBy(o => o.ContactId!.Value).Select(g => new Debtor(contacts[g.Key], g.Count(), g.Sum(o => o.Remaining)))
            .OrderBy(d => d.Contact.Name, StringComparer.CurrentCulture).ToList();
        var contact = input.ContactId is Guid id && contacts.TryGetValue(id, out var found) ? found : null;
        var open = await collections.OpenCollectionsByOrderAsync(m.CustomerId);
        // Oldest first (the order the money pays them); posted orders keep the order the user arranged.
        var orders = contact is null ? [] : debts.Where(o => o.ContactId == contact.Id).OrderBy(o => o.CreatedAt).ThenBy(o => o.OrderNo, StringComparer.Ordinal)
            .Select(o => new DebtOrder(o, OrderMath.EffectiveStatus(o, now), open.TryGetValue(o.Id, out var inside) ? inside : null)).ToList();
        if (!selectAll && input.OrderIds.Count > 0)
        {
            var rank = input.OrderIds.Select((oid, i) => (oid, i)).GroupBy(x => x.oid).ToDictionary(g => g.Key, g => g.First().i);
            orders = [.. orders.OrderBy(o => rank.TryGetValue(o.Order.Id, out var r) ? r : int.MaxValue)];
        }
        else if (selectAll) input.OrderIds = orders.Where(o => o.InCollection is null).Select(o => o.Order.Id).ToList();
        var banks = (await store.BankAccountsAsync(m.CustomerId)).Where(b => b.IsActive && b.ReceiveAccount is not null).ToList();
        input.BankAccountId ??= banks.FirstOrDefault(b => b.IsDefault)?.Id ?? banks.FirstOrDefault()?.Id;
        return View("Create", new CollectionCreateVm(m, await store.MerchantIdAsync(m.CustomerId) is not null, debtors, contact, orders, banks, input));
    }
}
