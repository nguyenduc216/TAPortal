using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Controllers;

// "Gói dịch vụ": the company's available transactions and invoices, the packages T.A sells, and the company's purchases.
// Buying creates a PENDING purchase; T.A confirms the payment (automatic payment comes later) and the quantities are
// added. Every query is scoped to the company selected at sign-in; buying and cancelling need OWNER or ADMIN.
public sealed class ServicePackagesController(ICurrentCustomerContext customer, IServicePackageStore store, TimeProvider clock) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new ServicePackagesPageVm(m, CanManage(m), await store.BalanceAsync(m.CustomerId), await store.PackagesAsync(activeOnly: true),
            await store.PurchasesAsync(m.CustomerId, null, 100), clock.GetUtcNow().UtcDateTime));
    }

    [HttpPost]
    public async Task<IActionResult> Buy(Guid packageId)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var (outcome, purchase) = await store.RequestAsync(m.CustomerId, packageId, ServicePurchaseVia.Customer, Actor);
        if (outcome == ServicePurchaseOutcome.Done && purchase is not null)
            Flash($"Đã tạo yêu cầu mua {purchase.PackageName} (mã {purchase.PurchaseNo}, {ServicePackageRules.Money(purchase.Amount)}). Số lượng sẽ được cộng ngay khi T.A xác nhận đã nhận thanh toán.");
        else
            Flash(outcome == ServicePurchaseOutcome.AuditUnavailable ? "Chưa tạo được yêu cầu mua gói, vui lòng thử lại sau." : "Gói này không còn mở bán. Vui lòng chọn gói khác.", error: true);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var outcome = await store.CancelAsync(id, m.CustomerId, "Khách hàng hủy yêu cầu", Actor);
        if (outcome == ServicePurchaseOutcome.NotFound) return NotFound();
        Flash(outcome switch
        {
            ServicePurchaseOutcome.Done => "Đã hủy yêu cầu mua gói.",
            ServicePurchaseOutcome.NotPending => "Yêu cầu này đã được xử lý nên không thể hủy.",
            _ => "Chưa hủy được yêu cầu, vui lòng thử lại sau."
        }, error: outcome != ServicePurchaseOutcome.Done);
        return RedirectToAction(nameof(Index));
    }
}
