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
    IEmailSender email, IOptions<EmailOptions> emailOptions, IPortalContentStore content, ILogger<AccountController> logger,
    IPasswordSecurityStore passwordSecurity) : Controller
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
            ModelState.AddModelError("", $"Hệ thống tạm thời chưa kiểm tra được đăng nhập vì không truy cập được dữ liệu. Vui lòng thử lại sau hoặc báo bộ phận hỗ trợ Hóa đơn tự động (mã sự cố: {reference}).");
            return View(vm);
        }

        if (!valid || user is null || memberships.Count == 0)
        {
            ModelState.AddModelError("", "Tài khoản hoặc mật khẩu không đúng, hoặc chưa được cấp quyền.");
            return View(vm);
        }

        // Operator 2026-11-13: a password on the weak list (edited in the Admin portal) must be replaced first.
        if (!user.MustChangePassword && (await passwordSecurity.GetAsync()).IsWeak(vm.Password))
        {
            user = user with { MustChangePassword = true };
            try { await store.RequirePasswordChange(user.Id); }
            catch (Exception ex) when (IsDataUnavailable(ex)) { logger.LogWarning(ex, "CUSTOMER_WEAK_PASSWORD_FLAG_FAILED user {UserId}", user.Id); }
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

    // ---- self sign-up (operator decisions 2026-11-06 and 2026-11-12) -----------------------------------------------
    // A business, a household business or an individual (who may have no tax code). No password is typed: the system creates
    // one, emails it with the login details (template "Thông tin đăng nhập", edited in the Admin portal) and asks for a new
    // one at first login. Nobody is signed in by the sign-up itself, so only the owner of the mailbox can use the account.
    [AllowAnonymous, HttpGet("register")]
    public IActionResult Register() => User.Identity?.IsAuthenticated == true ? LocalRedirect(Url.Content("~/")) : View(new SignupVm());

    [AllowAnonymous, HttpPost("register"), EnableRateLimiting("login")]
    public async Task<IActionResult> Register(SignupVm vm, [FromServices] ISignupStore signups)
    {
        vm.TaxCode = TaxCodeRules.Normalize(vm.TaxCode);
        vm.Email = vm.Email?.Trim().ToLowerInvariant() ?? "";
        vm.Phone = vm.Phone?.Trim() ?? "";
        vm.CompanyName = vm.CompanyName?.Trim() ?? "";
        vm.ContactName = vm.ContactName?.Trim() ?? "";
        vm.Address = string.IsNullOrWhiteSpace(vm.Address) ? null : vm.Address.Trim();
        if (vm.CustomerType is not (SignupVm.Business or SignupVm.Household or SignupVm.Individual)) vm.CustomerType = SignupVm.Business;
        if (vm.TaxCode is null)
        {
            if (vm.CustomerType != SignupVm.Individual) ModelState.AddModelError(nameof(vm.TaxCode), "Nhập mã số thuế.");
        }
        else if (vm.CustomerType == SignupVm.Business ? !TaxCodeRules.IsLookupable(vm.TaxCode) : !TaxCodeRules.IsValid(vm.TaxCode))
            ModelState.AddModelError(nameof(vm.TaxCode), vm.CustomerType switch
            {
                SignupVm.Business => "Mã số thuế doanh nghiệp gồm 10 chữ số (chi nhánh: 10 chữ số-3 chữ số).",
                SignupVm.Household => "Mã số thuế hộ kinh doanh gồm 10 hoặc 12 chữ số.",
                _ => "Mã số thuế cá nhân gồm 10 hoặc 12 chữ số (số CCCD). Có thể để trống."
            });
        if (!ModelState.IsValid) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(vm); }

        var password = PasswordPolicy.GenerateTemporary();
        var terms = await content.GetAsync(PortalContentCodes.Terms);
        SignupResult result;
        try
        {
            result = await signups.RegisterAsync(new SignupRequest(vm.TaxCode, vm.CompanyName, vm.CustomerType, vm.Address, vm.ContactName, vm.Email, vm.Phone,
                passwords.Hash(password), HttpContext.Connection.RemoteIpAddress?.ToString(),
                terms is null ? "default" : terms.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (IsDataUnavailable(ex))
        {
            var reference = RequestReference;
            logger.LogError(ex, "CUSTOMER_SIGNUP_DATA_UNAVAILABLE reference {Reference} sql error {SqlNumber}", reference, (ex as SqlException)?.Number);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", $"Hệ thống tạm thời chưa đăng ký được. Vui lòng thử lại sau hoặc liên hệ bộ phận hỗ trợ Hóa đơn tự động (mã sự cố: {reference}).");
            return View(vm);
        }
        switch (result.Outcome)
        {
            case SignupOutcome.TaxCodeTaken:
                ModelState.AddModelError(nameof(vm.TaxCode), "Mã số thuế này đã có tài khoản. Hãy đăng nhập, hoặc liên hệ bộ phận hỗ trợ Hóa đơn tự động để được thêm vào doanh nghiệp.");
                break;
            case SignupOutcome.EmailTaken:
                ModelState.AddModelError(nameof(vm.Email), "Email này đã được dùng cho một tài khoản khác. Hãy đăng nhập hoặc dùng email khác.");
                break;
            case SignupOutcome.AuditUnavailable:
                ModelState.AddModelError("", "Hệ thống tạm thời chưa đăng ký được. Vui lòng thử lại sau.");
                break;
            case SignupOutcome.Created:
                logger.LogInformation("CUSTOMER_SELF_REGISTERED customer {CustomerId} user {UserId}", result.CustomerId, result.UserId);
                var sent = await SendLoginDetails(vm, result.CustomerCode, password, result.Username ?? vm.Email);
                TempData["SignupEmail"] = vm.Email;
                TempData["SignupName"] = vm.CompanyName;
                TempData["SignupSent"] = sent;
                return RedirectToAction(nameof(RegisterDone));
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }

    [AllowAnonymous, HttpGet("register/done")]
    public IActionResult RegisterDone() =>
        TempData["SignupEmail"] is string mail
            ? View(new SignupDoneVm(mail, TempData["SignupName"] as string ?? "", TempData["SignupSent"] is true))
            : RedirectToAction(nameof(Login));

    /// <summary>The account exists either way; a mail failure is logged and the page says to use "Quên mật khẩu" or support.</summary>
    private async Task<bool> SendLoginDetails(SignupVm vm, string? customerCode, string password, string username)
    {
        if (!email.IsConfigured) { logger.LogError("CUSTOMER_SIGNUP_EMAIL_NOT_CONFIGURED"); return false; }
        try
        {
            var baseUrl = emailOptions.Value.PublicBaseUrl!.TrimEnd('/');
            var spec = PortalEmailTemplates.AccountCreated;
            var saved = await content.GetAsync(spec.Code);
            var mail = PortalEmailRenderer.Render(spec, saved?.Subject, saved?.Body, new Dictionary<string, string>
            {
                ["NguoiLienHe"] = vm.ContactName, ["TenKhachHang"] = vm.CompanyName, ["MaKhachHang"] = customerCode ?? vm.TaxCode ?? "",
                ["DiaChiDangNhap"] = baseUrl + "/account/login", ["TenDangNhap"] = username, ["MatKhau"] = password
            }, baseUrl);
            await email.SendAsync(new EmailMessage(vm.Email, mail.Subject, mail.TextBody, mail.HtmlBody));
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "CUSTOMER_SIGNUP_EMAIL_FAILED reference {Reference}", RequestReference);
            return false;
        }
    }

    /// <summary>The terms of service as a page (the sign-up form shows the same text in a dialog).</summary>
    [AllowAnonymous, HttpGet("terms")]
    public IActionResult Terms() => View();

    /// <summary>Name and address registered for an enterprise tax code, to fill the sign-up and buyer forms (public data).</summary>
    [AllowAnonymous, HttpGet("tax-lookup"), EnableRateLimiting("lookup")]
    public async Task<IActionResult> TaxLookup(string? code, [FromServices] ITaxCodeLookup lookup)
    {
        var result = await lookup.LookupAsync(code, HttpContext.RequestAborted);
        var message = result.Status switch
        {
            TaxCodeLookupStatus.Found => "Đã điền tên và địa chỉ theo đăng ký thuế.",
            TaxCodeLookupStatus.NotFound => "Không tìm thấy mã số thuế này. Kiểm tra lại hoặc nhập tay.",
            TaxCodeLookupStatus.Invalid => "Chỉ tra cứu được mã số thuế doanh nghiệp 10 chữ số (hoặc 10-3).",
            _ => "Tạm thời chưa tra cứu được. Vui lòng nhập tay tên và địa chỉ."
        };
        var info = result.Info;
        return Json(new { ok = info is not null, name = info?.Name, shortName = info?.ShortName, address = info?.Address, message });
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
        var security = await passwordSecurity.GetAsync();
        var policy = PasswordPolicy.Validate(vm.NewPassword);
        if (user is null || !passwords.Verify(vm.CurrentPassword, user.PasswordHash) || !policy.IsValid || vm.CurrentPassword == vm.NewPassword)
        {
            ModelState.AddModelError("", "Kiểm tra mật khẩu hiện tại và mật khẩu mới (ít nhất 8 ký tự, có chữ, số và ký tự đặc biệt).");
            return View(vm);
        }
        if (security.IsWeak(vm.NewPassword))
        {
            ModelState.AddModelError("", security.ValidateNew(vm.NewPassword).Error);
            return View(vm);
        }

        if (!await store.ChangePassword(user.Id, user.PasswordHash, passwords.Hash(vm.NewPassword)))
        {
            ModelState.AddModelError("", "Tài khoản đã thay đổi. Vui lòng đăng nhập lại.");
            return View(vm);
        }

        // Operator 2026-11-13: the new password is emailed to the account's address; a failed email never undoes the change.
        var mailed = false;
        if (!string.IsNullOrWhiteSpace(user.Email) && email.IsConfigured)
        {
            try
            {
                await email.SendAsync(PasswordChangedEmail(user.Email, user.DisplayName, user.Username, vm.NewPassword));
                mailed = true;
            }
            catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or FormatException or IOException)
            {
                logger.LogWarning(ex, "CUSTOMER_PASSWORD_EMAIL_FAILED user {UserId}", user.Id);
            }
        }
        await HttpContext.SignOutAsync();
        TempData["Notice"] = "Đã đổi mật khẩu" + (mailed ? " và gửi mật khẩu mới về email của bạn" : "") + ". Vui lòng đăng nhập bằng mật khẩu mới.";
        return RedirectToAction(nameof(Login));
    }

    public static EmailMessage PasswordChangedEmail(string to, string displayName, string username, string password)
    {
        var at = DateTime.UtcNow.AddHours(7).ToString("HH:mm dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var text = $"Xin chào {displayName},\n\nMật khẩu tài khoản Hóa đơn tự động ({username}) vừa được đổi lúc {at} (giờ Việt Nam).\nMật khẩu mới: {password}\n\n" +
            "Hãy lưu mật khẩu ở nơi an toàn và xóa email này sau khi đã ghi nhớ. Nếu bạn không thực hiện thay đổi này, hãy liên hệ ngay bộ phận hỗ trợ Hóa đơn tự động.\n\nHóa đơn tự động";
        static string e(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var html = $"<p>Xin chào {e(displayName)},</p><p>Mật khẩu tài khoản Hóa đơn tự động (<b>{e(username)}</b>) vừa được đổi lúc {e(at)} (giờ Việt Nam).</p>" +
            $"<p>Mật khẩu mới: <b>{e(password)}</b></p><p>Hãy lưu mật khẩu ở nơi an toàn và xóa email này sau khi đã ghi nhớ. Nếu bạn không thực hiện thay đổi này, hãy liên hệ ngay bộ phận hỗ trợ Hóa đơn tự động.</p><p>Hóa đơn tự động</p>";
        return new(to, "Hóa đơn tự động - Mật khẩu mới của bạn", text, html);
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
                await email.SendAsync(await ResetEmail(account, emailOptions.Value.PublicBaseUrl!.TrimEnd('/') + "/account/reset-password?token=" + token));
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
        var policy = (await passwordSecurity.GetAsync()).ValidateNew(vm.NewPassword);
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

    private async Task<EmailMessage> ResetEmail(RecoveryAccount account, string link)
    {
        var spec = PortalEmailTemplates.PasswordReset;
        var saved = await content.GetAsync(spec.Code);
        var mail = PortalEmailRenderer.Render(spec, saved?.Subject, saved?.Body, new Dictionary<string, string>
        {
            ["NguoiLienHe"] = account.DisplayName, ["TenDangNhap"] = account.Username, ["LienKet"] = link,
            ["ThoiHanPhut"] = ((int)PasswordResetTokens.Lifetime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, emailOptions.Value.PublicBaseUrl!);
        return new(account.Email, mail.Subject, mail.TextBody, mail.HtmlBody);
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
