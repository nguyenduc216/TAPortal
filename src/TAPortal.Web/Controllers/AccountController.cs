using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

public sealed partial class AccountController(PortalDb db, IAdminAuthStore auth, PasswordService passwords, IAdminLoginThrottle throttle, ILogger<AccountController> logger,
    IPasswordSecurityStore passwordSecurity, IServiceProvider services) : Controller
{
    // Unknown, ineligible or hash-less accounts still pay the full PBKDF2 cost so response time does not reveal which accounts exist.
    private static readonly string DummyHash = new PasswordService().Hash(Guid.NewGuid() + "Aa1!");
    internal const string GenericLoginError = "Tài khoản hoặc mật khẩu không đúng.";
    internal const string MustChangePasswordClaim = "must_change_password";

    // PasswordService.Verify returns early (no PBKDF2) for empty/malformed hashes; never let that path be observable.
    private bool VerifyWithConstantWork(string password, string? storedHash)
    {
        var usable = storedHash is not null && storedHash.StartsWith("PBKDF2$", StringComparison.Ordinal);
        var ok = passwords.Verify(password, usable ? storedHash : DummyHash);
        return usable && ok;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return Landing(User);
        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginVm());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        // Throttle before validation, lookup or hashing so every POST is bounded identically for existing and unknown accounts.
        var decision = throttle.TryAcquire(HttpContext.Connection.RemoteIpAddress, vm.Username);
        if (!decision.Allowed)
        {
            var seconds = (int)Math.Ceiling(decision.RetryAfter.TotalSeconds);
            logger.LogWarning("Admin login throttled. Account partition {AccountTag}, retry after {Seconds}s.", AdminLoginThrottle.PartitionTag(vm.Username), seconds);
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, $"Bạn đã thử đăng nhập quá nhiều lần. Vui lòng thử lại sau khoảng {Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalMinutes))} phút.");
            vm.Password = string.Empty;
            return View(vm);
        }

        if (!ModelState.IsValid) return View(vm);
        // One lookup, then PBKDF2, for every outcome; roles/permissions are loaded only after the password matched.
        var user = await auth.FindLoginCandidateAsync(vm.Username);
        var validPassword = VerifyWithConstantWork(vm.Password, user?.PasswordHash);
        var grants = user is not null && validPassword ? await auth.LoadGrantsAsync(user.Id) : null;
        if (user is null || grants is null || (grants.Roles.Count == 0 && grants.Permissions.Count == 0))
        {
            logger.LogInformation("Admin login failed. Account partition {AccountTag}.", AdminLoginThrottle.PartitionTag(vm.Username));
            ModelState.AddModelError(string.Empty, GenericLoginError);
            vm.Password = string.Empty;
            return View(vm);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("display_name", user.DisplayName),
            new("password_stamp", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user.PasswordHash))))
        };
        if (!string.IsNullOrWhiteSpace(user.Email)) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        // Operator 2026-11-13: an initial password or one on the weak list must be replaced before anything else.
        var mustChange = user.MustChangePassword;
        if (!mustChange && (await passwordSecurity.GetAsync()).IsWeak(vm.Password))
        {
            mustChange = true;
            await auth.RequirePasswordChangeAsync(user.Id);
        }
        if (mustChange) claims.Add(new Claim(MustChangePasswordClaim, "1"));
        claims.AddRange(grants.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(grants.Permissions.Select(p => new Claim("permission", p)));
        claims.AddRange((grants.RoleNames ?? []).Select(n => new Claim("role_name", n)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = vm.RememberMe, AllowRefresh = true });
        await auth.TouchLoginAsync(user.Id);

        if (mustChange) return RedirectToAction(nameof(ChangePassword));
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return Landing(principal);
    }

    // Dashboard when allowed, else the first page (sidebar order) the account may open, else the profile.
    private IActionResult Landing(ClaimsPrincipal user) =>
        AdminNavigation.FirstAllowed(user) is { } page ? RedirectToAction(page.Action, page.Controller) : RedirectToAction(nameof(Profile));

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Signing out during "Truy cập hộ" ends that session too.
        if (Impersonation.Read(User) is { } impersonation)
        {
            try { await services.GetRequiredService<IImpersonationStore>().EndAsync(impersonation.SessionId, "LOGOUT"); }
            catch (Microsoft.Data.SqlClient.SqlException ex) { logger.LogWarning(ex, "IMPERSONATION_END_FAILED session {SessionId}", impersonation.SessionId); }
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // Ask supporting browsers to drop any cached admin pages for this origin (no-store already prevents storing them).
        Response.Headers["Clear-Site-Data"] = "\"cache\"";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await db.GetAuthUserAsync(User.Identity!.Name!);
        // Show role names, never role codes.
        if (user is not null)
            ViewBag.RoleNames = (await db.GetRolesAsync()).Where(r => user.Roles.Contains(r.Code, StringComparer.OrdinalIgnoreCase)).Select(r => r.Name).ToList();
        return View(user);
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        ViewBag.MustChange = User.HasClaim(MustChangePasswordClaim, "1");
        return View(new ChangePasswordVm());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        ViewBag.MustChange = User.HasClaim(MustChangePasswordClaim, "1");
        if (!ModelState.IsValid) return View(vm);
        var policy = (await passwordSecurity.GetAsync()).ValidateNew(vm.NewPassword);
        if (!policy.IsValid)
        {
            ModelState.AddModelError(nameof(vm.NewPassword), policy.Error);
            return View(vm);
        }

        var user = await db.GetAuthUserAsync(User.Identity!.Name!);
        if (user is null || !passwords.Verify(vm.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "Mật khẩu hiện tại không đúng.");
            return View(vm);
        }
        if (passwords.Verify(vm.NewPassword, user.PasswordHash))
        {
            ModelState.AddModelError(nameof(vm.NewPassword), "Mật khẩu mới phải khác mật khẩu hiện tại.");
            return View(vm);
        }

        try
        {
            await db.UpdatePasswordAsync(user.Id, passwords.Hash(vm.NewPassword));
        }
        catch (AuditUnavailableException)
        {
            ModelState.AddModelError(string.Empty, "Không thể ghi nhật ký audit nên mật khẩu chưa được đổi. Liên hệ quản trị hệ thống.");
            return View(vm);
        }
        // Operator 2026-11-13: the new password is emailed to the account's address. A failed email never undoes the change.
        var mailed = false;
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                var error = await services.GetRequiredService<TAPortal.Integrations.Notifications.NotificationSettings>()
                    .SendSystemEmailAsync(user.Email, "TAPortal - Mật khẩu mới của bạn", PasswordChangedEmail(user.DisplayName, user.Username, vm.NewPassword));
                mailed = error is null;
                if (error is not null) logger.LogWarning("ADMIN_PASSWORD_EMAIL_FAILED user {UserId}: {Error}", user.Id, error);
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                logger.LogWarning(ex, "ADMIN_PASSWORD_EMAIL_FAILED user {UserId}", user.Id);
            }
        }
        // The password stamp changed, so the session ends: sign in again with the new password.
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Success"] = "Đã đổi mật khẩu thành công" + (mailed ? " và gửi mật khẩu mới về email của bạn" : "") + ". Vui lòng đăng nhập lại bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    public static string PasswordChangedEmail(string displayName, string username, string password) =>
        $"Xin chào {displayName},\n\nMật khẩu tài khoản TAPortal ({username}) vừa được đổi lúc {DateTime.UtcNow.AddHours(7):HH:mm dd/MM/yyyy} (giờ Việt Nam).\n" +
        $"Mật khẩu mới: {password}\n\nHãy lưu mật khẩu ở nơi an toàn và xóa email này sau khi đã ghi nhớ. " +
        "Nếu bạn không thực hiện thay đổi này, hãy báo ngay cho quản trị hệ thống.\n\nTư vấn giải pháp T.A";

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
