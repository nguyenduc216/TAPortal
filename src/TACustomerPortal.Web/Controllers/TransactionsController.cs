using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Controllers;

public sealed record TransactionsVm(CustomerMembership Customer, bool CanManage, string Status, string? Query, DateOnly? From, DateOnly? To,
    PaymentTxSummary Summary, IReadOnlyList<PaymentTransaction> Items);

// "Đối soát giao dịch" (operator decision 2026-11-05): every bank notification on this company's receiving accounts, with
// what the system did with it. Staff close the ones left "chưa xử lý" / "cần xem lại" at the end of the day. Every query
// and action is limited to the signed-in company (scope = its CustomerId); actions need OWNER/ADMIN.
public sealed class TransactionsController(ICurrentCustomerContext customer, IPaymentTransactionStore transactions) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index(string? status = "STAFF", string? q = null, DateOnly? from = null, DateOnly? to = null)
    {
        if (await Current() is not { } m) return SelectCustomer();
        status ??= "";
        var (fromUtc, toUtc) = PaymentTxText.Days(from, to);
        var filter = new PaymentTxFilter(m.CustomerId, status, fromUtc, toUtc, q);
        var ct = HttpContext.RequestAborted;
        return View(new TransactionsVm(m, CanManage(m), status, q, from, to, await transactions.SummaryAsync(filter with { Search = null }, ct), await transactions.SearchAsync(filter, ct)));
    }

    [HttpPost]
    public async Task<IActionResult> Assign(long id, string? orderNo, string? note, string? back)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var result = string.IsNullOrWhiteSpace(orderNo) ? new PaymentTxResult(false, "Vui lòng nhập mã đơn hàng.")
            : await transactions.AssignAsync(id, m.CustomerId, m.CustomerId, orderNo, Actor, PaymentTxVia.Portal, note, HttpContext.RequestAborted);
        Flash(result.Error ?? $"Đã gán giao dịch vào đơn {orderNo!.Trim()}.", error: !result.Ok);
        return Back(back);
    }

    [HttpPost]
    public async Task<IActionResult> Accept(long id, string? note, string? back)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var result = await transactions.AcceptAsync(id, m.CustomerId, Actor, PaymentTxVia.Portal, note, HttpContext.RequestAborted);
        Flash(result.Error ?? "Đã chấp nhận giao dịch cho đơn hàng.", error: !result.Ok);
        return Back(back);
    }

    [HttpPost]
    public async Task<IActionResult> Close(long id, string? note, string? back)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var result = await transactions.CloseAsync(id, m.CustomerId, Actor, PaymentTxVia.Portal, note ?? "", HttpContext.RequestAborted);
        Flash(result.Error ?? "Đã đánh dấu giao dịch là đã xử lý.", error: !result.Ok);
        return Back(back);
    }

    // "Đồng bộ từ ngân hàng" (operator 2026-11-12): fetches the receiving accounts' transactions from the payment partner and
    // queues the ones whose notification never reached the system, so they are matched like any notification. Period:
    // the list's dates, else the last 3 days (Vietnam time).
    [HttpPost]
    public async Task<IActionResult> Sync(DateOnly? from, DateOnly? to, string? back, [FromServices] TingeeTransactionSync sync, [FromServices] TimeProvider clock)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(7));
        var end = to ?? today;
        var start = from ?? end.AddDays(-2);
        if (end < start) (start, end) = (end, start);
        var (fromUtc, toUtc) = PaymentTxText.Days(start, end);
        var result = await sync.SyncAsync(m.CustomerId, fromUtc!.Value, toUtc!.Value, ct: HttpContext.RequestAborted);
        Flash(TingeeTransactionSync.Message(result, start, end), error: !result.Ok);
        return Back(back);
    }

    // Back to the same filtered list; only local paths are followed.
    private IActionResult Back(string? back) => !string.IsNullOrEmpty(back) && Url.IsLocalUrl(back) ? LocalRedirect(back) : RedirectToAction(nameof(Index));
}
