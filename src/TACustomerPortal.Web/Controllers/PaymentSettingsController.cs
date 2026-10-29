using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;

// Company defaults applied to new orders: automatic e-invoice when fully paid (ON by default) and QR lifetime.
public sealed class PaymentSettingsController(ICurrentCustomerContext customer, IPaymentStore store) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        var s = await store.SettingsAsync(m.CustomerId);
        return View(new PaymentSettingsVm(m, CanManage(m), new() { AutoIssueInvoiceDefault = s.AutoIssueInvoiceDefault, DefaultQrExpireMinutes = s.DefaultQrExpireMinutes }));
    }

    [HttpPost]
    public async Task<IActionResult> Index(PaymentSettingsInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new PaymentSettingsVm(m, true, input));
        }
        await store.SaveSettingsAsync(m.CustomerId, new(input.AutoIssueInvoiceDefault, input.DefaultQrExpireMinutes), Actor);
        Flash("Đã lưu cài đặt thanh toán. Áp dụng cho đơn hàng tạo mới.");
        return RedirectToAction(nameof(Index));
    }
}
