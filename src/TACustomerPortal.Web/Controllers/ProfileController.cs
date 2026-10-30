using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ProfileController(ICurrentCustomerContext customer, ICustomerStore store) : Controller
{
    public async Task<IActionResult> Company()
    {
        var current = await customer.Get();
        return current is null ? RedirectToAction("SelectCustomer", "Account") : View(current);
    }

    public async Task<IActionResult> Personal()
    {
        var user = await store.FindUser(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (user is not null) return View(user);
        // The account was disabled or removed meanwhile: end the session instead of failing on a null model.
        await HttpContext.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }
}
