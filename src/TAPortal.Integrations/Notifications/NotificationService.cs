using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Notifications;

/// <summary>A channel as a settings page shows it: saved values, which secrets are set (never their values), last test.</summary>
public sealed record ChannelView(string Channel, string Label, string Audience, IReadOnlyList<ChannelField> Fields, bool IsEnabled, IReadOnlySet<string> Events,
    IReadOnlyDictionary<string, string> Settings, IReadOnlySet<string> SecretKeys, DateTime? LastTestAtUtc, string? LastTestResult);

/// <summary>Settings pages of T.A Admin (scope SYSTEM) and of a company (scope CUSTOMER, its own id only).</summary>
public sealed class NotificationSettings(INotificationStore store, ISecretProtector protector, INotificationTransport transport, TimeProvider clock)
{
    // A company may send a few test messages a minute through its own channels; more is refused (no spam relay).
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> Tests = new();
    public const int TestsPerMinute = 5;

    /// <summary>DNS check that a company's SMTP host is public (replaceable in tests, which never use the network).</summary>
    public Func<string, CancellationToken, Task<bool>> PublicHost { get; init; } = PublicHostGuard.IsPublicAsync;

    public async Task<IReadOnlyList<ChannelView>> ChannelsAsync(string scope, Guid? customerId, CancellationToken ct = default)
    {
        Owner(scope, customerId);
        var saved = await store.ChannelsAsync(scope, customerId, ct);
        var views = new List<ChannelView>();
        foreach (var channel in NotificationCatalog.Channels(scope))
        {
            var c = saved.FirstOrDefault(s => s.Channel == channel);
            var secretKeys = c is null ? new HashSet<string>()
                : protector.Unprotect((await store.SecretAsync(scope, customerId, channel, ct))?.Secret).Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key).ToHashSet();
            views.Add(new(channel, NotificationCatalog.Label(channel), NotificationCatalog.Audience(scope, channel), NotificationCatalog.Fields(channel), c?.IsEnabled ?? false,
                c?.Events ?? new HashSet<string>(NotificationEvent.All.Select(e => e.Code)), c?.Settings ?? new Dictionary<string, string>(), secretKeys, c?.LastTestAtUtc, c?.LastTestResult));
        }
        return views;
    }

    /// <summary>
    /// Saves one channel. Blank secret fields keep the saved secret; <paramref name="clearSecrets"/> removes the named ones.
    /// Returns the validation errors (nothing saved when there are any).
    /// </summary>
    public async Task<IReadOnlyList<string>> SaveAsync(string scope, Guid? customerId, string channel, bool enabled, IEnumerable<string> events,
        IReadOnlyDictionary<string, string?> form, IEnumerable<string> clearSecrets, Guid actor, CancellationToken ct = default)
    {
        Owner(scope, customerId);
        if (!NotificationCatalog.Allowed(scope, channel)) return ["Kênh không được hỗ trợ."];
        var fields = NotificationCatalog.Fields(channel);
        var settings = new Dictionary<string, string>();
        var secrets = protector.Unprotect((await store.SecretAsync(scope, customerId, channel, ct))?.Secret);
        foreach (var key in clearSecrets) secrets.Remove(key);
        foreach (var f in fields)
        {
            var value = form.TryGetValue(f.Key, out var v) ? v?.Trim() : null;
            if (value is { Length: > 2000 }) return [$"{f.Label} quá dài."];
            if (f.Secret) { if (!string.IsNullOrEmpty(value)) secrets[f.Key] = value; }
            else if (!string.IsNullOrEmpty(value)) settings[f.Key] = value;
        }
        var errors = NotificationCatalog.Validate(channel, settings, secrets, enabled);
        if (channel == NotificationChannelKind.Smtp && scope == NotificationScope.Customer && settings.TryGetValue("host", out var host) && errors.Count == 0
            && !await PublicHost(host, ct))
            errors.Add("Máy chủ SMTP phải là địa chỉ công khai trên Internet.");
        if (errors.Count > 0) return errors;
        // Only known secret keys are kept.
        var kept = secrets.Where(p => fields.Any(f => f.Secret && f.Key == p.Key)).ToDictionary(p => p.Key, p => p.Value);
        await store.SaveChannelAsync(scope, customerId, channel, enabled, NotificationEvent.Join(events), settings, kept.Count == 0 ? null : protector.Protect(kept), actor, ct);
        return [];
    }

    /// <summary>Sends a test message now (not through the outbox) and records the result on the channel.</summary>
    public async Task<string> TestAsync(string scope, Guid? customerId, string channel, string? recipient, CancellationToken ct = default)
    {
        Owner(scope, customerId);
        var c = (await store.ChannelsAsync(scope, customerId, ct)).FirstOrDefault(x => x.Channel == channel);
        if (c is null) return "Chưa lưu cấu hình kênh này.";
        var key = $"{scope}:{customerId}";
        var now = clock.GetUtcNow().UtcDateTime;
        var queue = Tests.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < now.AddMinutes(-1)) queue.Dequeue();
            if (queue.Count >= TestsPerMinute) return "Bạn đã gửi thử quá nhiều lần, vui lòng thử lại sau một phút.";
            queue.Enqueue(now);
        }
        var to = channel == NotificationChannelKind.TelegramGroup ? c.Settings.GetValueOrDefault("chatId") : recipient?.Trim();
        if (string.IsNullOrEmpty(to)) return channel == NotificationChannelKind.Smtp ? "Vui lòng nhập email nhận thử." : channel == NotificationChannelKind.ZaloOa ? "Vui lòng nhập số điện thoại nhận thử." : "Vui lòng nhập chat ID nhận thử.";
        var sample = new NotifiableTransaction(0, Tingee.PaymentTxStatus.Matched, null, null, 10_000, "DH000000-0000", "0000000000", now, customerId, "Doanh nghiệp mẫu",
            Guid.Empty, "DH000000-0000", 10_000, 10_000, Tingee.TingeeOrderStatus.Paid, "Khách hàng mẫu", null, null);
        var data = c.Settings.TryGetValue("templateData", out var tpl) && NotificationCatalog.TemplateData(tpl) is { } map ? NotificationPlanner.Fill(map, NotificationPlanner.Values(sample)) : null;
        var message = new OutboxMessage($"test:{Guid.NewGuid():N}", scope, customerId, channel, to, "Tin thử từ hệ thống hóa đơn điện tử",
            "Đây là tin nhắn thử. Kênh thông báo đã được cấu hình đúng.", data, null);
        var (result, _) = await Dispatch.SendAsync(store, protector, transport, c, message, ct);
        var text = result.Ok ? $"Gửi thử thành công tới {to}." : $"Gửi thử thất bại: {result.Error}";
        await store.RecordTestAsync(scope, customerId, channel, text, ct);
        return text;
    }

    /// <summary>
    /// Sends one email now through the SYSTEM SMTP channel (not the outbox); null when sent, else the reason.
    /// Used for account emails of the Admin portal (operator 2026-11-13: the new password after a change).
    /// </summary>
    public async Task<string?> SendSystemEmailAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var c = (await store.ChannelsAsync(NotificationScope.System, null, ct)).FirstOrDefault(x => x.Channel == NotificationChannelKind.Smtp);
        if (c is null || !c.IsEnabled) return "Chưa bật kênh email hệ thống.";
        var message = new OutboxMessage($"account:{Guid.NewGuid():N}", NotificationScope.System, null, NotificationChannelKind.Smtp, to.Trim(), subject, body, null, null);
        var (result, _) = await Dispatch.SendAsync(store, protector, transport, c, message, ct);
        return result.Ok ? null : result.Error ?? "Gửi email thất bại.";
    }

    /// <summary>
    /// Sends one ZNS template message now through the SYSTEM Zalo OA channel (not the outbox), with another template than the
    /// channel's payment one; null when sent, else the reason. Used for the phone OTP (ZALO-MINI-APP, operator 2026-11-13).
    /// </summary>
    public async Task<string?> SendSystemZnsAsync(string phone, string templateId, IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
    {
        var c = (await store.ChannelsAsync(NotificationScope.System, null, ct)).FirstOrDefault(x => x.Channel == NotificationChannelKind.ZaloOa);
        if (c is null || !c.IsEnabled) return "Chưa bật kênh Zalo OA hệ thống.";
        var settings = new Dictionary<string, string>(c.Settings) { ["templateId"] = templateId };
        var message = new OutboxMessage($"otp:{Guid.NewGuid():N}", NotificationScope.System, null, NotificationChannelKind.ZaloOa, phone, null, "", data, null);
        var (result, _) = await Dispatch.SendAsync(store, protector, transport, c with { Settings = settings }, message, ct);
        return result.Ok ? null : result.Error ?? "Gửi tin Zalo thất bại.";
    }

    public Task<NotificationRecipients> RecipientsAsync(Guid customerId, CancellationToken ct = default) => store.RecipientsAsync(customerId, ct);

    public async Task<IReadOnlyList<string>> SaveRecipientsAsync(Guid customerId, string? email, string? zaloPhone, string? telegramChatId, IEnumerable<string> events, Guid actor, CancellationToken ct = default)
    {
        var errors = new List<string>();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        zaloPhone = string.IsNullOrWhiteSpace(zaloPhone) ? null : zaloPhone.Trim();
        telegramChatId = string.IsNullOrWhiteSpace(telegramChatId) ? null : telegramChatId.Trim();
        if (email is not null && !NotificationCatalog.Mail(email)) errors.Add("Email nhận thông báo không hợp lệ.");
        if (zaloPhone is not null && NotificationCatalog.ZaloPhone(zaloPhone) is null) errors.Add("Số điện thoại Zalo không hợp lệ (ví dụ 0912345678).");
        if (telegramChatId is not null && !NotificationCatalog.TelegramChat(telegramChatId)) errors.Add("Chat ID Telegram không hợp lệ.");
        if (errors.Count == 0) await store.SaveRecipientsAsync(customerId, new(email, zaloPhone, telegramChatId, NotificationEvent.Parse(string.Join(",", events))), actor, ct);
        return errors;
    }

    public Task<IReadOnlyList<OutboxEntry>> RecentAsync(string? scope, Guid? customerId, int max = 50, CancellationToken ct = default) => store.RecentAsync(scope, customerId, max, ct);

    private static void Owner(string scope, Guid? customerId)
    {
        if (scope == NotificationScope.System ? customerId is not null : scope != NotificationScope.Customer || customerId is null)
            throw new ArgumentException("A channel owner is SYSTEM without a customer, or CUSTOMER with one.");
    }
}

/// <summary>Sends through a channel with its decrypted secrets, saving refreshed tokens (optimistically, by version).</summary>
public static class Dispatch
{
    public static async Task<(SendResult Result, bool SecretsSaved)> SendAsync(INotificationStore store, ISecretProtector protector, INotificationTransport transport,
        NotificationChannel channel, OutboxMessage message, CancellationToken ct)
    {
        var secret = await store.SecretAsync(channel.Scope, channel.CustomerId, channel.Channel, ct);
        var result = await transport.SendAsync(channel, protector.Unprotect(secret?.Secret), message, ct);
        var saved = result.Secrets is { } fresh && secret is { } s && await store.ReplaceSecretAsync(channel.Scope, channel.CustomerId, channel.Channel, s.Version, protector.Protect(fresh), ct);
        return (result, saved);
    }
}

/// <summary>
/// Runs in the webhook site: plans messages for new transactions (outbox, idempotent by EventKey) and sends due messages,
/// retrying a failed one up to <see cref="MaxAttempts"/> times with a growing delay.
/// </summary>
public sealed class NotificationDispatcher(INotificationStore store, ISecretProtector protector, INotificationTransport transport, TimeProvider clock, ILogger<NotificationDispatcher> logger)
{
    public const int MaxAttempts = 5;
    /// <summary>Transactions older than this are never notified (no flood when a channel is switched on).</summary>
    public static readonly TimeSpan PlanWindow = TimeSpan.FromDays(2);

    public async Task<int> PlanAsync(int max = 50, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await store.UnnotifiedAsync(now - PlanWindow, max, ct);
        if (pending.Count == 0) return 0;
        var system = await store.ChannelsAsync(NotificationScope.System, null, ct);
        var companies = new Dictionary<Guid, (IReadOnlyList<NotificationChannel>, NotificationRecipients)>();
        var queued = 0;
        foreach (var t in pending)
        {
            IReadOnlyList<NotificationChannel> company = [];
            var recipients = NotificationRecipients.Empty;
            if (t.CustomerId is Guid c)
            {
                if (!companies.TryGetValue(c, out var loaded))
                    companies[c] = loaded = (await store.ChannelsAsync(NotificationScope.Customer, c, ct), await store.RecipientsAsync(c, ct));
                (company, recipients) = loaded;
            }
            var messages = NotificationPlanner.Build(t, system, company, recipients);
            await store.EnqueueAsync(t.Id, messages, ct);
            queued += messages.Count;
        }
        return queued;
    }

    public async Task<int> SendDueAsync(int max = 20, CancellationToken ct = default)
    {
        var sent = 0;
        var channels = new Dictionary<(string, Guid?), IReadOnlyList<NotificationChannel>>();
        foreach (var p in await store.DueAsync(max, ct))
        {
            var m = p.Message;
            var owner = (m.Scope, m.Scope == NotificationScope.System ? (Guid?)null : m.CustomerId);
            if (!channels.TryGetValue(owner, out var list)) channels[owner] = list = await store.ChannelsAsync(owner.Item1, owner.Item2, ct);
            var channel = list.FirstOrDefault(c => c.Channel == m.Channel);
            if (channel is not { IsEnabled: true }) { await store.MarkFailedAsync(p.Id, "Kênh đã tắt hoặc bị xóa", null, ct); continue; }
            var (result, _) = await Dispatch.SendAsync(store, protector, transport, channel, m, ct);
            if (result.Ok) { await store.MarkSentAsync(p.Id, ct); sent++; continue; }
            var attempt = p.Attempts + 1;
            logger.LogWarning("NOTIFICATION_SEND_FAILED id={Id} channel={Channel} scope={Scope} attempt={Attempt} error={Error}", p.Id, m.Channel, m.Scope, attempt, result.Error);
            await store.MarkFailedAsync(p.Id, result.Error ?? "Lỗi không xác định", attempt >= MaxAttempts ? null : clock.GetUtcNow().UtcDateTime.AddMinutes(Math.Pow(2, attempt)), ct);
            if (result.Secrets is not null) channels.Remove(owner);   // tokens changed: reload before the next message
        }
        return sent;
    }
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NOTIFICATION_WORKER_STARTED interval={Interval}", Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();
                await dispatcher.PlanAsync(ct: stoppingToken);
                await dispatcher.SendDueAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "NOTIFICATION_WORKER_ERROR"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}

public static class NotificationServiceCollectionExtensions
{
    /// <summary>Settings, store and transport for a site; <paramref name="worker"/> also plans and sends (the webhook site only).</summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services, bool worker = false)
    {
        services.AddHttpClient(NotificationTransport.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20)).RemoveAllLoggers();   // URLs carry the Telegram bot token
        services.TryAddSingleton(TimeProvider.System);
        if (OperatingSystem.IsWindows()) services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddScoped<INotificationStore, SqlNotificationStore>();
        services.AddScoped<INotificationTransport, NotificationTransport>();
        services.AddScoped<NotificationSettings>();
        if (worker)
        {
            services.AddScoped<NotificationDispatcher>();
            services.AddHostedService<NotificationWorker>();
        }
        return services;
    }
}
