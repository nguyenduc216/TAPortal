extern alias admin;

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Checkpoint 10 release regression (AUD-39 + endpoint inventory). No SQL.
public sealed class SafeReleaseRegressionTests
{
    private static WebApplicationFactory<admin::Program> Admin() =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Production")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()));

    private static WebApplicationFactory<Program> Customer() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL", ["DataProtection:KeyPath"] = "" }))
            .ConfigureTestServices(s => { s.RemoveAll<ICustomerStore>(); s.AddSingleton<ICustomerStore>(new EmptyStore()); s.AddDataProtection().UseEphemeralDataProtectionProvider(); }));

    private static HttpClient Client<T>(WebApplicationFactory<T> f) where T : class =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static void AssertSecurityHeaders(HttpResponseMessage r)
    {
        foreach (var (name, value) in SecurityHeaders.Values)
            Assert.True(r.Headers.TryGetValues(name, out var v) && v.Single() == value, $"{r.RequestMessage!.RequestUri} missing {name}");
    }

    [Theory]
    [InlineData("/account/login", HttpStatusCode.OK)]
    [InlineData("/users", HttpStatusCode.Redirect)]
    [InlineData("/account/accessdenied", HttpStatusCode.OK)]
    [InlineData("/css/ta-portal.css", HttpStatusCode.OK)]
    [InlineData("/does-not-exist", HttpStatusCode.NotFound)]
    public async Task AdminSendsSecurityHeadersOnEveryResponseType(string path, HttpStatusCode expected)
    {
        using var f = Admin();
        var r = await Client(f).GetAsync(path);
        Assert.Equal(expected, r.StatusCode);
        AssertSecurityHeaders(r);
    }

    [Theory]
    [InlineData("/account/login", HttpStatusCode.OK)]
    [InlineData("/", HttpStatusCode.Redirect)]
    [InlineData("/account/access-denied", HttpStatusCode.Forbidden)]
    [InlineData("/css/customer.css", HttpStatusCode.OK)]
    public async Task CustomerPortalSendsSecurityHeadersOnEveryResponseType(string path, HttpStatusCode expected)
    {
        using var f = Customer();
        var r = await Client(f).GetAsync(path);
        Assert.Equal(expected, r.StatusCode);
        AssertSecurityHeaders(r);
    }

    [Fact]
    public async Task SecurityHeadersDoNotWeakenNoStoreOrStaticCaching()
    {
        using var f = Admin();
        using var c = Client(f);
        Assert.True((await c.GetAsync("/account/login")).Headers.CacheControl!.NoStore);
        Assert.False((await c.GetAsync("/css/ta-portal.css")).Headers.CacheControl?.NoStore ?? false);
    }

    // Every MVC action must require authentication/authorization unless it is on the reviewed anonymous list.
    [Fact]
    public void AdminEndpointInventoryHasOnlyReviewedAnonymousActions()
    {
        using var f = Admin();
        var anonymous = Inventory(f.Services).Where(e => e.Anonymous).Select(e => e.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "Account.Login:GET", "Account.Login:POST", "Account.AccessDenied:ANY", "Home.Error:ANY" }, anonymous);
        Assert.All(Inventory(f.Services).Where(e => !e.Anonymous), e => Assert.True(e.Protected, $"{e.Name} has no authorization metadata"));
        Assert.All(Inventory(f.Services).Where(e => e.Name.EndsWith(":POST") && e.Name != "Account.Login:POST"), e => Assert.True(e.Protected, e.Name));
    }

    [Fact]
    public void CustomerEndpointInventoryHasOnlyReviewedAnonymousActions()
    {
        using var f = Customer();
        var endpoints = Inventory(f.Services);
        var anonymous = endpoints.Where(e => e.Anonymous).Select(e => e.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "Account.Login:GET", "Account.Login:POST", "Account.AccessDenied:GET", "Account.Error:ANY" }, anonymous);
        // Everything else falls under the authenticated fallback policy.
        Assert.NotNull(f.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value.FallbackPolicy);
    }

    [Fact]
    public void EveryAdminPostActionValidatesAntiforgery()
    {
        using var f = Admin();
        var posts = Inventory(f.Services).Where(e => e.Name.EndsWith(":POST")).ToList();
        Assert.NotEmpty(posts);
        Assert.All(posts, e => Assert.True(e.Antiforgery, $"{e.Name} lacks antiforgery validation"));
    }

    private sealed record EndpointInfo(string Name, bool Anonymous, bool Protected, bool Antiforgery);

    private static List<EndpointInfo> Inventory(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => (Endpoint: e, Action: e.Metadata.GetMetadata<ControllerActionDescriptor>()))
            .Where(x => x.Action is not null)
            .Select(x =>
            {
                var methods = x.Endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                var verb = methods is { Count: > 0 } ? string.Join("|", methods) : "ANY";
                var name = $"{x.Action!.ControllerName}.{x.Action.ActionName}:{verb}";
                var anonymous = x.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var authorize = x.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any();
                var antiforgery = x.Endpoint.Metadata.Any(m => m is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute)
                    || x.Action.FilterDescriptors.Any(fd => fd.Filter is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute);
                return new EndpointInfo(name, anonymous, authorize, antiforgery);
            })
            .DistinctBy(e => e.Name)
            .ToList();

    private sealed class EmptyStore : ICustomerStore
    {
        public Task<CustomerUser?> FindUser(string username) => Task.FromResult<CustomerUser?>(null);
        public Task<CustomerUser?> FindUser(Guid id) => Task.FromResult<CustomerUser?>(null);
        public Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId) => Task.FromResult<IReadOnlyList<CustomerMembership>>([]);
        public Task<bool> ChangePassword(Guid id, string oldHash, string newHash) => Task.FromResult(false);
        public Task TouchLogin(Guid id) => Task.CompletedTask;
    }
}
