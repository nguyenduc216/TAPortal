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
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Checkpoint 7 (AUD-15/16/17). No SQL: in-memory stores apply the production policies serially.
// SQL locking, the real soft-delete UPDATE and AuditLogs writes are NOT verified here (operator UAT).
public sealed class SafeAdminAccountTests
{
    private const string TestScheme = "SafeAccountTest";
    private static readonly Guid AdminA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdminB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Member = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- AUD-15: one login-eligibility rule -------------------------------------------------------------
    [Theory]
    [InlineData(true, "ACTIVE", false, true)]
    [InlineData(false, "ACTIVE", false, false)]
    [InlineData(true, "LOCKED", false, false)]
    [InlineData(true, "ACTIVE", true, false)]
    [InlineData(true, null, false, false)]
    public void LoginEligibilityRequiresActiveFlagActiveStatusAndNotDeleted(bool active, string? status, bool deleted, bool expected)
    {
        Assert.Equal(expected, AdminServices.AccountStatusPolicy.IsLoginEligible(active, status, deleted));
        Assert.Equal(expected, AdminServices.AccountStatusPolicy.Describe(active, status, deleted) == "Hoạt động");
    }

    [Fact]
    public void EveryAuthenticationQueryUsesTheSameEligibilityPredicate()
    {
        // Admin login/cookie validation lives in PortalDb (SQL); assert the SYS_ADMIN counters share the rule too.
        foreach (var sql in new[] { AdminServices.UserAccountSql.SystemAdmins, AdminServices.UserRoleSql.SystemAdmins })
            Assert.Contains("u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE'", sql);
    }

    [Fact]
    public void AccountPolicyBlocksSelfDeactivationSelfDeletionStaleStampAndNonAdmins()
    {
        var self = Snap(AdminA, admins: [AdminA, AdminB]);
        Assert.Equal(AdminServices.AccountChangeOutcome.SelfDeactivation, AdminServices.AccountChangePolicy.Evaluate(self, AdminA, self.Stamp, false, false));
        Assert.Equal(AdminServices.AccountChangeOutcome.SelfDeletion, AdminServices.AccountChangePolicy.Evaluate(self, AdminA, self.Stamp, false, true));
        Assert.Equal(AdminServices.AccountChangeOutcome.Conflict, AdminServices.AccountChangePolicy.Evaluate(self, AdminA, "STALE", true, false));
        var member = Snap(Member, admins: [AdminA]);
        Assert.Equal(AdminServices.AccountChangeOutcome.Forbidden, AdminServices.AccountChangePolicy.Evaluate(member, Member, member.Stamp, true, false));
        Assert.Equal(AdminServices.AccountChangeOutcome.Updated, AdminServices.AccountChangePolicy.Evaluate(member, AdminA, member.Stamp, false, true));
    }

    [Fact]
    public void LockedStatusIsNotReactivatedAndDoesNotTripAdminProtection()
    {
        // A user with Status=LOCKED stays ineligible even when IsActive=true is posted; editing other fields is allowed.
        var locked = Snap(Member, status: "LOCKED", admins: [AdminA]);
        Assert.False(AdminServices.AccountStatusPolicy.IsLoginEligible(true, locked.Status, false));
        Assert.Equal(AdminServices.AccountChangeOutcome.Updated, AdminServices.AccountChangePolicy.Evaluate(locked, AdminA, locked.Stamp, false, false));
    }

    [Fact]
    public async Task TwoAdminsDeactivatingOrDeletingEachOtherCannotLeaveNoAdmin()
    {
        var store = new MemoryAccounts();
        var pageA = (await store.GetAsync(AdminB))!;
        var pageB = (await store.GetAsync(AdminA))!;
        Assert.Equal(AdminServices.AccountChangeOutcome.Updated, (await store.UpdateAsync(AdminA, Vm(pageA, active: false), pageA.Stamp, null)).Outcome);
        Assert.Equal(AdminServices.AccountChangeOutcome.Forbidden, (await store.SoftDeleteAsync(AdminB, AdminA, pageB.Stamp, null)).Outcome);
        Assert.True(store.IsEligibleAdmin(AdminA));
    }

    // ---- AUD-16: tax code ------------------------------------------------------------------------------
    [Theory]
    [InlineData(" 0101 234567 ", "0101234567")]
    [InlineData("0101234567-001", "0101234567-001")]
    [InlineData("\t0012345678 ", "0012345678")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void TaxCodeNormalizationRemovesOnlyWhitespace(string? input, string? expected) =>
        Assert.Equal(expected, AdminServices.TaxCodePolicy.Normalize(input));

    [Theory]
    [InlineData("0101234567", true)]
    [InlineData("0101234567-001", true)]
    [InlineData("0101234567001", false)]
    [InlineData("010123456", false)]
    [InlineData("0101234567-01", false)]
    [InlineData("01012345AB", false)]
    public void TaxCodeFormatAcceptsTenDigitsOrBranchSuffix(string value, bool valid) =>
        Assert.Equal(valid, AdminServices.TaxCodePolicy.IsValid(value));

    [Fact]
    public void LegacyTaxCodeDoesNotBlockUnrelatedEditsButChangedValueIsValidated()
    {
        Assert.Equal(AdminServices.CustomerChangeOutcome.Saved, AdminServices.CustomerChangePolicy.ValidateTaxCode("MST-CU", "MST-CU"));
        Assert.Equal(AdminServices.CustomerChangeOutcome.InvalidTaxCode, AdminServices.CustomerChangePolicy.ValidateTaxCode("MST-CU", "12345"));
    }

    [Fact]
    public async Task CustomerHttpFlowRejectsDuplicateTaxCodeButNotSelf()
    {
        var customers = new MemoryCustomers();
        using var f = Factory("SYS_ADMIN", new MemoryAccounts(), customers);
        using var c = Client(f);
        var dup = await Post(c, "/customers/create", "/customers/create", new() { ["Code"] = "KH-NEW", ["Name"] = "Công ty mới", ["TaxCode"] = " 0101 234567 ", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("đã được sử dụng bởi khách hàng khác (mã KH KH-A)", WebUtility.HtmlDecode(await dup.Content.ReadAsStringAsync()));
        var invalid = await Post(c, "/customers/create", "/customers/create", new() { ["Code"] = "KH-NEW", ["Name"] = "Công ty mới", ["TaxCode"] = "12345", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("10 chữ số", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));
        Assert.Equal(1, customers.Count);
        var self = await Post(c, $"/customers/edit/{MemoryCustomers.A}", $"/customers/edit/{MemoryCustomers.A}", new() { ["Name"] = "Công ty A (đổi tên)", ["TaxCode"] = "0101234567", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, self.StatusCode);
        var branch = await Post(c, "/customers/create", "/customers/create", new() { ["Code"] = "KH-CN", ["Name"] = "Chi nhánh", ["TaxCode"] = "0101234567-001", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, branch.StatusCode);
        Assert.Equal(2, customers.Count);
    }

    [Fact]
    public async Task SerializedConcurrentCreatesOfSameTaxCodeKeepOneButDatabaseConstraintIsSeparate()
    {
        // Models the SERIALIZABLE application check only. Without migration 009 (UX_Customers_TaxCode) writers outside
        // the application are not prevented — reported as NOT_VERIFIED / PENDING_OPERATOR, not as database-safe.
        var store = new MemoryCustomers();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
            store.CreateAsync(new AdminModels.CustomerEditVm { Code = "KH-R" + i, Name = "Race", TaxCode = "0309999999", IsActive = true }, AdminA))));
        Assert.Equal(1, results.Count(r => r.Outcome == AdminServices.CustomerChangeOutcome.Saved));
        Assert.Equal(7, results.Count(r => r.Outcome == AdminServices.CustomerChangeOutcome.DuplicateTaxCode));
        Assert.False(store.HasDatabaseUniqueIndex);
    }

    // ---- AUD-15/17 HTTP flows --------------------------------------------------------------------------
    [Fact]
    public async Task EditShowsEffectiveStatusAndIgnoresOverpostedFields()
    {
        var accounts = new MemoryAccounts();
        accounts.SetStatus(Member, "LOCKED");
        using var f = Factory("SYS_ADMIN", accounts, new MemoryCustomers());
        using var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/users/edit/{Member}"));
        Assert.Contains("Bị khóa (trạng thái hệ thống: LOCKED)", page);
        var response = await Post(c, $"/users/edit/{Member}", $"/users/edit/{Member}", new()
        {
            ["DisplayName"] = "Tên mới", ["Email"] = "new@example.invalid", ["IsActive"] = "true", ["Stamp"] = Stamp(page),
            ["Username"] = "renamed", ["Status"] = "ACTIVE", ["PasswordHash"] = "PBKDF2$attacker", ["IsDeleted"] = "false"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = (await accounts.GetAsync(Member))!;
        Assert.Equal("Tên mới", after.DisplayName);
        Assert.Equal("member", after.Username);
        Assert.Equal("LOCKED", after.Status);                    // never reactivated through the form
        Assert.Equal(MemoryAccounts.OriginalPassword, accounts.PasswordOf(Member));
    }

    [Theory]
    [InlineData("stale", "vừa được thay đổi bởi thao tác khác")]
    [InlineData("self-deactivate", "Không thể tự vô hiệu hóa")]
    [InlineData("audit-required", "Không thể ghi nhật ký audit")]
    public async Task RejectedEditsShowErrorAndWriteNothing(string kind, string message)
    {
        var accounts = new MemoryAccounts { RequireAudit = kind == "audit-required" };
        using var f = Factory("SYS_ADMIN", accounts, new MemoryCustomers());
        using var c = Client(f);
        var target = kind == "self-deactivate" ? AdminA : Member;
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/users/edit/{target}"));
        var response = await Post(c, $"/users/edit/{target}", $"/users/edit/{target}", new()
        {
            ["DisplayName"] = "X", ["IsActive"] = kind == "self-deactivate" ? "false" : "true", ["Stamp"] = kind == "stale" ? "STALE" : Stamp(page)
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, accounts.Writes);
    }

    [Fact]
    public async Task SoftDeleteKeepsRowRolesMembershipsAndBlocksFurtherEdits()
    {
        var accounts = new MemoryAccounts();
        using var f = Factory("SYS_ADMIN", accounts, new MemoryCustomers());
        using var c = Client(f);
        var confirm = WebUtility.HtmlDecode(await c.GetStringAsync($"/users/delete/{Member}"));
        Assert.Contains("xóa mềm", confirm);
        var response = await Post(c, $"/users/delete/{Member}", $"/users/delete/{Member}", new() { ["stamp"] = Regex.Match(confirm, "name=\"stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(accounts.Exists(Member));                    // no hard delete
        Assert.True(accounts.IsDeleted(Member));
        Assert.False(accounts.IsActive(Member));
        Assert.Equal(["Biên tập"], accounts.RolesOf(Member));    // assignments kept for history, not cascaded
        Assert.Equal(2, accounts.MembershipsOf(Member));         // memberships untouched
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/users/edit/{Member}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/users/delete/{Member}")).StatusCode);
    }

    [Fact]
    public async Task SelfDeletionIsRejectedAndConfirmButtonDisabled()
    {
        var accounts = new MemoryAccounts();
        using var f = Factory("SYS_ADMIN", accounts, new MemoryCustomers());
        using var c = Client(f);
        var confirm = WebUtility.HtmlDecode(await c.GetStringAsync($"/users/delete/{AdminA}"));
        Assert.Contains("Không thể tự xóa", confirm);
        Assert.Matches("type=\"submit\" class=\"btn btn-danger ms-auto\" disabled", confirm);
        var response = await Post(c, $"/users/delete/{AdminA}", $"/users/delete/{AdminA}", new() { ["stamp"] = Regex.Match(confirm, "name=\"stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, accounts.Writes);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task NonSystemAdminCannotDeleteEvenWithDeletePermission(string method)
    {
        var accounts = new MemoryAccounts();
        using var f = Factory("OWNER", accounts, new MemoryCustomers(), "AUTH.USERS.DELETE", "AUTH.USERS.UPDATE");
        var request = new HttpRequestMessage(new HttpMethod(method), $"/users/delete/{Member}") { Content = method == "POST" ? new FormUrlEncodedContent([]) : null };
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(f).SendAsync(request)).StatusCode);
        Assert.Equal(0, accounts.Writes);
    }

    [Fact]
    public async Task DeleteWithoutCsrfIsRejected()
    {
        var accounts = new MemoryAccounts();
        using var f = Factory("SYS_ADMIN", accounts, new MemoryCustomers());
        Assert.Equal(HttpStatusCode.BadRequest, (await Client(f).PostAsync($"/users/delete/{Member}", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(0, accounts.Writes);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static AdminServices.UserAccountSnapshot Snap(Guid id, string status = "ACTIVE", bool active = true, Guid[]? admins = null) =>
        new(id, "user", "User", null, null, active, status, [], (admins ?? []).ToHashSet(), 0);

    private static AdminModels.UserEditVm Vm(AdminServices.UserAccountSnapshot s, bool active) =>
        new() { Id = s.Id, DisplayName = s.DisplayName, Email = s.Email, Phone = s.Phone, IsActive = active };

    private static string Stamp(string html) => Regex.Match(html, "name=\"Stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value;

    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, string tokenPath, Dictionary<string, string> data)
    {
        var html = await c.GetStringAsync(tokenPath);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return await c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryAccounts accounts, MemoryCustomers customers, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.IUserAccountStore>();
                s.AddSingleton<AdminServices.IUserAccountStore>(accounts);
                s.RemoveAll<AdminServices.ICustomerAdminStore>();
                s.AddSingleton<AdminServices.ICustomerAdminStore>(customers);
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed class MemoryAccounts : AdminServices.IUserAccountStore
    {
        public const string OriginalPassword = "PBKDF2$SHA256$150000$memory$only";
        private sealed class Row
        {
            public required Guid Id; public required string Username; public string DisplayName = "Display"; public string? Email; public string? Phone;
            public bool IsActive = true; public string Status = "ACTIVE"; public bool IsDeleted; public string Password = OriginalPassword;
            public List<string> Roles = []; public int Memberships;
        }
        private readonly object gate = new();
        private readonly Dictionary<Guid, Row> rows = new()
        {
            [AdminA] = new() { Id = AdminA, Username = "admin-a", Roles = ["SYS_ADMIN"] },
            [AdminB] = new() { Id = AdminB, Username = "admin-b", Roles = ["SYS_ADMIN"] },
            [Member] = new() { Id = Member, Username = "member", Roles = ["Biên tập"], Memberships = 2 }
        };
        public bool RequireAudit { get; init; }
        public int Writes { get; private set; }
        public void SetStatus(Guid id, string status) { lock (gate) rows[id].Status = status; }
        public bool Exists(Guid id) => rows.ContainsKey(id);
        public bool IsDeleted(Guid id) => rows[id].IsDeleted;
        public bool IsActive(Guid id) => rows[id].IsActive;
        public string PasswordOf(Guid id) => rows[id].Password;
        public List<string> RolesOf(Guid id) => rows[id].Roles;
        public int MembershipsOf(Guid id) => rows[id].Memberships;
        public bool IsEligibleAdmin(Guid id) { var r = rows[id]; return r.Roles.Contains("SYS_ADMIN") && AdminServices.AccountStatusPolicy.IsLoginEligible(r.IsActive, r.Status, r.IsDeleted); }

        public Task<AdminServices.UserAccountSnapshot?> GetAsync(Guid userId) { lock (gate) return Task.FromResult(Snap(userId)); }

        public Task<AdminServices.AccountChangeResult> UpdateAsync(Guid actorId, AdminModels.UserEditVm vm, string? stamp, string? ip) =>
            Change(actorId, vm.Id, stamp, vm.IsActive, false, r => { r.DisplayName = vm.DisplayName.Trim(); r.Email = vm.Email; r.Phone = vm.Phone; r.IsActive = vm.IsActive; });

        public Task<AdminServices.AccountChangeResult> SoftDeleteAsync(Guid actorId, Guid userId, string? stamp, string? ip) =>
            Change(actorId, userId, stamp, false, true, r => { r.IsDeleted = true; r.IsActive = false; });

        private Task<AdminServices.AccountChangeResult> Change(Guid actor, Guid id, string? stamp, bool remainsActive, bool deletion, Action<Row> apply)
        {
            lock (gate)
            {
                var before = Snap(id);
                if (before is null) return Task.FromResult(new AdminServices.AccountChangeResult(AdminServices.AccountChangeOutcome.NotFound));
                var outcome = AdminServices.AccountChangePolicy.Evaluate(before, actor, stamp, AdminServices.AccountStatusPolicy.IsLoginEligible(remainsActive, before.Status, deletion), deletion);
                if (outcome != AdminServices.AccountChangeOutcome.Updated) return Task.FromResult(new AdminServices.AccountChangeResult(outcome));
                if (RequireAudit) return Task.FromResult(new AdminServices.AccountChangeResult(AdminServices.AccountChangeOutcome.AuditUnavailable)); // no audit table: rolled back
                apply(rows[id]);
                Writes++;
                return Task.FromResult(new AdminServices.AccountChangeResult(AdminServices.AccountChangeOutcome.Updated, false));
            }
        }

        private AdminServices.UserAccountSnapshot? Snap(Guid id) =>
            rows.TryGetValue(id, out var r) && !r.IsDeleted
                ? new(r.Id, r.Username, r.DisplayName, r.Email, r.Phone, r.IsActive, r.Status, r.Roles.ToList(), rows.Values.Where(x => x.Roles.Contains("SYS_ADMIN") && AdminServices.AccountStatusPolicy.IsLoginEligible(x.IsActive, x.Status, x.IsDeleted)).Select(x => x.Id).ToHashSet(), r.Memberships)
                : null;
    }

    private sealed class MemoryCustomers : AdminServices.ICustomerAdminStore
    {
        public static readonly Guid A = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
        private readonly object gate = new();
        private readonly Dictionary<Guid, AdminModels.CustomerEditVm> rows = new() { [A] = new() { Id = A, Code = "KH-A", Name = "Công ty A", TaxCode = "0101234567", IsActive = true } };
        public bool HasDatabaseUniqueIndex => false;
        public int Count { get { lock (gate) return rows.Count; } }

        public Task<AdminModels.CustomerEditVm?> GetAsync(Guid id) { lock (gate) return Task.FromResult(rows.TryGetValue(id, out var r) ? Copy(r) : null); }
        public Task<AdminServices.CustomerChangeResult> CreateAsync(AdminModels.CustomerEditVm vm, Guid? actor) => Save(vm, true);
        public Task<AdminServices.CustomerChangeResult> UpdateAsync(AdminModels.CustomerEditVm vm, Guid? actor) => Save(vm, false);

        private Task<AdminServices.CustomerChangeResult> Save(AdminModels.CustomerEditVm vm, bool isNew)
        {
            lock (gate)
            {
                string? current = null;
                if (isNew)
                {
                    vm.Code = vm.Code.Trim().ToUpperInvariant();
                    if (rows.Values.Any(r => r.Code == vm.Code)) return Done(AdminServices.CustomerChangeOutcome.DuplicateCode, vm.Code);
                    vm.Id = Guid.NewGuid();
                }
                else if (!rows.TryGetValue(vm.Id, out var existing)) return Done(AdminServices.CustomerChangeOutcome.NotFound);
                else current = AdminServices.TaxCodePolicy.Normalize(existing.TaxCode);
                var tax = AdminServices.TaxCodePolicy.Normalize(vm.TaxCode);
                if (AdminServices.CustomerChangePolicy.ValidateTaxCode(current, tax) == AdminServices.CustomerChangeOutcome.InvalidTaxCode) return Done(AdminServices.CustomerChangeOutcome.InvalidTaxCode);
                var other = rows.Values.FirstOrDefault(r => r.Id != vm.Id && tax != null && AdminServices.TaxCodePolicy.Normalize(r.TaxCode) == tax);
                if (other != null) return Done(AdminServices.CustomerChangeOutcome.DuplicateTaxCode, other.Code);
                vm.TaxCode = tax;
                rows[vm.Id] = Copy(vm);
                return Done(AdminServices.CustomerChangeOutcome.Saved);
            }
        }

        private static Task<AdminServices.CustomerChangeResult> Done(AdminServices.CustomerChangeOutcome o, string? code = null) => Task.FromResult(new AdminServices.CustomerChangeResult(o, code));
        private static AdminModels.CustomerEditVm Copy(AdminModels.CustomerEditVm v) => new() { Id = v.Id, Code = v.Code, Name = v.Name, TaxCode = v.TaxCode, Email = v.Email, Phone = v.Phone, Address = v.Address, Status = v.Status, IsActive = v.IsActive };
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, AdminA.ToString()), new(ClaimTypes.Name, "safe-account-test"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
