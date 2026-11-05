using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Customer Portal self sign-up (operator 2026-11-06: a company signs up, is signed in at once and picks a package) and the
// tax code lookup that fills the company name and address. No SQL, no network: a recording sign-up store and lookup.
public sealed class SafeCustomerSignupTests
{
    private const string Password = "Dang-ky!2026";

    [Fact]
    public async Task SignUpPageIsPublicAndLinkedFromTheSignInPage()
    {
        using var f = Factory(out _, out _);
        var c = Client(f);
        Assert.Contains("data-cp-signup-link", await c.GetStringAsync("/account/login"));
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/account/register"));
        Assert.Contains("Đăng ký tài khoản doanh nghiệp", html);
        Assert.Contains("data-cp-tax-lookup=\"/account/tax-lookup\"", html);
        Assert.Contains("value=\"HOUSEHOLD\"", html);
        Assert.DoesNotMatch(@"(?i)\b(tingee|heno|vietqr)\b", Regex.Replace(html, "<script[^>]*src=[^>]*>", ""));
    }

    [Fact]
    public async Task ValidSignUpCreatesTheCompanyAndSignsInAsOwner()
    {
        using var f = Factory(out var signups, out _);
        var c = Client(f);
        var r = await Post(c, "/account/register", await Token(c), Form(tax: " 0101 234567 ", email: " Ke.Toan@Example.VN "));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/ServicePackages", r.Headers.Location!.OriginalString);
        Assert.Contains(r.Headers.GetValues("Set-Cookie"), v => v.StartsWith("TA.Customer.Auth=", StringComparison.Ordinal));
        var req = Assert.Single(signups.Requests);
        Assert.Equal("0101234567", req.TaxCode);
        Assert.Equal("ke.toan@example.vn", req.Email);
        Assert.Equal("BUSINESS", req.CustomerType);
        Assert.Equal("Công ty TNHH Thử Nghiệm", req.CompanyName);
        Assert.StartsWith("PBKDF2$", req.PasswordHash);
        Assert.DoesNotContain(Password, req.PasswordHash);
    }

    [Theory]
    [InlineData("BUSINESS", "012345678901", "Mã số thuế doanh nghiệp gồm 10 chữ số")]   // 12 digits is a personal code
    [InlineData("HOUSEHOLD", "12345", "Mã số thuế hộ kinh doanh gồm 10 hoặc 12 chữ số")]
    public async Task WrongTaxCodesAreRefusedBeforeAnythingIsSaved(string type, string tax, string message)
    {
        using var f = Factory(out var signups, out _);
        var c = Client(f);
        var r = await Post(c, "/account/register", await Token(c), Form(tax: tax, type: type));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(signups.Requests);
    }

    [Fact]
    public async Task HouseholdMayUseTheTwelveDigitPersonalTaxCode()
    {
        using var f = Factory(out var signups, out _);
        var c = Client(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/account/register", await Token(c), Form(tax: "001088012345", type: "HOUSEHOLD"))).StatusCode);
        Assert.Equal("HOUSEHOLD", Assert.Single(signups.Requests).CustomerType);
    }

    [Fact]
    public async Task WeakPasswordsMissingTermsAndTakenAccountsAreExplained()
    {
        using var f = Factory(out var signups, out _);
        var c = Client(f);
        var token = await Token(c);
        var weak = Form(); weak["Password"] = weak["ConfirmPassword"] = "abcdefgh";
        Assert.Contains("ít nhất một chữ số", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, weak)).Content.ReadAsStringAsync()));
        var noTerms = Form(); noTerms.Remove("AcceptTerms");
        Assert.Contains("điều khoản", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, noTerms)).Content.ReadAsStringAsync()));
        Assert.Empty(signups.Requests);

        signups.Next = SignupOutcome.TaxCodeTaken;
        var taken = await Post(c, "/account/register", token, Form());
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("Mã số thuế này đã có tài khoản", WebUtility.HtmlDecode(await taken.Content.ReadAsStringAsync()));
        signups.Next = SignupOutcome.EmailTaken;
        Assert.Contains("Email này đã được dùng", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, Form())).Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task TaxLookupAnswersWithNameAndAddressOrAPlainMessage()
    {
        using var f = Factory(out _, out var lookup);
        var c = Client(f);
        var found = JsonDocument.Parse(await c.GetStringAsync("/account/tax-lookup?code=0101234567")).RootElement;
        Assert.True(found.GetProperty("ok").GetBoolean());
        Assert.Equal("CÔNG TY TNHH THỬ NGHIỆM", found.GetProperty("name").GetString());
        Assert.Equal("1 Tràng Tiền, Hà Nội", found.GetProperty("address").GetString());
        lookup.Status = TaxCodeLookupStatus.Unavailable;
        var down = JsonDocument.Parse(await c.GetStringAsync("/account/tax-lookup?code=0101234567")).RootElement;
        Assert.False(down.GetProperty("ok").GetBoolean());
        Assert.Contains("nhập tay", down.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"code\":\"00\",\"desc\":\"Success\",\"data\":{\"id\":\"0101234567\",\"name\":\" CÔNG TY A \",\"shortName\":\"A\",\"address\":\"Hà Nội\"}}", TaxCodeLookupStatus.Found)]
    [InlineData(HttpStatusCode.OK, "{\"code\":\"51\",\"desc\":\"Tax not found\",\"data\":null}", TaxCodeLookupStatus.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests, please try again later.", TaxCodeLookupStatus.Unavailable)]
    [InlineData(HttpStatusCode.OK, "not json", TaxCodeLookupStatus.Unavailable)]
    public async Task RegisterLookupReadsTheServiceAnswer(HttpStatusCode status, string body, TaxCodeLookupStatus expected)
    {
        var handler = new StubHandler(status, body);
        var lookup = new VietQrTaxCodeLookup(new Clients(handler), TimeProvider.System, NullLogger<VietQrTaxCodeLookup>.Instance);
        var code = "01" + Random.Shared.Next(10_000_000, 99_999_999);   // a code no other test cached
        var result = await lookup.LookupAsync(code);
        Assert.Equal(expected, result.Status);
        Assert.Equal("/v2/business/" + code, handler.Paths.Single());
        if (expected == TaxCodeLookupStatus.Found) Assert.Equal(("CÔNG TY A", "Hà Nội"), (result.Info!.Name, result.Info.Address));
        // Answers are cached, failures are not; malformed codes never reach the service.
        await lookup.LookupAsync(code);
        Assert.Equal(expected == TaxCodeLookupStatus.Unavailable ? 2 : 1, handler.Paths.Count);
        Assert.Equal(TaxCodeLookupStatus.Invalid, (await lookup.LookupAsync("012345678901")).Status);
        Assert.Equal(expected == TaxCodeLookupStatus.Unavailable ? 2 : 1, handler.Paths.Count);
    }

    private static Dictionary<string, string> Form(string tax = "0101234567", string email = "ke.toan@example.vn", string type = "BUSINESS") => new()
    {
        ["CustomerType"] = type, ["TaxCode"] = tax, ["CompanyName"] = "Công ty TNHH Thử Nghiệm", ["Address"] = "1 Tràng Tiền, Hà Nội",
        ["ContactName"] = "Nguyễn Văn A", ["Email"] = email, ["Phone"] = "0901234567", ["Password"] = Password, ["ConfirmPassword"] = Password, ["AcceptTerms"] = "true"
    };

    private static async Task<string> Token(HttpClient c) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync("/account/register"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<Program> Factory(out RecordingSignups signups, out FakeLookup lookup)
    {
        var s = signups = new RecordingSignups();
        var l = lookup = new FakeLookup();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<ISignupStore>();
                services.AddSingleton<ISignupStore>(s);
                services.RemoveAll<ITaxCodeLookup>();
                services.AddSingleton<ITaxCodeLookup>(l);
            }));
    }

    public sealed class RecordingSignups : ISignupStore
    {
        public List<SignupRequest> Requests { get; } = [];
        public SignupOutcome Next { get; set; } = SignupOutcome.Created;
        public Task<SignupResult> RegisterAsync(SignupRequest request)
        {
            if (Next == SignupOutcome.Created) Requests.Add(request);
            return Task.FromResult(new SignupResult(Next, Guid.NewGuid(), Guid.NewGuid()));
        }
    }

    public sealed class FakeLookup : ITaxCodeLookup
    {
        public TaxCodeLookupStatus Status { get; set; } = TaxCodeLookupStatus.Found;
        public Task<TaxCodeLookupResult> LookupAsync(string? taxCode, CancellationToken ct = default) =>
            Task.FromResult(Status == TaxCodeLookupStatus.Found
                ? new TaxCodeLookupResult(Status, new TaxCodeInfo(taxCode!, "CÔNG TY TNHH THỬ NGHIỆM", null, "1 Tràng Tiền, Hà Nội"))
                : new TaxCodeLookupResult(Status));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, body.StartsWith('{') ? "application/json" : "text/plain") });
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.vietqr.io/") };
    }
}
