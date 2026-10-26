using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;

namespace TACustomerPortal.Web.Controllers;
public sealed class DashboardController(ICurrentCustomerContext customer) : Controller
{
    public async Task<IActionResult> Index()
    {
        var current = await customer.Get();
        return current is null ? Redirect("/account/select-customer") : View(current);
    }
}
