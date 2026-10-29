extern alias admin;
extern alias webhooks;

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
using TAPortal.Integrations.Tingee;
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Admin Customer Workspace → "Thanh toán Tingee": T.A (Master Merchant) creates or links the customer's Tingee merchant.
// No SQL, no network: in-memory mapping store and a recording Tingee fake. Also covers the webhook inbox dedup key.
public sealed class SafeAdminTingeeTests
{
    private const string TestScheme = "SafeAdminTingee";
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid Inactive = Guid.Parse("cccccccc-0000-0000-0000-0000000000c3");

    [Fact]
    public async Task WorkspaceHasATingeeTabThatStartsUnlinked()
    {
        using var f = Factory(out _, out _, configured: false);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/customers/tingee/{A}"));
        Assert.Matches("aria-current=\"page\"[^>]*data-ta-tab=\"tingee\"", html);
        Assert.Contains("data-ta-tingee-unlinked", html);
        Assert.Contains("data-ta-tingee-unconfigured", html);
        Assert.Contains("value=\"0912345678\"", html);   // phone prefilled from the customer, digits only
        Assert.Contains("value=\"KH-A\"", html);
    }

    [Fact]
    public async Task CreatingAMerchantCallsTingeeOnceAndStoresTheMapping()
    {
        using var f = Factory(out var store, out var tingee);
        var c = Client(f);
        var r = await Post(c, $"/customers/createtingeemerchant/{A}", await Token(c, $"/customers/tingee/{A}"),
            new() { ["Create.Name"] = "Công ty A", ["Create.PhoneNumber"] = "0912345678", ["Create.Email"] = "a@a.example", ["Create.Code"] = "KH-A" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var request = Assert.Single(tingee.Created);
        Assert.Equal(("Công ty A", "0912345678", "KH-A"), (request.Name, request.PhoneNumber, request.Code));
        var link = Assert.Single(store.Links);
        Assert.Equal((A, 4242L, "CREATED", Actor), (link.Value.CustomerId, link.Value.MerchantId, link.Value.Source, store.LastActor));
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/tingee/{A}"));
        Assert.Contains("#4242", page);
        Assert.Contains("data-ta-tingee-linked", page);

        // Already linked: no second merchant is created.
        await Post(c, $"/customers/createtingeemerchant/{A}", await Token(c, $"/customers/tingee/{A}"), new() { ["Create.Name"] = "X", ["Create.PhoneNumber"] = "0912345678" });
        Assert.Single(tingee.Created);
    }

    [Fact]
    public async Task InvalidInputAndTingeeErrorsDoNotLinkAnything()
    {
        using var f = Factory(out var store, out var tingee);
        var c = Client(f);
        var token = await Token(c, $"/customers/tingee/{A}");
        var invalid = await Post(c, $"/customers/createtingeemerchant/{A}", token, new() { ["Create.Name"] = "A", ["Create.PhoneNumber"] = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("10 chữ số", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        tingee.Error = new TingeeApiException("1133", "Không có quyền thực hiện");
        await Post(c, $"/customers/createtingeemerchant/{A}", token, new() { ["Create.Name"] = "A", ["Create.PhoneNumber"] = "0912345678" });
        Assert.Empty(store.Links);
        Assert.Contains("mã 1133", WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/tingee/{A}")));
    }

    [Fact]
    public async Task CreatedButUnstoredMerchantIsReportedForManualLinkingNotRecreated()
    {
        using var f = Factory(out var store, out var tingee);
        store.FailLink = true;
        var c = Client(f);
        await Post(c, $"/customers/createtingeemerchant/{A}", await Token(c, $"/customers/tingee/{A}"), new() { ["Create.Name"] = "A", ["Create.PhoneNumber"] = "0912345678" });
        Assert.Contains("Tingee đã tạo merchant #4242", WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/tingee/{A}")));
    }

    [Fact]
    public async Task ExistingMerchantsCanBeLinkedOnceAndUnlinkedLocally()
    {
        using var f = Factory(out var store, out var tingee);
        var c = Client(f);
        var token = await Token(c, $"/customers/tingee/{A}");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, $"/customers/linktingeemerchant/{A}", token, new() { ["merchantId"] = "777" })).StatusCode);
        Assert.Equal(("LINKED", 777L), (store.Links[A].Source, store.Links[A].MerchantId));
        Assert.Empty(tingee.Created);
        await Post(c, $"/customers/unlinktingeemerchant/{A}", token, new());
        Assert.Empty(store.Links);
        await Post(c, $"/customers/linktingeemerchant/{Inactive}", token, new() { ["merchantId"] = "778" });
        Assert.Empty(store.Links);   // inactive customers cannot be linked
        await Post(c, $"/customers/linktingeemerchant/{A}", token, new() { ["merchantId"] = "-5" });
        Assert.Empty(store.Links);
    }

    [Fact]
    public void WebhookDedupKeyPrefersTheTransactionCode()
    {
        static TingeeWebhookEnvelope E(TingeeWebhookKind kind, string body, string? requestId) => new(kind, requestId, "20261030103000123", body, DateTimeOffset.UtcNow);
        Assert.Equal("tx:FT123", webhooks::TAPortal.Webhooks.SqlTingeeWebhookInbox.DedupKey(E(TingeeWebhookKind.Payment, "{\"transactionCode\":\" FT123 \",\"amount\":1}", "req-9")));
        Assert.Equal("rq:req-9", webhooks::TAPortal.Webhooks.SqlTingeeWebhookInbox.DedupKey(E(TingeeWebhookKind.Payment, "not json", "req-9")));
        Assert.Equal("rq:req-9", webhooks::TAPortal.Webhooks.SqlTingeeWebhookInbox.DedupKey(E(TingeeWebhookKind.VaStatus, "{\"transactionCode\":\"FT1\"}", "req-9")));
        var hashed = webhooks::TAPortal.Webhooks.SqlTingeeWebhookInbox.DedupKey(E(TingeeWebhookKind.BankLink, "{}", null));
        Assert.Matches("^sha:[0-9a-f]{64}$", hashed);
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

    private static WebApplicationFactory<admin::Program> Factory(out MemoryLinks store, out RecordingTingee tingee, bool configured = true)
    {
        var s = store = new MemoryLinks();
        var t = tingee = new RecordingTingee(configured);
        return new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<AdminServices.ICustomerAdminStore>();
                services.AddSingleton<AdminServices.ICustomerAdminStore>(new Customers());
                services.RemoveAll<AdminServices.ITingeeMerchantStore>();
                services.AddSingleton<AdminServices.ITingeeMerchantStore>(s);
                services.RemoveAll<ITingeeApi>();
                services.AddSingleton<ITingeeApi>(t);
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    private sealed class Customers : AdminServices.ICustomerAdminStore
    {
        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid customerId) => Task.FromResult(customerId == A
            ? new AdminModels.CustomerEditVm { Id = A, Code = "KH-A", Name = "Công ty A", Phone = "0912 345 678", Email = "a@a.example", Status = "ACTIVE", IsActive = true }
            : customerId == Inactive ? new AdminModels.CustomerEditVm { Id = Inactive, Code = "KH-C", Name = "Công ty C", Status = "ACTIVE", IsActive = false } : null);
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new NotSupportedException();
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new NotSupportedException();
    }

    public sealed class MemoryLinks : AdminServices.ITingeeMerchantStore
    {
        public Dictionary<Guid, AdminServices.TingeeMerchantLink> Links { get; } = [];
        public bool FailLink { get; set; }
        public Guid LastActor { get; private set; }
        public Task<AdminServices.TingeeMerchantLink?> GetAsync(Guid customerId) => Task.FromResult(Links.TryGetValue(customerId, out var l) ? l : null);
        public Task<AdminServices.TingeeLinkOutcome> LinkAsync(AdminServices.TingeeMerchantLink link, Guid actor)
        {
            LastActor = actor;
            if (FailLink) return Task.FromResult(AdminServices.TingeeLinkOutcome.AuditUnavailable);
            if (Links.ContainsKey(link.CustomerId)) return Task.FromResult(AdminServices.TingeeLinkOutcome.CustomerAlreadyLinked);
            if (Links.Values.Any(l => l.MerchantId == link.MerchantId)) return Task.FromResult(AdminServices.TingeeLinkOutcome.MerchantAlreadyLinked);
            Links[link.CustomerId] = link;
            return Task.FromResult(AdminServices.TingeeLinkOutcome.Saved);
        }
        public Task<AdminServices.TingeeLinkOutcome> UnlinkAsync(Guid customerId, Guid actor) =>
            Task.FromResult(Links.Remove(customerId) ? AdminServices.TingeeLinkOutcome.Saved : AdminServices.TingeeLinkOutcome.NotLinked);
    }

    public sealed class RecordingTingee(bool configured) : ITingeeApi
    {
        public bool IsConfigured => configured;
        public TingeeApiException? Error { get; set; }
        public List<TingeeCreateMerchantRequest> Created { get; } = [];
        public Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default)
        {
            if (Error is not null) throw Error;
            Created.Add(request);
            return Task.FromResult(new TingeeMerchantCreated { MerchantId = 4242 });
        }
        public Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Actor.ToString()), new Claim(ClaimTypes.Name, "safe-tingee"), new Claim("display_name", "Safe Tingee"), new Claim(ClaimTypes.Role, "SYS_ADMIN") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
