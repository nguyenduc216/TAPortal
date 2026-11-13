extern alias admin;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using TAPortal.Integrations.ZaloMiniApp;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// The "Zalo" pages of both sites and the Mini App API, rendered end to end with in-memory stores (no SQL, no Zalo, no ZNS).
public sealed class SafeZaloMiniAppPageTests
{
    private static readonly Guid Staff = Guid.Parse("5a000000-0000-0000-0000-00000000005a");

    private static Dictionary<string, string?> Config(bool required = false) => new()
    {
        ["ZaloMiniApp:AppId"] = "1234567890", ["ZaloMiniApp:SecretKey"] = "test-secret", ["ZaloMiniApp:OtpTemplateId"] = "445566",
        ["ZaloMiniApp:RequirePhoneVerification"] = required ? "true" : "false"
    };

    private static void Fakes(IServiceCollection s, SafeZaloMiniAppTests.Store store, SafeZaloMiniAppTests.Sender sender)
    {
        s.RemoveAll<IMiniAppStore>();
        s.AddSingleton<IMiniAppStore>(store);
        s.RemoveAll<IZnsSender>();
        s.AddSingleton<IZnsSender>(sender);
        s.RemoveAll<IZaloGraph>();
        s.AddSingleton<IZaloGraph>(new SafeZaloMiniAppTests.Graph());
    }

    private static WebApplicationFactory<Program> Customer(SafeZaloMiniAppTests.Store store, SafeZaloMiniAppTests.Sender sender, bool required = false) =>
        Factory("OWNER", out _, out _).WithWebHostBuilder(b => b
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Config(required)))
            .ConfigureTestServices(s => Fakes(s, store, sender)));

    private static SafeZaloMiniAppTests.Store StoreWith(Guid user, string username, string? phone)
    {
        var store = new SafeZaloMiniAppTests.Store();
        store.Accounts[user] = new(user, username, "Người dùng", phone, "hash", false);
        return store;
    }

    private static string Html(string raw) => WebUtility.HtmlDecode(raw);

    [Fact]
    public async Task CustomerVerifiesThePhoneByZaloOtpThenOpensTheQr()
    {
        var store = StoreWith(UserId, "member", "0912345678");
        var sender = new SafeZaloMiniAppTests.Sender();
        using var f = Customer(store, sender);
        var c = Client(f);

        var page = Html(await c.GetStringAsync("/account/zalo"));
        Assert.Contains("data-cp-phone-unverified", page);
        Assert.Contains("0912345678", page);
        Assert.Contains("data-cp-send-otp", page);
        Assert.DoesNotContain("data-cp-open-qr", page);
        Assert.DoesNotContain("T.A", page[page.IndexOf("data-cp-zalo", StringComparison.Ordinal)..]);

        var token = await Token(c, "/account/zalo");
        var sent = await Post(c, "/account/zalo/send-otp", token, []);
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        var (phone, _, data) = Assert.Single(sender.Sent);
        Assert.Equal("84912345678", phone);

        page = await c.GetStringAsync("/account/zalo");
        Assert.Contains("data-cp-otp-form", page);
        var challenge = System.Text.RegularExpressions.Regex.Match(page, "name=\"challengeId\" value=\"([^\"]+)\"").Groups[1].Value;
        var wrong = data["otp"] == "000000" ? "111111" : "000000";
        await Post(c, "/account/zalo/confirm-otp", await Token(c, "/account/zalo"), new() { ["challengeId"] = challenge, ["code"] = wrong });
        Assert.Contains("data-cp-otp-form", await c.GetStringAsync("/account/zalo"));   // still open for another try
        await Post(c, "/account/zalo/confirm-otp", await Token(c, "/account/zalo"), new() { ["challengeId"] = challenge, ["code"] = data["otp"] });

        page = await c.GetStringAsync("/account/zalo");
        Assert.Contains("data-cp-phone-verified", page);
        Assert.Contains("data-cp-open-qr", page);
        var qr = await Post(c, "/account/zalo/qr", await Token(c, "/account/zalo"), []);
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        var qrPage = await qr.Content.ReadAsStringAsync();
        Assert.Contains("data-cp-zalo-qr", qrPage);
        Assert.Contains("<svg", qrPage);
        Assert.Contains("data-cp-countdown-stay", qrPage);
        Assert.Single(store.Qrs);
    }

    [Fact]
    public async Task CustomerChangesThePhoneAndUnlinksZalo()
    {
        var store = StoreWith(UserId, "member", "0912345678");
        store.Links.Add(("Z-1", UserId, "84912345678", MiniAppLinkSource.Qr, false));
        using var f = Customer(store, new());
        var c = Client(f);
        Assert.Contains("data-cp-links", await c.GetStringAsync("/account/zalo"));
        await Post(c, "/account/zalo/unlink", await Token(c, "/account/zalo"), []);
        Assert.All(store.Links, l => Assert.True(l.Revoked));

        await Post(c, "/account/zalo/phone", await Token(c, "/account/zalo"), new() { ["phone"] = "0987 654 321" });
        Assert.Equal("0987654321", store.Accounts[UserId].Phone);
        await Post(c, "/account/zalo/phone", await Token(c, "/account/zalo"), new() { ["phone"] = "12345" });
        Assert.Equal("0987654321", store.Accounts[UserId].Phone);
    }

    [Fact]
    public async Task WhenRequiredAnUnverifiedAccountSeesOnlyTheZaloPage()
    {
        var store = StoreWith(UserId, "member", "0912345678");
        var sender = new SafeZaloMiniAppTests.Sender();
        using var f = Customer(store, sender, required: true);
        var c = Client(f);
        var blocked = await c.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal("/account/zalo", blocked.Headers.Location!.OriginalString);
        Assert.Contains("data-cp-verify-required", await c.GetStringAsync("/account/zalo"));

        await Post(c, "/account/zalo/send-otp", await Token(c, "/account/zalo"), []);
        var challenge = store.Otps.Single().Id.ToString();
        await Post(c, "/account/zalo/confirm-otp", await Token(c, "/account/zalo"), new() { ["challengeId"] = challenge, ["code"] = sender.Sent[0].Data["otp"] });
        Assert.NotEqual("/account/zalo", (await c.GetAsync("/dashboard")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task MiniAppApiSignsInWithPasswordAndSamePhoneThenServesTheSession()
    {
        var store = StoreWith(UserId, "member", "0912345678");
        using var f = Customer(store, new()).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IMiniAppPasswordCheck>();
            s.AddSingleton<IMiniAppPasswordCheck>(new PasswordIsSecret());
        }));
        using var api = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        var bad = await api.PostAsJsonAsync("/api/zalo-mini-app/login", new { username = "member", password = "secret", accessToken = "zalo:Z-9", phoneToken = "phone:0987654321" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("không trùng", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var ok = await api.PostAsJsonAsync("/api/zalo-mini-app/login", new { username = "member", password = "secret", accessToken = "zalo:Z-1", phoneToken = "phone:0912345678" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var session = (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetString();

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/zalo-mini-app/me");
        me.Headers.Authorization = new("Bearer", session);
        var meResponse = await api.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal("member", (await meResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account").GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/zalo-mini-app/me")).StatusCode);

        var resume = await api.PostAsJsonAsync("/api/zalo-mini-app/resume", new { accessToken = "zalo:Z-1" });
        Assert.False(string.IsNullOrEmpty((await resume.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetString()));
    }

    [Fact]
    public async Task MiniAppApiAllowsOnlyTheMiniAppOrigins()
    {
        using var f = Customer(StoreWith(UserId, "member", "0912345678"), new());
        using var api = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        async Task<string?> Preflight(string origin)
        {
            using var r = new HttpRequestMessage(HttpMethod.Options, "/api/zalo-mini-app/resume");
            r.Headers.Add("Origin", origin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            r.Headers.Add("Access-Control-Request-Headers", "content-type");
            var response = await api.SendAsync(r);
            return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;
        }
        Assert.Equal("https://h5.zdn.vn", await Preflight("https://h5.zdn.vn"));
        Assert.Null(await Preflight("https://evil.example"));
    }

    [Fact]
    public async Task StaffVerifiesThePhoneAndOpensTheQrOnTaPortal()
    {
        var store = StoreWith(Staff, "staff", "0912345678");
        var sender = new SafeZaloMiniAppTests.Sender();
        using var f = Admin(store, sender);
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

        var page = Html(await c.GetStringAsync("/Account/Zalo"));
        Assert.Contains("data-ta-phone-unverified", page);
        Assert.Contains("data-ta-send-otp", page);
        await Post(c, "/Account/ZaloSendOtp", await Token(c, "/Account/Zalo"), []);
        var challenge = store.Otps.Single().Id.ToString();
        await Post(c, "/Account/ZaloConfirmOtp", await Token(c, "/Account/Zalo"), new() { ["challengeId"] = challenge, ["code"] = sender.Sent[0].Data["otp"] });
        page = await c.GetStringAsync("/Account/Zalo");
        Assert.Contains("data-ta-phone-verified", page);
        var qr = await Post(c, "/Account/ZaloQr", await Token(c, "/Account/Zalo"), []);
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Contains("data-ta-zalo-qr", await qr.Content.ReadAsStringAsync());
        Assert.Single(store.Qrs);
    }

    [Fact]
    public async Task StaffZaloPostsNeedAntiforgery()
    {
        using var f = Admin(StoreWith(Staff, "staff", "0912345678"), new());
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/Account/ZaloSendOtp", new FormUrlEncodedContent([]))).StatusCode);
    }

    private sealed class PasswordIsSecret : IMiniAppPasswordCheck
    {
        public bool TryAcquire(string username) => true;
        public bool Verify(string password, string? passwordHash) => password == "secret";
    }

    // ---- TA Portal host --------------------------------------------------------------------------------------------
    private const string AdminScheme = "ZaloAdminTest";

    private static WebApplicationFactory<admin::Program> Admin(SafeZaloMiniAppTests.Store store, SafeZaloMiniAppTests.Sender sender) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>(Config()) { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                Fakes(s, store, sender);
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, StaffAuth>(AdminScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = AdminScheme; o.DefaultAuthenticateScheme = AdminScheme; o.DefaultChallengeScheme = AdminScheme; o.DefaultForbidScheme = AdminScheme; });
            }));

    private sealed class StaffAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Staff.ToString()), new Claim(ClaimTypes.Name, "staff"), new Claim("display_name", "Nhân viên"), new Claim(ClaimTypes.Role, "STAFF") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, AdminScheme)), AdminScheme)));
        }
    }
}
