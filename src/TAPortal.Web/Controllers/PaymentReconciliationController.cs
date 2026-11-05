using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Web.Controllers;

public sealed record PaymentReconciliationVm(Guid? CustomerId, bool UnattributedOnly, string Status, string? Query, DateOnly? From, DateOnly? To,
    IReadOnlyList<(Guid Id, string Code, string Name)> Customers, PaymentTxSummary? Summary, IReadOnlyList<PaymentTransaction> Items, string? Error);

// T.A reconciliation of payment notifications (operator decision 2026-11-05): every bank notification of every customer,
// including money on accounts linked to no customer. T.A can filter by customer, assign an unmatched transaction to any
// customer's order, accept a REVIEW transaction or close one with a note. System administrators only.
[Authorize(Policy = "SystemAdminOnly")]
public sealed class PaymentReconciliationController(IPaymentTransactionStore transactions, ILogger<PaymentReconciliationController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid? customerId, bool unattributed = false, string? status = "STAFF", string? q = null, DateOnly? from = null, DateOnly? to = null)
    {
        status ??= "";
        ViewData["Title"] = "Đối soát giao dịch";
        var (fromUtc, toUtc) = PaymentTxText.Days(from, to);
        var filter = new PaymentTxFilter(customerId, status, fromUtc, toUtc, q, unattributed);
        var ct = HttpContext.RequestAborted;
        try
        {
            return View(new PaymentReconciliationVm(customerId, unattributed, status, q, from, to, await transactions.CustomersAsync(ct),
                await transactions.SummaryAsync(filter with { Search = null }, ct), await transactions.SearchAsync(filter, ct), null));
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "PAYMENT_RECONCILIATION_UNAVAILABLE");
            return View(new PaymentReconciliationVm(customerId, unattributed, status, q, from, to, [], null, [],
                "Không đọc được dbo.PaymentTransactions (đã chạy migration 019 chưa?)."));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(long id, Guid? orderCustomerId, string? orderNo, string? note, string? back)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var tx = await transactions.GetAsync(id, null, HttpContext.RequestAborted);
        var customer = tx?.CustomerId ?? orderCustomerId;
        var result = tx is null ? new PaymentTxResult(false, "Không tìm thấy giao dịch.")
            : customer is not Guid c ? new PaymentTxResult(false, "Vui lòng chọn khách hàng của đơn hàng.")
            : string.IsNullOrWhiteSpace(orderNo) ? new PaymentTxResult(false, "Vui lòng nhập mã đơn hàng.")
            : await transactions.AssignAsync(id, null, c, orderNo, actor, PaymentTxVia.Admin, note, HttpContext.RequestAborted);
        TempData[result.Ok ? "Success" : "Error"] = result.Error ?? $"Đã gán giao dịch #{id} vào đơn {orderNo!.Trim()}.";
        return Back(back);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(long id, string? note, string? back)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await transactions.AcceptAsync(id, null, actor, PaymentTxVia.Admin, note, HttpContext.RequestAborted);
        TempData[result.Ok ? "Success" : "Error"] = result.Error ?? $"Đã chấp nhận giao dịch #{id} cho đơn hàng.";
        return Back(back);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(long id, string? note, string? back)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await transactions.CloseAsync(id, null, actor, PaymentTxVia.Admin, note ?? "", HttpContext.RequestAborted);
        TempData[result.Ok ? "Success" : "Error"] = result.Error ?? $"Đã đánh dấu giao dịch #{id} là đã xử lý.";
        return Back(back);
    }

    private IActionResult Back(string? back) => !string.IsNullOrEmpty(back) && Url.IsLocalUrl(back) ? LocalRedirect(back) : RedirectToAction(nameof(Index));
}
