using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// Base for the payments area (bank accounts, contacts, orders, settings): everything is scoped to the customer selected
/// at sign-in. Viewing is open to every member; changes need OWNER or ADMIN until the flexible role model replaces it.
/// </summary>
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[PaymentsDataUnavailable]
public abstract class CustomerAreaController(ICurrentCustomerContext customer) : Controller
{
    protected Task<CustomerMembership?> Current() => customer.Get();

    protected Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static bool CanManage(CustomerMembership membership) => membership.Role is "OWNER" or "ADMIN";

    protected IActionResult SelectCustomer() => RedirectToAction("SelectCustomer", "Account");

    protected void Flash(string message, bool error = false) => TempData[error ? "Error" : "Success"] = message;
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
