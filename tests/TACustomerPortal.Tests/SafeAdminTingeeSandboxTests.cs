extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Admin → Tingee Sandbox: T.A tests its own Tingee Merchant account (Direct mode). No SQL, no network: a recording
// Tingee fake and an in-memory webhook inbox shared by the /hooks/tingee receivers and the sandbox list.
public sealed class SafeAdminTingeeSandboxTests
{
    private const string TestScheme = "SafeTingeeSandbox";
    private const string Secret = "sandbox-test-secret";

    [Fact]
    public async Task UnconfiguredSandboxExplainsTheSettingsAndCallsNothing()
    {
        using var f = Factory(out var tingee, out _, configured: false);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/tingeesandbox"));
        Assert.Contains("data-ta-sandbox-unconfigured", html);
        Assert.Contains("data-ta-sandbox-howto", html);
        Assert.Contains("https://localhost/hooks/tingee/payment", html);
        Assert.Contains("\"Mode\": \"Direct\"", html);
        Assert.Empty(tingee.Calls);
    }

    [Fact]
    public async Task ConfiguredSandboxTestsTheConnectionAndListsLinkedAccounts()
    {
        using var f = Factory(out var tingee, out _);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/tingeesandbox"));
        Assert.Contains("data-ta-sandbox-production", html);
        Assert.Contains("data-ta-sandbox-conn-ok", html);
        Assert.Contains("#9001", html);
        Assert.Contains("963123456789", html);
        Assert.Contains("value=\"970422|963123456789|personal-account\"", html);   // offered as a QR target
        Assert.Equal(["get-va-paging"], tingee.Calls);
        Assert.Contains("href=\"/TingeeSandbox\"", html.Replace("href=\"/tingeesandbox\"", "href=\"/TingeeSandbox\""));
    }

    [Fact]
    public async Task ConnectionErrorsAreShownWithTheTingeeCode()
    {
        using var f = Factory(out var tingee, out _);
        tingee.ListError = new TingeeApiException("401", "Invalid signature");
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/tingeesandbox"));
        Assert.Contains("data-ta-sandbox-conn-failed", html);
        Assert.Contains("Tingee trả mã 401: Invalid signature", html);
    }

    [Fact]
    public async Task MasterModeIsRefused()
    {
        using var f = Factory(out var tingee, out _, mode: "Master");
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/tingeesandbox"));
        Assert.Contains("data-ta-sandbox-master", html);
        var r = await Post(c, "/tingeesandbox/createqr", await Token(c), new() { ["Qr.Account"] = "970422|963123456789|personal-account", ["Qr.Amount"] = "2000" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Tingee__Mode=Direct", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(tingee.Qrs);
    }

    [Fact]
    public async Task DynamicQrIsCreatedAndItsPaymentStatusIsPolled()
    {
        using var f = Factory(out var tingee, out _);
        var c = Client(f);
        var r = await Post(c, "/tingeesandbox/createqr", await Token(c),
            new() { ["Qr.Account"] = "970422|963123456789|personal-account", ["Qr.Amount"] = "2000", ["Qr.Purpose"] = "TA TEST", ["Qr.Type"] = "dynamic" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("data-ta-sandbox-qr-result", html);
        Assert.Contains("<svg", html);
        Assert.Contains("/TingeeSandbox/QrStatus?qrAccount=QRACC&billId=BILL1", html);
        var q = Assert.Single(tingee.Qrs);
        Assert.Equal(("970422", "963123456789", 2000m, TingeeQrTypes.OneTime, "TA TEST", 15), (q.BankBin, q.VaAccountNumber, q.Amount, q.QrCodeType, q.Purpose, q.ExpireInMinute));

        tingee.Paid = 2000;
        var status = JsonDocument.Parse(await c.GetStringAsync("/tingeesandbox/qrstatus?qrAccount=QRACC&billId=BILL1")).RootElement;
        Assert.True(status.GetProperty("ok").GetBoolean());
        Assert.Equal(2000m, status.GetProperty("paid").GetDecimal());
        Assert.Equal("paid", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task StaticQrAndInvalidInputs()
    {
        using var f = Factory(out var tingee, out _);
        var c = Client(f);
        var token = await Token(c);
        var r = await Post(c, "/tingeesandbox/createqr", token, new() { ["Qr.Account"] = "970422|963123456789|personal-account", ["Qr.Amount"] = "5000", ["Qr.Type"] = "static" });
        Assert.Contains("QR tĩnh VietQR", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Equal(("970422", "963123456789", 5000m), Assert.Single(tingee.StaticQrs));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, "/tingeesandbox/createqr", token, new() { ["Qr.Account"] = "970422|963123456789|personal-account", ["Qr.Amount"] = "100" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, "/tingeesandbox/createqr", token, new() { ["Qr.Account"] = "999999|1|x", ["Qr.Amount"] = "2000" })).StatusCode);
        Assert.Empty(tingee.Qrs);
    }

    [Fact]
    public async Task BankLinkAsksForTheOtpAndConfirmsIt()
    {
        using var f = Factory(out var tingee, out _);
        var c = Client(f);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync("/tingeesandbox?bin=970422&type=personal-account"));
        Assert.Contains("name=\"Bank.AccountNumber\"", form);
        Assert.Contains("name=\"Bank.Mobile\"", form);
        var token = await Token(c);
        var missing = await Post(c, "/tingeesandbox/linkbank", token, new() { ["Bank.Bin"] = "970422", ["Bank.AccountType"] = "personal-account" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Empty(tingee.Vas);

        var r = await Post(c, "/tingeesandbox/linkbank", token, new()
        {
            ["Bank.Bin"] = "970422", ["Bank.AccountType"] = "personal-account", ["Bank.AccountNumber"] = "0123456789",
            ["Bank.AccountName"] = "CONG TY TA", ["Bank.Identity"] = "001234567890", ["Bank.Mobile"] = "0912345678", ["Bank.VaSuffix"] = "IGNORED"
        });
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("data-ta-sandbox-otp", html);
        var va = Assert.Single(tingee.Vas);
        Assert.Equal(("0123456789", "0912345678", (string?)null), (va.AccountNumber, va.Mobile, va.VaSuffix));   // only fields the bank requires

        var bad = await Post(c, "/tingeesandbox/confirmotp", token, new() { ["bankBin"] = "970422", ["confirmId"] = "CONF-1", ["otp"] = "12" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await Post(c, "/tingeesandbox/confirmotp", token, new() { ["bankBin"] = "970422", ["confirmId"] = "CONF-1", ["otp"] = "123456" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(("970422", "CONF-1", "123456"), Assert.Single(tingee.Confirms));
    }

    [Fact]
    public async Task BankLinkNeedsTheMerchantIdOfTA()
    {
        using var f = Factory(out var tingee, out _, merchantId: null);
        var c = Client(f);
        Assert.Contains("data-ta-sandbox-no-merchant", WebUtility.HtmlDecode(await c.GetStringAsync("/tingeesandbox")));
        var r = await Post(c, "/tingeesandbox/linkbank", await Token(c), new()
        {
            ["Bank.Bin"] = "970422", ["Bank.AccountType"] = "personal-account", ["Bank.AccountNumber"] = "0123456789",
            ["Bank.AccountName"] = "CONG TY TA", ["Bank.Identity"] = "001234567890", ["Bank.Mobile"] = "0912345678"
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("1096", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(tingee.Vas);
    }

    [Fact]
    public async Task NonAdminsCannotOpenTheSandbox()
    {
        using var f = Factory(out var tingee, out _, admin: false);
        var r = await Client(f).GetAsync("/tingeesandbox");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(tingee.Calls);
    }

    [Fact]
    public async Task AdminSiteReceivesSignedWebhooksAndListsThem()
    {
        using var f = Factory(out _, out var inbox);
        var c = Client(f);
        var body = "{\"transactionCode\":\"FT26307ABC\",\"amount\":2000,\"content\":\"TA SANDBOX\",\"vaAccountNumber\":\"963123456789\",\"bankName\":\"MB\"}";
        var unsigned = await c.PostAsync("/hooks/tingee/payment", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Empty(inbox.Saved);

        var signed = await c.SendAsync(Signed("/hooks/tingee/payment", body));
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Single(inbox.Saved);

        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/tingeesandbox/webhooks"));
        Assert.Contains("FT26307ABC", list);
        Assert.Contains("2.000đ", list);
        Assert.Contains("TA SANDBOX", list);
    }

    [Fact]
    public async Task WebhooksAreRefusedWhenTingeeIsNotConfigured()
    {
        using var f = Factory(out _, out var inbox, configured: false);
        var r = await Client(f).PostAsync("/hooks/tingee/payment", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Empty(inbox.Saved);
    }

    [Fact]
    public void WebhookParserReadsCommonFieldsAndSurvivesGarbage()
    {
        var w = AdminServices.TingeeSandboxWebhookParser.Parse(1, "Payment", "RECEIVED", DateTime.UtcNow, "{\"transferAmount\":\"15000\",\"description\":\"CK TA\",\"accountNumber\":\"123\",\"transactionId\":\"T9\"}");
        Assert.Equal((15000m, "CK TA", "123", "T9"), (w.Amount, w.Content, w.Account, w.TransactionCode));
        var junk = AdminServices.TingeeSandboxWebhookParser.Parse(2, "BankLink", "RECEIVED", DateTime.UtcNow, "not json");
        Assert.Equal(((decimal?)null, "not json"), (junk.Amount, junk.RawBody));
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static HttpRequestMessage Signed(string path, string body)
    {
        var ts = TingeeSignature.Timestamp(DateTimeOffset.UtcNow);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-request-timestamp", ts);
        request.Headers.Add("x-signature", TingeeSignature.Sign(ts, body, Secret));
        request.Headers.Add("x-request-id", "req-" + Guid.NewGuid().ToString("N"));
        return request;
    }

    private static async Task<string> Token(HttpClient c) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync("/tingeesandbox?bin=970422&type=personal-account"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(out FakeTingee tingee, out MemoryInbox inbox, bool configured = true, string mode = "Direct", bool admin = true, string? merchantId = "9001")
    {
        var t = tingee = new FakeTingee(configured);
        var i = inbox = new MemoryInbox();
        var settings = new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["Tingee:BaseUrl"] = TingeeOptions.ProductionBaseUrl, ["Tingee:Mode"] = mode };
        if (configured) { settings["Tingee:ClientId"] = "sandbox-client"; settings["Tingee:SecretKey"] = Secret; }
        if (merchantId is not null) settings["Tingee:MerchantId"] = merchantId;
        var role = admin ? "SYS_ADMIN" : "VIEWER";
        return new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<ITingeeApi>();
                services.AddSingleton<ITingeeApi>(t);
                services.RemoveAll<ITingeeWebhookInbox>();
                services.AddSingleton<ITingeeWebhookInbox>(i);
                services.RemoveAll<AdminServices.ITingeeSandboxInbox>();
                services.AddSingleton<AdminServices.ITingeeSandboxInbox>(i);
                services.AddSingleton(new TestRole(role));
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    public sealed record TestRole(string Role);

    public sealed class MemoryInbox : ITingeeWebhookInbox, AdminServices.ITingeeSandboxInbox
    {
        public List<TingeeWebhookEnvelope> Saved { get; } = [];
        public bool IsAvailable => true;
        public Task SaveAsync(TingeeWebhookEnvelope envelope, CancellationToken cancellationToken) { Saved.Add(envelope); return Task.CompletedTask; }
        public Task<IReadOnlyList<AdminModels.TingeeSandboxWebhook>> RecentAsync(int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdminModels.TingeeSandboxWebhook>>(Saved.Select((e, n) =>
                AdminServices.TingeeSandboxWebhookParser.Parse(n + 1, e.Kind.ToString(), "RECEIVED", e.ReceivedAtUtc.UtcDateTime, e.RawBody)).Reverse().Take(take).ToList());
    }

    public sealed class FakeTingee(bool configured) : ITingeeApi
    {
        public bool IsConfigured => configured;
        public List<string> Calls { get; } = [];
        public List<TingeeDynamicQrRequest> Qrs { get; } = [];
        public List<(string, string, decimal?)> StaticQrs { get; } = [];
        public List<TingeeCreateVaRequest> Vas { get; } = [];
        public List<(string, string, string)> Confirms { get; } = [];
        public TingeeApiException? ListError { get; set; }
        public decimal Paid { get; set; }

        public Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default)
        {
            Calls.Add("get-va-paging");
            if (ListError is not null) throw ListError;
            return Task.FromResult(new TingeePage<TingeeVaItem> { TotalCount = 1, Items = [new() { BankBin = "970422", AccountType = "personal-account", AccountNumber = "0123456789", VaAccountNumber = "963123456789", Status = "active" }] });
        }
        public Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default)
        {
            Vas.Add(request);
            return Task.FromResult(new TingeeCreateVaResult { ConfirmId = "CONF-1", OtpMethod = "SMS" });
        }
        public Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default)
        {
            Confirms.Add((bankBin, confirmId, otp));
            return Task.FromResult(new TingeeConfirmVaResult { VaAccountNumber = "963123456789" });
        }
        public Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default)
        {
            Qrs.Add(request);
            return Task.FromResult(new TingeeDynamicQr { QrCode = "00020101021238570010A000000727", QrAccount = "QRACC", BillId = "BILL1" });
        }
        public Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default)
        {
            StaticQrs.Add((request.BankBin, request.AccountNumber, request.Amount));
            return Task.FromResult(new TingeeStaticQr { QrCode = "00020101021138570010A000000727STATIC" });
        }
        public Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) =>
            Task.FromResult(new TingeeQrStatus { BillInfo = new() { BillId = billId, Amount = 2000, TotalAmountPaid = Paid, TotalPaymentsCount = Paid > 0 ? 1 : 0, Status = Paid >= 2000 ? "paid" : "pending" } });
        public Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestRole role)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "5a5d0000-0000-0000-0000-00000000000a"), new Claim(ClaimTypes.Name, "safe-sandbox"), new Claim("display_name", "Safe Sandbox"), new Claim(ClaimTypes.Role, role.Role) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
