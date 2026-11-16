extern alias admin;

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
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
using AdminControllers = admin::TAPortal.Web.Controllers;
using AdminSecurity = admin::TAPortal.Web.Security;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// "Truy cập hộ" (user impersonation, operator 2026-11-14): sign in under another account after giving a reason; everything
// done meanwhile is recorded as "<actor> under <account>". No SQL here: the SQL text and the migration are checked as text,
// the HTTP tests use an in-memory history store and a test principal carrying the impersonation claims.
public sealed class SafeImpersonationTests
{
    private const string TestScheme = "SafeImpersonationTest";
    private static readonly Guid Actor = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid Target = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private static readonly Guid Session = Guid.Parse("c3333333-3333-4333-8333-333333333333");

    private static S.AuthUserSnapshot User(Guid id, params string[] roles) =>
        new(id, "thuytt", "Trần Thị Thủy", null, null, "PBKDF2$x", roles, ["TASKS.VIEW"], ["Nhân viên"]);

    [Fact]
    public void AReasonIsRequiredAndBounded()
    {
        Assert.NotNull(AdminSecurity.Impersonation.ValidateReason(null));
        Assert.NotNull(AdminSecurity.Impersonation.ValidateReason("   ngắn   "));
        Assert.NotNull(AdminSecurity.Impersonation.ValidateReason(new string('a', 501)));
        Assert.Null(AdminSecurity.Impersonation.ValidateReason("Nhập báo cáo công việc giúp chị Thủy"));
    }

    [Fact]
    public void OnlyAnotherEligibleNonAdministratorAccountCanBeUsed()
    {
        Assert.NotNull(AdminSecurity.Impersonation.CheckTarget(Actor, Actor, User(Actor, "STAFF")));          // yourself
        Assert.NotNull(AdminSecurity.Impersonation.CheckTarget(Actor, Target, null));                          // locked / deleted
        Assert.NotNull(AdminSecurity.Impersonation.CheckTarget(Actor, Target, User(Target, "SYS_ADMIN")));    // would widen rights
        Assert.NotNull(AdminSecurity.Impersonation.CheckTarget(Actor, Target, User(Target) with { Permissions = [] }));
        Assert.Null(AdminSecurity.Impersonation.CheckTarget(Actor, Target, User(Target, "STAFF")));
    }

    [Fact]
    public void OnlyHoldersOfThePermissionOrSystemAdministratorsMayImpersonate()
    {
        Assert.True(AdminSecurity.Impersonation.MayImpersonate(User(Actor, "SYS_ADMIN")));
        Assert.True(AdminSecurity.Impersonation.MayImpersonate(User(Actor) with { Permissions = ["AUTH.USERS.IMPERSONATE"] }));
        Assert.False(AdminSecurity.Impersonation.MayImpersonate(User(Actor, "STAFF")));
        Assert.Contains("AUTH.USERS.IMPERSONATE", AdminSecurity.AdminPermissions.Codes);
        Assert.Equal(["AUTH.USERS.IMPERSONATE"], AdminSecurity.AdminPermissions.Migration042Codes);
        Assert.DoesNotContain("AUTH.USERS.IMPERSONATE", AdminSecurity.AdminPermissions.MigrationCodes);   // not part of 028
    }

    [Fact]
    public void TheSessionClaimsRoundTripAndNeverForceAPasswordChange()
    {
        var started = new DateTime(2026, 11, 14, 2, 0, 0, DateTimeKind.Utc);
        var info = new AdminSecurity.ImpersonationInfo(Session, Actor, "duc", "Phạm Đức", "STAMP", "Nhập báo cáo giúp Thủy", started, started.AddHours(1));
        var claims = AdminSecurity.AdminClaims.For(User(Target, "STAFF") with { MustChangePassword = true }, mustChangePassword: false).Concat(AdminSecurity.Impersonation.Claims(info));
        var principal = AdminSecurity.AdminClaims.Principal(claims);
        Assert.Equal(info, AdminSecurity.Impersonation.Read(principal));
        Assert.Equal(Target.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));     // works with the account's rights
        Assert.False(principal.HasClaim("must_change_password", "1"));
        Assert.Null(AdminSecurity.Impersonation.Read(AdminSecurity.AdminClaims.Principal(AdminSecurity.AdminClaims.For(User(Actor, "STAFF"), false))));
    }

    [Fact]
    public void AuditRowsAndTaskEventsNameThePersonWhoReallyActed()
    {
        Assert.Contains("ImpersonatorUserId,ImpersonationSessionId) VALUES(@actor,@action,@entity,@target,@old,@new,@ip,@impActor,@impSession)", AuditSql.Insert);
        Assert.Contains("ELSE IF @impActor IS NOT NULL AND COL_LENGTH(N'dbo.AuditLogs',N'ImpersonatorUserId') IS NOT NULL", AuditSql.Insert);
        Assert.Contains("IF @imp IS NOT NULL AND COL_LENGTH(N'dbo.WorkTaskActivities', N'ImpersonatorUserId') IS NOT NULL", S.WorkTaskSql.Activity);
        Assert.Contains("JOIN dbo.Users iu ON iu.Id=a.ImpersonatorUserId", S.WorkTaskSql.Detail);
        Assert.Equal("Phạm Đức (truy cập hộ Trần Thị Thủy)", S.WorkTaskPolicy.ActedUnder("Phạm Đức", "Trần Thị Thủy"));
    }

    [Fact]
    public async Task TheAmbientContextFlowsWithTheRequestOnly()
    {
        Assert.Null(ImpersonationAmbient.Current);
        ImpersonationAmbient.Current = new(Actor, Session);
        var seen = await Task.Run(async () => { await Task.Yield(); return ImpersonationAmbient.Current; });
        Assert.Equal(new ImpersonationContext(Actor, Session), seen);
        ImpersonationAmbient.Current = null;
    }

    [Fact]
    public void TheMigrationRecordsSessionsRequestsAndTheActorColumns()
    {
        var root = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "042-user-impersonation.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.Contains("CONSTRAINT CK_ImpersonationSessions_NotSelf CHECK (ActorUserId <> TargetUserId)", sql);
        Assert.Contains("CREATE TABLE dbo.ImpersonationActions", sql);
        Assert.Contains("ALTER TABLE dbo.AuditLogs ADD ImpersonatorUserId uniqueidentifier NULL, ImpersonationSessionId uniqueidentifier NULL", sql);
        Assert.Contains("ALTER TABLE dbo.WorkTaskActivities ADD ImpersonatorUserId uniqueidentifier NULL", sql);
        Assert.Contains("'AUTH.USERS.IMPERSONATE'", sql);
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "073-verify-user-impersonation.sql")));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "074-rollback-user-impersonation.sql")));
    }

    [Fact]
    public void StartingAndTheHistoryNeedThePermissionLeavingNeedsOnlyASignIn()
    {
        var type = typeof(AdminControllers.ImpersonationController);
        string? Policy(string action, Type? verb = null) => type.GetMethods().Where(m => m.Name == action && (verb is null || m.GetCustomAttributes(verb, true).Length > 0))
            .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(true)).Select(a => a.Policy).FirstOrDefault(p => p is not null);
        Assert.Equal("PERM:AUTH.USERS.IMPERSONATE", Policy("Start", typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute)));
        Assert.Equal("PERM:AUTH.USERS.IMPERSONATE", Policy("Start", typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute)));
        Assert.Equal("PERM:AUTH.USERS.IMPERSONATE", Policy("Index"));
        Assert.Equal("PERM:AUTH.USERS.IMPERSONATE", Policy("Details"));
        var stop = typeof(AdminControllers.AccountController).GetMethod("StopImpersonation")!;   // the account used may hold no permission
        Assert.NotNull(stop.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(stop.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(stop.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
        Assert.NotEmpty(type.GetCustomAttributes<AuthorizeAttribute>(true));
        Assert.Contains("/impersonation/start", AdminSecurity.Impersonation.BlockedPaths);
        Assert.Contains("/account/changepassword", AdminSecurity.Impersonation.BlockedPaths);
    }

    // ---- HTTP -----------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task EveryPageShowsTheBarAndEveryRequestIsRecorded()
    {
        var store = new MemoryHistory();
        using var f = Factory(store, impersonating: true);
        var response = await Client(f).GetAsync("/account/accessdenied?from=test");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("ta-imp-bar", html);
        Assert.Contains("Thoát truy cập hộ", html);
        Assert.Contains("Nhập báo cáo giúp Thủy", html);
        Assert.Contains(store.Requests, r => r.Session == Session && r.Method == "GET" && r.Path == "/account/accessdenied?from=test");
    }

    [Fact]
    public async Task TheAccountsPasswordAndANestedImpersonationStayClosed()
    {
        var store = new MemoryHistory();
        using var f = Factory(store, impersonating: true);
        var client = Client(f);
        foreach (var path in new[] { "/account/changepassword", $"/impersonation/start/{Target}" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.EndsWith("/account/accessdenied", response.Headers.Location!.OriginalString);
            Assert.Contains(store.Requests, r => r.Path == path && r.Status == 403);
        }
    }

    [Fact]
    public async Task ANormalSessionIsNeitherMarkedNorRecorded()
    {
        var store = new MemoryHistory();
        using var f = Factory(store, impersonating: false);
        var html = await (await Client(f).GetAsync("/account/accessdenied")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("ta-imp-bar", html);
        Assert.Empty(store.Requests);
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(MemoryHistory store, bool impersonating) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IImpersonationStore>();
                s.AddSingleton<S.IImpersonationStore>(store);
                s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                s.AddSingleton(new Impersonating(impersonating));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed record Impersonating(bool On);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Impersonating impersonating)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Target.ToString()), new(ClaimTypes.Name, "thuytt"), new("display_name", "Trần Thị Thủy"), new(ClaimTypes.Role, "STAFF"), new("permission", "TASKS.VIEW") };
            if (impersonating.On)
            {
                var now = DateTime.UtcNow;
                claims.AddRange(AdminSecurity.Impersonation.Claims(new(Session, Actor, "duc", "Phạm Đức", "STAMP", "Nhập báo cáo giúp Thủy", now, now.AddHours(1))));
            }
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }

    private sealed class MemoryHistory : S.IImpersonationStore
    {
        private readonly object gate = new();
        public List<(Guid Session, string Method, string Path, int Status)> Requests { get; } = [];

        public Task<S.ImpersonationTarget?> FindTargetAsync(Guid userId) => Task.FromResult<S.ImpersonationTarget?>(new(userId, "thuytt", "Trần Thị Thủy"));
        public Task<Guid> StartAsync(Guid actorId, Guid targetId, string reason, DateTime expiresAtUtc, string? ip, string? userAgent) => Task.FromResult(Session);
        public Task EndAsync(Guid sessionId, string endReason) => Task.CompletedTask;
        public Task<bool> IsOpenAsync(Guid sessionId) => Task.FromResult(true);
        public Task LogRequestAsync(Guid sessionId, string method, string path, int statusCode) { lock (gate) Requests.Add((sessionId, method, path, statusCode)); return Task.CompletedTask; }
        public Task<IReadOnlyList<S.ImpersonationSessionRow>> ListAsync(int take = 300) => Task.FromResult<IReadOnlyList<S.ImpersonationSessionRow>>([]);
        public Task<S.ImpersonationSessionDetail?> GetAsync(Guid sessionId) => Task.FromResult<S.ImpersonationSessionDetail?>(null);
    }
}
