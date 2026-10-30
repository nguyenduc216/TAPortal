using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TAPortal.Web.Controllers;

// The standalone "Tài khoản Customer Portal" page was retired: memberships are managed in each customer's workspace and
// listed in Customers → "Tài khoản Portal". Old links and bookmarks are redirected without losing customer context.
[Authorize(Policy = "SystemAdminOnly")]
public sealed class CustomerPortalAccountsController : Controller
{
    [HttpGet]
    public IActionResult Index(Guid? customerId) => customerId is { } id && id != Guid.Empty
        ? RedirectToAction("PortalAccounts", "Customers", new { id })
        : RedirectToAction("Accounts", "Customers");
}
