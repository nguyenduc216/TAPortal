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
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Customer Portal self sign-up (operator 2026-11-06; 2026-11-12: individuals without a tax code, a generated password sent
// by email, terms in a dialog) and the tax code lookup that fills the company name and address. No SQL, no network: a
// recording sign-up store, lookup and mail sender.
public sealed class SafeCustomerSignupTests
{
    [Fact]
    public async Task SignUpPageIsPublicAndLinkedFromTheSignInPage()
    {
        using var f = Factory(out _, out _, out _);
        var c = Client(f);
        Assert.Contains("data-cp-signup-link", await c.GetStringAsync("/account/login"));
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/account/register"));
        Assert.Contains("Đăng ký tài khoản", html);
        Assert.Contains("data-cp-tax-lookup=\"/account/tax-lookup\"", html);
        Assert.Contains("value=\"HOUSEHOLD\"", html);
        Assert.Contains("value=\"INDIVIDUAL\"", html);
        Assert.DoesNotContain("type=\"password\"", html);                       // the system creates the password
        Assert.Contains("data-cp-terms-dialog", html);                          // terms open in a dialog …
        Assert.Matches("<a[^>]*data-cp-terms-open[^>]*href=\"/account/terms\"|<a[^>]*href=\"/account/terms\"[^>]*data-cp-terms-open", html);
        Assert.Contains("Điều 1. Phạm vi áp dụng", html);
        Assert.Contains("<a class=\"cp-maker\" href=\"https://tuvangiaiphapta.vn\"", html);   // … and T.A is named at the bottom only
        Assert.Contains("Sản phẩm của Tư vấn giải pháp T.A", WebUtility.HtmlDecode(await c.GetStringAsync("/account/login")));
        Assert.Contains("Điều 16.", WebUtility.HtmlDecode(await c.GetStringAsync("/account/terms")));
        Assert.DoesNotMatch(@"(?i)\b(tingee|heno|vietqr)\b", Regex.Replace(html, "<script[^>]*src=[^>]*>", ""));
    }

    [Fact]
    public async Task ValidSignUpCreatesTheAccountAndMailsAGeneratedPassword()
    {
        using var f = Factory(out var signups, out _, out var mails);
        var c = Client(f);
        var r = await Post(c, "/account/register", await Token(c), Form(tax: " 0101 234567 ", email: " Ke.Toan@Example.VN "));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/account/register/done", r.Headers.Location!.OriginalString);
        Assert.False(r.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(v => v.StartsWith("TA.Customer.Auth=", StringComparison.Ordinal)));
        var req = Assert.Single(signups.Requests);
        Assert.Equal("0101234567", req.TaxCode);
        Assert.Equal("ke.toan@example.vn", req.Email);
        Assert.Equal("BUSINESS", req.CustomerType);
        Assert.Equal("Công ty TNHH Thử Nghiệm", req.CompanyName);
        Assert.Equal("default", req.TermsVersion);
        Assert.StartsWith("PBKDF2$", req.PasswordHash);

        // The email carries the login page, the username and the one password whose hash was stored.
        var mail = Assert.Single(mails.Sent);
        Assert.Equal("ke.toan@example.vn", mail.To);
        Assert.Equal("Thông tin đăng nhập Hóa đơn tự động - Công ty TNHH Thử Nghiệm", mail.Subject);
        var password = Regex.Match(mail.TextBody, "Mật khẩu tạm: (\\S+)").Groups[1].Value;
        Assert.True(PasswordPolicy.Validate(password).IsValid);
        Assert.True(new PasswordService().Verify(password, req.PasswordHash));
        Assert.Contains("Địa chỉ đăng nhập: https://hoadon.example.vn/account/login", mail.TextBody);
        Assert.Contains("src=\"https://hoadon.example.vn/images/logo.png\"", mail.HtmlBody);
        Assert.Contains(WebUtility.HtmlEncode(password), mail.HtmlBody);
        Assert.DoesNotMatch(@"(?i)\b(tingee|heno)\b|T\.A\b", mail.HtmlBody + mail.TextBody);

        var done = WebUtility.HtmlDecode(await c.GetStringAsync("/account/register/done"));
        Assert.Contains("Thông tin đăng nhập đã gửi đến", done);
        Assert.Contains("ke.toan@example.vn", done);
        Assert.Equal(HttpStatusCode.Redirect, (await c.GetAsync("/account/register/done")).StatusCode);   // shown once
    }

    [Fact]
    public async Task AnIndividualMaySignUpWithoutATaxCode()
    {
        using var f = Factory(out var signups, out _, out _);
        var c = Client(f);
        var token = await Token(c);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/account/register", token, Form(tax: "", type: "INDIVIDUAL"))).StatusCode);
        var req = Assert.Single(signups.Requests);
        Assert.Equal("INDIVIDUAL", req.CustomerType);
        Assert.Null(req.TaxCode);
        // A business still needs one, and an individual's code, when given, has 10 or 12 digits.
        Assert.Contains("Nhập mã số thuế.", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, Form(tax: ""))).Content.ReadAsStringAsync()));
        Assert.Contains("Mã số thuế cá nhân gồm 10 hoặc 12 chữ số", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, Form(tax: "123", type: "INDIVIDUAL"))).Content.ReadAsStringAsync()));
        Assert.Single(signups.Requests);
    }

    [Fact]
    public async Task WhenMailCannotBeSentTheAccountIsKeptAndTheUserIsToldWhatToDo()
    {
        using var f = Factory(out var signups, out _, out var mails);
        mails.Configured = false;
        var c = Client(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/account/register", await Token(c), Form())).StatusCode);
        Assert.Single(signups.Requests);
        Assert.Empty(mails.Sent);
        var done = WebUtility.HtmlDecode(await c.GetStringAsync("/account/register/done"));
        Assert.Contains("chưa gửi được email", done);
        Assert.Contains("Quên mật khẩu", done);
    }

    [Theory]
    [InlineData("BUSINESS", "012345678901", "Mã số thuế doanh nghiệp gồm 10 chữ số")]   // 12 digits is a personal code
    [InlineData("HOUSEHOLD", "12345", "Mã số thuế hộ kinh doanh gồm 10 hoặc 12 chữ số")]
    public async Task WrongTaxCodesAreRefusedBeforeAnythingIsSaved(string type, string tax, string message)
    {
        using var f = Factory(out var signups, out _, out _);
        var c = Client(f);
        var r = await Post(c, "/account/register", await Token(c), Form(tax: tax, type: type));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(signups.Requests);
    }

    [Fact]
    public async Task HouseholdMayUseTheTwelveDigitPersonalTaxCode()
    {
        using var f = Factory(out var signups, out _, out _);
        var c = Client(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/account/register", await Token(c), Form(tax: "001088012345", type: "HOUSEHOLD"))).StatusCode);
        Assert.Equal("HOUSEHOLD", Assert.Single(signups.Requests).CustomerType);
    }

    [Fact]
    public async Task MissingTermsAndTakenAccountsAreExplained()
    {
        using var f = Factory(out var signups, out _, out var mails);
        var c = Client(f);
        var token = await Token(c);
        var noTerms = Form(); noTerms.Remove("AcceptTerms");
        Assert.Contains("điều khoản", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, noTerms)).Content.ReadAsStringAsync()));
        Assert.Empty(signups.Requests);

        signups.Next = SignupOutcome.TaxCodeTaken;
        var taken = await Post(c, "/account/register", token, Form());
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("Mã số thuế này đã có tài khoản", WebUtility.HtmlDecode(await taken.Content.ReadAsStringAsync()));
        signups.Next = SignupOutcome.EmailTaken;
        Assert.Contains("Email này đã được dùng", WebUtility.HtmlDecode(await (await Post(c, "/account/register", token, Form())).Content.ReadAsStringAsync()));
        Assert.Empty(mails.Sent);
    }

    [Fact]
    public async Task TaxLookupAnswersWithNameAndAddressOrAPlainMessage()
    {
        using var f = Factory(out _, out var lookup, out _);
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
        ["ContactName"] = "Nguyễn Văn A", ["Email"] = email, ["Phone"] = "0901234567", ["AcceptTerms"] = "true"
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

    private static WebApplicationFactory<Program> Factory(out RecordingSignups signups, out FakeLookup lookup, out RecordingMail mails)
    {
        var s = signups = new RecordingSignups();
        var l = lookup = new FakeLookup();
        var m = mails = new RecordingMail();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "",
                ["Email:PublicBaseUrl"] = "https://hoadon.example.vn" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<ISignupStore>();
                services.AddSingleton<ISignupStore>(s);
                services.RemoveAll<ITaxCodeLookup>();
                services.AddSingleton<ITaxCodeLookup>(l);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(m);
                services.RemoveAll<IPortalContentStore>();
                services.AddSingleton<IPortalContentStore>(new NoContent());
            }));
    }

    public sealed class RecordingMail : IEmailSender
    {
        public bool Configured { get; set; } = true;
        public List<EmailMessage> Sent { get; } = [];
        public bool IsConfigured => Configured;
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Sent.Add(message); return Task.CompletedTask; }
    }

    /// <summary>No edited content: every email and the terms use the built-in defaults.</summary>
    public sealed class NoContent : IPortalContentStore
    {
        public Task<PortalContentEntry?> GetAsync(string code, CancellationToken ct = default) => Task.FromResult<PortalContentEntry?>(null);
        public Task<IReadOnlyDictionary<string, PortalContentEntry>> AllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PortalContentEntry>>(new Dictionary<string, PortalContentEntry>());
        public Task SaveAsync(string code, string? subject, string body, Guid actor, string? ip) => throw new InvalidOperationException("read only");
        public Task<bool> ResetAsync(string code, Guid actor, string? ip) => throw new InvalidOperationException("read only");
    }

    public sealed class RecordingSignups : ISignupStore
    {
        public List<SignupRequest> Requests { get; } = [];
        public SignupOutcome Next { get; set; } = SignupOutcome.Created;
        public Task<SignupResult> RegisterAsync(SignupRequest request)
        {
            if (Next == SignupOutcome.Created) Requests.Add(request);
            return Task.FromResult(new SignupResult(Next, Guid.NewGuid(), Guid.NewGuid(), request.TaxCode ?? "CN12345678"));
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
