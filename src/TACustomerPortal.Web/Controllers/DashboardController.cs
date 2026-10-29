using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;

namespace TACustomerPortal.Web.Controllers;
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DashboardController(ICurrentCustomerContext customer) : Controller
{
    public async Task<IActionResult> Index()
    {
        var current = await customer.Get();
        return current is null ? RedirectToAction("SelectCustomer", "Account") : View(current);
    }
}
