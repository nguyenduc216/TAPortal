using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Models;

namespace TAPortal.Web.Controllers;

// Customer Workspace → "Gói dịch vụ": the customer's available balance (transactions, invoices), its purchases, and an
// admin entry of a purchase on the customer's behalf (PENDING, then confirmed on the purchase page).
public sealed partial class CustomersController
{
    [HttpGet]
    public async Task<IActionResult> ServicePackages(Guid id, [FromServices] IServicePackageStore services, [FromServices] TimeProvider clock)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.ServicePackages);
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            return View(new CustomerServicesVm(customer, await services.BalanceAsync(id), await services.PurchasesAsync(id, null),
                await services.PackagesAsync(activeOnly: true), now, null));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "SERVICE_PACKAGES_UNAVAILABLE for customer {CustomerId}.", id);
            return View(new CustomerServicesVm(customer, ServiceBalance.Empty, [], [], now, ServicePackagesController.StoreUnavailable));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateServicePurchase(Guid id, Guid packageId, [FromServices] IServicePackageStore services)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        var (outcome, purchase) = await services.RequestAsync(id, packageId, ServicePurchaseVia.Admin, actor);
        if (outcome == ServicePurchaseOutcome.Done && purchase is not null)
        {
            TempData["Success"] = $"Đã tạo giao dịch {purchase.PurchaseNo} (chờ thanh toán). Xác nhận thanh toán để cộng số lượng cho khách hàng.";
            return RedirectToAction("Purchase", "ServicePackages", new { id = purchase.Id });
        }
        TempData["Error"] = outcome == ServicePurchaseOutcome.AuditUnavailable
            ? "Không ghi được nhật ký kiểm toán nên giao dịch chưa được tạo."
            : "Gói không còn mở bán. Vui lòng chọn gói khác.";
        return RedirectToAction(nameof(ServicePackages), new { id });
    }
}
