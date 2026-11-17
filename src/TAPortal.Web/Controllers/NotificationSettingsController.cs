using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Notifications;
using TAPortal.Integrations.ZaloMiniApp;

namespace TAPortal.Web.Controllers;

public sealed record ScopedChannel(string Scope, ChannelView View);
public sealed record NotificationSettingsVm(string Tab, IReadOnlyList<ScopedChannel> Channels, IReadOnlyList<OutboxEntry> Recent, IReadOnlyList<string> Errors, string? ErrorChannel, string? StoreError);

// T.A's own notification channels (scope SYSTEM, operator decision 2026-11-05): Zalo OA, Telegram bot, Telegram group, SMTP.
// They notify the companies that use the e-invoice portal (at the addresses each company sets in its portal) and T.A's
// staff group. Companies' own channels are configured by each company in the Customer Portal and are not shown here.
// Secrets are encrypted (DPAPI) and never sent back to the page. System administrators only.
[Authorize(Policy = AdminPermissions.Policy.NotificationsManage)]
// Operator 2026-11-15: two tabs. "Hóa đơn tự động" = SYSTEM channels that speak to customers; "Portal" = channels for
// T.A staff: the PORTAL email channel (account emails, legal watch) and the SYSTEM Telegram group of the staff.
public sealed class NotificationSettingsController(NotificationSettings settings, ILogger<NotificationSettingsController> logger) : Controller
{
    public const string InvoiceTab = "invoice", PortalTab = "portal";

    [HttpGet]
    public async Task<IActionResult> Index(string? tab) => View(await Model(Tab(tab), [], null));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string channel, string? scope, bool enabled, string[]? events, string[]? clear)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var s = Scope(scope);
        if (!NotificationCatalog.Allowed(s, channel)) return BadRequest();
        var form = NotificationCatalog.Fields(channel, s).ToDictionary(f => f.Key, f => (string?)Request.Form["f_" + f.Key].ToString());
        var errors = await settings.SaveAsync(s, null, channel, enabled, events ?? [], form, clear ?? [], actor, HttpContext.RequestAborted);
        var tab = TabOf(s, channel);
        if (errors.Count > 0) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(nameof(Index), await Model(tab, errors, s + ":" + channel)); }
        TempData["Success"] = $"Đã lưu kênh {NotificationCatalog.Label(channel)}.";
        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(string channel, string? scope, string? recipient, string? kind,
        [FromServices] IOtpTemplateSource otpTemplate, [FromServices] IOptions<ZaloMiniAppOptions> zalo)
    {
        var s = Scope(scope);
        if (!NotificationCatalog.Allowed(s, channel)) return BadRequest();
        // Operator 2026-11-17: "Gửi thử OTP" sends the account OTP template (the one the phone verification uses) with a random code.
        var otp = kind == "otp" && s == NotificationScope.System && channel == NotificationChannelKind.ZaloOa;
        var result = otp
            ? await settings.TestOtpAsync(recipient, await otpTemplate.TemplateIdAsync(HttpContext.RequestAborted), zalo.Value.OtpTemplateParam, HttpContext.RequestAborted)
            : await settings.TestAsync(s, null, channel, recipient, HttpContext.RequestAborted);
        TempData[result.StartsWith("Gửi thử thành công", StringComparison.Ordinal) || result.StartsWith("Gửi thử OTP thành công", StringComparison.Ordinal) ? "Success" : "Error"] = result;
        return RedirectToAction(nameof(Index), new { tab = TabOf(s, channel) });
    }

    private static string Tab(string? tab) => tab == PortalTab ? PortalTab : InvoiceTab;
    private static string Scope(string? scope) => scope == NotificationScope.Portal ? NotificationScope.Portal : NotificationScope.System;
    private static string TabOf(string scope, string channel) => NotificationCatalog.ForStaff(scope, channel) ? PortalTab : InvoiceTab;

    private async Task<NotificationSettingsVm> Model(string tab, IReadOnlyList<string> errors, string? errorChannel)
    {
        ViewData["Title"] = "Cài đặt thông báo";
        try
        {
            var all = new List<ScopedChannel>();
            foreach (var scope in new[] { NotificationScope.Portal, NotificationScope.System })
                all.AddRange((await settings.ChannelsAsync(scope, null, HttpContext.RequestAborted)).Select(c => new ScopedChannel(scope, c)));
            var channels = all.Where(c => NotificationCatalog.ForStaff(c.Scope, c.View.Channel) == (tab == PortalTab)).ToList();
            var recent = (await settings.RecentAsync(null, null, 100, HttpContext.RequestAborted))
                .Where(o => (o.Scope != NotificationScope.Customer && NotificationCatalog.ForStaff(o.Scope, o.Channel)) == (tab == PortalTab)).Take(50).ToList();
            return new(tab, channels, recent, errors, errorChannel, null);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "NOTIFICATION_SETTINGS_UNAVAILABLE");
            return new(tab, [], [], errors, errorChannel, "Không đọc được dbo.NotificationChannels (đã chạy migration 020 chưa?).");
        }
    }
}
