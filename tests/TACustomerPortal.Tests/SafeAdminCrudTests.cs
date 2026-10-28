extern alias admin;

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AdminControllers = admin::TAPortal.Web.Controllers;
using AdminModels = admin::TAPortal.Web.Models;

namespace TACustomerPortal.Tests;

// No SQL host, connection, schema, seed or rollback. Every HTTP case stops at authorization/CSRF before PortalDb is used.
public sealed class SafeAdminCrudTests
{
    private const string TestScheme = "SafeTest";
    private static readonly Type[] AdminControllerTypes =
    {
        typeof(AdminControllers.UsersController), typeof(AdminControllers.CustomersController),
        typeof(AdminControllers.CustomerPortalAccountsController), typeof(AdminControllers.HomeController),
        typeof(AdminControllers.RolesController), typeof(AdminControllers.MenusController), typeof(AdminControllers.AccountController)
    };

    private static WebApplicationFactory<admin::Program> Factory(string? role, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                if (role is null) return;
                s.AddSingleton(new TestIdentity(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Theory]
    [InlineData("/users/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("/customers/create")]
    [InlineData("/customers/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("/customerportalaccounts")]
    public async Task AnonymousAdminCrudRoutesRedirectToLogin(string path)
    {
        using var f = Factory(null);
        var response = await Client(f).GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("GET", "/users/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/users/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("GET", "/customers/create")]
    [InlineData("POST", "/customers/create")]
    [InlineData("GET", "/customers/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("POST", "/customers/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("GET", "/customerportalaccounts")]
    [InlineData("POST", "/customerportalaccounts/save")]
    public async Task GrantedPermissionsWithoutSystemAdminAreForbidden(string method, string path)
    {
        // Customer Portal roles and fine-grained permissions must never unlock SYS_ADMIN-only management.
        using var f = Factory("OWNER", "AUTH.USERS.UPDATE", "CRM.CUSTOMERS.CREATE", "CRM.CUSTOMERS.UPDATE", "CUSTOMERS.PORTAL.MANAGE");
        var response = await Client(f).SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? new FormUrlEncodedContent(new Dictionary<string, string>()) : null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/users/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("/users/create")]
    [InlineData("/customers/create")]
    [InlineData("/customers/edit/6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("/customerportalaccounts/save")]
    [InlineData("/customerportalaccounts/setactive")]
    public async Task SystemAdminPostsWithoutCsrfTokenAreRejectedBeforeData(string path)
    {
        using var f = Factory("SYS_ADMIN");
        var response = await Client(f).PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["DisplayName"] = "x", ["IsActive"] = "false" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void EveryAdminPostActionValidatesAntiforgeryAndManagementControllersRequireSystemAdmin()
    {
        foreach (var type in AdminControllerTypes)
            foreach (var action in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null))
                Assert.True(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null, $"{type.Name}.{action.Name} lacks CSRF validation");
        foreach (var type in new[] { typeof(AdminControllers.UsersController), typeof(AdminControllers.CustomersController), typeof(AdminControllers.CustomerPortalAccountsController) })
            Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(true), a => a.Policy == "SystemAdminOnly");
    }

    [Theory]
    [InlineData(typeof(AdminControllers.UsersController), "Edit", "PERM:AUTH.USERS.UPDATE")]
    [InlineData(typeof(AdminControllers.CustomersController), "Create", "PERM:CRM.CUSTOMERS.CREATE")]
    [InlineData(typeof(AdminControllers.CustomersController), "Edit", "PERM:CRM.CUSTOMERS.UPDATE")]
    public void CrudActionsDeclareFineGrainedPermission(Type controller, string action, string policy)
    {
        var methods = controller.GetMethods().Where(m => m.Name == action).ToList();
        Assert.Equal(2, methods.Count); // GET + POST
        Assert.All(methods, m => Assert.Contains(m.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == policy));
    }

    [Fact]
    public void EditModelsCannotOverpostIdentityPasswordOrRoles()
    {
        var userProps = typeof(AdminModels.UserEditVm).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(userProps, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase) || n.Contains("Role", StringComparison.OrdinalIgnoreCase));
        foreach (var name in new[] { "Username", "Status" })
            Assert.NotNull(typeof(AdminModels.UserEditVm).GetProperty(name)!.GetCustomAttribute<ValidateNeverAttribute>());
        Assert.NotNull(typeof(AdminModels.CustomerEditVm).GetProperty("Status")!.GetCustomAttribute<ValidateNeverAttribute>());
    }

    [Theory]
    [InlineData("", "a@b.vn", false)]
    [InlineData("Nguyễn Văn A", "not-an-email", false)]
    [InlineData("Nguyễn Văn A", null, true)]
    [InlineData("Nguyễn Văn A", "a@b.vn", true)]
    public void UserEditValidation(string name, string? email, bool valid)
    {
        var vm = new AdminModels.UserEditVm { DisplayName = name, Email = email };
        Assert.Equal(valid, Validator.TryValidateObject(vm, new ValidationContext(vm), new List<ValidationResult>(), true));
    }

    [Theory]
    [InlineData("KH-001", "Công ty A", true)]
    [InlineData("KH 001", "Công ty A", false)]
    [InlineData("KH<script>", "Công ty A", false)]
    [InlineData("KH001", "", false)]
    public void CustomerValidation(string code, string name, bool valid)
    {
        var vm = new AdminModels.CustomerEditVm { Code = code, Name = name };
        Assert.Equal(valid, Validator.TryValidateObject(vm, new ValidationContext(vm), new List<ValidationResult>(), true));
    }

    private sealed record TestIdentity(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestIdentity identity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Name, "safe-test"), new(ClaimTypes.Role, identity.Role) };
            claims.AddRange(identity.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
