using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;
using TAPortal.Integrations.Tingee;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// Logos (company and its customers) and the public payment page of an order (/pay/{token}). No SQL: the in-memory store
// below follows SqlOrderPageStore's rules (tokens per order, invoice details frozen once the invoice is issued or cancelled).
public sealed class SafeCustomerPayPageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    [Fact]
    public void LogosAreRecognisedByContentAndSvgIsRefused()
    {
        Assert.Equal("image/png", LogoUpload.Sniff(Png));
        Assert.Equal("image/jpeg", LogoUpload.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]));
        Assert.Equal("image/webp", LogoUpload.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(LogoUpload.Sniff("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8));
        Assert.Null(LogoUpload.Sniff([]));
        Assert.Matches("^[A-Za-z0-9_-]{32}$", SqlOrderPageStore.NewToken());
        Assert.NotEqual(SqlOrderPageStore.NewToken(), SqlOrderPageStore.NewToken());
        Assert.Equal("BM", TACustomerPortal.Web.Models.PayPageVm.Initials("Công ty TNHH Bình Minh"));
        Assert.Equal("A", TACustomerPortal.Web.Models.PayPageVm.Initials("Công ty A"));
        Assert.Equal("?", TACustomerPortal.Web.Models.PayPageVm.Initials(" "));
    }

    [Fact]
    public async Task ThePaymentPageShowsBothPartiesTheOrderTheQrAndACountdown()
    {
        using var f = Factory("MEMBER", out var store, out _);
        var pages = f.Services.GetRequiredService<MemoryOrderPageStore>();
        var contact = new Contact(Guid.NewGuid(), "KH001", "Nhà hàng Sen", "Công ty TNHH Sen Việt", "0109876543", null, null, null, null, true, BuyerTypes.Business);
        store.Contacts.Add((A, contact));
        var order = OpenOrder(contact.Id);
        store.Orders.Add((A, order));
        pages.Logos[(A, contact.Id)] = new LogoImage("image/png", Png, DateTime.UtcNow);
        var c = Client(f);

        // The order page gives the link; members (not only managers) can see and share it.
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        var link = Regex.Match(details, "href=\"(https://localhost/pay/[A-Za-z0-9_-]{32})\"[^>]*>.*?Xem link QR", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(link);
        Assert.Equal(link, Regex.Match(await c.GetStringAsync($"/orders/details/{order.Id}"), "https://localhost/pay/[A-Za-z0-9_-]{32}").Value);   // stable

        var page = WebUtility.HtmlDecode(await c.GetStringAsync(link));
        Assert.Contains("Công ty A", page);                                   // seller
        Assert.Contains("Nhà hàng Sen", page);                                // their customer
        Assert.Contains("class=\"cp-pay__bridge\"", page);                    // the two logos are joined
        // Operator 2026-11-06: customer on the left, seller on the right, a bank in between (the money's path).
        var buyerAt = page.IndexOf("cp-pay__party--buyer", StringComparison.Ordinal);
        var bridgeAt = page.IndexOf("class=\"cp-pay__bridge\"", StringComparison.Ordinal);
        Assert.True(buyerAt >= 0 && buyerAt < bridgeAt && bridgeAt < page.IndexOf("cp-pay__party--seller", StringComparison.Ordinal));
        Assert.Contains("M5 6l7 -3l7 3", page[bridgeAt..page.IndexOf("cp-pay__party--seller", StringComparison.Ordinal)]);   // building-bank icon
        Assert.Contains(link[17..] + "/logo/buyer?v=", page);                 // buyer logo uploaded, seller shows initials
        Assert.DoesNotContain("/logo/seller", page);
        Assert.Contains("Tiệc cuối năm", page);                               // title = description
        Assert.Contains("value=\"0109876543\"", page);                        // invoice details prefilled from the buyer
        Assert.Contains("value=\"Công ty TNHH Sen Việt\"", page);
        Assert.Contains("<svg", page);
        Assert.Contains("150.000 ₫", page);
        Assert.Contains("DH2611060001", page);                               // transfer content
        Assert.Matches("data-cp-countdown=\"[0-9T:.-]+", page);
        Assert.Contains("data-cp-poll=\"" + link[17..] + "/status\"", page);
        Assert.False(PartnerText.Mentions(Regex.Replace(page, "<script.*?</script>|<link[^>]*>", "", RegexOptions.Singleline)));
        Assert.DoesNotContain("cp-sidebar", page);                            // its own shell, no portal navigation

        var logo = await c.GetAsync(link + "/logo/buyer");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(link + "/logo/seller")).StatusCode);
        Assert.Contains("\"status\":\"AWAITING_PAYMENT\"", await c.GetStringAsync(link + "/status"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pay/" + new string('x', 32))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/pay/" + order.Id)).StatusCode);   // the order id never opens it
    }

    [Fact]
    public async Task ThePayerCorrectsInvoiceDetailsUntilTheInvoiceIsIssued()
    {
        using var f = Factory("MEMBER", out var store, out _);
        var pages = f.Services.GetRequiredService<MemoryOrderPageStore>();
        var order = OpenOrder(null);   // walk-in: no buyer profile
        store.Orders.Add((A, order));
        var token = await pages.TokenAsync(A, order.Id);
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/pay/{token}"));
        Assert.Contains("is-single", page);                                   // seller alone
        Assert.Contains("data-cp-dirty-save", page);

        var form = await Token(c, $"/pay/{token}");
        var bad = await Post(c, $"/pay/{token}/invoice", form, new() { ["TaxCode"] = "12", ["Name"] = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Mã số thuế gồm 10 chữ số", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/pay/{token}/invoice", form, new() { ["TaxCode"] = "0101234567", ["Name"] = " " })).StatusCode);

        var ok = await Post(c, $"/pay/{token}/invoice", form, new() { ["TaxCode"] = " 0101 234 567 ", ["Name"] = " Công ty Bình Minh " });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(new InvoiceBuyer("0101234567", "Công ty Bình Minh", pages.Buyers[order.Id].UpdatedAt), await pages.InvoiceBuyerAsync(A, order.Id));
        Assert.Contains("value=\"Công ty Bình Minh\"", WebUtility.HtmlDecode(await c.GetStringAsync($"/pay/{token}")));
        // The company sees what the payer entered.
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.Contains("data-cp-invoice-buyer", details);
        Assert.Contains("0101234567", details);

        // Issued: the details are shown, not editable, and a late post changes nothing.
        var i = store.Orders.FindIndex(o => o.Order.Id == order.Id);
        store.Orders[i] = (A, store.Orders[i].Order with { InvoiceStatus = OrderInvoiceStatus.Issued });
        var frozen = WebUtility.HtmlDecode(await c.GetStringAsync($"/pay/{token}"));
        Assert.DoesNotContain("data-cp-dirty-form", frozen);
        Assert.Contains("Hóa đơn điện tử đã được xuất", frozen);
        var late = await Post(c, $"/pay/{token}/invoice", form, new() { ["TaxCode"] = "0309999999", ["Name"] = "Khác" });
        Assert.Equal(HttpStatusCode.Redirect, late.StatusCode);
        Assert.Equal("0101234567", (await pages.InvoiceBuyerAsync(A, order.Id)).TaxCode);
    }

    [Fact]
    public async Task ManagersUploadTheCompanyAndCustomerLogosAndSvgIsRefused()
    {
        using var f = Factory("OWNER", out var store, out _);
        var pages = f.Services.GetRequiredService<MemoryOrderPageStore>();
        var contact = new Contact(Guid.NewGuid(), "KH001", "Chị Hoa", null, null, null, null, null, null, true);
        store.Contacts.Add((A, contact));
        var c = Client(f);

        var token = await Token(c, "/profile/company");
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(c, "/profile/logo", token, "logo", "<svg/>"u8.ToArray())).StatusCode);
        Assert.False(pages.Logos.ContainsKey((A, null)));
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(c, "/profile/logo", token, "logo", Png)).StatusCode);
        Assert.Equal("image/png", pages.Logos[(A, null)].ContentType);
        var company = WebUtility.HtmlDecode(await c.GetStringAsync("/profile/company"));
        Assert.Contains("/profile/companylogo?v=", company, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("image/png", (await c.GetAsync("/profile/companylogo")).Content.Headers.ContentType?.MediaType);
        Assert.Contains("data-cp-company-logo", company);                                  // sidebar shows the logo instead of initials

        var edit = await Token(c, $"/contacts/edit/{contact.Id}");
        var refused = await Upload(c, $"/contacts/edit/{contact.Id}", edit, "logo", "GIF89a"u8.ToArray(), new() { ["Code"] = "KH001", ["Name"] = "Chị Hoa" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("PNG, JPG hoặc WEBP", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(c, $"/contacts/edit/{contact.Id}", edit, "logo", Png, new() { ["Code"] = "KH001", ["Name"] = "Chị Hoa" })).StatusCode);
        Assert.True(pages.Logos.ContainsKey((A, contact.Id)));
        Assert.Contains($"/contacts/logo/{contact.Id}?v=", WebUtility.HtmlDecode(await c.GetStringAsync($"/contacts/edit/{contact.Id}")), StringComparison.OrdinalIgnoreCase);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/contacts"));
        Assert.Contains($"/contacts/logo/{contact.Id}?v=", list, StringComparison.OrdinalIgnoreCase);   // logo in the customer list
        Assert.Single(Regex.Matches(list, "data-cp-contact-logo"));
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(c, $"/contacts/edit/{contact.Id}", edit, null, null, new() { ["Code"] = "KH001", ["Name"] = "Chị Hoa", ["removeLogo"] = "true" })).StatusCode);
        Assert.False(pages.Logos.ContainsKey((A, contact.Id)));

        // A new customer gets its logo in the same save.
        var created = await Upload(c, "/contacts/create", await Token(c, "/contacts/create"), "logo", Png, new() { ["Code"] = "KH002", ["Name"] = "Anh Nam" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.True(pages.Logos.ContainsKey((A, store.Contacts.Single(x => x.Contact.Code == "KH002").Contact.Id)));
    }

    [Fact]
    public async Task MembersCannotChangeTheCompanyLogo()
    {
        using var f = Factory("MEMBER", out _, out _);
        var c = Client(f);
        var token = await Token(c, "/profile/company");
        Assert.DoesNotContain("data-cp-logo-input", await c.GetStringAsync("/profile/company"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(c, "/profile/logo", token, "logo", Png)).StatusCode);
        Assert.False(f.Services.GetRequiredService<MemoryOrderPageStore>().Logos.ContainsKey((A, null)));
    }

    private static Order OpenOrder(Guid? contact) =>
        new(Guid.NewGuid(), "DH261106-0001", contact, null, null, "Tiệc cuối năm", 150_000, 0, OrderStatus.Awaiting, TingeeQrTypes.Static,
            "00020101021238570010A000000727", "123456789", null, 1440, DateTime.UtcNow.AddHours(2), true, OrderInvoiceStatus.None, null, DateTime.UtcNow,
            [TACustomerPortal.Web.Services.OrderMath.Line(1, "Set tiệc", "suất", 1, 150_000, "0")]);

    private static Task<HttpResponseMessage> Upload(HttpClient c, string path, string token, string? field, byte[]? bytes, Dictionary<string, string>? data = null)
    {
        var body = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (k, v) in data ?? []) body.Add(new StringContent(v), k);
        if (field is not null && bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");   // the declared type is ignored: content decides
            body.Add(file, field, "logo.png");
        }
        return c.PostAsync(path, body);
    }
}

/// <summary>In-memory logos and payment pages over the in-memory payment store.</summary>
public sealed class MemoryOrderPageStore(SafeCustomerPaymentsTests.MemoryPaymentStore orders) : ILogoStore, IOrderPageStore
{
    public Dictionary<(Guid Customer, Guid? Buyer), LogoImage> Logos { get; } = [];
    public Dictionary<Guid, (Guid Customer, string Token)> Tokens { get; } = [];
    public Dictionary<Guid, InvoiceBuyer> Buyers { get; } = [];

    public Task<DateTime?> LogoVersionAsync(Guid customerId, Guid? buyerId) => Task.FromResult(Logos.TryGetValue((customerId, buyerId), out var l) ? l.UpdatedAt : (DateTime?)null);
    public Task<IReadOnlyDictionary<Guid, DateTime>> BuyerLogoVersionsAsync(Guid customerId) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(Logos.Where(l => l.Key.Customer == customerId && l.Key.Buyer is not null).ToDictionary(l => l.Key.Buyer!.Value, l => l.Value.UpdatedAt));
    public Task<LogoImage?> LogoAsync(Guid customerId, Guid? buyerId) => Task.FromResult(Logos.GetValueOrDefault((customerId, buyerId)));
    public Task SaveLogoAsync(Guid customerId, Guid? buyerId, LogoImage logo, Guid actor)
    {
        if (buyerId is null || orders.Contacts.Any(c => c.Customer == customerId && c.Contact.Id == buyerId)) Logos[(customerId, buyerId)] = logo;
        return Task.CompletedTask;
    }
    public Task RemoveLogoAsync(Guid customerId, Guid? buyerId) { Logos.Remove((customerId, buyerId)); return Task.CompletedTask; }

    public Task<string?> TokenAsync(Guid customerId, Guid orderId)
    {
        if (!orders.Orders.Any(o => o.Customer == customerId && o.Order.Id == orderId)) return Task.FromResult<string?>(null);
        if (!Tokens.TryGetValue(orderId, out var t)) Tokens[orderId] = t = (customerId, SqlOrderPageStore.NewToken());
        return Task.FromResult<string?>(t.Token);
    }

    public Task<PayPageOrder?> FindAsync(string token)
    {
        var hit = Tokens.FirstOrDefault(t => t.Value.Token == token);
        if (hit.Value.Token is null) return Task.FromResult<PayPageOrder?>(null);
        var b = Buyers.GetValueOrDefault(hit.Key);
        return Task.FromResult<PayPageOrder?>(new(hit.Value.Customer, hit.Key, hit.Value.Customer == SafeCustomerPaymentsTests.A ? "Công ty A" : "Công ty B", null, b?.TaxCode, b?.Name, b?.UpdatedAt));
    }

    public Task<InvoiceBuyer> InvoiceBuyerAsync(Guid customerId, Guid orderId) => Task.FromResult(Buyers.GetValueOrDefault(orderId) ?? new(null, null, null));

    public Task<bool> SaveInvoiceBuyerAsync(string token, string? taxCode, string? name)
    {
        var hit = Tokens.FirstOrDefault(t => t.Value.Token == token);
        var order = orders.Orders.FirstOrDefault(o => o.Order.Id == hit.Key).Order;
        if (order is null || order.InvoiceStatus is not (OrderInvoiceStatus.None or OrderInvoiceStatus.Failed) || order.Status is OrderStatus.Cancelled or OrderStatus.CancelledPaid)
            return Task.FromResult(false);
        Buyers[hit.Key] = new(taxCode, name, DateTime.UtcNow);
        return Task.FromResult(true);
    }
}
