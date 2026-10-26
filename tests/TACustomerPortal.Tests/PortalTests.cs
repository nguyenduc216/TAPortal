extern alias admin;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;
public sealed class PortalTests : IAsyncLifetime
{
    private readonly string database = "TACustomerPortal_Test_" + Guid.NewGuid().ToString("N");
    private string ConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\MSSQLLocalDB",
        InitialCatalog = database,
        IntegratedSecurity = true,
        TrustServerCertificate = true
    }.ConnectionString;

    private readonly Guid user = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
    private readonly PasswordService passwords = new();
    private const string Password = "Test-only!12345";
    private WebApplicationFactory<Program> factory = null !;
    private HttpClient client = null !;
    private IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "Database:ConnectionString", ConnectionString } }).Build();

    private static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "TAPortal.sln")))
                d = d.Parent;
            return d?.FullName ?? throw new Exception("Repository missing");
        }
    }

    public async Task InitializeAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("SQL integration tests require Windows LocalDB; do not use production");
        await using (var cn = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await cn.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{database}]", cn).ExecuteNonQueryAsync();
        }

        await Script("database/migrations/002-create-auth-core.sql", true);
        await Script("database/migrations/007-create-customer-core.sql", true);
        await Script("database/migrations/008-customer-portal-memberships.sql");
        await Execute("INSERT dbo.Users(Id,Username,NormalizedUsername,DisplayName,PasswordHash) VALUES(@u,'customer','CUSTOMER','Test Customer',@h); INSERT dbo.Customers(Id,Code,Name) VALUES(@a,'A','Company Alpha'),(@b,'B','Company Beta'); INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@u,@a,'OWNER');", ("@u", user), ("@h", passwords.Hash(Password)), ("@a", a), ("@b", b));
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddConfiguration(Configuration)));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory != null)
            await factory.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await cn.OpenAsync();
        // Only this randomly named test database can be dropped.
        if (!Regex.IsMatch(database, "^TACustomerPortal_Test_[a-f0-9]{32}$"))
            throw new Exception("Unsafe test database name");
        await new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", cn).ExecuteNonQueryAsync();
    }

    private async Task Script(string file, bool removeUse = false)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(Root, file));
        if (removeUse)
            text = text.Replace("USE [TAPortal];", "");
        foreach (var batch in Regex.Split(text, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch))
                await Execute(batch);
    }

    private async Task<int> Execute(string sql, params (string, object)[] values)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var(k, v)in values)
            cmd.Parameters.AddWithValue(k, v);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> Count(string sql)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        return Convert.ToInt32(await new SqlCommand(sql, cn).ExecuteScalarAsync());
    }

    private async Task<string> Token(string path)
    {
        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }

    private async Task<HttpResponseMessage> Post(string path, Dictionary<string, string> data, string tokenPath)
    {
        data["__RequestVerificationToken"] = await Token(tokenPath);
        return await client.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private Task<HttpResponseMessage> Login(string password = Password) => Post("/account/login", new() { { "Username", "customer" }, { "Password", password } }, "/account/login");
    [Fact]
    public async Task SeparateUsersSeeOnlyTheirOwnCustomer()
    {
        var second = Guid.NewGuid();
        await Execute("INSERT dbo.Users(Id,Username,NormalizedUsername,DisplayName,PasswordHash) VALUES(@id,'second','SECOND','Second user',@h); INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@id,@b,'VIEWER');", ("@id", second), ("@h", passwords.Hash(Password)), ("@b", b));
        await Login();
        Assert.Contains("Company Alpha", await client.GetStringAsync("/"));
        await Post("/account/logout", new(), "/");
        await Post("/account/login", new() { { "Username", "second" }, { "Password", Password } }, "/account/login");
        var html = await client.GetStringAsync("/profile/company?customerId=" + a);
        Assert.Contains("Company Beta", html);
        Assert.DoesNotContain("Company Alpha", html);
    }

    [Fact]
    public async Task CustomerCookieHasIndependentSecurityAttributes()
    {
        var response = await Login();
        var cookie = response.Headers.GetValues("Set-Cookie").Last(c => c.StartsWith("TA.Customer.Auth="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.DoesNotContain("TA.Portal.Auth=", cookie);
    }

    [Fact]
    public async Task ValidLoginAndTenantIsolation()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await Login()).StatusCode);
        var html = await client.GetStringAsync("/?customerId=" + b);
        Assert.Contains("Company Alpha", html);
        Assert.DoesNotContain("Company Beta", html);
        var profile = await client.GetStringAsync("/profile/company?customerId=" + b);
        Assert.Contains("Company Alpha", profile);
        Assert.DoesNotContain("Company Beta", profile);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("locked")]
    [InlineData("deleted")]
    [InlineData("no-membership")]
    [InlineData("customer-locked")]
    [InlineData("customer-deleted")]
    public async Task InvalidLoginRejected(string condition)
    {
        if (condition == "locked")
            await Execute("UPDATE dbo.Users SET IsActive=0");
        if (condition == "deleted")
            await Execute("UPDATE dbo.Users SET IsDeleted=1");
        if (condition == "no-membership")
            await Execute("UPDATE dbo.CustomerPortalMemberships SET IsActive=0");
        if (condition == "customer-locked")
            await Execute("UPDATE dbo.Customers SET Status='LOCKED'");
        if (condition == "customer-deleted")
            await Execute("UPDATE dbo.Customers SET IsDeleted=1");
        var response = await Login(condition == "wrong" ? "Wrong!123" : Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task MultiCustomerSwitchRechecksMembership()
    {
        await Execute("INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@u,@b,'VIEWER')", ("@u", user), ("@b", b));
        Assert.Equal("/account/select-customer", (await Login()).Headers.Location?.OriginalString);
        var response = await Post("/account/select-customer", new() { { "customerId", b.ToString() } }, "/account/select-customer");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Company Beta", await client.GetStringAsync("/"));
        await Execute("UPDATE dbo.CustomerPortalMemberships SET IsActive=0 WHERE CustomerId=@b", ("@b", b));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task SystemAdminHasNoMembershipBypass()
    {
        await Execute("INSERT dbo.Roles(Id,Code,Name) VALUES(NEWID(),'SYS_ADMIN','Admin'); INSERT dbo.UserRoles(UserId,RoleId) SELECT @u,Id FROM dbo.Roles; UPDATE dbo.CustomerPortalMemberships SET IsActive=0;", ("@u", user));
        Assert.Equal(HttpStatusCode.OK, (await Login()).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task CustomerCannotLoginToAdminOrReuseCustomerCookie()
    {
        await Login();
        using var adminFactory = new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddConfiguration(Configuration)));
        using var adminClient = adminFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var page = await adminClient.GetStringAsync("/account/login");
        var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var result = await adminClient.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Username", "customer" }, { "Password", Password }, { "__RequestVerificationToken", token } }));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        foreach (var route in new[]
        {
            "/",
            "/customers",
            "/users",
            "/customerportalaccounts"
        }

        )
            Assert.Equal(HttpStatusCode.Redirect, (await adminClient.GetAsync(route)).StatusCode);
        var rawCookie = (await Login()).Headers.GetValues("Set-Cookie").First(c => c.StartsWith("TA.Customer.Auth=")).Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/customerportalaccounts");
        request.Headers.Add("Cookie", rawCookie);
        Assert.Equal(HttpStatusCode.Redirect, (await adminClient.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task AdminCanProvisionWithoutGrantingSystemRole()
    {
        await Execute("INSERT dbo.Roles(Id,Code,Name) VALUES(NEWID(),'SYS_ADMIN','Admin'); INSERT dbo.UserRoles(UserId,RoleId) SELECT @u,Id FROM dbo.Roles;", ("@u", user));
        using var adminFactory = new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").ConfigureAppConfiguration((_, c) => c.AddConfiguration(Configuration)));
        using var adminClient = adminFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        async Task<string> AdminToken(string path)
        {
            var html = await adminClient.GetStringAsync(path);
            return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        }

        var loginToken = await AdminToken("/account/login");
        Assert.Equal(HttpStatusCode.Redirect, (await adminClient.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> { { "Username", "customer" }, { "Password", Password }, { "__RequestVerificationToken", loginToken } }))).StatusCode);
        var token = await AdminToken("/customerportalaccounts");
        var result = await adminClient.PostAsync("/customerportalaccounts/save", new FormUrlEncodedContent(new Dictionary<string, string> { { "CustomerId", b.ToString() }, { "Username", "provisioned" }, { "DisplayName", "Provisioned user" }, { "Password", Password }, { "PortalRole", "VIEWER" }, { "__RequestVerificationToken", token } }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM dbo.CustomerPortalMemberships m JOIN dbo.Users u ON u.Id=m.UserId WHERE u.Username='provisioned'"));
        Assert.Equal(0, await Count("SELECT COUNT(*) FROM dbo.UserRoles ur JOIN dbo.Users u ON u.Id=ur.UserId WHERE u.Username='provisioned'"));
    }

    [Fact]
    public async Task AnonymousBusinessPagesChallenge()
    {
        foreach (var path in new[]
        {
            "/",
            "/profile/company",
            "/profile/personal",
            "/account/change-password"
        }

        )
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task CannotSelectForeignCustomer()
    {
        await Login();
        var denied = await Post("/account/select-customer", new() { { "customerId", b.ToString() } }, "/account/select-customer");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("/account/access-denied", denied.Headers.Location!.OriginalString);
        Assert.Contains("Company Alpha", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task RevocationInvalidatesExistingCookie()
    {
        await Login();
        await Execute("UPDATE dbo.CustomerPortalMemberships SET IsActive=0");
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeIsSharedAndInvalidatesOtherSessions()
    {
        await Login();
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var savedClient = client;
        client = other;
        await Login();
        client = savedClient;
        var response = await Post("/account/change-password", new() { { "CurrentPassword", Password }, { "NewPassword", "New-password!678" }, { "ConfirmPassword", "New-password!678" } }, "/account/change-password");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/")).StatusCode);
        var store = new CustomerStore(Configuration);
        var updated = await store.FindUser(user);
        Assert.True(passwords.Verify("New-password!678", updated!.PasswordHash));
        Assert.False(passwords.Verify(Password, updated.PasswordHash));
        Assert.Equal(HttpStatusCode.Redirect, (await Login("New-password!678")).StatusCode);
    }

    [Fact]
    public async Task CsrfAndLogout()
    {
        await Login();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("/account/logout", new(), "/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task LoginRateLimited()
    {
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await Login("wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login("wrong")).StatusCode);
    }

    [Fact]
    public async Task DatabaseConstraintsAndTransactionRollback()
    {
        await Assert.ThrowsAsync<SqlException>(() => Execute("INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@u,@a,'VIEWER')", ("@u", user), ("@a", a)));
        await Assert.ThrowsAsync<SqlException>(() => Execute("INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(NEWID(),@a,'VIEWER')", ("@a", a)));
        await Assert.ThrowsAsync<SqlException>(() => Execute("INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@u,NEWID(),'VIEWER')", ("@u", user)));
        await Assert.ThrowsAsync<SqlException>(() => Execute("INSERT dbo.CustomerPortalMemberships(UserId,CustomerId,PortalRole) VALUES(@u,@b,'SYS_ADMIN')", ("@u", user), ("@b", b)));
        var store = new MembershipAdminStore(Configuration);
        await Assert.ThrowsAsync<SqlException>(() => store.Save(new() { CustomerId = Guid.NewGuid(), Username = "rollback", DisplayName = "Rollback", PortalRole = "VIEWER" }, user, passwords.Hash(Password)));
        Assert.Equal(1, await Count("SELECT COUNT(*) FROM dbo.Users"));
    }

    [Fact]
    public async Task ProvisionReactivateAndMigrationIdempotency()
    {
        var store = new MembershipAdminStore(Configuration);
        await store.Save(new() { CustomerId = b, Username = "new-user", DisplayName = "New user", PortalRole = "ADMIN" }, user, passwords.Hash(Password));
        var rows = await store.List(b);
        Assert.Single(rows);
        await store.SetActive(rows[0].UserId, b, false, user);
        Assert.False((await store.List(b))[0].Active);
        await store.Save(new() { CustomerId = b, UserId = rows[0].UserId, PortalRole = "VIEWER" }, user, null);
        Assert.True((await store.List(b))[0].Active);
        await Script("database/migrations/008-customer-portal-memberships.sql");
        await Script("database/scripts/004-verify-customer-portal.sql");
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM dbo.Users"));
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM dbo.Customers"));
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM dbo.CustomerPortalMemberships"));
        await Assert.ThrowsAsync<SqlException>(() => Script("database/scripts/005-rollback-customer-portal.sql"));
        Assert.Equal(2, await Count("SELECT COUNT(*) FROM dbo.CustomerPortalMemberships"));
    }

    [Theory]
    [InlineData("PBKDF2$SHA256$0$AA==$AA==")]
    [InlineData("PBKDF2$SHA256$150000$bad$bad")]
    [InlineData("invalid")]
    public void MalformedHashesFailClosed(string hash) => Assert.False(passwords.Verify(Password, hash));
}
