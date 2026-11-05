extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text;
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
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Admin Customer Workspace → "Hồ sơ doanh nghiệp", "Người đại diện", "Tài khoản hệ thống" (CUSTOMER-PROFILE phase 1).
// No SQL: an in-memory profile store and a reversible fake protector. Checks that secrets are write-only in forms,
// never rendered, only revealed through RevealSecret, and that every row stays scoped to the route customer.
public sealed class SafeCustomerProfileTests
{
    private const string TestScheme = "SafeCustomerProfile";
    private const string Secret = "Pa55-word-TEST!";
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");

    [Fact]
    public async Task WorkspaceHasTheThreeProfileTabs()
    {
        using var f = Factory(out _);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync($"/customers/profile/{A}"));
        foreach (var (tab, href) in new[] { ("profile", $"/Customers/Profile/{A}"), ("representatives", $"/Customers/Representatives/{A}"), ("system-accounts", $"/Customers/SystemAccounts/{A}") })
            Assert.Matches($"data-ta-tab=\"{tab}\"[^>]*href=\"{Regex.Escape(href)}\"|href=\"{Regex.Escape(href)}\"[^>]*data-ta-tab=\"{tab}\"", html);
        Assert.Matches("aria-current=\"page\"[^>]*data-ta-tab=\"profile\"", html);
        Assert.Contains("data-ta-profile-form", html);
        Assert.Contains($"<option value=\"{B}\">KH-B — Công ty B</option>", html);   // other customers as linked unit
        Assert.DoesNotContain($"<option value=\"{A}\"", html);                       // never itself
    }

    [Fact]
    public async Task ProfileIsSavedWithValidCodesAndRejectedOtherwise()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/profile/{A}");
        var bad = await Post(c, $"/customers/profile/{A}", token, new() { ["Profile.LegalForm"] = "LLC_9", ["Profile.ShortName"] = "A" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(store.Profiles);

        var ok = await Post(c, $"/customers/profile/{A}", token, new()
        {
            ["Profile.LegalForm"] = "LLC_2", ["Profile.ShortName"] = "Nhà hàng A", ["Profile.CharterCapital"] = "500000000", ["Profile.EstablishedDate"] = "2026-03-12",
            ["Profile.BusinessStatus"] = "OPERATING", ["Profile.ParentCustomerId"] = B.ToString(), ["Profile.CustomerId"] = B.ToString(),
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var saved = Assert.Single(store.Profiles).Value;
        Assert.Equal((A, "LLC_2", 500000000m, new DateOnly(2026, 3, 12), B), (saved.CustomerId, saved.LegalForm, saved.CharterCapital, saved.EstablishedDate, saved.ParentCustomerId));

        var self = await Post(c, $"/customers/profile/{A}", token, new() { ["Profile.ParentCustomerId"] = A.ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task RepresentativesAreScopedToTheCustomerAndListOnlyMaskedIdentityNumbers()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/representatives/{A}?add=true");
        var r = await Post(c, $"/customers/saverepresentative/{A}", token, new()
            { ["Edit.RoleCode"] = "DIRECTOR", ["Edit.FullName"] = "Nguyễn Văn A", ["Edit.IdentityNumber"] = "012345678901", ["Edit.Gender"] = "MALE", ["Edit.IsLegalRepresentative"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var row = Assert.Single(store.Representatives);
        Assert.Equal(A, row.Customer);

        var list = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/representatives/{A}"));
        Assert.Contains("Nguyễn Văn A", list);
        Assert.Contains("••••••••8901", list);
        Assert.DoesNotContain("012345678901", list);
        Assert.Contains("Đại diện pháp luật", list);
        Assert.DoesNotContain("Nguyễn Văn A", WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/representatives/{B}")));

        var invalid = await Post(c, $"/customers/saverepresentative/{A}", token, new() { ["Edit.RoleCode"] = "KING", ["Edit.FullName"] = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        // Another customer's row id through A's route is not found and changes nothing.
        var foreign = await Post(c, $"/customers/deleterepresentative/{B}", token, new() { ["representativeId"] = row.Row.Id.ToString(), ["stamp"] = row.Row.Stamp });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Single(store.Representatives);
    }

    [Fact]
    public async Task ARepresentativeNameCannotBreakOutOfTheDeleteConfirmation()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/representatives/{A}?add=true");
        const string name = "x');alert(1);//";
        await Post(c, $"/customers/saverepresentative/{A}", token, new() { ["Edit.RoleCode"] = "DIRECTOR", ["Edit.FullName"] = name });
        var html = await c.GetStringAsync($"/customers/representatives/{A}");
        Assert.DoesNotContain("onsubmit", html);
        Assert.Contains("data-ta-confirm=\"Xóa người đại diện x&#x27;);alert(1);//?\"", html);
        var views = Path.Combine(RepoRoot(), "src", "TAPortal.Web", "Views", "Customers");
        foreach (var file in Directory.GetFiles(views, "*.cshtml", SearchOption.AllDirectories))
            Assert.DoesNotMatch(@"onsubmit=""[^""]*@", File.ReadAllText(file));   // no Razor value inside inline JS
    }

    [Fact]
    public async Task AConflictingSaveReloadsTheStoredVersionInsteadOfKeepingStaleInput()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/representatives/{A}?add=true");
        await Post(c, $"/customers/saverepresentative/{A}", token, new() { ["Edit.RoleCode"] = "DIRECTOR", ["Edit.FullName"] = "Tên gốc" });
        var row = Assert.Single(store.Representatives).Row;
        var first = await Post(c, $"/customers/saverepresentative/{A}", token, new() { ["Edit.Id"] = row.Id.ToString(), ["Edit.Stamp"] = row.Stamp, ["Edit.RoleCode"] = "DIRECTOR", ["Edit.FullName"] = "Người thứ nhất" });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var stale = await Post(c, $"/customers/saverepresentative/{A}", token, new() { ["Edit.Id"] = row.Id.ToString(), ["Edit.Stamp"] = row.Stamp, ["Edit.RoleCode"] = "DIRECTOR", ["Edit.FullName"] = "Người thứ hai" });
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var page = WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync());
        Assert.Contains("thay đổi của bạn chưa được lưu", page);
        Assert.Contains("value=\"Người thứ nhất\"", page);
        Assert.DoesNotContain("value=\"Người thứ hai\"", page);
        Assert.Equal("Người thứ nhất", Assert.Single(store.Representatives).Row.FullName);
    }

    [Fact]
    public async Task PasswordsAreEncryptedWriteOnlyAndNeverRendered()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/systemaccounts/{A}?add=TAX_PORTAL");
        var r = await Post(c, $"/customers/savesystemaccount/{A}", token, new()
            { ["Edit.SystemType"] = "TAX_PORTAL", ["Edit.Username"] = "0101234567-QL", ["Edit.Password"] = Secret, ["Edit.Pin"] = "4321", ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var account = Assert.Single(store.Accounts);
        Assert.Equal(FakeProtector.Cipher(Secret), account.Password);   // only ciphertext reaches the store
        Assert.Equal(FakeProtector.Cipher("4321"), account.Pin);

        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/systemaccounts/{A}"));
        Assert.Contains("0101234567-QL", page);
        Assert.Contains("data-ta-secret-action=\"reveal\"", page);
        Assert.DoesNotContain(Secret, page);
        Assert.DoesNotContain("4321", page);
        var edit = WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/systemaccounts/{A}?edit={account.Row.Id}"));
        Assert.DoesNotContain(Secret, edit);
        Assert.Contains("Để trống = giữ nguyên", edit);

        // A blank password on edit keeps the stored one; "clear" removes it.
        var stamp = store.Accounts[0].Row.Stamp;
        await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.Id"] = account.Row.Id.ToString(), ["Edit.Stamp"] = stamp, ["Edit.SystemType"] = "TAX_PORTAL", ["Edit.Username"] = "u2", ["Edit.Password"] = "", ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        Assert.Equal("u2", store.Accounts[0].Row.Username);
        Assert.Equal(FakeProtector.Cipher(Secret), store.Accounts[0].Password);
        await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.Id"] = account.Row.Id.ToString(), ["Edit.Stamp"] = store.Accounts[0].Row.Stamp, ["Edit.SystemType"] = "TAX_PORTAL", ["Edit.ClearPassword"] = "true", ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        Assert.Null(store.Accounts[0].Password);
        Assert.Equal(FakeProtector.Cipher("4321"), store.Accounts[0].Pin);

        // A failed save never echoes the typed password back.
        var bad = await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.SystemType"] = "TAX_PORTAL", ["Edit.LoginUrl"] = "javascript:alert(1)", ["Edit.Password"] = Secret, ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.DoesNotContain(Secret, await bad.Content.ReadAsStringAsync());
        Assert.Single(store.Accounts);
    }

    [Fact]
    public async Task SecretsCannotBeSavedWithoutAConfiguredKey()
    {
        using var f = Factory(out var store, secretsEnabled: false);
        var c = Client(f);
        var token = await Token(c, $"/customers/systemaccounts/{A}?add=BANKING");
        Assert.Contains("data-ta-secrets-disabled", await c.GetStringAsync($"/customers/systemaccounts/{A}"));
        var r = await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.SystemType"] = "BANKING", ["Edit.Password"] = Secret, ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("CustomerSecrets__Key", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Empty(store.Accounts);
        // Login data without a secret can still be stored.
        Assert.Equal(HttpStatusCode.Redirect, (await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.SystemType"] = "BANKING", ["Edit.Username"] = "ketoan", ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" })).StatusCode);
    }

    [Fact]
    public async Task RevealReturnsThePlaintextOnlyForThisCustomerAndOnlyWithAnAuditRow()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/systemaccounts/{A}?add=TAX_PORTAL");
        await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.SystemType"] = "TAX_PORTAL", ["Edit.Password"] = Secret, ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        var id = store.Accounts[0].Row.Id.ToString();

        var ok = await Post(c, $"/customers/revealsecret/{A}", token, new() { ["accountId"] = id, ["field"] = "Password" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Secret, JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetString());
        Assert.Equal([(A, store.Accounts[0].Row.Id, AdminServices.SecretField.Password)], store.Reveals);

        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/customers/revealsecret/{B}", token, new() { ["accountId"] = id, ["field"] = "Password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, $"/customers/revealsecret/{A}", token, new() { ["accountId"] = id, ["field"] = "Pin" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, $"/customers/revealsecret/{A}", token, new() { ["accountId"] = id, ["field"] = "7" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/customers/revealsecret/{A}", new FormUrlEncodedContent(new Dictionary<string, string> { ["accountId"] = id, ["field"] = "Password" }))).StatusCode);   // no antiforgery token

        store.AuditDown = true;
        var refused = await Post(c, $"/customers/revealsecret/{A}", token, new() { ["accountId"] = id, ["field"] = "Password" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.DoesNotContain(Secret, await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeletingAnAccountNeedsTheCurrentStamp()
    {
        using var f = Factory(out var store);
        var c = Client(f);
        var token = await Token(c, $"/customers/systemaccounts/{A}?add=EMAIL");
        await Post(c, $"/customers/savesystemaccount/{A}", token, new() { ["Edit.SystemType"] = "EMAIL", ["Edit.Username"] = "a@a.example", ["Edit.Status"] = "ACTIVE", ["Edit.ManagedBy"] = "TA" });
        var row = store.Accounts[0].Row;
        await Post(c, $"/customers/deletesystemaccount/{A}", token, new() { ["accountId"] = row.Id.ToString(), ["stamp"] = "0000000000000000" });
        Assert.Single(store.Accounts);
        Assert.Contains("vừa được người khác thay đổi nên chưa xóa", WebUtility.HtmlDecode(await c.GetStringAsync($"/customers/systemaccounts/{A}")));
        await Post(c, $"/customers/deletesystemaccount/{A}", token, new() { ["accountId"] = row.Id.ToString(), ["stamp"] = row.Stamp });
        Assert.Empty(store.Accounts);
    }

    [Fact]
    public async Task MissingMigrationShowsAnExplicitUnavailableState()
    {
        using var f = Factory(out var store);
        store.Unavailable = true;
        var c = Client(f);
        foreach (var path in new[] { "profile", "representatives", "systemaccounts" })
        {
            var r = await c.GetAsync($"/customers/{path}/{A}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Contains("data-ta-profile-unavailable", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void MigrationAndScriptsFollowTheDatabaseRules()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "013-customer-profile-core.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "015-verify-customer-profile.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "016-rollback-customer-profile.sql"));
        foreach (var sql in new[] { migration, verify, rollback }) Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql);
        Assert.DoesNotMatch(@"(?i)\b(DROP|ALTER)\s+TABLE\b|\bDELETE\b|\bUPDATE\b|\bINSERT\b", migration);   // additive DDL only
        Assert.Contains("PasswordCipher varbinary(4000)", migration);
        Assert.Contains("THROW 51002, 'Rollback refused", rollback);
        Assert.DoesNotMatch(@"(?i)\bSELECT\b[^;]*\b(PasswordCipher|PinCipher|IdentityNumber)\b[^;]*\bFROM\s+dbo\.", verify);   // verify never prints secrets/PII
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.Contains("data-ta-secret-action", File.ReadAllText(Path.Combine(root, "src", "TAPortal.Web", "wwwroot", "js", "ta-portal.js")));
    }

    [Fact]
    public void AesGcmProtectorRoundTripsWithAFixedKeyAndRequiresOne()
    {
        static AdminServices.AesGcmCustomerSecretProtector With(string? key) =>
            new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [AdminServices.AesGcmCustomerSecretProtector.ConfigKey] = key }).Build());
        var key = AdminServices.AesGcmCustomerSecretProtector.GenerateKey();
        var on = With(key);
        Assert.True(on.Enabled);
        var account = Guid.NewGuid();
        var ctx = AdminServices.CustomerSecretContext.For(A, account, AdminServices.SecretField.Password);
        var cipher = on.Protect(Secret, ctx);
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(cipher));
        Assert.NotEqual(cipher, on.Protect(Secret, ctx));                  // random nonce per value
        Assert.Equal(Secret, With(key).Unprotect(cipher, ctx));            // same key after a restart reads old data
        Assert.Null(With(AdminServices.AesGcmCustomerSecretProtector.GenerateKey()).Unprotect(cipher, ctx));   // other key
        // Copied to another field, account or customer in the database, the value no longer decrypts.
        Assert.Null(on.Unprotect(cipher, AdminServices.CustomerSecretContext.For(A, account, AdminServices.SecretField.Pin)));
        Assert.Null(on.Unprotect(cipher, AdminServices.CustomerSecretContext.For(A, Guid.NewGuid(), AdminServices.SecretField.Password)));
        Assert.Null(on.Unprotect(cipher, AdminServices.CustomerSecretContext.For(B, account, AdminServices.SecretField.Password)));
        var tampered = (byte[])cipher.Clone();
        tampered[^1] ^= 1;
        Assert.Null(on.Unprotect(tampered, ctx));
        Assert.Null(on.Unprotect([1, 2, 3], ctx));
        foreach (var bad in new[] { null, "", "not-base64!", Convert.ToBase64String(new byte[16]) })
        {
            var off = With(bad);
            Assert.False(off.Enabled);
            Assert.Throws<InvalidOperationException>(() => off.Protect(Secret, ctx));
        }
        Assert.Equal("••••5678", AdminModels.CustomerProfileCodes.MaskIdentity("12345678"));
        Assert.Equal(("Sắp hết hạn", "ta-badge--warning"), AdminModels.CustomerProfileCodes.Expiry(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 4)));
        Assert.Equal("Đã hết hạn", AdminModels.CustomerProfileCodes.Expiry(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4))!.Value.Label);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("TAPortal.sln not found above the test output folder.");
    }

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static Task<HttpResponseMessage> Post(HttpClient c, string path, string token, Dictionary<string, string> data)
    {
        data["__RequestVerificationToken"] = token;
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(out MemoryProfiles store, bool secretsEnabled = true)
    {
        var s = store = new MemoryProfiles();
        return new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<AdminServices.ICustomerAdminStore>();
                services.AddSingleton<AdminServices.ICustomerAdminStore>(new Customers());
                services.RemoveAll<AdminServices.ICustomerProfileStore>();
                services.AddSingleton<AdminServices.ICustomerProfileStore>(s);
                services.RemoveAll<AdminServices.ICustomerSecretProtector>();
                services.AddSingleton<AdminServices.ICustomerSecretProtector>(new FakeProtector(secretsEnabled));
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));
    }

    private sealed class Customers : AdminServices.ICustomerAdminStore
    {
        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid customerId) => Task.FromResult(
            customerId == A ? new AdminModels.CustomerEditVm { Id = A, Code = "KH-A", Name = "Công ty A", Status = "ACTIVE", IsActive = true }
            : customerId == B ? new AdminModels.CustomerEditVm { Id = B, Code = "KH-B", Name = "Công ty B", Status = "ACTIVE", IsActive = true } : null);
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new NotSupportedException();
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actorId) => throw new NotSupportedException();
    }

    // Reversible, recognisable "ciphertext" so tests can prove only the protected form is stored.
    private sealed class FakeProtector(bool enabled) : AdminServices.ICustomerSecretProtector
    {
        public static byte[] Cipher(string plaintext) => Encoding.UTF8.GetBytes("enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)));
        public bool Enabled => enabled;
        public byte[] Protect(string plaintext, string context) => enabled ? Cipher(plaintext) : throw new InvalidOperationException();
        public string? Unprotect(byte[] ciphertext, string context)
        {
            var text = Encoding.UTF8.GetString(ciphertext);
            return text.StartsWith("enc:", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(text[4..])) : null;
        }
    }

    public sealed class MemoryProfiles : AdminServices.ICustomerProfileStore
    {
        private int version;
        public Dictionary<Guid, AdminModels.CustomerProfileVm> Profiles { get; } = [];
        public List<(Guid Customer, AdminModels.RepresentativeRow Row)> Representatives { get; } = [];
        public List<(Guid Customer, AdminModels.SystemAccountRow Row, byte[]? Password, byte[]? Pin)> Accounts { get; } = [];
        public List<(Guid, Guid, AdminServices.SecretField)> Reveals { get; } = [];
        public bool AuditDown { get; set; }
        public bool Unavailable { get; set; }

        private string NextStamp() => (++version).ToString("X16");
        private void Check() { if (Unavailable) throw SqlErrors.Create(); }

        public Task<AdminModels.CustomerProfileVm?> GetProfileAsync(Guid customerId) { Check(); return Task.FromResult(Profiles.GetValueOrDefault(customerId)); }
        public Task<IReadOnlyList<AdminModels.CustomerOption>> CustomerOptionsAsync(Guid exceptCustomerId)
        {
            Check();
            IReadOnlyList<AdminModels.CustomerOption> all = [new(A, "KH-A", "Công ty A"), new(B, "KH-B", "Công ty B")];
            return Task.FromResult<IReadOnlyList<AdminModels.CustomerOption>>(all.Where(o => o.Id != exceptCustomerId).ToList());
        }
        public Task<AdminServices.ProfileChangeOutcome> SaveProfileAsync(AdminModels.CustomerProfileVm vm, Guid actor)
        {
            Assert.Equal(Actor, actor);
            vm.Stamp = NextStamp();
            Profiles[vm.CustomerId] = vm;
            return Task.FromResult(AdminServices.ProfileChangeOutcome.Saved);
        }

        public Task<IReadOnlyList<AdminModels.RepresentativeRow>> ListRepresentativesAsync(Guid customerId)
        { Check(); return Task.FromResult<IReadOnlyList<AdminModels.RepresentativeRow>>(Representatives.Where(r => r.Customer == customerId).Select(r => r.Row).ToList()); }
        public Task<AdminServices.ProfileChangeOutcome> SaveRepresentativeAsync(Guid customerId, AdminModels.RepresentativeInput i, Guid actor)
        {
            var id = i.Id ?? Guid.NewGuid();
            if (i.Id is not null && Representatives.FirstOrDefault(r => r.Row.Id == id).Row?.Stamp != i.Stamp) return Task.FromResult(AdminServices.ProfileChangeOutcome.Conflict);
            Representatives.RemoveAll(r => r.Row.Id == id);
            Representatives.Add((customerId, new(id, i.RoleCode, i.IsLegalRepresentative, i.IsPrimaryContact, i.FullName, i.Gender, i.DateOfBirth, i.IdentityNumber, i.IdentityIssueDate, i.IdentityIssuePlace, i.Address, i.Email, i.Phone, i.Note, i.SortOrder, NextStamp())));
            return Task.FromResult(AdminServices.ProfileChangeOutcome.Saved);
        }
        public Task<AdminServices.ProfileChangeOutcome> DeleteRepresentativeAsync(Guid customerId, Guid id, string? stamp, Guid actor)
        {
            var i = Representatives.FindIndex(r => r.Customer == customerId && r.Row.Id == id);
            if (i < 0) return Task.FromResult(AdminServices.ProfileChangeOutcome.NotFound);
            if (Representatives[i].Row.Stamp != stamp) return Task.FromResult(AdminServices.ProfileChangeOutcome.Conflict);
            Representatives.RemoveAt(i);
            return Task.FromResult(AdminServices.ProfileChangeOutcome.Saved);
        }

        public Task<IReadOnlyList<AdminModels.SystemAccountRow>> ListSystemAccountsAsync(Guid customerId)
        { Check(); return Task.FromResult<IReadOnlyList<AdminModels.SystemAccountRow>>(Accounts.Where(a => a.Customer == customerId).Select(a => a.Row with { HasPassword = a.Password is not null, HasPin = a.Pin is not null }).ToList()); }
        public Task<AdminServices.ProfileChangeOutcome> SaveSystemAccountAsync(Guid customerId, Guid accountId, AdminModels.SystemAccountInput i, AdminServices.SecretChange password, AdminServices.SecretChange pin, Guid actor)
        {
            byte[]? pwd = null, p = null;
            if (i.Id is { } existing)
            {
                var at = Accounts.FindIndex(a => a.Customer == customerId && a.Row.Id == existing);
                if (at < 0) return Task.FromResult(AdminServices.ProfileChangeOutcome.NotFound);
                if (Accounts[at].Row.Stamp != i.Stamp) return Task.FromResult(AdminServices.ProfileChangeOutcome.Conflict);
                (pwd, p) = (Accounts[at].Password, Accounts[at].Pin);
                Accounts.RemoveAt(at);
            }
            if (password.Changes) pwd = password.Value;
            if (pin.Changes) p = pin.Value;
            var id = accountId;
            Accounts.Add((customerId, new(id, i.SystemType, i.ProviderName, i.SystemName, i.LoginUrl, i.Username, i.LoginPhone, i.StoreName, i.UnitCode, i.BankAccountNumber, i.CertificateType, i.CertificateSerial,
                i.ValidFrom, i.ExpiresAt, pwd is not null, p is not null, null, i.Status, i.ManagedBy, i.Note, i.SortOrder, NextStamp()), pwd, p));
            return Task.FromResult(AdminServices.ProfileChangeOutcome.Saved);
        }
        public Task<AdminServices.ProfileChangeOutcome> DeleteSystemAccountAsync(Guid customerId, Guid id, string? stamp, Guid actor)
        {
            var i = Accounts.FindIndex(a => a.Customer == customerId && a.Row.Id == id);
            if (i < 0) return Task.FromResult(AdminServices.ProfileChangeOutcome.NotFound);
            if (Accounts[i].Row.Stamp != stamp) return Task.FromResult(AdminServices.ProfileChangeOutcome.Conflict);
            Accounts.RemoveAt(i);
            return Task.FromResult(AdminServices.ProfileChangeOutcome.Saved);
        }
        public Task<AdminServices.SecretRevealResult> RevealSecretAsync(Guid customerId, Guid accountId, AdminServices.SecretField field, Guid actor, string? ip)
        {
            var a = Accounts.FirstOrDefault(x => x.Customer == customerId && x.Row.Id == accountId);
            var cipher = a.Row is null ? null : field == AdminServices.SecretField.Pin ? a.Pin : a.Password;
            if (cipher is null) return Task.FromResult(new AdminServices.SecretRevealResult(AdminServices.ProfileChangeOutcome.NotFound, null));
            if (AuditDown) return Task.FromResult(new AdminServices.SecretRevealResult(AdminServices.ProfileChangeOutcome.AuditUnavailable, null));
            Reveals.Add((customerId, accountId, field));
            return Task.FromResult(new AdminServices.SecretRevealResult(AdminServices.ProfileChangeOutcome.Saved, cipher));
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Actor.ToString()), new Claim(ClaimTypes.Name, "safe-profile"), new Claim("display_name", "Safe Profile"), new Claim(ClaimTypes.Role, "SYS_ADMIN") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}

// Builds a SqlException (error 208, "invalid object name") through the client library's internal factories, so
// "migration not applied" can be simulated without opening any connection.
internal static class SqlErrors
{
    public static Microsoft.Data.SqlClient.SqlException Create()
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var collection = Activator.CreateInstance(typeof(Microsoft.Data.SqlClient.SqlErrorCollection), nonPublic: true)!;
        var ctor = typeof(Microsoft.Data.SqlClient.SqlError).GetConstructors(Any).OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(int) ? 208 : p.ParameterType == typeof(string) ? "Invalid object name 'dbo.CustomerProfiles'." : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        var error = ctor.Invoke(args);
        typeof(Microsoft.Data.SqlClient.SqlErrorCollection).GetMethod("Add", Any)!.Invoke(collection, [error]);
        var create = typeof(Microsoft.Data.SqlClient.SqlException).GetMethods(Any).First(m => m.Name == "CreateException" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(string));
        return (Microsoft.Data.SqlClient.SqlException)create.Invoke(null, [collection, "16.0"])!;
    }
}
