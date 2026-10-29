using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Tingee;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Customer Workspace → "Tingee": T.A (Master Merchant) creates a Tingee child merchant for the customer through the API,
// or links an existing merchant id. Every action takes the customer from the route id. Unlinking only removes the local
// mapping; it never deletes the merchant on Tingee.
public sealed partial class CustomersController
{
    [HttpGet]
    public async Task<IActionResult> Tingee(Guid id, [FromServices] ITingeeMerchantStore tingee, [FromServices] ITingeeApi api)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        return await TingeeView(customer, tingee, api, null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTingeeMerchant(Guid id, [Bind(Prefix = "Create")] TingeeMerchantCreateInput input, [FromServices] ITingeeMerchantStore tingee, [FromServices] ITingeeApi api)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (!ModelState.IsValid) return await TingeeView(customer, tingee, api, input, StatusCodes.Status400BadRequest);
        if (Refusal(customer, api) is string refused) return TingeeError(id, refused);
        TingeeMerchantLink? existing;
        try { existing = await tingee.GetAsync(id); }
        catch (SqlException ex) { logger.LogWarning(ex, "Tingee merchant mapping unavailable for customer {CustomerId}.", id); return TingeeError(id, StoreUnavailable); }
        if (existing is not null) return TingeeError(id, $"Khách hàng đã được liên kết với merchant Tingee #{existing.MerchantId}.");

        long merchantId;
        try
        {
            var created = await api.CreateMerchantAsync(new TingeeCreateMerchantRequest(input.Name.Trim(), input.PhoneNumber, Blank(input.Email), Code: Blank(input.Code), FullAddress: Blank(customer.Address)));
            merchantId = created.MerchantId;
        }
        catch (TingeeApiException ex) { return TingeeError(id, $"Tingee từ chối tạo tài khoản (mã {ex.Code}): {ex.TingeeMessage}"); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "Tingee unreachable creating merchant for {CustomerId}.", id); return TingeeError(id, "Không kết nối được Tingee. Vui lòng thử lại sau."); }

        var outcome = await SafeLink(new(id, merchantId, Blank(input.Code), input.PhoneNumber, Blank(input.Email), "CREATED", DateTime.UtcNow), tingee, actor);
        if (outcome == TingeeLinkOutcome.Saved)
        {
            TempData["Success"] = $"Đã tạo tài khoản Tingee (merchant #{merchantId}) và liên kết với khách hàng.";
            return RedirectToAction(nameof(Tingee), new { id });
        }
        // Created on Tingee but not stored here: keep the id visible so it can be linked by hand, never create twice.
        logger.LogError("TINGEE_MERCHANT_CREATED_NOT_LINKED customer={CustomerId} merchant={MerchantId} outcome={Outcome}", id, merchantId, outcome);
        return TingeeError(id, $"Tingee đã tạo merchant #{merchantId} nhưng chưa lưu được liên kết ({Explain(outcome)}). Dùng \"Liên kết merchant có sẵn\" với mã #{merchantId}; không tạo lại.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkTingeeMerchant(Guid id, [FromForm] long? merchantId, [FromServices] ITingeeMerchantStore tingee, [FromServices] ITingeeApi api)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (merchantId is not > 0) return TingeeError(id, "Mã merchant Tingee phải là số dương.");
        if (!new CustomerWorkspaceHeader(customer.Id, customer.Code, customer.Name, customer.IsActive, customer.Status, "").Operational)
            return TingeeError(id, InactiveCustomer);
        var outcome = await SafeLink(new(id, merchantId.Value, null, null, null, "LINKED", DateTime.UtcNow), tingee, actor);
        if (outcome != TingeeLinkOutcome.Saved) return TingeeError(id, Explain(outcome));
        TempData["Success"] = $"Đã liên kết merchant Tingee #{merchantId} với khách hàng.";
        return RedirectToAction(nameof(Tingee), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnlinkTingeeMerchant(Guid id, [FromServices] ITingeeMerchantStore tingee)
    {
        if (await customers.GetAsync(id) is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        TingeeLinkOutcome outcome;
        try { outcome = await tingee.UnlinkAsync(id, actor); }
        catch (SqlException ex) { logger.LogWarning(ex, "Tingee unlink failed for customer {CustomerId}.", id); return TingeeError(id, StoreUnavailable); }
        if (outcome != TingeeLinkOutcome.Saved) return TingeeError(id, Explain(outcome));
        TempData["Success"] = "Đã hủy liên kết Tingee trên TAPortal. Merchant trên Tingee không bị xóa.";
        return RedirectToAction(nameof(Tingee), new { id });
    }

    private const string StoreUnavailable = "Không đọc/ghi được bảng liên kết Tingee (dbo.TingeeMerchants). Kiểm tra migration 011 và quyền cơ sở dữ liệu.";
    private const string InactiveCustomer = "Khách hàng đang ngừng hoạt động: không thể tạo hoặc liên kết tài khoản Tingee.";

    private async Task<IActionResult> TingeeView(CustomerEditVm customer, ITingeeMerchantStore tingee, ITingeeApi api, TingeeMerchantCreateInput? input, int status = StatusCodes.Status200OK)
    {
        TingeeMerchantLink? link = null;
        var available = true;
        try { link = await tingee.GetAsync(customer.Id); }
        catch (SqlException ex) { logger.LogWarning(ex, "Tingee merchant mapping unavailable for customer {CustomerId}.", customer.Id); available = false; }
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Tingee);
        Response.StatusCode = status;
        input ??= new() { Name = customer.Name, PhoneNumber = Digits(customer.Phone), Email = customer.Email, Code = customer.Code.Length <= 20 ? customer.Code : null };
        return View("Tingee", new CustomerTingeeVm(customer, link, available, api.IsConfigured, input));
    }

    private static string? Refusal(CustomerEditVm customer, ITingeeApi api) =>
        !api.IsConfigured ? "Chưa cấu hình khóa API Master Merchant của Tingee (Tingee__ClientId, Tingee__SecretKey)."
        : !(customer.IsActive && customer.Status == "ACTIVE") ? InactiveCustomer : null;

    private async Task<TingeeLinkOutcome> SafeLink(TingeeMerchantLink link, ITingeeMerchantStore tingee, Guid actor)
    {
        try { return await tingee.LinkAsync(link, actor); }
        catch (SqlException ex) { logger.LogWarning(ex, "Tingee link failed for customer {CustomerId}.", link.CustomerId); return TingeeLinkOutcome.NotLinked; }
    }

    private static string Explain(TingeeLinkOutcome outcome) => outcome switch
    {
        TingeeLinkOutcome.CustomerAlreadyLinked => "Khách hàng đã có liên kết Tingee.",
        TingeeLinkOutcome.MerchantAlreadyLinked => "Merchant này đã được liên kết với khách hàng khác.",
        TingeeLinkOutcome.AuditUnavailable => "Không ghi được nhật ký audit nên thay đổi đã bị hủy.",
        TingeeLinkOutcome.NotLinked => StoreUnavailable,
        _ => "Không thể lưu liên kết Tingee."
    };

    private IActionResult TingeeError(Guid id, string message)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Tingee), new { id });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Digits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());
}
