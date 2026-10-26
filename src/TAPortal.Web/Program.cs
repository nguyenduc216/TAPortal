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
    options.AccessDeniedPath = "/account/access-denied";
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
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program
{
}
