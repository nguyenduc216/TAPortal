extern alias admin;

using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TACustomerPortal.Web.Services;
using AdminModels = admin::TAPortal.Web.Models;
using AdminServices = admin::TAPortal.Web.Services;
using CustomerModels = TACustomerPortal.Web.Models;
using Shared = TAPortal.Shared;

namespace TACustomerPortal.Tests.Visual;

// Renders the real Razor views (layout included) with in-memory sample models to static HTML for headless-browser
// screenshots. Not part of the Safe suite: runs only when TA_VISUAL_OUT names an output folder. No SQL, no network.
// The sample rows are fixtures for the screenshots only; they are never written anywhere else.
public sealed class VisualSnapshotTests
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RenderAdminAndCustomerPages()
    {
        var output = Environment.GetEnvironmentVariable("TA_VISUAL_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var root = RepoRoot();

        using (var f = AdminFactory())
        {
            using var scope = f.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var adminUser = Principal(("display_name", "Nguyễn Văn A"), (ClaimTypes.Role, "SYS_ADMIN"));
            var wwwroot = Path.Combine(root, "src", "TAPortal.Web", "wwwroot");
            async Task Save(string name, string controller, string action, string view, object? model, ClaimsPrincipal? user = null, Action<ViewDataDictionary>? data = null)
                => await File.WriteAllTextAsync(Path.Combine(output, $"admin-{name}.html"), Localize(await Render(sp, controller, action, view, model, user ?? adminUser, data), wwwroot));

            var customers = SampleCustomers();
            var users = new List<AdminModels.PortalUser>
            {
                new(Actor, "nguyenvana", "Nguyễn Văn A", "a.nguyen@ta.example", "0901000001", "ACTIVE", true),
                new(Guid.NewGuid(), "tranthib", "Trần Thị B", "b.tran@ta.example", "0901000002", "ACTIVE", true),
                new(Guid.NewGuid(), "levanc", "Lê Văn C", "c.le@ta.example", null, "LOCKED", true),
                new(Guid.NewGuid(), "phamthid", "Phạm Thị D", "d.pham@ta.example", "0901000004", "ACTIVE", false),
            };
            var roles = new List<AdminModels.PortalRole>
            {
                new(Guid.NewGuid(), "SYS_ADMIN", "Quản trị hệ thống", "Toàn quyền hệ thống", true, true),
                new(Guid.NewGuid(), "EDITOR", "Biên tập viên", "Cập nhật hồ sơ khách hàng", false, true),
                new(Guid.NewGuid(), "VIEWER", "Người xem", null, false, false),
            };
            var roleMap = new Dictionary<Guid, List<string>> { [Actor] = ["Quản trị hệ thống"], [users[1].Id] = ["Biên tập viên"] };
            var snapshot = new AdminServices.DashboardSnapshot(new DateTimeOffset(2026, 10, 29, 8, 30, 0, TimeSpan.Zero),
                new AdminServices.UserKpis(3, 1, 1, 2), new AdminServices.CustomerKpis(9, 1), new AdminServices.MembershipKpis(6, 4, 1), 2, true,
                [new(new DateTime(2026, 10, 29, 8, 12, 0), "USER_ROLES_UPDATED", "Nguyễn Văn A"), new(new DateTime(2026, 10, 28, 16, 2, 0), "USER_UPDATED", "Nguyễn Văn A")]);

            await Save("dashboard", "Home", "Index", "Index", new AdminModels.DashboardPageVm(snapshot, false, 5, 20, TimeSpan.FromMinutes(5)));
            await Save("dashboard-error", "Home", "Index", "Index", new AdminModels.DashboardPageVm(null, false, 5, 20, TimeSpan.FromMinutes(5)));
            await Save("customers", "Customers", "Index", "Index", customers);
            await Save("customers-empty", "Customers", "Index", "Index", new List<AdminModels.CustomerRow>());
            await Save("customer-create", "Customers", "Create", "Edit", new AdminModels.CustomerEditVm());
            await Save("customer-edit", "Customers", "Edit", "Edit", new AdminModels.CustomerEditVm { Id = customers[0].Id, Code = customers[0].Code, Name = customers[0].Name, TaxCode = customers[0].TaxCode, Email = customers[0].Email, Phone = customers[0].Phone, Address = "12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh" });
            await Save("users", "Users", "Index", "Index", users, data: v => { v["Roles"] = roles; v["RoleMap"] = roleMap; });
            var account = new AdminServices.UserAccountSnapshot(users[1].Id, users[1].Username, users[1].DisplayName, users[1].Email, users[1].Phone, true, "ACTIVE", ["Biên tập viên"], new HashSet<Guid> { Actor }, 2);
            await Save("user-edit", "Users", "Edit", "Edit", new AdminModels.UserEditVm { Id = account.Id, Username = account.Username, DisplayName = account.DisplayName, Email = account.Email, Phone = account.Phone, IsActive = true, Stamp = account.Stamp }, data: v => v["Account"] = account);
            await Save("user-delete", "Users", "Delete", "Delete", account);
            var roleOptions = roles.Select(r => new AdminServices.RoleOption(r.Id, r.Code, r.Name, r.IsActive)).ToList();
            await Save("user-roles", "Users", "Roles", "Roles", new AdminServices.UserRoleSnapshot(users[1].Id, users[1].Username, users[1].DisplayName, true, "ACTIVE", roleOptions, new HashSet<Guid> { roles[1].Id }, new HashSet<Guid> { Actor }, [new("AUTH.USERS.DELETE", "Xóa người dùng", "DENY"), new("CRM.CUSTOMERS.UPDATE", "Sửa khách hàng", "ALLOW")]));
            var memberships = new List<Shared.MembershipAdminRow>
            {
                new(users[1].Id, customers[0].Id, "Trần Thị B", "tranthib", "b.tran@ta.example", customers[0].Name, "OWNER", true, "ACTIVE", new DateTime(2026, 10, 28, 9, 15, 0)),
                new(users[2].Id, customers[1].Id, "Lê Văn C", "levanc", null, customers[1].Name, "VIEWER", true, "ACTIVE", null),
                new(users[3].Id, customers[9].Id, "Phạm Thị D", "phamthid", "d.pham@ta.example", customers[9].Name, "ADMIN", false, "ACTIVE", new DateTime(2026, 9, 2, 14, 0, 0)),
            };
            await Save("cp-accounts", "CustomerPortalAccounts", "Index", "Index", memberships, data: v => { v["Customers"] = customers; v["Users"] = users; v["CustomerId"] = null; });
            await Save("roles", "Roles", "Index", "Index", roles);
            await Save("menus", "Menus", "Index", "Index", new List<AdminModels.PortalMenu> { new(Guid.NewGuid(), null, "CUSTOMERS", "Khách hàng", "building", "/Customers", 10, true, true), new(Guid.NewGuid(), null, "USERS", "Người dùng", "users", "/Users", 20, true, true) });
            await Save("profile", "Account", "Profile", "Profile", new AdminServices.AuthUserSnapshot(Actor, "nguyenvana", "Nguyễn Văn A", "a.nguyen@ta.example", "0901000001", "", ["SYS_ADMIN"], []));
            await Save("change-password", "Account", "ChangePassword", "ChangePassword", new AdminModels.ChangePasswordVm());
            await Save("access-denied", "Account", "AccessDenied", "AccessDenied", null, Principal(("display_name", "Người dùng hạn chế"), ("permission", "AUTH.ROLES.VIEW")));
            await Save("login", "Account", "Login", "Login", new AdminModels.LoginVm(), new ClaimsPrincipal(new ClaimsIdentity()));
        }

        using (var f = CustomerFactory())
        {
            using var scope = f.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var user = Principal(("display_name", "Trần Thị B"));
            var wwwroot = Path.Combine(root, "src", "TACustomerPortal.Web", "wwwroot");
            async Task Save(string name, string controller, string action, string view, object? model, ClaimsPrincipal? principal = null)
                => await File.WriteAllTextAsync(Path.Combine(output, $"customer-{name}.html"), Localize(await Render(sp, controller, action, view, model, principal ?? user, null), wwwroot));
            var company = new CustomerMembership(Guid.NewGuid(), "KH0004", "Công ty Cổ phần Ánh Dương", "0312345674", "hello@anhduong.example", "02838201004", "12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh", "OWNER");
            await Save("login", "Account", "Login", "Login", new CustomerModels.LoginVm(), new ClaimsPrincipal(new ClaimsIdentity()));
            await Save("select-customer", "Account", "SelectCustomer", "SelectCustomer", new List<CustomerMembership> { company, company with { CustomerId = Guid.NewGuid(), Code = "KH0008", Name = "Công ty Cổ phần Bình Minh", Role = "VIEWER" } });
            await Save("dashboard", "Dashboard", "Index", "Index", company);
            await Save("company", "Profile", "Company", "Company", company);
            await Save("personal", "Profile", "Personal", "Personal", new CustomerUser(Actor, "tranthib", "Trần Thị B", "b.tran@ta.example", ""));
            await Save("change-password", "Account", "ChangePassword", "ChangePassword", new CustomerModels.ChangePasswordVm());
        }
    }

    private static List<AdminModels.CustomerRow> SampleCustomers()
    {
        var names = new[] { ("KH0004", "Công ty Cổ phần Ánh Dương", "0312345674", "hello@anhduong.example", "02838201004"), ("KH0008", "Công ty Cổ phần Bình Minh", "0102345678", "contact@binhminh.example", "02438201008"), ("KH0006", "Công ty Cổ phần Hương Việt", "0102345676", "info@huongviet.example", "02438201006"), ("KH0002", "Công ty Cổ phần Mây Trắng", "0312345672", "contact@maytrang.example", "02838201002"), ("KH0010", "Công ty Cổ phần Sông Lam", "0402345680", "hello@songlam.example", "02363820110"), ("KH0005", "Công ty TNHH Hải Đăng", "0202345675", "contact@haidang.example", "02838201005"), ("KH0001", "Công ty TNHH Sao Biển", "0312345671", "hello@saobien.example", "02838201001"), ("KH0009", "Công ty TNHH Sen Hồng", "0402345679", "info@senhong.example", "02363820109"), ("KH0007", "Công ty TNHH Tre Việt", "0102345677", "hello@treviet.example", "02438201007"), ("KH0003", "Công ty Cổ phần Đại Phát", "0312345673", "contact@daiphat.example", "02838201003") };
        return names.Select((n, i) => new AdminModels.CustomerRow(Guid.NewGuid(), n.Item1, n.Item2, n.Item3, n.Item4, n.Item5, "ACTIVE", i != 9)).ToList();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)).Append(new Claim(ClaimTypes.NameIdentifier, Actor.ToString())).Append(new Claim(ClaimTypes.Name, "nguyenvana")), "Visual"));

    private static async Task<string> Render(IServiceProvider sp, string controller, string action, string view, object? model, ClaimsPrincipal user, Action<ViewDataDictionary>? data)
    {
        var http = new DefaultHttpContext { RequestServices = sp, User = user };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("localhost");
        http.Request.Path = $"/{controller}/{action}";
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "visual"));
        var route = new RouteData();
        route.Values["controller"] = controller;
        route.Values["action"] = action;
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action, RouteValues = new Dictionary<string, string?> { ["controller"] = controller, ["action"] = action } };
        var context = new ActionContext(http, route, descriptor);
        var found = sp.GetRequiredService<ICompositeViewEngine>().FindView(context, view, isMainPage: true);
        if (!found.Success) throw new InvalidOperationException($"View {controller}/{view} not found: {string.Join(", ", found.SearchedLocations)}");
        var viewData = new ViewDataDictionary(sp.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model };
        data?.Invoke(viewData);
        using var writer = new StringWriter();
        var viewContext = new ViewContext(context, found.View, viewData, new TempDataDictionary(http, sp.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
        await found.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    // Point root-relative asset URLs at the source wwwroot so the static file renders like the served page.
    private static string Localize(string html, string wwwroot)
    {
        var baseUri = new Uri(wwwroot + Path.DirectorySeparatorChar).AbsoluteUri;
        return Regex.Replace(html, "(href|src)=\"/(?!/)([^\"?]*)(\\?v=[^\"]*)?\"", m => $"{m.Groups[1].Value}=\"{baseUri}{m.Groups[2].Value}\"");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("TAPortal.sln not found above the test output folder.");
    }

    private static WebApplicationFactory<admin::Program> AdminFactory() =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: visual snapshots never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));

    private static WebApplicationFactory<Program> CustomerFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: visual snapshots never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));
}
