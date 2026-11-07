using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;
using TAPortal.Shared;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// Operator 2026-11-07: a company manages its own staff accounts and decides per function Xem / Quản lý. OWNER and ADMIN
// manage everything, VIEWER views everything except "Tài khoản & phân quyền", MEMBER follows its permission string.
// No SQL: in-memory membership (role + permissions of the signed-in user) and an in-memory staff account store.
public sealed class SafeCustomerMembersTests
{
    private static readonly Guid Owner = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid Staff = Guid.Parse("71000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherCompany = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    // ---- rules -----------------------------------------------------------------------------------------
    [Theory]
    [InlineData("OWNER", null, CustomerModules.Members, CustomerAccessLevel.Manage)]
    [InlineData("ADMIN", null, CustomerModules.Orders, CustomerAccessLevel.Manage)]
    [InlineData("VIEWER", null, CustomerModules.Orders, CustomerAccessLevel.View)]
    [InlineData("VIEWER", null, CustomerModules.Members, CustomerAccessLevel.None)]
    [InlineData("MEMBER", null, CustomerModules.Orders, CustomerAccessLevel.None)]
    [InlineData("MEMBER", "ORDERS:V", CustomerModules.Orders, CustomerAccessLevel.View)]
    [InlineData("MEMBER", "orders:m;CONTACTS:V", CustomerModules.Orders, CustomerAccessLevel.Manage)]
    [InlineData("MEMBER", "ORDERS:M", CustomerModules.Contacts, CustomerAccessLevel.None)]
    [InlineData("MEMBER", "ORDERS:X;NOPE:M;;", CustomerModules.Orders, CustomerAccessLevel.None)]
    [InlineData("MEMBER", null, CustomerModules.Dashboard, CustomerAccessLevel.View)]
    [InlineData("SOMETHING", "ORDERS:M", CustomerModules.Orders, CustomerAccessLevel.None)]
    [InlineData("OWNER", null, "UNKNOWN", CustomerAccessLevel.None)]
    public void AccessFollowsTheRoleAndForMembersThePermissionString(string role, string? permissions, string module, CustomerAccessLevel expected) =>
        Assert.Equal(expected, CustomerAccess.Level(role, permissions, module));

    [Fact]
    public void PermissionsRoundTripInCanonicalOrderAndManageImpliesView()
    {
        var levels = CustomerAccess.FromForm(["CONTACTS"], ["ORDERS", "MEMBERS"]);
        var text = CustomerAccess.Format(levels);
        Assert.Equal("ORDERS:M;CONTACTS:V;MEMBERS:M", text);
        Assert.Equal(levels.Where(l => l.Value != CustomerAccessLevel.None), CustomerAccess.Parse(text).OrderBy(l => CustomerModules.All.ToList().FindIndex(m => m.Code == l.Key)));
        Assert.Null(CustomerAccess.Format(CustomerAccess.FromForm([], [])));
        Assert.True(text!.Length <= 500);
        Assert.True(CustomerAccess.Format(CustomerPresets.All[0].Levels)!.Length <= 500);
    }

    [Fact]
    public void PresetsMatchTheOperatorsDefinition()
    {
        var p = CustomerPresets.All.ToDictionary(x => x.Code, x => x.Levels);
        Assert.All(CustomerModules.All, m => Assert.Equal(CustomerAccessLevel.Manage, p["ADMIN"][m.Code]));
        Assert.Equal("ORDERS:V;CONTACTS:V;TRANSACTIONS:M;BANKS:M;PAYMENT_SETTINGS:V;EINVOICE:M;NOTIFICATIONS:V;PACKAGES:V;PROFILE:V;MEMBERS:V", CustomerAccess.Format(p["ACCOUNTANT"]));
        Assert.Equal("ORDERS:M;CONTACTS:M;TRANSACTIONS:V;PACKAGES:V", CustomerAccess.Format(p["SALES"]));
        Assert.Equal(CustomerAccessLevel.None, p["READONLY"][CustomerModules.Members]);
        Assert.Equal("Kế toán", CustomerAccess.Summary("MEMBER", CustomerAccess.Format(p["ACCOUNTANT"])));
        Assert.Equal("Thành viên", CustomerAccess.RoleLabel("MEMBER"));
    }

    [Fact]
    public void AStaffManagerCoversOnlyWhatItHolds()
    {
        var manager = Membership("MEMBER", "MEMBERS:M;ORDERS:V");
        Assert.True(CustomerAccess.Covers(manager, CustomerAccess.Parse("ORDERS:V")));
        Assert.False(CustomerAccess.Covers(manager, CustomerAccess.Parse("ORDERS:M")));
        Assert.False(CustomerAccess.Covers(manager, CustomerAccess.Levels("ADMIN", null)));
        Assert.True(CustomerAccess.Covers(Membership("ADMIN", null), CustomerAccess.Levels("ADMIN", null)));
    }

    [Fact]
    public void EveryCompanyControllerNamesItsModule()
    {
        var web = typeof(CustomerAccess).Assembly;
        var expected = new Dictionary<string, string>
        {
            ["BanksController"] = CustomerModules.Banks, ["ContactsController"] = CustomerModules.Contacts, ["EInvoiceController"] = CustomerModules.EInvoice,
            ["NotificationsController"] = CustomerModules.Notifications, ["OrdersController"] = CustomerModules.Orders,
            ["PaymentSettingsController"] = CustomerModules.PaymentSettings, ["ServicePackagesController"] = CustomerModules.Packages,
            ["TransactionsController"] = CustomerModules.Transactions, ["MembersController"] = CustomerModules.Members,
        };
        foreach (var type in web.GetTypes().Where(t => t.IsSubclassOf(typeof(TACustomerPortal.Web.Controllers.CustomerAreaController)) && !t.IsAbstract))
            Assert.Equal(expected[type.Name], ((CustomerModuleAttribute)Attribute.GetCustomAttribute(type, typeof(CustomerModuleAttribute))!).Module);
        var profile = web.GetType("TACustomerPortal.Web.Controllers.ProfileController")!;
        foreach (var action in new[] { "Company", "Logo" })
            Assert.All(profile.GetMethods().Where(m => m.Name == action), m => Assert.NotNull(m.GetCustomAttributes(typeof(CustomerModuleAttribute), false).SingleOrDefault()));
        Assert.Empty(profile.GetMethod("Personal")!.GetCustomAttributes(typeof(CustomerModuleAttribute), false));
    }

    // ---- enforcement -----------------------------------------------------------------------------------
    [Fact]
    public async Task AMemberWithoutOrdersGetsAFriendly403AndNoMenuEntry()
    {
        using var f = Factory("MEMBER", out _, out _, permissions: "CONTACTS:V");
        var c = Client(f);
        var denied = await c.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var page = WebUtility.HtmlDecode(await denied.Content.ReadAsStringAsync());
        Assert.Contains("Chưa được cấp quyền", page);
        Assert.Contains("Đơn hàng", page);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/orders/create")).StatusCode);
        var token = await Token(c, "/");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/orders/create", token, new())).StatusCode);

        var home = WebUtility.HtmlDecode(await c.GetStringAsync("/"));
        Assert.DoesNotContain("Đơn hàng & QR", home);
        Assert.DoesNotContain("Tài khoản ngân hàng", home);
        Assert.DoesNotContain("Tài khoản & phân quyền", home);
        Assert.Contains("href=\"/Contacts\"", home);                         // the one module it may view
        Assert.Contains("Thông tin cá nhân", home);                          // its own account stays reachable
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/contacts")).StatusCode);
        foreach (var path in new[] { "/banks", "/transactions", "/einvoice", "/notifications", "/servicepackages", "/paymentsettings", "/profile/company", "/members" })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/profile/personal")).StatusCode);
    }

    [Fact]
    public async Task ViewOnlyOrdersCannotPostOrderActions()
    {
        using var f = Factory("MEMBER", out var store, out _, permissions: "ORDERS:V");
        var order = new Order(Guid.NewGuid(), "DH261107-0001", null, null, null, null, 100_000, 0, OrderStatus.Awaiting, null, null, null, null, 1440, null, true, "NONE", null, DateTime.UtcNow,
            [OrderMath.Line(1, "Hàng", null, 1, 100_000, "0")]);
        store.Orders.Add((A, order));
        var c = Client(f);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/orders"));
        Assert.Contains("DH261107-0001", list);
        Assert.DoesNotContain("Tạo đơn hàng", list);
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{order.Id}"));
        Assert.DoesNotContain("Hủy đơn", details);
        var token = await Token(c, "/orders");
        foreach (var path in new[] { "/orders/create", $"/orders/cancel/{order.Id}", $"/orders/issueinvoice/{order.Id}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, path, token, new())).StatusCode);
        Assert.Equal(OrderStatus.Awaiting, store.Orders.Single(x => x.Order.Id == order.Id).Order.Status);

        using var manager = Factory("MEMBER", out _, out _, permissions: "ORDERS:M");
        Assert.Contains("Tạo đơn hàng", WebUtility.HtmlDecode(await Client(manager).GetStringAsync("/orders")));
    }

    [Fact]
    public async Task ViewersSeeEverythingButTheStaffAccountsPage()
    {
        using var f = Factory("VIEWER", out _, out _);
        var c = Client(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/members")).StatusCode);
        var notifications = WebUtility.HtmlDecode(await c.GetStringAsync("/notifications"));
        Assert.Contains("data-cp-readonly", notifications);
        Assert.DoesNotContain("Lưu nơi nhận", notifications);
        var token = await Token(c, "/notifications");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/notifications/recipients", token, new() { ["email"] = "a@b.vn" })).StatusCode);
        Assert.DoesNotContain("Tài khoản & phân quyền", WebUtility.HtmlDecode(await c.GetStringAsync("/")));
    }

    // ---- "Tài khoản & phân quyền" ------------------------------------------------------------------------
    [Fact]
    public async Task OwnersCreateEditDeactivateAndResetStaffAccounts()
    {
        var members = Seeded();
        using var f = Factory("OWNER", out _, out _, members: members);
        var c = Client(f);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/members"));
        Assert.Contains("Chủ Doanh Nghiệp", list);
        Assert.Contains("Thêm tài khoản", list);
        Assert.DoesNotContain("Người Công Ty Khác", list);                     // another company's account
        Assert.False(PartnerText.Mentions(Regex.Replace(list, "<script.*?</script>|<link[^>]*>", "", RegexOptions.Singleline)));

        var form = WebUtility.HtmlDecode(await c.GetStringAsync("/members/create"));
        Assert.Contains("data-cp-perm-matrix", form);
        foreach (var preset in new[] { "Quản trị", "Kế toán", "Bán hàng", "Chỉ xem" }) Assert.Contains(preset, form);
        var token = await Token(c, "/members/create");

        // Weak password and a taken username are refused with a clear message.
        var weak = await Send(c, "/members/create", token, New("ke.toan", "123"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("ít nhất", WebUtility.HtmlDecode(await weak.Content.ReadAsStringAsync()));
        var taken = await Send(c, "/members/create", token, New("chu.dn", "Ke-Toan!2026"));
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Contains("Tên đăng nhập này đã được dùng", WebUtility.HtmlDecode(await taken.Content.ReadAsStringAsync()));

        var created = await Send(c, "/members/create", token, [.. New("ke.toan", "Ke-Toan!2026"), new("View", "ORDERS"), new("Manage", "TRANSACTIONS")]);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var row = members.Rows.Single(r => r.Member.Username == "ke.toan");
        Assert.Equal((A, "MEMBER", "ORDERS:V;TRANSACTIONS:M", true), (row.Customer, row.Member.Role, row.Member.Permissions, row.Member.IsActive));
        Assert.True(new PasswordService().Verify("Ke-Toan!2026", members.Hashes[row.Member.UserId]));
        var id = row.Member.UserId;

        Assert.Equal(HttpStatusCode.Redirect, (await Send(c, $"/members/permissions/{id}", token, [new("View", "CONTACTS"), new("Manage", "ORDERS")])).StatusCode);
        Assert.Equal("ORDERS:M;CONTACTS:V", members.Find(id).Permissions);
        var edit = WebUtility.HtmlDecode(await c.GetStringAsync($"/members/edit/{id}"));
        Assert.Matches("name=\"Manage\" value=\"ORDERS\" checked", edit);

        Assert.Equal(HttpStatusCode.Redirect, (await Send(c, $"/members/setactive/{id}", token, [new("active", "false")])).StatusCode);
        Assert.False(members.Find(id).IsActive);
        Assert.Equal(HttpStatusCode.Redirect, (await Send(c, $"/members/setactive/{id}", token, [new("active", "true")])).StatusCode);
        Assert.True(members.Find(id).IsActive);

        Assert.Equal(HttpStatusCode.Redirect, (await Send(c, $"/members/password/{id}", token, [new("newPassword", "Moi-Mat-Khau!9"), new("confirmPassword", "Moi-Mat-Khau!9")])).StatusCode);
        Assert.True(new PasswordService().Verify("Moi-Mat-Khau!9", members.Hashes[id]));
        Assert.Equal(new[] { "CREATED", "PERMISSIONS", "INACTIVE", "ACTIVE", "PASSWORD" }, members.Audit.Where(a => a.Target == id).Select(a => a.Action));
    }

    [Fact]
    public async Task TheOwnerAndOneselfCannotBeChangedHere()
    {
        var members = Seeded();
        // The signed-in user is an ADMIN of company A.
        members.Rows.Add((A, Member(UserId, "toi", "ADMIN", null)));
        using var f = Factory("ADMIN", out _, out _, members: members);
        var c = Client(f);
        var token = await Token(c, "/members");
        foreach (var target in new[] { Owner, UserId })
        {
            await Send(c, $"/members/permissions/{target}", token, [new("View", "ORDERS")]);
            await Send(c, $"/members/setactive/{target}", token, [new("active", "false")]);
            await Send(c, $"/members/password/{target}", token, [new("newPassword", "Moi-Mat-Khau!9"), new("confirmPassword", "Moi-Mat-Khau!9")]);
            var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/members/edit/{target}"));
            Assert.Contains("data-cp-readonly", page);
            Assert.DoesNotContain("Lưu phân quyền", page);
        }
        Assert.Empty(members.Audit);
        Assert.True(members.Find(Owner).IsActive);
        Assert.Equal("ADMIN", members.Find(UserId).Role);
        await Send(c, $"/members/setactive/{Owner}", token, [new("active", "false")]);
        Assert.Contains("Tài khoản chủ sở hữu không thể thay đổi tại đây.", WebUtility.HtmlDecode(await c.GetStringAsync("/members")));   // flashed

        // Another company's account is not found from this company.
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/members/edit/{Guid.Parse("71000000-0000-0000-0000-0000000000cc")}")).StatusCode);
    }

    [Fact]
    public async Task MembersViewOnlySeesTheListAndAStaffManagerCannotGrantMoreThanItHolds()
    {
        var members = Seeded();
        using (var viewer = Factory("MEMBER", out _, out _, permissions: "MEMBERS:V", members: members))
        {
            var c = Client(viewer);
            var list = WebUtility.HtmlDecode(await c.GetStringAsync("/members"));
            Assert.Contains("Nhân Viên Bán Hàng", list);
            Assert.DoesNotContain("Thêm tài khoản", list);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/members/create")).StatusCode);
            var token = await Token(c, "/members");
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, $"/members/setactive/{Staff}", token, [new("active", "false")])).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, "/members/create", token, New("x.y", "Ke-Toan!2026"))).StatusCode);
        }
        using var manager = Factory("MEMBER", out _, out _, permissions: "MEMBERS:M;ORDERS:V;CONTACTS:M", members: members);
        var m = Client(manager);
        var t = await Token(m, "/members");
        // The sales account manages orders: more than this manager holds, so it cannot be changed by it.
        await Send(m, $"/members/permissions/{Staff}", t, [new("View", "ORDERS")]);
        Assert.Equal("ORDERS:M;CONTACTS:M", members.Find(Staff).Permissions);
        Assert.Contains("data-cp-readonly", WebUtility.HtmlDecode(await m.GetStringAsync($"/members/edit/{Staff}")));
        var refused = await Send(m, "/members/create", t, [.. New("ban.hang2", "Ke-Toan!2026"), new("Manage", "BANKS")]);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("cao hơn quyền của chính mình", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(members.Rows, r => r.Member.Username == "ban.hang2");
        Assert.Equal(HttpStatusCode.Redirect, (await Send(m, "/members/create", t, [.. New("ban.hang3", "Ke-Toan!2026"), new("View", "ORDERS"), new("Manage", "CONTACTS")])).StatusCode);
        Assert.Equal("ORDERS:V;CONTACTS:M", members.Rows.Single(r => r.Member.Username == "ban.hang3").Member.Permissions);
    }

    // ---- helpers -----------------------------------------------------------------------------------------
    private static CustomerMembership Membership(string role, string? permissions) => new(A, "KH-A", "Công ty A", null, null, null, null, role, permissions);

    private static CompanyMember Member(Guid id, string username, string role, string? permissions, string? name = null) =>
        new(id, username, name ?? username, null, null, role, permissions, true, DateTime.UtcNow);

    private static MemoryMemberStore Seeded()
    {
        var store = new MemoryMemberStore();
        store.Rows.Add((A, Member(Owner, "chu.dn", "OWNER", null, "Chủ Doanh Nghiệp")));
        store.Rows.Add((A, Member(Staff, "ban.hang", "MEMBER", "ORDERS:M;CONTACTS:M", "Nhân Viên Bán Hàng")));
        store.Rows.Add((OtherCompany, Member(Guid.Parse("71000000-0000-0000-0000-0000000000cc"), "khac", "OWNER", null, "Người Công Ty Khác")));
        return store;
    }

    private static List<KeyValuePair<string, string>> New(string username, string password) =>
        [new("DisplayName", "Nhân viên " + username), new("Username", username), new("Email", username + "@example.invalid"), new("Phone", "0912345678"),
         new("Password", password), new("ConfirmPassword", password)];

    private static Task<HttpResponseMessage> Send(HttpClient c, string path, string token, List<KeyValuePair<string, string>> data)
    {
        data.Add(new("__RequestVerificationToken", token));
        return c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    /// <summary>In-memory staff accounts: same contract as SqlCustomerMemberStore (company-scoped, OWNER refused).</summary>
    public sealed class MemoryMemberStore : ICustomerMemberStore
    {
        public List<(Guid Customer, CompanyMember Member)> Rows { get; } = [];
        public Dictionary<Guid, string> Hashes { get; } = [];
        public List<(string Action, Guid Target)> Audit { get; } = [];

        public CompanyMember Find(Guid id) => Rows.Single(r => r.Customer == A && r.Member.UserId == id).Member;

        public Task<IReadOnlyList<CompanyMember>> ListAsync(Guid customerId) =>
            Task.FromResult<IReadOnlyList<CompanyMember>>(Rows.Where(r => r.Customer == customerId).Select(r => r.Member).ToList());

        public Task<CompanyMember?> GetAsync(Guid customerId, Guid userId) =>
            Task.FromResult(Rows.Where(r => r.Customer == customerId && r.Member.UserId == userId).Select(r => r.Member).SingleOrDefault());

        public Task<(MemberOutcome Outcome, Guid UserId)> CreateAsync(Guid customerId, NewCompanyMember x, Guid actor, string? ip)
        {
            if (Rows.Any(r => string.Equals(r.Member.Username, x.Username, StringComparison.OrdinalIgnoreCase))) return Task.FromResult((MemberOutcome.UsernameTaken, Guid.Empty));
            var id = Guid.NewGuid();
            Rows.Add((customerId, new(id, x.Username, x.DisplayName, x.Email, x.Phone, "MEMBER", x.Permissions, true, DateTime.UtcNow)));
            Hashes[id] = x.PasswordHash;
            Audit.Add(("CREATED", id));
            return Task.FromResult((MemberOutcome.Done, id));
        }

        public Task<MemberOutcome> SetPermissionsAsync(Guid customerId, Guid userId, string? permissions, Guid actor, string? ip) =>
            Change(customerId, userId, m => m with { Role = "MEMBER", Permissions = permissions }, "PERMISSIONS");

        public Task<MemberOutcome> SetActiveAsync(Guid customerId, Guid userId, bool active, Guid actor, string? ip) =>
            Change(customerId, userId, m => m with { IsActive = active }, active ? "ACTIVE" : "INACTIVE");

        public Task<MemberOutcome> SetPasswordAsync(Guid customerId, Guid userId, string passwordHash, Guid actor, string? ip)
        {
            var outcome = Change(customerId, userId, m => m, "PASSWORD");
            if (outcome.Result == MemberOutcome.Done) Hashes[userId] = passwordHash;
            return outcome;
        }

        private Task<MemberOutcome> Change(Guid customerId, Guid userId, Func<CompanyMember, CompanyMember> change, string action)
        {
            var i = Rows.FindIndex(r => r.Customer == customerId && r.Member.UserId == userId);
            if (i < 0) return Task.FromResult(MemberOutcome.NotFound);
            if (Rows[i].Member.Role == "OWNER") return Task.FromResult(MemberOutcome.Protected);
            Rows[i] = (customerId, change(Rows[i].Member));
            Audit.Add((action, userId));
            return Task.FromResult(MemberOutcome.Done);
        }
    }
}
