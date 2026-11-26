using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.SoftDreams;
using TAPortal.Integrations.Tingee;
using static TACustomerPortal.Tests.SafeCustomerEInvoiceTests;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// SoftDreams EasyInvoice, integrated directly (operator 2026-11-26). No network and no SQL: a recording fake of the
// EasyInvoice API, a plain (test-only) secret protector and the in-memory stores.
public sealed class SafeEasyInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 3, 0, 0, TimeSpan.Zero);

    // ---- authentication ------------------------------------------------------------------------------
    [Fact]
    public void TheAuthenticationHeaderFollowsTheDocument()
    {
        var login = new EasyInvoiceCredentials("api-user", "Secret#123", "0109999999", false);
        var at = DateTimeOffset.FromUnixTimeSeconds(1553507490);
        var header = EasyInvoiceAuth.Header("post", login, at, "68cfe3f19b4b4668ae7746a514faf2cd");
        var expected = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes("POST155350749068cfe3f19b4b4668ae7746a514faf2cd")));
        Assert.Equal($"{expected}:68cfe3f19b4b4668ae7746a514faf2cd:1553507490:api-user:Secret#123:0109999999", header);
        Assert.Matches("^[a-f0-9]{32}$", EasyInvoiceAuth.NewNonce());
        Assert.DoesNotContain("Secret#123", login.ToString());
        Assert.DoesNotContain("Secret#123", new EasyInvoiceSecret("Secret#123", true).ToString());
    }

    [Fact]
    public void TheStoredLoginIsEncryptedForThisMachine()
    {
        if (!OperatingSystem.IsWindows()) return;
        var protector = new DpapiInvoiceSecretProtector();
        var data = protector.Protect(new EasyInvoiceSecret("Secret#123", true));
        Assert.DoesNotContain("Secret#123", Encoding.UTF8.GetString(data));
        Assert.Equal(new EasyInvoiceSecret("Secret#123", true), protector.Unprotect(data));
        Assert.Null(protector.Unprotect([1, 2, 3]));   // tampered or from another machine
        Assert.Null(protector.Unprotect(null));
    }

    // ---- responses -----------------------------------------------------------------------------------
    [Fact]
    public void StatusTwoIsSuccessWhetherSentAsNumberOrText()
    {
        Assert.NotNull(EasyInvoiceResponse.Parse("""{"Status":2,"Message":"Ok","ErrorCode":0,"Data":{"Invoices":[]}}""", null, 200));
        Assert.NotNull(EasyInvoiceResponse.Parse("""{"Status":"2","Message":"Ok","ErrorCode":"0","Data":{}}""", null, 200));
    }

    [Fact]
    public void RefusalsCarryTheCodeAndThePerInvoiceDetail()
    {
        var refused = Assert.Throws<EasyInvoiceApiException>(() => EasyInvoiceResponse.Parse(
            """{"Status":"4","Message":"Dữ liệu không hợp lệ","ErrorCode":"E102","Data":{"KeyInvoiceMsg":{"DH1-AB":"Thiếu tên người mua"}}}""", "DH1-AB", 400));
        Assert.Equal(("4", "E102", "Dữ liệu không hợp lệ", "Thiếu tên người mua", true), (refused.Status, refused.Code, refused.ProviderMessage, refused.Detail, refused.Permanent));

        var server = Assert.Throws<EasyInvoiceApiException>(() => EasyInvoiceResponse.Parse("""{"Status":5,"Message":"Có lỗi xảy ra"}""", null, 500));
        Assert.False(server.Permanent);
        Assert.Equal("HTTP500", server.Code);

        Assert.False(Assert.Throws<EasyInvoiceApiException>(() => EasyInvoiceResponse.Parse("<html>502</html>", null, 502)).Permanent);
        Assert.True(Assert.Throws<EasyInvoiceApiException>(() => EasyInvoiceResponse.Parse("Unauthorized", null, 401)).Permanent);
    }

    // Operator 2026-11-27: connecting a real account failed with "(mã 128): Ikeys không tồn tại trong hệ thống". The live
    // service answers an unknown key with ErrorCode 128 after accepting the login: that is "no invoice", not a failure.
    [Theory]
    [InlineData("""{"Status":4,"Message":"Ikeys không tồn tại trong hệ thống","ErrorCode":128,"Data":null}""", 200)]
    [InlineData("""{"Status":"3","Message":"Ikeys không tồn tại trong hệ thống","ErrorCode":"128"}""", 400)]
    public async Task AnUnknownIkeyIsNoInvoiceNotAnError(string body, int http)
    {
        var api = Api(new StubHandler(http, body));
        Assert.Empty(await api.GetByIkeysAsync(new("u", "p", "0109999999", true), ["KIEMTRA-1"]));
    }

    [Fact]
    public async Task OtherLookupErrorsStillFail()
    {
        var api = Api(new StubHandler(200, """{"Status":4,"Message":"Sai tên đăng nhập hoặc mật khẩu","ErrorCode":401}"""));
        var ex = await Assert.ThrowsAsync<EasyInvoiceApiException>(() => api.GetByIkeysAsync(new("u", "p", "0109999999", true), ["KIEMTRA-1"]));
        Assert.Equal("401", ex.Code);
    }

    private static EasyInvoiceApi Api(HttpMessageHandler handler) => new(new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(new EasyInvoiceOptions()), TimeProvider.System, NullLogger<EasyInvoiceApi>.Instance);

    private sealed class StubHandler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public void InvoicesAreReadWithTheirStatusAndLookupCode()
    {
        var data = EasyInvoiceResponse.Parse("""
            {"Status":2,"Message":"Ok","Data":{"Invoices":[{"Ikey":"DH1-AB","InvoiceStatus":1,"No":"0000125","LookupCode":"LK9","LinkView":"https://x/LK9","TCTCheckStatus":-1}]}}
            """, null, 200);
        var invoice = Assert.Single(EasyInvoiceResponse.Invoices(data));
        Assert.Equal(("DH1-AB", 1, "0000125", "LK9", "-1"), (invoice.Ikey, invoice.InvoiceStatus!.Value, invoice.No, invoice.LookupCode, invoice.TaxCheckStatus));
        Assert.True(invoice.IsIssued);
        Assert.True((invoice with { InvoiceStatus = 5 }).IsCancelled);
        Assert.False((invoice with { InvoiceStatus = 0 }).IsIssued);
        Assert.Empty(EasyInvoiceResponse.Invoices(null));
    }

    [Fact]
    public void ThePdfIsFoundInAJsonAnswer()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4 test");
        Assert.Equal(pdf, EasyInvoiceResponse.FindPdf(EasyInvoiceResponse.Parse($$"""{"Status":2,"Data":"{{Convert.ToBase64String(pdf)}}"}""", null, 200)));
        var wrapped = "{\"Status\":2,\"Data\":{\"File\":\"" + Convert.ToBase64String(pdf) + "\"}}";
        Assert.Equal(pdf, EasyInvoiceResponse.FindPdf(EasyInvoiceResponse.Parse(wrapped, null, 200)));
        Assert.Null(EasyInvoiceResponse.FindPdf(EasyInvoiceResponse.Parse("""{"Status":2,"Data":{"File":"bm90IGEgcGRm"}}""", null, 200)));
    }

    // ---- invoice content -----------------------------------------------------------------------------
    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(15, "Mười lăm đồng")]
    [InlineData(21, "Hai mươi mốt đồng")]
    [InlineData(105, "Một trăm linh năm đồng")]
    [InlineData(75_600, "Bảy mươi lăm nghìn sáu trăm đồng")]
    [InlineData(1_000_005, "Một triệu không trăm linh năm đồng")]
    [InlineData(1_250_000, "Một triệu hai trăm năm mươi nghìn đồng")]
    [InlineData(21_200_000, "Hai mươi mốt triệu hai trăm nghìn đồng")]
    [InlineData(1_000_000_000, "Một tỷ đồng")]
    [InlineData(2_034_014, "Hai triệu không trăm ba mươi tư nghìn không trăm mười bốn đồng")]
    public void AmountsAreWrittenInVietnameseWords(long amount, string words) => Assert.Equal(words, VietnameseMoney.Words(amount));

    [Fact]
    public void TheInvoiceCarriesTheAmountsTheBuyerPaid()
    {
        // Priced with VAT: 2 × 35 000 = 70 000 paid, 64 815 before tax + 5 185 VAT 8% (not 2 × 32 407).
        var withTax = OrderMath.LineWithTax(1, "Cà phê sữa", "Ly", 2, 35_000, "8");
        var job = Job("BUSINESS", "Anh Nam", "Công ty TNHH Nam Phát & Cộng sự", "0101234567", "ketoan@namphat.vn",
            [new(1, withTax.ItemName, withTax.UnitName, withTax.Quantity, withTax.UnitPrice, withTax.TaxRateCode, withTax.AmountBeforeTax, withTax.TaxAmount),
             new(2, "Phí giao hàng <nội thành>", null, 1, 20_000, "-1")]);
        var xml = XElement.Parse(EasyInvoiceXml.Build(job, new DateOnly(2026, 11, 26), sendEmail: true));
        var inv = xml.Element("Inv")!.Element("Invoice")!;
        string V(XElement e, string name) => e.Element(name)!.Value;

        Assert.Equal(("DH2611260001-ABCDEF12", "Công ty TNHH Nam Phát & Cộng sự", "Anh Nam", "0101234567", "ketoan@namphat.vn"),
            (V(inv, "Ikey"), V(inv, "CusName"), V(inv, "Buyer"), V(inv, "CusTaxCode"), V(inv, "CusEmails")));
        Assert.Equal(("Chuyển khoản", "26/11/2026", "VND"), (V(inv, "PaymentMethod"), V(inv, "ArisingDate"), V(inv, "CurrencyUnit")));
        var lines = inv.Element("Products")!.Elements("Product").ToList();
        Assert.Equal(("64815", "8", "5185", "70000"), (V(lines[0], "Total"), V(lines[0], "VATRate"), V(lines[0], "VATAmount"), V(lines[0], "Amount")));
        Assert.Equal(("Phí giao hàng <nội thành>", "-1", "0", "20000"), (V(lines[1], "ProdName"), V(lines[1], "VATRate"), V(lines[1], "VATAmount"), V(lines[1], "Amount")));
        Assert.Equal(("84815", "5185", "90000"), (V(inv, "Total"), V(inv, "VATAmount"), V(inv, "Amount")));
        Assert.Equal(("64815", "5185", "70000", "20000"), (V(inv, "GrossValue8"), V(inv, "VatAmount8"), V(inv, "Amount8"), V(inv, "GrossValue")));
        Assert.Equal("Chín mươi nghìn đồng", V(inv, "AmountInWords"));
        Assert.Contains("DH261126-0001", V(inv, "Note"));

        // Without the email setting the buyer's address is not sent (EasyInvoice would email it).
        Assert.Null(XElement.Parse(EasyInvoiceXml.Build(job, new DateOnly(2026, 11, 26), sendEmail: false)).Descendants("CusEmails").SingleOrDefault());
    }

    [Fact]
    public void RetailAndAnonymousBuyersAreNamedAsOnOtherProviders()
    {
        var retail = XElement.Parse(EasyInvoiceXml.Build(Job("INDIVIDUAL", "Chị Lan", null, null, null), new DateOnly(2026, 11, 26), true)).Descendants("Invoice").Single();
        Assert.Equal("Chị Lan", retail.Element("Buyer")!.Value);
        Assert.Null(retail.Element("CusName"));
        Assert.Null(retail.Element("CusTaxCode"));
        var nobody = XElement.Parse(EasyInvoiceXml.Build(Job(null, null, null, null, null), new DateOnly(2026, 11, 26), true)).Descendants("Invoice").Single();
        Assert.Equal(OrderInvoiceRules.AnonymousBuyer, nobody.Element("Buyer")!.Value);
        Assert.Equal("1C26TAA", EasyInvoiceXml.Pattern(Connected(Guid.NewGuid()) with { InvoiceSeries = "c26taa" }));
    }

    // ---- issuing -------------------------------------------------------------------------------------
    [Fact]
    public async Task APaidOrderIsIssuedAndSignedOnEasyInvoice()
    {
        var (store, api, invoicer, customer, order) = Setup(buyerEmail: "ketoan@namphat.vn");
        Assert.Equal(1, await invoicer.RunOnceAsync());
        var o = store.Order(order);
        Assert.Equal((OrderInvoiceStatus.Issued, "0000125"), (o.InvoiceStatus, o.InvoiceNumber));
        Assert.NotNull(o.InvoiceEmailedAt);   // EasyInvoice sends it (CusEmails)
        var call = Assert.Single(api.Issued);
        Assert.Equal(("1C26TAA", o.InvoiceCode, "api-user", "Secret#123", false), (call.Pattern, call.Ikey, call.Login.Username, call.Login.Password, call.Login.Demo));
        Assert.Contains("<CusEmails>ketoan@namphat.vn</CusEmails>", call.Xml);
        Assert.Equal([o.InvoiceCode!], api.Looked);   // looked up first: an earlier lost answer would be found
    }

    [Fact]
    public async Task AnInvoiceIssuedByALostAttemptIsNotIssuedAgain()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.Existing = ikey => new(ikey, 1, "0000099", "LK1", null, null, null, null);
        var outcome = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.True(outcome.Issued);
        Assert.Empty(api.Issued);
        Assert.Equal(("0000099", "Hóa đơn đã được phát hành ở lần xuất trước."), (store.Order(order).InvoiceNumber, store.Order(order).InvoiceError));
    }

    [Fact]
    public async Task RefusalsFailAndServerErrorsAreRetried()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.IssueErrors.Enqueue(new EasyInvoiceApiException("5", "E500", "Có lỗi xảy ra"));
        Assert.Equal(OrderInvoiceStatus.Pending, (await invoicer.IssueAsync(customer, order, manual: false)).Status);
        Assert.True(store.NextAttempt.ContainsKey(order));

        api.IssueErrors.Enqueue(new EasyInvoiceApiException("4", "E102", "Dữ liệu không hợp lệ", "Thiếu địa chỉ người mua"));
        store.NextAttempt.Clear();
        var refused = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.Equal(OrderInvoiceStatus.Failed, refused.Status);
        Assert.Contains("mã E102", store.Order(order).InvoiceError);
        Assert.Contains("Thiếu địa chỉ người mua", store.Order(order).InvoiceError);
    }

    [Fact]
    public async Task DraftModeCreatesAnUnsignedInvoiceOnly()
    {
        var (store, api, invoicer, customer, order) = Setup();
        store.Settings[customer] = store.Settings[customer] with { IssueMode = InvoiceIssueModes.Draft };
        var outcome = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.Equal(OrderInvoiceStatus.Draft, outcome.Status);
        Assert.Single(api.Drafts);
        Assert.Empty(api.Issued);
        Assert.Contains("SoftDreams EasyInvoice", store.Order(order).InvoiceError);
    }

    [Fact]
    public async Task WithoutAReadableLoginTheCompanyIsAskedToConnectAgain()
    {
        var (store, api, invoicer, customer, order) = Setup();
        store.Settings[customer] = store.Settings[customer] with { ProviderSecret = [1, 2, 3] };   // e.g. encrypted on another server
        Assert.True(store.Settings[customer].IsConnected);   // stored, but unreadable here
        var outcome = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.Equal(OrderInvoiceStatus.Failed, outcome.Status);
        Assert.Contains("kết nối lại", store.Order(order).InvoiceError);
        Assert.Empty(api.Issued);
    }

    [Fact]
    public void SoftDreamsIsConnectedByItsStoredLoginNotAPartnerAccount()
    {
        var c = Guid.NewGuid();
        Assert.True(Connected(c).IsConnected);
        Assert.False((Connected(c) with { ProviderSecret = null }).IsConnected);
        Assert.False((Connected(c) with { ProviderSecret = null, ProviderAccountId = 77 }).IsConnected);
        Assert.False(CustomerInvoiceProviders.SoftDreams.ViaPartner);
        Assert.False(CustomerInvoiceProviders.SoftDreams.HasTemplates);
        Assert.Contains(CustomerInvoiceProviders.SoftDreams, CustomerInvoiceProviders.All);
    }

    // ---- Customer Portal -----------------------------------------------------------------------------
    [Fact]
    public async Task TheCompanyConnectsEasyInvoiceAndThePasswordIsKeptEncrypted()
    {
        var invoices = new MemoryInvoiceStore();
        var partner = new FakeInvoiceApi();
        var easy = new FakeEasyInvoice();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, invoiceApi: partner, easyInvoice: easy);
        payments.Merchants[A] = 9001;
        invoices.Settings[A] = new(A, TingeeInvoiceProviders.Misa, 77, "0109999999", "ketoan@seller.vn", "1", "C26TAA", true, true, Now.UtcDateTime, Now.UtcDateTime);
        var c = Client(f);
        var r = await Post(c, "/einvoice/connect", await Token(c, "/einvoice"), new()
        {
            ["Account.Provider"] = CustomerInvoiceProviders.SoftDreamsCode, ["Account.TaxCode"] = "0108888888", ["Account.Username"] = "api-user",
            ["Account.Password"] = "Secret#123", ["Account.Demo"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var check = Assert.Single(easy.LoginChecks);
        Assert.Equal(("api-user", "Secret#123", "0108888888", true), (check.Username, check.Password, check.TaxCode, check.Demo));
        Assert.Empty(partner.Saved);          // no account at the partner
        Assert.Equal([77L], partner.Deleted);  // the MISA one is removed
        var s = invoices.Settings[A];
        Assert.Equal((CustomerInvoiceProviders.SoftDreamsCode, (long?)null, null, false), (s.Provider, s.ProviderAccountId, s.InvoicePattern, s.AutoIssueEnabled));
        Assert.NotNull(s.ProviderSecret);
        Assert.DoesNotContain("Secret#123", Encoding.UTF8.GetString(s.ProviderSecret!));   // never kept as plain text
        Assert.Equal(new EasyInvoiceSecret("Secret#123", true), new PlainInvoiceProtector().Unprotect(s.ProviderSecret));

        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice"));
        Assert.Contains("data-cp-einvoice-provider>SoftDreams EasyInvoice", page);
        Assert.Contains("data-cp-einvoice-env>Môi trường thử (demo)", page);
        Assert.Contains("data-cp-template-manual", page);
        Assert.DoesNotContain("Lấy mẫu hóa đơn từ EasyInvoice", page);
        Assert.DoesNotContain("Secret#123", page);
        Assert.DoesNotContain("Tingee", page, StringComparison.OrdinalIgnoreCase);

        // Manual template: mẫu số 1 + ký hiệu C26TAA, then automatic signed invoices.
        r = await Post(c, "/einvoice/save", await Token(c, "/einvoice"), new()
        {
            ["Template.Pattern"] = "1", ["Template.Series"] = "c26taa", ["Template.IssueMode"] = InvoiceIssueModes.Signed, ["Template.SendEmail"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.True(invoices.Settings[A].IsReady);
        Assert.Equal("1C26TAA", EasyInvoiceXml.Pattern(invoices.Settings[A]));

        // Disconnecting forgets the login.
        r = await Post(c, "/einvoice/disconnect", await Token(c, "/einvoice"), []);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Null(invoices.Settings[A].ProviderSecret);
        Assert.Equal([77L], partner.Deleted);   // nothing more removed at the partner
    }

    [Fact]
    public async Task AWrongEasyInvoiceLoginIsNotSaved()
    {
        var invoices = new MemoryInvoiceStore();
        var easy = new FakeEasyInvoice();
        easy.LoginErrors.Enqueue(new EasyInvoiceApiException("4", "E401", "Sai tên đăng nhập hoặc mật khẩu"));
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, easyInvoice: easy);
        payments.Merchants[A] = 9001;
        var c = Client(f);
        var r = await Post(c, "/einvoice/connect", await Token(c, "/einvoice"), new()
        {
            ["Account.Provider"] = CustomerInvoiceProviders.SoftDreamsCode, ["Account.TaxCode"] = "0108888888", ["Account.Username"] = "api-user", ["Account.Password"] = "wrong"
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Sai tên đăng nhập hoặc mật khẩu", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.False(invoices.Settings.ContainsKey(A));

        // The separator of the Authentication header cannot be part of the login.
        r = await Post(c, "/einvoice/connect", await Token(c, "/einvoice"), new()
        {
            ["Account.Provider"] = CustomerInvoiceProviders.SoftDreamsCode, ["Account.TaxCode"] = "0108888888", ["Account.Username"] = "api-user", ["Account.Password"] = "a:b"
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Single(easy.LoginChecks);
    }

    [Fact]
    public async Task TheIssuedInvoicePdfComesFromEasyInvoice()
    {
        var invoices = new MemoryInvoiceStore();
        var easy = new FakeEasyInvoice();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, easyInvoice: easy);
        invoices.Payments = payments;
        payments.Merchants[A] = 9001;
        invoices.Settings[A] = Connected(A);
        var order = PaidOrder() with { InvoiceStatus = OrderInvoiceStatus.Issued, InvoiceCode = "DH2611260001-ABCDEF12", InvoiceNumber = "0000125" };
        payments.Orders.Add((A, order));
        var r = await Client(f).GetAsync($"/orders/invoicepdf/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal([("1C26TAA", "DH2611260001-ABCDEF12")], easy.Pdfs);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    internal static CustomerInvoiceSettings Connected(Guid customer) =>
        new(customer, CustomerInvoiceProviders.SoftDreamsCode, null, "0109999999", "api-user", "1", "C26TAA", true, true, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(-1),
            InvoiceIssueModes.Signed, ProviderSecret: new PlainInvoiceProtector().Protect(new EasyInvoiceSecret("Secret#123", false)));

    private static OrderInvoiceJob Job(string? type, string? name, string? legal, string? tax, string? email, IReadOnlyList<OrderInvoiceLine>? lines = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "DH261126-0001", "Bàn 5", "DH2611260001-ABCDEF12", 1, null, type, name, legal, tax, "1 Lê Lợi", "0901234567", email,
            lines ?? [new(1, "Cà phê sữa", "Ly", 2, 35_000, "8")]);

    private static Order PaidOrder() =>
        new(Guid.NewGuid(), "DH261126-0001", null, null, null, "Bàn 5", 75_600, 75_600, OrderStatus.Paid, null, null, null, null, 1440, null, true, OrderInvoiceStatus.Pending, null,
            Now.UtcDateTime.AddHours(-1), [OrderMath.Line(1, "Cà phê sữa", "Ly", 2, 35_000, "8")]);

    private static (MemoryInvoiceStore Store, FakeEasyInvoice Api, OrderInvoicer Invoicer, Guid Customer, Guid Order) Setup(string? buyerEmail = null)
    {
        var payments = new MemoryPaymentStore();
        var customer = Guid.NewGuid();
        var contact = new Contact(Guid.NewGuid(), "KH001", "Anh Nam", "Công ty TNHH Nam Phát", "0101234567", null, buyerEmail, null, null, true, BuyerTypes.Business);
        payments.Contacts.Add((customer, contact));   // no merchant: EasyInvoice does not need the payment partner
        var order = PaidOrder() with { ContactId = contact.Id };
        payments.Orders.Add((customer, order));
        var store = new MemoryInvoiceStore { Payments = payments, PaidAt = { [order.Id] = Now.UtcDateTime } };
        store.Settings[customer] = Connected(customer);
        var api = new FakeEasyInvoice();
        var invoicer = new OrderInvoicer(store, new FakeInvoiceApi(), new FixedClock(Now), NullLogger<OrderInvoicer>.Instance, api, new PlainInvoiceProtector());
        return (store, api, invoicer, customer, order.Id);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Test-only "encryption": a marker plus reversed bytes, so the stored value never shows the password as is.</summary>
    public sealed class PlainInvoiceProtector : IInvoiceSecretProtector
    {
        public byte[] Protect(EasyInvoiceSecret secret) => [0xEE, .. System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(secret).Reverse()];
        public EasyInvoiceSecret? Unprotect(byte[]? data) =>
            data is [0xEE, .. var rest] ? System.Text.Json.JsonSerializer.Deserialize<EasyInvoiceSecret>(rest.Reverse().ToArray()) : null;
    }

    public sealed record Call(EasyInvoiceCredentials Login, string Pattern, string Xml, string Ikey);

    public sealed class FakeEasyInvoice : IEasyInvoiceApi
    {
        public List<Call> Issued { get; } = [];
        public List<Call> Drafts { get; } = [];
        public List<string> Looked { get; } = [];
        public List<EasyInvoiceCredentials> LoginChecks { get; } = [];
        public List<(string Pattern, string Ikey)> Pdfs { get; } = [];
        public Queue<Exception> IssueErrors { get; } = new();
        public Queue<Exception> LoginErrors { get; } = new();
        public Func<string, EasyInvoiceInfo?> Existing { get; set; } = _ => null;

        public Task<EasyInvoiceIssued> ImportAndIssueAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default)
        {
            Issued.Add(new(login, pattern, xmlData, ikey));
            return IssueErrors.TryDequeue(out var e) ? Task.FromException<EasyInvoiceIssued>(e) : Task.FromResult(new EasyInvoiceIssued("0000125", "LK125", null));
        }

        public Task ImportDraftAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default)
        {
            Drafts.Add(new(login, pattern, xmlData, ikey));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EasyInvoiceInfo>> GetByIkeysAsync(EasyInvoiceCredentials login, IReadOnlyList<string> ikeys, CancellationToken ct = default)
        {
            if (ikeys.Single().StartsWith("KIEMTRA-", StringComparison.Ordinal))
            {
                LoginChecks.Add(login);
                if (LoginErrors.TryDequeue(out var e)) return Task.FromException<IReadOnlyList<EasyInvoiceInfo>>(e);
                return Task.FromResult<IReadOnlyList<EasyInvoiceInfo>>([]);
            }
            Looked.AddRange(ikeys);
            return Task.FromResult<IReadOnlyList<EasyInvoiceInfo>>(ikeys.Select(k => Existing(k)).OfType<EasyInvoiceInfo>().ToList());
        }

        public Task<byte[]> GetPdfAsync(EasyInvoiceCredentials login, string pattern, string ikey, CancellationToken ct = default)
        {
            Pdfs.Add((pattern, ikey));
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.4 test"));
        }
    }
}
