extern alias admin;

using System.Collections.Concurrent;
using System.Net;
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
using TAPortal.Shared;
using AdminModels = admin::TAPortal.Web.Models;
using AdminSecurity = admin::TAPortal.Web.Security;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Checkpoint 9 (AUD-23/24/25/29/35/36/37). No SQL, no real proxy, no timing measurement:
// TIMING_NOT_VERIFIED, PROXY_NOT_VERIFIED, DATABASE_NOT_VERIFIED for the corresponding live behaviour.
public sealed class SafeAdminHardeningTests
{
    private const string Correct = "Correct!123";

    // ---- AUD-23: identical work and response for every failed login --------------------------------------
    [Theory]
    [InlineData("nobody", Correct)]          // unknown username
    [InlineData("admin-ok", "Wrong!123")]    // existing account, wrong password
    [InlineData("inactive", Correct)]        // IsActive=0  (SQL lookup returns no row)
    [InlineData("deleted", Correct)]         // IsDeleted=1 (SQL lookup returns no row)
    [InlineData("locked", Correct)]          // Status<>ACTIVE (SQL lookup returns no row)
    [InlineData("nohash", Correct)]          // account without a usable hash
    public async Task FailedLoginsDoTheSameStoreWorkAndLookIdentical(string username, string password)
    {
        var store = new FakeAuth();
        using var f = Factory(store);
        using var c = Client(f);
        var response = await PostLogin(c, username, password);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tài khoản hoặc mật khẩu không đúng.", html);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], v => v.StartsWith("TA.Portal.Auth=") && !v.StartsWith("TA.Portal.Auth=;"));
        Assert.Equal(["find"], store.Calls);                   // grants are never loaded before the password matched
        Assert.DoesNotContain(password, html);                  // password field is cleared
        Assert.Equal(Normalize(await BaselineFailure()), Normalize(html.Replace(username, "<user>")));
    }

    [Fact]
    public async Task CorrectPasswordLoadsGrantsOnlyThenSignsIn()
    {
        var store = new FakeAuth();
        using var f = Factory(store);
        using var c = Client(f);
        var response = await PostLogin(c, "admin-ok", Correct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(["find", "grants", "touch"], store.Calls);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), v => v.StartsWith("TA.Portal.Auth=") && !v.StartsWith("TA.Portal.Auth=;"));
    }

    [Fact]
    public async Task CorrectPasswordWithoutAnyGrantIsStillAGenericFailure()
    {
        var store = new FakeAuth();
        using var f = Factory(store);
        var response = await PostLogin(Client(f), "customer-only", Correct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tài khoản hoặc mật khẩu không đúng.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(["find", "grants"], store.Calls);         // only reachable with the right password
    }

    [Fact]
    public async Task ThrottledRequestsNeverReachTheStoreAndPasswordsNeverReachLogs()
    {
        var store = new FakeAuth();
        var logs = new LogSink();
        using var f = Factory(store, logs: logs);
        using var c = Client(f);
        for (var i = 0; i < 5; i++) await PostLogin(c, "admin-ok", "Secret-Attempt-" + i);
        var blocked = await PostLogin(c, "admin-ok", "Secret-Attempt-5");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(5, store.Calls.Count(x => x == "find"));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("Secret-Attempt") || m.Contains("admin-ok"));
    }

    // ---- AUD-24: throttle bounds, concurrency, proxy allowlist ---------------------------------------------
    [Fact]
    public void ConcurrentAttemptsOnOneAccountAreCountedExactlyOnceEach()
    {
        var t = Throttle(new FakeTime());
        var allowed = 0;
        Parallel.For(0, 200, i => { if (t.TryAcquire(IPAddress.Parse("198.51.100." + (i % 200 + 1)), "victim").Allowed) Interlocked.Increment(ref allowed); });
        Assert.Equal(5, allowed);
    }

    [Fact]
    public void TrackedKeysAreBoundedAndFailClosedUntilWindowsExpire()
    {
        var time = new FakeTime();
        var t = Throttle(time, maxKeys: 100);
        for (var i = 0; i < 50; i++) Assert.True(t.TryAcquire(IPAddress.Parse("198.51.100." + (i + 1)), "user-" + i).Allowed);
        Assert.Equal(100, t.TrackedKeys);
        var full = t.TryAcquire(IPAddress.Parse("203.0.113.1"), "newcomer");
        Assert.False(full.Allowed);                                  // no eviction of live counters
        Assert.True(t.TrackedKeys <= 100);
        Assert.True(t.TryAcquire(IPAddress.Parse("198.51.100.1"), "user-0").Allowed); // existing partitions keep working
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(t.TryAcquire(IPAddress.Parse("203.0.113.1"), "newcomer").Allowed); // expired windows swept
        Assert.Equal(2, t.TrackedKeys);
    }

    [Fact]
    public void SeparateInstancesDoNotShareCountersKnownLimitation()
    {
        // Documents AUD-24: per-process memory. Two instances (or a restart) each allow the full budget.
        var a = Throttle(new FakeTime());
        var b = Throttle(new FakeTime());
        var total = Enumerable.Range(0, 10).Count(i => (i % 2 == 0 ? a : b).TryAcquire(null, "victim").Allowed);
        Assert.Equal(10, total);
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("255.255.255.255")]
    [InlineData("proxy.local")]
    public void TrustedProxyAllowlistRejectsRangesWildcardsAndNames(string value) =>
        Assert.Throws<InvalidOperationException>(() => AdminSecurity.TrustedProxyConfig.Parse([value]));

    [Fact]
    public void TrustedProxyAllowlistAcceptsExactAddressesAndDefaultsToNone()
    {
        Assert.Empty(AdminSecurity.TrustedProxyConfig.Parse(null));
        Assert.Equal(2, AdminSecurity.TrustedProxyConfig.Parse(["10.0.0.5", " ::1 "]).Count);
    }

    [Fact]
    public async Task ForwardedForIsHonouredOnlyFromAllowlistedProxy()
    {
        using var f = Factory(new FakeAuth(), config: new() { ["ReverseProxy:KnownProxies:0"] = "10.0.0.5" });
        using var c = Client(f);
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "rot-" + i, "x", socket: "10.0.0.5", forwardedFor: "198.51.100.10")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostLogin(c, "rot-20", "x", socket: "10.0.0.5", forwardedFor: "198.51.100.10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "rot-20", "x", socket: "10.0.0.5", forwardedFor: "198.51.100.11")).StatusCode); // other client behind the proxy
        Assert.Equal(HttpStatusCode.OK, (await PostLogin(c, "rot-20", "x", socket: "10.0.0.9", forwardedFor: "198.51.100.10")).StatusCode); // untrusted socket: header ignored
    }

    [Fact]
    public void InvalidProxyConfigurationStopsTheApplication()
    {
        using var f = Factory(new FakeAuth(), config: new() { ["ReverseProxy:KnownProxies:0"] = "0.0.0.0/0" });
        Assert.Throws<InvalidOperationException>(() => f.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }

    // ---- AUD-25/29: fail-closed decision -------------------------------------------------------------------
    [Theory]
    [InlineData(AuditWriteStatus.Written, false, false, false)]
    [InlineData(AuditWriteStatus.Written, true, false, false)]
    [InlineData(AuditWriteStatus.TableUnavailable, false, false, false)]  // default mode: change kept, warning shown
    [InlineData(AuditWriteStatus.TableUnavailable, true, false, true)]
    [InlineData(AuditWriteStatus.SchemaMismatch, true, false, true)]
    [InlineData(AuditWriteStatus.InsertFailed, true, false, true)]        // e.g. INSERT permission denied
    [InlineData(AuditWriteStatus.InsertFailed, false, true, true)]        // doomed transaction always rolls back
    public void AuditRollbackDecision(AuditWriteStatus status, bool required, bool doomed, bool rollback) =>
        Assert.Equal(rollback, AuditPolicy.MustRollback(new AuditWriteResult(status), required, doomed));

    [Theory]
    [InlineData(1, AuditWriteStatus.Written)]
    [InlineData(-1, AuditWriteStatus.TableUnavailable)]
    [InlineData(-2, AuditWriteStatus.SchemaMismatch)]
    public void AuditStatementResultIsClassified(int code, AuditWriteStatus expected) => Assert.Equal(expected, AuditSql.Classify(code).Status);

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData(null, false)]
    public void RequireDurableAuditIsOptInOnly(string? value, bool expected) =>
        Assert.Equal(expected, AuditPolicy.RequireDurable(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:Audit:RequireDurableAudit"] = value }).Build()));

    [Fact]
    public void AuditStatementNeverReferencesColumnsBeforeCheckingThem()
    {
        // The INSERT runs through sp_executesql so a missing column yields -2 instead of a batch compile error.
        var insertAt = AuditSql.Insert.IndexOf("INSERT dbo.AuditLogs", StringComparison.Ordinal);
        Assert.True(AuditSql.Insert.IndexOf("sp_executesql", StringComparison.Ordinal) < insertAt);
        Assert.True(AuditSql.Insert.IndexOf("COL_LENGTH", StringComparison.Ordinal) < insertAt);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void StartupWarnsWhenDurableAuditIsNotEnforced(string required, bool enforced)
    {
        var logs = new LogSink();
        using var f = Factory(new FakeAuth(), config: new() { ["Security:Audit:RequireDurableAudit"] = required }, logs: logs);
        _ = f.Server; // build and start the host
        Assert.Equal(!enforced, logs.Messages.Any(m => m.Contains("DURABLE_AUDIT_NOT_ENFORCED")));
    }

    [Fact]
    public async Task CustomerSaveRejectedWhenAuditIsMandatoryAndUnavailable()
    {
        using var f = Factory(new FakeAuth(), role: "SYS_ADMIN", customers: new AuditFailingCustomers());
        using var c = Client(f);
        var html = await c.GetStringAsync("/customers/create");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await c.PostAsync("/customers/create", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["Code"] = "KH-X", ["Name"] = "X", ["IsActive"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Không thể ghi nhật ký audit", body);
        Assert.DoesNotContain("SqlException", body);
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    private static AdminSecurity.AdminLoginThrottle Throttle(FakeTime time, int maxKeys = 100_000) =>
        new(new AdminSecurity.AdminLoginThrottleOptions { AccountPermitLimit = 5, IpPermitLimit = 20, Window = TimeSpan.FromMinutes(5), MaxTrackedKeys = maxKeys }, time);

    private static async Task<string> BaselineFailure()
    {
        using var f = Factory(new FakeAuth());
        var r = await PostLogin(Client(f), "baseline-unknown", "Whatever!1");
        return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()).Replace("baseline-unknown", "<user>");
    }

    private static string Normalize(string html) => Regex.Replace(html, "name=\"__RequestVerificationToken\"[^>]*value=\"[^\"]+\"", "TOKEN");

    private static async Task<HttpResponseMessage> PostLogin(HttpClient c, string username, string password, string? socket = null, string? forwardedFor = null)
    {
        var html = await c.GetStringAsync("/account/login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/login") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = username, ["Password"] = password, ["__RequestVerificationToken"] = token }) };
        if (socket != null) request.Headers.Add("X-Test-Socket", socket);
        if (forwardedFor != null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await c.SendAsync(request);
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(FakeAuth auth, Dictionary<string, string?>? config = null, LogSink? logs = null, string? role = null, AdminServices.ICustomerAdminStore? customers = null) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>(config ?? new()) { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureLogging(l => { if (logs != null) l.AddProvider(logs); })
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(new FakeTime());
                s.RemoveAll<AdminServices.IAdminAuthStore>();
                s.AddSingleton<AdminServices.IAdminAuthStore>(auth);
                s.AddSingleton<IStartupFilter, SocketStartupFilter>();
                if (customers != null) { s.RemoveAll<AdminServices.ICustomerAdminStore>(); s.AddSingleton(customers); }
                if (role is null) return;
                s.AddSingleton(new TestRole(role));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("SafeHardening", _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = "SafeHardening"; o.DefaultAuthenticateScheme = "SafeHardening"; o.DefaultChallengeScheme = "SafeHardening"; o.DefaultForbidScheme = "SafeHardening"; });
            }));

    private sealed class FakeAuth : AdminServices.IAdminAuthStore
    {
        private static readonly string Hash = new PasswordService().Hash(Correct);
        private readonly Dictionary<string, AdminServices.LoginCandidate> eligible = new(StringComparer.OrdinalIgnoreCase)
        {
            ["admin-ok"] = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "admin-ok", "Admin", null, null, Hash),
            ["customer-only"] = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "customer-only", "Customer", null, null, Hash),
            ["nohash"] = new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "nohash", "No hash", null, null, "")
        };
        public ConcurrentQueue<string> CallLog { get; } = new();
        public List<string> Calls => CallLog.ToList();
        public Task<AdminServices.LoginCandidate?> FindLoginCandidateAsync(string username)
        {
            CallLog.Enqueue("find");
            return Task.FromResult(eligible.TryGetValue(username.Trim(), out var c) ? c : null); // inactive/deleted/locked are filtered by SQL
        }
        public Task<AdminServices.AuthGrants> LoadGrantsAsync(Guid userId)
        {
            CallLog.Enqueue("grants");
            return Task.FromResult(userId == eligible["admin-ok"].Id ? new AdminServices.AuthGrants(["SYS_ADMIN"], []) : new AdminServices.AuthGrants([], []));
        }
        public Task TouchLoginAsync(Guid userId) { CallLog.Enqueue("touch"); return Task.CompletedTask; }
    }

    private sealed class AuditFailingCustomers : AdminServices.ICustomerAdminStore
    {
        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid customerId) => Task.FromResult<AdminModels.CustomerEditVm?>(null);
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => Task.FromResult(new AdminServices.CustomerChangeResult(AdminServices.CustomerChangeOutcome.AuditUnavailable));
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => Task.FromResult(new AdminServices.CustomerChangeResult(AdminServices.CustomerChangeOutcome.AuditUnavailable));
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    // Test-only stand-in for the TCP peer address; runs before the app's forwarded-headers middleware.
    private sealed class SocketStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, n) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Socket", out var ip)) context.Connection.RemoteIpAddress = IPAddress.Parse(ip!);
                return n(context);
            });
            next(app);
        };
    }

    private sealed class LogSink : ILoggerProvider
    {
        public ConcurrentQueue<string> Queue { get; } = new();
        public List<string> Messages => Queue.ToList();
        public ILogger CreateLogger(string categoryName) => new Sink(Queue);
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> queue) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => queue.Enqueue(formatter(state, exception));
        }
    }

    private sealed record TestRole(string Role);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestRole role)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"), new(ClaimTypes.Name, "hardening"), new(ClaimTypes.Role, role.Role)], "SafeHardening")), "SafeHardening")));
    }
}
