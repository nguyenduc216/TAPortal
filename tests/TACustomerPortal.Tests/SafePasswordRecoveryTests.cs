using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Customer Portal "Quên mật khẩu": one-time 30-minute links by email, no account enumeration, link built from the
// configured public address (never the request's Host). No SQL, no SMTP: the store and the sender are in memory.
public sealed class SafePasswordRecoveryTests
{
    private const string Base = "https://hoadon.example.vn";

    [Fact]
    public async Task KnownAccountGetsAOneTimeLinkThatSetsTheNewPassword()
    {
        using var f = Factory(out var resets, out var mail);
        var c = Client(f);
        var sent = await Post(c, "/account/forgot-password", "/account/forgot-password", new() { ["Login"] = " Customer@Example.Invalid " });
        Assert.Contains("data-cp-forgot-sent", Html(await sent.Content.ReadAsStringAsync()));
        var message = Assert.Single(mail.Sent);
        Assert.Equal("customer@example.invalid", message.To);
        var link = Regex.Match(message.TextBody, Regex.Escape(Base) + @"/account/reset-password\?token=([A-Za-z0-9_-]+)");
        Assert.True(link.Success);
        var token = link.Groups[1].Value;
        Assert.DoesNotContain(resets.Tokens, t => t.Hash.SequenceEqual(System.Text.Encoding.ASCII.GetBytes(token)));   // only the hash is stored

        var form = Html(await c.GetStringAsync("/account/reset-password?token=" + token));
        Assert.Contains("Tài khoản <strong>customer</strong>", form);
        var weak = await Post(c, "/account/reset-password", "/account/reset-password?token=" + token, new() { ["Token"] = token, ["NewPassword"] = "short", ["ConfirmPassword"] = "short" });
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
        Assert.Null(resets.NewHash);
        var done = await Post(c, "/account/reset-password", "/account/reset-password?token=" + token, new() { ["Token"] = token, ["NewPassword"] = "Recovered-pass!1", ["ConfirmPassword"] = "Recovered-pass!1" });
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.True(new PasswordService().Verify("Recovered-pass!1", resets.NewHash));

        Assert.Contains("data-cp-reset-invalid", Html(await c.GetStringAsync("/account/reset-password?token=" + token)));   // used once
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("throttled")]
    public async Task UnknownOrThrottledAccountsGetTheSameAnswerAndNoEmail(string login)
    {
        using var f = Factory(out var resets, out var mail);
        resets.Requests = login == "throttled" ? PasswordResetTokens.MaxRequestsPerHour : 0;
        var r = await Post(Client(f), "/account/forgot-password", "/account/forgot-password", new() { ["Login"] = login == "throttled" ? "customer" : login });
        Assert.Contains("data-cp-forgot-sent", Html(await r.Content.ReadAsStringAsync()));
        Assert.Empty(mail.Sent);
        Assert.Empty(resets.Tokens);
    }

    [Fact]
    public async Task ExpiredOrForgedLinksAreRefused()
    {
        using var f = Factory(out var resets, out _);
        resets.Tokens.Add((PasswordResetTokens.Hash("expired-token")!, DateTime.UtcNow.AddMinutes(-1), false));
        var c = Client(f);
        Assert.Contains("data-cp-reset-invalid", Html(await c.GetStringAsync("/account/reset-password?token=expired-token")));
        Assert.Contains("data-cp-reset-invalid", Html(await c.GetStringAsync("/account/reset-password?token=forged")));
        Assert.Contains("data-cp-reset-invalid", Html(await c.GetStringAsync("/account/reset-password")));
    }

    [Fact]
    public async Task TheLinkUsesTheConfiguredAddressNotTheRequestHost()
    {
        using var f = Factory(out _, out var mail);
        var c = Client(f);
        c.DefaultRequestHeaders.Host = "evil.example";
        await Post(c, "/account/forgot-password", "/account/forgot-password", new() { ["Login"] = "customer" });
        var message = Assert.Single(mail.Sent);
        Assert.Contains(Base + "/account/reset-password?token=", message.TextBody);
        Assert.DoesNotContain("evil.example", message.TextBody + message.HtmlBody);
    }

    [Fact]
    public async Task WithoutEmailSettingsThePageSaysToAskTA()
    {
        using var f = Factory(out _, out var mail, configured: false);
        var c = Client(f);
        Assert.Contains("data-cp-forgot-disabled", Html(await c.GetStringAsync("/account/forgot-password")));
        Assert.Contains("Quên mật khẩu?", Html(await c.GetStringAsync("/account/login")));
        await Post(c, "/account/forgot-password", "/account/forgot-password", new() { ["Login"] = "customer" });
        Assert.Empty(mail.Sent);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static string Html(string s) => WebUtility.HtmlDecode(s);

    private static HttpClient Client(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, string tokenPath, Dictionary<string, string> data)
    {
        var page = await c.GetStringAsync(tokenPath);
        data["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        return await c.PostAsync(path, new FormUrlEncodedContent(data));
    }

    private static WebApplicationFactory<Program> Factory(out MemoryResets resets, out MemoryMail mail, bool configured = true)
    {
        var r = resets = new MemoryResets();
        var m = mail = new MemoryMail(configured);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "INVALID: tests must never use SQL",
                ["DataProtection:KeyPath"] = "",
                ["Email:PublicBaseUrl"] = Base + "/"
            }))
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IPasswordResetStore>();
                s.AddSingleton<IPasswordResetStore>(r);
                s.RemoveAll<IEmailSender>();
                s.AddSingleton<IEmailSender>(m);
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
            }));
    }

    public sealed class MemoryMail(bool configured) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool IsConfigured => configured;
        public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Sent.Add(message); return Task.CompletedTask; }
    }

    public sealed class MemoryResets : IPasswordResetStore
    {
        private static readonly RecoveryAccount Account = new(Guid.NewGuid(), "customer", "Customer Person", "customer@example.invalid");
        public List<(byte[] Hash, DateTime Expires, bool Used)> Tokens { get; } = [];
        public int Requests { get; set; }
        public string? NewHash { get; private set; }

        public Task<RecoveryAccount?> FindAccountAsync(string usernameOrEmail) =>
            Task.FromResult(usernameOrEmail.Trim().ToUpperInvariant() is "CUSTOMER" or "CUSTOMER@EXAMPLE.INVALID" ? Account : null);
        public Task<int> RecentRequestsAsync(Guid userId, DateTime sinceUtc) => Task.FromResult(Requests);
        public Task SaveTokenAsync(Guid userId, byte[] tokenHash, DateTime expiresUtc, string? requestIp)
        {
            for (var i = 0; i < Tokens.Count; i++) Tokens[i] = Tokens[i] with { Used = true };
            Tokens.Add((tokenHash, expiresUtc, false));
            return Task.CompletedTask;
        }
        public Task<RecoveryAccount?> FindByTokenAsync(byte[] tokenHash, DateTime nowUtc) =>
            Task.FromResult(Tokens.Any(t => t.Hash.SequenceEqual(tokenHash) && !t.Used && t.Expires > nowUtc) ? Account : null);
        public async Task<bool> ResetAsync(byte[] tokenHash, string newPasswordHash, DateTime nowUtc)
        {
            if (await FindByTokenAsync(tokenHash, nowUtc) is null) return false;
            var i = Tokens.FindIndex(t => t.Hash.SequenceEqual(tokenHash));
            Tokens[i] = Tokens[i] with { Used = true };
            NewHash = newPasswordHash;
            return true;
        }
    }
}
