using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// "Truy cập hộ" (operator 2026-11-14): Người dùng > ⋮ > "Truy cập hộ" asks for a reason, then the browser works under that
// account (its permissions only) until "Thoát truy cập hộ" (AccountController.StopImpersonation), sign-out or one hour. History: Quản lý hệ thống > Lịch sử truy cập hộ.
[Authorize]
public sealed class ImpersonationController(PortalDb db, IImpersonationStore store, TimeProvider clock, ILogger<ImpersonationController> logger) : Controller
{
    public sealed record StartVm(ImpersonationTarget Target, string? Reason, string? Error);

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.UsersImpersonate)]
    public async Task<IActionResult> Start(Guid id)
    {
        var target = await store.FindTargetAsync(id);
        if (target is null) return NotFound();
        var error = Impersonation.IsActive(User) ? "Bạn đang truy cập hộ một tài khoản khác. Hãy thoát truy cập hộ trước." : await CheckAsync(target);
        return View(new StartVm(target, null, error));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.UsersImpersonate)]
    public async Task<IActionResult> Start(Guid id, [FromForm] string? reason)
    {
        var target = await store.FindTargetAsync(id);
        if (target is null) return NotFound();
        if (Impersonation.IsActive(User)) return View(new StartVm(target, reason, "Bạn đang truy cập hộ một tài khoản khác. Hãy thoát truy cập hộ trước."));
        var error = Impersonation.ValidateReason(reason) ?? await CheckAsync(target);
        var account = error is null ? await db.GetAuthUserAsync(target.Username) : null;
        var actor = error is null ? await db.GetAuthUserAsync(User.Identity?.Name ?? "") : null;
        if (error is null && (actor is null || actor.Id != ActorId || !Impersonation.MayImpersonate(actor)))
            error = "Tài khoản của bạn không còn quyền truy cập hộ.";
        if (error is not null || account is null || actor is null) return View(new StartVm(target, reason, error ?? "Không thể truy cập hộ tài khoản này."));

        var text = reason!.Trim();
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + Impersonation.MaxDuration;
        Guid sessionId;
        try
        {
            sessionId = await store.StartAsync(actor.Id, account.Id, text, expires, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        }
        catch (Exception ex) when (ex is SqlException or AuditUnavailableException)
        {
            logger.LogError(ex, "IMPERSONATION_START_FAILED actor {ActorId} target {TargetId}", actor.Id, account.Id);
            return View(new StartVm(target, reason, "Không ghi được nhật ký truy cập hộ nên chưa thể truy cập hộ. Liên hệ quản trị hệ thống."));
        }

        var info = new ImpersonationInfo(sessionId, actor.Id, actor.Username, actor.DisplayName, AdminClaims.Stamp(actor.PasswordHash), text, now, expires);
        var principal = AdminClaims.Principal(AdminClaims.For(account, mustChangePassword: false).Concat(Impersonation.Claims(info)));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        logger.LogWarning("IMPERSONATION_START session {SessionId} actor {ActorId} target {TargetId}", sessionId, actor.Id, account.Id);
        TempData["Success"] = $"Bạn đang truy cập hộ {account.DisplayName}. Mọi thao tác được ghi nhận là của {actor.DisplayName} dưới quyền tài khoản {account.Username}.";
        return AdminNavigation.FirstAllowed(principal) is { } page ? RedirectToAction(page.Action, page.Controller) : RedirectToAction("Profile", "Account");
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.UsersImpersonate)]
    public async Task<IActionResult> Index()
    {
        try { return View(await store.ListAsync()); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Impersonation history unavailable.");
            ViewData["Unavailable"] = true;
            return View(Array.Empty<ImpersonationSessionRow>());
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.UsersImpersonate)]
    public async Task<IActionResult> Details(Guid id)
    {
        var detail = await store.GetAsync(id);
        return detail is null ? NotFound() : View(detail);
    }

    private async Task<string?> CheckAsync(ImpersonationTarget target) =>
        Impersonation.CheckTarget(ActorId, target.Id, await db.GetAuthUserAsync(target.Username));
}
