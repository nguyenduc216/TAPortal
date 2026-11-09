using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;
using TAPortal.Integrations.Tingee;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// Product catalogue (migration 034) and per-customer prices: catalogue prices include VAT, order lines convert them to the
// price before tax; a price the user chose to keep for a customer is offered on that customer's next orders. No SQL.
public sealed class SafeCustomerProductsTests
{
    [Theory]
    [InlineData(100_000, "8", 92_593)]
    [InlineData(110_000, "10", 100_000)]
    [InlineData(55_000, "0", 55_000)]
    [InlineData(55_000, "-2", 55_000)]
    [InlineData(105_000, "5", 100_000)]
    public void PriceIncludingVatBecomesThePriceBeforeTax(decimal withTax, string rate, decimal before) =>
        Assert.Equal(before, OrderMath.PriceBeforeTax(withTax, rate));

    [Fact]
    public void AVatInclusiveLineChargesExactlyQuantityTimesPrice()
    {
        var line = OrderMath.LineWithTax(1, " Cà phê ", "Ly", 2, 35_000, "8");
        Assert.Equal((70_000m, 64_815m, 5_185m, 32_407m, "Cà phê"), (line.Total, line.AmountBeforeTax, line.TaxAmount, line.UnitPrice, line.ItemName));
        Assert.Equal(55_000, OrderMath.LineWithTax(1, "x", null, 1, 55_000, "-1").Total);
        Assert.Equal(0, OrderMath.LineWithTax(1, "x", null, 1, 55_000, "-1").TaxAmount);
    }

    [Fact]
    public async Task OwnerAddsAProductWithImageAndSeesItListed()
    {
        using var f = Factory("OWNER", out _, out _);
        var products = Store(f);
        var c = Client(f);
        var token = await Token(c, "/products/create");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("Cà phê sữa"), "Name" }, { new StringContent("Ly"), "UnitName" },
            { new StringContent("35000"), "Price" }, { new StringContent("8"), "TaxRate" }, { new StringContent("true"), "IsActive" },
        };
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        form.Add(new ByteArrayContent(png), "image", "cafe.png");
        var response = await c.PostAsync("/products/create", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = Assert.Single(products.Products, p => p.Customer == A).Product;
        Assert.Equal(("Cà phê sữa", "Ly", 35_000m, "8", true), (saved.Name, saved.UnitName, saved.Price, saved.TaxRateCode, saved.IsActive));
        Assert.NotNull(products.Images[saved.Id]);

        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/products"));
        Assert.Contains("Cà phê sữa", list);
        Assert.Contains("35.000 ₫", list);
        Assert.Contains("/Products/Image/" + saved.Id, list);
        Assert.Equal("image/png", (await c.GetAsync("/products/image/" + saved.Id)).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProductFormRefusesAFractionalPriceAndViewersCannotEdit()
    {
        using var owner = Factory("OWNER", out _, out _);
        var c = Client(owner);
        var response = await Post(c, "/products/create", await Token(c, "/products/create"), new() { ["Name"] = "Gói", ["Price"] = "1000.5", ["TaxRate"] = "0", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không lẻ", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(Store(owner).Products);

        using var viewer = Factory("VIEWER", out _, out _);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(viewer).GetAsync("/products/create")).StatusCode);
    }

    [Fact]
    public async Task OtherCompaniesProductsAreNeverShownOrUsed()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var foreign = products.Add(B, "Hàng công ty B", 50_000, "0");
        var c = Client(f);
        Assert.DoesNotContain("Hàng công ty B", WebUtility.HtmlDecode(await c.GetStringAsync("/products")));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/products/edit/" + foreign.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/products/image/" + foreign.Id)).StatusCode);

        var bank = Ready(store);
        var response = await Post(c, "/orders/create", await Token(c, "/orders/create"), Order(bank, null, (foreign.Id, "Hàng công ty B", "50000", "0", false)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sản phẩm không tồn tại", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(store.Orders);
    }

    [Fact]
    public async Task OrderFormListsActiveProductsWithTheCustomersOwnPrice()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var cafe = products.Add(A, "Cà phê sữa", 35_000, "8", unit: "Ly");
        products.Add(A, "Món ngừng bán", 10_000, "0", active: false);
        var buyer = Guid.NewGuid();
        store.Contacts.Add((A, Contact(buyer, "KH001", "Quán Xanh")));
        products.Prices[(buyer, cafe.Id)] = 32_000;
        Ready(store);
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/orders/create?contactId=" + buyer));
        Assert.Contains("data-cp-open-products", html);
        Assert.Contains("Đơn giá gồm VAT", html);
        Assert.Contains("name=\"PricesIncludeTax\" value=\"true\"", html);
        var json = System.Text.RegularExpressions.Regex.Match(html, "data-cp-products>(.*?)</script>").Groups[1].Value;
        var list = JsonDocument.Parse(json).RootElement;
        Assert.Equal(1, list.GetArrayLength());   // inactive products are not offered
        Assert.Equal("Cà phê sữa", list[0].GetProperty("name").GetString());
        Assert.Contains("32000", System.Text.RegularExpressions.Regex.Match(html, "data-cp-buyer-prices>(.*?)</script>").Groups[1].Value);

        var prices = JsonDocument.Parse(await c.GetStringAsync("/orders/buyerprices?contactId=" + buyer)).RootElement;
        Assert.Equal(32_000, prices.GetProperty(cafe.Id.ToString()).GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/orders/buyerprices?contactId=" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task OrderWithAProductConvertsTheVatInclusivePriceAndKeepsTheChosenCustomerPrice()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var cafe = products.Add(A, "Cà phê sữa", 35_000, "8", unit: "Ly");
        var tea = products.Add(A, "Trà đào", 30_000, "8", unit: "Ly");
        var buyer = Guid.NewGuid();
        store.Contacts.Add((A, Contact(buyer, "KH001", "Quán Xanh")));
        var bank = Ready(store);
        var c = Client(f);
        var response = await Post(c, "/orders/create", await Token(c, "/orders/create"), Order(bank, buyer,
            (cafe.Id, "Cà phê sữa", "32400", "8", true),     // new price, user chose to keep it
            (tea.Id, "Trà đào", "28000", "8", false)));     // new price, not kept
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var order = store.Orders.Single().Order;
        Assert.Equal(30_000, order.Lines[0].UnitPrice);   // 32 400 incl. 8% → 30 000 before tax
        Assert.Equal(64_800, order.Lines[0].Total);       // quantity 2
        Assert.Equal(cafe.Id, order.Lines[0].ProductId);
        Assert.Equal(25_926, order.Lines[1].UnitPrice);   // 28 000 / 1.08
        Assert.Equal(32_400, products.Prices[(buyer, cafe.Id)]);
        Assert.False(products.Prices.ContainsKey((buyer, tea.Id)));

        // Back to the catalogue price and kept: the customer's own price is removed.
        var again = await Post(c, "/orders/create", await Token(c, "/orders/create"), Order(bank, buyer, (cafe.Id, "Cà phê sữa", "35000", "8", true)));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.False(products.Prices.ContainsKey((buyer, cafe.Id)));
    }

    [Fact]
    public async Task PricesAreNotKeptForAWalkInCustomerAndOldFormsKeepPricesBeforeTax()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var cafe = products.Add(A, "Cà phê sữa", 35_000, "8");
        var bank = Ready(store);
        var c = Client(f);
        var data = Order(bank, null, (cafe.Id, "Cà phê sữa", "30000", "8", true));
        data.Remove("PricesIncludeTax");   // a form without the field: price before tax, as before
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, "/orders/create", await Token(c, "/orders/create"), data)).StatusCode);
        Assert.Equal(30_000, store.Orders.Single().Order.Lines[0].UnitPrice);
        Assert.Empty(products.Prices);
    }

    [Fact]
    public async Task ProductPageListsAndRemovesCustomerPrices()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var cafe = products.Add(A, "Cà phê sữa", 35_000, "8");
        var buyer = Guid.NewGuid();
        store.Contacts.Add((A, Contact(buyer, "KH001", "Quán Xanh")));
        products.Prices[(buyer, cafe.Id)] = 32_000;
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/products/edit/" + cafe.Id));
        Assert.Contains("Quán Xanh", html);
        Assert.Contains("32.000 ₫", html);
        Assert.Contains("−3.000 ₫", html);
        Assert.Contains("1 khách", WebUtility.HtmlDecode(await c.GetStringAsync("/products")));
        var response = await Post(c, "/products/removeprice/" + cafe.Id, await Token(c, "/products/edit/" + cafe.Id), new() { ["buyerId"] = buyer.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(products.Prices);
    }

    [Fact]
    public async Task DeactivatedProductStaysListedButLeavesThePicker()
    {
        using var f = Factory("OWNER", out var store, out _);
        var products = Store(f);
        var cafe = products.Add(A, "Cà phê sữa", 35_000, "8");
        Ready(store);
        var c = Client(f);
        var response = await Post(c, "/products/setactive/" + cafe.Id, await Token(c, "/products"), new() { ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Ngừng bán", WebUtility.HtmlDecode(await c.GetStringAsync("/products")));
        Assert.DoesNotContain("data-cp-open-products", await c.GetStringAsync("/orders/create"));
    }

    [Fact]
    public async Task MenuShowsProducts()
    {
        using var f = Factory("VIEWER", out _, out _);
        Assert.Contains("href=\"/Products\"", await Client(f).GetStringAsync("/products"));
    }

    // ---- helpers -----------------------------------------------------------------------------------------
    private static MemoryProductStore Store(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> f) => (MemoryProductStore)f.Services.GetRequiredService<IProductStore>();

    private static Guid Ready(MemoryPaymentStore store)
    {
        store.Merchants[A] = 9001;
        var bank = Bank("970418", BankStatus.Active, TingeeAccountTypes.Business) with { VaAccountNumber = "TINGEE8888" };
        store.Banks.Add((A, bank));
        return bank.Id;
    }

    private static Dictionary<string, string> Order(Guid bank, Guid? contact, params (Guid Product, string Name, string Price, string Tax, bool Remember)[] lines)
    {
        var data = new Dictionary<string, string> { ["BankAccountId"] = bank.ToString(), ["QrExpireMinutes"] = "60", ["PricesIncludeTax"] = "true", ["ContactId"] = contact?.ToString() ?? "" };
        for (var n = 0; n < lines.Length; n++)
        {
            var l = lines[n];
            data[$"Lines[{n}].ProductId"] = l.Product.ToString();
            data[$"Lines[{n}].ItemName"] = l.Name;
            data[$"Lines[{n}].Quantity"] = n == 0 ? "2" : "1";
            data[$"Lines[{n}].UnitPrice"] = l.Price;
            data[$"Lines[{n}].TaxRate"] = l.Tax;
            data[$"Lines[{n}].RememberPrice"] = l.Remember ? "true" : "false";
        }
        return data;
    }

    public sealed class MemoryProductStore(MemoryPaymentStore payments) : IProductStore
    {
        public List<(Guid Customer, Product Product)> Products { get; } = [];
        public Dictionary<Guid, LogoImage> Images { get; } = [];
        public Dictionary<(Guid Buyer, Guid Product), decimal> Prices { get; } = [];

        public Product Add(Guid customer, string name, decimal price, string tax, string? unit = null, bool active = true)
        {
            var p = new Product(Guid.NewGuid(), name, unit, price, tax, active, null);
            Products.Add((customer, p));
            return p;
        }

        private Product With(Product p) => p with { ImageAt = Images.TryGetValue(p.Id, out var i) ? i.UpdatedAt : null, CustomPrices = Prices.Keys.Count(k => k.Product == p.Id) };

        public Task<IReadOnlyList<Product>> ProductsAsync(Guid customerId) =>
            Task.FromResult<IReadOnlyList<Product>>(Products.Where(p => p.Customer == customerId).Select(p => With(p.Product)).ToList());
        public Task<Product?> ProductAsync(Guid customerId, Guid id) =>
            Task.FromResult(Products.Where(p => p.Customer == customerId && p.Product.Id == id).Select(p => With(p.Product)).FirstOrDefault());
        public Task<Guid> SaveProductAsync(Guid customerId, Product product, LogoImage? image, bool removeImage, Guid actor)
        {
            var id = product.Id == Guid.Empty ? Guid.NewGuid() : product.Id;
            var i = Products.FindIndex(p => p.Customer == customerId && p.Product.Id == id);
            if (product.Id != Guid.Empty && i < 0) return Task.FromResult(Guid.Empty);
            if (i >= 0) Products[i] = (customerId, product with { Id = id }); else Products.Add((customerId, product with { Id = id }));
            if (image is not null) Images[id] = image; else if (removeImage) Images.Remove(id);
            return Task.FromResult(id);
        }
        public Task SetActiveAsync(Guid customerId, Guid id, bool active, Guid actor)
        {
            var i = Products.FindIndex(p => p.Customer == customerId && p.Product.Id == id);
            if (i >= 0) Products[i] = (customerId, Products[i].Product with { IsActive = active });
            return Task.CompletedTask;
        }
        public Task<LogoImage?> ImageAsync(Guid customerId, Guid id) =>
            Task.FromResult(Products.Any(p => p.Customer == customerId && p.Product.Id == id) && Images.TryGetValue(id, out var image) ? image : null);
        public Task<IReadOnlyDictionary<Guid, decimal>> BuyerPricesAsync(Guid customerId, Guid buyerId) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(Prices.Where(p => p.Key.Buyer == buyerId && Products.Any(x => x.Customer == customerId && x.Product.Id == p.Key.Product))
                .ToDictionary(p => p.Key.Product, p => p.Value));
        public Task<IReadOnlyList<BuyerPrice>> ProductPricesAsync(Guid customerId, Guid productId) =>
            Task.FromResult<IReadOnlyList<BuyerPrice>>(Prices.Where(p => p.Key.Product == productId)
                .Select(p => (Price: p, Buyer: payments.Contacts.FirstOrDefault(c => c.Customer == customerId && c.Contact.Id == p.Key.Buyer).Contact))
                .Where(x => x.Buyer is not null)
                .Select(x => new BuyerPrice(x.Buyer.Id, x.Buyer.Code, x.Buyer.Name, productId, x.Price.Value, DateTime.UtcNow)).ToList());
        public Task SaveBuyerPricesAsync(Guid customerId, Guid buyerId, IReadOnlyDictionary<Guid, decimal> prices, Guid actor)
        {
            // Same rules as SqlProductStore: only this company's products and buyers; the catalogue price removes the own price.
            if (!payments.Contacts.Any(c => c.Customer == customerId && c.Contact.Id == buyerId)) return Task.CompletedTask;
            foreach (var (productId, price) in prices)
            {
                var product = Products.Where(p => p.Customer == customerId && p.Product.Id == productId).Select(p => p.Product).FirstOrDefault();
                if (product is null) continue;
                if (product.Price == price) Prices.Remove((buyerId, productId)); else Prices[(buyerId, productId)] = price;
            }
            return Task.CompletedTask;
        }
        public Task RemoveBuyerPriceAsync(Guid customerId, Guid buyerId, Guid productId) { Prices.Remove((buyerId, productId)); return Task.CompletedTask; }
    }
}
