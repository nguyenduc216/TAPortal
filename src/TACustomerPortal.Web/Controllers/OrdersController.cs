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
        var all = (await store.OrdersAsync(m.CustomerId)).Select(o => o with { Status = OrderMath.EffectiveStatus(o, now) }).ToList();
        var cards = OrderSummary.Cards(all);
        var orders = string.IsNullOrEmpty(status) ? all : all.Where(o => OrderSummary.Matches(o, status)).ToList();
        return View(new OrdersVm(m, CanManage(m), await store.MerchantIdAsync(m.CustomerId) is not null, status, orders, now, cards));
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
        input.QrExpireMinutes = QrLifetime.Apply(ModelState, nameof(input.QrExpireMinutes), input.QrExpireMinutes, input.QrExpireDays, input.QrExpireHours, input.QrExpireMins);
        var merchant = await store.MerchantIdAsync(m.CustomerId);
        if (merchant is null) ModelState.AddModelError(string.Empty, "Doanh nghiệp chưa được T.A kích hoạt dịch vụ thanh toán.");
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

    public async Task<IActionResult> Details(Guid id, [FromServices] IOrderInvoiceStore invoices, [FromServices] IOrderPageStore pages)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var status = OrderMath.EffectiveStatus(order, now);
        var bank = order.BankAccountId is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        var contact = order.ContactId is Guid c ? await store.ContactAsync(m.CustomerId, c) : null;
        var qr = order.QrPayload is not null && status is OrderStatus.Awaiting or OrderStatus.Partial ? QrImage.Svg(order.QrPayload) : null;
        var banks = (await store.BankAccountsAsync(m.CustomerId)).Where(x => x.IsActive && x.ReceiveAccount is not null).ToList();
        // The public payment page (/pay/{token}) the customer's own customer scans; the token is created the first time.
        var token = await pages.TokenAsync(m.CustomerId, order.Id);
        var payUrl = token is null ? null : Url.Action("Index", "Pay", new { token }, Request.Scheme);
        return View(new OrderDetailsVm(m, CanManage(m), order, status, bank, contact, qr, now, banks, (await invoices.SettingsAsync(m.CustomerId)).IsReady,
            payUrl, await pages.InvoiceBuyerAsync(m.CustomerId, order.Id)));
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
        Flash(result.Error ?? (result.Order.PartnerDiffers
            ? $"Lệch đối soát: hệ thống đã ghi nhận {PaymentText.Money(result.Order.PaidAmount)}, dịch vụ thanh toán báo {PaymentText.Money(result.Order.PartnerPaidAmount ?? 0)}. Giao dịch sẽ được ghi nhận khi ngân hàng gửi thông báo, hoặc được xử lý tại Đối soát giao dịch."
            : $"Khớp đối soát — đã nhận {PaymentText.Money(result.Order.PaidAmount)}."), error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>(Re)issues the QR of a draft or expired order; bankAccountId switches the receiving account before that.</summary>
    [HttpPost]
    public async Task<IActionResult> IssueQr(Guid id, Guid? bankAccountId)
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
        var chosen = bankAccountId ?? order.BankAccountId;
        var bank = chosen is Guid b ? await store.BankAccountAsync(m.CustomerId, b) : null;
        if (bank is null || !bank.IsActive || bank.ReceiveAccount is null || await store.MerchantIdAsync(m.CustomerId) is not long merchantId)
        {
            Flash("Tài khoản nhận tiền không còn hoạt động. Vui lòng chọn tài khoản khác.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await payments.IssueQrAsync(m.CustomerId, merchantId, order with { Status = status, BankAccountId = bank.Id }, bank, Actor);
        Flash(result.Error ?? $"Đã tạo QR mới cho số tiền còn lại {PaymentText.Money(order.Remaining)}.", error: result.Error is not null);
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// Issues the e-invoice of a fully paid order now: an order paid before automatic invoices were on, one without automatic
    /// invoicing, or one whose automatic attempt failed. The invoice code stays the same, so it can never be issued twice.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> IssueInvoice(Guid id, [FromServices] OrderInvoicer invoicer)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null) return NotFound();
        if (order.Status != OrderStatus.Paid || order.InvoiceStatus == OrderInvoiceStatus.Issued)
        {
            Flash("Chỉ xuất hóa đơn cho đơn đã thanh toán đủ và chưa có hóa đơn.", error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await invoicer.IssueAsync(m.CustomerId, id, manual: true, HttpContext.RequestAborted);
        Flash(result.Message, error: !result.Issued);
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>PDF of the issued invoice, opened in the browser.</summary>
    [HttpGet]
    public async Task<IActionResult> InvoicePdf(Guid id, [FromServices] ITingeeInvoiceApi invoices, [FromServices] IOrderInvoiceStore settingsStore)
    {
        if (await Current() is not { } m) return SelectCustomer();
        var order = await store.OrderAsync(m.CustomerId, id);
        if (order is null || order.InvoiceStatus != OrderInvoiceStatus.Issued || order.InvoiceCode is not string code) return NotFound();
        if (await store.MerchantIdAsync(m.CustomerId) is not long merchantId) return NotFound();
        var settings = await settingsStore.SettingsAsync(m.CustomerId);
        try
        {
            var pdf = await invoices.DownloadAsync(merchantId, code, settings.ProviderAccountId, HttpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(pdf.PdfBase64)) throw new TingeeApiException("EMPTY", "Không nhận được file PDF.");
            return File(Convert.FromBase64String(pdf.PdfBase64), "application/pdf");
        }
        catch (Exception ex) when (ex is FormatException or TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException)
        {
            Flash("Chưa tải được hóa đơn. " + (ex is FormatException ? "File PDF không đúng định dạng." : OrderInvoicer.Reason(ex)), error: true);
            return RedirectToAction(nameof(Details), new { id });
        }
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
