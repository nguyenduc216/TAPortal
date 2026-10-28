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
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// AUD-12 (Checkpoint 6). No SQL: business rules run against an in-memory store that applies the same
// RoleChangePolicy serially. Real SQL locking/transactions are NOT verified here (operator UAT).
public sealed class SafeAdminRoleTests
{
    private const string TestScheme = "SafeRoleTest";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Target = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SysAdminRole = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EditorRole = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid RetiredRole = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static AdminServices.UserRoleSnapshot Snapshot(Guid user, IEnumerable<Guid> assigned, IEnumerable<Guid> admins) =>
        new(user, "user", "User", true, "ACTIVE",
            [new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", true), new(EditorRole, "EDITOR", "Biên tập", true), new(RetiredRole, "LEGACY", "Cũ", false)],
            assigned.ToHashSet(), admins.ToHashSet());

    private static AdminServices.RoleChangePlan Evaluate(AdminServices.UserRoleSnapshot s, Guid actor, params Guid[] requested) =>
        AdminServices.RoleChangePolicy.Evaluate(s, actor, requested, s.Stamp);

    [Fact]
    public void NonSystemAdminCannotChangeRolesIncludingSelfEscalation()
    {
        var self = Snapshot(Target, [EditorRole], [Actor]);
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, Evaluate(self, Target, EditorRole, SysAdminRole).Outcome);
    }

    [Fact]
    public void UnknownOrInactiveRolesAreRejected()
    {
        var s = Snapshot(Target, [], [Actor]);
        Assert.Equal(AdminServices.RoleChangeOutcome.InvalidRole, Evaluate(s, Actor, Guid.NewGuid()).Outcome);
        Assert.Equal(AdminServices.RoleChangeOutcome.InvalidRole, Evaluate(s, Actor, RetiredRole).Outcome);
    }

    [Fact]
    public void LastActiveSystemAdminCannotLoseSysAdminEvenBySelf()
    {
        var only = Snapshot(Actor, [SysAdminRole], [Actor]);
        Assert.Equal(AdminServices.RoleChangeOutcome.LastSystemAdmin, Evaluate(only, Actor).Outcome);
        Assert.Equal(AdminServices.RoleChangeOutcome.LastSystemAdmin, Evaluate(only, Actor, EditorRole).Outcome);
    }

    [Fact]
    public void SysAdminCanBeRemovedWhenAnotherActiveAdminRemains()
    {
        var plan = Evaluate(Snapshot(Actor, [SysAdminRole], [Actor, Target]), Actor, EditorRole);
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, plan.Outcome);
        Assert.Equal([EditorRole], plan.Add);
        Assert.Equal([SysAdminRole], plan.Remove);
    }

    [Fact]
    public void SystemAdminMayGrantSysAdminToAnotherUser()
    {
        var plan = Evaluate(Snapshot(Target, [EditorRole], [Actor]), Actor, EditorRole, SysAdminRole);
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, plan.Outcome);
        Assert.Equal([SysAdminRole], plan.Add);
        Assert.Empty(plan.Remove);
    }

    [Fact]
    public void InactiveAssignmentsArePreservedAndUnchangedRequestsAreNoOps()
    {
        var s = Snapshot(Target, [EditorRole, RetiredRole], [Actor]);
        var cleared = Evaluate(s, Actor);
        Assert.Equal([EditorRole], cleared.Remove);
        Assert.Contains(RetiredRole, cleared.Final);
        Assert.Equal(AdminServices.RoleChangeOutcome.NoChange, Evaluate(s, Actor, EditorRole).Outcome);
    }

    [Fact]
    public void StaleStampIsRejectedAndStampIsOrderIndependent()
    {
        var s = Snapshot(Target, [EditorRole], [Actor]);
        Assert.Equal(AdminServices.RoleChangeOutcome.Conflict, AdminServices.RoleChangePolicy.Evaluate(s, Actor, [SysAdminRole], AdminServices.RoleChangePolicy.Stamp([])).Outcome);
        Assert.Equal(AdminServices.RoleChangePolicy.Stamp([EditorRole, SysAdminRole]), AdminServices.RoleChangePolicy.Stamp([SysAdminRole, EditorRole]));
    }

    [Fact]
    public async Task SerializedConcurrentEditsCannotRemoveBothRemainingAdmins()
    {
        // Two admins each try to remove the other's SYS_ADMIN using pages loaded at the same time.
        var store = new MemoryRoleStore();
        store.Assign(Target, SysAdminRole);
        var actorPage = (await store.GetAsync(Target))!;
        var targetPage = (await store.GetAsync(Actor))!;
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, (await store.UpdateAsync(Actor, Target, [], actorPage.Stamp, null)).Outcome);
        var second = await store.UpdateAsync(Target, Actor, [], targetPage.Stamp, null);
        Assert.Equal(AdminServices.RoleChangeOutcome.Forbidden, second.Outcome); // the second actor is no longer SYS_ADMIN
        Assert.Contains(SysAdminRole, store.RolesOf(Actor));
        // Same page submitted twice: the second submission carries a stale stamp.
        var page = (await store.GetAsync(Target))!;
        Assert.Equal(AdminServices.RoleChangeOutcome.Updated, (await store.UpdateAsync(Actor, Target, [EditorRole], page.Stamp, null)).Outcome);
        Assert.Equal(AdminServices.RoleChangeOutcome.Conflict, (await store.UpdateAsync(Actor, Target, [], page.Stamp, null)).Outcome);
    }

    [Fact]
    public void RoleSqlNeverTouchesPasswordsUsernamesOrCustomerPortalMemberships()
    {
        var statements = typeof(AdminServices.UserRoleSql).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(statements);
        Assert.All(statements, sql =>
        {
            Assert.DoesNotContain("PasswordHash", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CustomerPortalMemberships", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"UPDATE\s+dbo\.Users", sql);
            Assert.DoesNotMatch(@"(INSERT|UPDATE|DELETE)\s+(FROM\s+)?dbo\.(Roles|Permissions|RolePermissions|UserPermissions)\b", sql);
        });
        Assert.StartsWith("IF OBJECT_ID(N'dbo.AuditLogs',N'U') IS NOT NULL", AdminServices.UserRoleSql.Audit);
    }

    [Fact]
    public async Task SystemAdminSeesRoleScreenWithStampAndNoStore()
    {
        using var f = Factory("SYS_ADMIN", new MemoryRoleStore());
        var response = await Client(f).GetAsync($"/users/roles/{Target}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SYS_ADMIN", html);
        Assert.Contains("EDITOR", html);
        Assert.Contains("name=\"stamp\"", html);
        Assert.DoesNotContain($"value=\"{RetiredRole}\"", html); // inactive roles are not selectable
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task UsersWithoutSystemAdminAreForbiddenEvenWithRolePermission(string method)
    {
        var store = new MemoryRoleStore();
        using var f = Factory("OWNER", store, "AUTH.ROLES.MANAGE", "AUTH.USERS.UPDATE");
        var request = new HttpRequestMessage(new HttpMethod(method), $"/users/roles/{Target}") { Content = method == "POST" ? new FormUrlEncodedContent([new("roleIds", SysAdminRole.ToString())]) : null };
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(f).SendAsync(request)).StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task MissingCsrfTokenIsRejected()
    {
        var store = new MemoryRoleStore();
        using var f = Factory("SYS_ADMIN", store);
        var response = await Client(f).PostAsync($"/users/roles/{Target}", new FormUrlEncodedContent([new("roleIds", SysAdminRole.ToString())]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task SuccessfulUpdateIgnoresOverpostedFieldsAndKeepsPasswordAndMemberships()
    {
        var store = new MemoryRoleStore();
        using var f = Factory("SYS_ADMIN", store);
        using var c = Client(f);
        var (token, stamp) = await Form(c);
        var response = await c.PostAsync($"/users/roles/{Target}", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("stamp", stamp), new("roleIds", EditorRole.ToString()), new("roleIds", SysAdminRole.ToString()),
            new("PasswordHash", "PBKDF2$attacker"), new("Username", "renamed"), new("IsActive", "false"), new("CustomerId", Guid.NewGuid().ToString()), new("PortalRole", "OWNER"), new("UserId", Actor.ToString())]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Users/Roles/{Target}", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { SysAdminRole, EditorRole }.Order(), store.RolesOf(Target).Order());
        Assert.Equal((Actor, Target), store.LastCall);
        Assert.Equal(MemoryRoleStore.OriginalPassword, store.PasswordHash);
        Assert.Equal("target", store.Username);
        Assert.Equal(MemoryRoleStore.OriginalMemberships, store.Memberships);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("invalid-role", "Vai trò không hợp lệ")]
    [InlineData("malformed-id", "không hợp lệ")]
    [InlineData("stale-stamp", "vừa được thay đổi bởi thao tác khác")]
    [InlineData("last-admin", "quản trị viên hoạt động cuối cùng")]
    public async Task RejectedChangesShowErrorAndWriteNothing(string kind, string message)
    {
        var store = new MemoryRoleStore();
        using var f = Factory("SYS_ADMIN", store);
        using var c = Client(f);
        var targetUser = kind == "last-admin" ? Actor : Target;
        var (token, stamp) = await Form(c, targetUser);
        var roles = kind switch { "invalid-role" => Guid.NewGuid().ToString(), "malformed-id" => "not-a-guid", _ => EditorRole.ToString() };
        var response = await c.PostAsync($"/users/roles/{targetUser}", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("stamp", kind == "stale-stamp" ? "STALE" : stamp), new("roleIds", roles)]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ServerRechecksActorAgainstStoreNotCookieClaims()
    {
        var store = new MemoryRoleStore();
        using var f = Factory("SYS_ADMIN", store);
        using var c = Client(f);
        var (token, stamp) = await Form(c);
        store.Revoke(Actor, SysAdminRole); // revoked after the page was rendered; the cookie still claims SYS_ADMIN
        store.Assign(Target, SysAdminRole);  // keep another admin so the actor can be revoked
        var response = await c.PostAsync($"/users/roles/{Target}", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("stamp", stamp), new("roleIds", EditorRole.ToString())]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownUserReturnsNotFound()
    {
        using var f = Factory("SYS_ADMIN", new MemoryRoleStore());
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).GetAsync($"/users/roles/{Guid.NewGuid()}")).StatusCode);
    }

    private static async Task<(string Token, string Stamp)> Form(HttpClient c, Guid? user = null)
    {
        var html = await c.GetStringAsync($"/users/roles/{user ?? Target}");
        return (WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value),
                Regex.Match(html, "name=\"stamp\" value=\"([0-9A-F]+)\"").Groups[1].Value);
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryRoleStore store, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<AdminServices.IUserRoleStore>();
                s.AddSingleton<AdminServices.IUserRoleStore>(store);
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    // Applies RoleChangePolicy under a lock, mirroring the serializable SQL transaction.
    private sealed class MemoryRoleStore : AdminServices.IUserRoleStore
    {
        public const string OriginalPassword = "PBKDF2$SHA256$150000$memory$only";
        public static readonly string[] OriginalMemberships = ["CUSTOMER-A:OWNER", "CUSTOMER-B:VIEWER"];
        private readonly object gate = new();
        private readonly Dictionary<Guid, HashSet<Guid>> assignments = new() { [Actor] = [SysAdminRole], [Target] = [] };
        private readonly List<AdminServices.RoleOption> catalog = [new(SysAdminRole, "SYS_ADMIN", "Quản trị hệ thống", true), new(EditorRole, "EDITOR", "Biên tập", true), new(RetiredRole, "LEGACY", "Cũ", false)];
        public string PasswordHash { get; } = OriginalPassword;
        public string Username { get; } = "target";
        public string[] Memberships { get; } = OriginalMemberships.ToArray();
        public int Writes { get; private set; }
        public (Guid, Guid)? LastCall { get; private set; }
        public IReadOnlySet<Guid> RolesOf(Guid user) => assignments[user];
        public void Assign(Guid user, Guid role) { lock (gate) assignments[user].Add(role); }
        public void Revoke(Guid user, Guid role) { lock (gate) assignments[user].Remove(role); }

        public Task<AdminServices.UserRoleSnapshot?> GetAsync(Guid userId)
        {
            lock (gate) return Task.FromResult(Snap(userId));
        }

        public Task<AdminServices.RoleChangeResult> UpdateAsync(Guid actorId, Guid userId, IReadOnlyCollection<Guid> requested, string? stamp, string? ip)
        {
            lock (gate)
            {
                LastCall = (actorId, userId);
                var snapshot = Snap(userId);
                if (snapshot is null) return Task.FromResult(new AdminServices.RoleChangeResult(AdminServices.RoleChangeOutcome.NotFound));
                var plan = AdminServices.RoleChangePolicy.Evaluate(snapshot, actorId, requested, stamp);
                if (plan.Outcome != AdminServices.RoleChangeOutcome.Updated) return Task.FromResult(new AdminServices.RoleChangeResult(plan.Outcome));
                assignments[userId] = plan.Final.ToHashSet();
                Writes++;
                return Task.FromResult(new AdminServices.RoleChangeResult(AdminServices.RoleChangeOutcome.Updated, false, [], []));
            }
        }

        private AdminServices.UserRoleSnapshot? Snap(Guid userId) =>
            assignments.TryGetValue(userId, out var assigned)
                ? new(userId, userId == Target ? Username : "actor", "Display", true, "ACTIVE", catalog, assigned.ToHashSet(), assignments.Where(a => a.Value.Contains(SysAdminRole)).Select(a => a.Key).ToHashSet())
                : null;
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-role-test"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
