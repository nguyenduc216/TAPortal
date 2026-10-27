extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// No SQL host, connection, schema, seed or rollback. Exercise real HTTP/cookies/CSRF/views.
public sealed class SafePortalTests : IDisposable
{
    private const string Password = "Memory-only!123";
    private readonly MemoryStore store = new();
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    public SafePortalTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "INVALID: tests must never use SQL",
                ["DataProtection:KeyPath"] = ""
            }))
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<ICustomerStore>();
                s.AddSingleton<ICustomerStore>(store);
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
            }));
        client = NewClient();
    }
    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    public void Dispose() { client.Dispose(); factory.Dispose(); }
    private static async Task<string> Token(HttpClient c, string path)
    {
        var response = await c.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private async Task<HttpResponseMessage> Post(string path, Dictionary<string, string> data, string tokenPath, HttpClient? target = null)
    {
        var c = target ?? client;
        data["__RequestVerificationToken"] = await Token(c, tokenPath);
        return await c.PostAsync(path, new FormUrlEncodedContent(data));
    }
    private Task<HttpResponseMessage> Login(HttpClient? target = null, string password = Password) => Post("/account/login", new() { ["Username"] = "customer", ["Password"] = password }, "/account/login", target);
    private static void LoginRedirect(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/account/login", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task SingleMembershipDashboardProfilesAndLogout()
    {
        var login = await Login();
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        var cookie = login.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("TA.Customer.Auth="));
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.Equal(1, store.LoginTouches);
        foreach (var path in new[] { "/", "/profile/company" })
        {
            var response = await client.GetAsync(path + "?customerId=" + store.B);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Company Alpha", html);
            Assert.DoesNotContain("Company Beta", html);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        Assert.Contains("Customer Person", WebUtility.HtmlDecode(await client.GetStringAsync("/profile/personal")));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/account/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("/account/logout", new(), "/")).StatusCode);
        LoginRedirect(await client.GetAsync("/"));
    }

    [Fact]
    public async Task MultipleMembershipsRequireSelectionAndCannotSelectForeignTenant()
    {
        store.GrantB = true;
        Assert.Equal("/account/select-customer", (await Login()).Headers.Location!.OriginalString);
        Assert.Equal("/account/select-customer", (await client.GetAsync("/")).Headers.Location!.OriginalString);
        var choices = await client.GetStringAsync("/account/select-customer");
        Assert.Contains("Company Alpha", choices);
        Assert.Contains("Company Beta", choices);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("/account/select-customer", new() { ["customerId"] = store.B.ToString() }, "/account/select-customer")).StatusCode);
        Assert.Contains("Company Beta", await client.GetStringAsync("/"));
        var denied = await Post("/account/select-customer", new() { ["customerId"] = Guid.NewGuid().ToString() }, "/account/select-customer");
        Assert.Contains("/account/access-denied", denied.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Company Alpha", await client.GetStringAsync("/profile/company?customerId=" + store.A));
    }

    [Fact]
    public async Task SwitchingTenantPreservesOriginalTicketLifetime()
    {
        store.GrantB = true;
        var login = await Login();
        var original = Ticket(login);
        await Task.Delay(1100); // Ticket timestamps have second precision.
        var switched = await Post("/account/select-customer", new() { ["customerId"] = store.B.ToString() }, "/account/select-customer");
        var current = Ticket(switched);
        Assert.Equal(original.Properties.IssuedUtc, current.Properties.IssuedUtc);
        Assert.Equal(original.Properties.ExpiresUtc, current.Properties.ExpiresUtc);
    }
    private Microsoft.AspNetCore.Authentication.AuthenticationTicket Ticket(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("TA.Customer.Auth="));
        var value = cookie.Split(';')[0]["TA.Customer.Auth=".Length..];
        return factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies").TicketDataFormat.Unprotect(value)!;
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("user")]
    [InlineData("password")]
    public async Task ExistingSessionIsRejectedAfterRevocation(string kind)
    {
        await Login();
        if (kind == "membership") store.GrantA = false;
        else if (kind == "user") store.UserActive = false;
        else store.User = store.User with { PasswordHash = new PasswordService().Hash("Different!123") };
        LoginRedirect(await client.GetAsync("/profile/company"));
    }

    [Fact]
    public async Task TwoUsersCannotSeeEachOthersTenantOrPersonalProfile()
    {
        using var second = NewClient();
        await Login();
        await Post("/account/login", new() { ["Username"] = "second", ["Password"] = Password }, "/account/login", second);
        var html = await second.GetStringAsync("/profile/company?customerId=" + store.A);
        Assert.Contains("Company Beta", html);
        Assert.DoesNotContain("Company Alpha", html);
        Assert.DoesNotContain("Second Person", WebUtility.HtmlDecode(await client.GetStringAsync("/profile/personal?userId=" + store.Second.Id)));
        var denied = await Post("/account/select-customer", new() { ["customerId"] = store.B.ToString() }, "/account/select-customer");
        Assert.Contains("/account/access-denied", denied.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("inactive")]
    [InlineData("no-membership")]
    public async Task LoginFailsClosed(string kind)
    {
        if (kind == "inactive") store.UserActive = false;
        if (kind == "no-membership") store.GrantA = false;
        var response = await Login(password: kind == "wrong" ? "Wrong!123" : Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], x => x.StartsWith("TA.Customer.Auth=") && !x.StartsWith("TA.Customer.Auth=;"));
        LoginRedirect(await client.GetAsync("/"));
        Assert.Equal(0, store.LoginTouches);
    }

    [Fact]
    public async Task PasswordChangeInvalidatesOtherSessionsAndRequiresRelogin()
    {
        using var other = NewClient();
        await Login(); await Login(other);
        var changed = await Post("/account/change-password", new() { ["CurrentPassword"] = Password, ["NewPassword"] = "Changed-only!123", ["ConfirmPassword"] = "Changed-only!123" }, "/account/change-password");
        LoginRedirect(changed);
        Assert.Equal(1, store.PasswordWrites);
        LoginRedirect(await client.GetAsync("/"));
        LoginRedirect(await other.GetAsync("/"));
        Assert.Equal(HttpStatusCode.OK, (await Login()).StatusCode); // old password fails
        Assert.Equal(HttpStatusCode.Redirect, (await Login(password: "Changed-only!123")).StatusCode);
    }

    [Theory]
    [InlineData("wrong-current")]
    [InlineData("weak")]
    [InlineData("mismatch")]
    [InlineData("same")]
    [InlineData("concurrent")]
    public async Task InvalidPasswordChangesDoNotWrite(string kind)
    {
        await Login();
        var next = kind == "weak" ? "short" : kind == "same" ? Password : "Next-only!123";
        store.RejectPasswordWrite = kind == "concurrent";
        Assert.Equal(HttpStatusCode.OK, (await Post("/account/change-password", new() { ["CurrentPassword"] = kind == "wrong-current" ? "wrong" : Password, ["NewPassword"] = next, ["ConfirmPassword"] = kind == "mismatch" ? "Different!123" : next }, "/account/change-password")).StatusCode);
        Assert.Equal(0, store.PasswordWrites);
    }

    [Fact]
    public async Task EveryMutatingEndpointRejectsMissingCsrf()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        await Login();
        foreach (var path in new[] { "/account/logout", "/account/select-customer", "/account/change-password" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0, store.PasswordWrites);
    }

    [Fact]
    public async Task AccountThrottleNormalizesUsername()
    {
        for (var i = 0; i < 6; i++)
        {
            var response = await Post("/account/login", new() { ["Username"] = i % 2 == 0 ? "customer" : "  CUSTOMER  ", ["Password"] = "wrong" }, "/account/login");
            Assert.Equal(i == 5 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("PBKDF2$SHA256$0$AA==$AA==")]
    [InlineData("invalid")]
    [InlineData("PBKDF2$SHA256$150000$bad$bad")]
    public void MalformedHashesFailClosed(string hash) => Assert.False(new PasswordService().Verify(Password, hash));

    [Theory]
    [InlineData("SYS_ADMIN", true)]
    [InlineData("AGENT_ADMIN", false)]
    [InlineData("OWNER", false)]
    [InlineData("ADMIN", false)]
    [InlineData("VIEWER", false)]
    [InlineData("", false)]
    public async Task AdminManagementPolicyRequiresSystemAdmin(string role, bool allowed)
    {
        using var adminFactory = new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));
        // Resolve real Admin policy without issuing SQL-backed authenticated requests.
        using var scope = adminFactory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role), new Claim("permission", "CUSTOMERS.PORTAL.MANAGE") }, "test"));
        Assert.Equal(allowed, (await auth.AuthorizeAsync(user, null, "SystemAdminOnly")).Succeeded);
        foreach (var type in new[] { typeof(admin::TAPortal.Web.Controllers.CustomerPortalAccountsController), typeof(admin::TAPortal.Web.Controllers.CustomersController), typeof(admin::TAPortal.Web.Controllers.UsersController), typeof(admin::TAPortal.Web.Controllers.HomeController) })
            Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), a => a.Policy == "SystemAdminOnly");
    }

    [Fact]
    public async Task AdminAndCustomerCookiesCannotAuthenticateEachOther()
    {
        var response = await Login();
        var cookie = response.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("TA.Customer.Auth=")).Split(';')[0];
        using var adminFactory = new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> { ["Database:ConnectionString"] = "INVALID" }))
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));
        using var adminClient = adminFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        // Rename as well: cryptographic app isolation must still reject the ticket before any SQL.
        adminClient.DefaultRequestHeaders.Add("Cookie", cookie.Replace("TA.Customer.Auth=", "TA.Portal.Auth="));
        LoginRedirect(await adminClient.GetAsync("/customerportalaccounts"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/profile/company")]
    [InlineData("/profile/personal")]
    [InlineData("/account/select-customer")]
    [InlineData("/account/change-password")]
    public async Task AnonymousBusinessRoutesRequireAuthentication(string path) => LoginRedirect(await client.GetAsync(path));

    [Fact]
    public async Task ForgedForwardedIpDoesNotBypassIpThrottle()
    {
        for (var i = 0; i < 11; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2." + (i + 1));
            var response = await Post("/account/login", new() { ["Username"] = "unknown-" + i, ["Password"] = "wrong" }, "/account/login");
            Assert.Equal(i == 10 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, response.StatusCode);
        }
    }

    private sealed class MemoryStore : ICustomerStore
    {
        public Guid A { get; } = Guid.NewGuid();
        public Guid B { get; } = Guid.NewGuid();
        public CustomerUser User { get; set; } = new(Guid.NewGuid(), "customer", "Customer Person", "customer@example.invalid", new PasswordService().Hash(Password));
        public CustomerUser Second { get; } = new(Guid.NewGuid(), "second", "Second Person", "second@example.invalid", new PasswordService().Hash(Password));
        public bool UserActive { get; set; } = true;
        public bool GrantA { get; set; } = true;
        public bool GrantB { get; set; }
        public bool RejectPasswordWrite { get; set; }
        public int PasswordWrites { get; private set; }
        public int LoginTouches { get; private set; }
        public Task<CustomerUser?> FindUser(string username) => Task.FromResult(username.Trim().Equals("second", StringComparison.OrdinalIgnoreCase) ? Second : username.Trim().Equals("customer", StringComparison.OrdinalIgnoreCase) && UserActive ? User : null);
        public Task<CustomerUser?> FindUser(Guid id) => Task.FromResult(id == Second.Id ? Second : id == User.Id && UserActive ? User : null);
        public Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId)
        {
            var result = new List<CustomerMembership>();
            if (userId == User.Id && UserActive && GrantA) result.Add(new(A, "A", "Company Alpha", null, null, null, null, "OWNER"));
            if (userId == Second.Id || userId == User.Id && UserActive && GrantB) result.Add(new(B, "B", "Company Beta", null, null, null, null, "VIEWER"));
            return Task.FromResult<IReadOnlyList<CustomerMembership>>(result);
        }
        public Task<bool> ChangePassword(Guid id, string oldHash, string newHash)
        {
            if (RejectPasswordWrite || !UserActive || id != User.Id || User.PasswordHash != oldHash) return Task.FromResult(false);
            User = User with { PasswordHash = newHash }; PasswordWrites++; return Task.FromResult(true);
        }
        public Task TouchLogin(Guid id) { LoginTouches++; return Task.CompletedTask; }
    }
}
