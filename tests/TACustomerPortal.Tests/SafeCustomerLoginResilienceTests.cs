using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Customer Portal sign-in when the data layer fails (connection string missing, SQL unreachable, missing permission).
// Before this fix any such failure surfaced as the generic error page, for right and wrong passwords alike. No SQL.
public sealed class SafeCustomerLoginResilienceTests
{
    private const string Password = "Memory-only!123";

    [Fact]
    public async Task DataLayerFailureKeepsTheSignInFormWithAReferenceInsteadOfTheErrorPage()
    {
        var store = new FlakyStore { FailFind = true };
        using var f = Factory(store);
        var response = await Login(Client(f), Password);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("không truy cập được dữ liệu", html);
        Assert.Matches("mã sự cố: [^)]+\\)", html);
        Assert.Contains("name=\"Password\"", html);                 // the form is still there
        Assert.DoesNotContain("Không thể xử lý yêu cầu", html);
        Assert.DoesNotContain(Password, html);
        Assert.DoesNotContain("INVALID: tests", html);              // no configuration detail leaks to the page
    }

    [Theory]
    [InlineData(Password, "Tài khoản hoặc mật khẩu không đúng")]   // right password but no membership in this fake
    [InlineData("Wrong-pass!1", "Tài khoản hoặc mật khẩu không đúng")]
    public async Task WrongCredentialsShowAValidationMessage(string password, string expected)
    {
        var store = new FlakyStore { NoMemberships = password == Password };
        using var f = Factory(store);
        var response = await Login(Client(f), password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task FailingToRecordTheLastSignInDoesNotUndoASuccessfulSignIn()
    {
        var store = new FlakyStore { FailTouch = true };
        using var f = Factory(store);
        var response = await Login(Client(f), Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("TA.Customer.Auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TooManyAttemptsShowAMessageNotABlankPage()
    {
        using var f = Factory(new FlakyStore());
        var c = Client(f);
        HttpResponseMessage last = null!;
        for (var i = 0; i < 6; i++) last = await Login(c, "Wrong-pass!1");
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Contains("quá nhiều lần", WebUtility.HtmlDecode(await last.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ErrorPageShowsAReferenceForTheLogs()
    {
        using var f = Factory(new FlakyStore());
        var response = await Client(f).GetAsync("/account/error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Matches("data-ta-reference>[^<]+<", await response.Content.ReadAsStringAsync());
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static async Task<HttpResponseMessage> Login(HttpClient c, string password)
    {
        var page = await c.GetStringAsync("/account/login");
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return await c.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = "customer", ["Password"] = password, ["__RequestVerificationToken"] = token }));
    }

    private static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<Program> Factory(FlakyStore store) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<ICustomerStore>();
                s.AddSingleton<ICustomerStore>(store);
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
            }));

    private sealed class FlakyStore : ICustomerStore
    {
        private static readonly CustomerUser User = new(Guid.Parse("12345678-0000-0000-0000-000000000001"), "customer", "Customer Person", null, new PasswordService().Hash(Password));
        private static readonly CustomerMembership Company = new(Guid.Parse("12345678-0000-0000-0000-0000000000aa"), "A", "Company Alpha", null, null, null, null, "OWNER");
        public bool FailFind { get; init; }
        public bool FailTouch { get; init; }
        public bool NoMemberships { get; init; }

        public Task<CustomerUser?> FindUser(string username) =>
            FailFind ? throw new InvalidOperationException("Configure Database__ConnectionString") : Task.FromResult<CustomerUser?>(User);
        public Task<CustomerUser?> FindUser(Guid id) => Task.FromResult<CustomerUser?>(id == User.Id ? User : null);
        public Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId) => Task.FromResult<IReadOnlyList<CustomerMembership>>(NoMemberships ? [] : [Company]);
        public Task<bool> ChangePassword(Guid id, string oldHash, string newHash) => Task.FromResult(false);
        public Task TouchLogin(Guid id) => FailTouch ? throw new InvalidOperationException("UPDATE permission denied") : Task.CompletedTask;
    }
}
