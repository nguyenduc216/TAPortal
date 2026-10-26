using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Threading.RateLimiting;
using TACustomerPortal.Web.Services;
using TACustomerPortal.Web.Security;
using TAPortal.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddScoped<ICustomerStore, CustomerStore>();
builder.Services.AddScoped<ICurrentCustomerContext, CurrentCustomerContext>();
builder.Services.AddScoped<CustomerCookieEvents>();
var protection = builder.Services.AddDataProtection().SetApplicationName("TA.CustomerPortal");
if (!string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeyPath"]))
{
    protection.PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"]!));
    if (OperatingSystem.IsWindows())
        protection.ProtectKeysWithDpapi();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "TA.Customer.Auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/access-denied";
    o.ExpireTimeSpan = TimeSpan.FromHours(2);
    o.SlidingExpiration = false;
    o.EventsType = typeof(CustomerCookieEvents);
});
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
// Read configuration when options are resolved, including host-supplied configuration.
// No configured proxies means forwarded headers remain disabled.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var trustedProxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    options.ForwardedHeaders = trustedProxies.Length == 0 ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in trustedProxies)
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});
var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/account/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();
public partial class Program
{
}
