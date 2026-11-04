using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Controllers;
[Route("account")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountController(ICustomerStore store, PasswordService passwords, LoginAttemptLimiter attempts, IPasswordResetStore resets,
    IEmailSender email, IOptions<EmailOptions> emailOptions, ILogger<AccountController> logger) : Controller
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
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError("", "Bạn đã thử đăng nhập quá nhiều lần. Vui lòng đợi 1 phút rồi thử lại.");
            return View(vm);
        }

        CustomerUser? user;
        bool valid;
        IReadOnlyList<CustomerMembership> memberships;
        try
        {
            user = await store.FindUser(vm.Username);
            // AUD-38: always run PBKDF2 (also for empty/malformed hashes) and load memberships only after the password matched,
            // so a wrong password for an existing account does the same work as an unknown username.
            var usableHash = user?.PasswordHash is { } h && h.StartsWith("PBKDF2$", StringComparison.Ordinal);
            valid = passwords.Verify(vm.Password, usableHash ? user!.PasswordHash : DummyHash) && usableHash;
            memberships = user is null || !valid ? [] : await store.Memberships(user.Id);
        }
        catch (Exception ex) when (IsDataUnavailable(ex))
        {
            // Database not configured/reachable or permission missing: keep the sign-in form, say what happened and give a
            // reference for the logs instead of the generic error page. Credentials are never logged.
            var reference = RequestReference;
            logger.LogError(ex, "CUSTOMER_LOGIN_DATA_UNAVAILABLE reference {Reference} sql error {SqlNumber}", reference, (ex as SqlException)?.Number);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", $"Hệ thống tạm thời chưa kiểm tra được đăng nhập vì không truy cập được dữ liệu. Vui lòng thử lại sau hoặc báo quản trị viên T.A (mã sự cố: {reference}).");
            return View(vm);
        }

        if (!valid || user is null || memberships.Count == 0)
        {
            ModelState.AddModelError("", "Tài khoản hoặc mật khẩu không đúng, hoặc chưa được cấp quyền.");
            return View(vm);
        }

        await HttpContext.SignOutAsync();
        await HttpContext.SignInAsync(CustomerIdentity.Create(user, memberships.Count == 1 ? memberships[0].CustomerId : null), new AuthenticationProperties { IsPersistent = false });
        try
        {
            await store.TouchLogin(user.Id);
        }
        catch (Exception ex) when (IsDataUnavailable(ex))
        {
            // The "last sign-in" timestamp is informational: failing to write it must not undo a successful sign-in.
            logger.LogWarning(ex, "CUSTOMER_LOGIN_TOUCH_FAILED user {UserId} sql error {SqlNumber}", user.Id, (ex as SqlException)?.Number);
        }
        if (user.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword));
        // Path-base aware (the portal may run under an IIS virtual directory).
        return memberships.Count == 1 ? LocalRedirect(Url.Content("~/")) : RedirectToAction(nameof(SelectCustomer));
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
        // Switching tenants must not renew the fixed two-hour login lifetime.
        var session = await HttpContext.AuthenticateAsync();
        if (!session.Succeeded || session.Properties is null)
            return Challenge();
        await HttpContext.SignInAsync(CustomerIdentity.Create(user, customerId), session.Properties);
        return LocalRedirect(Url.Content("~/"));
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

    // ---- Quên mật khẩu: a one-time link (30 minutes) to the account's email. The answer never reveals whether the
    // account exists, has an email or was throttled; only accounts that may use the Customer Portal are served.
    [AllowAnonymous, HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => View(new ForgotPasswordVm { Enabled = email.IsConfigured });

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm)
    {
        vm.Enabled = email.IsConfigured;
        if (!vm.Enabled || !ModelState.IsValid) return View(vm);
        try
        {
            var account = await resets.FindAccountAsync(vm.Login);
            var now = DateTime.UtcNow;
            if (account is not null && await resets.RecentRequestsAsync(account.UserId, now.AddHours(-1)) < PasswordResetTokens.MaxRequestsPerHour)
            {
                var token = PasswordResetTokens.New();
                await resets.SaveTokenAsync(account.UserId, PasswordResetTokens.Hash(token)!, now.Add(PasswordResetTokens.Lifetime), HttpContext.Connection.RemoteIpAddress?.ToString());
                await email.SendAsync(ResetEmail(account, emailOptions.Value.PublicBaseUrl!.TrimEnd('/') + "/account/reset-password?token=" + token));
                logger.LogInformation("CUSTOMER_PASSWORD_RESET_SENT user {UserId}", account.UserId);
            }
        }
        catch (Exception ex) when (IsDataUnavailable(ex) || ex is System.Net.Mail.SmtpException)
        {
            // Same answer for the user; the cause (database or mail server) goes to the log only.
            logger.LogError(ex, "CUSTOMER_PASSWORD_RESET_FAILED reference {Reference}", RequestReference);
        }
        vm.Sent = true;
        return View(vm);
    }

    [AllowAnonymous, HttpGet("reset-password")]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        var account = PasswordResetTokens.Hash(token) is { } hash ? await resets.FindByTokenAsync(hash, DateTime.UtcNow) : null;
        return View(new ResetPasswordVm { Token = token, Valid = account is not null, Username = account?.Username });
    }

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        var hash = PasswordResetTokens.Hash(vm.Token);
        var account = hash is null ? null : await resets.FindByTokenAsync(hash, DateTime.UtcNow);
        vm.Valid = account is not null;
        vm.Username = account?.Username;
        if (account is null || !ModelState.IsValid) return View(vm);
        var policy = PasswordPolicy.Validate(vm.NewPassword);
        if (!policy.IsValid)
        {
            ModelState.AddModelError(nameof(vm.NewPassword), policy.Error);
            return View(vm);
        }
        if (!await resets.ResetAsync(hash!, passwords.Hash(vm.NewPassword), DateTime.UtcNow))
        {
            vm.Valid = false;
            return View(vm);
        }
        logger.LogInformation("CUSTOMER_PASSWORD_RESET_DONE user {UserId}", account.UserId);
        await HttpContext.SignOutAsync();
        TempData["Notice"] = "Đã đặt mật khẩu mới. Vui lòng đăng nhập bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    private static EmailMessage ResetEmail(RecoveryAccount account, string link)
    {
        var name = System.Net.WebUtility.HtmlEncode(account.DisplayName);
        var user = System.Net.WebUtility.HtmlEncode(account.Username);
        var href = System.Net.WebUtility.HtmlEncode(link);
        var minutes = (int)PasswordResetTokens.Lifetime.TotalMinutes;
        var text = $"""
            Xin chào {account.DisplayName},

            Có yêu cầu đặt lại mật khẩu Customer Portal cho tài khoản {account.Username}.
            Mở liên kết sau để đặt mật khẩu mới (hiệu lực {minutes} phút, chỉ dùng một lần):
            {link}

            Nếu bạn không yêu cầu, hãy bỏ qua email này; mật khẩu hiện tại vẫn giữ nguyên.
            T.A
            """;
        var html = $"""
            <p>Xin chào {name},</p>
            <p>Có yêu cầu đặt lại mật khẩu Customer Portal cho tài khoản <strong>{user}</strong>.</p>
            <p><a href="{href}" style="display:inline-block;padding:10px 18px;background:#1d4ed8;color:#ffffff;border-radius:6px;text-decoration:none">Đặt mật khẩu mới</a></p>
            <p>Liên kết có hiệu lực {minutes} phút và chỉ dùng được một lần. Nếu nút không mở được, chép địa chỉ sau vào trình duyệt:<br><span style="word-break:break-all">{href}</span></p>
            <p>Nếu bạn không yêu cầu, hãy bỏ qua email này; mật khẩu hiện tại vẫn giữ nguyên.</p>
            <p>T.A</p>
            """;
        return new(account.Email, "Đặt lại mật khẩu T.A Customer Portal", text, html);
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
        ViewData["Reference"] = RequestReference;
        return View();
    }

    // SqlException: server/permission/schema problems; InvalidOperationException: missing connection string or pool exhausted.
    internal static bool IsDataUnavailable(Exception ex) => ex is SqlException or InvalidOperationException or TimeoutException;

    private string RequestReference => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
