extern alias admin;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Admin → Hóa đơn Sandbox: Tingee e-invoice API through a recording fake on top of the Tingee Sandbox test host. No SQL, no network.
public sealed class SafeAdminTingeeInvoiceSandboxTests
{
    [Fact]
    public async Task UnconfiguredPageCallsNothing()
    {
        using var f = Factory(out var invoices, configured: false);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/tingeeinvoicesandbox"));
        Assert.Contains("data-ta-invoice-unconfigured", html);
        Assert.Empty(invoices.Calls);
    }

    [Fact]
    public async Task PageListsProviderAccountsAndIsInTheTingeeMenu()
    {
        using var f = Factory(out var invoices);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/tingeeinvoicesandbox"));
        Assert.Contains("data-ta-invoice-account-list", html);
        Assert.Contains("#77", html);
        Assert.Contains("0101234567", html);
        Assert.Contains("Hóa đơn Sandbox", html);
        Assert.Contains("data-ta-invoice-production", html);
        Assert.Equal(["get-paging-account", "get-provider"], invoices.Calls);
        Assert.Equal([0L], invoices.Merchants.Distinct());   // Direct: TingeeInvoiceApi adds Tingee:MerchantId itself
    }

    [Fact]
    public async Task SaveAccountSendsThePasswordToTingeeAndNeverEchoesIt()
    {
        using var f = Factory(out var invoices);
        invoices.SaveError = new TingeeApiException("2037", "Invalid provider account");
        var c = Client(f);
        var response = await Post(c, "/tingeeinvoicesandbox/saveaccount", new()
        {
            ["Account.Provider"] = "misa", ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "demo@company.vn", ["Account.Password"] = "S3cret-Provider!", ["Account.IsDefault"] = "true"
        });
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Tingee trả mã 2037", html);
        Assert.DoesNotContain("S3cret-Provider!", html);
        var sent = Assert.Single(invoices.Saved);
        Assert.Equal("misa", sent.Provider);
        Assert.Equal("S3cret-Provider!", sent.Password);
        Assert.True(sent.IsDefault);

        invoices.SaveError = null;
        response = await Post(c, "/tingeeinvoicesandbox/saveaccount", new()
        {
            ["Account.Provider"] = "misa", ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "demo@company.vn", ["Account.Password"] = "S3cret-Provider!"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task UnknownProviderIsRejectedBeforeTingee()
    {
        using var f = Factory(out var invoices);
        var response = await Post(Client(f), "/tingeeinvoicesandbox/saveaccount", new()
        {
            ["Account.Provider"] = "evil", ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "u", ["Account.Password"] = "p"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invoices.Saved);
    }

    [Fact]
    public async Task TemplatesAreListedForTheChosenAccount()
    {
        using var f = Factory(out var invoices);
        var html = WebUtility.HtmlDecode(await (await Post(Client(f), "/tingeeinvoicesandbox/templates", new() { ["accountId"] = "77" })).Content.ReadAsStringAsync());
        Assert.Contains("data-ta-invoice-templates", html);
        Assert.Contains("data-series=\"C26TAB\"", html);
        Assert.Equal((77L, (int?)null), invoices.TemplateCalls.Single());
    }

    [Fact]
    public async Task DraftBuildsTheTingeeBodyAndShowsThePdf()
    {
        using var f = Factory(out var invoices);
        var html = WebUtility.HtmlDecode(await (await Post(Client(f), "/tingeeinvoicesandbox/draft", Invoice())).Content.ReadAsStringAsync());
        Assert.Contains("data-ta-invoice-draft", html);
        Assert.Contains("data-pdf=\"JVBERi0xLjQK\"", html);
        var body = Assert.Single(invoices.Drafts);
        Assert.Equal(77, body.AccountId);
        Assert.Equal("2026-11-04", body.InvoiceDate);
        Assert.Equal("C26TAB", body.InvoiceSeries);
        var item = Assert.Single(body.Items);                       // the empty second line is dropped
        Assert.Equal(("Dịch vụ kế toán", "1.5", "100000", "8"), (item.ItemName, item.Quantity, item.UnitPrice, item.TaxRateCode));
        Assert.Empty(invoices.Issued);
    }

    [Fact]
    public async Task IssueNeedsTheConfirmationBox()
    {
        using var f = Factory(out var invoices);
        var c = Client(f);
        var refused = await Post(c, "/tingeeinvoicesandbox/issue", Invoice());
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Empty(invoices.Issued);

        var form = Invoice();
        form["Invoice.ConfirmIssue"] = "true";
        var html = WebUtility.HtmlDecode(await (await Post(c, "/tingeeinvoicesandbox/issue", form)).Content.ReadAsStringAsync());
        Assert.Contains("data-ta-invoice-issued", html);
        Assert.Contains("C26TAB0000001", html);
        Assert.Equal("TASBX-TEST-0001", Assert.Single(invoices.Issued).InvoiceCode);
        Assert.Matches(new Regex("name=\"Invoice.Code\" value=\"TASBX-\\d{12}-[0-9A-F]{4}\""), html); // a fresh code for the next test
    }

    [Fact]
    public async Task DownloadReturnsThePdf()
    {
        using var f = Factory(out _);
        var response = await Post(Client(f), "/tingeeinvoicesandbox/download", new() { ["invoiceCode"] = "TASBX-TEST-0001", ["view"] = "false" });
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-1.4\n", System.Text.Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task NonAdminsCannotOpenThePage()
    {
        using var f = Factory(out var invoices, admin: false);
        var response = await Client(f).GetAsync("/tingeeinvoicesandbox");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(invoices.Calls);
    }

    private static Dictionary<string, string> Invoice() => new()
    {
        ["Invoice.AccountId"] = "77", ["Invoice.Pattern"] = "1", ["Invoice.Series"] = "C26TAB", ["Invoice.Code"] = "TASBX-TEST-0001", ["Invoice.Date"] = "2026-11-04",
        ["Invoice.BuyerName"] = "Nguyễn Văn A", ["Invoice.PaymentMethod"] = "2",
        ["Invoice.Lines[0].Name"] = "Dịch vụ kế toán", ["Invoice.Lines[0].Unit"] = "Tháng", ["Invoice.Lines[0].Quantity"] = "1.5", ["Invoice.Lines[0].UnitPrice"] = "100000", ["Invoice.Lines[0].TaxRate"] = "8",
        ["Invoice.Lines[1].Name"] = "", ["Invoice.Lines[1].Quantity"] = "1", ["Invoice.Lines[1].UnitPrice"] = "0", ["Invoice.Lines[1].TaxRate"] = "10"
    };

    private static async Task<HttpResponseMessage> Post(HttpClient c, string url, Dictionary<string, string> form)
    {
        var page = await c.GetStringAsync("/tingeeinvoicesandbox");
        form["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(out FakeInvoices invoices, bool configured = true, bool admin = true)
    {
        var fake = invoices = new FakeInvoices(configured);
        var host = SafeAdminTingeeSandboxTests.Host(configured, admin);
        return host.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITingeeInvoiceApi>();
            services.AddSingleton<ITingeeInvoiceApi>(fake);
        }));
    }

    public sealed class FakeInvoices(bool configured) : ITingeeInvoiceApi
    {
        public bool IsConfigured => configured;
        public List<string> Calls { get; } = [];
        public List<long> Merchants { get; } = [];
        public List<TingeeInvoiceAccountRequest> Saved { get; } = [];
        public List<(long?, int?)> TemplateCalls { get; } = [];
        public List<TingeeInvoiceRequest> Drafts { get; } = [];
        public List<TingeeInvoiceRequest> Issued { get; } = [];
        public TingeeApiException? SaveError { get; set; }

        public Task<IReadOnlyList<TingeeInvoiceProvider>> GetProvidersAsync(CancellationToken ct = default)
        {
            Calls.Add("get-provider");
            return Task.FromResult<IReadOnlyList<TingeeInvoiceProvider>>([new() { Code = "misa", Name = "Misa", Description = "MISA JSC" }]);
        }
        public Task<TingeePage<TingeeInvoiceAccount>> GetAccountsAsync(long merchantId, CancellationToken ct = default)
        {
            Calls.Add("get-paging-account");
            Merchants.Add(merchantId);
            return Task.FromResult(new TingeePage<TingeeInvoiceAccount> { TotalCount = 1, Items = [new() { Id = 77, Provider = "misa", TaxCode = "0101234567", Username = "demo@company.vn", IsDefault = true, IsActive = true }] });
        }
        public Task<long> SaveAccountAsync(TingeeInvoiceAccountRequest request, CancellationToken ct = default)
        {
            Saved.Add(request);
            return SaveError is not null ? Task.FromException<long>(SaveError) : Task.FromResult(78L);
        }
        public Task DeleteAccountAsync(long merchantId, long accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TingeeInvoiceTemplate>> GetTemplatesAsync(long merchantId, long? accountId, int? invoiceNoToCheck = null, CancellationToken ct = default)
        {
            TemplateCalls.Add((accountId, invoiceNoToCheck));
            return Task.FromResult<IReadOnlyList<TingeeInvoiceTemplate>>([new() { InvoicePattern = "1", InvoiceSeries = "C26TAB" }]);
        }
        public Task<TingeeInvoicePdf> CreateDraftAsync(TingeeInvoiceRequest request, CancellationToken ct = default)
        {
            Drafts.Add(request);
            return Task.FromResult(new TingeeInvoicePdf { InvoiceCode = request.InvoiceCode, PdfBase64 = "JVBERi0xLjQK" });
        }
        public Task<TingeeIssuedInvoice> CreateAndIssueAsync(TingeeInvoiceRequest request, CancellationToken ct = default)
        {
            Issued.Add(request);
            return Task.FromResult(new TingeeIssuedInvoice { InvoiceCode = request.InvoiceCode, InvoiceNumber = "C26TAB0000001", Url = "https://lookup.test/x" });
        }
        public Task<TingeeInvoicePdf> DownloadAsync(long merchantId, string invoiceCode, long? accountId = null, CancellationToken ct = default) =>
            Task.FromResult(new TingeeInvoicePdf { InvoiceCode = invoiceCode, PdfBase64 = "JVBERi0xLjQK" });
        public Task SendEmailAsync(long merchantId, string invoiceCode, string recipientEmail, string? buyerName = null, long? accountId = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
