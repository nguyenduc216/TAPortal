using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Cài ứng dụng + thông báo đẩy (operator 2026-11-16). The manifest is public (browsers fetch it without cookies, also on the
// sign-in page); everything else belongs to the signed-in user. While someone uses "Truy cập hộ", the device must not be
// subscribed to the other person's notifications.
public sealed class PushController(IWebPushStore store, IWebPushSender sender, IConfiguration configuration, ILogger<PushController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet("/manifest.webmanifest")]
    public IActionResult Manifest()
    {
        var root = Url.Content("~/");
        var manifest = new
        {
            id = root,
            name = "T.A Portal",
            short_name = "T.A Portal",
            description = "Tư vấn giải pháp T.A - quản lý công việc, hợp đồng và khách hàng.",
            lang = "vi",
            start_url = root,
            scope = root,
            display = "standalone",
            orientation = "any",
            background_color = "#ffffff",
            theme_color = "#082746",
            icons = new object[]
            {
                new { src = Url.Content("~/images/app-icon-192.png"), sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = Url.Content("~/images/app-icon-512.png"), sizes = "512x512", type = "image/png", purpose = "any" },
                new { src = Url.Content("~/images/app-icon-maskable-512.png"), sizes = "512x512", type = "image/png", purpose = "maskable" }
            },
            shortcuts = new object[]
            {
                new { name = "Việc của tôi", url = Url.Content("~/Work"), icons = new[] { new { src = Url.Content("~/images/app-icon-192.png"), sizes = "192x192" } } },
                new { name = "Danh sách công việc", url = Url.Content("~/Work/Tasks"), icons = new[] { new { src = Url.Content("~/images/app-icon-192.png"), sizes = "192x192" } } }
            }
        };
        return new JsonResult(manifest) { ContentType = "application/manifest+json; charset=utf-8" };
    }

    [Authorize]
    [HttpGet("/push/key")]
    public async Task<IActionResult> Key()
    {
        var vapid = await store.VapidAsync(WebPushSender.Subject(configuration));
        return vapid is null ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Json(new { publicKey = vapid.PublicKey });
    }

    [Authorize]
    [HttpPost("/push/subscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(string? endpoint, string? p256dh, string? auth)
    {
        if (UsersController.ActorId(User) is not Guid user || Impersonation.Read(User) is not null) return Forbid();
        if (!WebPushPolicy.IsAllowedEndpoint(endpoint) || !WebPushPolicy.IsValidKeys(p256dh, auth))
            return BadRequest(new { error = "Trình duyệt này không hỗ trợ thông báo đẩy." });
        var saved = await store.SaveAsync(user, endpoint!.Trim(), p256dh!.Trim(), auth!.Trim(), WebPushPolicy.Device(Request.Headers.UserAgent));
        if (!saved) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Chưa lưu được, vui lòng thử lại sau." });
        logger.LogInformation("Web push device saved for user {UserId} ({Host}).", user, new Uri(endpoint!).Host);
        return Json(new { ok = true });
    }

    [Authorize]
    [HttpPost("/push/unsubscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(string? endpoint)
    {
        if (UsersController.ActorId(User) is not Guid user) return Forbid();
        if (!string.IsNullOrWhiteSpace(endpoint) && endpoint.Length <= 800) await store.RemoveAsync(user, endpoint.Trim());
        return Json(new { ok = true });
    }

    [Authorize]
    [HttpPost("/push/test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test()
    {
        if (UsersController.ActorId(User) is not Guid user || Impersonation.Read(User) is not null) return Forbid();
        var result = await sender.SendAsync(user, new WebPushMessage("T.A Portal", "Thông báo thử: thiết bị này đã nhận được thông báo của T.A Portal.",
            Url.Action("Profile", "Account"), "ta-test"), HttpContext.RequestAborted);
        return Json(new { sent = result.Sent, failed = result.Failed, removed = result.Removed });
    }
}
