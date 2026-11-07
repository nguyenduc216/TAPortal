using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// Base for the company area (bank accounts, contacts, orders, settings, ...): everything is scoped to the customer selected
/// at sign-in. Each controller names its module with [CustomerModule] (view right, checked before the action); changing
/// actions check the manage right of that module (CustomerAccess: OWNER/ADMIN everything, VIEWER view-only, MEMBER per
/// function).
/// </summary>
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[PaymentsDataUnavailable]
public abstract class CustomerAreaController(ICurrentCustomerContext customer) : Controller
{
    protected Task<CustomerMembership?> Current() => customer.Get();

    protected Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool CanManage(CustomerMembership membership, string module) => CustomerAccess.CanManage(membership, module);

    public static bool CanView(CustomerMembership membership, string module) => CustomerAccess.CanView(membership, module);

    /// <summary>The friendly 403 page: the member lacks the manage right on the module.</summary>
    protected IActionResult Denied(string module) => ModuleDenied.Result(module, manage: true, ModelState);

    protected IActionResult SelectCustomer() => RedirectToAction("SelectCustomer", "Account");

    // Messages may carry text from a payment partner: partner names are scrubbed before the customer sees them (PartnerText).
    protected void Flash(string message, bool error = false) => TempData[error ? "Error" : "Success"] = PartnerText.Scrub(message);
}

/// <summary>
/// The payments tables come from migration 011 (operator-run). Until it runs, or without permissions, SQL fails: show a
/// clear "not ready" page with a reference instead of the generic error page.
/// </summary>
public sealed class PaymentsDataUnavailableAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var unavailable = context.Exception is SqlException
            || (context.Exception is InvalidOperationException ex && ex.Message.Contains("ConnectionString", StringComparison.Ordinal));
        if (!unavailable) return;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TACustomerPortal.Payments");
        var reference = context.HttpContext.TraceIdentifier;
        logger.LogError(context.Exception, "CUSTOMER_PAYMENTS_DATA_UNAVAILABLE reference={Reference}", reference);
        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/PaymentsUnavailable.cshtml",
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState) { ["Reference"] = reference }
        };
        context.ExceptionHandled = true;
    }
}
