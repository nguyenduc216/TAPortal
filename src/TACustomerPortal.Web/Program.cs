using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Threading.RateLimiting;
using TACustomerPortal.Web.Services;
using TACustomerPortal.Web.Security;
using TAPortal.Integrations;
using TAPortal.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddScoped<ICustomerStore, CustomerStore>();
builder.Services.AddScoped<ISignupStore, SqlSignupStore>();
builder.Services.AddHttpClient(VietQrTaxCodeLookup.ClientName, c => { c.BaseAddress = new Uri("https://api.vietqr.io/"); c.Timeout = TimeSpan.FromSeconds(6); });
builder.Services.AddSingleton<ITaxCodeLookup, VietQrTaxCodeLookup>();
builder.Services.AddScoped<ICurrentCustomerContext, CurrentCustomerContext>();
builder.Services.AddScoped<CustomerCookieEvents>();
builder.Services.AddScoped<IPaymentStore, SqlPaymentStore>();
builder.Services.AddScoped<OrderPayments>();
builder.Services.AddScoped<TAPortal.Integrations.Tingee.IOrderInvoiceStore, TAPortal.Integrations.Tingee.SqlOrderInvoiceStore>();
builder.Services.AddScoped<TAPortal.Integrations.Tingee.OrderInvoicer>();
builder.Services.AddScoped<TAPortal.Integrations.Tingee.IPaymentTransactionStore, TAPortal.Integrations.Tingee.SqlPaymentTransactionStore>();
TAPortal.Integrations.Notifications.NotificationServiceCollectionExtensions.AddNotifications(builder.Services);
builder.Services.AddScoped<IServicePackageStore, SqlServicePackageStore>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IPasswordResetStore, SqlPasswordResetStore>();
builder.Services.AddTingee(builder.Configuration);
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
    o.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("Quá nhiều yêu cầu đăng nhập từ địa chỉ này. Vui lòng đợi 1 phút rồi thử lại.", token);
    };
    o.AddPolicy("lookup", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
// Without a connection string every sign-in fails: make that visible at startup (no secret is logged).
if (string.IsNullOrWhiteSpace(app.Configuration["Database:ConnectionString"]))
    app.Logger.LogCritical("CUSTOMER_PORTAL_DB_NOT_CONFIGURED: set Database__ConnectionString for this site (TAPortalSys). Sign-in cannot work without it.");
app.UseForwardedHeaders();
app.UseTaSecurityHeaders();
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
app.UseMustChangePasswordGate();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();
public partial class Program
{
}
