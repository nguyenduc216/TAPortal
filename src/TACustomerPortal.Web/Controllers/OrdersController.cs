using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

// Orders with a payment QR. Everyone in the company sees every order (operator decision); creating, re-issuing a QR and
// cancelling need OWNER/ADMIN. The order is saved first, then the QR is requested, so a Tingee failure never loses it.
public sealed class OrdersController(ICurrentCustomerContext customer, IPaymentStore store, ITingeeApi tingee, OrderPayments payments, TimeProvider clock)
    : CustomerAreaController(customer)
{
    public const int MaxLines = 50;

    public async Task<IActionResult> Index(string? status)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var now = clock.GetUtcNow().UtcDateTime;
        var orders = (await store.OrdersAsync(m.CustomerId)).Select(o => o with { Status = OrderMath.EffectiveStatus(o, now) }).ToList();
        if (!string.IsNullOrEmpty(status)) orders = orders.Where(o => o.Status == status).ToList();
        return View(new OrdersVm(m, CanManage(m), await store.MerchantIdAsync(m.CustomerId) is not null, status, orders, now));
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid? contactId)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var settings = await store.SettingsAsync(m.CustomerId);
        var banks = (await store.BankAccountsAsync(m.CustomerId)).Where(b => b.IsActive && b.ReceiveAccount is not null).ToList();
        var input = new OrderInput { ContactId = contactId, BankAccountId = banks.FirstOrDefault(b => b.IsDefault)?.Id ?? banks.FirstOrDefault()?.Id, QrExpireMinutes = settings.DefaultQrExpireMinutes, AutoIssueInvoice = settings.AutoIssueInvoiceDefault };
        return await CreateView(m, input);
    }

    [HttpPost]
    public async Task<IActionResult> Create(OrderInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var merchant = await store.MerchantIdAsync(m.CustomerId);
        if (merchant is null) ModelState.AddModelError(string.Empty, "Doanh nghiệp chưa được T.A kích hoạt thanh toán Tingee.");
        var bank = input.BankAccountId is Guid bankId ? await store.BankAccountAsync(m.CustomerId, bankId) : null;
        if (bank is null || !bank.IsActive || bank.ReceiveAccount is null) ModelState.AddModelError(nameof(input.BankAccountId), "Vui lòng chọn tài khoản nhận tiền đang hoạt động.");
        if (input.ContactId is Guid contactId && await store.ContactAsync(m.CustomerId, contactId) is not { IsActive: true })
            ModelState.AddModelError(nameof(input.ContactId), "Khách hàng không tồn tại hoặc đã ngừng sử dụng.");
        var lines = ReadLines(input);
        if (!ModelState.IsValid || merchant is not long merchantId || bank is null)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return await CreateView(m, input);
        }

        var created = await store.CreateOrderAsync(m.CustomerId, new NewOrder(input.ContactId, bank.Id, string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), input.QrExpireMinutes, input.AutoIssueInvoice, lines), Actor);
        var order = await store.OrderAsync(m.CustomerId, created.Id);
        var result = await payments.IssueQrAsync(m.CustomerId, merchantId, order!, bank, Actor);
        Flash(result.Error is null ? $"Đã tạo đơn {created.OrderNo} và mã QR thanh toán." : $"Đã lưu đơn {created.OrderNo} nhưng chưa tạo được QR: {result.Error}", error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var status = OrderMath.EffectiveStatus(order, now);
        var bank = order.BankAccountId is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        var contact = order.ContactId is Guid c ? await store.ContactAsync(m.CustomerId, c) : null;
        var qr = order.QrPayload is not null && status is OrderStatus.Awaiting or OrderStatus.Partial ? QrImage.Svg(order.QrPayload) : null;
        return View(new OrderDetailsVm(m, CanManage(m), order, status, bank, contact, qr, now));
    }

    /// <summary>Lightweight status for the details page to poll while the QR is open.</summary>
    [HttpGet]
    public async Task<IActionResult> Status(Guid id)
    {
        if (await Current() is not { } m) return Unauthorized();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        return Json(new { status = OrderMath.EffectiveStatus(order, clock.GetUtcNow().UtcDateTime), paid = order.PaidAmount, amount = order.Amount });
    }

    [HttpPost]
    public async Task<IActionResult> Refresh(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId) return NotFound();
        var result = await payments.RefreshAsync(m.CustomerId, merchantId, order, Actor);
        Flash(result.Error ?? PaymentText.OrderStatus(result.Order.Status).Label + $" — đã nhận {PaymentText.Money(result.Order.PaidAmount)}.", error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> IssueQr(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var status = OrderMath.EffectiveStatus(order, now);
        if (status is not (OrderStatus.Draft or OrderStatus.Expired) || order.Remaining <= 0)
        {
            Flash("Chỉ tạo lại QR cho đơn chưa có QR hoặc đã hết hạn và còn tiền phải thu.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var bank = order.BankAccountId is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        if (bank is null || await store.MerchantIdAsync(m.CustomerId) is not long merchantId)
        {
            Flash("Tài khoản nhận tiền của đơn không còn hoạt động.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await payments.IssueQrAsync(m.CustomerId, merchantId, order with { Status = status }, bank, Actor);
        Flash(result.Error ?? $"Đã tạo QR mới cho số tiền còn lại {PaymentText.Money(order.Remaining)}.", error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        var result = await payments.CancelAsync(m.CustomerId, await store.MerchantIdAsync(m.CustomerId), order, Actor);
        Flash(result.Error ?? $"Đã hủy đơn {order.OrderNo}.", error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    private List<OrderLine> ReadLines(OrderInput input)
    {
        var lines = new List<OrderLine>();
        var rows = (input.Lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.UnitPrice is not null).ToList();
        if (rows.Count == 0) ModelState.AddModelError("Lines", "Đơn hàng cần ít nhất một dòng hàng hóa/dịch vụ.");
        if (rows.Count > MaxLines) ModelState.AddModelError("Lines", $"Tối đa {MaxLines} dòng.");
        for (var i = 0; i < rows.Count && i < MaxLines; i++)
        {
            var l = rows[i];
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(l.ItemName) || l.ItemName.Trim().Length > 500) ModelState.AddModelError("Lines", $"Dòng {n}: nhập tên hàng hóa/dịch vụ (tối đa 500 ký tự).");
            if (l.UnitName?.Trim().Length > 50) ModelState.AddModelError("Lines", $"Dòng {n}: đơn vị tính tối đa 50 ký tự.");
            if (l.Quantity is not > 0 || l.Quantity > 1_000_000_000) ModelState.AddModelError("Lines", $"Dòng {n}: số lượng phải lớn hơn 0.");
            if (l.UnitPrice is not >= 0 || l.UnitPrice > 100_000_000_000 || decimal.Truncate(l.UnitPrice ?? 0) != l.UnitPrice) ModelState.AddModelError("Lines", $"Dòng {n}: đơn giá là số đồng (không âm, không lẻ).");
            if (!OrderMath.IsTaxRate(l.TaxRate)) ModelState.AddModelError("Lines", $"Dòng {n}: thuế suất không hợp lệ.");
            if (ModelState.IsValid) lines.Add(OrderMath.Line(n, l.ItemName!, l.UnitName, l.Quantity!.Value, l.UnitPrice!.Value, l.TaxRate));
        }
        if (ModelState.IsValid && lines.Sum(l => l.Total) <= 0) ModelState.AddModelError("Lines", "Tổng tiền đơn hàng phải lớn hơn 0.");
        if (ModelState.IsValid && lines.Sum(l => l.Total) > 999_999_999_999) ModelState.AddModelError("Lines", "Tổng tiền vượt giới hạn.");
        return lines;
    }

    private async Task<IActionResult> CreateView(CustomerMembership m, OrderInput input)
    {
        var contacts = (await store.ContactsAsync(m.CustomerId)).Where(c => c.IsActive).ToList();
        var banks = (await store.BankAccountsAsync(m.CustomerId)).Where(b => b.IsActive && b.ReceiveAccount is not null).ToList();
        if (input.Lines.Count == 0) input.Lines.Add(new());
        return View("Create", new OrderCreateVm(m, await store.MerchantIdAsync(m.CustomerId) is not null, tingee.IsConfigured, contacts, banks, input));
    }
}
