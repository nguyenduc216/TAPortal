using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Integrations.Notifications;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Notification channels (operator decision 2026-11-05): T.A's SYSTEM channels notify companies and T.A staff; a company's
// own CUSTOMER channels notify its buyers and its staff group. No secret ever goes back to a page; no company channel is
// ever used for another company.
public sealed class SafeNotificationTests
{
    private static readonly Guid A = Guid.Parse("a0000000-0000-0000-0000-00000000000a"), B = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid Actor = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private static NotifiableTransaction Paid(Guid? customer = null, string status = PaymentTxStatus.Matched, string? flags = null) =>
        new(7, status, flags, "Xử lý tự động thất bại: TingeeApiException", 100_000, "DH2611050001", "12345678", DateTime.UtcNow, customer ?? A, "Công ty A",
            Guid.NewGuid(), "DH261105-0001", 300_000, 100_000, TingeeOrderStatus.Partial, "Anh Ba", "ba@example.com", "0912345678");

    private static NotificationChannel Channel(string scope, Guid? customer, string kind, Dictionary<string, string>? settings = null, params string[] events) =>
        new(Guid.NewGuid(), scope, customer, kind, true, (events.Length == 0 ? [NotificationEvent.Payment, NotificationEvent.Attention] : events).ToHashSet(),
            settings ?? [], new HashSet<string>(), 0, null, null);

    [Fact]
    public void ThePlannerSendsEachAudienceOnlyWhatItShouldGet()
    {
        var system = new[]
        {
            Channel(NotificationScope.System, null, NotificationChannelKind.Smtp),
            Channel(NotificationScope.System, null, NotificationChannelKind.TelegramGroup, new() { ["chatId"] = "-100111" }, NotificationEvent.Attention),
        };
        var companyA = new[]
        {
            Channel(NotificationScope.Customer, A, NotificationChannelKind.Smtp),
            Channel(NotificationScope.Customer, A, NotificationChannelKind.TelegramGroup, new() { ["chatId"] = "-100222" }),
            Channel(NotificationScope.Customer, A, NotificationChannelKind.ZaloOa, new() { ["templateData"] = """{"order_code":"{order_no}","money":"{amount}"}""" }),
        };
        var recipients = new NotificationRecipients("ketoan@a.vn", null, null, new HashSet<string> { NotificationEvent.Payment });

        var paid = NotificationPlanner.Build(Paid(), system, companyA, recipients);
        // T.A staff group only listens to ATTENTION; the company's group, its buyer (email, Zalo) and the company itself get the payment.
        Assert.Equal(new[] { "-100222", "84912345678", "ba@example.com", "ketoan@a.vn" }, paid.Select(m => m.Recipient).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new Dictionary<string, string> { ["order_code"] = "DH261105-0001", ["money"] = "100.000 ₫" }, paid.Single(m => m.Channel == NotificationChannelKind.ZaloOa).TemplateData);
        Assert.Equal(paid.Count, paid.Select(m => m.EventKey).Distinct().Count());

        // Another company's channels are never used for A's money.
        var companyB = new[] { Channel(NotificationScope.Customer, B, NotificationChannelKind.Smtp) };
        Assert.DoesNotContain(NotificationPlanner.Build(Paid(), [], companyB, recipients), m => m.Scope == NotificationScope.Customer);

        // Unmatched money: T.A staff see the raw reason; the company sees a fixed text; buyers hear nothing.
        var unmatched = Paid() with { Status = PaymentTxStatus.Unmatched, OrderId = null, OrderNo = null };
        var attention = NotificationPlanner.Build(unmatched, system, companyA, recipients with { Events = new HashSet<string> { NotificationEvent.Attention } });
        Assert.Contains("TingeeApiException", attention.Single(m => m.Recipient == "-100111").Body);
        Assert.All(attention.Where(m => m.Recipient != "-100111"), m => Assert.DoesNotContain("Tingee", m.Body));
        Assert.DoesNotContain(attention, m => m.Recipient == "ba@example.com");
    }

    [Fact]
    public void ChannelInputIsValidated()
    {
        var none = new Dictionary<string, string>();
        Assert.NotEmpty(NotificationCatalog.Validate(NotificationChannelKind.Smtp, new Dictionary<string, string> { ["host"] = "smtp.example.com", ["port"] = "465", ["fromAddress"] = "a@b.vn" }, none, true));
        Assert.Empty(NotificationCatalog.Validate(NotificationChannelKind.Smtp, new Dictionary<string, string> { ["host"] = "smtp.example.com", ["port"] = "587", ["fromAddress"] = "a@b.vn" }, none, true));
        Assert.NotEmpty(NotificationCatalog.Validate(NotificationChannelKind.Smtp, new Dictionary<string, string> { ["host"] = "10.0.0.5", ["port"] = "587", ["fromAddress"] = "a@b.vn" }, none, true));
        Assert.NotEmpty(NotificationCatalog.Validate(NotificationChannelKind.TelegramGroup, new Dictionary<string, string> { ["chatId"] = "-100123" }, new Dictionary<string, string> { ["botToken"] = "../../evil" }, true));
        Assert.NotEmpty(NotificationCatalog.Validate(NotificationChannelKind.ZaloOa, new Dictionary<string, string> { ["templateData"] = "[1]" }, none, false));
        Assert.Equal("84912345678", NotificationCatalog.ZaloPhone("0912 345 678"));
        Assert.Null(NotificationCatalog.ZaloPhone("12345"));
        Assert.False(PublicHostGuard.IsPublic(IPAddress.Parse("127.0.0.1")));
        Assert.False(PublicHostGuard.IsPublic(IPAddress.Parse("192.168.1.10")));
        Assert.False(PublicHostGuard.IsPublic(IPAddress.Parse("169.254.169.254")));
        Assert.False(PublicHostGuard.IsPublic(IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.True(PublicHostGuard.IsPublic(IPAddress.Parse("142.250.1.1")));
    }

    [Fact]
    public async Task SecretsAreKeptEncryptedAndNeverShownBack()
    {
        var store = new MemoryNotificationStore();
        var settings = new NotificationSettings(store, new PlainProtector(), new FakeTransport(), TimeProvider.System);
        var token = "123456:" + new string('A', 35);
        Assert.Empty(await settings.SaveAsync(NotificationScope.Customer, A, NotificationChannelKind.TelegramGroup, true, [NotificationEvent.Payment],
            new Dictionary<string, string?> { ["botToken"] = token, ["chatId"] = "-100222" }, [], Actor));
        var view = (await settings.ChannelsAsync(NotificationScope.Customer, A)).Single(c => c.Channel == NotificationChannelKind.TelegramGroup);
        Assert.Contains("botToken", view.SecretKeys);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(view));
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(store.Rows.Single().Value.Settings));

        // A blank secret keeps the saved one; "clear" removes it.
        Assert.Empty(await settings.SaveAsync(NotificationScope.Customer, A, NotificationChannelKind.TelegramGroup, false, [], new Dictionary<string, string?> { ["chatId"] = "-100333" }, [], Actor));
        Assert.Equal(token, new PlainProtector().Unprotect(store.Rows.Single().Value.Secret)["botToken"]);
        Assert.NotEmpty(await settings.SaveAsync(NotificationScope.Customer, A, NotificationChannelKind.TelegramGroup, true, [], new Dictionary<string, string?> { ["chatId"] = "-100333" }, ["botToken"], Actor));

        // Company B sees nothing of A's channel; a company cannot configure T.A's private Telegram bot.
        Assert.All(await settings.ChannelsAsync(NotificationScope.Customer, B), c => Assert.False(c.IsEnabled));
        Assert.NotEmpty(await settings.SaveAsync(NotificationScope.Customer, A, NotificationChannelKind.TelegramBot, true, [], new Dictionary<string, string?> { ["botToken"] = token }, [], Actor));
        Assert.DoesNotContain(await settings.ChannelsAsync(NotificationScope.Customer, A), c => c.Channel == NotificationChannelKind.TelegramBot);
        await Assert.ThrowsAsync<ArgumentException>(() => settings.ChannelsAsync(NotificationScope.Customer, null));
    }

    [Fact]
    public async Task TheDispatcherSendsRetriesAndSavesRefreshedTokens()
    {
        var store = new MemoryNotificationStore();
        var transport = new FakeTransport();
        var protector = new PlainProtector();
        var settings = new NotificationSettings(store, protector, transport, TimeProvider.System) { PublicHost = (_, _) => Task.FromResult(true) };
        Assert.Empty(await settings.SaveAsync(NotificationScope.Customer, A, NotificationChannelKind.Smtp, true, [NotificationEvent.Payment],
            new Dictionary<string, string?> { ["host"] = "smtp.example.com", ["port"] = "587", ["fromAddress"] = "no-reply@a.vn" }, [], Actor));
        store.Transactions.Add(Paid());
        var dispatcher = new NotificationDispatcher(store, protector, transport, TimeProvider.System, NullLogger<NotificationDispatcher>.Instance);

        Assert.Equal(1, await dispatcher.PlanAsync());
        Assert.Equal(0, await dispatcher.PlanAsync());                    // planned once
        transport.Fail = true;
        Assert.Equal(0, await dispatcher.SendDueAsync());
        Assert.Equal(("PENDING", 1), (store.Outbox.Single().Status, store.Outbox.Single().Attempts));
        store.Outbox[0] = store.Outbox[0] with { Next = DateTime.UtcNow.AddMinutes(-1) };
        transport.Fail = false;
        transport.Refresh = new() { ["password"] = "rotated" };
        Assert.Equal(1, await dispatcher.SendDueAsync());
        Assert.Equal("SENT", store.Outbox.Single().Status);
        Assert.Equal("ba@example.com", transport.Sent.Single().Recipient);
        Assert.Equal("rotated", protector.Unprotect(store.Rows.Single().Value.Secret)["password"]);
    }

    // ---- fakes (also used by the portal page tests) -------------------------------------------------------
    public sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(IReadOnlyDictionary<string, string> secrets) => JsonSerializer.SerializeToUtf8Bytes(secrets);
        public Dictionary<string, string> Unprotect(byte[]? data) => data is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(data)!;
    }

    public sealed class FakeTransport : INotificationTransport
    {
        public bool Fail { get; set; }
        public Dictionary<string, string>? Refresh { get; set; }
        public List<OutboxMessage> Sent { get; } = [];
        public Task<SendResult> SendAsync(NotificationChannel channel, IReadOnlyDictionary<string, string> secrets, OutboxMessage message, CancellationToken ct)
        {
            if (Fail) return Task.FromResult(new SendResult(false, "down"));
            Sent.Add(message);
            return Task.FromResult(new SendResult(true, null, Refresh));
        }
    }

    public sealed class MemoryNotificationStore : INotificationStore
    {
        public sealed record Row(bool Enabled, string Events, byte[] Settings, byte[]? Secret, int Version);
        public sealed record Out(long Id, OutboxMessage Message, string Status, int Attempts, DateTime Next);
        public Dictionary<(string, Guid?, string), Row> Rows { get; } = [];
        public Dictionary<Guid, NotificationRecipients> Recipients { get; } = [];
        public List<NotifiableTransaction> Transactions { get; } = [];
        public HashSet<long> Notified { get; } = [];
        public List<Out> Outbox { get; } = [];

        public Task<IReadOnlyList<NotificationChannel>> ChannelsAsync(string scope, Guid? customerId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationChannel>>(Rows.Where(r => r.Key.Item1 == scope && r.Key.Item2 == customerId).Select(r => new NotificationChannel(Guid.NewGuid(), scope, customerId, r.Key.Item3,
                r.Value.Enabled, NotificationEvent.Parse(r.Value.Events), JsonSerializer.Deserialize<Dictionary<string, string>>(r.Value.Settings)!, new HashSet<string>(), r.Value.Version, null, null)).ToList());
        public Task<(byte[]? Secret, int Version)?> SecretAsync(string scope, Guid? customerId, string channel, CancellationToken ct = default) =>
            Task.FromResult<(byte[]?, int)?>(Rows.TryGetValue((scope, customerId, channel), out var r) ? (r.Secret, r.Version) : null);
        public Task SaveChannelAsync(string scope, Guid? customerId, string channel, bool enabled, string events, IReadOnlyDictionary<string, string> settings, byte[]? secret, Guid actor, CancellationToken ct = default)
        {
            var v = Rows.TryGetValue((scope, customerId, channel), out var old) ? old.Version + 1 : 0;
            Rows[(scope, customerId, channel)] = new(enabled, events, JsonSerializer.SerializeToUtf8Bytes(settings), secret, v);
            return Task.CompletedTask;
        }
        public Task<bool> ReplaceSecretAsync(string scope, Guid? customerId, string channel, int expectedVersion, byte[] secret, CancellationToken ct = default)
        {
            if (!Rows.TryGetValue((scope, customerId, channel), out var r) || r.Version != expectedVersion) return Task.FromResult(false);
            Rows[(scope, customerId, channel)] = r with { Secret = secret, Version = r.Version + 1 };
            return Task.FromResult(true);
        }
        public Task RecordTestAsync(string scope, Guid? customerId, string channel, string result, CancellationToken ct = default) => Task.CompletedTask;
        public Task<NotificationRecipients> RecipientsAsync(Guid customerId, CancellationToken ct = default) => Task.FromResult(Recipients.GetValueOrDefault(customerId, NotificationRecipients.Empty));
        public Task SaveRecipientsAsync(Guid customerId, NotificationRecipients recipients, Guid actor, CancellationToken ct = default) { Recipients[customerId] = recipients; return Task.CompletedTask; }
        public Task<IReadOnlyList<NotifiableTransaction>> UnnotifiedAsync(DateTime sinceUtc, int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotifiableTransaction>>(Transactions.Where(t => !Notified.Contains(t.Id)).Take(max).ToList());
        public Task EnqueueAsync(long transactionId, IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
        {
            foreach (var m in messages.Where(m => Outbox.All(o => o.Message.EventKey != m.EventKey))) Outbox.Add(new(Outbox.Count + 1, m, "PENDING", 0, DateTime.UtcNow.AddSeconds(-1)));
            Notified.Add(transactionId);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<PendingMessage>> DueAsync(int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingMessage>>(Outbox.Where(o => o.Status == "PENDING" && o.Next <= DateTime.UtcNow).Take(max).Select(o => new PendingMessage(o.Id, o.Message, o.Attempts)).ToList());
        public Task MarkSentAsync(long id, CancellationToken ct = default) { Update(id, o => o with { Status = "SENT", Attempts = o.Attempts + 1 }); return Task.CompletedTask; }
        public Task MarkFailedAsync(long id, string error, DateTime? retryAtUtc, CancellationToken ct = default)
        {
            Update(id, o => o with { Status = retryAtUtc is null ? "FAILED" : "PENDING", Attempts = o.Attempts + 1, Next = retryAtUtc ?? o.Next });
            return Task.CompletedTask;
        }
        private void Update(long id, Func<Out, Out> f) { var i = Outbox.FindIndex(o => o.Id == id); Outbox[i] = f(Outbox[i]); }
        public Task<IReadOnlyList<OutboxEntry>> RecentAsync(string? scope, Guid? customerId, int max, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboxEntry>>(Outbox.Where(o => (scope is null || o.Message.Scope == scope) && (customerId is null || o.Message.CustomerId == customerId))
                .Select(o => new OutboxEntry(o.Id, o.Message.Scope, o.Message.CustomerId, o.Message.Channel, o.Message.Recipient, o.Message.Subject, o.Status, o.Attempts, null, DateTime.UtcNow, null)).ToList());
    }
}
