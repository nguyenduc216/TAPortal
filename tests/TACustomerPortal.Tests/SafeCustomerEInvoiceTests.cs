using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// Automatic MISA e-invoice for paid QR orders (operator request 2026-11-06). No SQL and no network: an in-memory invoice
// store over the in-memory orders and a recording fake of the e-invoice API.
public sealed class SafeCustomerEInvoiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);

    // ---- invoice content -------------------------------------------------------------------------------
    [Fact]
    public void TheInvoiceCodeIsFixedPerOrderAndUniqueAcrossCompanies()
    {
        var id = Guid.Parse("3f2a9c1b-0000-0000-0000-000000000001");
        Assert.Equal("DH2611060001-3F2A9C1B", OrderInvoiceRules.InvoiceCode("DH261106-0001", id));
        Assert.Equal(OrderInvoiceRules.InvoiceCode("DH261106-0001", id), OrderInvoiceRules.InvoiceCode("DH261106-0001", id));
        Assert.NotEqual(OrderInvoiceRules.InvoiceCode("DH261106-0001", id), OrderInvoiceRules.InvoiceCode("DH261106-0001", Guid.NewGuid()));
        Assert.True(OrderInvoiceRules.InvoiceCode("DH261106-0001", Guid.NewGuid()).Length <= 40);
    }

    [Fact]
    public void BuyersAreInvoicedByTheirKind()
    {
        var settings = Ready(Guid.NewGuid());
        var date = new DateOnly(2026, 11, 6);
        var business = OrderInvoiceRules.Build(Job("BUSINESS", "Anh Nam", "Công ty TNHH Nam Phát", "0101234567", "ketoan@namphat.vn"), settings, date);
        Assert.Equal(("Công ty TNHH Nam Phát", "0101234567", null), (business.BuyerLegalName, business.BuyerTaxCode, business.BuyerName));
        Assert.Equal(("1", "C26TAA", 77L, 2, "2026-11-06"), (business.InvoicePattern, business.InvoiceSeries, business.AccountId!.Value, business.PaymentMethod!.Value, business.InvoiceDate));
        Assert.Equal(("Cà phê sữa", "Ly", "2", "35000", "8"), (business.Items[0].ItemName, business.Items[0].UnitName, business.Items[0].Quantity, business.Items[0].UnitPrice, business.Items[0].TaxRateCode));
        Assert.Contains("DH261106-0001", business.Note);

        var retail = OrderInvoiceRules.Build(Job("INDIVIDUAL", "Chị Lan", null, null, null), settings, date);
        Assert.Equal(("Chị Lan", null, null), (retail.BuyerName, retail.BuyerLegalName, retail.BuyerTaxCode));

        var household = OrderInvoiceRules.Build(Job("HOUSEHOLD", "Quán Bà Tư", null, "8001234567", null), settings, date);
        Assert.Equal(("Quán Bà Tư", "Quán Bà Tư", "8001234567"), (household.BuyerName, household.BuyerLegalName, household.BuyerTaxCode));

        var anonymous = OrderInvoiceRules.Build(Job(null, null, null, null, null), settings, date);
        Assert.Equal(OrderInvoiceRules.AnonymousBuyer, anonymous.BuyerName);
        Assert.Null(anonymous.BuyerTaxCode);
    }

    // ---- issuing -------------------------------------------------------------------------------------
    [Fact]
    public async Task APaidOrderIsIssuedOnceAndEmailedToTheBuyer()
    {
        var (store, api, invoicer, customer, order) = Setup(buyerEmail: "ketoan@namphat.vn");
        Assert.Equal(1, await invoicer.RunOnceAsync());
        var o = store.Order(order);
        Assert.Equal((OrderInvoiceStatus.Issued, "0000123"), (o.InvoiceStatus, o.InvoiceNumber));
        Assert.NotNull(o.InvoiceEmailedAt);
        Assert.Single(api.Issued);
        Assert.Equal(o.InvoiceCode, api.Issued[0].InvoiceCode);
        Assert.Equal(["ketoan@namphat.vn"], api.Emails);
        // Issued orders are never due again.
        Assert.Equal(0, await invoicer.RunOnceAsync());
        Assert.Single(api.Issued);
        Assert.False((await invoicer.IssueAsync(customer, order, manual: true)).Issued);
        Assert.Single(api.Issued);
    }

    [Fact]
    public async Task OnlyOrdersPaidAfterAutoIssueWasTurnedOnAreInvoicedAutomatically()
    {
        var (store, api, invoicer, customer, order) = Setup();
        store.Settings[customer] = store.Settings[customer] with { AutoIssueFrom = Now.UtcDateTime.AddMinutes(5) };
        Assert.Equal(0, await invoicer.RunOnceAsync());
        store.Settings[customer] = store.Settings[customer] with { AutoIssueFrom = Now.UtcDateTime.AddDays(-1), AutoIssueEnabled = false };
        Assert.Equal(0, await invoicer.RunOnceAsync());
        store.Settings[customer] = store.Settings[customer] with { AutoIssueEnabled = true };
        store.Update(order, o => o with { AutoIssueInvoice = false });
        Assert.Equal(0, await invoicer.RunOnceAsync());
        Assert.Empty(api.Issued);
        // By hand it can still be issued.
        Assert.True((await invoicer.IssueAsync(customer, order, manual: true)).Issued);
    }

    [Fact]
    public async Task ALostAnswerNeverIssuesASecondInvoice()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.IssueErrors.Enqueue(new HttpRequestException("timeout"));
        api.IssueErrors.Enqueue(new TingeeApiException("2034", "Invoice code already issued"));
        Assert.Equal(OrderInvoiceStatus.Pending, (await invoicer.IssueAsync(customer, order, manual: false)).Status);
        var first = store.Order(order);
        Assert.Equal(OrderInvoiceStatus.Pending, first.InvoiceStatus);
        Assert.Equal(Now.UtcDateTime.AddMinutes(1), store.NextAttempt[order]);
        Assert.Equal(0, await invoicer.RunOnceAsync());   // waiting for the retry time
        store.NextAttempt[order] = Now.UtcDateTime;
        Assert.Equal(1, await invoicer.RunOnceAsync());
        var o = store.Order(order);
        Assert.Equal(OrderInvoiceStatus.Issued, o.InvoiceStatus);
        Assert.Equal(first.InvoiceCode, o.InvoiceCode);
        Assert.All(api.Issued, r => Assert.Equal(first.InvoiceCode, r.InvoiceCode));
        Assert.Contains("lần xuất trước", o.InvoiceError);
    }

    [Fact]
    public async Task RefusalsFailWithTheReasonAndCanBeIssuedAgainByHand()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.IssueErrors.Enqueue(new TingeeApiException("2037", "Tingee: invalid login"));
        var failed = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.Equal(OrderInvoiceStatus.Failed, failed.Status);
        Assert.Contains("2037", store.Order(order).InvoiceError);
        Assert.Contains("sai thông tin đăng nhập MISA", store.Order(order).InvoiceError);
        Assert.Equal(0, await invoicer.RunOnceAsync());   // failed orders wait for the company
        Assert.True((await invoicer.IssueAsync(customer, order, manual: true)).Issued);
        Assert.Equal(2, api.Issued.Count);
        Assert.Equal(api.Issued[0].InvoiceCode, api.Issued[1].InvoiceCode);
    }

    [Fact]
    public async Task TemporaryFailuresStopRetryingAfterTheLimit()
    {
        var (store, api, invoicer, customer, order) = Setup();
        for (var i = 0; i < OrderInvoicer.MaxAutoAttempts; i++) api.IssueErrors.Enqueue(new HttpRequestException("down"));
        for (var i = 0; i < OrderInvoicer.MaxAutoAttempts; i++)
        {
            store.NextAttempt.Remove(order);
            await invoicer.RunOnceAsync();
        }
        Assert.Equal(OrderInvoiceStatus.Failed, store.Order(order).InvoiceStatus);
        Assert.Equal(OrderInvoicer.MaxAutoAttempts, api.Issued.Count);
    }

    [Fact]
    public async Task NothingIsIssuedWithoutAConnectedAccountAndTemplate()
    {
        var (store, api, invoicer, customer, order) = Setup();
        store.Settings[customer] = store.Settings[customer] with { InvoiceSeries = null };
        Assert.Equal(OrderInvoiceStatus.None, (await invoicer.IssueAsync(customer, order, manual: true)).Status);
        Assert.Equal(0, await invoicer.RunOnceAsync());
        Assert.Empty(api.Issued);
    }

    // Operator 2026-11-07: MISA refuses to sign (400 "CallSignServiceFail", no certificate usable for API issuing). The
    // order must not show an error: a draft with the same invoice code is created and the order is DRAFT.
    [Fact]
    public async Task WhenSigningFailsADraftIsCreatedInsteadOfAnError()
    {
        var (store, api, invoicer, customer, order) = Setup(buyerEmail: "ketoan@namphat.vn");
        api.IssueErrors.Enqueue(new TingeeApiException("400", "CallSignServiceFail"));
        var outcome = await invoicer.IssueAsync(customer, order, manual: false);
        Assert.Equal(OrderInvoiceStatus.Draft, outcome.Status);
        var o = store.Order(order);
        Assert.Equal(OrderInvoiceStatus.Draft, o.InvoiceStatus);
        Assert.Equal(OrderInvoiceRules.DraftNote, o.InvoiceError);
        var draft = Assert.Single(api.Drafts);
        Assert.Equal((o.InvoiceCode, api.Issued[0].InvoiceCode), (draft.InvoiceCode, draft.InvoiceCode));
        Assert.Empty(api.Emails);                           // a draft is not sent to the buyer
        Assert.Equal(0, await invoicer.RunOnceAsync());     // and is not retried automatically
        Assert.Single(api.Issued);
    }

    [Fact]
    public async Task ADraftCanBeIssuedAgainByHandOnceSigningWorks()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.IssueErrors.Enqueue(new TingeeApiException("400", "CallSignServiceFail"));
        await invoicer.IssueAsync(customer, order, manual: false);
        // Still no usable certificate: the draft already holds the code (2034) and the order stays DRAFT.
        api.IssueErrors.Enqueue(new TingeeApiException("400", "CallSignServiceFail"));
        api.DraftErrors.Enqueue(new TingeeApiException("2034", "Invoice code already exists"));
        Assert.Equal(OrderInvoiceStatus.Draft, (await invoicer.IssueAsync(customer, order, manual: true)).Status);
        // The code is taken by the draft: never reported as issued.
        api.IssueErrors.Enqueue(new TingeeApiException("2034", "Invoice code already exists"));
        Assert.Equal(OrderInvoiceStatus.Draft, (await invoicer.IssueAsync(customer, order, manual: true)).Status);
        Assert.Equal(OrderInvoiceStatus.Draft, store.Order(order).InvoiceStatus);
        // Signing works now.
        var issued = await invoicer.IssueAsync(customer, order, manual: true);
        Assert.Equal((OrderInvoiceStatus.Issued, "0000123"), (store.Order(order).InvoiceStatus, issued.InvoiceNumber));
        Assert.All(api.Issued.Concat(api.Drafts), r => Assert.Equal(store.Order(order).InvoiceCode, r.InvoiceCode));
    }

    [Fact]
    public async Task OnlySigningFailuresBecomeDraftsAndAFailedDraftIsReported()
    {
        var (store, api, invoicer, customer, order) = Setup();
        api.IssueErrors.Enqueue(new TingeeApiException("400", "CallSignServiceFail"));
        api.DraftErrors.Enqueue(new TingeeApiException("2037", "invalid login"));
        Assert.Equal(OrderInvoiceStatus.Failed, (await invoicer.IssueAsync(customer, order, manual: false)).Status);
        Assert.Contains("2037", store.Order(order).InvoiceError);
        api.IssueErrors.Enqueue(new TingeeApiException("400", "Misa API error"));
        Assert.Equal(OrderInvoiceStatus.Failed, (await invoicer.IssueAsync(customer, order, manual: true)).Status);
        Assert.Single(api.Drafts);
        Assert.True(OrderInvoiceRules.IsSigningFailure(new TingeeApiException("400", "CallSignServiceFail")));
        Assert.False(OrderInvoiceRules.IsSigningFailure(new TingeeApiException("400", "Misa API error")));
    }

    // Operator 2026-11-07: without MISA's HSM the company chooses drafts, approved and signed by hand on MISA.
    [Fact]
    public async Task InDraftModePaidOrdersGetADraftAndNothingIsSigned()
    {
        var (store, api, invoicer, customer, order) = Setup(buyerEmail: "ketoan@namphat.vn");
        store.Settings[customer] = store.Settings[customer] with { IssueMode = InvoiceIssueModes.Draft };
        Assert.Equal(0, await invoicer.RunOnceAsync());     // no invoice issued
        Assert.Empty(api.Issued);
        Assert.Single(api.Drafts);
        var o = store.Order(order);
        Assert.Equal((OrderInvoiceStatus.Draft, OrderInvoiceRules.ManualDraftNote), (o.InvoiceStatus, o.InvoiceError));
        Assert.Equal(o.InvoiceCode, api.Drafts[0].InvoiceCode);
        Assert.Empty(api.Emails);
        Assert.Equal(0, await invoicer.RunOnceAsync());
        Assert.Single(api.Drafts);
        // NONE: nothing automatic; settings saved before the mode existed follow AutoIssueEnabled.
        Assert.Equal(InvoiceIssueModes.Signed, (store.Settings[customer] with { IssueMode = null }).Mode);
        Assert.Equal(InvoiceIssueModes.None, (store.Settings[customer] with { IssueMode = null, AutoIssueEnabled = false }).Mode);
    }

    // ---- Customer Portal -------------------------------------------------------------------------------
    [Fact]
    public async Task TheSettingsPageWarnsAboutHsmAndOffersThreeWaysToIssue()
    {
        var invoices = new MemoryInvoiceStore();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, invoiceApi: new FakeInvoiceApi());
        payments.Merchants[A] = 9001;
        invoices.Settings[A] = Ready(A) with { AutoIssueEnabled = false, AutoIssueFrom = null };
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice"));
        Assert.Contains("Ký số tự động cần chữ ký số HSM của MISA", page);
        Assert.Contains("Đã mua HSM — ký số và phát hành tự động", page);
        Assert.Contains("Chưa mua HSM — xuất hóa đơn nháp, duyệt thủ công", page);
        Assert.Contains("Không xuất hóa đơn tự động", page);
        Assert.Contains("value=\"NONE\" checked", page);

        // No HSM yet: drafts. Nothing is recorded as confirmed.
        var token = await Token(c, "/einvoice");
        var r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "C26TAA", ["Template.IssueMode"] = "DRAFT" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var s = invoices.Settings[A];
        Assert.Equal((InvoiceIssueModes.Draft, true, null), (s.Mode, s.AutoIssueEnabled, s.HsmConfirmedAt));
        Assert.NotNull(s.AutoIssueFrom);
        Assert.Contains("Tự động xuất nháp", WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice")));

        // HSM bought: the choice is recorded with who and when, and kept when the mode changes later.
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "C26TAA", ["Template.IssueMode"] = "SIGNED" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        s = invoices.Settings[A];
        Assert.Equal(InvoiceIssueModes.Signed, s.Mode);
        Assert.NotNull(s.HsmConfirmedAt);
        Assert.NotNull(s.HsmConfirmedBy);
        Assert.Contains("Đã xác nhận lúc", WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice")));
        var confirmed = s.HsmConfirmedAt;
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "C26TAA", ["Template.IssueMode"] = "NONE" });
        s = invoices.Settings[A];
        Assert.Equal((InvoiceIssueModes.None, false, confirmed), (s.Mode, s.AutoIssueEnabled, s.HsmConfirmedAt));
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "C26TAA", ["Template.IssueMode"] = "AUTO" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task ADraftOrderShowsItsStatusWithoutAnError()
    {
        var api = new FakeInvoiceApi();
        var invoices = new MemoryInvoiceStore();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, invoiceApi: api);
        invoices.Payments = payments;
        payments.Merchants[A] = 9001;
        var order = PaidOrder() with { AutoIssueInvoice = false };
        payments.Orders.Add((A, order));
        invoices.Settings[A] = Ready(A);
        var c = Client(f);
        api.IssueErrors.Enqueue(new TingeeApiException("400", "CallSignServiceFail"));
        var r = await Post(c, $"/orders/issueinvoice/{order.Id}", await Token(c, $"/orders/details/{order.Id}"), []);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.Contains("Đã tạo hóa đơn nháp", page);
        Assert.DoesNotContain("role=\"alert\" data-ta-toast", page);
        Assert.Contains("Đã xuất nháp", page);
        Assert.Contains("Xem bản nháp PDF", page);
        Assert.Contains("Xuất lại hóa đơn", page);
        Assert.DoesNotContain("CallSignServiceFail", page);
        Assert.DoesNotContain("cp-error\" style=\"margin-top:10px\" data-cp-invoice-error", page);
        Assert.Contains("Đã xuất nháp", WebUtility.HtmlDecode(await c.GetStringAsync("/orders")));
        Assert.Equal("application/pdf", (await c.GetAsync($"/orders/invoicepdf/{order.Id}")).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheCompanyConnectsMisaWithoutTheTPortalKeepingThePassword()
    {
        var invoices = new MemoryInvoiceStore();
        var api = new FakeInvoiceApi();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, invoiceApi: api);
        payments.Merchants[A] = 9001;
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice"));
        Assert.Contains("Kết nối MISA meInvoice", page);
        Assert.Contains("value=\"0101234567\"", page);   // the company's tax code is offered

        var token = await Token(c, "/einvoice");
        var r = await Post(c, "/einvoice/connect", token, new() { ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "ketoan@a.vn", ["Account.Password"] = "Secret#123" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var saved = Assert.Single(api.Saved);
        Assert.Equal((TingeeInvoiceProviders.Misa, 9001L, "Secret#123", true), (saved.Provider, saved.MerchantId!.Value, saved.Password, saved.IsDefault));
        var s = invoices.Settings[A];
        Assert.Equal((78L, "0101234567", "ketoan@a.vn"), (s.ProviderAccountId!.Value, s.ProviderTaxCode, s.ProviderUsername));
        Assert.DoesNotContain("Secret#123", System.Text.Json.JsonSerializer.Serialize(s));
        Assert.DoesNotContain("Secret#123", await c.GetStringAsync("/einvoice"));

        // Templates from MISA, then automatic invoices on.
        token = await Token(c, "/einvoice");
        var list = WebUtility.HtmlDecode(await (await Post(c, "/einvoice/templates", token, [])).Content.ReadAsStringAsync());
        Assert.Contains("C26TAB", list);
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Choice"] = "1|C26TAB", ["Template.AutoIssueEnabled"] = "true", ["Template.SendEmail"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        s = invoices.Settings[A];
        Assert.Equal(("1", "C26TAB", true), (s.InvoicePattern, s.InvoiceSeries, s.AutoIssueEnabled));
        Assert.NotNull(s.AutoIssueFrom);
        var on = WebUtility.HtmlDecode(await c.GetStringAsync("/einvoice"));
        Assert.Contains("Đang tự động xuất", on);
        Assert.DoesNotContain("Tingee", on, StringComparison.OrdinalIgnoreCase);
    }

    // Production 2026-11-06: MISA returns the template id as a GUID (invoice-templates), and a draft with mẫu số "1" is
    // refused ("Misa API error"). Picking that template must save the whole 36-character id.
    [Fact]
    public async Task AMisaTemplateIdentifiedByAGuidCanBePickedAndSaved()
    {
        var invoices = new MemoryInvoiceStore();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices, invoiceApi: new FakeInvoiceApi());
        payments.Merchants[A] = 9001;
        var c = Client(f);
        var token = await Token(c, "/einvoice");
        await Post(c, "/einvoice/connect", token, new() { ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "ketoan@a.vn", ["Account.Password"] = "Secret#123" });
        const string id = "245b1d28-63dc-4288-b009-904ab8f848a0";
        token = await Token(c, "/einvoice");
        var r = await Post(c, "/einvoice/save", token, new() { ["Template.Choice"] = id + "|1C26TTA" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal((id, "1C26TTA"), (invoices.Settings[A].InvoicePattern, invoices.Settings[A].InvoiceSeries));
        token = await Token(c, "/einvoice");
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = id, ["Template.Series"] = "1C26TTA" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
    }

    [Fact]
    public async Task AutomaticInvoicesNeedAConnectedAccountAndATemplate()
    {
        var invoices = new MemoryInvoiceStore();
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices);
        payments.Merchants[A] = 9001;
        var c = Client(f);
        var token = await Token(c, "/einvoice");
        var r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "C26TAA", ["Template.AutoIssueEnabled"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(invoices.Settings.ContainsKey(A));
        invoices.Settings[A] = CustomerInvoiceSettings.Empty(A) with { ProviderAccountId = 78 };
        r = await Post(c, "/einvoice/save", token, new() { ["Template.Pattern"] = "1", ["Template.Series"] = "", ["Template.AutoIssueEnabled"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(invoices.Settings[A].AutoIssueEnabled);
    }

    [Fact]
    public async Task MembersSeeTheSettingsButCannotChangeThem()
    {
        var invoices = new MemoryInvoiceStore();
        var api = new FakeInvoiceApi();
        using var f = Factory("VIEWER", out var payments, out _, invoiceStore: invoices, invoiceApi: api);
        payments.Merchants[A] = 9001;
        var c = Client(f);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/einvoice")).StatusCode);
        var token = await Token(c, "/einvoice");
        foreach (var path in new[] { "/einvoice/connect", "/einvoice/disconnect", "/einvoice/templates", "/einvoice/save", "/einvoice/preview" })
            Assert.NotEqual(HttpStatusCode.Redirect, (await Post(c, path, token, new() { ["Account.TaxCode"] = "0101234567", ["Account.Username"] = "x", ["Account.Password"] = "y" })).StatusCode);
        Assert.Empty(api.Saved);
        Assert.False(invoices.Settings.ContainsKey(A));
    }

    [Fact]
    public async Task APaidOrderCanBeInvoicedFromItsPageAndThePdfOpened()
    {
        var api = new FakeInvoiceApi();
        MemoryInvoiceStore? invoices = null;
        using var f = Factory("OWNER", out var payments, out _, invoiceStore: invoices = new MemoryInvoiceStore(), invoiceApi: api);
        invoices.Payments = payments;
        payments.Merchants[A] = 9001;
        var order = PaidOrder() with { AutoIssueInvoice = false };
        payments.Orders.Add((A, order));
        var c = Client(f);
        Assert.Contains("Kết nối MISA meInvoice", WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}")));

        invoices.Settings[A] = Ready(A);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.Contains("Xuất hóa đơn", page);
        var r = await Post(c, $"/orders/issueinvoice/{order.Id}", await Token(c, $"/orders/details/{order.Id}"), []);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Single(api.Issued);
        page = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.Contains("Đã xuất hóa đơn", page);
        Assert.Contains("0000123", page);
        var pdf = await c.GetAsync($"/orders/invoicepdf/{order.Id}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(invoices.Order(order.Id).InvoiceCode, api.Downloads.Single());
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static CustomerInvoiceSettings Ready(Guid customer) =>
        new(customer, TingeeInvoiceProviders.Misa, 77, "0109999999", "ketoan@seller.vn", "1", "C26TAA", true, true, Now.UtcDateTime.AddDays(-1), Now.UtcDateTime.AddDays(-1));

    private static OrderInvoiceJob Job(string? type, string? name, string? legal, string? tax, string? email) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "DH261106-0001", "Bàn 5", "DH2611060001-ABCDEF12", 1, 9001, type, name, legal, tax, "1 Lê Lợi", "0901234567", email,
            [new(1, "Cà phê sữa", "Ly", 2, 35_000, "8")]);

    private static Order PaidOrder() =>
        new(Guid.NewGuid(), "DH261106-0001", null, null, null, "Bàn 5", 75_600, 75_600, OrderStatus.Paid, null, null, null, null, 1440, null, true, OrderInvoiceStatus.Pending, null,
            Now.UtcDateTime.AddHours(-1), [OrderMath.Line(1, "Cà phê sữa", "Ly", 2, 35_000, "8")]);

    private static (MemoryInvoiceStore Store, FakeInvoiceApi Api, OrderInvoicer Invoicer, Guid Customer, Guid Order) Setup(string? buyerEmail = null)
    {
        var payments = new MemoryPaymentStore();
        var customer = Guid.NewGuid();
        var contact = new Contact(Guid.NewGuid(), "KH001", "Anh Nam", "Công ty TNHH Nam Phát", "0101234567", null, buyerEmail, null, null, true, BuyerTypes.Business);
        payments.Contacts.Add((customer, contact));
        payments.Merchants[customer] = 9001;
        var order = PaidOrder() with { ContactId = contact.Id };
        payments.Orders.Add((customer, order));
        var store = new MemoryInvoiceStore { Payments = payments, PaidAt = { [order.Id] = Now.UtcDateTime } };
        store.Settings[customer] = Ready(customer);
        var api = new FakeInvoiceApi();
        return (store, api, new OrderInvoicer(store, api, new FixedClock(Now), NullLogger<OrderInvoicer>.Instance), customer, order.Id);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Invoice settings plus the invoice columns of the in-memory orders of <see cref="Payments"/>.</summary>
    public sealed class MemoryInvoiceStore : IOrderInvoiceStore
    {
        public MemoryPaymentStore Payments { get; set; } = new();
        public Dictionary<Guid, CustomerInvoiceSettings> Settings { get; } = [];
        public Dictionary<Guid, DateTime> PaidAt { get; } = [];
        public Dictionary<Guid, DateTime> NextAttempt { get; } = [];
        private readonly Dictionary<Guid, int> attempts = [];

        public Order Order(Guid id) => Payments.Orders.Single(o => o.Order.Id == id).Order;
        public void Update(Guid id, Func<Order, Order> change)
        {
            var i = Payments.Orders.FindIndex(o => o.Order.Id == id);
            Payments.Orders[i] = (Payments.Orders[i].Customer, change(Payments.Orders[i].Order));
        }

        public Task<CustomerInvoiceSettings> SettingsAsync(Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Settings.TryGetValue(customerId, out var s) ? s : CustomerInvoiceSettings.Empty(customerId));
        public Task SaveSettingsAsync(CustomerInvoiceSettings settings, Guid actor, CancellationToken ct = default) { Settings[settings.CustomerId] = settings; return Task.CompletedTask; }

        public Task<IReadOnlyList<(Guid CustomerId, Guid OrderId)>> DueAsync(int max, DateTime utcNow, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid, Guid)>>(Payments.Orders.Where(x => Settings.TryGetValue(x.Customer, out var s) && s.AutoIssueEnabled && s.IsReady
                    && x.Order.Status == OrderStatus.Paid && x.Order.AutoIssueInvoice && x.Order.InvoiceStatus is OrderInvoiceStatus.None or OrderInvoiceStatus.Pending
                    && s.AutoIssueFrom is { } from && PaidAt.GetValueOrDefault(x.Order.Id, utcNow) >= from
                    && (!NextAttempt.TryGetValue(x.Order.Id, out var next) || next <= utcNow))
                .Take(max).Select(x => (x.Customer, x.Order.Id)).ToList());

        public Task<OrderInvoiceJob?> ClaimAsync(Guid customerId, Guid orderId, DateTime utcNow, DateTime leaseUntil, bool manual, CancellationToken ct = default)
        {
            var row = Payments.Orders.FirstOrDefault(x => x.Customer == customerId && x.Order.Id == orderId);
            var o = row.Order;
            if (o is null || o.Status != OrderStatus.Paid || !(o.InvoiceStatus is OrderInvoiceStatus.None or OrderInvoiceStatus.Pending || manual && o.InvoiceStatus is OrderInvoiceStatus.Failed or OrderInvoiceStatus.Draft))
                return Task.FromResult<OrderInvoiceJob?>(null);
            attempts[orderId] = manual ? 1 : attempts.GetValueOrDefault(orderId) + 1;
            var code = o.InvoiceCode ?? OrderInvoiceRules.InvoiceCode(o.OrderNo, orderId);
            Update(orderId, x => x with { InvoiceStatus = OrderInvoiceStatus.Pending, InvoiceCode = code });
            var b = o.ContactId is Guid c ? Payments.Contacts.First(x => x.Contact.Id == c).Contact : null;
            return Task.FromResult<OrderInvoiceJob?>(new(customerId, orderId, o.OrderNo, o.Description, code, attempts[orderId],
                Payments.Merchants.TryGetValue(customerId, out var m) ? m : null, b?.Type, b?.Name, b?.LegalName, b?.TaxCode, b?.Address, b?.Phone, b?.Email,
                o.Lines.Select(l => new OrderInvoiceLine(l.LineNumber, l.ItemName, l.UnitName, l.Quantity, l.UnitPrice, l.TaxRateCode)).ToList(),
                o.InvoiceStatus == OrderInvoiceStatus.Draft));
        }

        public Task CompleteAsync(Guid customerId, Guid orderId, OrderInvoiceIssued issued, string? note, CancellationToken ct = default)
        {
            Update(orderId, o => o with { InvoiceStatus = OrderInvoiceStatus.Issued, InvoiceNumber = issued.InvoiceNumber ?? o.InvoiceNumber, InvoiceIssuedAt = o.InvoiceIssuedAt ?? DateTime.UtcNow, InvoiceError = note });
            NextAttempt.Remove(orderId);
            return Task.CompletedTask;
        }
        public Task DraftAsync(Guid customerId, Guid orderId, string note, CancellationToken ct = default)
        {
            Update(orderId, o => o with { InvoiceStatus = OrderInvoiceStatus.Draft, InvoiceError = note });
            NextAttempt.Remove(orderId);
            return Task.CompletedTask;
        }
        public Task RetryLaterAsync(Guid customerId, Guid orderId, DateTime nextAttemptAt, string error, CancellationToken ct = default)
        {
            Update(orderId, o => o with { InvoiceError = error });
            NextAttempt[orderId] = nextAttemptAt;
            return Task.CompletedTask;
        }
        public Task FailAsync(Guid customerId, Guid orderId, string error, CancellationToken ct = default)
        {
            Update(orderId, o => o with { InvoiceStatus = OrderInvoiceStatus.Failed, InvoiceError = error });
            return Task.CompletedTask;
        }
        public Task MarkEmailedAsync(Guid customerId, Guid orderId, CancellationToken ct = default)
        {
            Update(orderId, o => o with { InvoiceEmailedAt = DateTime.UtcNow });
            return Task.CompletedTask;
        }
    }

    public sealed class FakeInvoiceApi : ITingeeInvoiceApi
    {
        public bool IsConfigured { get; set; } = true;
        public List<TingeeInvoiceAccountRequest> Saved { get; } = [];
        public List<TingeeInvoiceRequest> Issued { get; } = [];
        public List<TingeeInvoiceRequest> Drafts { get; } = [];
        public List<string> Emails { get; } = [];
        public List<string> Downloads { get; } = [];
        public Queue<Exception> IssueErrors { get; } = new();
        public Queue<Exception> DraftErrors { get; } = new();

        public Task<IReadOnlyList<TingeeInvoiceProvider>> GetProvidersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TingeeInvoiceProvider>>([]);
        public Task<TingeePage<TingeeInvoiceAccount>> GetAccountsAsync(long merchantId, CancellationToken ct = default) => Task.FromResult(new TingeePage<TingeeInvoiceAccount>());
        public Task<long> SaveAccountAsync(TingeeInvoiceAccountRequest request, CancellationToken ct = default) { Saved.Add(request); return Task.FromResult(78L); }
        public Task DeleteAccountAsync(long merchantId, long accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<TingeeInvoiceTemplate>> GetTemplatesAsync(long merchantId, long? accountId, int? invoiceNoToCheck = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TingeeInvoiceTemplate>>([new() { InvoicePattern = "1", InvoiceSeries = "C26TAB" }]);
        public Task<TingeeInvoicePdf> CreateDraftAsync(TingeeInvoiceRequest request, CancellationToken ct = default)
        {
            Drafts.Add(request);
            if (DraftErrors.TryDequeue(out var error)) return Task.FromException<TingeeInvoicePdf>(error);
            return Task.FromResult(new TingeeInvoicePdf { InvoiceCode = request.InvoiceCode, PdfBase64 = "JVBERi0xLjQK" });
        }
        public Task<TingeeIssuedInvoice> CreateAndIssueAsync(TingeeInvoiceRequest request, CancellationToken ct = default)
        {
            Issued.Add(request);
            return IssueErrors.TryDequeue(out var error) ? Task.FromException<TingeeIssuedInvoice>(error)
                : Task.FromResult(new TingeeIssuedInvoice { InvoiceCode = request.InvoiceCode, InvoiceNumber = "0000123", ReservationCode = "LOOKUP1" });
        }
        public Task<TingeeInvoicePdf> DownloadAsync(long merchantId, string invoiceCode, long? accountId = null, CancellationToken ct = default)
        {
            Downloads.Add(invoiceCode);
            return Task.FromResult(new TingeeInvoicePdf { InvoiceCode = invoiceCode, PdfBase64 = "JVBERi0xLjQK" });
        }
        public Task SendEmailAsync(long merchantId, string invoiceCode, string recipientEmail, string? buyerName = null, long? accountId = null, CancellationToken ct = default)
        {
            Emails.Add(recipientEmail);
            return Task.CompletedTask;
        }
    }
}
