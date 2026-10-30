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
using TAPortal.Shared;
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Customer Workspace (per-customer Portal accounts). No SQL: in-memory customer store and portal-account seam.
// The SYS_ADMIN re-check, audit and UPDATE-or-INSERT inside MembershipAdminStore are unchanged and not exercised here.
public sealed class SafeCustomerWorkspaceTests
{
    private const string TestScheme = "SafeWorkspaceTest";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Inactive = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-1111-0000-0000-000000000002");
    private static readonly Guid FreeUser = Guid.Parse("dddddddd-1111-0000-0000-000000000004");
    private static readonly Guid RevokedUser = Guid.Parse("eeeeeeee-1111-0000-0000-000000000005");
    private static readonly Guid StaffUser = Guid.Parse("ffffffff-1111-0000-0000-000000000006");

    [Fact]
    public async Task SidebarNoLongerOffersStandalonePortalAccountsMenu()
    {
        using var f = Factory("SYS_ADMIN", out _);
        var html = await Client(f).GetStringAsync($"/customers/details/{A}");
        var nav = html[html.IndexOf("<aside class=\"ta-sidebar\"", StringComparison.Ordinal)..html.IndexOf("</aside>", StringComparison.Ordinal)];
        Assert.DoesNotContain("CustomerPortalAccounts", nav);
        Assert.DoesNotContain("Tài khoản Customer Portal", WebUtility.HtmlDecode(nav));
        Assert.Matches("<a class=\"ta-nav__link\"[^>]*aria-current=\"page\"[^>]*href=\"/Customers\">", nav);   // workspace keeps "Khách hàng" active
    }

    [Fact]
    public async Task WorkspaceShowsCustomerHeaderTabsAndRealPortalCounts()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/customers/details/{A}"));
        Assert.Contains("Công ty A", html);
        Assert.Contains("<span class=\"ta-code\" title=\"Mã khách hàng\">KH-A</span>", html);
        foreach (var (tab, href) in new[] { ("overview", $"/Customers/Details/{A}"), ("info", $"/Customers/Edit/{A}"), ("portal", $"/Customers/PortalAccounts/{A}") })
            Assert.Matches($"data-ta-tab=\"{tab}\"[^>]*href=\"{Regex.Escape(href)}\"|href=\"{Regex.Escape(href)}\"[^>]*data-ta-tab=\"{tab}\"", html);
        Assert.Matches("aria-current=\"page\"[^>]*data-ta-tab=\"overview\"", html);
        Assert.Contains("data-ta-metric=\"total\">2<", html);       // UserA (active) + revoked row of customer A
        Assert.Contains("data-ta-metric=\"effective\">1<", html);
        Assert.Contains("data-ta-metric=\"revoked\">1<", html);
        Assert.Equal([A], portal.Listed.Distinct());
    }

    [Fact]
    public async Task PortalTabListsOnlyThisCustomersMembershipsAndSeparatesAssignFromCreate()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/customers/portalaccounts/{A}"));
        Assert.Contains("User A", html);
        Assert.DoesNotContain("User B", html);                     // B's membership never reaches A's workspace
        Assert.Contains("data-bs-target=\"#assignAccount\"", html);
        Assert.Contains("data-bs-target=\"#createAccount\"", html);
        Assert.Contains("name=\"mode\" value=\"assign\"", html);
        Assert.Contains("name=\"mode\" value=\"create\"", html);
        Assert.Matches("<option value=\"" + UserA + "\" disabled=\"disabled\">[^<]*đã có quyền", html);
        Assert.DoesNotMatch("name=\"CustomerId\"", html);          // customer context comes from the route only
        Assert.Equal([A], portal.Listed.Distinct());
    }

    [Fact]
    public async Task RoleChoicesShowNamesOnlyWhileSubmittingCodes()
    {
        using var f = Factory("SYS_ADMIN", out _);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/customers/portalaccounts/{A}"));
        Assert.Contains("value=\"VIEWER\">Chỉ xem</option>", html);
        Assert.Contains("value=\"ADMIN\">Quản trị doanh nghiệp</option>", html);
        Assert.Matches("selected=\"selected\" value=\"OWNER\">Chủ sở hữu</option>", html);
        Assert.DoesNotMatch(">(OWNER|ADMIN|VIEWER)( —[^<]*)?</option>", html);
    }

    [Fact]
    public void EveryTextFieldInBothAppsHasALeftIcon()
    {
        var src = Path.Combine(AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)], "src");
        var missing = new List<string>();
        foreach (var file in new[] { "TAPortal.Web", "TACustomerPortal.Web" }.SelectMany(app => Directory.EnumerateFiles(Path.Combine(src, app, "Views"), "*.cshtml", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "<(input|textarea)\\b[^>]*class=\"(form-control|cp-input)[^\"]*\"[^>]*>"))
            {
                if (Regex.IsMatch(m.Value, "type=\"(hidden|search|checkbox)\"")) continue;
                var before = text[Math.Max(0, m.Index - 120)..m.Index];
                if (!Regex.IsMatch(before, "<div class=\"(ta-input-icon|cp-input-wrap|cp-search)[^\"]*\">\\s*@UiIcon\\.Render\\(\"[a-z0-9-]+\"\\)\\s*$")) missing.Add($"{Path.GetFileName(file)}: {m.Value[..Math.Min(80, m.Value.Length)]}");
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public async Task EmptyAndInactiveCustomersShowExplicitStates()
    {
        using var f = Factory("SYS_ADMIN", out _);
        var c = Client(f);
        var inactive = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/portalaccounts/{Inactive}"));
        Assert.Contains("data-ta-portal-empty", inactive);            // no memberships: explicit empty state
        Assert.Contains("Khách hàng đang ngừng hoạt động", inactive);
        Assert.Matches("data-bs-target=\"#assignAccount\" disabled=\"disabled\"", inactive);
        Assert.DoesNotContain("id=\"assignAccount\"", inactive);
    }

    [Fact]
    public async Task UnknownCustomerIs404AndNonAdminIs403()
    {
        using var f = Factory("SYS_ADMIN", out _);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).GetAsync($"/customers/details/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).GetAsync($"/customers/portalaccounts/{Guid.NewGuid()}")).StatusCode);
        using var owner = Factory("OWNER", out var portal, "CRM.CUSTOMERS.UPDATE", "CUSTOMERS.PORTAL.MANAGE");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(owner).GetAsync($"/customers/details/{A}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(owner).GetAsync($"/customers/portalaccounts/{A}")).StatusCode);
        Assert.Empty(portal.Listed);
    }

    [Fact]
    public async Task PostedCustomerIdThatDiffersFromRouteIsRejected()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var token = await Token(c, $"/customers/portalaccounts/{A}");
        var save = await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "assign", ["UserId"] = FreeUser.ToString(), ["PortalRole"] = "VIEWER", ["CustomerId"] = B.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        var revoke = await Post(c, $"/customers/setportalaccountactive/{A}", token, new() { ["userId"] = UserB.ToString(), ["active"] = "false", ["customerId"] = B.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, revoke.StatusCode);
        Assert.Empty(portal.Saved);
        Assert.Empty(portal.Toggled);
    }

    [Fact]
    public async Task RevokingAMembershipOfAnotherCustomerFromThisWorkspaceDoesNothing()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var response = await Post(c, $"/customers/setportalaccountactive/{A}", await Token(c, $"/customers/portalaccounts/{A}"), new() { ["userId"] = UserB.ToString(), ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Customers/PortalAccounts/{A}", response.Headers.Location!.OriginalString);
        Assert.Empty(portal.Toggled);                               // UserB belongs to customer B only
    }

    [Fact]
    public async Task AssignCreateAndRevokeAreScopedToTheRouteCustomerAndReturnToItsTab()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var token = await Token(c, $"/customers/portalaccounts/{A}");
        var assign = await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "assign", ["UserId"] = FreeUser.ToString(), ["PortalRole"] = "ADMIN" });
        Assert.Equal($"/Customers/PortalAccounts/{A}", assign.Headers.Location!.OriginalString);
        var create = await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "create", ["Username"] = "new.portal", ["DisplayName"] = "New Portal", ["Password"] = "Portal-Pass!123", ["PortalRole"] = "VIEWER" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var revoke = await Post(c, $"/customers/setportalaccountactive/{A}", token, new() { ["userId"] = UserA.ToString(), ["active"] = "false" });
        Assert.Equal($"/Customers/PortalAccounts/{A}", revoke.Headers.Location!.OriginalString);

        Assert.Equal(2, portal.Saved.Count);
        Assert.All(portal.Saved, s => Assert.Equal(A, s.Input.CustomerId));
        Assert.Equal((FreeUser, "ADMIN", false), (portal.Saved[0].Input.UserId!.Value, portal.Saved[0].Input.PortalRole, portal.Saved[0].Hash is not null));
        Assert.Null(portal.Saved[1].Input.UserId);
        Assert.StartsWith("PBKDF2$", portal.Saved[1].Hash);        // password hashed, never stored as given
        Assert.Equal([(UserA, A, false)], portal.Toggled);
    }

    [Theory]
    [InlineData("assign", "", "VIEWER")]                         // assign requires an existing user
    [InlineData("create", "", "VIEWER")]                         // create requires username, name and a valid password
    [InlineData("assign", "dddddddd-1111-0000-0000-000000000004", "SYS_ADMIN")] // only OWNER/ADMIN/VIEWER; never a TAPortal role
    public async Task InvalidRequestsDoNotReachTheStore(string mode, string userId, string role)
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var data = new Dictionary<string, string> { ["mode"] = mode, ["PortalRole"] = role };
        if (userId.Length > 0) data["UserId"] = userId;
        var response = await Post(c, $"/customers/saveportalaccount/{A}", await Token(c, $"/customers/portalaccounts/{A}"), data);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(portal.Saved);
    }

    [Fact]
    public async Task UnknownModeOrUserIdOnCreateIsRejected()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var token = await Token(c, $"/customers/portalaccounts/{A}");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["UserId"] = FreeUser.ToString(), ["PortalRole"] = "VIEWER" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "create", ["UserId"] = FreeUser.ToString(), ["Username"] = "x", ["DisplayName"] = "x", ["Password"] = "Portal-Pass!123" })).StatusCode);
        Assert.Empty(portal.Saved);
    }

    [Fact]
    public async Task WorkspacePostsRequireAntiforgery()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var response = await c.PostAsync($"/customers/setportalaccountactive/{A}", new FormUrlEncodedContent(new Dictionary<string, string> { ["userId"] = UserA.ToString(), ["active"] = "false" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(portal.Toggled);
    }

    [Fact]
    public async Task ReactivationIsRefusedForAnInactiveCustomerButRevokeIsNotBlockedByStatus()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var token = await Token(c, $"/customers/portalaccounts/{Inactive}");
        var response = await Post(c, $"/customers/setportalaccountactive/{Inactive}", token, new() { ["userId"] = UserA.ToString(), ["active"] = "true" });
        Assert.Equal($"/Customers/PortalAccounts/{Inactive}", response.Headers.Location!.OriginalString);
        Assert.Empty(portal.Toggled);
        Assert.Empty(portal.Listed.Where(id => id != Inactive));
        var revoke = await Post(c, $"/customers/setportalaccountactive/{A}", token, new() { ["userId"] = UserA.ToString(), ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);
        Assert.Equal([(UserA, A, false)], portal.Toggled);
    }

    [Fact]
    public async Task LegacyRouteKeepsCustomerContextAndOverviewMovedToCustomersTab()
    {
        using var f = Factory("SYS_ADMIN", out _);
        var response = await Client(f).GetAsync($"/CustomerPortalAccounts?customerId={A}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Customers/PortalAccounts/{A}", response.Headers.Location!.OriginalString);
        var overview = await Client(f).GetAsync("/CustomerPortalAccounts");
        Assert.Equal("/Customers/Accounts", overview.Headers.Location!.OriginalString);
        // The old cross-customer write endpoints are gone (management happens only in the customer workspace).
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).PostAsync("/CustomerPortalAccounts/Save", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
    }

    [Fact]
    public async Task CustomersAccountsTabListsEveryCustomersAccountsWithLinksToTheirWorkspace()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/customers/accounts"));
        Assert.Matches("aria-current=\"page\"[^>]*>.*?Tài khoản Portal|data-ta-tab=\"accounts\" aria-current=\"page\"", html);
        Assert.Contains("User A", html);
        Assert.Contains("User B", html);                              // cross-customer overview
        Assert.Contains($"href=\"/Customers/PortalAccounts/{B}\"", html);
        Assert.Contains("data-ta-pager=\"10\"", html);
        Assert.Contains("Chủ sở hữu", html);
        Assert.DoesNotContain("<form", html[html.IndexOf("allAccountsTable", StringComparison.Ordinal)..].Split("</table>")[0]);   // read-only
        Assert.True(portal.ListedAll);
    }

    [Fact]
    public void StaffDefinitionIsAnyTaPortalRole()
    {
        var users = new List<AdminModels.PortalUser> { new(UserA, "portal", "Portal", null, null, "ACTIVE", true), new(StaffUser, "staff.ta", "Staff", null, null, "ACTIVE", true), new(FreeUser, "norole", "No role", null, null, "ACTIVE", true) };
        var map = new Dictionary<Guid, List<string>> { [StaffUser] = ["Biên tập viên"], [FreeUser] = [] };
        Assert.Equal([UserA, FreeUser], AdminServices.PortalCandidates.Filter(users, map).Select(u => u.Id));
    }

    [Fact]
    public async Task StaffAccountsAreNotOfferedAndCannotBeGrantedButExistingMembershipsStayManageable()
    {
        using var f = Factory("SYS_ADMIN", out var portal);
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/portalaccounts/{A}"));
        Assert.DoesNotContain("staff.ta", page);                       // TA staff account not in the "existing account" list
        var token = await Token(c, $"/customers/portalaccounts/{A}");
        var staff = await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "assign", ["UserId"] = StaffUser.ToString(), ["PortalRole"] = "VIEWER" });
        Assert.Equal(HttpStatusCode.Redirect, staff.StatusCode);
        Assert.Empty(portal.Saved);
        var member = await Post(c, $"/customers/saveportalaccount/{A}", token, new() { ["mode"] = "assign", ["UserId"] = RevokedUser.ToString(), ["PortalRole"] = "ADMIN" });
        Assert.Equal(HttpStatusCode.Redirect, member.StatusCode);
        Assert.Single(portal.Saved);                                   // role change of an existing membership still works
    }

    [Fact]
    public async Task EditTabRendersInsideWorkspaceAndSaveReturnsToIt()
    {
        using var f = Factory("SYS_ADMIN", out _);
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/edit/{A}"));
        Assert.Matches("aria-current=\"page\"[^>]*data-ta-tab=\"info\"", html);
        Assert.Contains("readonly disabled", html);                   // customer code stays read-only
        var save = await Post(c, $"/customers/edit/{A}", await Token(c, $"/customers/edit/{A}"), new() { ["Name"] = "Công ty A mới", ["TaxCode"] = "0101234567", ["IsActive"] = "true" });
        Assert.Equal($"/Customers/Edit/{A}", save.Headers.Location!.OriginalString);
        var invalid = await Post(c, $"/customers/edit/{A}", await Token(c, $"/customers/edit/{A}"), new() { ["Name"] = "Tên khác", ["TaxCode"] = "123", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var body = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Contains("10 chữ số", body);
        Assert.Contains("<h1 class=\"ta-page-title\">Công ty A mới</h1>", body);   // header shows the stored name, not the rejected input
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(string role, out FakePortal portal, params string[] permissions)
    {
        var fake = portal = new FakePortal();
        return new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.ICustomerAdminStore>();
                s.AddSingleton<AdminServices.ICustomerAdminStore>(new FakeCustomers());
                s.RemoveAll<AdminServices.ICustomerPortalAccounts>();
                s.AddSingleton<AdminServices.ICustomerPortalAccounts>(fake);
                s.AddSingleton(new Identity(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    private sealed class FakeCustomers : AdminServices.ICustomerAdminStore
    {
        private readonly Dictionary<Guid, AdminModels.CustomerEditVm> rows = new()
        {
            [A] = new() { Id = A, Code = "KH-A", Name = "Công ty A", TaxCode = "0101234567", Status = "ACTIVE", IsActive = true },
            [B] = new() { Id = B, Code = "KH-B", Name = "Công ty B", Status = "ACTIVE", IsActive = true },
            [Inactive] = new() { Id = Inactive, Code = "KH-C", Name = "Công ty C", Status = "ACTIVE", IsActive = false },
        };
        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid customerId) => Task.FromResult(rows.TryGetValue(customerId, out var c)
            ? new AdminModels.CustomerEditVm { Id = c.Id, Code = c.Code, Name = c.Name, TaxCode = c.TaxCode, Status = c.Status, IsActive = c.IsActive } : null);
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new InvalidOperationException("not used");
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actorId)
        {
            var tax = AdminServices.TaxCodePolicy.Normalize(vm.TaxCode);
            if (!AdminServices.TaxCodePolicy.IsValid(tax)) return Task.FromResult(new AdminServices.CustomerChangeResult(AdminServices.CustomerChangeOutcome.InvalidTaxCode));
            rows[vm.Id] = new() { Id = vm.Id, Code = vm.Code, Name = vm.Name, TaxCode = tax, Status = vm.Status, IsActive = vm.IsActive };
            return Task.FromResult(new AdminServices.CustomerChangeResult(AdminServices.CustomerChangeOutcome.Saved));
        }
    }

    public sealed class FakePortal : AdminServices.ICustomerPortalAccounts
    {
        private readonly List<MembershipAdminRow> rows =
        [
            new(UserA, A, "User A", "user.a", "a@a.example", "Công ty A", "OWNER", true, "ACTIVE", null),
            new(RevokedUser, A, "Revoked", "revoked", null, "Công ty A", "VIEWER", false, "ACTIVE", null),
            new(UserB, B, "User B", "user.b", null, "Công ty B", "ADMIN", true, "ACTIVE", null),
        ];
        public List<Guid> Listed { get; } = [];
        public bool ListedAll { get; private set; }
        public List<(MembershipInput Input, string? Hash)> Saved { get; } = [];
        public List<(Guid User, Guid Customer, bool Active)> Toggled { get; } = [];

        public Task<List<MembershipAdminRow>> ListAsync(Guid actor, Guid customerId)
        {
            Listed.Add(customerId);
            return Task.FromResult(rows.Where(r => r.CustomerId == customerId).ToList());
        }
        public Task<List<MembershipAdminRow>> ListAllAsync(Guid actor) { ListedAll = true; return Task.FromResult(rows.ToList()); }
        public Task<List<AdminModels.CustomerRow>> CustomersAsync() => Task.FromResult(new List<AdminModels.CustomerRow>
        {
            new(A, "KH-A", "Công ty A", null, null, null, "ACTIVE", true), new(B, "KH-B", "Công ty B", null, null, null, "ACTIVE", true),
        });
        public Task SaveAsync(MembershipInput input, Guid actor, string? passwordHash) { Saved.Add((input, passwordHash)); return Task.CompletedTask; }
        public Task SetActiveAsync(Guid userId, Guid customerId, bool active, Guid actor) { Toggled.Add((userId, customerId, active)); return Task.CompletedTask; }
        // Candidates exclude TA staff (StaffUser holds a TAPortal role in the real store).
        public Task<List<AdminModels.PortalUser>> CandidateUsersAsync() => Task.FromResult(new List<AdminModels.PortalUser>
        {
            new(UserA, "user.a", "User A", null, null, "ACTIVE", true),
            new(FreeUser, "free", "Free User", null, null, "ACTIVE", true),
        });
    }

    private sealed record Identity(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Identity identity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-workspace"), new("display_name", "Safe Workspace"), new(ClaimTypes.Role, identity.Role) };
            claims.AddRange(identity.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
