using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/access-denied";
        options.Cookie.Name = "TA.Portal.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddScoped<IAuthorizationHandler, FunctionActionHandler>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<PortalDb>();
// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-008
// Components use a testable contract while PortalDb remains the production implementation and SQL boundary.
builder.Services.AddScoped<IBlazorPortalService>(services => services.GetRequiredService<PortalDb>());
builder.Services.AddScoped<OperationalDb>();
builder.Services.AddScoped<GlobalLoadingService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-009
// Preserve status codes while replacing blank local/runtime responses with the branded status page.
app.UseStatusCodePagesWithReExecute("/home/status-code", "?code={0}");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<TAPortal.Web.Components.App>()
    .AddInteractiveServerRenderMode();
// FEATURE_ID: TAPORTAL-PLATFORM-BLAZOR-MIGRATION
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-003
// Map attribute-routed MVC endpoints explicitly so staged MVC routes remain available beside Blazor endpoints.
app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

// Bootstrap the first administrator only when explicitly enabled and supplied through secure configuration.
// CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-003
if (builder.Configuration.GetValue<bool>("Bootstrap:EnsureAdmin"))
{
    var bootstrapPassword = builder.Configuration["Bootstrap:AdminPassword"];
    if (string.IsNullOrWhiteSpace(bootstrapPassword))
        throw new InvalidOperationException("Bootstrap:AdminPassword must be supplied through secure configuration when Bootstrap:EnsureAdmin is enabled.");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PortalDb>();
    var passwords = scope.ServiceProvider.GetRequiredService<PasswordService>();
    await db.EnsureAdminAsync(passwords.Hash(bootstrapPassword));
}

app.Run();
