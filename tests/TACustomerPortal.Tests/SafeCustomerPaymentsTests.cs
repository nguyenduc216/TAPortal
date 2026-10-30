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
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Customer Portal payments: bank accounts (Tingee create-va/confirm-va), contacts, orders with QR. No SQL and no network:
// in-memory payment store and a recording fake of the Tingee API. Customer isolation is enforced by the store contract
// (every call carries the signed-in customer id); these tests check the controllers always pass the current customer.
public sealed class SafeCustomerPaymentsTests
{
    private const string TestScheme = "SafePayments";
    private static readonly Guid UserId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    // ---- pure rules ------------------------------------------------------------------------------------
    [Fact]
    public void LineTotalsRoundPerLineAndExemptRatesCarryNoTax()
    {
        var line = OrderMath.Line(1, " Gói dịch vụ ", null, 2, 100_000, "10");
        Assert.Equal(200_000, line.AmountBeforeTax);
        Assert.Equal(20_000, line.TaxAmount);
        Assert.Equal("Gói dịch vụ", line.ItemName);
        Assert.Equal(0, OrderMath.Line(1, "x", null, 1, 50_000, "-1").TaxAmount);
        Assert.Equal(2_667, OrderMath.Line(1, "x", null, 1.5m, 22_222, "8").TaxAmount);  // 33 333 × 8% = 2 666.64 → 2 667
        Assert.Equal(OrderStatus.Paid, OrderMath.StatusFor(100, 120));
        Assert.Equal(OrderStatus.Partial, OrderMath.StatusFor(100, 1));
        Assert.Equal(OrderStatus.Awaiting, OrderMath.StatusFor(100, 0));
    }

    [Fact]
    public void OpenOrdersPastTheirQrExpiryAreExpiredButPaidOnesAreNot()
    {
        var now = new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc);
        var order = Order(OrderStatus.Partial, 100, 40) with { ExpiresAt = now.AddMinutes(-1) };
        Assert.Equal(OrderStatus.Expired, OrderMath.EffectiveStatus(order, now));
        Assert.Equal(OrderStatus.Partial, OrderMath.EffectiveStatus(order with { ExpiresAt = now.AddMinutes(1) }, now));
        Assert.Equal(OrderStatus.Paid, OrderMath.EffectiveStatus(order with { Status = OrderStatus.Paid, PaidAmount = 100 }, now));
        Assert.Equal(OrderStatus.Draft, OrderMath.EffectiveStatus(order with { Status = OrderStatus.Draft }, now));
    }

    [Theory]
    [InlineData("970418", TingeeAccountTypes.Business, true)]   // BIDV business
    [InlineData("970418", TingeeAccountTypes.Personal, false)]
    [InlineData("970436", TingeeAccountTypes.Personal, false)]  // VCB personal: no dynamic QR
    [InlineData("970436", TingeeAccountTypes.Business, true)]
    [InlineData("970422", TingeeAccountTypes.Personal, true)]   // MB
    [InlineData("970448", TingeeAccountTypes.Personal, true)]   // OCB personal
    [InlineData("970416", TingeeAccountTypes.Business, false)]  // ACB: static QR only
    [InlineData("970403", TingeeAccountTypes.Business, false)]  // Sacombank has no business type here
    public void DynamicQrSupportFollowsTingeeDocumentation(string bin, string type, bool expected) =>
        Assert.Equal(expected, TingeeBankCatalog.SupportsDynamicQr(bin, type));

    [Fact]
    public void BankCatalogHasUniqueBinsAndRootOnlyBanksHaveNoVa()
    {
        Assert.Equal(TingeeBankCatalog.Banks.Count, TingeeBankCatalog.Banks.Select(b => b.Bin).Distinct().Count());
        Assert.False(TingeeBankCatalog.Find("970430")!.Rule(TingeeAccountTypes.Personal).VaSupported);   // PGBank
        Assert.True(TingeeBankCatalog.Find("970416")!.Rule(TingeeAccountTypes.Business).Required.Contains(TingeeBankField.AcbUserId));
        Assert.DoesNotContain(TingeeBankField.Identity, TingeeBankCatalog.Find("970418")!.Rule(TingeeAccountTypes.Business).Required);
    }

    // ---- access ----------------------------------------------------------------------------------------
    [Fact]
    public async Task ViewersSeeThePaymentsAreaButCannotChangeAnything()
    {
        using var f = Factory("VIEWER", out var store, out _);
        store.Contacts.Add((A, Contact(Guid.NewGuid(), "KH01", "Nguyễn An")));
        var c = Client(f);
        foreach (var path in new[] { "/orders", "/contacts", "/banks", "/paymentsettings" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(path)).StatusCode);
        var contacts = WebUtility.HtmlDecode(await c.GetStringAsync("/contacts"));
        Assert.Contains("Nguyễn An", contacts);
        Assert.DoesNotContain("Thêm khách hàng", contacts);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/contacts/create")).StatusCode);
        var token = await Token(c, "/contacts");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/contacts/create", token, new() { ["Code"] = "X", ["Name"] = "Y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/orders/create", token, new())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/banks/add", token, new())).StatusCode);
        Assert.Single(store.Contacts);
    }

    [Fact]
    public async Task NavigationOffersThePaymentsModules()
    {
        using var f = Factory("OWNER", out _, out _);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/orders"));
        foreach (var label in new[] { "Đơn hàng & QR", "Khách hàng", "Tài khoản ngân hàng", "Cài đặt thanh toán" })
            Assert.Contains(label, html);
        Assert.Matches("href=\"/Orders\"[^>]*aria-current=\"page\"", html);
    }

    [Fact]
    public async Task MissingPaymentTablesShowANotReadyPageWithAReference()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Fail = true;
        var response = await Client(f).GetAsync("/orders");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Chức năng thanh toán chưa sẵn sàng", html);
        Assert.Matches("data-ta-reference>[^<]+<", html);
        Assert.DoesNotContain("ConnectionString", html);
    }

    // ---- contacts --------------------------------------------------------------------------------------
    [Fact]
    public async Task ContactsAreCreatedForTheCurrentCompanyAndCodesStayUnique()
    {
        using var f = Factory("ADMIN", out var store, out _);
        var c = Client(f);
        var token = await Token(c, "/contacts/create");
        var ok = await Post(c, "/contacts/create", token, new() { ["Code"] = "kh-01", ["Name"] = "Công ty Bình Minh", ["TaxCode"] = "0101234567", ["Phone"] = "0912345678", ["Email"] = "ketoan@binhminh.example" });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var saved = Assert.Single(store.Contacts);
        Assert.Equal(A, saved.Customer);
        Assert.Equal("KH-01", saved.Contact.Code);

        var duplicate = await Post(c, "/contacts/create", token, new() { ["Code"] = "KH-01", ["Name"] = "Khác" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("Mã khách hàng đã được dùng", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));

        var invalid = await Post(c, "/contacts/create", token, new() { ["Code"] = "KH-02", ["Name"] = "X", ["Phone"] = "12345", ["TaxCode"] = "12" });
        var body = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Số điện thoại gồm 10 chữ số", body);
        Assert.Contains("Mã số thuế gồm 10 chữ số", body);
    }

    [Fact]
    public async Task AnotherCompanysContactIsNotFound()
    {
        using var f = Factory("OWNER", out var store, out _);
        var foreign = Contact(Guid.NewGuid(), "KH-B", "Của doanh nghiệp B");
        store.Contacts.Add((B, foreign));
        var c = Client(f);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/contacts/edit/{foreign.Id}")).StatusCode);
        Assert.DoesNotContain("Của doanh nghiệp B", await c.GetStringAsync("/contacts"));
    }

    // ---- bank accounts ---------------------------------------------------------------------------------
    [Fact]
    public async Task BankFormAsksOnlyForTheFieldsTheBankRequires()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Merchants[A] = 9001;
        var c = Client(f);
        var bidvBusiness = WebUtility.HtmlDecode(await c.GetStringAsync("/banks/add?bin=970418&type=business-account"));
        Assert.Contains("name=\"VaSuffix\"", bidvBusiness);
        Assert.Contains("name=\"MerchantName\"", bidvBusiness);
        Assert.DoesNotContain("name=\"Identity\"", bidvBusiness);
        var acbHousehold = WebUtility.HtmlDecode(await c.GetStringAsync("/banks/add?bin=970416&type=business-household-account"));
        Assert.Contains("name=\"AcbUserId\"", acbHousehold);
        Assert.Contains("name=\"NotifyRootAccount\"", acbHousehold);   // ACB supports root-account notifications
    }

    [Fact]
    public async Task OtpLinkingStoresAPendingAccountThenActivatesItForTheCurrentMerchant()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        tingee.CreateVaResult = new() { ConfirmId = "confirm-1" };
        tingee.ConfirmVaResult = new() { AccountNumber = "0123456789", VaAccountNumber = "88880123456789", ShopId = 77 };
        var c = Client(f);
        var token = await Token(c, "/banks/add?bin=970422&type=personal-account");
        var missing = await Post(c, "/banks/add", token, new() { ["Bin"] = "970422", ["AccountType"] = "personal-account", ["AccountNumber"] = "0123456789" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Vui lòng nhập số điện thoại nhận OTP", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
        Assert.Empty(tingee.CreateVaRequests);

        var created = await Post(c, "/banks/add", token, new()
        {
            ["Bin"] = "970422", ["AccountType"] = "personal-account", ["AccountNumber"] = "0123 456 789", ["AccountName"] = "Nguyễn Văn Đức",
            ["Identity"] = "001099012345", ["Mobile"] = "0912345678", ["VaSuffix"] = "IGNORED"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var request = Assert.Single(tingee.CreateVaRequests);
        Assert.Equal(9001, request.MerchantId);
        Assert.Equal("NGUYEN VAN DUC", request.AccountName);     // upper case, no diacritics
        Assert.Equal("0123456789", request.AccountNumber);
        Assert.Null(request.VaSuffix);                           // not a field MB personal asks for
        var pending = Assert.Single(store.Banks).Account;
        Assert.Equal(BankStatus.PendingOtp, pending.Status);
        Assert.Equal($"/Banks/Confirm/{pending.Id}", created.Headers.Location!.OriginalString);

        var badOtp = await Post(c, $"/banks/confirm/{pending.Id}", token, new() { ["otp"] = "12" });
        Assert.Equal(HttpStatusCode.BadRequest, badOtp.StatusCode);
        var confirmed = await Post(c, $"/banks/confirm/{pending.Id}", token, new() { ["otp"] = "123456" });
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal(("confirm-1", "123456", 9001L), tingee.Confirmations.Single());
        var active = store.Banks.Single().Account;
        Assert.Equal(BankStatus.Active, active.Status);
        Assert.Equal("88880123456789", active.ReceiveAccount);
        Assert.True(active.IsDefault);
    }

    [Fact]
    public async Task ExpiredOtpSessionMarksTheRequestFailedInsteadOfRetryingIt()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var pending = Bank("970422", BankStatus.PendingOtp) with { ConfirmId = "old" };
        store.Banks.Add((A, pending));
        tingee.ConfirmError = new TingeeApiException("1065", "OTP timeout");
        var c = Client(f);
        var r = await Post(c, $"/banks/confirm/{pending.Id}", await Token(c, "/banks"), new() { ["otp"] = "123456" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(BankStatus.Failed, store.Banks.Single().Account.Status);
        Assert.Null(store.Banks.Single().Account.ConfirmId);
    }

    [Fact]
    public async Task BankLinkingIsRefusedUntilTAHasLinkedATingeeMerchant()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        var c = Client(f);
        Assert.Contains("chưa được kích hoạt", WebUtility.HtmlDecode(await c.GetStringAsync("/banks")));
        var r = await Post(c, "/banks/add", await Token(c, "/banks"), new() { ["Bin"] = "970422", ["AccountType"] = "personal-account" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("chưa được T.A kích hoạt thanh toán Tingee", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(tingee.CreateVaRequests);
    }

    // ---- orders ----------------------------------------------------------------------------------------
    [Fact]
    public async Task OrderForADynamicQrBankGetsARecurringQrForTheFullAmount()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var bank = Bank("970418", BankStatus.Active, TingeeAccountTypes.Business) with { VaAccountNumber = "TINGEE8888" };
        store.Banks.Add((A, bank));
        var c = Client(f);
        var response = await Post(c, "/orders/create", await Token(c, "/orders/create"), new()
        {
            ["BankAccountId"] = bank.Id.ToString(), ["QrExpireMinutes"] = "60", ["AutoIssueInvoice"] = "true", ["Description"] = "Phí tháng 10",
            ["Lines[0].ItemName"] = "Phí dịch vụ", ["Lines[0].Quantity"] = "2", ["Lines[0].UnitPrice"] = "100000", ["Lines[0].TaxRate"] = "10",
            ["Lines[1].ItemName"] = "", ["Lines[1].UnitPrice"] = ""   // blank row ignored
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var order = store.Orders.Single().Order;
        Assert.Equal(220_000, order.Amount);
        Assert.Single(order.Lines);
        Assert.True(order.AutoIssueInvoice);
        var qr = Assert.Single(tingee.DynamicQrRequests);
        Assert.Equal((9001L, "TINGEE8888", "970418", 220_000m, 60, TingeeQrTypes.Recurring, order.OrderNo), (qr.MerchantId, qr.VaAccountNumber, qr.BankBin, qr.Amount, qr.ExpireInMinute, qr.QrCodeType, qr.Purpose));
        Assert.Equal(OrderStatus.Awaiting, order.Status);
        Assert.Matches(@"^DH\d{6}-0001$", order.OrderNo);

        var details = WebUtility.HtmlDecode(await c.GetStringAsync(response.Headers.Location!.OriginalString));
        Assert.Contains("<svg", details);
        Assert.Contains("data-cp-poll=", details);
        Assert.Contains("220.000 ₫", details);
    }

    [Fact]
    public async Task StaticQrBanksUseTheOrderNumberAsTransferContent()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var bank = Bank("970416", BankStatus.Active) with { AccountNumber = "12345678", IsNotifyAccountNumber = true, VaAccountNumber = null };
        store.Banks.Add((A, bank));
        var c = Client(f);
        await Post(c, "/orders/create", await Token(c, "/orders/create"), Lines(bank.Id, "50000"));
        var order = store.Orders.Single().Order;
        var request = Assert.Single(tingee.StaticQrRequests);
        Assert.Equal(("970416", "12345678", 50_000m, order.OrderNo), (request.BankBin, request.AccountNumber, request.Amount, request.Content));
        Assert.Empty(tingee.DynamicQrRequests);
        Assert.Equal(TingeeQrTypes.Static, order.QrType);
    }

    [Fact]
    public async Task ATingeeFailureKeepsTheOrderAsDraftAndTheQrCanBeIssuedLater()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var bank = Bank("970422", BankStatus.Active) with { VaAccountNumber = "VQR1" };
        store.Banks.Add((A, bank));
        tingee.QrError = new TingeeApiException("1018", "Loại tài khoản chưa hỗ trợ");
        var c = Client(f);
        var token = await Token(c, "/orders/create");
        var created = await Post(c, "/orders/create", token, Lines(bank.Id, "75000"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var order = store.Orders.Single().Order;
        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Contains("1018", order.LastError);

        tingee.QrError = null;
        await Post(c, $"/orders/issueqr/{order.Id}", token, new());
        Assert.Equal(OrderStatus.Awaiting, store.Orders.Single().Order.Status);
        Assert.Null(store.Orders.Single().Order.LastError);
    }

    [Fact]
    public async Task PartialPaymentsAccumulateUntilTheOrderIsPaidAndTheQrIsClosed()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var order = Order(OrderStatus.Awaiting, 300_000, 0) with { QrType = TingeeQrTypes.Recurring, QrAccount = "QRA", BillId = "BILL1", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        store.Orders.Add((A, order));
        var c = Client(f);
        var token = await Token(c, "/orders");
        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 100_000, Status = "partially-paid" } };
        await Post(c, $"/orders/refresh/{order.Id}", token, new());
        Assert.Equal((OrderStatus.Partial, 100_000m), (store.Orders.Single().Order.Status, store.Orders.Single().Order.PaidAmount));
        Assert.Empty(tingee.DeletedQrs);

        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 300_000, Status = "fully-paid" } };
        await Post(c, $"/orders/refresh/{order.Id}", token, new());
        Assert.Equal((OrderStatus.Paid, 300_000m), (store.Orders.Single().Order.Status, store.Orders.Single().Order.PaidAmount));
        Assert.Equal(("QRA", "BILL1"), tingee.DeletedQrs.Single());
    }

    [Fact]
    public async Task AQrReissuedForTheRemainderAddsToWhatWasAlreadyPaid()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        // 100 000 of 300 000 paid on an expired QR; the new QR was issued for the 200 000 still due.
        var order = Order(OrderStatus.Partial, 300_000, 100_000) with { QrType = TingeeQrTypes.Recurring, QrAccount = "QRB", BillId = "BILL2", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        store.Orders.Add((A, order));
        tingee.Status = new() { BillInfo = new() { Amount = 200_000, TotalAmountPaid = 50_000, Status = "partially-paid" } };
        var c = Client(f);
        await Post(c, $"/orders/refresh/{order.Id}", await Token(c, "/orders"), new());
        Assert.Equal(150_000, store.Orders.Single().Order.PaidAmount);
    }

    [Fact]
    public async Task OrdersWithMoneyReceivedCannotBeCancelled()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Merchants[A] = 9001;
        var paidSome = Order(OrderStatus.Partial, 300_000, 1_000);
        var untouched = Order(OrderStatus.Awaiting, 300_000, 0);
        store.Orders.Add((A, paidSome));
        store.Orders.Add((A, untouched));
        var c = Client(f);
        var token = await Token(c, "/orders");
        await Post(c, $"/orders/cancel/{paidSome.Id}", token, new());
        await Post(c, $"/orders/cancel/{untouched.Id}", token, new());
        Assert.Equal(OrderStatus.Partial, store.Orders.Single(o => o.Order.Id == paidSome.Id).Order.Status);
        Assert.Equal(OrderStatus.Cancelled, store.Orders.Single(o => o.Order.Id == untouched.Id).Order.Status);
    }

    [Fact]
    public async Task OrderCreationRejectsForeignBankAccountsAndEmptyLines()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var foreign = Bank("970418", BankStatus.Active, TingeeAccountTypes.Business) with { VaAccountNumber = "OTHER" };
        store.Banks.Add((B, foreign));
        var c = Client(f);
        var r = await Post(c, "/orders/create", await Token(c, "/orders/create"), new() { ["BankAccountId"] = foreign.Id.ToString(), ["QrExpireMinutes"] = "60" });
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Vui lòng chọn tài khoản nhận tiền đang hoạt động", html);
        Assert.Contains("ít nhất một dòng", html);
        Assert.Empty(store.Orders);
        Assert.Empty(tingee.DynamicQrRequests);
    }

    [Fact]
    public async Task SettingsDefaultToAutomaticInvoicingAndFeedNewOrders()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Merchants[A] = 9001;
        store.Banks.Add((A, Bank("970422", BankStatus.Active) with { VaAccountNumber = "VQR1" }));
        var c = Client(f);
        Assert.Contains("name=\"AutoIssueInvoiceDefault\" value=\"true\" checked=\"checked\"", await c.GetStringAsync("/paymentsettings"));
        var save = await Post(c, "/paymentsettings", await Token(c, "/paymentsettings"), new() { ["AutoIssueInvoiceDefault"] = "false", ["DefaultQrExpireMinutes"] = "30" });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var create = await c.GetStringAsync("/orders/create");
        Assert.Contains("value=\"30\"", create);
        Assert.DoesNotContain("name=\"AutoIssueInvoice\" value=\"true\" checked=\"checked\"", create);
        var invalid = await Post(c, "/paymentsettings", await Token(c, "/paymentsettings"), new() { ["DefaultQrExpireMinutes"] = "50000" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static Dictionary<string, string> Lines(Guid bank, string price) => new()
    {
        ["BankAccountId"] = bank.ToString(), ["QrExpireMinutes"] = "1440", ["Lines[0].ItemName"] = "Hàng", ["Lines[0].Quantity"] = "1", ["Lines[0].UnitPrice"] = price, ["Lines[0].TaxRate"] = "0"
    };

    private static Contact Contact(Guid id, string code, string name) => new(id, code, name, null, null, null, null, null, null, true);

    private static BankAccount Bank(string bin, string status, string type = TingeeAccountTypes.Personal)
    {
        var bank = TingeeBankCatalog.Find(bin)!;
        return new(Guid.NewGuid(), bin, bank.Code, bank.Name, type, "0123456789", "NGUYEN VAN A", null, false, null, status, null, null, null, null, false, DateTime.UtcNow);
    }

    private static Order Order(string status, decimal amount, decimal paid) =>
        new(Guid.NewGuid(), "DH261030-0001", null, null, null, null, amount, paid, status, null, null, null, null, 1440, null, true, "NONE", null, DateTime.UtcNow,
            [OrderMath.Line(1, "Hàng", null, 1, amount, "0")]);

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<Program> Factory(string role, out MemoryPaymentStore store, out FakeTingee tingee)
    {
        var s = store = new MemoryPaymentStore();
        var t = tingee = new FakeTingee();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<ICustomerStore>();
                services.AddSingleton<ICustomerStore>(new Members(role));
                services.RemoveAll<IPaymentStore>();
                services.AddSingleton<IPaymentStore>(s);
                services.RemoveAll<ITingeeApi>();
                services.AddSingleton<ITingeeApi>(t);
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    private sealed class Members(string role) : ICustomerStore
    {
        private static readonly CustomerUser User = new(UserId, "member", "Thành viên", null, "hash");
        public Task<CustomerUser?> FindUser(string username) => Task.FromResult<CustomerUser?>(User);
        public Task<CustomerUser?> FindUser(Guid id) => Task.FromResult<CustomerUser?>(id == UserId ? User : null);
        public Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId) => Task.FromResult<IReadOnlyList<CustomerMembership>>(
            [new(A, "KH-A", "Công ty A", "0101234567", null, null, null, role), new(B, "KH-B", "Công ty B", null, null, null, null, "OWNER")]);
        public Task<bool> ChangePassword(Guid id, string oldHash, string newHash) => Task.FromResult(false);
        public Task TouchLogin(Guid id) => Task.CompletedTask;
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, "member"), new Claim("display_name", "Thành viên"), new Claim("customer_id", A.ToString()) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }

    public sealed class MemoryPaymentStore : IPaymentStore
    {
        public bool Fail { get; set; }
        public Dictionary<Guid, long> Merchants { get; } = [];
        public List<(Guid Customer, BankAccount Account)> Banks { get; } = [];
        public List<(Guid Customer, Contact Contact)> Contacts { get; } = [];
        public List<(Guid Customer, Order Order)> Orders { get; } = [];
        private PaymentSettings settings = PaymentSettings.Default;

        private void Check() { if (Fail) throw new InvalidOperationException("Configure Database__ConnectionString"); }

        public Task<long?> MerchantIdAsync(Guid customerId) { Check(); return Task.FromResult<long?>(Merchants.TryGetValue(customerId, out var id) ? id : null); }
        public Task<IReadOnlyList<BankAccount>> BankAccountsAsync(Guid customerId) { Check(); return Task.FromResult<IReadOnlyList<BankAccount>>(Banks.Where(b => b.Customer == customerId && b.Account.Status != BankStatus.Deleted).Select(b => b.Account).ToList()); }
        public Task<BankAccount?> BankAccountAsync(Guid customerId, Guid id) => Task.FromResult(Banks.Where(b => b.Customer == customerId && b.Account.Id == id && b.Account.Status != BankStatus.Deleted).Select(b => b.Account).FirstOrDefault());
        public Task<Guid> AddBankAccountAsync(Guid customerId, BankAccount account, Guid actor)
        {
            var saved = account with { Id = Guid.NewGuid(), IsDefault = !Banks.Any(b => b.Customer == customerId && b.Account.IsActive) };
            Banks.Add((customerId, saved));
            return Task.FromResult(saved.Id);
        }
        public Task UpdateBankAccountAsync(Guid customerId, BankAccount account, Guid actor)
        {
            var i = Banks.FindIndex(b => b.Customer == customerId && b.Account.Id == account.Id);
            if (i >= 0) Banks[i] = (customerId, account);
            return Task.CompletedTask;
        }
        public Task SetDefaultBankAccountAsync(Guid customerId, Guid id, Guid actor)
        {
            for (var i = 0; i < Banks.Count; i++)
                if (Banks[i].Customer == customerId) Banks[i] = (customerId, Banks[i].Account with { IsDefault = Banks[i].Account.Id == id });
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<Contact>> ContactsAsync(Guid customerId) { Check(); return Task.FromResult<IReadOnlyList<Contact>>(Contacts.Where(c => c.Customer == customerId).Select(c => c.Contact).ToList()); }
        public Task<Contact?> ContactAsync(Guid customerId, Guid id) => Task.FromResult(Contacts.Where(c => c.Customer == customerId && c.Contact.Id == id).Select(c => c.Contact).FirstOrDefault());
        public Task<bool> SaveContactAsync(Guid customerId, Contact contact, Guid actor)
        {
            if (Contacts.Any(c => c.Customer == customerId && c.Contact.Code == contact.Code && c.Contact.Id != contact.Id)) return Task.FromResult(false);
            Contacts.RemoveAll(c => c.Customer == customerId && c.Contact.Id == contact.Id && contact.Id != Guid.Empty);
            Contacts.Add((customerId, contact.Id == Guid.Empty ? contact with { Id = Guid.NewGuid() } : contact));
            return Task.FromResult(true);
        }
        public Task SetContactActiveAsync(Guid customerId, Guid id, bool active, Guid actor)
        {
            var i = Contacts.FindIndex(c => c.Customer == customerId && c.Contact.Id == id);
            if (i >= 0) Contacts[i] = (customerId, Contacts[i].Contact with { IsActive = active });
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<Order>> OrdersAsync(Guid customerId) { Check(); return Task.FromResult<IReadOnlyList<Order>>(Orders.Where(o => o.Customer == customerId).Select(o => o.Order).ToList()); }
        public Task<Order?> OrderAsync(Guid customerId, Guid id) => Task.FromResult(Orders.Where(o => o.Customer == customerId && o.Order.Id == id).Select(o => o.Order).FirstOrDefault());
        public Task<(Guid Id, string OrderNo)> CreateOrderAsync(Guid customerId, NewOrder order, Guid actor)
        {
            var no = $"DH{DateTime.UtcNow.AddHours(7):yyMMdd}-{Orders.Count(o => o.Customer == customerId) + 1:0000}";
            var saved = new Order(Guid.NewGuid(), no, order.ContactId, null, order.BankAccountId, order.Description, order.Amount, 0, OrderStatus.Draft, null, null, null, null,
                order.QrExpireMinutes, null, order.AutoIssueInvoice, "NONE", null, DateTime.UtcNow, order.Lines);
            Orders.Add((customerId, saved));
            return Task.FromResult((saved.Id, no));
        }
        public Task UpdateOrderAsync(Guid customerId, Order order, Guid actor)
        {
            var i = Orders.FindIndex(o => o.Customer == customerId && o.Order.Id == order.Id);
            if (i >= 0) Orders[i] = (customerId, order with { Lines = Orders[i].Order.Lines });
            return Task.CompletedTask;
        }
        public Task<PaymentSettings> SettingsAsync(Guid customerId) { Check(); return Task.FromResult(settings); }
        public Task SaveSettingsAsync(Guid customerId, PaymentSettings s, Guid actor) { settings = s; return Task.CompletedTask; }
    }

    public sealed class FakeTingee : ITingeeApi
    {
        public bool IsConfigured => true;
        public TingeeCreateVaResult CreateVaResult { get; set; } = new() { VaAccountNumber = "VA1", Status = "active" };
        public TingeeConfirmVaResult ConfirmVaResult { get; set; } = new();
        public TingeeApiException? ConfirmError { get; set; }
        public TingeeApiException? QrError { get; set; }
        public TingeeQrStatus Status { get; set; } = new();
        public List<TingeeCreateVaRequest> CreateVaRequests { get; } = [];
        public List<(string ConfirmId, string Otp, long Merchant)> Confirmations { get; } = [];
        public List<TingeeDynamicQrRequest> DynamicQrRequests { get; } = [];
        public List<TingeeStaticQrRequest> StaticQrRequests { get; } = [];
        public List<(string QrAccount, string BillId)> DeletedQrs { get; } = [];

        public Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default) { CreateVaRequests.Add(request); return Task.FromResult(CreateVaResult); }
        public Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default)
        {
            if (ConfirmError is not null) throw ConfirmError;
            Confirmations.Add((confirmId, otp, merchantId));
            return Task.FromResult(ConfirmVaResult);
        }
        public Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) => Task.FromResult(new TingeeDeleteVaResult());
        public Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => Task.CompletedTask;
        public Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default) => Task.FromResult(new TingeePage<TingeeVaItem>());
        public Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default)
        {
            if (QrError is not null) throw QrError;
            DynamicQrRequests.Add(request);
            return Task.FromResult(new TingeeDynamicQr { QrCode = "00020101021238570010A000000727QRD" + request.Amount, QrAccount = "QRACC", BillId = "BILL" + DynamicQrRequests.Count });
        }
        public Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default)
        {
            if (QrError is not null) throw QrError;
            StaticQrRequests.Add(request);
            return Task.FromResult(new TingeeStaticQr { QrCode = "00020101021138540010A0000007270124STATIC" });
        }
        public Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => Task.FromResult(Status);
        public Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) { DeletedQrs.Add((qrAccount, billId)); return Task.CompletedTask; }
    }
}
