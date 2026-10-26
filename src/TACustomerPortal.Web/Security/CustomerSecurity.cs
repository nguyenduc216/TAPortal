using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Security;
public static class CustomerIdentity
{
    public static string Stamp(string hash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)));
    public static ClaimsPrincipal Create(CustomerUser user, Guid? customer = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new("password_stamp", Stamp(user.PasswordHash))
        };
        if (customer.HasValue)
            claims.Add(new("customer_id", customer.Value.ToString()));
        return new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}

public sealed class CustomerCookieEvents(ICustomerStore store) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            await Reject(context);
            return;
        }

        var user = await store.FindUser(id);
        if (user is null || principal.FindFirstValue("password_stamp") != CustomerIdentity.Stamp(user.PasswordHash))
        {
            await Reject(context);
            return;
        }

        var memberships = await store.Memberships(id);
        var selected = principal.FindFirstValue("customer_id");
        if (memberships.Count == 0 || (selected != null && (!Guid.TryParse(selected, out var customerId) || !memberships.Any(m => m.CustomerId == customerId))))
            await Reject(context);
    }

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync();
    }
}

public interface ICurrentCustomerContext
{
    Task<CustomerMembership?> Get();
}

public sealed class CurrentCustomerContext(IHttpContextAccessor accessor, ICustomerStore store) : ICurrentCustomerContext
{
    public async Task<CustomerMembership?> Get()
    {
        var user = accessor.HttpContext?.User;
        if (!Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !Guid.TryParse(user.FindFirstValue("customer_id"), out var customer))
            return null;
        return (await store.Memberships(id)).SingleOrDefault(m => m.CustomerId == customer);
    }
}
