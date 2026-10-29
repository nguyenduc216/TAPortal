extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Checkpoint 8 (AUD-18 dashboard, AUD-26 direct permissions, AUD-30..34 UX). No SQL: fake stores.
// The SQL aggregates themselves are NOT verified against TAPortalSys; responsive behaviour is BROWSER_NOT_VERIFIED.
public sealed class SafeAdminDashboardUxTests
{
    private const string TestScheme = "SafeDashboardTest";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- KPI definitions ------------------------------------------------------------------------------
    [Fact]
    public void AccountBucketsPartitionEveryStateExactlyOnceAndMatchLoginRule()
    {
        foreach (var active in new[] { true, false })
        foreach (var status in new[] { "ACTIVE", "LOCKED", null })
        foreach (var deleted in new[] { true, false })
        {
            var bucket = AdminServices.DashboardMetrics.Classify(active, status, deleted);
            Assert.Equal(AdminServices.AccountStatusPolicy.IsLoginEligible(active, status, deleted), bucket == AdminServices.AccountBucket.Active);
            Assert.Equal(deleted, bucket == AdminServices.AccountBucket.Deleted);
        }
    }

    [Fact]
    public void CurrentUserTotalExcludesSoftDeletedAndEqualsSumOfBuckets()
    {
        var rows = new (bool Active, string Status, bool Deleted)[] { (true, "ACTIVE", false), (true, "ACTIVE", false), (false, "ACTIVE", false), (true, "LOCKED", false), (true, "ACTIVE", true), (false, "ACTIVE", true) };
        var b = rows.Select(r => AdminServices.DashboardMetrics.Classify(r.Active, r.Status, r.Deleted)).ToList();
        var kpi = new AdminServices.UserKpis(b.Count(x => x == AdminServices.AccountBucket.Active), b.Count(x => x == AdminServices.AccountBucket.Disabled), b.Count(x => x == AdminServices.AccountBucket.Locked), b.Count(x => x == AdminServices.AccountBucket.Deleted));
        Assert.Equal((2, 1, 1, 2), (kpi.Active, kpi.Disabled, kpi.Locked, kpi.SoftDeleted));
        Assert.Equal(rows.Count(r => !r.Deleted), kpi.CurrentTotal);
    }

    [Fact]
    public void KpiSqlAggregatesSingleTablesOrOneRowPerMembership()
    {
        // Structural guard against double counting: user/customer KPIs never JOIN; membership KPI counts one row per
        // membership and DISTINCT users. (Execution against SQL Server is NOT verified here.)
        Assert.DoesNotContain("JOIN", AdminServices.DashboardSql.Users);
        Assert.DoesNotContain("JOIN", AdminServices.DashboardSql.Customers);
        Assert.Contains("COUNT(DISTINCT", AdminServices.DashboardSql.Memberships);
        Assert.DoesNotContain("OldValues", AdminServices.DashboardSql.RecentAudit);
    }

    [Theory]
    [InlineData(true, "ACTIVE", true, "ACTIVE", true, null)]
    [InlineData(false, "ACTIVE", true, "ACTIVE", false, null)]
    [InlineData(true, "LOCKED", true, "ACTIVE", false, "vô hiệu hóa")]
    [InlineData(true, "DELETED", true, "ACTIVE", false, "đã xóa")]
    [InlineData(true, "SUSPENDED", true, "ACTIVE", false, "bị khóa (SUSPENDED)")]
    [InlineData(true, "ACTIVE", false, "ACTIVE", false, "doanh nghiệp ngừng hoạt động")]
    [InlineData(true, "ACTIVE", true, "CLOSED", false, "doanh nghiệp ngừng hoạt động")]
    public void MembershipEffectMatchesCustomerPortalLoginConditions(bool membershipActive, string userStatus, bool customerActive, string customerStatus, bool effective, string? reason)
    {
        var customer = new AdminModels.CustomerRow(Guid.NewGuid(), "KH", "Công ty", null, null, null, customerStatus, customerActive);
        var result = AdminServices.MembershipDisplay.Evaluate(membershipActive, userStatus, customer);
        Assert.Equal(effective, result.Effective);
        if (reason is null) Assert.Null(result.Reason); else Assert.Contains(reason, result.Reason);
        Assert.Contains("không còn tồn tại", AdminServices.MembershipDisplay.Evaluate(true, "ACTIVE", null).Reason);
    }

    // ---- Dashboard HTTP --------------------------------------------------------------------------------
    [Fact]
    public async Task DashboardShowsRealCountsWithDefinitionsAndNoStaticConnectionBadge()
    {
        using var f = Factory("SYS_ADMIN", new FakeDashboard(Sample()));
        var response = await Client(f).GetAsync("/");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("Người dùng hiện hành", html);
        Assert.Matches(@"ta-stat-value"">1\.239<", html);          // 1234+3+2, vi-VN thousands separator
        Assert.Contains("Bị khóa theo trạng thái: <strong>2</strong>", html);
        Assert.Contains("Tài khoản đã xóa mềm (lịch sử): <strong>7</strong>", html);
        Assert.Contains("Tài khoản có quyền: <strong>4</strong>", html);
        Assert.Contains("data-ta-db-status=\"up\"", html);
        Assert.DoesNotContain(">Kết nối<", html);                    // old hard-coded badge
        Assert.Contains("Cập nhật vai trò người dùng", html);
        Assert.Contains("&lt;script&gt;", await response.Content.ReadAsStringAsync()); // actor name is HTML-encoded
    }

    [Fact]
    public async Task DashboardFailureShowsErrorStateWithoutDetailsOrFakeNumbers()
    {
        using var f = Factory("SYS_ADMIN", new FakeDashboard(null));
        var response = await Client(f).GetAsync("/");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-ta-dashboard-error", html);
        Assert.Contains("data-ta-db-status=\"down\"", html);
        Assert.DoesNotContain("SECRET-SQL-DETAIL", html);
        Assert.DoesNotContain("ta-stat-value", html);
    }

    [Theory]
    [InlineData("memberships-missing", "Chưa có dữ liệu (bảng membership chưa sẵn sàng)")]
    [InlineData("audit-missing", "Nhật ký audit chưa sẵn sàng")]
    [InlineData("audit-unreadable", "chỉ có quyền ghi")]
    [InlineData("audit-empty", "Chưa có thay đổi nào được ghi nhận")]
    public async Task DashboardEmptyStatesAreExplicit(string kind, string expected)
    {
        var s = Sample();
        s = kind switch
        {
            "memberships-missing" => s with { Memberships = null },
            "audit-missing" => s with { AuditTableVisible = false, RecentAudit = null },
            "audit-unreadable" => s with { RecentAudit = null },
            _ => s with { RecentAudit = [] }
        };
        using var f = Factory("SYS_ADMIN", new FakeDashboard(s));
        Assert.Contains(expected, WebUtility.HtmlDecode(await Client(f).GetStringAsync("/")));
    }

    [Fact]
    public async Task DashboardKpiLinksPointToExistingProtectedRoutes()
    {
        using var f = Factory("SYS_ADMIN", new FakeDashboard(Sample()));
        var html = await Client(f).GetStringAsync("/");
        var links = Regex.Matches(html, "<a [^>]*data-ta-kpi=\"[^\"]+\"[^>]*>").Select(m => Regex.Match(m.Value, "href=\"([^\"]+)\"").Groups[1].Value).ToList();
        Assert.All(links, l => Assert.StartsWith("/", l));
        Assert.Equal(4, links.Count);
        using var anonymous = Factory(null, new FakeDashboard(Sample()));
        foreach (var link in links)
        {
            var response = await Client(anonymous).GetAsync(link);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // route exists and is protected (not 404)
            Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task DashboardIsSystemAdminOnly()
    {
        using var f = Factory("OWNER", new FakeDashboard(Sample()), permissions: ["CUSTOMERS.VIEW", "AUTH.USERS.VIEW"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(f).GetAsync("/")).StatusCode);
    }

    // ---- Layout / accessibility ------------------------------------------------------------------------
    [Fact]
    public async Task SidebarHasMobileToggleLandmarkAndDecorativeIconsHidden()
    {
        using var f = Factory("SYS_ADMIN", new FakeDashboard(Sample()));
        var html = await Client(f).GetStringAsync("/");
        Assert.Contains("class=\"navbar-toggler\"", html);
        Assert.Contains("aria-controls=\"taSidebarMenu\"", html);
        Assert.Matches("<nav class=\"collapse navbar-collapse\" id=\"taSidebarMenu\" aria-label=\"[^\"]+\">", html);
        Assert.DoesNotMatch("<i class=\"ti [^\"]+\"></i>", html);   // every icon is aria-hidden
        Assert.Matches("data-bs-toggle=\"dropdown\" role=\"button\"[^>]*aria-label=\"Tài khoản", WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData(null, "/Account/Login", "Đăng nhập")]
    [InlineData("OWNER", "/Account/Profile", "Về hồ sơ cá nhân")]
    [InlineData("SYS_ADMIN", "/", "Về tổng quan")]
    public async Task AccessDeniedOffersAReachableDestination(string? role, string href, string label)
    {
        using var f = Factory(role, new FakeDashboard(Sample()));
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/account/accessdenied"));
        Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains(label, html);
    }

    // ---- Role screen (AUD-26 / AUD-33) -----------------------------------------------------------------
    [Fact]
    public async Task RoleScreenShowsDirectPermissionsReadOnlyAndLastAdminWarning()
    {
        var snapshot = new AdminServices.UserRoleSnapshot(Actor, "admin", "Admin", true, "ACTIVE", [new(Guid.NewGuid(), "SYS_ADMIN", "Quản trị", true)], new HashSet<Guid>(), new HashSet<Guid> { Actor },
            [new("AUTH.USERS.DELETE", "Xóa người dùng", "DENY"), new("CRM.CUSTOMERS.VIEW", "Xem khách hàng", "ALLOW")]);
        using var f = Factory("SYS_ADMIN", new FakeDashboard(Sample()), roles: new FakeRoles(snapshot));
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/users/roles/{Actor}"));
        Assert.Contains("data-ta-last-admin", html);
        Assert.Contains("AUTH.USERS.DELETE", html);
        Assert.Contains("Từ chối (ghi đè vai trò)", html);
        Assert.Contains("Cho phép", html);
        var section = html[html.IndexOf("data-ta-direct-permissions", StringComparison.Ordinal)..];
        Assert.DoesNotContain("<input", section);                    // read-only: no form fields for permissions
    }

    [Fact]
    public async Task EditHidesDeleteLinkForOwnAccountOnly()
    {
        var accounts = new FakeAccounts();
        using var f = Factory("SYS_ADMIN", new FakeDashboard(Sample()), accounts: accounts);
        using var c = Client(f);
        Assert.DoesNotContain("data-ta-delete-link", await c.GetStringAsync($"/users/edit/{Actor}"));
        Assert.Contains("data-ta-delete-link", await c.GetStringAsync($"/users/edit/{Other}"));
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static AdminServices.DashboardSnapshot Sample() => new(
        new DateTimeOffset(2026, 10, 29, 3, 0, 0, TimeSpan.Zero), new(1234, 3, 2, 7), new(10, 2), new(5, 4, 1), 3, true,
        [new(new DateTime(2026, 10, 29, 2, 0, 0), "USER_ROLES_UPDATED", "<script>alert(1)</script>")]);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string? role, FakeDashboard dashboard, string[]? permissions = null, FakeRoles? roles = null, FakeAccounts? accounts = null) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.IDashboardStore>();
                s.AddSingleton<AdminServices.IDashboardStore>(dashboard);
                if (roles != null) { s.RemoveAll<AdminServices.IUserRoleStore>(); s.AddSingleton<AdminServices.IUserRoleStore>(roles); }
                if (accounts != null) { s.RemoveAll<AdminServices.IUserAccountStore>(); s.AddSingleton<AdminServices.IUserAccountStore>(accounts); }
                if (role is null) return;
                s.AddSingleton(new TestPrincipal(role, permissions ?? []));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed class FakeDashboard(AdminServices.DashboardSnapshot? snapshot) : AdminServices.IDashboardStore
    {
        public Task<AdminServices.DashboardSnapshot> LoadAsync() =>
            snapshot is null ? throw new InvalidOperationException("SECRET-SQL-DETAIL: server=prod;password=x") : Task.FromResult(snapshot);
    }

    private sealed class FakeRoles(AdminServices.UserRoleSnapshot snapshot) : AdminServices.IUserRoleStore
    {
        public Task<AdminServices.UserRoleSnapshot?> GetAsync(Guid userId) => Task.FromResult<AdminServices.UserRoleSnapshot?>(userId == snapshot.UserId ? snapshot : null);
        public Task<AdminServices.RoleChangeResult> UpdateAsync(Guid actorId, Guid userId, IReadOnlyCollection<Guid> requestedRoleIds, string? expectedStamp, string? ipAddress) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeAccounts : AdminServices.IUserAccountStore
    {
        public Task<AdminServices.UserAccountSnapshot?> GetAsync(Guid userId) =>
            Task.FromResult<AdminServices.UserAccountSnapshot?>(new(userId, "u" + userId.ToString()[..4], "User", null, null, true, "ACTIVE", [], new HashSet<Guid> { Actor, Other }, 0));
        public Task<AdminServices.AccountChangeResult> UpdateAsync(Guid actorId, AdminModels.UserEditVm vm, string? expectedStamp, string? ipAddress) => throw new InvalidOperationException("not used");
        public Task<AdminServices.AccountChangeResult> SoftDeleteAsync(Guid actorId, Guid userId, string? expectedStamp, string? ipAddress) => throw new InvalidOperationException("not used");
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-dashboard-test"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
