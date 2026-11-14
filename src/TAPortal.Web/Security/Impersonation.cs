using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TAPortal.Web.Services;

namespace TAPortal.Web.Security;

// The claims of an Admin portal session, built the same way at sign-in, on every request and for "Truy cập hộ".
public static class AdminClaims
{
    public const string DisplayName = "display_name";
    public const string PasswordStamp = "password_stamp";

    public static string Stamp(string passwordHash) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash)));

    public static List<Claim> For(AuthUserSnapshot user, bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(DisplayName, user.DisplayName),
            new(PasswordStamp, Stamp(user.PasswordHash))
        };
        if (!string.IsNullOrWhiteSpace(user.Email)) claims.Add(new(ClaimTypes.Email, user.Email));
        if (mustChangePassword) claims.Add(new(Controllers.AccountController.MustChangePasswordClaim, "1"));
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Permissions.Select(p => new Claim("permission", p)));
        claims.AddRange((user.RoleNames ?? []).Select(n => new Claim("role_name", n)));
        return claims;
    }

    public static ClaimsPrincipal Principal(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    // The account may still sign in: it exists, is active, its password did not change and it holds a role or permission.
    public static bool StillValid(AuthUserSnapshot? user, string? id, string? stamp) =>
        user is not null && id == user.Id.ToString() && (user.Roles.Count > 0 || user.Permissions.Count > 0) && stamp == Stamp(user.PasswordHash);
}

// What an impersonated ("Truy cập hộ") session carries next to the claims of the account used.
public sealed record ImpersonationInfo(Guid SessionId, Guid ActorId, string ActorUsername, string ActorDisplayName, string ActorStamp, string Reason, DateTime StartedAtUtc, DateTime ExpiresAtUtc);

// "Truy cập hộ" (operator 2026-11-14): a user holding AUTH.USERS.IMPERSONATE enters a reason and then works under another
// account with exactly that account's permissions. The session records who really acted: dbo.ImpersonationSessions
// (who, whose account, why, when), dbo.ImpersonationActions (every request) and, through ImpersonationAmbient, the
// ImpersonatorUserId of every dbo.AuditLogs row and task event written meanwhile.
public static class Impersonation
{
    public const string SessionClaim = "imp_session";
    public const string ActorIdClaim = "imp_actor_id";
    public const string ActorUsernameClaim = "imp_actor_username";
    public const string ActorDisplayClaim = "imp_actor_display";
    public const string ActorStampClaim = "imp_actor_stamp";
    public const string ReasonClaim = "imp_reason";
    public const string StartedClaim = "imp_started";
    public const string ExpiresClaim = "imp_expires";

    public const int ReasonMin = 10, ReasonMax = 500;
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(1);

    // Pages a session under another account never opens: the account's own password and Zalo phone, and a second
    // "Truy cập hộ" from inside the first.
    public static readonly IReadOnlyList<string> BlockedPaths = ["/account/changepassword", "/account/zalo", "/impersonation/start"];

    private static readonly string[] Keys = [SessionClaim, ActorIdClaim, ActorUsernameClaim, ActorDisplayClaim, ActorStampClaim, ReasonClaim, StartedClaim, ExpiresClaim];

    public static ImpersonationInfo? Read(ClaimsPrincipal? user)
    {
        if (user?.FindFirst(SessionClaim)?.Value is not { } session || !Guid.TryParse(session, out var sessionId)) return null;
        if (!Guid.TryParse(user.FindFirst(ActorIdClaim)?.Value, out var actorId)) return null;
        static DateTime When(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
        return new(sessionId, actorId, user.FindFirst(ActorUsernameClaim)?.Value ?? "", user.FindFirst(ActorDisplayClaim)?.Value ?? "",
            user.FindFirst(ActorStampClaim)?.Value ?? "", user.FindFirst(ReasonClaim)?.Value ?? "", When(user.FindFirst(StartedClaim)?.Value), When(user.FindFirst(ExpiresClaim)?.Value));
    }

    public static bool IsActive(ClaimsPrincipal? user) => Read(user) is not null;

    public static IEnumerable<Claim> Claims(ImpersonationInfo info) =>
    [
        new(SessionClaim, info.SessionId.ToString()),
        new(ActorIdClaim, info.ActorId.ToString()),
        new(ActorUsernameClaim, info.ActorUsername),
        new(ActorDisplayClaim, info.ActorDisplayName),
        new(ActorStampClaim, info.ActorStamp),
        new(ReasonClaim, info.Reason),
        new(StartedClaim, info.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
        new(ExpiresClaim, info.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture))
    ];

    public static bool IsImpersonationClaim(Claim claim) => Keys.Contains(claim.Type, StringComparer.Ordinal);

    public static bool MayImpersonate(AuthUserSnapshot user) =>
        user.Roles.Contains(AdminPermissions.SystemAdminRole, StringComparer.OrdinalIgnoreCase)
        || user.Permissions.Contains(AdminPermissions.UsersImpersonate, StringComparer.OrdinalIgnoreCase);

    public static string? ValidateReason(string? reason)
    {
        var text = (reason ?? "").Trim();
        if (text.Length < ReasonMin) return $"Nhập lý do truy cập hộ (ít nhất {ReasonMin} ký tự).";
        if (text.Length > ReasonMax) return $"Lý do tối đa {ReasonMax} ký tự.";
        return null;
    }

    // null = allowed. The account used must be another, sign-in eligible account that is not a system administrator
    // (a SYS_ADMIN session would widen the actor's rights).
    public static string? CheckTarget(Guid actorId, Guid targetId, AuthUserSnapshot? target)
    {
        if (actorId == targetId) return "Không thể truy cập hộ chính tài khoản của bạn.";
        if (target is null) return "Tài khoản này đang bị khóa, đã xóa hoặc chưa được phép đăng nhập nên không thể truy cập hộ.";
        if (target.Roles.Contains(AdminPermissions.SystemAdminRole, StringComparer.OrdinalIgnoreCase)) return "Không được truy cập hộ tài khoản Quản trị hệ thống.";
        if (target.Roles.Count == 0 && target.Permissions.Count == 0) return "Tài khoản này chưa có vai trò hoặc quyền nào nên không thể truy cập hộ.";
        return null;
    }

    public static string EndReasonLabel(string? reason, DateTime expiresAtUtc, DateTime nowUtc) => reason switch
    {
        null => expiresAtUtc <= nowUtc ? "Hết hạn" : "Đang truy cập",
        "EXIT" => "Đã thoát",
        "LOGOUT" => "Đăng xuất",
        "TIMEOUT" => "Hết thời gian",
        "REVOKED" => "Tự kết thúc",
        _ => reason
    };
}

// Rebuilds the principal on every request (OnValidatePrincipal): permissions follow the database, a changed password or a
// locked account ends the session, and a "Truy cập hộ" session ends (back to the actor's own account) when it expires,
// when the actor lost the right to use it, or when the account used became ineligible.
public static class AdminPrincipalValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<PortalDb>();
        var principal = context.Principal;
        var user = await db.GetAuthUserAsync(principal?.Identity?.Name ?? "");
        var valid = AdminClaims.StillValid(user, principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, principal?.FindFirst(AdminClaims.PasswordStamp)?.Value);
        var impersonation = Impersonation.Read(principal);
        if (impersonation is null)
        {
            if (!valid) { await RejectAsync(context); return; }
            context.ReplacePrincipal(AdminClaims.Principal(AdminClaims.For(user!, user!.MustChangePassword)));
            return;
        }

        var store = services.GetRequiredService<IImpersonationStore>();
        var clock = services.GetRequiredService<TimeProvider>();
        var actor = await db.GetAuthUserAsync(impersonation.ActorUsername);
        var actorValid = AdminClaims.StillValid(actor, impersonation.ActorId.ToString(), impersonation.ActorStamp) && Impersonation.MayImpersonate(actor!);
        var expired = impersonation.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime;
        var open = !expired && actorValid && valid && Impersonation.CheckTarget(impersonation.ActorId, user!.Id, user) is null
            && await store.IsOpenAsync(impersonation.SessionId);
        if (open)
        {
            context.ReplacePrincipal(AdminClaims.Principal(AdminClaims.For(user!, mustChangePassword: false).Concat(Impersonation.Claims(impersonation))));
            return;
        }

        await store.EndAsync(impersonation.SessionId, expired ? "TIMEOUT" : "REVOKED");
        services.GetRequiredService<ILoggerFactory>().CreateLogger("TAPortal.Impersonation")
            .LogWarning("IMPERSONATION_END session {SessionId} actor {ActorId}: {Reason}", impersonation.SessionId, impersonation.ActorId, expired ? "TIMEOUT" : "REVOKED");
        if (!actorValid) { await RejectAsync(context); return; }
        context.ReplacePrincipal(AdminClaims.Principal(AdminClaims.For(actor!, actor!.MustChangePassword)));
        context.ShouldRenew = true;   // the cookie is re-issued for the actor's own account
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
