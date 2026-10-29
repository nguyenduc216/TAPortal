using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
var protection = builder.Services.AddDataProtection().SetApplicationName("TA.AdminPortal");
if (!string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeyPath"]))
{
    protection.PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"]!));
    if (OperatingSystem.IsWindows())
        protection.ProtectKeysWithDpapi();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/accessdenied"; // conventional route of AccountController.AccessDenied
    options.Cookie.Name = "TA.Portal.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Events.OnValidatePrincipal = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<PortalDb>();
        var user = await db.GetAuthUserAsync(context.Principal?.Identity?.Name ?? "");
        var stamp = user is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash)));
        if (user is null || context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value != user.Id.ToString() || (user.Roles.Count == 0 && user.Permissions.Count == 0) || context.Principal?.FindFirst("password_stamp")?.Value != stamp)
        {
            context.RejectPrincipal();
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context.HttpContext);
        }
        else
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(System.Security.Claims.ClaimTypes.Name, user.Username),
                new("display_name", user.DisplayName),
                new("password_stamp", stamp!)
            };
            if (!string.IsNullOrWhiteSpace(user.Email)) claims.Add(new(System.Security.Claims.ClaimTypes.Email, user.Email));
            claims.AddRange(user.Roles.Select(r => new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, r)));
            claims.AddRange(user.Permissions.Select(p => new System.Security.Claims.Claim("permission", p)));
            context.ReplacePrincipal(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        }
    };
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization(options => options.AddPolicy("SystemAdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("SYS_ADMIN")));
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<PortalDb>();
builder.Services.AddScoped<MembershipAdminStore>();
builder.Services.AddScoped<ICustomerPortalAccounts, SqlCustomerPortalAccounts>();
builder.Services.AddScoped<IUserRoleStore, SqlUserRoleStore>();
builder.Services.AddScoped<IUserAccountStore, SqlUserAccountStore>();
builder.Services.AddScoped<ICustomerAdminStore, SqlCustomerAdminStore>();
builder.Services.AddScoped<IDashboardStore, SqlDashboardStore>();
builder.Services.AddScoped<ITingeeMerchantStore, SqlTingeeMerchantStore>();
TAPortal.Integrations.IntegrationsServiceCollectionExtensions.AddTingee(builder.Services, builder.Configuration);
builder.Services.AddSingleton(sp =>
{
    var options = new AdminAuditOptions();
    builder.Configuration.GetSection("Security:Audit").Bind(options);
    return options;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var options = new AdminLoginThrottleOptions();
    builder.Configuration.GetSection("Security:AdminLogin").Bind(options);
    if (options.AccountPermitLimit < 1 || options.IpPermitLimit < 1 || options.Window <= TimeSpan.Zero || options.MaxTrackedKeys < 100)
        throw new InvalidOperationException("Security:AdminLogin limits must be positive (MaxTrackedKeys >= 100).");
    return options;
});
builder.Services.AddSingleton<AdminLoginThrottle>();
builder.Services.AddSingleton<IAdminLoginThrottle>(sp => sp.GetRequiredService<AdminLoginThrottle>());
builder.Services.AddScoped<IAdminAuthStore>(sp => sp.GetRequiredService<PortalDb>());
// AUD-24: X-Forwarded-For/Proto are trusted only from ReverseProxy:KnownProxies (exact IPs, one hop). Empty = ignored.
builder.Services.AddOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var proxies = TrustedProxyConfig.Parse(configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>());
    options.ForwardedHeaders = proxies.Count == 0 ? Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.None
        : Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();   // removes the framework's implicit loopback trust
    foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
});
var app = builder.Build();
if (!app.Services.GetRequiredService<AdminAuditOptions>().RequireDurableAudit)
    app.Logger.LogWarning("DURABLE_AUDIT_NOT_ENFORCED: Security:Audit:RequireDurableAudit=false; administrative changes may commit without a dbo.AuditLogs row. Enable after database/scripts/007-verify-audit-logs.sql reports PASS.");
app.UseForwardedHeaders();
app.UseTaSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
// Every dynamic admin response (pages, redirects, 401/403/429, errors) must never be stored by browsers or proxies.
// Registered after UseStaticFiles, so CSS/JS/images keep their normal caching.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.CacheControl = "no-store, no-cache, max-age=0";
        headers.Pragma = "no-cache";
        headers.Expires = "0";
        return Task.CompletedTask;
    });
    return next(context);
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program
{
}
