using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Notifications;

namespace TAPortal.Web.Controllers;

public sealed record NotificationSettingsVm(IReadOnlyList<ChannelView> Channels, IReadOnlyList<OutboxEntry> Recent, IReadOnlyList<string> Errors, string? ErrorChannel, string? StoreError);

// T.A's own notification channels (scope SYSTEM, operator decision 2026-11-05): Zalo OA, Telegram bot, Telegram group, SMTP.
// They notify the companies that use the e-invoice portal (at the addresses each company sets in its portal) and T.A's
// staff group. Companies' own channels are configured by each company in the Customer Portal and are not shown here.
// Secrets are encrypted (DPAPI) and never sent back to the page. System administrators only.
[Authorize(Policy = "SystemAdminOnly")]
public sealed class NotificationSettingsController(NotificationSettings settings, ILogger<NotificationSettingsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await Model([], null));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string channel, bool enabled, string[]? events, string[]? clear)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var form = NotificationCatalog.Fields(channel).ToDictionary(f => f.Key, f => (string?)Request.Form["f_" + f.Key].ToString());
        var errors = await settings.SaveAsync(NotificationScope.System, null, channel, enabled, events ?? [], form, clear ?? [], actor, HttpContext.RequestAborted);
        if (errors.Count > 0) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(nameof(Index), await Model(errors, channel)); }
        TempData["Success"] = $"Đã lưu kênh {NotificationCatalog.Label(channel)}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(string channel, string? recipient)
    {
        if (!NotificationCatalog.Allowed(NotificationScope.System, channel)) return BadRequest();
        var result = await settings.TestAsync(NotificationScope.System, null, channel, recipient, HttpContext.RequestAborted);
        TempData[result.StartsWith("Gửi thử thành công", StringComparison.Ordinal) ? "Success" : "Error"] = result;
        return RedirectToAction(nameof(Index));
    }

    private async Task<NotificationSettingsVm> Model(IReadOnlyList<string> errors, string? errorChannel)
    {
        ViewData["Title"] = "Cài đặt thông báo";
        try
        {
            return new(await settings.ChannelsAsync(NotificationScope.System, null, HttpContext.RequestAborted), await settings.RecentAsync(null, null, 50, HttpContext.RequestAborted), errors, errorChannel, null);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "NOTIFICATION_SETTINGS_UNAVAILABLE");
            return new([], [], errors, errorChannel, "Không đọc được dbo.NotificationChannels (đã chạy migration 020 chưa?).");
        }
    }
}
