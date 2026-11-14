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
    // Claims follow the database on every request; "Truy cập hộ" sessions are checked and ended here too.
    options.Events.OnValidatePrincipal = AdminPrincipalValidator.ValidateAsync;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAuthorization(options => options.AddPolicy("SystemAdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("SYS_ADMIN")));
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<IPasswordSecurityStore, SqlPasswordSecurityStore>(); // weak password list + initial password (operator 2026-11-13)
builder.Services.AddScoped<PortalDb>();
builder.Services.AddScoped<MembershipAdminStore>();
builder.Services.AddScoped<ICustomerPortalAccounts, SqlCustomerPortalAccounts>();
builder.Services.AddScoped<IUserRoleStore, SqlUserRoleStore>();
builder.Services.AddScoped<IUserAccountStore, SqlUserAccountStore>();
builder.Services.AddScoped<IRoleAdminStore, SqlRoleAdminStore>();
builder.Services.AddScoped<IEmployeeStore, SqlEmployeeStore>();
builder.Services.AddScoped<IHrCatalogStore, SqlHrCatalogStore>();
builder.Services.AddScoped<IContractStore, SqlContractStore>();
// Hợp đồng dịch vụ: AI contract review (Claude API key in ContractReview:Anthropic:ApiKey, set by the operator on the server).
builder.Services.AddScoped<IContractReviewStore, SqlContractReviewStore>();
builder.Services.AddScoped<ILegalLibraryStore, SqlLegalLibraryStore>();
// Kho văn bản pháp luật → Văn bản mới cần duyệt (LEGAL-WATCH): weekly Công báo read, monthly vbpl.vn status check.
builder.Services.AddScoped<ILegalWatchStore, SqlLegalWatchStore>();
builder.Services.AddScoped<LegalWatchService>();
builder.Services.AddSingleton<LegalWatchTrigger>();
builder.Services.AddSingleton<ILegalWatchWeb, HttpLegalWatchWeb>();
builder.Services.AddHttpClient(HttpLegalWatchWeb.ClientName, c => c.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All });
if (!string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
    builder.Services.AddHostedService<LegalWatchScheduler>();
builder.Services.AddSingleton(ContractReviewAiOptions.From(builder.Configuration));
builder.Services.AddSingleton<IContractReviewAi, AnthropicContractReviewAi>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
    builder.Services.AddHostedService<ContractReviewWorker>();
builder.Services.AddScoped<ICustomerStaffStore, SqlCustomerStaffStore>();
builder.Services.AddScoped<IImpersonationStore, SqlImpersonationStore>();
builder.Services.AddScoped<IWorkCatalogStore, SqlWorkCatalogStore>();
builder.Services.AddScoped<IEmployeePhotoStore, SqlEmployeePhotoStore>(); // Ảnh đại diện nhân viên (migration 051) // Danh mục công việc (operator 2026-11-14) // "Truy cập hộ" (operator 2026-11-14)
builder.Services.AddScoped<IWebsiteAdminStore, SqlWebsiteAdminStore>(); // Website TA (WEBSITE-TA)
builder.Services.AddSingleton<TAPortal.Shared.IPortalContentStore, TAPortal.Shared.SqlPortalContentStore>(); // Hóa đơn tự động emails and terms
builder.Services.AddScoped<ICustomerAdminStore, SqlCustomerAdminStore>();
builder.Services.AddScoped<ICustomerProfileStore, SqlCustomerProfileStore>();
builder.Services.AddScoped<ICustomerImportStore, SqlCustomerImportStore>();
builder.Services.AddMemoryCache(); // the uploaded workbook between preview and confirmation (never written to disk)
builder.Services.AddSingleton<ICustomerSecretProtector, AesGcmCustomerSecretProtector>();
builder.Services.AddScoped<IDashboardStore, SqlDashboardStore>();
builder.Services.AddScoped<IWorkTaskStore, SqlWorkTaskStore>();
// Recurring tasks (Công việc → Việc lặp theo kỳ) are generated hourly; not registered without a database (tests, local).
if (!string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
    builder.Services.AddHostedService<WorkTaskScheduler>();
builder.Services.AddScoped<ITingeeMerchantStore, SqlTingeeMerchantStore>();
builder.Services.AddScoped<IServicePackageStore, SqlServicePackageStore>();
builder.Services.AddScoped<ITingeeSandboxInbox, SqlTingeeSandboxInbox>();
builder.Services.AddScoped<TAPortal.Integrations.Tingee.IPaymentTransactionStore, TAPortal.Integrations.Tingee.SqlPaymentTransactionStore>();
TAPortal.Integrations.Notifications.NotificationServiceCollectionExtensions.AddNotifications(builder.Services);
// Zalo Mini App account page: phone OTP and the one-time Mini App QR (ZALO-MINI-APP, operator 2026-11-13).
TAPortal.Integrations.ZaloMiniApp.ZaloMiniAppServiceCollectionExtensions.AddZaloMiniApp(builder.Services, builder.Configuration);
TAPortal.Integrations.Branding.LogoServiceCollectionExtensions.AddLogos(builder.Services);
builder.Services.AddScoped<ITingeeSandboxMerchants, SqlTingeeSandboxMerchants>();
builder.Services.AddHttpClient(TAPortal.Shared.VietQrTaxCodeLookup.ClientName, c => { c.BaseAddress = new Uri("https://api.vietqr.io/"); c.Timeout = TimeSpan.FromSeconds(6); });
builder.Services.AddSingleton<TAPortal.Shared.ITaxCodeLookup, TAPortal.Shared.VietQrTaxCodeLookup>();
builder.Services.AddHttpClient(nameof(TAPortal.Web.Controllers.TingeeSandboxController), client => client.Timeout = TimeSpan.FromSeconds(20));
// Tingee Sandbox: webhooks are received here too (same signature-checked routes as TAPortal.Webhooks) into dbo.TingeeWebhookInbox.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
    builder.Services.AddSingleton<TAPortal.Integrations.Tingee.ITingeeWebhookInbox, TAPortal.Integrations.Tingee.SqlTingeeWebhookInbox>();
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
// The speech-to-text buttons (wwwroot/js/ta-work.js) need the microphone on this origin only. Registered before
// UseTaSecurityHeaders: OnStarting callbacks run in reverse order, so this one runs last and its value wins.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Permissions-Policy"] = AdminHeaders.PermissionsPolicy;
        return Task.CompletedTask;
    });
    return next(context);
});
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
// "Truy cập hộ" (operator 2026-11-14): while signed in under another account, every request is recorded in
// dbo.ImpersonationActions and every audited change names the person who really acted (ImpersonationAmbient).
// The account's own password, Zalo phone and a nested "Truy cập hộ" stay closed.
app.Use(async (context, next) =>
{
    var impersonation = Impersonation.Read(context.User);
    if (impersonation is null) { await next(); return; }
    var path = context.Request.Path;
    if (Impersonation.BlockedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect(context.Request.PathBase + "/account/accessdenied");
        await LogImpersonatedRequest(context, impersonation, StatusCodes.Status403Forbidden);
        return;
    }
    TAPortal.Shared.ImpersonationAmbient.Current = new(impersonation.ActorId, impersonation.SessionId);
    try { await next(); }
    finally
    {
        TAPortal.Shared.ImpersonationAmbient.Current = null;
        if (!path.StartsWithSegments(TAPortal.Shared.BuildInfo.Path, StringComparison.OrdinalIgnoreCase))
            await LogImpersonatedRequest(context, impersonation, context.Response.StatusCode);
    }
});
// Operator 2026-11-13: an account signed in with an initial/weak password (or MustChangePassword=1) sees only "Đổi mật khẩu".
string[] passwordChangeAllowed = ["/account/changepassword", "/account/logout", "/account/login", "/account/accessdenied", TAPortal.Shared.BuildInfo.Path];
app.Use(async (context, next) =>
{
    if (context.User.HasClaim(TAPortal.Web.Controllers.AccountController.MustChangePasswordClaim, "1")
        && !passwordChangeAllowed.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect(context.Request.PathBase + "/account/changepassword");
        return;
    }
    await next();
});
// Operator 2026-11-13: once ZaloMiniApp:RequirePhoneVerification is on, an account must verify its phone (Zalo OTP) first.
// Not while "Truy cập hộ": the account's phone is its owner's to verify.
app.UseWhen(context => !Impersonation.IsActive(context.User), branch => TAPortal.Integrations.ZaloMiniApp.PhoneVerificationGate.UsePhoneVerificationGate(branch, TAPortal.Web.Controllers.AccountController.ZaloPath,
    "/account/zalophone", "/account/zalosendotp", "/account/zaloconfirmotp", "/account/changepassword", "/account/logout", "/account/login", "/account/accessdenied", "/home/error", TAPortal.Shared.BuildInfo.Path));
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
TAPortal.Shared.BuildInfo.MapBuildInfo(app);
TAWebsite.Core.SiteAssets.MapSiteAssets(app).RequireAuthorization(); // default website images for the Website TA editor
// Tingee calls the webhook site (TAPortal.Webhooks, Tingee__WebhookBaseUrl). The Admin site keeps the same /tingee receivers,
// and the old /hooks/tingee path only until Tingee's endpoint points at the webhook site.
TAPortal.Integrations.Tingee.TingeeWebhookEndpoints.MapTingeeWebhooks(app);
TAPortal.Integrations.Tingee.TingeeWebhookEndpoints.MapTingeeWebhooks(app, "/hooks/tingee");
app.Run();
static async Task LogImpersonatedRequest(HttpContext context, ImpersonationInfo impersonation, int status)
{
    try
    {
        var path = context.Request.Path.Value ?? "/";
        if (context.Request.QueryString.HasValue) path += context.Request.QueryString.Value;
        await context.RequestServices.GetRequiredService<IImpersonationStore>().LogRequestAsync(impersonation.SessionId, context.Request.Method, path, status);
    }
    catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TAPortal.Impersonation")
            .LogWarning(ex, "IMPERSONATION_REQUEST_LOG_FAILED session {SessionId} {Method} {Path}", impersonation.SessionId, context.Request.Method, context.Request.Path.Value);
    }
}

public partial class Program
{
}
