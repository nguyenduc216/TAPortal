using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.ZaloMiniApp;

namespace TACustomerPortal.Tests;

// T.A Zalo Mini App sign-in (operator decisions 2026-11-13): only the phone counts. The Zalo phone must equal the phone of the
// TA Portal / Hoa don tu dong account; the web QR is one-time, 5 minutes, and opens only for that phone; the direct way needs
// username + password and the same phone; phones are verified by a ZNS OTP. No network, no database.
public sealed class SafeZaloMiniAppTests
{
    private static readonly Guid Ann = Guid.Parse("a0000000-0000-0000-0000-00000000000a"), Bob = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private const string AnnPhone = "84912345678", BobPhone = "84987654321";

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store : IMiniAppStore
    {
        public readonly Dictionary<Guid, MiniAppAccount> Accounts = [];
        public readonly Dictionary<Guid, (string Phone, string Method)> Verified = [];
        public readonly List<(byte[] Hash, Guid User, DateTime Expires, bool Used)> Qrs = [];
        public readonly List<(string Zalo, Guid User, string Phone, string Source, bool Revoked)> Links = [];
        public readonly List<OtpChallenge> Otps = [];
        public readonly List<DateTime> OtpTimes = [];
        public Func<DateTime> Now = () => DateTime.UtcNow;

        public Task<MiniAppAccount?> AccountAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Accounts.GetValueOrDefault(userId));
        public Task<MiniAppAccount?> AccountByUsernameAsync(string username, CancellationToken ct = default) =>
            Task.FromResult(Accounts.Values.FirstOrDefault(a => a.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)));
        public Task<string?> VerifiedPhoneAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Verified.TryGetValue(userId, out var v) ? v.Phone : null);
        public Task SetVerifiedPhoneAsync(Guid userId, string phone, string method, CancellationToken ct = default) { Verified[userId] = (phone, method); return Task.CompletedTask; }
        public Task CreateQrAsync(byte[] tokenHash, Guid userId, string portal, DateTime expiresUtc, CancellationToken ct = default) { Qrs.Add((tokenHash, userId, expiresUtc, false)); return Task.CompletedTask; }
        public Task<Guid?> ConsumeQrAsync(byte[] tokenHash, string zaloUserId, DateTime nowUtc, CancellationToken ct = default)
        {
            var i = Qrs.FindIndex(q => q.Hash.SequenceEqual(tokenHash) && !q.Used && q.Expires > nowUtc);
            if (i < 0) return Task.FromResult<Guid?>(null);
            Qrs[i] = Qrs[i] with { Used = true };
            return Task.FromResult<Guid?>(Qrs[i].User);
        }
        public Task LinkAsync(string zaloUserId, Guid userId, string phone, string source, CancellationToken ct = default)
        {
            var i = Links.FindIndex(l => l.Zalo == zaloUserId && l.User == userId && !l.Revoked);
            if (i >= 0) Links[i] = Links[i] with { Phone = phone }; else Links.Add((zaloUserId, userId, phone, source, false));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<(Guid UserId, string Phone)>> LinksAsync(string zaloUserId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(Guid, string)>>(Links.Where(l => l.Zalo == zaloUserId && !l.Revoked).Select(l => (l.User, l.Phone)).ToList());
        public Task<bool> TouchLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default) => Task.FromResult(Links.Any(l => l.Zalo == zaloUserId && l.User == userId && !l.Revoked));
        public Task<int> RevokeLinkAsync(string zaloUserId, Guid userId, CancellationToken ct = default) => Revoke(l => l.Zalo == zaloUserId && l.User == userId);
        public Task<int> RevokeLinksAsync(Guid userId, Guid actor, CancellationToken ct = default) => Revoke(l => l.User == userId);
        public Task<int> ActiveLinkCountAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Links.Count(l => l.User == userId && !l.Revoked));
        private Task<int> Revoke(Func<(string Zalo, Guid User, string Phone, string Source, bool Revoked), bool> match)
        {
            var n = 0;
            for (var i = 0; i < Links.Count; i++) if (!Links[i].Revoked && match(Links[i])) { Links[i] = Links[i] with { Revoked = true }; n++; }
            return Task.FromResult(n);
        }
        public Task CreateOtpAsync(Guid id, Guid userId, string phone, string purpose, byte[] codeHash, DateTime expiresUtc, string? ip, CancellationToken ct = default)
        {
            Otps.Add(new(id, userId, phone, codeHash, 0, expiresUtc, null));
            OtpTimes.Add(Now());
            return Task.CompletedTask;
        }
        public Task<(int Count, DateTime? LastUtc)> OtpStatsAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default)
        {
            var times = Otps.Select((o, i) => (o, t: OtpTimes[i])).Where(x => x.o.UserId == userId && x.t >= sinceUtc).Select(x => x.t).ToList();
            return Task.FromResult((times.Count, times.Count == 0 ? (DateTime?)null : times.Max()));
        }
        public Task<OtpChallenge?> OtpAsync(Guid id, Guid userId, CancellationToken ct = default) => Task.FromResult(Otps.FirstOrDefault(o => o.Id == id && o.UserId == userId));
        public Task FailOtpAsync(Guid id, CancellationToken ct = default) { Update(id, o => o with { Attempts = o.Attempts + 1 }); return Task.CompletedTask; }
        public Task<bool> ConsumeOtpAsync(Guid id, DateTime nowUtc, CancellationToken ct = default)
        {
            var o = Otps.First(x => x.Id == id);
            if (o.ConsumedAtUtc is not null || o.ExpiresAtUtc <= nowUtc) return Task.FromResult(false);
            Update(id, x => x with { ConsumedAtUtc = nowUtc });
            return Task.FromResult(true);
        }
        private void Update(Guid id, Func<OtpChallenge, OtpChallenge> f) { var i = Otps.FindIndex(o => o.Id == id); Otps[i] = f(Otps[i]); }
    }

    /// <summary>Zalo: access token "zalo:&lt;user id&gt;", phone token "phone:&lt;number&gt;".</summary>
    private sealed class Graph : IZaloGraph
    {
        public Task<(ZaloIdentity? Identity, string? Error)> IdentityAsync(string accessToken, string? phoneToken, CancellationToken ct = default)
        {
            if (!accessToken.StartsWith("zalo:", StringComparison.Ordinal)) return Task.FromResult<(ZaloIdentity?, string?)>((null, "Phiên Zalo đã hết hạn"));
            var phone = phoneToken is null ? null : MiniAppPhone.Normalize(phoneToken.Replace("phone:", ""));
            if (phoneToken is not null && phone is null) return Task.FromResult<(ZaloIdentity?, string?)>((null, "Không đọc được số"));
            return Task.FromResult<(ZaloIdentity?, string?)>((new(accessToken[5..], phone), null));
        }
    }

    private sealed class Passwords : IMiniAppPasswordCheck
    {
        public int Attempts;
        public bool TryAcquire(string username) => ++Attempts <= 5;
        public bool Verify(string password, string? passwordHash) => passwordHash == "hash:" + password;
    }

    private sealed class Sender : IZnsSender
    {
        public readonly List<(string Phone, string Template, IReadOnlyDictionary<string, string> Data)> Sent = [];
        public string? Fail;
        public Task<string?> SendAsync(string phone, string templateId, IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
        {
            Sent.Add((phone, templateId, data));
            return Task.FromResult(Fail);
        }
    }

    private sealed class Rig
    {
        public readonly Clock Clock = new();
        public readonly Store Store = new();
        public readonly Sender Sender = new();
        public readonly Passwords Passwords = new();
        public readonly MiniAppAuth Auth;
        public readonly PhoneVerification Otp;

        public Rig(bool configured = true)
        {
            Store.Now = () => Clock.GetUtcNow().UtcDateTime;
            var options = Options.Create(new ZaloMiniAppOptions { AppId = configured ? "1234567890" : null, SecretKey = configured ? "secret" : null, OtpTemplateId = "445566" });
            Auth = new(Store, new Graph(), Passwords, new EphemeralDataProtectionProvider(), options, Clock);
            Otp = new(Store, Sender, options, Clock);
            Store.Accounts[Ann] = new(Ann, "ann", "Chị An", "0912 345 678", "hash:Pass@123", false);
            Store.Accounts[Bob] = new(Bob, "bob", "Anh Bình", "+84 987 654 321", "hash:Pass@456", false);
        }

        public void Verify(Guid user, string phone) => Store.Verified[user] = (phone, VerifiedPhoneMethod.ZnsOtp);

        public async Task<string> QrToken(Guid user)
        {
            var qr = await Auth.CreateQrAsync(user, MiniAppPortal.Invoice);
            Assert.True(qr.Ok, qr.Error);
            return Uri.UnescapeDataString(qr.Link!.Split("?qr=")[1]);
        }
    }

    [Theory]
    [InlineData("0912345678", "84912345678")]
    [InlineData("0912 345 678", "84912345678")]
    [InlineData("+84 912.345.678", "84912345678")]
    [InlineData("84912345678", "84912345678")]
    [InlineData("091234567", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void PhonesAreComparedInOneForm(string? raw, string? expected) => Assert.Equal(expected, MiniAppPhone.Normalize(raw));

    [Fact]
    public void QrLinkOpensTheMiniAppAndTokensAreRandom()
    {
        var a = MiniAppSecrets.NewToken();
        Assert.NotEqual(a, MiniAppSecrets.NewToken());
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.Equal($"https://zalo.me/s/123/?qr={a}", MiniAppSecrets.QrLink("123", a));
        Assert.Equal(32, MiniAppSecrets.TokenHash(a).Length);
        Assert.Equal("…678", MiniAppPhone.Masked(AnnPhone));
    }

    [Fact]
    public async Task QrNeedsAVerifiedPhoneAndConfiguration()
    {
        var rig = new Rig();
        Assert.Equal(MiniAppAuth.VerifyPhoneFirst, (await rig.Auth.CreateQrAsync(Ann, MiniAppPortal.Portal)).Error);
        rig.Verify(Ann, BobPhone);   // verified another number than the account's current one
        Assert.Equal(MiniAppAuth.VerifyPhoneFirst, (await rig.Auth.CreateQrAsync(Ann, MiniAppPortal.Portal)).Error);
        rig.Verify(Ann, AnnPhone);
        Assert.True((await rig.Auth.CreateQrAsync(Ann, MiniAppPortal.Portal)).Ok);
        Assert.Equal(MiniAppAuth.NotConfigured, (await new Rig(configured: false).Auth.CreateQrAsync(Ann, MiniAppPortal.Portal)).Error);
        Assert.All(rig.Store.Qrs, q => Assert.Equal(rig.Clock.Now.UtcDateTime + MiniAppAuth.QrLifetime, q.Expires));
    }

    [Fact]
    public async Task QrOpensOnlyForTheAccountsOwnZaloPhoneAndOnlyOnce()
    {
        var rig = new Rig();
        rig.Verify(Ann, AnnPhone);
        var token = await rig.QrToken(Ann);

        var stranger = await rig.Auth.QrSignInAsync(token, "zalo:Z-BOB", "phone:0987654321");
        Assert.Equal(MiniAppAuth.PhoneMismatch, stranger.Error);
        Assert.Null(stranger.Session);
        Assert.Empty(rig.Store.Links);
        // The QR was used up by that attempt: the owner makes a new one.
        Assert.False((await rig.Auth.QrSignInAsync(token, "zalo:Z-ANN", "phone:0912345678")).Ok);

        var again = await rig.QrToken(Ann);
        var ok = await rig.Auth.QrSignInAsync(again, "zalo:Z-ANN", "phone:0912345678");
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(Ann, ok.Account!.UserId);
        Assert.Equal(Ann, (await rig.Auth.ValidateAsync(ok.Session))!.UserId);
        Assert.Single(rig.Store.Links, l => l.Zalo == "Z-ANN" && l.User == Ann && l.Source == MiniAppLinkSource.Qr);
        Assert.False((await rig.Auth.QrSignInAsync(again, "zalo:Z-ANN", "phone:0912345678")).Ok);
    }

    [Fact]
    public async Task QrExpiresAfterFiveMinutes()
    {
        var rig = new Rig();
        rig.Verify(Ann, AnnPhone);
        var token = await rig.QrToken(Ann);
        rig.Clock.Now += MiniAppAuth.QrLifetime + TimeSpan.FromSeconds(1);
        Assert.Contains("hết hạn", (await rig.Auth.QrSignInAsync(token, "zalo:Z-ANN", "phone:0912345678")).Error);
    }

    [Fact]
    public async Task PasswordSignInNeedsTheRightPasswordAndTheSamePhone()
    {
        var rig = new Rig();
        Assert.Equal(MiniAppAuth.BadCredentials, (await rig.Auth.PasswordSignInAsync("ann", "wrong", "zalo:Z-ANN", "phone:0912345678")).Error);
        Assert.Equal(MiniAppAuth.BadCredentials, (await rig.Auth.PasswordSignInAsync("nobody", "Pass@123", "zalo:Z-ANN", "phone:0912345678")).Error);
        Assert.Equal(MiniAppAuth.PhoneMismatch, (await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-BOB", "phone:0987654321")).Error);
        Assert.Contains("số điện thoại", (await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", null)).Error);
        Assert.Empty(rig.Store.Links);

        var ok = await rig.Auth.PasswordSignInAsync("ANN", "Pass@123", "zalo:Z-ANN", "phone:0912345678");
        Assert.True(ok.Ok, ok.Error);
        Assert.Single(rig.Store.Links, l => l.Source == MiniAppLinkSource.Login);
        // Zalo proved the number and it is the account's: it now counts as verified.
        Assert.Equal((AnnPhone, VerifiedPhoneMethod.ZaloMiniApp), rig.Store.Verified[Ann]);
    }

    [Fact]
    public async Task PasswordSignInIsLimitedAndRefusesAccountsThatMustChangePassword()
    {
        var rig = new Rig();
        rig.Store.Accounts[Bob] = rig.Store.Accounts[Bob] with { MustChangePassword = true };
        Assert.Contains("đổi mật khẩu", (await rig.Auth.PasswordSignInAsync("bob", "Pass@456", "zalo:Z-BOB", "phone:0987654321")).Error);
        for (var i = 0; i < 4; i++) await rig.Auth.PasswordSignInAsync("ann", "wrong", "zalo:Z-ANN", "phone:0912345678");
        Assert.Contains("quá nhiều lần", (await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", "phone:0912345678")).Error);
    }

    [Fact]
    public async Task ReopeningGoesStraightInOrOffersTheLinkedAccounts()
    {
        var rig = new Rig();
        Assert.Null((await rig.Auth.ResumeAsync("zalo:Z-1")).Session);   // not linked: sign-in screen
        Assert.False((await rig.Auth.ResumeAsync("bad-token")).Ok);

        rig.Store.Accounts[Bob] = rig.Store.Accounts[Bob] with { Phone = "0912345678" };   // same person, two accounts
        await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-1", "phone:0912345678");
        var one = await rig.Auth.ResumeAsync("zalo:Z-1");
        Assert.Equal(Ann, (await rig.Auth.ValidateAsync(one.Session))!.UserId);

        await rig.Auth.PasswordSignInAsync("bob", "Pass@456", "zalo:Z-1", "phone:0912345678");
        var two = await rig.Auth.ResumeAsync("zalo:Z-1");
        Assert.Null(two.Session);
        Assert.Equal([Ann, Bob], two.Accounts!.Select(a => a.UserId).Order().ToArray());
        var picked = await rig.Auth.SelectAsync("zalo:Z-1", Bob);
        Assert.Equal(Bob, (await rig.Auth.ValidateAsync(picked.Session))!.UserId);
        Assert.False((await rig.Auth.SelectAsync("zalo:Z-OTHER", Bob)).Ok);   // another Zalo user cannot pick it
    }

    [Fact]
    public async Task ChangingTheAccountPhoneEndsTheLink()
    {
        var rig = new Rig();
        var ok = await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", "phone:0912345678");
        rig.Store.Accounts[Ann] = rig.Store.Accounts[Ann] with { Phone = "0900000000" };
        Assert.Null(await rig.Auth.ValidateAsync(ok.Session));
        Assert.Null((await rig.Auth.ResumeAsync("zalo:Z-ANN")).Session);
    }

    [Fact]
    public async Task SessionsExpireCannotBeForgedAndEndAtSignOutOrUnlink()
    {
        var rig = new Rig();
        var ok = await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", "phone:0912345678");
        Assert.Null(await rig.Auth.ValidateAsync(ok.Session + "x"));
        Assert.Null(await rig.Auth.ValidateAsync("not-a-session"));
        Assert.Null(await rig.Auth.ValidateAsync(null));

        var session = (await rig.Auth.ValidateAsync(ok.Session))!;
        await rig.Auth.SignOutAsync(session);
        Assert.Null(await rig.Auth.ValidateAsync(ok.Session));

        var again = await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", "phone:0912345678");
        await rig.Store.RevokeLinksAsync(Ann, Ann);   // "Hủy liên kết Zalo" on the web
        Assert.Null(await rig.Auth.ValidateAsync(again.Session));

        var late = await rig.Auth.PasswordSignInAsync("ann", "Pass@123", "zalo:Z-ANN", "phone:0912345678");
        rig.Clock.Now += MiniAppAuth.SessionLifetime + TimeSpan.FromSeconds(1);
        Assert.Null(await rig.Auth.ValidateAsync(late.Session));
    }

    [Fact]
    public async Task OtpGoesToTheAccountsOwnPhoneAndOnlyItsHashIsKept()
    {
        var rig = new Rig();
        var request = await rig.Otp.RequestAsync(Ann, "1.2.3.4");
        Assert.True(request.Ok, request.Error);
        var (phone, template, data) = Assert.Single(rig.Sender.Sent);
        Assert.Equal(AnnPhone, phone);
        Assert.Equal("445566", template);
        var code = data["otp"];
        Assert.Matches("^[0-9]{6}$", code);
        var stored = Assert.Single(rig.Store.Otps);
        Assert.Equal(MiniAppSecrets.OtpHash(stored.Id, code), stored.CodeHash);
        Assert.DoesNotContain(System.Text.Encoding.ASCII.GetBytes(code), new[] { stored.CodeHash });
        Assert.Equal(rig.Clock.Now.UtcDateTime + PhoneVerification.Lifetime, stored.ExpiresAtUtc);

        Assert.False(await rig.Otp.IsVerifiedAsync(Ann));
        Assert.Null(await rig.Otp.VerifyAsync(Ann, request.ChallengeId!.Value, code));
        Assert.Equal((AnnPhone, VerifiedPhoneMethod.ZnsOtp), rig.Store.Verified[Ann]);
        Assert.True(await rig.Otp.IsVerifiedAsync(Ann));
        Assert.NotNull(await rig.Otp.VerifyAsync(Ann, request.ChallengeId!.Value, code));   // used once only
        Assert.NotNull(await rig.Otp.VerifyAsync(Bob, request.ChallengeId!.Value, code));   // never for another account
    }

    [Fact]
    public async Task OtpAllowsFiveTriesWithinFiveMinutes()
    {
        var rig = new Rig();
        var id = (await rig.Otp.RequestAsync(Ann, null)).ChallengeId!.Value;
        var code = rig.Sender.Sent[0].Data["otp"];
        var wrong = code == "000000" ? "111111" : "000000";
        Assert.Contains("còn 4 lần", await rig.Otp.VerifyAsync(Ann, id, wrong));
        for (var i = 0; i < 4; i++) await rig.Otp.VerifyAsync(Ann, id, wrong);
        Assert.Contains("quá nhiều lần", await rig.Otp.VerifyAsync(Ann, id, code));
        Assert.False(rig.Store.Verified.ContainsKey(Ann));

        rig.Clock.Now += TimeSpan.FromMinutes(2);
        var next = (await rig.Otp.RequestAsync(Ann, null)).ChallengeId!.Value;
        rig.Clock.Now += PhoneVerification.Lifetime + TimeSpan.FromSeconds(1);
        Assert.Contains("hết hạn", await rig.Otp.VerifyAsync(Ann, next, rig.Sender.Sent[1].Data["otp"]));
    }

    [Fact]
    public async Task OtpIsLimitedToOneAMinuteAndFiveAnHour()
    {
        var rig = new Rig();
        Assert.True((await rig.Otp.RequestAsync(Ann, null)).Ok);
        Assert.Contains("giây", (await rig.Otp.RequestAsync(Ann, null)).Error);
        for (var i = 0; i < 4; i++) { rig.Clock.Now += TimeSpan.FromMinutes(2); Assert.True((await rig.Otp.RequestAsync(Ann, null)).Ok); }
        rig.Clock.Now += TimeSpan.FromMinutes(2);
        Assert.Contains("quá nhiều mã", (await rig.Otp.RequestAsync(Ann, null)).Error);
        Assert.Equal(5, rig.Sender.Sent.Count);
    }

    [Fact]
    public async Task OtpNeedsAMobilePhoneOnTheAccount()
    {
        var rig = new Rig();
        rig.Store.Accounts[Ann] = rig.Store.Accounts[Ann] with { Phone = "028 3822 1234" };
        Assert.Contains("chưa có số điện thoại di động", (await rig.Otp.RequestAsync(Ann, null)).Error);
        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public void MigrationIsGuardedAndHasVerifyAndRollback()
    {
        var root = FindRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "038-zalo-mini-app-auth.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", migration);
        Assert.DoesNotContain("DROP ", migration);
        Assert.DoesNotContain("UPDATE ", migration);
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "065-verify-zalo-mini-app-auth.sql")));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "066-rollback-zalo-mini-app-auth.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", rollback);
        foreach (var table in new[] { "UserVerifiedPhones", "PhoneOtpChallenges", "ZaloMiniAppQrTokens", "ZaloMiniAppLinks" })
        {
            Assert.Contains($"CREATE TABLE dbo.{table}", migration);
            Assert.Contains($"DROP TABLE dbo.{table}", rollback);
        }
    }

    [Fact]
    public void ApiNeverTakesAPhoneNumberFromTheClient()
    {
        // The phone always comes from Zalo (phone token decoded server side), never from the request body.
        foreach (var type in typeof(TACustomerPortal.Web.Controllers.ZaloMiniAppController).GetNestedTypes())
            Assert.DoesNotContain(type.GetProperties(), p => p.Name.Equals("Phone", StringComparison.OrdinalIgnoreCase));
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "TACustomerPortal.Web", "Controllers", "ZaloMiniAppController.cs"));
        Assert.Contains("EnableRateLimiting(\"miniapp\")", source);
        Assert.Contains("EnableCors(MiniAppCors.Policy)", source);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }
}
