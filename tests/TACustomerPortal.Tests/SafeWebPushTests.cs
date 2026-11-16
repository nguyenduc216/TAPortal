extern alias admin;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// CONTRACTS-AND-TASKS: T.A Portal installs as an app and receives web push (operator 2026-11-16, migration 059). The
// encryption is checked by decrypting as the browser would (RFC 8291, written out with HMAC here, independent of the
// HKDF calls in WebPushCrypto) and the VAPID token by verifying its ES256 signature. No push service is called.
public sealed class SafeWebPushTests
{
    [Fact]
    public void PushMessagesAreEncryptedForTheBrowserAndSignedWithTheVapidKey()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = browser.ExportParameters(false).Q;
        var browserKey = new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray();
        var auth = RandomNumberGenerator.GetBytes(16);
        var payload = Encoding.UTF8.GetBytes(S.WebPushPolicy.Payload(new("Việc mới", "Bạn được giao: Lập tờ khai thuế GTGT", "/Work/Details/1", "t1")));

        var body = S.WebPushCrypto.Encrypt(payload, browserKey, auth);
        Assert.Equal(payload, Decrypt(body, browser, browserKey, auth));
        Assert.NotEqual(body, S.WebPushCrypto.Encrypt(payload, browserKey, auth));                       // new salt and sender key each time

        var (publicKey, privateKey) = S.WebPushCrypto.NewVapidKeys();
        var vapid = new S.WebPushVapid(publicKey, privateKey, "https://portal.tuvangiaiphapta.vn");
        var header = S.WebPushCrypto.VapidHeader("https://fcm.googleapis.com/fcm/send/abc:def", vapid, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var m = System.Text.RegularExpressions.Regex.Match(header, @"^vapid t=([\w-]+)\.([\w-]+)\.([\w-]+), k=([\w-]+)$");
        Assert.True(m.Success, header);
        Assert.Equal(publicKey, m.Groups[4].Value);
        Assert.True(S.WebPushCrypto.TryDecode(m.Groups[2].Value, out var claimsJson));
        using var claims = JsonDocument.Parse(claimsJson);
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(1_800_000_000 + 12 * 3600, claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("https://portal.tuvangiaiphapta.vn", claims.RootElement.GetProperty("sub").GetString());
        S.WebPushCrypto.TryDecode(publicKey, out var point);
        S.WebPushCrypto.TryDecode(m.Groups[3].Value, out var signature);
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..65] } });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes(m.Groups[1].Value + "." + m.Groups[2].Value), signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void OnlyTheBrowsersPushServicesAreCalled()
    {
        Assert.True(S.WebPushPolicy.IsAllowedEndpoint("https://fcm.googleapis.com/fcm/send/abc"));                   // Chrome, Edge (Android)
        Assert.True(S.WebPushPolicy.IsAllowedEndpoint("https://updates.push.services.mozilla.com/wpush/v2/x"));      // Firefox
        Assert.True(S.WebPushPolicy.IsAllowedEndpoint("https://web.push.apple.com/QO_x"));                           // Safari
        Assert.True(S.WebPushPolicy.IsAllowedEndpoint("https://wns2-par02p.notify.windows.com/w/?token=x"));        // Edge (Windows)
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("http://fcm.googleapis.com/fcm/send/abc"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://fcm.googleapis.com:8443/x"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://localhost/x"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://10.0.0.5/x"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://evil.com/fcm.googleapis.com"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://fcm.googleapis.com.evil.com/x"));
        Assert.False(S.WebPushPolicy.IsAllowedEndpoint("https://user@fcm.googleapis.com/x"));

        using var ec = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ec.ExportParameters(false).Q;
        var key = S.WebPushCrypto.Encode(new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray());
        Assert.True(S.WebPushPolicy.IsValidKeys(key, S.WebPushCrypto.Encode(new byte[16])));
        Assert.False(S.WebPushPolicy.IsValidKeys(key, S.WebPushCrypto.Encode(new byte[8])));
        Assert.False(S.WebPushPolicy.IsValidKeys("not base64!", S.WebPushCrypto.Encode(new byte[16])));

        using var p = JsonDocument.Parse(S.WebPushPolicy.Payload(new("T", "B", "https://evil.com/")));
        Assert.Equal("/", p.RootElement.GetProperty("url").GetString());                                        // only paths of the portal
        Assert.Equal("Chrome · Android", S.WebPushPolicy.Device("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Mobile Safari/537.36"));
        Assert.Equal("Safari · iPhone", S.WebPushPolicy.Device("Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1"));
        Assert.Equal("Edge · Windows", S.WebPushPolicy.Device("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36 Edg/131.0"));
    }

    [Fact]
    public void Migration059CreatesThePushTablesOnly()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "059-web-push.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "105-verify-web-push.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "106-rollback-web-push.sql"));
        Assert.All(new[] { migration, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|DROP)\b", migration);
        Assert.Contains("CREATE UNIQUE INDEX UX_WebPushSubscriptions_Endpoint", migration);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.True(migration.All(ch => ch < 128), "migration text must be ASCII (sqlcmd code page)");
        Assert.True(verify.All(ch => ch < 128) && rollback.All(ch => ch < 128));
        Assert.Contains("UPDLOCK, HOLDLOCK", S.WebPushSql.AddKeys);
        Assert.Contains("@max", S.WebPushSql.Save);
    }

    [Fact]
    public async Task ThePortalIsInstallableFromAnyPage()
    {
        using var f = new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" })));
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var r = await c.GetAsync("/manifest.webmanifest");                                                      // no sign-in needed
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/manifest+json", r.Content.Headers.ContentType?.MediaType);
        using var manifest = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("standalone", manifest.RootElement.GetProperty("display").GetString());
        Assert.Equal("/", manifest.RootElement.GetProperty("start_url").GetString());
        var icons = manifest.RootElement.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, i => i.GetProperty("sizes").GetString() == "512x512" && i.GetProperty("purpose").GetString() == "maskable");
        foreach (var src in icons.Select(i => i.GetProperty("src").GetString()!).Append("/images/notify-badge.png"))
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(src)).StatusCode);

        var sw = await c.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, sw.StatusCode);
        var swText = await sw.Content.ReadAsStringAsync();
        Assert.Contains("showNotification", swText);
        Assert.DoesNotContain("caches.open", swText);                                                           // admin pages are never cached

        var login = await c.GetStringAsync("/account/login");
        Assert.Contains("rel=\"manifest\" href=\"/manifest.webmanifest\"", login);
        Assert.Contains("ta-app.js", login);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync("/push/key")).StatusCode);                          // the rest needs a sign-in
    }

    [Fact]
    public void TaskChangesBecomeOneNotificationPerPersonAndTask()
    {
        Guid task = Guid.NewGuid(), an = Guid.NewGuid(), boss = Guid.NewGuid();
        var at = new DateTime(2026, 11, 16, 3, 0, 0);
        S.WorkPushActivity A(string kind, string? body, Guid to, int sec, string? from = null, string? st = null) =>
            new(Guid.NewGuid(), kind, body, from, st, "Nguyễn Văn An", task, "CV000030", "Lập tờ khai thuế", to, at.AddSeconds(sec));

        var messages = S.WorkPushPolicy.Messages([
            A("CHECKLIST", "Hoàn thành: Thu thập hóa đơn", boss, 1),
            A("CHECKLIST", "Đổi thứ tự checklist", boss, 2),                       // not news
            A("COMMENT", "Đã nộp, anh kiểm tra giúp", boss, 3),
            A("STATUS", null, an, 4, "NEW", "IN_PROGRESS")]);
        Assert.Equal(2, messages.Count);
        var toBoss = messages.Single(m => m.User == boss).Message;
        Assert.Equal("Nguyễn Văn An · CV000030", toBoss.Title);
        Assert.Equal("(+1 cập nhật khác) Đã nộp, anh kiểm tra giúp", toBoss.Body);
        Assert.Equal($"/Work/Details/{task}", toBoss.Url);
        Assert.Equal("task-" + task.ToString("N"), toBoss.Tag);                                                 // replaces the older one on the phone
        Assert.StartsWith("CV000030: ", messages.Single(m => m.User == an).Message.Title);
        Assert.Contains("Việc mới: CV000030", S.WorkPushPolicy.Message(A("CREATED", null, an, 5)).Title);

        var due = S.WorkPushPolicy.Reminder(new(an, 2, 1, 0))!;
        Assert.Equal("Có việc quá hạn cần xử lý", due.Title);
        Assert.Equal("2 việc quá hạn · 1 việc đến hạn hôm nay.", due.Body);
        Assert.Null(S.WorkPushPolicy.Reminder(new(an, 0, 0, 0)));
        Assert.False(S.WorkPushPolicy.ReminderDue(new DateTime(2026, 11, 16, 0, 59, 0), null));                 // 07:59 in Vietnam
        Assert.True(S.WorkPushPolicy.ReminderDue(new DateTime(2026, 11, 16, 1, 0, 0), null));                   // 08:00
        Assert.False(S.WorkPushPolicy.ReminderDue(new DateTime(2026, 11, 16, 5, 0, 0), new DateOnly(2026, 11, 16)));   // once a day
        Assert.True(S.WorkPushPolicy.ReminderDue(new DateTime(2026, 11, 16, 5, 0, 0), new DateOnly(2026, 11, 15)));

        Assert.Contains("p.UserId<>a.CreatedBy", S.WorkPushSql.Activities);                                    // never to the person who acted
        Assert.Contains("WebPushSubscriptions s WHERE s.UserId=p.UserId", S.WorkPushSql.Activities);           // only who turned it on
        Assert.Contains("Status IN ('NEW','IN_PROGRESS')", S.WorkPushSql.Due);
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "060-work-push-notifications.sql"));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", migration);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|DROP)", migration);
        Assert.True(migration.All(ch => ch < 128));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "107-verify-work-push-notifications.sql")));
        Assert.True(File.Exists(Path.Combine(root, "database", "scripts", "108-rollback-work-push-notifications.sql")));
    }

    // Browser side of RFC 8291 / RFC 8188, written out with HMAC-SHA256.
    private static byte[] Decrypt(byte[] body, ECDiffieHellman browser, byte[] browserKey, byte[] auth)
    {
        var salt = body[..16];
        var rs = body[16] << 24 | body[17] << 16 | body[18] << 8 | body[19];
        Assert.Equal(4096, rs);
        Assert.Equal(65, body[20]);
        var senderKey = body[21..86];
        using var sender = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = senderKey[1..33], Y = senderKey[33..65] } });
        var shared = browser.DeriveRawSecretAgreement(sender.PublicKey);
        var prkKey = HMACSHA256.HashData(auth, shared);
        var ikm = HMACSHA256.HashData(prkKey, (byte[])[.. "WebPush: info\0"u8.ToArray(), .. browserKey, .. senderKey, 1]);
        var prk = HMACSHA256.HashData(salt, ikm);
        var cek = HMACSHA256.HashData(prk, (byte[])[.. "Content-Encoding: aes128gcm\0"u8.ToArray(), 1])[..16];
        var nonce = HMACSHA256.HashData(prk, (byte[])[.. "Content-Encoding: nonce\0"u8.ToArray(), 1])[..12];
        var cipher = body[86..^16];
        var plain = new byte[cipher.Length];
        using (var gcm = new AesGcm(cek, 16)) gcm.Decrypt(nonce, cipher, body[^16..], plain);
        Assert.Equal(2, plain[^1]);                                                                            // last record
        return plain[..^1];
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }
}
