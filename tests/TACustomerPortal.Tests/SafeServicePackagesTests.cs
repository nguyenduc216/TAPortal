extern alias admin;

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
using TAPortal.Shared;
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Service packages (migration 018): rules, Admin catalogue/purchases/confirmation and the customer workspace tab. No SQL:
// MemoryServicePackageStore mirrors the SQL store (snapshot on request, lot on confirmation, balance of unexpired lots).
// Customer Portal pages are covered in SafeCustomerPaymentsTests (its signed-in customer harness).
public sealed class SafeServicePackagesTests
{
    private const string TestScheme = "SafeServicePackages";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // The purchase list wraps the purchase SELECT in "SELECT TOP (@take) * FROM (...) x": SQL Server refuses a derived table
    // whose columns repeat a name ("The column 'DisplayName' was specified multiple times for 'x'", production 2026-11-04).
    [Fact]
    public void PurchaseSelectHasUniqueColumnNames()
    {
        var sql = (string)typeof(SqlServicePackageStore).GetField("PurchaseSelect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var list = Regex.Match(sql, @"SELECT\s+(.*?)\s+FROM\s", RegexOptions.Singleline).Groups[1].Value;
        var names = list.Split(',').Select(c => c.Trim()).Select(c => Regex.Match(c, @"(?:\s+AS\s+|\.)(\w+)$", RegexOptions.IgnoreCase).Groups[1].Value).ToList();
        Assert.Equal(28, names.Count);
        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 11, 4, 5, 0, 0, DateTimeKind.Utc);

    // ---- rules ------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(ServicePackageTypes.Invoice, 500, 0, true)]
    [InlineData(ServicePackageTypes.Invoice, 0, 0, false)]
    [InlineData(ServicePackageTypes.Transaction, 0, 1000, true)]
    [InlineData(ServicePackageTypes.Transaction, 0, 0, false)]
    [InlineData(ServicePackageTypes.Combo, 100, 1000, true)]
    [InlineData(ServicePackageTypes.Combo, 100, 0, false)]
    [InlineData("OTHER", 1, 1, false)]
    public void PackageTypeDecidesWhichQuantitiesAreRequired(string type, int invoices, int transactions, bool valid)
    {
        var input = new ServicePackageInput { Code = "x", Name = "x", PackageType = type, InvoiceQuota = invoices, TransactionQuota = transactions };
        Assert.Equal(valid, !ServicePackageRules.Validate(input).Any());
    }

    [Fact]
    public void NormalizeClearsTheQuantityTheTypeDoesNotInclude()
    {
        var input = new ServicePackageInput { Code = " hd-500 ", Name = " Gói 500 HĐ ", PackageType = ServicePackageTypes.Invoice, InvoiceQuota = 500, TransactionQuota = 99, Description = "  " };
        ServicePackageRules.Normalize(input);
        Assert.Equal(("HD-500", "Gói 500 HĐ", 0, (string?)null), (input.Code, input.Name, input.TransactionQuota, input.Description));
    }

    [Fact]
    public void BalanceCountsOnlyPaidLotsThatHaveNotExpired()
    {
        var lots = new[]
        {
            Lot(ServicePurchaseStatus.Paid, 100, 10, expires: null),
            Lot(ServicePurchaseStatus.Paid, 50, 5, expires: Now.AddDays(3)),
            Lot(ServicePurchaseStatus.Paid, 999, 999, expires: Now.AddMinutes(-1)),   // expired
            Lot(ServicePurchaseStatus.Pending, 777, 777, expires: null),
            Lot(ServicePurchaseStatus.Cancelled, 888, 888, expires: null),
        };
        var b = ServicePackageRules.Balance(lots, Now);
        Assert.Equal((150, 15, (DateTime?)Now.AddDays(3)), (b.Transactions, b.Invoices, b.NextExpiry));
        Assert.Equal(Now.AddDays(30), ServicePackageRules.ExpiresAt(Now, 30));
        Assert.Null(ServicePackageRules.ExpiresAt(Now, null));
        Assert.Matches("^DV261104[0-9A-F]{6}$", ServicePackageRules.NewPurchaseNo(Now));
        Assert.Equal("1.200 hóa đơn + 5.000 giao dịch", ServicePackageRules.Quantities(1200, 5000));
        Assert.Equal("1.990.000đ", ServicePackageRules.Money(1_990_000));
    }

    // ---- Admin ------------------------------------------------------------------------------------------
    [Fact]
    public async Task AdminCreatesPackagesWithTypeRulesAndUniqueCodes()
    {
        using var f = Factory("SYS_ADMIN", out var store);
        var c = Client(f);
        var token = await Token(c, "/servicepackages/create");
        var bad = await Post(c, "/servicepackages/create", token, Package("COMBO-1", ServicePackageTypes.Combo, invoices: "100", transactions: "0"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Vui lòng nhập số lượng giao dịch lớn hơn 0.", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));

        var ok = await Post(c, "/servicepackages/create", token, Package("hd-500", ServicePackageTypes.Invoice, invoices: "500", transactions: "77", days: ""));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var saved = Assert.Single(store.Packages);
        Assert.Equal(("HD-500", 500, 0, (int?)null, 990_000m), (saved.Code, saved.InvoiceQuota, saved.TransactionQuota, saved.ValidityDays, saved.Price));

        var dup = await Post(c, "/servicepackages/create", token, Package("HD-500", ServicePackageTypes.Transaction, invoices: "0", transactions: "10"));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("Mã gói đã được dùng.", WebUtility.HtmlDecode(await dup.Content.ReadAsStringAsync()));

        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/servicepackages"));
        Assert.Contains("data-sp-package=\"HD-500\"", list);
        Assert.Contains("Gói hóa đơn", list);
        Assert.Contains("990.000đ", list);
        Assert.Contains("Không hết hạn", list);
    }

    [Fact]
    public async Task AdminPurchaseIsPendingUntilPaymentIsConfirmedThenTheBalanceGrows()
    {
        using var f = Factory("SYS_ADMIN", out var store);
        var combo = store.Add("COMBO", ServicePackageTypes.Combo, 200, 3000, 2_500_000, 365);
        var c = Client(f);
        var tab = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/servicepackages/{A}"));
        Assert.Contains("data-sp-balance-transactions=\"0\"", tab);
        Assert.Contains("Gói dịch vụ", tab);

        var created = await Post(c, $"/customers/createservicepurchase/{A}", await Token(c, $"/customers/servicepackages/{A}"), new() { ["packageId"] = combo.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var purchase = Assert.Single(store.Purchases);
        Assert.Equal((ServicePurchaseStatus.Pending, ServicePurchaseVia.Admin, A, 0, 0), (purchase.Status, purchase.RequestedVia, purchase.CustomerId, purchase.InvoicesRemaining, purchase.TransactionsRemaining));
        Assert.Equal(ServiceBalance.Empty, await store.BalanceAsync(A));

        var page = $"/servicepackages/purchase/{purchase.Id}";
        var confirm = await Post(c, $"/servicepackages/confirmpaid/{purchase.Id}", await Token(c, page), new() { ["Confirm.PaidAmount"] = "2500000", ["Confirm.Method"] = "BANK_TRANSFER", ["Confirm.Reference"] = "FT2611" });
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        var paid = store.Purchases.Single();
        Assert.Equal((ServicePurchaseStatus.Paid, 200, 3000, "FT2611"), (paid.Status, paid.InvoicesRemaining, paid.TransactionsRemaining, paid.PaymentReference));
        Assert.Equal(Now.AddDays(365), paid.ExpiresAt);
        Assert.Equal(new ServiceBalance(3000, 200, Now.AddDays(365)), await store.BalanceAsync(A));
        Assert.Contains("data-sp-balance-invoices=\"200\"", WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/servicepackages/{A}")));

        // a paid purchase can be neither confirmed again nor cancelled
        await Post(c, $"/servicepackages/confirmpaid/{purchase.Id}", await Token(c, page), new() { ["Confirm.PaidAmount"] = "1", ["Confirm.Method"] = "CASH" });
        await Post(c, $"/servicepackages/cancelpurchase/{purchase.Id}", await Token(c, page), new() { ["reason"] = "x" });
        Assert.Equal((ServicePurchaseStatus.Paid, "FT2611"), (store.Purchases.Single().Status, store.Purchases.Single().PaymentReference));
        var detail = WebUtility.HtmlDecode(await c.GetStringAsync(page));
        Assert.Contains("data-sp-status=\"PAID\"", detail);
        Assert.DoesNotContain("data-sp-confirm", detail);
        Assert.Contains("data-sp-purchase=\"" + paid.PurchaseNo + "\"", WebUtility.HtmlDecode(await c.GetStringAsync("/servicepackages/purchases?status=PAID")));
    }

    [Fact]
    public async Task DeactivatedPackagesCannotBeBoughtButExistingPurchasesStay()
    {
        using var f = Factory("SYS_ADMIN", out var store);
        var p = store.Add("TRX", ServicePackageTypes.Transaction, 0, 1000, 300_000, null);
        var c = Client(f);
        var r = await Post(c, $"/servicepackages/setactive/{p.Id}", await Token(c, "/servicepackages"), new() { ["active"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.False(store.Packages.Single().IsActive);
        Assert.Equal(ServicePurchaseOutcome.PackageUnavailable, (await store.RequestAsync(A, p.Id, ServicePurchaseVia.Admin, Actor)).Outcome);
    }

    [Fact]
    public async Task OnlySystemAdminsManagePackages()
    {
        using var f = Factory("STAFF", out _);
        var c = Client(f);
        foreach (var path in new[] { "/servicepackages", "/servicepackages/purchases", "/servicepackages/create" })
            Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync(path)).StatusCode);
    }

    // ---- harness ------------------------------------------------------------------------------------------
    private static ServicePurchase Lot(string status, int transactions, int invoices, DateTime? expires) =>
        new(Guid.NewGuid(), "DV1", A, "KH-A", "A", Guid.NewGuid(), "P", "P", ServicePackageTypes.Combo, invoices, transactions, 1, null, status,
            ServicePurchaseVia.Customer, null, Now, null, null, null, null, null, expires, invoices, transactions, null, null, null);

    private static Dictionary<string, string> Package(string code, string type, string invoices, string transactions, string days = "30") => new()
    {
        ["Input.Code"] = code, ["Input.Name"] = "Gói " + code, ["Input.PackageType"] = type, ["Input.InvoiceQuota"] = invoices, ["Input.TransactionQuota"] = transactions,
        ["Input.Price"] = "990000", ["Input.ValidityDays"] = days, ["Input.SortOrder"] = "0", ["Input.IsActive"] = "true"
    };

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(string role, out MemoryServicePackageStore store)
    {
        var s = store = new MemoryServicePackageStore(new FixedClock(Now));
        return new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IServicePackageStore>();
                services.AddSingleton<IServicePackageStore>(s);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.RemoveAll<AdminServices.ICustomerAdminStore>();
                services.AddSingleton<AdminServices.ICustomerAdminStore>(new OneCustomer());
                services.AddSingleton(new Role(role));
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    private sealed record Role(string Name);

    private sealed class OneCustomer : AdminServices.ICustomerAdminStore
    {
        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid customerId) => Task.FromResult(customerId == A
            ? new AdminModels.CustomerEditVm { Id = A, Code = "KH-A", Name = "Công ty A", Status = "ACTIVE", IsActive = true } : null);
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new InvalidOperationException("not used");
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new InvalidOperationException("not used");
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Role role)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-sp"), new("display_name", "Safe SP"), new(ClaimTypes.Role, role.Name) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}

public sealed class FixedClock(DateTime utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
}

/// <summary>In-memory IServicePackageStore with the same state rules as SqlServicePackageStore (no audit).</summary>
public sealed class MemoryServicePackageStore(TimeProvider clock) : IServicePackageStore
{
    public List<ServicePackage> Packages { get; } = [];
    public List<ServicePurchase> Purchases { get; } = [];
    public Dictionary<Guid, (string Code, string Name)> Customers { get; } = new() { [Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")] = ("KH-A", "Công ty A"), [Guid.Parse("a0000000-0000-0000-0000-00000000000a")] = ("KH-A", "Công ty A"), [Guid.Parse("b0000000-0000-0000-0000-00000000000b")] = ("KH-B", "Công ty B") };
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public ServicePackage Add(string code, string type, int invoices, int transactions, decimal price, int? days, bool active = true)
    {
        var p = new ServicePackage(Guid.NewGuid(), code, "Gói " + code, type, invoices, transactions, price, days, null, active, Packages.Count, Now);
        Packages.Add(p);
        return p;
    }

    public ServicePurchase AddPurchase(Guid customerId, ServicePackage p, string status = ServicePurchaseStatus.Pending)
    {
        var x = new ServicePurchase(Guid.NewGuid(), ServicePackageRules.NewPurchaseNo(Now), customerId, Customers[customerId].Code, Customers[customerId].Name, p.Id, p.Code, p.Name,
            p.PackageType, p.InvoiceQuota, p.TransactionQuota, p.Price, p.ValidityDays, status, ServicePurchaseVia.Customer, null, Now, null, null, null, null, null, null, 0, 0, null, null, null);
        Purchases.Add(x);
        return x;
    }

    public Task<IReadOnlyList<ServicePackage>> PackagesAsync(bool activeOnly) => Task.FromResult<IReadOnlyList<ServicePackage>>(Packages.Where(p => !activeOnly || p.IsActive).ToList());
    public Task<ServicePackage?> PackageAsync(Guid id) => Task.FromResult(Packages.FirstOrDefault(p => p.Id == id));

    public Task<(ServicePackageSaveOutcome Outcome, Guid? Id)> SavePackageAsync(Guid? id, ServicePackageInput i, Guid actor)
    {
        if (Packages.Any(p => p.Code == i.Code && p.Id != id)) return Task.FromResult<(ServicePackageSaveOutcome, Guid?)>((ServicePackageSaveOutcome.CodeTaken, null));
        var index = Packages.FindIndex(p => p.Id == id);
        if (id is not null && index < 0) return Task.FromResult<(ServicePackageSaveOutcome, Guid?)>((ServicePackageSaveOutcome.NotFound, null));
        var p = new ServicePackage(id ?? Guid.NewGuid(), i.Code, i.Name, i.PackageType, i.InvoiceQuota, i.TransactionQuota, i.Price, i.ValidityDays, i.Description, i.IsActive, i.SortOrder, Now);
        if (index < 0) Packages.Add(p); else Packages[index] = p;
        return Task.FromResult<(ServicePackageSaveOutcome, Guid?)>((ServicePackageSaveOutcome.Saved, p.Id));
    }

    public Task<ServicePackageSaveOutcome> SetPackageActiveAsync(Guid id, bool active, Guid actor)
    {
        var index = Packages.FindIndex(p => p.Id == id);
        if (index < 0) return Task.FromResult(ServicePackageSaveOutcome.NotFound);
        Packages[index] = Packages[index] with { IsActive = active };
        return Task.FromResult(ServicePackageSaveOutcome.Saved);
    }

    public Task<IReadOnlyList<ServicePurchase>> PurchasesAsync(Guid? customerId, string? status, int take = 500) =>
        Task.FromResult<IReadOnlyList<ServicePurchase>>(Purchases.Where(p => (customerId is null || p.CustomerId == customerId) && (status is null || p.Status == status)).OrderByDescending(p => p.CreatedAt).Take(take).ToList());

    public Task<ServicePurchase?> PurchaseAsync(Guid id, Guid? customerId = null) => Task.FromResult(Purchases.FirstOrDefault(p => p.Id == id && (customerId is null || p.CustomerId == customerId)));

    public Task<ServiceBalance> BalanceAsync(Guid customerId) => Task.FromResult(ServicePackageRules.Balance(Purchases.Where(p => p.CustomerId == customerId), Now));

    public Task<(ServicePurchaseOutcome Outcome, ServicePurchase? Purchase)> RequestAsync(Guid customerId, Guid packageId, string via, Guid actor)
    {
        var p = Packages.FirstOrDefault(x => x.Id == packageId && x.IsActive);
        if (p is null || !Customers.ContainsKey(customerId)) return Task.FromResult<(ServicePurchaseOutcome, ServicePurchase?)>((ServicePurchaseOutcome.PackageUnavailable, null));
        var x = AddPurchase(customerId, p) with { RequestedVia = via };
        Purchases[^1] = x;
        return Task.FromResult<(ServicePurchaseOutcome, ServicePurchase?)>((ServicePurchaseOutcome.Done, x));
    }

    public Task<ServicePurchaseOutcome> ConfirmPaidAsync(Guid id, decimal paidAmount, string method, string? reference, Guid actor)
    {
        var index = Purchases.FindIndex(p => p.Id == id);
        if (index < 0) return Task.FromResult(ServicePurchaseOutcome.NotFound);
        var p = Purchases[index];
        if (p.Status != ServicePurchaseStatus.Pending) return Task.FromResult(ServicePurchaseOutcome.NotPending);
        Purchases[index] = p with { Status = ServicePurchaseStatus.Paid, PaidAmount = paidAmount, PaidAt = Now, PaymentMethod = method, PaymentReference = reference,
            ExpiresAt = ServicePackageRules.ExpiresAt(Now, p.ValidityDays), InvoicesRemaining = p.InvoiceQuota, TransactionsRemaining = p.TransactionQuota };
        return Task.FromResult(ServicePurchaseOutcome.Done);
    }

    public Task<ServicePurchaseOutcome> CancelAsync(Guid id, Guid? customerId, string? reason, Guid actor)
    {
        var index = Purchases.FindIndex(p => p.Id == id && (customerId is null || p.CustomerId == customerId));
        if (index < 0) return Task.FromResult(ServicePurchaseOutcome.NotFound);
        if (Purchases[index].Status != ServicePurchaseStatus.Pending) return Task.FromResult(ServicePurchaseOutcome.NotPending);
        Purchases[index] = Purchases[index] with { Status = ServicePurchaseStatus.Cancelled, CancelledAt = Now, CancelReason = reason };
        return Task.FromResult(ServicePurchaseOutcome.Done);
    }
}
