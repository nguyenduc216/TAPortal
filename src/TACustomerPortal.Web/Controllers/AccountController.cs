using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Controllers;
[Route("account")]
public sealed class AccountController(ICustomerStore store, PasswordService passwords, LoginAttemptLimiter attempts) : Controller
{
    // Perform the same expensive password verification for unknown usernames.
    private static readonly string DummyHash = new PasswordService().Hash(Guid.NewGuid() + "Aa1!");
    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login() => View(new LoginVm());
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);
        if (!attempts.TryAcquire(vm.Username))
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var user = await store.FindUser(vm.Username);
        var valid = passwords.Verify(vm.Password, user?.PasswordHash ?? DummyHash);
        var memberships = user is null ? [] : await store.Memberships(user.Id);
        if (!valid || user is null || memberships.Count == 0)
        {
            ModelState.AddModelError("", "Tài khoản hoặc mật khẩu không đúng, hoặc chưa được cấp quyền.");
            return View(vm);
        }

        await HttpContext.SignOutAsync();
        await HttpContext.SignInAsync(CustomerIdentity.Create(user, memberships.Count == 1 ? memberships[0].CustomerId : null), new AuthenticationProperties { IsPersistent = false });
        await store.TouchLogin(user.Id);
        return Redirect(memberships.Count == 1 ? "/" : "/account/select-customer");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("select-customer")]
    public async Task<IActionResult> SelectCustomer() => View(await store.Memberships(UserId));
    [HttpPost("select-customer")]
    public async Task<IActionResult> SelectCustomer(Guid customerId)
    {
        var memberships = await store.Memberships(UserId);
        if (!memberships.Any(m => m.CustomerId == customerId))
            return Forbid();
        var user = await store.FindUser(UserId);
        if (user is null)
            return Challenge();
        await HttpContext.SignInAsync(CustomerIdentity.Create(user, customerId), new AuthenticationProperties { IsPersistent = false });
        return Redirect("/");
    }

    [HttpGet("change-password")]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid)
            return View(vm);
        var user = await store.FindUser(UserId);
        var policy = PasswordPolicy.Validate(vm.NewPassword);
        if (user is null || !passwords.Verify(vm.CurrentPassword, user.PasswordHash) || !policy.IsValid || vm.CurrentPassword == vm.NewPassword)
        {
            ModelState.AddModelError("", "Kiểm tra mật khẩu hiện tại và mật khẩu mới (ít nhất 8 ký tự, có chữ, số và ký tự đặc biệt).");
            return View(vm);
        }

        if (!await store.ChangePassword(user.Id, user.PasswordHash, passwords.Hash(vm.NewPassword)))
        {
            ModelState.AddModelError("", "Tài khoản đã thay đổi. Vui lòng đăng nhập lại.");
            return View(vm);
        }

        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View();
    }

    [AllowAnonymous, Route("error")]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View();
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
