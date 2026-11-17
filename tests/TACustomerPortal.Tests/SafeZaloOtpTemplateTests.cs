using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Notifications;
using TAPortal.Integrations.ZaloMiniApp;
using static TACustomerPortal.Tests.SafeNotificationTests;

namespace TACustomerPortal.Tests;

// Operator 2026-11-17: the account OTP goes through T.A's SYSTEM Zalo OA channel with a ZNS "Template xác thực" whose id is
// set in Admin (Cài đặt thông báo → Zalo OA → Mã mẫu ZNS OTP); ZaloMiniApp:OtpTemplateId stays as the fallback. The channel
// can be switched on for the OTP alone, without the payment template. No SQL, no Zalo.
public sealed class SafeZaloOtpTemplateTests
{
    private static readonly Guid Actor = Guid.Parse("c0000000-0000-0000-0000-00000000000c");
    private static readonly Dictionary<string, string> None = [];

    private static Dictionary<string, string?> Zalo(string? otp = null, string? tpl = null, string? data = null) => new()
    {
        ["appId"] = "1234567890", ["appSecret"] = "secret", ["refreshToken"] = "refresh",
        ["otpTemplateId"] = otp, ["templateId"] = tpl, ["templateData"] = data
    };

    private static Dictionary<string, string> Settings(Dictionary<string, string?> form) =>
        form.Where(p => p.Value is not null && p.Key is not ("appSecret" or "refreshToken")).ToDictionary(p => p.Key, p => p.Value!);

    private static readonly Dictionary<string, string> Secrets = new() { ["appSecret"] = "secret", ["refreshToken"] = "refresh" };

    [Fact]
    public void SystemZaloChannelHasTheOtpTemplateAndAnOptionalPaymentTemplate()
    {
        var system = NotificationCatalog.Fields(NotificationChannelKind.ZaloOa, NotificationScope.System);
        var otp = Assert.Single(system, f => f.Key == "otpTemplateId");
        Assert.False(otp.Secret);
        Assert.Contains("Template xác thực", otp.Help);
        Assert.All(system.Where(f => f.Key is "templateId" or "templateData"), f => Assert.False(f.Required));

        // A company's own Zalo OA only notifies payments: no OTP field, payment template still required.
        var company = NotificationCatalog.Fields(NotificationChannelKind.ZaloOa, NotificationScope.Customer);
        Assert.DoesNotContain(company, f => f.Key == "otpTemplateId");
        Assert.All(company.Where(f => f.Key is "templateId" or "templateData"), f => Assert.True(f.Required));
        Assert.Equal(company.Select(f => f.Key), NotificationCatalog.Fields(NotificationChannelKind.ZaloOa).Select(f => f.Key));
    }

    [Fact]
    public void SystemZaloChannelIsValidatedForOtpOrPayment()
    {
        string[] Errors(Dictionary<string, string?> form, bool enabled = true, string scope = NotificationScope.System) =>
            [.. NotificationCatalog.Validate(NotificationChannelKind.ZaloOa, Settings(form), Secrets, enabled, scope)];

        Assert.Empty(Errors(Zalo(otp: "31239")));                                              // OTP only
        Assert.Empty(Errors(Zalo(tpl: "445566", data: "{\"order_code\":\"{order_no}\"}")));       // payment only, as before
        Assert.Empty(Errors(Zalo(otp: "7895417a7d3f9461cd2e", tpl: "445566", data: "{\"a\":\"{amount}\"}")));
        Assert.Contains("Vui lòng nhập mã mẫu ZNS OTP hoặc mã mẫu báo thanh toán.", Errors(Zalo()));
        Assert.Empty(Errors(Zalo(), enabled: false));
        Assert.Contains("Mẫu báo thanh toán cần cả mã mẫu và tham số mẫu (JSON).", Errors(Zalo(otp: "31239", tpl: "445566")));
        Assert.Contains("Mã mẫu ZNS OTP chỉ gồm chữ và số.", Errors(Zalo(otp: "31 239")));
        Assert.Contains("Mã mẫu ZNS OTP chỉ gồm chữ và số.", Errors(Zalo(otp: "../x")));

        // A company's Zalo OA still needs its payment template.
        Assert.NotEmpty(Errors(Zalo(otp: "31239"), scope: NotificationScope.Customer));
        Assert.Empty(Errors(Zalo(tpl: "445566", data: "{\"a\":\"{amount}\"}"), scope: NotificationScope.Customer));
    }

    [Fact]
    public async Task AdminSavesTheOtpTemplateAndItIsReadOnlyWhileTheChannelIsOn()
    {
        var store = new MemoryNotificationStore();
        var settings = new NotificationSettings(store, new PlainProtector(), new FakeTransport(), TimeProvider.System);
        Assert.Null(await settings.SystemOtpTemplateIdAsync());

        Assert.Empty(await settings.SaveAsync(NotificationScope.System, null, NotificationChannelKind.ZaloOa, true, [], Zalo(otp: " 31239 "), [], Actor));
        Assert.Equal("31239", await settings.SystemOtpTemplateIdAsync());
        var view = (await settings.ChannelsAsync(NotificationScope.System, null)).Single(c => c.Channel == NotificationChannelKind.ZaloOa);
        Assert.Contains(view.Fields, f => f.Key == "otpTemplateId");
        Assert.Equal("31239", view.Settings["otpTemplateId"]);

        Assert.Empty(await settings.SaveAsync(NotificationScope.System, null, NotificationChannelKind.ZaloOa, false, [], Zalo(otp: "31239"), [], Actor));
        Assert.Null(await settings.SystemOtpTemplateIdAsync());

        // A company channel never carries the OTP template (not a field there, so not saved).
        var company = Guid.NewGuid();
        Assert.Empty(await settings.SaveAsync(NotificationScope.Customer, company, NotificationChannelKind.ZaloOa, true, [],
            Zalo(otp: "999", tpl: "445566", data: "{\"a\":\"{amount}\"}"), [], Actor));
        Assert.DoesNotContain("otpTemplateId", (await store.ChannelsAsync(NotificationScope.Customer, company)).Single().Settings.Keys);
    }

    [Fact]
    public async Task AdminTestSendsTheOtpTemplateWithASixDigitCode()
    {
        var store = new MemoryNotificationStore();
        var transport = new FakeTransport();
        var settings = new NotificationSettings(store, new PlainProtector(), transport, TimeProvider.System);

        Assert.Equal("Chưa có mã mẫu ZNS OTP.", await settings.TestOtpAsync("0912345678", null, "otp"));
        Assert.StartsWith("Vui lòng nhập số điện thoại", await settings.TestOtpAsync("12345", "31239", "otp"));
        Assert.Equal("Gửi thử OTP thất bại: Chưa bật kênh Zalo OA hệ thống.", await settings.TestOtpAsync("0912345678", "31239", "otp"));

        Assert.Empty(await settings.SaveAsync(NotificationScope.System, null, NotificationChannelKind.ZaloOa, true, [], Zalo(otp: "31239"), [], Actor));
        var result = await settings.TestOtpAsync("0912 345 678", "31239", "otp");
        Assert.StartsWith("Gửi thử OTP thành công tới 0912 345 678", result);
        var sent = Assert.Single(transport.Sent);
        Assert.Equal(("84912345678", NotificationScope.System, NotificationChannelKind.ZaloOa), (sent.Recipient, sent.Scope, sent.Channel));
        var code = Assert.Single(sent.TemplateData!);
        Assert.Equal("otp", code.Key);
        Assert.Matches("^[0-9]{6}$", code.Value);
        Assert.Contains(code.Value, result);
    }

    [Fact]
    public async Task OtpTemplateComesFromAdminThenFromConfiguration()
    {
        var store = new MemoryNotificationStore();
        var settings = new NotificationSettings(store, new PlainProtector(), new FakeTransport(), TimeProvider.System);
        IOtpTemplateSource Source(string? configured) =>
            new SystemOtpTemplate(settings, Options.Create(new ZaloMiniAppOptions { OtpTemplateId = configured }), NullLogger<SystemOtpTemplate>.Instance);

        Assert.Null(await Source(null).TemplateIdAsync());
        Assert.Null(await Source("  ").TemplateIdAsync());
        Assert.Equal("445566", await Source(" 445566 ").TemplateIdAsync());

        Assert.Empty(await settings.SaveAsync(NotificationScope.System, null, NotificationChannelKind.ZaloOa, true, [], Zalo(otp: "31239"), [], Actor));
        Assert.Equal("31239", await Source("445566").TemplateIdAsync());
        Assert.Equal("31239", await Source(null).TemplateIdAsync());

        // The store being down never hides the configured fallback.
        var broken = new NotificationSettings(new BrokenStore(), new PlainProtector(), new FakeTransport(), TimeProvider.System);
        var fallback = new SystemOtpTemplate(broken, Options.Create(new ZaloMiniAppOptions { OtpTemplateId = "445566" }), NullLogger<SystemOtpTemplate>.Instance);
        Assert.Equal("445566", await fallback.TemplateIdAsync());
    }

    [Fact]
    public async Task PhoneVerificationSendsTheTemplateTheSourceGives()
    {
        var store = new SafeZaloMiniAppTests.Store();
        var user = Guid.NewGuid();
        store.Accounts[user] = new(user, "ann", "Chị An", "0912345678", "hash", false);
        var sender = new SafeZaloMiniAppTests.Sender();
        var options = Options.Create(new ZaloMiniAppOptions { OtpTemplateId = null });
        var source = new FixedTemplate();
        var otp = new PhoneVerification(store, sender, source, options, TimeProvider.System);
        var account = new ZaloAccountService(store, otp, source, options, TimeProvider.System);

        Assert.False((await account.PanelAsync(user))!.OtpAvailable);
        var refused = await otp.RequestAsync(user, null);
        Assert.False(refused.Ok);
        Assert.Empty(sender.Sent);

        source.Id = "31239";
        Assert.True((await account.PanelAsync(user))!.OtpAvailable);
        var sent = await otp.RequestAsync(user, null);
        Assert.True(sent.Ok, sent.Error);
        var (phone, template, data) = Assert.Single(sender.Sent);
        Assert.Equal(("84912345678", "31239"), (phone, template));
        Assert.Matches("^[0-9]{6}$", data["otp"]);
    }

    [Fact]
    public void BothSitesUseTheAdminTemplateAndAdminCanTestIt()
    {
        var root = RepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src", "TAPortal.Web", "Views", "NotificationSettings", "Index.cshtml"));
        Assert.Contains("data-ns-test-otp", view);
        Assert.Contains("value=\"otp\"", view);
        var zalo = File.ReadAllText(Path.Combine(root, "src", "TAPortal.Web", "Views", "Account", "Zalo.cshtml"));
        Assert.Contains("Mã mẫu ZNS OTP", zalo);
        var registration = File.ReadAllText(Path.Combine(root, "src", "TAPortal.Integrations", "ZaloMiniApp", "PhoneVerification.cs"));
        Assert.Contains("TryAddScoped<IOtpTemplateSource, SystemOtpTemplate>", registration);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }

    private sealed class FixedTemplate : IOtpTemplateSource
    {
        public string? Id;
        public Task<string?> TemplateIdAsync(CancellationToken ct = default) => Task.FromResult(Id);
    }

    private sealed class BrokenStore : INotificationStore
    {
        private static Exception Down() => new InvalidOperationException("database down");
        public Task<IReadOnlyList<NotificationChannel>> ChannelsAsync(string scope, Guid? customerId, CancellationToken ct = default) => throw Down();
        public Task<(byte[]? Secret, int Version)?> SecretAsync(string scope, Guid? customerId, string channel, CancellationToken ct = default) => throw Down();
        public Task SaveChannelAsync(string scope, Guid? customerId, string channel, bool enabled, string events, IReadOnlyDictionary<string, string> settings, byte[]? secret, Guid actor, CancellationToken ct = default) => throw Down();
        public Task<bool> ReplaceSecretAsync(string scope, Guid? customerId, string channel, int expectedVersion, byte[] secret, CancellationToken ct = default) => throw Down();
        public Task RecordTestAsync(string scope, Guid? customerId, string channel, string result, CancellationToken ct = default) => throw Down();
        public Task<NotificationRecipients> RecipientsAsync(Guid customerId, CancellationToken ct = default) => throw Down();
        public Task SaveRecipientsAsync(Guid customerId, NotificationRecipients recipients, Guid actor, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<NotifiableTransaction>> UnnotifiedAsync(DateTime sinceUtc, int max, CancellationToken ct = default) => throw Down();
        public Task EnqueueAsync(long transactionId, IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<PendingMessage>> DueAsync(int max, CancellationToken ct = default) => throw Down();
        public Task MarkSentAsync(long id, CancellationToken ct = default) => throw Down();
        public Task MarkFailedAsync(long id, string error, DateTime? retryAtUtc, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<OutboxEntry>> RecentAsync(string? scope, Guid? customerId, int max, CancellationToken ct = default) => throw Down();
    }
}
