using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;
public sealed class ProfileController(ICurrentCustomerContext customer, ICustomerStore store) : Controller
{
    public async Task<IActionResult> Company()
    {
        var current = await customer.Get();
        return current is null ? Redirect("/account/select-customer") : View(current);
    }

    public async Task<IActionResult> Personal() => View(await store.FindUser(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
}
