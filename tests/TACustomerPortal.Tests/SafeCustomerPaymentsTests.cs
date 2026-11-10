using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Customer Portal payments: bank accounts (Tingee create-va/confirm-va), contacts, orders with QR. No SQL and no network:
// in-memory payment store and a recording fake of the Tingee API. Customer isolation is enforced by the store contract
// (every call carries the signed-in customer id); these tests check the controllers always pass the current customer.
public sealed class SafeCustomerPaymentsTests
{
    private const string TestScheme = "SafePayments";
    internal static readonly Guid UserId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    internal static readonly Guid A = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    internal static readonly Guid B = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

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
        // MB business: the tax code goes in identity (Tingee support), so taxCode is not sent.
        Assert.True(TingeeBankCatalog.Find("970422")!.Rule(TingeeAccountTypes.Business).TaxCodeAsIdentity(TingeeAccountTypes.Business));
        Assert.False(TingeeBankCatalog.Find("970415")!.Rule(TingeeAccountTypes.Household).TaxCodeAsIdentity(TingeeAccountTypes.Household));
        // VietinBank personal/household: mobile is sent although the docs call it optional (400 "Verify string error" without it).
        Assert.Contains(TingeeBankField.Mobile, TingeeBankCatalog.Find("970415")!.Rule(TingeeAccountTypes.Personal).Required);
        Assert.Contains(TingeeBankField.Mobile, TingeeBankCatalog.Find("970415")!.Rule(TingeeAccountTypes.Household).Required);
        Assert.DoesNotContain(TingeeBankField.Mobile, TingeeBankCatalog.Find("970415")!.Rule(TingeeAccountTypes.Business).Required);
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

    // Operator 2026-11-06: buyers are retail customers, households, businesses or other organisations; only a business must
    // give a tax code (and its registered name). Fields that do not apply to the chosen type are not kept.
    [Fact]
    public async Task BuyerTypesDecideWhichDetailsAreRequiredAndKept()
    {
        using var f = Factory("OWNER", out var store, out _);
        var c = Client(f);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync("/contacts/create"));
        Assert.Contains("data-cp-buyer-form", form);
        foreach (var t in new[] { "INDIVIDUAL", "HOUSEHOLD", "BUSINESS", "ORGANIZATION" }) Assert.Contains($"value=\"{t}\"", form);
        Assert.Contains("data-cp-tax-lookup=\"/account/tax-lookup\"", form);
        var token = await Token(c, "/contacts/create");

        var noTax = await Post(c, "/contacts/create", token, new() { ["BuyerType"] = "BUSINESS", ["Name"] = "Nhà hàng Sen", ["LegalName"] = "Công ty Sen" });
        Assert.Equal(HttpStatusCode.BadRequest, noTax.StatusCode);
        Assert.Contains("Khách hàng doanh nghiệp cần mã số thuế", WebUtility.HtmlDecode(await noTax.Content.ReadAsStringAsync()));

        // A retail customer: name, Zalo, CCCD and bank account; a tax code left over from another type is dropped.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/contacts/create", token, new()
        {
            ["BuyerType"] = "INDIVIDUAL", ["Name"] = "Chị Lan", ["BuyerGroup"] = "Bán lẻ", ["TaxCode"] = "0101234567", ["LegalName"] = "Cũ",
            ["IdentityNumber"] = "001 088 012345", ["Zalo"] = "0912345678", ["BankName"] = "Vietcombank", ["BankAccountNumber"] = "0123 456 789", ["BankAccountName"] = "Nguyen Thi Lan"
        })).StatusCode);
        var lan = store.Contacts.Single(x => x.Contact.Name == "Chị Lan").Contact;
        Assert.Equal(("INDIVIDUAL", null, null, "001088012345", "0912345678"), (lan.Type, lan.TaxCode, lan.LegalName, lan.IdentityNumber, lan.Zalo));
        Assert.Equal(("Vietcombank", "0123456789", "NGUYEN THI LAN", "Bán lẻ"), (lan.BankName, lan.BankAccountNumber, lan.BankAccountName, lan.BuyerGroup));

        // A business: tax code and registered name; no CCCD kept.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/contacts/create", token, new()
        {
            ["BuyerType"] = "BUSINESS", ["Name"] = "Anh Minh", ["LegalName"] = "Công ty TNHH Sen Vàng", ["TaxCode"] = "0101234567", ["IdentityNumber"] = "001088012345"
        })).StatusCode);
        var sen = store.Contacts.Single(x => x.Contact.Name == "Anh Minh").Contact;
        Assert.Equal(("BUSINESS", "0101234567", null), (sen.Type, sen.TaxCode, sen.IdentityNumber));

        // Production 2026-11-06: a looked-up tax code pasted with a trailing space (or a zero-width space) was refused as
        // badly formatted; a phone typed in groups is kept as its digits.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/contacts/create", token, new()
        {
            ["BuyerType"] = "BUSINESS", ["Name"] = "TODOX", ["LegalName"] = "CÔNG TY TNHH TODOX", ["TaxCode"] = "4202062905 ​", ["Phone"] = " 0366 699 999 ", ["Zalo"] = "+84 366.699.961"
        })).StatusCode);
        var todox = store.Contacts.Single(x => x.Contact.Name == "TODOX").Contact;
        Assert.Equal(("4202062905", "0366699999", "0366699961"), (todox.TaxCode, todox.Phone, todox.Zalo));
        Assert.Null(TACustomerPortal.Web.Controllers.ContactsController.PhoneDigits("Shop Hoa Mai"));   // a Zalo name stays a name
        Assert.Equal("0912345678", TACustomerPortal.Web.Controllers.ContactsController.PhoneDigits("(091) 234-5678"));
        var shortPhone = await Post(c, "/contacts/create", token, new() { ["Name"] = "Anh Tư", ["Phone"] = "0366 699 9" });
        Assert.Equal(HttpStatusCode.BadRequest, shortPhone.StatusCode);
        Assert.Contains("Số điện thoại gồm 10 chữ số", WebUtility.HtmlDecode(await shortPhone.Content.ReadAsStringAsync()));

        // A bank account number needs its bank.
        var noBank = await Post(c, "/contacts/create", token, new() { ["BuyerType"] = "HOUSEHOLD", ["Name"] = "Quán Bà Tư", ["BankAccountNumber"] = "123456" });
        Assert.Equal(HttpStatusCode.BadRequest, noBank.StatusCode);

        // Rows saved before the type existed: a tax code means business, otherwise a retail customer.
        Assert.Equal("BUSINESS", (Contact(Guid.NewGuid(), "KH9", "Cũ") with { TaxCode = "0101234567" }).Type);
        Assert.Equal("INDIVIDUAL", Contact(Guid.NewGuid(), "KH9", "Cũ").Type);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/contacts"));
        Assert.Contains("data-cp-buyer-type=\"INDIVIDUAL\"", list);
        Assert.Contains("Zalo: 0912345678", list);
    }

    [Fact]
    public async Task NewCustomerCodesAreNumberedAutomatically()
    {
        using var f = Factory("OWNER", out var store, out _);
        var c = Client(f);
        Assert.Contains("value=\"KH001\"", await c.GetStringAsync("/contacts/create"));   // first customer
        store.Contacts.Add((A, Contact(Guid.NewGuid(), "KH007", "Cũ")));
        store.Contacts.Add((A, Contact(Guid.NewGuid(), "VIP01", "Khác")));
        store.Contacts.Add((B, Contact(Guid.NewGuid(), "KH050", "Doanh nghiệp khác")));
        Assert.Contains("value=\"KH008\"", await c.GetStringAsync("/contacts/create"));
        var token = await Token(c, "/contacts/create");

        // Left empty: the next code is taken.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/contacts/create", token, new() { ["Code"] = "", ["Name"] = "Chị Hoa" })).StatusCode);
        Assert.Equal("KH008", store.Contacts.Single(x => x.Contact.Name == "Chị Hoa").Contact.Code);

        // The suggested code was taken meanwhile: refused, with the next free one suggested.
        var taken = await Post(c, "/contacts/create", token, new() { ["Code"] = "KH008", ["Name"] = "Anh Nam" });
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        var html = WebUtility.HtmlDecode(await taken.Content.ReadAsStringAsync());
        Assert.Contains("value=\"KH009\"", html);
        Assert.Contains("Mã khách hàng đã được dùng (KH008)", html);

        // A code typed by hand is kept; editing never renumbers.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/contacts/create", token, new() { ["Code"] = "vip02", ["Name"] = "Anh Nam" })).StatusCode);
        Assert.Equal("VIP02", store.Contacts.Single(x => x.Contact.Name == "Anh Nam").Contact.Code);
        var id = store.Contacts.Single(x => x.Contact.Name == "Anh Nam").Contact.Id;
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/contacts/edit/{id}", token, new() { ["Code"] = "", ["Name"] = "Anh Nam" })).StatusCode);
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
        Assert.Contains("chưa được kích hoạt dịch vụ thanh toán", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(tingee.CreateVaRequests);
    }

    [Fact]
    public async Task WithoutPaymentKeysTheLinkButtonIsOffAndTheCustomerIsToldWhy()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        tingee.IsConfigured = false;
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/banks"));
        Assert.Contains("data-cp-not-ready", html);
        Assert.Contains("đang được cấu hình", html);
        Assert.DoesNotContain("href=\"/banks/add\"", html, StringComparison.OrdinalIgnoreCase);
        tingee.IsConfigured = true;
        html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/banks"));
        Assert.DoesNotContain("data-cp-not-ready", html);
        Assert.Contains("href=\"/Banks/Add\"", html, StringComparison.OrdinalIgnoreCase);
    }

    // Rule: customers never see partner names (PartnerText), in fixed texts or in errors relayed from the partner.
    [Fact]
    public async Task PaymentPagesNeverNamePartners()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        var c = Client(f);
        var pages = new List<string> { await c.GetStringAsync("/banks"), await c.GetStringAsync("/") };   // not linked yet
        store.Merchants[A] = 9001;
        tingee.IsConfigured = false;
        pages.Add(await c.GetStringAsync("/banks"));
        tingee.IsConfigured = true;
        foreach (var path in new[] { "/banks", "/banks/add", "/banks/add?bin=970422&type=business-account", "/banks/add?bin=970436&type=business-account", "/orders", "/orders/create", "/contacts", "/paymentsettings", "/servicepackages", "/transactions", "/notifications" })
            pages.Add(await c.GetStringAsync(path));
        tingee.CreateVaError = new TingeeApiException("1133", "Tingee: merchant không có quyền, liên hệ support@tingee.vn");
        var failed = await Post(c, "/banks/add", await Token(c, "/banks/add?bin=970422&type=personal-account"), new()
        {
            ["Bin"] = "970422", ["AccountType"] = "personal-account", ["AccountNumber"] = "0123456789", ["AccountName"] = "A", ["Identity"] = "001099012345", ["Mobile"] = "0912345678"
        });
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        pages.Add(await failed.Content.ReadAsStringAsync());
        Assert.Contains("mã 1133", WebUtility.HtmlDecode(pages[^1]));
        foreach (var html in pages.Select(WebUtility.HtmlDecode))
            Assert.False(PartnerText.Mentions(Regex.Replace(html, "<script.*?</script>|<link[^>]*>", "", RegexOptions.Singleline)), Regex.Match(html, @"(?i).{0,80}(tingee|heno).{0,80}").Value);
    }

    // ---- bank link popup (SDK) -------------------------------------------------------------------------------
    [Fact]
    public async Task ABankLogoOpensTheLinkPopupSessionForTheCustomersMerchant()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        var c = Client(f);
        Assert.DoesNotContain("data-cp-banklink=", await c.GetStringAsync("/banks/add"));   // not activated: plain links only
        store.Merchants[A] = 9001;
        var html = await c.GetStringAsync("/banks/add");
        Assert.Contains("data-cp-banklink=\"/Banks/LinkSession\"", html);
        Assert.Contains("data-cp-bank=\"970422\"", html);
        var token = await Token(c, "/banks/add");

        var r = await Post(c, "/banks/linksession", token, new() { ["bin"] = "970422" });
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal(tingee.SessionUrl, json.GetProperty("url").GetString());
        Assert.EndsWith("/js/tingee-bank-link.js", json.GetProperty("script").GetString());
        var session = Assert.Single(tingee.Sessions);
        Assert.Equal(9001, session.MerchantId);
        Assert.Equal(TingeeBankLinkTypes.Link, session.Type);
        Assert.Equal("MBB", session.BankName);
        Assert.Null(session.BankAccountType);   // the popup asks for the account type
        Assert.Contains(A.ToString(), session.Metadata);

        var unknown = JsonDocument.Parse(await (await Post(c, "/banks/linksession", token, new() { ["bin"] = "000000" })).Content.ReadAsStringAsync()).RootElement;
        Assert.False(unknown.GetProperty("ok").GetBoolean());
        tingee.SessionUrl = "https://evil.example/link";
        var foreign = JsonDocument.Parse(await (await Post(c, "/banks/linksession", token, new() { ["bin"] = "970422" })).Content.ReadAsStringAsync()).RootElement;
        Assert.False(foreign.GetProperty("ok").GetBoolean());
        tingee.SessionError = new TingeeApiException("1133", "Tingee: merchant không có quyền");
        var failed = JsonDocument.Parse(await (await Post(c, "/banks/linksession", token, new() { ["bin"] = "970422" })).Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("mã 1133", failed.GetProperty("error").GetString());
        Assert.False(PartnerText.Mentions(failed.GetProperty("error").GetString()!));
        Assert.Equal(2, tingee.Sessions.Count);   // the foreign URL was created, then refused
    }

    [Fact]
    public async Task ViewersCannotOpenTheLinkPopup()
    {
        using var f = Factory("VIEWER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var c = Client(f);
        var r = await Post(c, "/banks/linksession", await Token(c, "/orders"), new() { ["bin"] = "970422" });
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty(tingee.Sessions);
    }

    [Fact]
    public async Task SyncAddsAccountsLinkedInThePopup()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        store.Banks.Add((A, Bank("970436", BankStatus.Active, "business-account") with { VaAccountNumber = "VA-OLD" }));
        tingee.VaItems.Add(new() { BankBin = "970436", AccountNumber = "0123456789", VaAccountNumber = "VA-OLD", Status = "active" });   // already listed
        tingee.VaItems.Add(new() { BankBin = "970422", AccountType = "personal-account", AccountNumber = "999888", AccountName = "NGUYEN VAN B", VaAccountNumber = "VA-NEW", Status = "active", ShopId = 7 });
        tingee.VaItems.Add(new() { BankBin = "970422", AccountNumber = "555", Status = "inactive" });
        var c = Client(f);
        var r = await Post(c, "/banks/sync", await Token(c, "/banks"), new());
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var added = Assert.Single(store.Banks, b => b.Account.VaAccountNumber == "VA-NEW").Account;
        Assert.Equal(("MBB", "personal-account", BankStatus.Active, "999888", 7L), (added.BankCode, added.AccountType, added.Status, added.AccountNumber, added.ShopId ?? 0));
        Assert.False(added.IsNotifyAccountNumber);
        Assert.Equal(2, store.Banks.Count);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/banks"));
        Assert.Contains("Đã thêm 1 tài khoản mới liên kết", page);
    }

    // ---- service packages --------------------------------------------------------------------------------
    [Fact]
    public async Task OwnerSeesTheBalanceAndPackagesOnSaleAndBuysForTheCurrentCompany()
    {
        var packages = new MemoryServicePackageStore(TimeProvider.System);
        var invoice = packages.Add("HD-500", ServicePackageTypes.Invoice, 500, 0, 990_000, 365);
        packages.Add("OLD", ServicePackageTypes.Transaction, 0, 10, 1_000, null, active: false);
        var paid = packages.AddPurchase(A, packages.Add("TRX-1K", ServicePackageTypes.Transaction, 0, 1000, 300_000, null));
        await packages.ConfirmPaidAsync(paid.Id, 300_000, "CASH", null, UserId);
        packages.AddPurchase(B, invoice);   // another company's purchase stays invisible
        using var f = Factory("OWNER", out _, out _, packages);
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/servicepackages"));
        Assert.Contains("data-sp-balance-transactions=\"1000\"", html);
        Assert.Contains("data-sp-balance-invoices=\"0\"", html);
        Assert.Contains("data-sp-package=\"HD-500\"", html);
        Assert.DoesNotContain("data-sp-package=\"OLD\"", html);
        Assert.Contains("990.000đ", html);
        Assert.Single(Regex.Matches(html, "data-sp-purchase=\""));

        var r = await Post(c, "/servicepackages/buy", await Token(c, "/servicepackages"), new() { ["packageId"] = invoice.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var mine = packages.Purchases.Last();
        Assert.Equal((A, ServicePurchaseStatus.Pending, ServicePurchaseVia.Customer, 990_000m), (mine.CustomerId, mine.Status, mine.RequestedVia, mine.Amount));
        Assert.Equal(1000, (await packages.BalanceAsync(A)).Transactions);   // nothing added before T.A confirms payment
        Assert.Equal(0, (await packages.BalanceAsync(A)).Invoices);

        var cancel = await Post(c, $"/servicepackages/cancel/{mine.Id}", await Token(c, "/servicepackages"), new());
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal(ServicePurchaseStatus.Cancelled, packages.Purchases.Last().Status);
        var other = packages.Purchases.Single(p => p.CustomerId == B);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/servicepackages/cancel/{other.Id}", await Token(c, "/servicepackages"), new())).StatusCode);
        Assert.Equal(ServicePurchaseStatus.Pending, packages.Purchases.Single(p => p.CustomerId == B).Status);
    }

    [Fact]
    public async Task ViewersSeePackagesButCannotBuy()
    {
        var packages = new MemoryServicePackageStore(TimeProvider.System);
        var p = packages.Add("HD-100", ServicePackageTypes.Invoice, 100, 0, 200_000, null);
        using var f = Factory("VIEWER", out _, out _, packages);
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/servicepackages"));
        Assert.Contains("data-sp-package=\"HD-100\"", html);
        Assert.DoesNotContain("action=\"/ServicePackages/Buy\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/servicepackages/buy", await Token(c, "/servicepackages"), new() { ["packageId"] = p.Id.ToString() })).StatusCode);
        Assert.Empty(packages.Purchases);
        Assert.Contains("Gói dịch vụ", WebUtility.HtmlDecode(await c.GetStringAsync("/orders")));   // menu entry
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
        // Transfer content: letters and digits only (banks reject "-"); extraInfo keeps the order number as shown.
        Assert.Equal((9001L, "TINGEE8888", "970418", 220_000m, 60, TingeeQrTypes.Recurring, order.OrderNo.Replace("-", ""), order.OrderNo),
            (qr.MerchantId, qr.VaAccountNumber, qr.BankBin, qr.Amount, qr.ExpireInMinute, qr.QrCodeType, qr.Purpose, qr.ExtraInfo));
        Assert.Matches("^DH[0-9]{10}$", qr.Purpose);
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
        Assert.Equal(("970416", "12345678", 50_000m, order.OrderNo.Replace("-", "")), (request.BankBin, request.AccountNumber, request.Amount, request.Content));
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
    public async Task CheckingPaymentRecordsThePartnerAmountForReconciliationButNeverCountsMoney()
    {
        // Operator decision 2026-11-05: money is counted from bank notifications (webhook site) only; "Kiểm tra thanh toán"
        // stores what the payment service reports next to the system's count, and a difference is shown.
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var order = Order(OrderStatus.Partial, 300_000, 100_000) with { QrType = TingeeQrTypes.Recurring, QrAccount = "QRA", BillId = "BILL1", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        store.Orders.Add((A, order));
        var c = Client(f);
        var token = await Token(c, "/orders");
        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 300_000, Status = "fully-paid" } };
        await Post(c, $"/orders/refresh/{order.Id}", token, new());
        var saved = store.Orders.Single().Order;
        Assert.Equal((OrderStatus.Partial, 100_000m, 300_000m), (saved.Status, saved.PaidAmount, saved.PartnerPaidAmount));
        Assert.True(saved.PartnerDiffers);
        Assert.Empty(tingee.DeletedQrs);
        var page = await c.GetStringAsync($"/orders/details/{order.Id}");
        Assert.Contains("Lệch đối soát", page);
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
        Assert.Equal((100_000m, 150_000m), (store.Orders.Single().Order.PaidAmount, store.Orders.Single().Order.PartnerPaidAmount));
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

    [Fact]
    public async Task QrLifetimeIsEnteredInDaysHoursAndMinutes()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var bank = Bank("970422", BankStatus.Active) with { VaAccountNumber = "VQR1" };
        store.Banks.Add((A, bank));
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/paymentsettings"));   // default 1440 min = 1 day
        Assert.Matches("name=\"DefaultQrExpireDays\"[^>]*value=\"1\"", page);
        Assert.Matches("name=\"DefaultQrExpireHours\"[^>]*value=\"0\"", page);
        Assert.Contains("Tổng: <span data-cp-duration-total>1 ngày</span>", page);

        var token = await Token(c, "/paymentsettings");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/paymentsettings", token, new()
            { ["AutoIssueInvoiceDefault"] = "true", ["DefaultQrExpireDays"] = "2", ["DefaultQrExpireHours"] = "3", ["DefaultQrExpireMins"] = "15" })).StatusCode);
        Assert.Equal(2 * 1440 + 3 * 60 + 15, (await store.SettingsAsync(A)).DefaultQrExpireMinutes);
        foreach (var bad in new[] { ("31", "0", "0"), ("30", "0", "1"), ("0", "0", "0"), ("0", "24", "0") })
        {
            var r = await Post(c, "/paymentsettings", token, new() { ["DefaultQrExpireDays"] = bad.Item1, ["DefaultQrExpireHours"] = bad.Item2, ["DefaultQrExpireMins"] = bad.Item3 });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal(3075, (await store.SettingsAsync(A)).DefaultQrExpireMinutes);

        var create = WebUtility.HtmlDecode(await c.GetStringAsync("/orders/create"));
        Assert.Contains("2 ngày 3 giờ 15 phút", create);
        var order = Lines(bank.Id, "10000");
        order.Remove("QrExpireMinutes");
        order["QrExpireDays"] = "0"; order["QrExpireHours"] = "1"; order["QrExpireMins"] = "30";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/orders/create", await Token(c, "/orders/create"), order)).StatusCode);
        Assert.Equal(90, store.Orders.Single().Order.QrExpireMinutes);
        Assert.Equal(90, tingee.DynamicQrRequests.Single().ExpireInMinute);
    }

    [Fact]
    public async Task TheDefaultAccountIsPreselectedAndADraftOrderCanSwitchAccountBeforeIssuingTheQr()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        store.Merchants[A] = 9001;
        var mb = Bank("970422", BankStatus.Active, TingeeAccountTypes.Business) with { VaAccountNumber = "VQRMB" };
        var vtb = Bank("970415", BankStatus.Active) with { VaAccountNumber = "104005395693", IsDefault = true };
        store.Banks.Add((A, mb));
        store.Banks.Add((A, vtb));
        var c = Client(f);
        var create = WebUtility.HtmlDecode(await c.GetStringAsync("/orders/create"));
        Assert.Matches($"<option value=\"{vtb.Id}\" selected=\"selected\">[^<]*· mặc định</option>", create);

        // Created on MB, the QR failed: the details page offers both accounts and "Tạo QR" uses the one picked.
        tingee.QrError = new TingeeApiException("400", "Purpose of transaction is wrong format");
        var token = await Token(c, "/orders/create");
        await Post(c, "/orders/create", token, Lines(mb.Id, "2000"));
        var order = store.Orders.Single().Order;
        Assert.Equal((OrderStatus.Draft, mb.Id), (order.Status, order.BankAccountId));
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.Contains("name=\"bankAccountId\"", details);
        Assert.Matches($"<option value=\"{mb.Id}\" selected=\"selected\">", details);

        tingee.QrError = null;
        await Post(c, $"/orders/issueqr/{order.Id}", token, new() { ["bankAccountId"] = vtb.Id.ToString() });
        var issued = store.Orders.Single().Order;
        Assert.Equal((OrderStatus.Awaiting, vtb.Id), (issued.Status, issued.BankAccountId));
        var sent = Assert.Single(tingee.StaticQrRequests);   // VietinBank personal: static QR
        Assert.Equal(("970415", "104005395693"), (sent.BankBin, sent.AccountNumber));

        // An account of another company is refused.
        var foreign = Bank("970418", BankStatus.Active) with { VaAccountNumber = "OTHER" };
        store.Banks.Add((B, foreign));
        var expired = store.Orders.Single().Order with { Status = OrderStatus.Expired, ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        store.Orders[0] = (A, expired);
        await Post(c, $"/orders/issueqr/{order.Id}", token, new() { ["bankAccountId"] = foreign.Id.ToString() });
        Assert.Single(tingee.StaticQrRequests);
        Assert.Empty(tingee.DynamicQrRequests);
        Assert.Equal(vtb.Id, store.Orders.Single().Order.BankAccountId);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static Dictionary<string, string> Lines(Guid bank, string price) => new()
    {
        ["BankAccountId"] = bank.ToString(), ["QrExpireMinutes"] = "1440", ["Lines[0].ItemName"] = "Hàng", ["Lines[0].Quantity"] = "1", ["Lines[0].UnitPrice"] = price, ["Lines[0].TaxRate"] = "0"
    };

    internal static Contact Contact(Guid id, string code, string name) => new(id, code, name, null, null, null, null, null, null, true);

    internal static BankAccount Bank(string bin, string status, string type = TingeeAccountTypes.Personal)
    {
        var bank = TingeeBankCatalog.Find(bin)!;
        return new(Guid.NewGuid(), bin, bank.Code, bank.Name, type, "0123456789", "NGUYEN VAN A", null, false, null, status, null, null, null, null, false, DateTime.UtcNow);
    }

    private static Order Order(string status, decimal amount, decimal paid) =>
        new(Guid.NewGuid(), "DH261030-0001", null, null, null, null, amount, paid, status, null, null, null, null, 1440, null, true, "NONE", null, DateTime.UtcNow,
            [OrderMath.Line(1, "Hàng", null, 1, amount, "0")]);

    internal static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    internal static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    internal static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    internal static WebApplicationFactory<Program> Factory(string role, out MemoryPaymentStore store, out FakeTingee tingee, MemoryServicePackageStore? packages = null,
        IOrderInvoiceStore? invoiceStore = null, ITingeeInvoiceApi? invoiceApi = null)
    {
        var invoiceSettings = invoiceStore ?? new SafeCustomerEInvoiceTests.MemoryInvoiceStore();
        var invoicePartner = invoiceApi ?? new SafeCustomerEInvoiceTests.FakeInvoiceApi();
        var s = store = new MemoryPaymentStore();
        var sp = packages ?? new MemoryServicePackageStore(TimeProvider.System);
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
                services.RemoveAll<IProductStore>();
                services.AddSingleton<IProductStore>(new SafeCustomerProductsTests.MemoryProductStore(s));
                var pages = new MemoryOrderPageStore(s);
                services.AddSingleton(pages);
                services.RemoveAll<TAPortal.Integrations.Branding.ILogoStore>();
                services.AddSingleton<TAPortal.Integrations.Branding.ILogoStore>(pages);
                services.RemoveAll<IOrderPageStore>();
                services.AddSingleton<IOrderPageStore>(pages);
                services.RemoveAll<ITaxCodeLookup>();   // never the public register from a test
                services.AddSingleton<ITaxCodeLookup>(new SafeCustomerSignupTests.FakeLookup());
                services.RemoveAll<IBankAppCatalog>();  // never VietQR from a test: the built-in list
                services.AddSingleton<IBankAppCatalog>(new SafeCustomerPayPageTests.FixedBankApps());
                services.RemoveAll<ITingeeApi>();
                services.AddSingleton<ITingeeApi>(t);
                services.RemoveAll<IServicePackageStore>();
                services.AddSingleton<IServicePackageStore>(sp);
                services.RemoveAll<IOrderInvoiceStore>();
                services.AddSingleton(invoiceSettings);
                services.RemoveAll<ITingeeInvoiceApi>();
                services.AddSingleton(invoicePartner);
                services.RemoveAll<IPaymentTransactionStore>();
                services.AddSingleton<IPaymentTransactionStore>(Transactions);
                services.RemoveAll<TAPortal.Integrations.Notifications.INotificationStore>();
                services.AddSingleton<TAPortal.Integrations.Notifications.INotificationStore>(new SafeNotificationTests.MemoryNotificationStore());
                services.RemoveAll<TAPortal.Integrations.Notifications.ISecretProtector>();
                services.AddSingleton<TAPortal.Integrations.Notifications.ISecretProtector>(new SafeNotificationTests.PlainProtector());
                services.RemoveAll<TAPortal.Integrations.Notifications.INotificationTransport>();
                services.AddSingleton<TAPortal.Integrations.Notifications.INotificationTransport>(new SafeNotificationTests.FakeTransport());
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

    // One store per test class run is enough: tests only add rows with fresh ids and read their own.
    private static readonly MemoryTransactionStore Transactions = new();

    public sealed class MemoryTransactionStore : IPaymentTransactionStore
    {
        public List<PaymentTransaction> Items { get; } = [];
        public List<(string Action, long Id, Guid? Scope, Guid? OrderCustomer, string? OrderNo)> Calls { get; } = [];

        public static PaymentTransaction Tx(long id, Guid? customer, string status, string content) =>
            new(id, "CREDIT", "FT" + id, 50_000, content, "MBB", "12345678", null, DateTime.UtcNow, DateTime.UtcNow, customer, null, null, null, null,
                status, null, "Không tìm thấy mã đơn hàng trong nội dung chuyển khoản", null, null, null, null, null, null, null, null);

        private IEnumerable<PaymentTransaction> Scoped(Guid? scope) { lock (Items) return Items.Where(t => scope is null || t.CustomerId == scope).ToList(); }
        public Task<IReadOnlyList<PaymentTransaction>> SearchAsync(PaymentTxFilter f, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PaymentTransaction>>(Scoped(f.CustomerId).Where(t => string.IsNullOrEmpty(f.Status) || t.Status == f.Status || (f.Status == "STAFF" && PaymentTxStatus.NeedsStaff(t.Status))).ToList());
        public Task<PaymentTxSummary> SummaryAsync(PaymentTxFilter f, CancellationToken ct = default)
        {
            var mine = Scoped(f.CustomerId).ToList();
            (int, decimal) G(string s) => (mine.Count(t => t.Status == s), mine.Where(t => t.Status == s).Sum(t => t.Amount));
            var (u, ua) = G(PaymentTxStatus.Unmatched); var (r, ra) = G(PaymentTxStatus.Review); var (m, ma) = G(PaymentTxStatus.Matched);
            return Task.FromResult(new PaymentTxSummary(u, ua, r, ra, m, ma, 0, 0, 0, 0));
        }
        public Task<PaymentTransaction?> GetAsync(long id, Guid? scope, CancellationToken ct = default) => Task.FromResult(Scoped(scope).FirstOrDefault(t => t.Id == id));
        public Task<IReadOnlyList<(Guid Id, string Code, string Name)>> CustomersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<(Guid, string, string)>>([]);
        public Task<PaymentTxResult> AssignAsync(long id, Guid? scope, Guid orderCustomerId, string orderNo, Guid actor, string via, string? note, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add(("assign", id, scope, orderCustomerId, orderNo));
            return Task.FromResult(Scoped(scope).Any(t => t.Id == id) ? PaymentTxResult.Success : new PaymentTxResult(false, "Không tìm thấy giao dịch."));
        }
        public Task<PaymentTxResult> AcceptAsync(long id, Guid? scope, Guid actor, string via, string? note, CancellationToken ct = default) { lock (Calls) Calls.Add(("accept", id, scope, null, null)); return Task.FromResult(PaymentTxResult.Success); }
        public Task<PaymentTxResult> CloseAsync(long id, Guid? scope, Guid actor, string via, string note, CancellationToken ct = default) { lock (Calls) Calls.Add(("close", id, scope, null, null)); return Task.FromResult(PaymentTxResult.Success); }
    }

    [Fact]
    public async Task TheReconciliationPageShowsOnlyTheCompanysOwnTransactionsAndActsWithinIt()
    {
        using var f = Factory("OWNER", out _, out _);
        lock (Transactions.Items)
        {
            Transactions.Items.Add(MemoryTransactionStore.Tx(9101, A, PaymentTxStatus.Unmatched, "CK mine 9101"));
            Transactions.Items.Add(MemoryTransactionStore.Tx(9102, B, PaymentTxStatus.Unmatched, "CK other 9102"));
            Transactions.Items.Add(MemoryTransactionStore.Tx(9103, null, PaymentTxStatus.Unmatched, "CK nobody 9103"));
        }
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/transactions"));
        Assert.Contains("CK mine 9101", page);
        Assert.DoesNotContain("CK other 9102", page);
        Assert.DoesNotContain("CK nobody 9103", page);
        Assert.Contains("data-cp-tx-cards", page);
        Assert.False(PartnerText.Mentions(Regex.Replace(page, "<script.*?</script>|<link[^>]*>", "", RegexOptions.Singleline)));

        // Another company's transaction: the action runs with this company as the scope, so the store refuses it.
        var token = await Token(c, "/transactions");
        await Post(c, "/transactions/assign/9102", token, new() { ["orderNo"] = "DH261105-0001" });
        lock (Transactions.Calls)
            Assert.Equal(("assign", (Guid?)A, (Guid?)A), Transactions.Calls.Where(x => x.Id == 9102).Select(x => (x.Action, x.Scope, x.OrderCustomer)).Single());
    }

    [Fact]
    public async Task OnlyOwnersAndAdminsResolveTransactions()
    {
        using var f = Factory("MEMBER", out _, out _);
        lock (Transactions.Items) Transactions.Items.Add(MemoryTransactionStore.Tx(9201, A, PaymentTxStatus.Unmatched, "CK member 9201"));
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/transactions"));
        Assert.Contains("CK member 9201", page);
        Assert.DoesNotContain("Gán vào đơn", page);
        var close = await Post(c, "/transactions/close/9201", await Token(c, "/transactions"), new() { ["note"] = "x" });
        Assert.NotEqual(HttpStatusCode.Redirect, close.StatusCode);
        lock (Transactions.Calls) Assert.DoesNotContain(Transactions.Calls, x => x.Id == 9201);
    }

    [Fact]
    public async Task TheNotificationPageIsForOwnersAndShowsOnlyTheCompanysChannels()
    {
        using (var f = Factory("MEMBER", out _, out _))
            Assert.NotEqual(HttpStatusCode.OK, (await Client(f).GetAsync("/notifications")).StatusCode);
        using var owner = Factory("OWNER", out _, out _);
        var c = Client(owner);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/notifications"));
        Assert.Contains("Nhận thông báo từ Hóa đơn tự động", page);
        Assert.Contains("data-cp-channel=\"SMTP\"", page);
        Assert.Contains("data-cp-channel=\"TELEGRAM_GROUP\"", page);
        Assert.Contains("data-cp-channel=\"ZALO_OA\"", page);
        Assert.DoesNotContain("data-cp-channel=\"TELEGRAM_BOT\"", page);   // T.A's private bot is not a company channel

        var token = await Token(c, "/notifications");
        var bad = await Post(c, "/notifications/save", token, new() { ["channel"] = "TELEGRAM_GROUP", ["enabled"] = "true", ["f_chatId"] = "-100222", ["f_botToken"] = "nope" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var secret = "123456:" + new string('Z', 35);
        var ok = await Post(c, "/notifications/save", token, new() { ["channel"] = "TELEGRAM_GROUP", ["enabled"] = "true", ["events"] = "PAYMENT", ["f_chatId"] = "-100222", ["f_botToken"] = secret });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var after = WebUtility.HtmlDecode(await c.GetStringAsync("/notifications"));
        Assert.Contains("-100222", after);
        Assert.DoesNotContain(secret, after);
        Assert.Contains("Đã lưu — để trống để giữ nguyên", after);
    }

    [Fact]
    public async Task TheOrderListHasSummaryCardsPerStatus()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Orders.Add((A, Order(OrderStatus.Awaiting, 100_000, 0) with { ExpiresAt = DateTime.UtcNow.AddHours(1) }));
        store.Orders.Add((A, Order(OrderStatus.Paid, 100_000, 120_000)));
        store.Orders.Add((A, Order(OrderStatus.ExpiredPaid, 100_000, 50_000)));
        store.Orders.Add((A, Order(OrderStatus.CancelledPaid, 100_000, 10_000)));
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync("/orders"));
        foreach (var label in new[] { "Chờ thanh toán", "Thanh toán một phần", "Đã thanh toán", "QR hết hạn có giao dịch", "Đơn hủy có giao dịch", "Trả dư", "Lệch đối soát" })
            Assert.Contains(label, page);
        Assert.Matches(new Regex("data-cp-card=\"OVERPAID\"[^§]*?1 đơn"), page);
        var overpaid = WebUtility.HtmlDecode(await c.GetStringAsync("/orders?status=OVERPAID"));
        Assert.Single(Regex.Matches(overpaid, "<tr data-ta-row"));
    }

    // Operator 2026-11-06: the list shows whether each order's e-invoice was issued.
    [Fact]
    public async Task TheOrderListShowsTheInvoiceStatus()
    {
        using var f = Factory("OWNER", out var store, out _);
        store.Orders.Add((A, Order(OrderStatus.Paid, 54_000, 54_000) with { InvoiceStatus = OrderInvoiceStatus.Issued, InvoiceNumber = "00000012" }));
        store.Orders.Add((A, Order(OrderStatus.Paid, 54_000, 54_000) with { InvoiceStatus = OrderInvoiceStatus.Failed, InvoiceError = "Nhà cung cấp hóa đơn từ chối (mã 400): CallSignServiceFail" }));
        store.Orders.Add((A, Order(OrderStatus.Awaiting, 2_000, 0) with { AutoIssueInvoice = true }));
        var page = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/orders"));
        Assert.Contains("<th>Hóa đơn</th>", page);
        Assert.Contains("Đã xuất hóa đơn</span><div class=\"cp-muted\">Số 00000012</div>", page);
        Assert.Contains("title=\"Nhà cung cấp hóa đơn từ chối (mã 400): CallSignServiceFail\">Xuất hóa đơn lỗi", page);
        Assert.Contains("Chưa xuất hóa đơn</span><div class=\"cp-muted\">Tự xuất khi thanh toán đủ</div>", page);
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
            // Same rule as SqlPaymentStore: the amount paid is never written by the portal.
            if (i >= 0) Orders[i] = (customerId, order with { Lines = Orders[i].Order.Lines, PaidAmount = Orders[i].Order.PaidAmount });
            return Task.CompletedTask;
        }
        public Task RecordPartnerPaidAsync(Guid customerId, Guid orderId, decimal partnerPaid)
        {
            var i = Orders.FindIndex(o => o.Customer == customerId && o.Order.Id == orderId);
            if (i >= 0) Orders[i] = (customerId, Orders[i].Order with { PartnerPaidAmount = partnerPaid, PartnerCheckedAt = DateTime.UtcNow });
            return Task.CompletedTask;
        }
        public Task<PaymentSettings> SettingsAsync(Guid customerId) { Check(); return Task.FromResult(settings); }
        public Task SaveSettingsAsync(Guid customerId, PaymentSettings s, Guid actor) { settings = s; return Task.CompletedTask; }
    }

    public sealed class FakeTingee : ITingeeApi
    {
        public bool IsConfigured { get; set; } = true;
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
        public Task<TingeePage<TingeeMerchantItem>> GetMerchantsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public TingeeApiException? CreateVaError { get; set; }
        public Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default) { if (CreateVaError is not null) throw CreateVaError; CreateVaRequests.Add(request); return Task.FromResult(CreateVaResult); }
        public Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default)
        {
            if (ConfirmError is not null) throw ConfirmError;
            Confirmations.Add((confirmId, otp, merchantId));
            return Task.FromResult(ConfirmVaResult);
        }
        public Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) => Task.FromResult(new TingeeDeleteVaResult());
        public Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => Task.CompletedTask;
        public List<TingeeVaItem> VaItems { get; } = [];
        public Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default) => Task.FromResult(new TingeePage<TingeeVaItem> { Items = [.. VaItems] });
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
        public Exception? StatusError { get; set; }
        public Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) =>
            StatusError is not null ? Task.FromException<TingeeQrStatus>(StatusError) : Task.FromResult(Status);
        public Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) { DeletedQrs.Add((qrAccount, billId)); return Task.CompletedTask; }
        public List<TingeeBankLinkSessionRequest> Sessions { get; } = [];
        public string SessionUrl { get; set; } = "https://uat-bank-link.tingee.vn/link?token=T1";
        public TingeeApiException? SessionError { get; set; }
        public Task<string> CreateBankLinkSessionAsync(TingeeBankLinkSessionRequest request, CancellationToken ct = default)
        {
            if (SessionError is not null) throw SessionError;
            Sessions.Add(request);
            return Task.FromResult(SessionUrl);
        }
    }
}
