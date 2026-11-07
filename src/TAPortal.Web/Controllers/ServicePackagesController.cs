using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Models;

namespace TAPortal.Web.Controllers;

// Admin → Gói dịch vụ: the package catalogue (invoice / transaction reconciliation / combo; price incl. VAT, optional
// validity) and every purchase. A purchase starts PENDING (customer request or admin entry); until automatic payment is
// connected, an admin confirms the payment, which starts the balance lot. Purchases keep a snapshot of the package, so
// editing a package never changes what a customer already bought.
// Signed-in staff only at class level; every action declares its permission (catalogue: PACKAGES.*).
[Authorize]
public sealed class ServicePackagesController(IServicePackageStore store, TimeProvider clock, ILogger<ServicePackagesController> logger) : Controller
{
    public const string StoreUnavailable = "Không đọc được dữ liệu gói dịch vụ (bảng của migration 018 chưa có hoặc thiếu quyền). Chi tiết đã ghi vào nhật ký ứng dụng.";

    [Authorize(Policy = AdminPermissions.Policy.PackagesView)]
    public async Task<IActionResult> Index()
    {
        try { return View(new ServicePackagesVm(await store.PackagesAsync(activeOnly: false), null)); }
        catch (SqlException ex) { logger.LogError(ex, "SERVICE_PACKAGES_UNAVAILABLE"); return View(new ServicePackagesVm([], StoreUnavailable)); }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.PackagesManage)]
    public IActionResult Create() => View("Edit", new ServicePackageEditVm(null, new ServicePackageInput()));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.PackagesManage)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] ServicePackageInput input) => Save(null, input);

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.PackagesManage)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var package = await store.PackageAsync(id);
        return package is null ? NotFound() : View(new ServicePackageEditVm(id, ServicePackageInput.From(package)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.PackagesManage)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] ServicePackageInput input) => Save(id, input);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.PackagesManage)]
    public async Task<IActionResult> SetActive(Guid id, bool active)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var outcome = await store.SetPackageActiveAsync(id, active, actor);
        if (outcome == ServicePackageSaveOutcome.NotFound) return NotFound();
        if (outcome == ServicePackageSaveOutcome.AuditUnavailable) TempData["Error"] = AuditUnavailable;
        else TempData["Success"] = active ? "Đã mở bán lại gói." : "Đã ngừng bán gói. Khách hàng đã mua vẫn giữ nguyên số lượng.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AdminPermissions.Policy.PurchasesView)]
    public async Task<IActionResult> Purchases(string? status, string? q)
    {
        status = ServicePurchaseStatus.All.Contains(status ?? "") ? status : null;
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            IEnumerable<ServicePurchase> list = await store.PurchasesAsync(null, status);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                list = list.Where(p => p.PurchaseNo.Contains(term, StringComparison.OrdinalIgnoreCase) || p.CustomerCode.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || p.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) || p.PackageCode.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            return View(new ServicePurchasesVm(list.ToList(), status, q, now, null));
        }
        catch (SqlException ex) { logger.LogError(ex, "SERVICE_PURCHASES_UNAVAILABLE"); return View(new ServicePurchasesVm([], status, q, now, StoreUnavailable)); }
    }

    [Authorize(Policy = AdminPermissions.Policy.PurchasesView)]
    public async Task<IActionResult> Purchase(Guid id)
    {
        var purchase = await store.PurchaseAsync(id);
        return purchase is null ? NotFound() : View(new ServicePurchaseVm(purchase, new ServicePurchaseConfirmInput { PaidAmount = purchase.Amount }, clock.GetUtcNow().UtcDateTime));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.PurchasesManage)]
    public async Task<IActionResult> ConfirmPaid(Guid id, [Bind(Prefix = "Confirm")] ServicePurchaseConfirmInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var purchase = await store.PurchaseAsync(id);
        if (purchase is null) return NotFound();
        if (!ServicePaymentMethods.All.Any(m => m.Code == input.Method)) ModelState.AddModelError("Confirm.Method", "Hình thức thanh toán không hợp lệ.");
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(nameof(Purchase), new ServicePurchaseVm(purchase, input, clock.GetUtcNow().UtcDateTime));
        }
        var outcome = await store.ConfirmPaidAsync(id, input.PaidAmount, input.Method, string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim(), actor);
        Report(outcome, $"Đã xác nhận thanh toán {purchase.PurchaseNo}: {ServicePackageRules.Quantities(purchase.InvoiceQuota, purchase.TransactionQuota)} đã được cộng cho {purchase.CustomerCode}.");
        return RedirectToAction(nameof(Purchase), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.PurchasesManage)]
    public async Task<IActionResult> CancelPurchase(Guid id, string? reason)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(500, reason.Trim().Length)];
        var outcome = await store.CancelAsync(id, null, reason, actor);
        if (outcome == ServicePurchaseOutcome.NotFound) return NotFound();
        Report(outcome, "Đã hủy giao dịch mua gói.");
        return RedirectToAction(nameof(Purchase), new { id });
    }

    private const string AuditUnavailable = "Không ghi được nhật ký kiểm toán nên thay đổi chưa được lưu.";

    private void Report(ServicePurchaseOutcome outcome, string success)
    {
        switch (outcome)
        {
            case ServicePurchaseOutcome.Done: TempData["Success"] = success; break;
            case ServicePurchaseOutcome.NotPending: TempData["Error"] = "Giao dịch không còn ở trạng thái chờ thanh toán."; break;
            case ServicePurchaseOutcome.AuditUnavailable: TempData["Error"] = AuditUnavailable; break;
            default: TempData["Error"] = "Không tìm thấy giao dịch."; break;
        }
    }

    private async Task<IActionResult> Save(Guid? id, ServicePackageInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ServicePackageRules.Normalize(input);
        foreach (var (field, message) in ServicePackageRules.Validate(input)) ModelState.AddModelError("Input." + field, message);
        if (ModelState.IsValid)
        {
            var (outcome, savedId) = await store.SavePackageAsync(id, input, actor);
            switch (outcome)
            {
                case ServicePackageSaveOutcome.Saved:
                    TempData["Success"] = id is null ? $"Đã tạo gói {input.Code}." : $"Đã lưu gói {input.Code}.";
                    return RedirectToAction(nameof(Index));
                case ServicePackageSaveOutcome.NotFound: return NotFound();
                case ServicePackageSaveOutcome.CodeTaken: ModelState.AddModelError("Input.Code", "Mã gói đã được dùng."); break;
                default: ModelState.AddModelError(string.Empty, AuditUnavailable); break;
            }
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new ServicePackageEditVm(id, input));
    }
}
