using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

public sealed class AccountController(PortalDb db, PasswordService passwords, AdminLoginThrottle throttle, ILogger<AccountController> logger) : Controller
{
    // Unknown usernames still pay the full PBKDF2 cost so response time does not reveal which accounts exist.
    private static readonly string DummyHash = new PasswordService().Hash(Guid.NewGuid() + "Aa1!");

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return User.IsInRole("SYS_ADMIN") ? RedirectToAction("Index", "Home") : RedirectToAction(nameof(Profile));
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
        var user = await db.GetAuthUserAsync(vm.Username);
        var validPassword = passwords.Verify(vm.Password, user?.PasswordHash ?? DummyHash);
        if (user is null || !validPassword || (user.Roles.Count == 0 && user.Permissions.Count == 0))
        {
            logger.LogInformation("Admin login failed. Account partition {AccountTag}.", AdminLoginThrottle.PartitionTag(vm.Username));
            ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không đúng.");
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
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = vm.RememberMe, AllowRefresh = true });
        await db.TouchLoginAsync(user.Id);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return user.Roles.Contains("SYS_ADMIN") ? RedirectToAction("Index", "Home") : RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
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
        return View(user);
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var policy = PasswordPolicy.Validate(vm.NewPassword);
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

        await db.UpdatePasswordAsync(user.Id, passwords.Hash(vm.NewPassword));
        TempData["Success"] = "Đã đổi mật khẩu thành công.";
        return RedirectToAction(nameof(Profile));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
