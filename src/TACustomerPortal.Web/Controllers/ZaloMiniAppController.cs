using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TACustomerPortal.Web.Security;
using TAPortal.Integrations.ZaloMiniApp;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Controllers;

/// <summary>
/// JSON API of the T.A Zalo Mini App (ZALO-MINI-APP, operator 2026-11-13). The Mini App sends the Zalo access token
/// (zmp-sdk getAccessToken) and, to sign in, the phone-number token (getPhoneNumber); the server reads the Zalo user and
/// phone from Zalo itself and never trusts a phone number sent by the client. Signed-in calls carry
/// "Authorization: Bearer &lt;session&gt;". Anonymous for the cookie scheme, no antiforgery (no cookie is used).
/// </summary>
[AllowAnonymous, IgnoreAntiforgeryToken, ApiController, Route("api/zalo-mini-app"), EnableRateLimiting("miniapp"), EnableCors(MiniAppCors.Policy)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ZaloMiniAppController(MiniAppAuth auth) : ControllerBase
{
    public sealed record ResumeRequest(string? AccessToken);
    public sealed record QrRequest(string? Qr, string? AccessToken, string? PhoneToken);
    public sealed record LoginRequest(string? Username, string? Password, string? AccessToken, string? PhoneToken);
    public sealed record SelectRequest(string? AccessToken, Guid UserId);

    /// <summary>Opening the Mini App: a session (one linked account), accounts to pick from, or nothing (show sign-in).</summary>
    [HttpPost("resume")]
    public async Task<IActionResult> Resume([FromBody] ResumeRequest r, CancellationToken ct) => Result(await auth.ResumeAsync(r.AccessToken ?? "", ct));

    [HttpPost("qr")]
    public async Task<IActionResult> Qr([FromBody] QrRequest r, CancellationToken ct) => Result(await auth.QrSignInAsync(r.Qr, r.AccessToken ?? "", r.PhoneToken, ct));

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest r, CancellationToken ct) => Result(await auth.PasswordSignInAsync(r.Username, r.Password, r.AccessToken ?? "", r.PhoneToken, ct));

    [HttpPost("select")]
    public async Task<IActionResult> Select([FromBody] SelectRequest r, CancellationToken ct) => Result(await auth.SelectAsync(r.AccessToken ?? "", r.UserId, ct));

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        await Session(ct) is { } s ? Ok(new { ok = true, account = new { userId = s.UserId, displayName = s.Account.DisplayName, username = s.Account.Username } }) : Unauthorized(new { ok = false });

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (await Session(ct) is { } s) await auth.SignOutAsync(s, ct);
        return Ok(new { ok = true });
    }

    private Task<MiniAppSession?> Session(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        return auth.ValidateAsync(header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..].Trim() : null, ct);
    }

    private IActionResult Result(MiniAppResult r) => r.Ok
        ? Ok(new { ok = true, session = r.Session, account = r.Account, accounts = r.Accounts })
        : BadRequest(new { ok = false, error = r.Error });
}

/// <summary>The Mini App's CORS policy: only the origins in ZaloMiniApp:AllowedOrigins, no credentials.</summary>
public static class MiniAppCors
{
    public const string Policy = "zalo-mini-app";
}

/// <summary>Passwords are checked as on the web sign-in, sharing its per-username attempt limit.</summary>
public sealed class MiniAppPasswordCheck(PasswordService passwords, LoginAttemptLimiter limiter) : IMiniAppPasswordCheck
{
    public bool TryAcquire(string username) => limiter.TryAcquire(username);
    public bool Verify(string password, string? passwordHash) => passwords.Verify(password, passwordHash);
}
