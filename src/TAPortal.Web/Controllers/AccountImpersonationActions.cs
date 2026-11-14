using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

public sealed partial class AccountController
{
    // "Thoát truy cập hộ": ends the session and signs the actor back in to their own account. Here (sign-in only) because
    // the account used may hold no user-management permission at all.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopImpersonation()
    {
        var info = Impersonation.Read(User);
        if (info is null) return Landing(User);
        await services.GetRequiredService<IImpersonationStore>().EndAsync(info.SessionId, "EXIT");
        logger.LogWarning("IMPERSONATION_END session {SessionId} actor {ActorId}: EXIT", info.SessionId, info.ActorId);
        var actor = await db.GetAuthUserAsync(info.ActorUsername);
        if (!AdminClaims.StillValid(actor, info.ActorId.ToString(), info.ActorStamp))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }
        var principal = AdminClaims.Principal(AdminClaims.For(actor!, actor!.MustChangePassword));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        TempData["Success"] = "Đã thoát truy cập hộ, bạn đang dùng lại tài khoản của mình.";
        return Impersonation.MayImpersonate(actor) ? RedirectToAction("Details", "Impersonation", new { id = info.SessionId }) : Landing(principal);
    }
}
