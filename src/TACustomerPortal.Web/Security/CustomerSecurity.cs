using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Security;
public static class CustomerIdentity
{
    /// <summary>Set after an administrator reset the password: until it is changed only the change-password page is served.</summary>
    public const string MustChangePasswordClaim = "must_change_password";

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
        if (user.MustChangePassword)
            claims.Add(new(MustChangePasswordClaim, "1"));
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

public static class MustChangePasswordGate
{
    private static readonly string[] Allowed = ["/account/change-password", "/account/logout", "/account/error", "/account/access-denied", "/account/login", "/account/forgot-password", "/account/reset-password", TAPortal.Shared.BuildInfo.Path];

    /// <summary>A signed-in user whose password was reset by T.A is sent to "Đổi mật khẩu" for every other page.</summary>
    public static IApplicationBuilder UseMustChangePasswordGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.HasClaim(CustomerIdentity.MustChangePasswordClaim, "1")
            && !Allowed.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(context.Request.PathBase + "/account/change-password");
            return;
        }
        await next();
    });
}

public interface ICurrentCustomerContext
{
    Task<CustomerMembership?> Get();
}

// Scoped: the result is reused within one request (controller + layout workspace switcher share a single query).
public sealed class CurrentCustomerContext(IHttpContextAccessor accessor, ICustomerStore store) : ICurrentCustomerContext
{
    private Task<CustomerMembership?>? current;

    public Task<CustomerMembership?> Get() => current ??= Load();

    private async Task<CustomerMembership?> Load()
    {
        var user = accessor.HttpContext?.User;
        if (!Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !Guid.TryParse(user.FindFirstValue("customer_id"), out var customer))
            return null;
        return (await store.Memberships(id)).SingleOrDefault(m => m.CustomerId == customer);
    }
}
