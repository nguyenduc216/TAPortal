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
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// UI/UX redesign: shared design system, local assets (no CDN), icons, permission-filtered sidebar, drawer markup.
// No SQL: fake dashboard store. Visual appearance itself is checked by screenshots (Visual namespace), not here.
public sealed class SafeUiDesignSystemTests
{
    private const string TestScheme = "SafeUiTest";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Root = RepoRoot();
    private static readonly string AdminWww = Path.Combine(Root, "src", "TAPortal.Web", "wwwroot");
    private static readonly string CustomerWww = Path.Combine(Root, "src", "TACustomerPortal.Web", "wwwroot");

    // Only the behaviour script is shared; the Customer Portal has its own visual design (customer.css).
    [Theory]
    [InlineData("js/ta-shell.js")]
    public void SharedDesignAssetsAreIdenticalInBothApps(string path)
    {
        var adminFile = Path.Combine(AdminWww, path);
        var customerFile = Path.Combine(CustomerWww, path);
        Assert.True(File.Exists(adminFile), adminFile);
        Assert.True(File.Exists(customerFile), customerFile);
        Assert.Equal(File.ReadAllBytes(adminFile), File.ReadAllBytes(customerFile));
    }

    [Fact]
    public void DesignTokensUseTheApprovedPalette()
    {
        var css = File.ReadAllText(Path.Combine(AdminWww, "css", "ta-design.css"));
        var expected = new Dictionary<string, string>
        {
            ["--ta-navy"] = "#0B2E52", ["--ta-sidebar"] = "#082746", ["--ta-blue"] = "#1677E8", ["--ta-gold"] = "#E5A900",
            ["--ta-bg"] = "#F5F7FB", ["--ta-surface"] = "#FFFFFF", ["--ta-text"] = "#17253D", ["--ta-text-2"] = "#64748B",
            ["--ta-border"] = "#E2E8F0", ["--ta-success"] = "#16803C", ["--ta-warning"] = "#B7791F", ["--ta-danger"] = "#DC3545",
        };
        foreach (var (token, value) in expected)
            Assert.Matches($@"{Regex.Escape(token)}:\s*{value};", css);
    }

    [Fact]
    public async Task LayoutsLoadOnlyLocalAssets()
    {
        using var adminFactory = AdminFactory(null);
        using var customerFactory = CustomerFactory();
        var adminHtml = await Client(adminFactory).GetStringAsync("/account/login");
        var customerHtml = await Client(customerFactory).GetStringAsync("/account/login");
        foreach (var html in new[] { adminHtml, customerHtml })
        {
            Assert.DoesNotContain("cdn.jsdelivr", html);
            Assert.DoesNotMatch("<(link|script)[^>]+(href|src)=\"(https?:)?//", html);
            Assert.Contains("/js/ta-shell.js", html);
        }
        Assert.Contains("/lib/tabler/tabler.min.css", adminHtml);
        Assert.Contains("/css/ta-design.css", adminHtml);
        Assert.Contains("/css/customer.css", customerHtml);
        Assert.DoesNotContain("tabler", customerHtml);                 // own design, not the Admin theme
        Assert.DoesNotContain("ta-design.css", customerHtml);
    }

    [Theory]
    [InlineData(true, "/lib/tabler/tabler.min.css")]
    [InlineData(true, "/lib/tabler/tabler.min.js")]
    [InlineData(true, "/css/ta-design.css")]
    [InlineData(true, "/js/ta-shell.js")]
    [InlineData(true, "/js/ta-portal.js")]
    [InlineData(false, "/css/customer.css")]
    [InlineData(false, "/js/customer.js")]
    [InlineData(false, "/js/ta-shell.js")]
    public async Task VendoredAndSharedAssetsAreServed(bool adminApp, string path)
    {
        using var adminFactory = AdminFactory(null);
        using var customerFactory = CustomerFactory();
        var response = await (adminApp ? Client(adminFactory) : Client(customerFactory)).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void CustomerPortalHasItsOwnGoldSidebarAndBlueDesign()
    {
        var css = File.ReadAllText(Path.Combine(CustomerWww, "css", "customer.css"));
        Assert.Matches(@"--cp-blue-600:\s*#2563EB;", css);              // primary
        Assert.Matches(@"--cp-gold-700:\s*#A16207;", css);              // deep-gold sidebar
        Assert.DoesNotContain("--ta-navy", css);
        Assert.False(File.Exists(Path.Combine(CustomerWww, "css", "ta-design.css")));
        Assert.False(Directory.Exists(Path.Combine(CustomerWww, "lib", "tabler")));
        var layout = File.ReadAllText(Path.Combine(Root, "src", "TACustomerPortal.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("class=\"cp-sidebar\" id=\"taSidebar\"", layout);   // own sidebar, drawer behaviour from ta-shell.js
        Assert.DoesNotContain("class=\"ta-sidebar\"", layout);              // not the Admin sidebar
        Assert.Contains("data-ta-swap", layout);                       // soft navigation container
    }

    [Fact]
    public void AdminShellMarksSoftNavigationRegionsAndHasNoLoadingBar()
    {
        var layout = File.ReadAllText(Path.Combine(Root, "src", "TAPortal.Web", "Views", "Shared", "_Layout.cshtml"));
        foreach (var id in new[] { "taNav", "taCrumbs", "taMain" })
            Assert.Matches($"id=\"{id}\"[^>]*data-ta-swap", layout);
        Assert.DoesNotContain("data-ta-progress", layout);
    }

    [Fact]
    public void EveryIconReferencedByViewsExists()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cshtml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"UiIcon\.Render\(([^,)]*)"))                 // first argument only (second is a CSS class)
                foreach (Match q in Regex.Matches(m.Groups[1].Value, "\"([a-z0-9-]+)\"")) names.Add(q.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"ViewData\[""PageIcon""\]\s*=([^;]*);"))
                foreach (Match q in Regex.Matches(m.Groups[1].Value, "\"([a-z0-9-]+)\"")) names.Add(q.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"(?<!Render)\(""[^""]+"",\s*""([a-z0-9-]+)""\)"))      // ("label", "icon") tuples, not Render(name, css)
                names.Add(m.Groups[1].Value);
        }
        Assert.True(names.Count > 30, $"only {names.Count} icon references found; the scan is broken");
        Assert.All(names, n => Assert.True(UiIcon.Exists(n), $"Icon '{n}' is used by a view but not defined in UiIcon."));
        Assert.Throws<KeyNotFoundException>(() => UiIcon.Render("does-not-exist"));
    }

    [Fact]
    public void IconsAreDecorativeUnlessLabelledAndLabelsAreEncoded()
    {
        var decorative = UiIcon.Render("pencil").ToString()!;
        Assert.Contains("aria-hidden=\"true\"", decorative);
        Assert.Contains("focusable=\"false\"", decorative);
        var labelled = UiIcon.Render("pencil", "x\"y", "<Sửa>").ToString()!;
        Assert.Contains("role=\"img\"", labelled);
        Assert.DoesNotContain("<Sửa>", labelled);
        Assert.DoesNotContain("x\"y", labelled);
    }

    [Fact]
    public void IconOnlyControlsHaveAccessibleNames()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cshtml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "<(button|a)\\b[^>]*class=\"[^\"]*(ta-btn-icon|ta-icon-btn|ta-password-toggle|ta-sidebar__close)[^\"]*\"[^>]*>"))
                if (!m.Value.Contains("aria-label=")) offenders.Add($"{Path.GetFileName(file)}: {m.Value}");
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task SystemAdminSidebarShowsEveryModuleWithIconsAndMarksCurrentPage()
    {
        using var f = AdminFactory(new Identity("SYS_ADMIN", []));
        var html = await Client(f).GetStringAsync("/");
        var nav = Sidebar(html);
        foreach (var href in new[] { "href=\"/\"", "href=\"/Customers\"", "href=\"/Users\"", "href=\"/Roles\"", "href=\"/Menus\"" })
            Assert.Contains(href, nav);
        Assert.Matches("<a class=\"ta-nav__link\"[^>]*aria-current=\"page\"[^>]*href=\"/\">", nav);   // the anchor tag helper writes href last
        Assert.Single(Regex.Matches(nav, "aria-current=\"page\""));
        Assert.DoesNotContain("href=\"/CustomerPortalAccounts\"", nav);  // managed inside each customer workspace
        foreach (Match link in Regex.Matches(nav, "<(a|span) class=\"ta-nav__link[^\"]*\"[^>]*>.*?</(a|span)></li>", RegexOptions.Singleline))
            Assert.Contains("<svg class=\"ta-icon\"", link.Value);       // no menu item without an icon
        Assert.Equal(2, Regex.Matches(nav, "aria-disabled=\"true\"").Count); // unimplemented modules stay visibly disabled
        Assert.Contains("data-ta-drawer-close", html);                       // mobile close button + overlay
        Assert.Contains("href=\"#taMain\"", html);                           // skip link
    }

    [Fact]
    public async Task SidebarOnlyShowsModulesTheUserMayOpen()
    {
        using var f = AdminFactory(new Identity("OWNER", ["AUTH.ROLES.VIEW"]));
        var nav = Sidebar(await Client(f).GetStringAsync("/account/accessdenied"));
        Assert.Contains("href=\"/Roles\"", nav);
        foreach (var hidden in new[] { "href=\"/Customers\"", "href=\"/CustomerPortalAccounts\"", "href=\"/Users\"", "href=\"/Menus\"" })
            Assert.DoesNotContain(hidden, nav);
        Assert.DoesNotContain("Quản lý khách hàng", nav);                    // empty group heading is not rendered
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static string Sidebar(string html)
    {
        var start = html.IndexOf("<aside class=\"ta-sidebar\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "sidebar not rendered");
        return html[start..html.IndexOf("</aside>", start, StringComparison.Ordinal)];
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("TAPortal.sln not found above the test output folder.");
    }

    private static HttpClient Client<T>(WebApplicationFactory<T> f) where T : class =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> AdminFactory(Identity? identity) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.IDashboardStore>();
                s.AddSingleton<AdminServices.IDashboardStore>(new FakeDashboard());
                if (identity is null) return;
                s.AddSingleton(identity);
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private static WebApplicationFactory<Program> CustomerFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));

    private sealed class FakeDashboard : AdminServices.IDashboardStore
    {
        public Task<AdminServices.DashboardSnapshot> LoadAsync() => Task.FromResult(new AdminServices.DashboardSnapshot(
            DateTimeOffset.UnixEpoch, new(1, 0, 0, 0), new(1, 0), null, 1, false, null));
    }

    private sealed record Identity(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Identity identity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-ui-test"), new("display_name", "Safe UI"), new(ClaimTypes.Role, identity.Role) };
            claims.AddRange(identity.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
