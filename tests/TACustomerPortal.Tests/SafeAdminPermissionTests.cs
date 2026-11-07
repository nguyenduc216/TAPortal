extern alias admin;

using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdminControllers = admin::TAPortal.Web.Controllers;
using AdminSecurity = admin::TAPortal.Web.Security;
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// ADMIN-ROLE-PERMISSIONS: Admin functions are granted by catalogue permission (role → permissions), not by SYS_ADMIN only.
// No SQL host or connection: role and user-role stores are in-memory fakes applying the same policies as the SQL stores.
public sealed class SafeAdminPermissionTests
{
    private const string TestScheme = "SafePermissionTest";
    private static readonly Guid Actor = Guid.Parse("31111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("32222222-2222-2222-2222-222222222222");
    private static readonly Guid SysAdminRole = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid EditorRole = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly string[] Staff = ["CRM.CUSTOMERS.VIEW", "AUTH.ROLES.VIEW"];

    // ---- Catalogue ---------------------------------------------------------------------------------------
    [Fact]
    public void CatalogueMatchesMigrationAndVerifyScripts()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "028-admin-permission-catalogue.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "045-verify-admin-permission-catalogue.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "046-rollback-admin-permission-catalogue.sql"));
        // Every module except HR (Nhân sự), whose rows already exist in dbo.Permissions (HR core bootstrap, see SafeAdminEmployeeTests).
        var expected = AdminSecurity.AdminPermissions.MigrationCodes.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, Regex.Matches(migration, @"\('([A-Z.]+)',\s+N'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(verify, @"\('([A-Z.]+)'\)").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        const string marker = "A0000028-0000-4000-8000-000000000028";
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains(marker, sql));
        Assert.All(new[] { migration, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.DoesNotContain("CREATE OR ALTER", migration + verify + rollback);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        // The migration only inserts; the rollback deletes marker rows only and refuses while another role uses them.
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE)\b", migration);
        Assert.All(Regex.Matches(rollback, @"(?im)^DELETE FROM [^\r\n]+").Select(m => m.Value), d => Assert.Contains("CreatedBy = @Marker", d));
        Assert.Contains("r.Code <> 'SYS_ADMIN'", rollback);
        // Every catalogue code has a Vietnamese label and belongs to exactly one module.
        Assert.Equal(AdminSecurity.AdminPermissions.All.Count, AdminSecurity.AdminPermissions.Codes.Count);
        Assert.All(AdminSecurity.AdminPermissions.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void EveryAdminActionRequiresACataloguePermission()
    {
        var controllers = typeof(AdminControllers.HomeController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(AdminControllers.AccountController)).ToList();
        Assert.Contains(typeof(AdminControllers.RolesController), controllers);
        var problems = new List<string>();
        foreach (var type in controllers)
        {
            var classPolicies = type.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => a.Policy).ToList();
            if (classPolicies.Contains("SystemAdminOnly")) problems.Add($"{type.Name}: still SystemAdminOnly");
            var classPerms = classPolicies.Where(p => p?.StartsWith("PERM:") == true).ToList();
            // A class gated only by a view permission (or by sign-in) must declare a permission on every write action.
            var viewOnlyClass = classPerms.Count == 0 || classPerms.All(p => p!.EndsWith(".VIEW", StringComparison.Ordinal));
            foreach (var action in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null && !m.IsSpecialName))
            {
                if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null) continue;
                var actionPerms = action.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Where(p => p?.StartsWith("PERM:") == true).ToList();
                if (classPerms.Count + actionPerms.Count == 0) problems.Add($"{type.Name}.{action.Name}: no permission");
                if (action.GetCustomAttribute<HttpPostAttribute>() is not null && viewOnlyClass && actionPerms.Count == 0) problems.Add($"{type.Name}.{action.Name}: POST without its own permission");
                // AUTH.ROLES.MANAGE is not grantable: only a SYS_ADMIN passes it (operator 2026-11-07).
                foreach (var p in classPerms.Concat(actionPerms).Where(p => p != AdminSecurity.AdminPermissions.Policy.RolesManage))
                    if (!AdminSecurity.AdminPermissions.IsCatalogued(p!["PERM:".Length..])) problems.Add($"{type.Name}.{action.Name}: {p} not in the catalogue");
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void LandingPageFollowsSidebarOrder()
    {
        // "Việc của tôi" first for every employee (operator 2026-11-07), then the dashboard.
        Assert.Equal("Work", AdminSecurity.AdminNavigation.FirstAllowed(Principal("SYS_ADMIN"))!.Controller);
        Assert.Equal("Work", AdminSecurity.AdminNavigation.FirstAllowed(Principal("STAFF", "DASHBOARD.VIEW", "TASKS.VIEW"))!.Controller);
        Assert.Equal("Home", AdminSecurity.AdminNavigation.FirstAllowed(Principal("STAFF", "AUTH.USERS.VIEW", "DASHBOARD.VIEW"))!.Controller);
        Assert.Equal(("ServicePackages", "Purchases"), AdminSecurity.AdminNavigation.FirstAllowed(Principal("STAFF", "AUTH.USERS.VIEW", "PACKAGES.PURCHASES.VIEW")) is { } p ? (p.Controller, p.Action) : default);
        Assert.Null(AdminSecurity.AdminNavigation.FirstAllowed(Principal("STAFF", "HR.EMPLOYEES.VIEW")));
    }

    // ---- Enforcement for a role holding only some permissions -----------------------------------------------
    [Fact]
    public async Task SidebarShowsOnlyGrantedItemsAndRoleNames()
    {
        using var f = Factory("STAFF", new MemoryRoles(), new MemoryUserRoles(false), Staff);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/roles"));
        var nav = html[html.IndexOf("id=\"taNav\"", StringComparison.Ordinal)..html.IndexOf("</nav>", html.IndexOf("id=\"taNav\"", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("href=\"/Customers\"", nav);
        Assert.Contains("href=\"/Roles\"", nav);
        foreach (var hidden in new[] { "href=\"/\"", "/PaymentReconciliation", "/ServicePackages", "/TingeeSandbox", "/TingeeInvoiceSandbox", "/TingeeLogs", "/NotificationSettings", "/Users", "/Menus", "/Employees", "/Departments", "/Teams", "/JobTitles" })
            Assert.DoesNotContain(hidden, nav);
        Assert.Contains("Kế toán", html);                       // role name instead of "Người dùng TAPortal"
        Assert.DoesNotContain("Người dùng TAPortal", html);
        Assert.DoesNotContain("data-ta-role-create", html);     // no AUTH.ROLES.MANAGE: no create button
    }

    [Fact]
    public async Task SystemAdminSeesEveryItem()
    {
        using var f = Factory("SYS_ADMIN", new MemoryRoles(), new MemoryUserRoles(true));
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/roles"));
        foreach (var link in new[] { "href=\"/\"", "/Customers", "/PaymentReconciliation", "/ServicePackages", "/ServicePackages/Purchases", "/TingeeSandbox", "/TingeeInvoiceSandbox", "/TingeeLogs", "/NotificationSettings", "/Users", "/Roles", "/Menus", "/Employees", "/Departments", "/Teams", "/JobTitles" })
            Assert.Contains(link, html);
        Assert.Contains("data-ta-role-create", html);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/users")]
    [InlineData("GET", "/paymentreconciliation")]
    [InlineData("GET", "/servicepackages")]
    [InlineData("GET", "/servicepackages/purchases")]
    [InlineData("GET", "/tingeesandbox")]
    [InlineData("GET", "/tingeeinvoicesandbox")]
    [InlineData("GET", "/tingeelogs")]
    [InlineData("GET", "/notificationsettings")]
    [InlineData("GET", "/menus")]
    [InlineData("GET", "/customers/create")]
    [InlineData("GET", "/customers/accounts")]
    [InlineData("GET", "/customerportalaccounts")]
    [InlineData("GET", "/roles/create")]
    [InlineData("POST", "/customers/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/customers/create")]
    [InlineData("POST", "/customers/saveportalaccount/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/customers/createtingeemerchant/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/customers/createservicepurchase/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/servicepackages/create")]
    [InlineData("POST", "/paymentreconciliation/accept/1")]
    [InlineData("POST", "/roles/create")]
    [InlineData("POST", "/roles/edit/bbbbbbbb-0000-0000-0000-000000000002")]
    [InlineData("POST", "/roles/delete/bbbbbbbb-0000-0000-0000-000000000002")]
    public async Task UngrantedPagesAndWriteActionsAreForbidden(string method, string path)
    {
        var roles = new MemoryRoles();
        using var f = Factory("STAFF", roles, new MemoryUserRoles(false), Staff);
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? new FormUrlEncodedContent([new("Input.Name", "x")]) : null };
        var response = await Client(f).SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);   // authorization runs before CSRF and before any store
        Assert.Equal(0, roles.Writes);
    }

    [Fact]
    public async Task AccessDeniedLeadsToAPageTheUserMayOpen()
    {
        using var f = Factory("STAFF", new MemoryRoles(), new MemoryUserRoles(false), Staff);
        var html = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/account/accessdenied"));
        Assert.Contains("href=\"/Customers\"", html[html.IndexOf("ta-error-code", StringComparison.Ordinal)..]);
        Assert.Contains("Bạn không có quyền truy cập chức năng này.", html);
    }

    [Theory]
    [InlineData(new[] { "AUTH.ROLES.VIEW" }, "/Roles")]
    [InlineData(new[] { "AUTH.ROLES.VIEW", "DASHBOARD.VIEW" }, "/")]
    [InlineData(new[] { "HR.EMPLOYEES.VIEW" }, "/Account/Profile")]
    public async Task SignedInLoginPageRedirectsToFirstAllowedPage(string[] permissions, string expected)
    {
        using var f = Factory("STAFF", new MemoryRoles(), new MemoryUserRoles(false), permissions);
        var response = await Client(f).GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    // ---- Role management round trip -------------------------------------------------------------------------
    [Fact]
    public async Task RoleCreateEditAndPermissionTicksRoundTrip()
    {
        var roles = new MemoryRoles();
        // Operator 2026-11-07: only a SYS_ADMIN manages roles; a granted AUTH.ROLES.MANAGE is ignored.
        using (var staff = Factory("STAFF", roles, new MemoryUserRoles(false), "AUTH.ROLES.VIEW", "AUTH.ROLES.MANAGE"))
        {
            var s = Client(staff);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync("/roles/create")).StatusCode);
            Assert.DoesNotContain("data-ta-role-create", WebUtility.HtmlDecode(await s.GetStringAsync("/roles")));
        }
        Assert.DoesNotContain(AdminSecurity.AdminPermissions.All, p => p.Code == "AUTH.ROLES.MANAGE");   // not offered on the Roles page
        using var f = Factory("SYS_ADMIN", roles, new MemoryUserRoles(true), "AUTH.ROLES.VIEW");
        var c = Client(f);
        var token = await Token(c, "/roles/create");
        var created = await c.PostAsync("/roles/create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("Input.Code", "ke_toan_2"), new("Input.Name", "Kế toán 2"), new("Input.Description", "Đối soát"),
            new("Input.IsActive", "true"), new("Input.Permissions", "CRM.CUSTOMERS.VIEW"), new("Input.Permissions", "PAYMENTS.RECONCILIATION.VIEW")]));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var role = roles.ByCode("KE_TOAN_2")!;
        Assert.Equal(new[] { "CRM.CUSTOMERS.VIEW", "PAYMENTS.RECONCILIATION.VIEW" }, role.PermissionCodes.Order(StringComparer.Ordinal).ToArray());
        Assert.EndsWith($"/Roles/Edit/{role.Id}", created.Headers.Location!.OriginalString);

        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/roles/edit/{role.Id}"));
        Assert.Matches("value=\"CRM.CUSTOMERS.VIEW\"[^>]*checked=\"checked\"", page);
        Assert.DoesNotMatch("value=\"AUTH.USERS.VIEW\"[^>]*checked=\"checked\"", page);
        Assert.Contains("data-ta-perm-all", page);              // per-module "select all"
        Assert.DoesNotContain("HR.SPECIALIZATION.", page);      // only catalogue codes are shown
        Assert.DoesNotContain("HR.EMPLOYEE_DOCUMENT.", page);
        var stamp = Regex.Match(page, "name=\"Input.Stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value;
        token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

        var saved = await c.PostAsync($"/roles/edit/{role.Id}", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("Input.Stamp", stamp), new("Input.Code", "HACKED"), new("Input.Name", "Kế toán trưởng"),
            new("Input.IsActive", "false"), new("Input.Permissions", "AUTH.USERS.VIEW"), new("Input.Permissions", "PACKAGES.VIEW")]));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        role = roles.ByCode("KE_TOAN_2")!;                     // code is immutable
        Assert.Equal("Kế toán trưởng", role.Name);
        Assert.False(role.IsActive);
        Assert.Equal(new[] { "AUTH.USERS.VIEW", "PACKAGES.VIEW" }, role.PermissionCodes.Order(StringComparer.Ordinal).ToArray());

        // A stale stamp is a conflict; a code outside the catalogue is rejected; nothing changes.
        var writes = roles.Writes;
        token = await Token(c, $"/roles/edit/{role.Id}");
        var stale = await c.PostAsync($"/roles/edit/{role.Id}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Input.Stamp", stamp), new("Input.Name", "x"), new("Input.Permissions", "PACKAGES.VIEW")]));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        var foreign = await c.PostAsync($"/roles/edit/{role.Id}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Input.Stamp", role.Stamp), new("Input.Name", "x"), new("Input.Permissions", "HR.LEAVE.APPROVE")]));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(writes, roles.Writes);
    }

    [Theory]
    [InlineData("SYS_ADMIN")]
    [InlineData("1A")]
    [InlineData("WITH SPACE")]
    [InlineData("A")]
    [InlineData("THIS_CODE_IS_LONGER_THAN_THIRTY")]
    public async Task InvalidOrReservedRoleCodesAreRejected(string code)
    {
        var roles = new MemoryRoles();
        using var f = Factory("SYS_ADMIN", roles, new MemoryUserRoles(true));
        var c = Client(f);
        var token = await Token(c, "/roles/create");
        var response = await c.PostAsync("/roles/create", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Input.Code", code), new("Input.Name", "x")]));
        Assert.Equal(code == "1A" ? HttpStatusCode.Redirect : HttpStatusCode.BadRequest, response.StatusCode); // 1A is a valid code
        Assert.Equal(code == "1A" ? 1 : 0, roles.Writes);
    }

    [Fact]
    public async Task SystemAdminRoleIsReadOnly()
    {
        var roles = new MemoryRoles();
        using var f = Factory("SYS_ADMIN", roles, new MemoryUserRoles(true));
        var c = Client(f);
        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/roles/edit/{SysAdminRole}"));
        Assert.Contains("luôn có mọi quyền", page);
        Assert.DoesNotContain("type=\"submit\"", page[page.IndexOf("data-ta-role-form", StringComparison.Ordinal)..]);
        var inputs = Regex.Matches(page, "<input[^>]*name=\"Input.Permissions\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(AdminSecurity.AdminPermissions.All.Count, inputs.Count);
        Assert.All(inputs, i => Assert.Contains("disabled", i));
        Assert.All(inputs, i => Assert.Contains("checked", i));   // shown as holding everything
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var sys = roles.Get(SysAdminRole)!;
        var edit = await c.PostAsync($"/roles/edit/{SysAdminRole}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("Input.Stamp", sys.Stamp), new("Input.Name", "Renamed"), new("Input.IsActive", "false")]));
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Contains("không thể sửa", WebUtility.HtmlDecode(await edit.Content.ReadAsStringAsync()));
        var delete = await c.PostAsync($"/roles/delete/{SysAdminRole}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("stamp", sys.Stamp)]));
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(0, roles.Writes);
        Assert.True(roles.Get(SysAdminRole)!.IsActive);
        Assert.Equal(AdminServices.RoleAdminOutcome.SystemRole, AdminServices.RoleAdminPolicy.EvaluateUpdate(sys, new() { Name = "x", Stamp = sys.Stamp }, true, []));
        Assert.Equal(AdminServices.RoleAdminOutcome.SystemRole, AdminServices.RoleAdminPolicy.EvaluateDelete(sys, sys.Stamp, true));
    }

    [Fact]
    public void RoleInUseCannotBeDeletedAndUnknownActorIsForbidden()
    {
        var used = new AdminServices.RoleDetail(EditorRole, "EDITOR", "Biên tập", null, false, true, new HashSet<string>(), 2);
        Assert.Equal(AdminServices.RoleAdminOutcome.InUse, AdminServices.RoleAdminPolicy.EvaluateDelete(used, used.Stamp, true));
        Assert.Equal(AdminServices.RoleAdminOutcome.Forbidden, AdminServices.RoleAdminPolicy.EvaluateDelete(used, used.Stamp, false));
        Assert.Equal(AdminServices.RoleAdminOutcome.Saved, AdminServices.RoleAdminPolicy.EvaluateDelete(used with { UserCount = 0 }, used.Stamp, true));
    }

    // ---- SYS_ADMIN can only be granted by SYS_ADMIN -----------------------------------------------------------
    [Fact]
    public void RoleManagerWithoutSystemAdminCannotGrantRevokeOrTouchSystemAdmin()
    {
        AdminServices.UserRoleSnapshot Snap(params Guid[] assigned) => new(Target, "target", "Target", true, "ACTIVE",
            [new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", true), new(EditorRole, "EDITOR", "Biên tập", true)], assigned.ToHashSet(), new HashSet<Guid> { Guid.NewGuid() });
        var plain = Snap();
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, AdminServices.RoleChangePolicy.Evaluate(plain, Actor, [EditorRole], plain.Stamp).Outcome);          // no permission
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, AdminServices.RoleChangePolicy.Evaluate(plain, Actor, [EditorRole], plain.Stamp, true).Outcome);     // AUTH.ROLES.MANAGE
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, AdminServices.RoleChangePolicy.Evaluate(plain, Actor, [SysAdminRole], plain.Stamp, true).Outcome); // grant SYS_ADMIN
        var admin = Snap(SysAdminRole);
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, AdminServices.RoleChangePolicy.Evaluate(admin, Actor, [], admin.Stamp, true).Outcome);             // revoke SYS_ADMIN
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, AdminServices.RoleChangePolicy.Evaluate(admin, Actor, [SysAdminRole, EditorRole], admin.Stamp, true).Outcome); // edit a SYS_ADMIN
        var sysActor = admin with { SystemAdminIds = new HashSet<Guid> { Actor, Target } };
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, AdminServices.RoleChangePolicy.Evaluate(sysActor, Actor, [SysAdminRole, EditorRole], sysActor.Stamp).Outcome);

        // Accounts: AUTH.USERS.UPDATE/DELETE work on ordinary accounts, never on an account holding SYS_ADMIN (active or not).
        var member = new AdminServices.UserAccountSnapshot(Target, "m", "M", null, null, true, "ACTIVE", [], new HashSet<Guid> { Guid.NewGuid() }, 0);
        Assert.Equal(AdminServices.AccountChangeOutcome.Forbidden, AdminServices.AccountChangePolicy.Evaluate(member, Actor, member.Stamp, true, false));
        Assert.Equal(AdminServices.AccountChangeOutcome.Updated, AdminServices.AccountChangePolicy.Evaluate(member, Actor, member.Stamp, false, true, true));
        var lockedAdmin = member with { IsActive = false, HoldsSystemAdminRole = true };
        Assert.Equal(AdminServices.AccountChangeOutcome.Forbidden, AdminServices.AccountChangePolicy.Evaluate(lockedAdmin, Actor, lockedAdmin.Stamp, true, false, true));
    }

    [Fact]
    public async Task OnlyASystemAdminAssignsRolesOverHttp()
    {
        // Operator 2026-11-07: only a SYS_ADMIN assigns roles, even to an account holding a granted AUTH.ROLES.MANAGE.
        var store = new MemoryUserRoles(false);
        using (var staff = Factory("STAFF", new MemoryRoles(), store, "AUTH.USERS.VIEW", "AUTH.ROLES.MANAGE"))
        {
            var s = Client(staff);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.GetAsync($"/users/roles/{Target}")).StatusCode);
            // The Users list reads the database directly, so its role picker is checked on the view source.
            var users = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TAPortal.Web", "Views", "Users", "Index.cshtml"));
            Assert.Contains("@if (isSystemAdmin) {<div class=\"ta-span-2\"><label class=\"form-label\" for=\"newRole\">", users);
            Assert.Contains("Vai trò do quản trị hệ thống gán", users);
        }
        Assert.Empty(store.RolesOf(Target));
        var admin = new MemoryUserRoles(true);
        using var f = Factory("SYS_ADMIN", new MemoryRoles(), admin, "AUTH.USERS.VIEW");
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync($"/users/roles/{Target}"));
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var stamp = Regex.Match(html, "name=\"stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value;
        var ok = await c.PostAsync($"/users/roles/{Target}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("stamp", stamp), new("roleIds", EditorRole.ToString())]));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(new[] { EditorRole }, admin.RolesOf(Target).ToArray());
    }

    [Fact]
    public void SqlRechecksKeepSystemAdminProtected()
    {
        // Any role at account creation needs an active SYS_ADMIN creator; AUTH.ROLES.MANAGE re-checks are SYS_ADMIN only.
        Assert.DoesNotContain("r.Code<>'SYS_ADMIN' OR EXISTS", AdminServices.PortalDb.CreateUserRoleSql);
        Assert.Contains("ar.Code='SYS_ADMIN'", AdminServices.PortalDb.CreateUserRoleSql);
        Assert.Equal("AUTH.ROLES.MANAGE", TAPortal.Shared.ActorPermissionSql.SystemAdminOnlyPermission);
        Assert.Contains("up.Effect='DENY'", TAPortal.Shared.ActorPermissionSql.HasPermission);
        Assert.Contains("u.Status='ACTIVE'", TAPortal.Shared.ActorPermissionSql.HasPermission);
        Assert.Equal("CUSTOMERS.PORTAL.MANAGE", TAPortal.Shared.MembershipAdminStore.ManagePermission);
        Assert.Contains("Code<>'SYS_ADMIN'", AdminServices.RoleAdminSql.Update);
        Assert.Contains("Code<>'SYS_ADMIN'", AdminServices.RoleAdminSql.SoftDelete);
        Assert.DoesNotContain("DELETE FROM dbo.Roles", AdminServices.RoleAdminSql.SoftDelete);
    }

    // ---- Host and fakes -----------------------------------------------------------------------------------
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static ClaimsPrincipal Principal(string role, params string[] permissions) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }.Concat(permissions.Select(p => new Claim("permission", p))), "test"));

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryRoles roles, MemoryUserRoles userRoles, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.IRoleAdminStore>();
                s.AddSingleton<AdminServices.IRoleAdminStore>(roles);
                s.RemoveAll<AdminServices.IUserRoleStore>();
                s.AddSingleton<AdminServices.IUserRoleStore>(userRoles);
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    // In-memory role catalogue applying RoleAdminPolicy like SqlRoleAdminStore. The actor is treated as holding
    // AUTH.ROLES.MANAGE (the SQL store re-checks it in the transaction); HTTP policies are what is tested here.
    private sealed class MemoryRoles : AdminServices.IRoleAdminStore
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, AdminServices.RoleDetail> roles = new()
        {
            [SysAdminRole] = new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", "Toàn quyền", true, true, new HashSet<string>(), 1),
            [EditorRole] = new(EditorRole, "EDITOR", "Biên tập", null, false, true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CRM.CUSTOMERS.VIEW" }, 0)
        };
        public int Writes { get; private set; }
        private static readonly IReadOnlySet<string> Available = AdminSecurity.AdminPermissions.Codes;

        public AdminServices.RoleDetail? Get(Guid id) { lock (gate) return roles.GetValueOrDefault(id); }
        public AdminServices.RoleDetail? ByCode(string code) { lock (gate) return roles.Values.FirstOrDefault(r => r.Code == code); }

        public Task<IReadOnlyList<AdminServices.RoleListItem>> ListAsync()
        {
            lock (gate) return Task.FromResult<IReadOnlyList<AdminServices.RoleListItem>>(roles.Values.Select(r => new AdminServices.RoleListItem(r.Id, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, r.UserCount, r.PermissionCodes.Count)).ToList());
        }

        public Task<AdminServices.RoleDetail?> GetAsync(Guid id) => Task.FromResult(Get(id));
        public Task<IReadOnlySet<string>> AvailablePermissionsAsync() => Task.FromResult(Available);

        public Task<AdminServices.RoleAdminResult> CreateAsync(Guid actorId, AdminServices.RoleInput input, string? ip)
        {
            lock (gate)
            {
                var code = AdminServices.RoleAdminPolicy.NormalizeCode(input.Code);
                var permissions = AdminServices.RoleAdminPolicy.Permissions(input.Permissions, Available);
                var outcome = AdminServices.RoleAdminPolicy.EvaluateCreate(input, true, roles.Values.Any(r => r.Code == code), permissions);
                if (outcome != AdminServices.RoleAdminOutcome.Saved) return Task.FromResult(new AdminServices.RoleAdminResult(outcome));
                var id = Guid.NewGuid();
                roles[id] = new(id, code, input.Name.Trim(), input.Description?.Trim(), false, input.IsActive, permissions!, 0);
                Writes++;
                return Task.FromResult(new AdminServices.RoleAdminResult(outcome, id));
            }
        }

        public Task<AdminServices.RoleAdminResult> UpdateAsync(Guid actorId, Guid roleId, AdminServices.RoleInput input, string? ip)
        {
            lock (gate)
            {
                if (!roles.TryGetValue(roleId, out var current)) return Task.FromResult(new AdminServices.RoleAdminResult(AdminServices.RoleAdminOutcome.NotFound));
                var permissions = AdminServices.RoleAdminPolicy.Permissions(input.Permissions, Available);
                var outcome = AdminServices.RoleAdminPolicy.EvaluateUpdate(current, input, true, permissions);
                if (outcome != AdminServices.RoleAdminOutcome.Saved) return Task.FromResult(new AdminServices.RoleAdminResult(outcome, roleId));
                roles[roleId] = current with { Name = input.Name.Trim(), Description = input.Description?.Trim(), IsActive = input.IsActive, PermissionCodes = permissions! };
                Writes++;
                return Task.FromResult(new AdminServices.RoleAdminResult(outcome, roleId));
            }
        }

        public Task<AdminServices.RoleAdminResult> DeleteAsync(Guid actorId, Guid roleId, string? stamp, string? ip)
        {
            lock (gate)
            {
                if (!roles.TryGetValue(roleId, out var current)) return Task.FromResult(new AdminServices.RoleAdminResult(AdminServices.RoleAdminOutcome.NotFound));
                var outcome = AdminServices.RoleAdminPolicy.EvaluateDelete(current, stamp, true);
                if (outcome == AdminServices.RoleAdminOutcome.Saved) { roles.Remove(roleId); Writes++; }
                return Task.FromResult(new AdminServices.RoleAdminResult(outcome, roleId));
            }
        }
    }

    // User-role assignments applying RoleChangePolicy; actorIsSystemAdmin decides whether the actor holds SYS_ADMIN,
    // otherwise the actor is treated as holding AUTH.ROLES.MANAGE (re-checked in SQL by SqlUserRoleStore).
    private sealed class MemoryUserRoles(bool actorIsSystemAdmin) : AdminServices.IUserRoleStore
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, HashSet<Guid>> assignments = new() { [Actor] = actorIsSystemAdmin ? [SysAdminRole] : [EditorRole], [Target] = [], [Guid.Parse("39999999-9999-9999-9999-999999999999")] = [SysAdminRole] };
        private readonly List<AdminServices.RoleOption> catalog = [new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", true), new(EditorRole, "EDITOR", "Kế toán", true)];
        public IReadOnlySet<Guid> RolesOf(Guid user) { lock (gate) return assignments[user].ToHashSet(); }

        public Task<AdminServices.UserRoleSnapshot?> GetAsync(Guid userId) { lock (gate) return Task.FromResult(Snap(userId)); }

        public Task<AdminServices.RoleChangeResult> UpdateAsync(Guid actorId, Guid userId, IReadOnlyCollection<Guid> requested, string? stamp, string? ip)
        {
            lock (gate)
            {
                var snapshot = Snap(userId);
                if (snapshot is null) return Task.FromResult(new AdminServices.RoleChangeResult(AdminServices.RoleChangeOutcome.NotFound));
                var plan = AdminServices.RoleChangePolicy.Evaluate(snapshot, actorId, requested, stamp, actorHasPermission: true);
                if (plan.Outcome != AdminServices.RoleChangeOutcome.Updated) return Task.FromResult(new AdminServices.RoleChangeResult(plan.Outcome));
                assignments[userId] = plan.Final.ToHashSet();
                return Task.FromResult(new AdminServices.RoleChangeResult(AdminServices.RoleChangeOutcome.Updated, false, [], []));
            }
        }

        private AdminServices.UserRoleSnapshot? Snap(Guid userId) => assignments.TryGetValue(userId, out var assigned)
            ? new(userId, "user", "Display", true, "ACTIVE", catalog, assigned.ToHashSet(), assignments.Where(a => a.Value.Contains(SysAdminRole)).Select(a => a.Key).ToHashSet())
            : null;
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-permission-test"), new("display_name", "Nguyễn Văn B"), new(ClaimTypes.Role, principal.Role) };
            if (principal.Role != "SYS_ADMIN") claims.Add(new("role_name", "Kế toán"));
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
