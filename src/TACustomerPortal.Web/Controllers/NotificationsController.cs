using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Notifications;

namespace TACustomerPortal.Web.Controllers;

public sealed record NotificationsVm(CustomerMembership Customer, NotificationRecipients Recipients, IReadOnlyList<ChannelView> Channels,
    IReadOnlyList<OutboxEntry> Recent, IReadOnlyList<string> Errors, string? ErrorChannel);

// "Cài đặt thông báo" (operator decision 2026-11-05), OWNER/ADMIN only:
// 1. Nhận thông báo từ T.A: where T.A's system channels reach this company (email, Zalo phone, Telegram chat id).
// 2. The company's own channels (Zalo OA, Telegram group, SMTP) used to notify its buyers and its staff group.
// Everything is read and written for the signed-in company only (scope CUSTOMER + its id); secrets are never shown back.
public sealed class NotificationsController(ICurrentCustomerContext customer, NotificationSettings settings) : CustomerAreaController(customer)
{
    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        return View(await Model(m, [], null));
    }

    [HttpPost]
    public async Task<IActionResult> Recipients(string? email, string? zaloPhone, string? telegramChatId, string[]? events)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var errors = await settings.SaveRecipientsAsync(m.CustomerId, email, zaloPhone, telegramChatId, events ?? [], Actor, HttpContext.RequestAborted);
        if (errors.Count > 0) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(nameof(Index), await Model(m, errors, "RECIPIENTS")); }
        Flash("Đã lưu nơi nhận thông báo từ T.A.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Save(string channel, bool enabled, string[]? events, string[]? clear)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        var form = NotificationCatalog.Fields(channel).ToDictionary(f => f.Key, f => (string?)Request.Form["f_" + f.Key].ToString());
        var errors = await settings.SaveAsync(NotificationScope.Customer, m.CustomerId, channel, enabled, events ?? [], form, clear ?? [], Actor, HttpContext.RequestAborted);
        if (errors.Count > 0) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(nameof(Index), await Model(m, errors, channel)); }
        Flash($"Đã lưu kênh {NotificationCatalog.Label(channel)}.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Test(string channel, string? recipient)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m)) return Forbid();
        if (!NotificationCatalog.Allowed(NotificationScope.Customer, channel)) return BadRequest();
        var result = await settings.TestAsync(NotificationScope.Customer, m.CustomerId, channel, recipient, HttpContext.RequestAborted);
        Flash(result, error: !result.StartsWith("Gửi thử thành công", StringComparison.Ordinal));
        return RedirectToAction(nameof(Index));
    }

    private async Task<NotificationsVm> Model(CustomerMembership m, IReadOnlyList<string> errors, string? errorChannel)
    {
        var ct = HttpContext.RequestAborted;
        return new(m, await settings.RecipientsAsync(m.CustomerId, ct), await settings.ChannelsAsync(NotificationScope.Customer, m.CustomerId, ct),
            await settings.RecentAsync(NotificationScope.Customer, m.CustomerId, 30, ct), errors, errorChannel);
    }
}
