extern alias admin;

using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdminSecurity = admin::TAPortal.Web.Security;

namespace TACustomerPortal.Tests;

// AUD-13 (Admin login throttling) and AUD-14 (Admin no-store). No SQL host, connection, account or real password:
// every login POST omits the password, so it is counted by the throttle and then stops at model validation.
public sealed class SafeAdminSecurityTests
{
    private const string TestScheme = "SafeSecurityTest";

    private static AdminSecurity.AdminLoginThrottle Throttle(FakeTime time, int account = 5, int ip = 20) =>
        new(new AdminSecurity.AdminLoginThrottleOptions { AccountPermitLimit = account, IpPermitLimit = ip, Window = TimeSpan.FromMinutes(5) }, time);

    [Fact]
    public void AccountLimitAllowsFiveThenRejectsWithRetryAfter()
    {
        var time = new FakeTime();
        var t = Throttle(time);
        var ip = IPAddress.Parse("198.51.100.1");
        for (var i = 0; i < 5; i++) Assert.True(t.TryAcquire(ip, "victim").Allowed);
        time.Advance(TimeSpan.FromMinutes(1));
        var rejected = t.TryAcquire(ip, "victim");
        Assert.False(rejected.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(4), rejected.RetryAfter);
    }

    [Fact]
    public void WindowRecoversAutomaticallyAndRejectedAttemptsDoNotExtendIt()
    {
        var time = new FakeTime();
        var t = Throttle(time);
        var ip = IPAddress.Parse("198.51.100.2");
        for (var i = 0; i < 5; i++) t.TryAcquire(ip, "victim");
        for (var i = 0; i < 50; i++) { time.Advance(TimeSpan.FromSeconds(5)); Assert.False(t.TryAcquire(ip, "victim").Allowed); }
        time.Advance(TimeSpan.FromSeconds(50)); // exactly 5 minutes after the first attempt
        Assert.True(t.TryAcquire(ip, "victim").Allowed);
    }

    [Fact]
    public void AccountPartitionIsIndependentOfIpAndNormalized()
    {
        var t = Throttle(new FakeTime());
        for (var i = 0; i < 5; i++) Assert.True(t.TryAcquire(IPAddress.Parse("198.51.100." + (10 + i)), i % 2 == 0 ? "Victim" : "  VICTIM ").Allowed);
        Assert.False(t.TryAcquire(IPAddress.Parse("203.0.113.99"), "victim").Allowed); // new IP cannot reset the account window
        Assert.True(t.TryAcquire(IPAddress.Parse("203.0.113.99"), "other").Allowed);   // other accounts unaffected
    }

    [Fact]
    public void IpLimitStopsUsernameRotationWithoutAffectingOtherSources()
    {
        var t = Throttle(new FakeTime());
        var attacker = IPAddress.Parse("198.51.100.3");
        for (var i = 0; i < 20; i++) Assert.True(t.TryAcquire(attacker, "guess-" + i).Allowed);
        Assert.False(t.TryAcquire(attacker, "guess-20").Allowed);
        Assert.True(t.TryAcquire(IPAddress.Parse("198.51.100.4"), "guess-20").Allowed);
    }

    [Fact]
    public void UnknownAndExistingAccountsAreThrottledIdentically()
    {
        // The throttle never consults the user store, so behaviour cannot depend on whether an account exists.
        var a = Throttle(new FakeTime());
        var results = Enumerable.Range(0, 7).Select(_ => (a.TryAcquire(null, "admin").Allowed, a.TryAcquire(null, "no-such-user-x").Allowed)).ToList();
        Assert.All(results, r => Assert.Equal(r.Item1, r.Item2));
        Assert.Equal(new[] { true, true, true, true, true, false, false }, results.Select(r => r.Item1));
    }

    [Fact]
    public void StateKeepsOnlyHashedKeysAndExpiredWindowsAreSwept()
    {
        var time = new FakeTime();
        var t = Throttle(time);
        t.TryAcquire(IPAddress.Parse("198.51.100.5"), "secret-username");
        var keys = Keys(t);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Matches("^(ip|acct):[0-9A-F]{64}$", k));
        time.Advance(TimeSpan.FromMinutes(6));
        t.TryAcquire(IPAddress.Parse("198.51.100.6"), "next");
        Assert.Equal(2, Keys(t).Count); // the first two expired entries were removed
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(5, 0)]
    public void InvalidConfigurationFailsAtStartup(int account, int ip)
    {
        using var f = Factory(null, new FakeTime(), new() { ["Security:AdminLogin:AccountPermitLimit"] = account.ToString(), ["Security:AdminLogin:IpPermitLimit"] = ip.ToString() });
        Assert.Throws<InvalidOperationException>(() => f.Services.GetRequiredService<AdminSecurity.AdminLoginThrottleOptions>());
    }

    [Fact]
    public async Task LoginPostReturns429WithRetryAfterAndRecoversAfterWindow()
    {
        var time = new FakeTime();
        using var f = Factory(null, time);
        using var c = Client(f);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "victim")).StatusCode);
        var blocked = await PostLogin(c, "victim");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(300), blocked.Headers.RetryAfter!.Delta);
        Assert.True(blocked.Headers.CacheControl!.NoStore);
        var html = WebUtility.HtmlDecode(await blocked.Content.ReadAsStringAsync());
        Assert.Contains("thử đăng nhập quá nhiều lần", html);
        Assert.DoesNotContain("mật khẩu không đúng", html); // credentials were not evaluated, so nothing about the account is revealed
        Assert.DoesNotContain("Tài khoản khởi tạo", html);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "victim")).StatusCode);
    }

    [Fact]
    public async Task GetLoginAndCsrfRejectedPostsAreNotCounted()
    {
        using var f = Factory(null, new FakeTime());
        using var c = Client(f);
        for (var i = 0; i < 30; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/account/login")).StatusCode);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = "victim" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "victim")).StatusCode); // CSRF still enforced and nothing was consumed
    }

    [Fact]
    public async Task IpPartitionsAreIsolatedAndForwardedHeadersAreIgnored()
    {
        using var f = Factory(null, new FakeTime());
        using var c = Client(f);
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "rotate-" + i, ip: "198.51.100.7")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostLogin(c, "rotate-20", ip: "198.51.100.7", forwardedFor: "192.0.2.55")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "rotate-20", ip: "198.51.100.8")).StatusCode);
    }

    [Theory]
    [InlineData("/account/login", HttpStatusCode.OK)]
    [InlineData("/users", HttpStatusCode.Redirect)]
    [InlineData("/customerportalaccounts", HttpStatusCode.Redirect)]
    [InlineData("/account/accessdenied", HttpStatusCode.OK)]
    public async Task AnonymousDynamicResponsesAreNoStore(string path, HttpStatusCode expected)
    {
        using var f = Factory(null, new FakeTime());
        var response = await Client(f).GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        AssertNoStore(response);
    }

    [Fact]
    public async Task ConfiguredCookiePathsResolveToRealPages()
    {
        using var f = Factory(null, new FakeTime());
        var cookie = f.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get("Cookies");
        using var c = Client(f);
        foreach (var path in new[] { cookie.LoginPath.Value!, cookie.AccessDeniedPath.Value! })
        {
            var response = await c.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertNoStore(response);
        }
    }

    [Fact]
    public async Task StaticAssetsKeepNormalCaching()
    {
        using var f = Factory(null, new FakeTime());
        using var c = Client(f);
        foreach (var path in new[] { "/css/ta-design.css", "/js/ta-portal.js", "/images/favicon-32x32.png" })
        {
            var response = await c.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.CacheControl?.NoStore ?? false, path);
            Assert.False(response.Headers.Pragma.Any(), path);
            Assert.NotNull(response.Headers.ETag);
        }
    }

    [Fact]
    public async Task AuthenticatedAdminPagesAndForbiddenResponsesAreNoStore()
    {
        using var admin = Factory("SYS_ADMIN", new FakeTime());
        var page = await Client(admin).GetAsync("/account/changepassword");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Đổi mật khẩu", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
        AssertNoStore(page);

        using var owner = Factory("OWNER", new FakeTime());
        var forbidden = await Client(owner).GetAsync("/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode); // authorization semantics unchanged
        AssertNoStore(forbidden);
    }

    [Fact]
    public async Task LogoutClearsCookieAndBrowserCache()
    {
        using var f = Factory("SYS_ADMIN", new FakeTime());
        using var c = Client(f);
        var token = await Token(c, "/account/changepassword");
        var response = await c.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("\"cache\"", response.Headers.GetValues("Clear-Site-Data").Single());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith("TA.Portal.Auth=;", StringComparison.Ordinal));
        AssertNoStore(response);
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore, $"{response.RequestMessage!.RequestUri} lacks no-store");
        Assert.False(response.Headers.CacheControl!.Public);
        Assert.Contains("no-cache", response.Headers.Pragma.Select(p => p.Name));
    }

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<HttpResponseMessage> PostLogin(HttpClient c, string username, string? ip = null, string? forwardedFor = null)
    {
        var token = await Token(c, "/account/login");
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = "", ["__RequestVerificationToken"] = token }) };
        if (ip != null) request.Headers.Add("X-Test-Ip", ip);
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await c.SendAsync(request);
    }

    private static List<string> Keys(AdminSecurity.AdminLoginThrottle t) =>
        ((IDictionary)typeof(AdminSecurity.AdminLoginThrottle).GetField("windows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t)!).Keys.Cast<string>().ToList();

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string? role, FakeTime time, Dictionary<string, string?>? config = null) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>(config ?? new()) { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(time);
                s.AddSingleton<IStartupFilter, TestIpStartupFilter>();
                if (role is null) return;
                s.AddSingleton(new TestRole(role));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    // Test-only stand-in for the socket address (TestServer has none); never trusts X-Forwarded-For.
    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, n) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip)) context.Connection.RemoteIpAddress = IPAddress.Parse(ip!);
                return n(context);
            });
            next(app);
        };
    }

    private sealed record TestRole(string Role);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestRole role)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "6f9619ff-8b86-d011-b42d-00cf4fc964ff"), new Claim(ClaimTypes.Name, "safe-security-test"), new Claim(ClaimTypes.Role, role.Role) }, TestScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
