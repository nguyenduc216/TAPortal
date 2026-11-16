using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Notifications;

namespace TAPortal.Web.Services;

// Thông báo đẩy (operator 2026-11-16, migration 059): T.A Portal installs as an app (wwwroot/sw.js, /manifest.webmanifest)
// and each staff member may allow notifications on each browser/device. The browser gives a subscription (endpoint of its
// push service + keys); the portal signs requests with its VAPID key (RFC 8292) and encrypts the message (RFC 8291,
// aes128gcm), so no third-party library is needed. Works in Chrome/Edge/Firefox on computers and Android, and in Safari
// on iPhone/iPad once T.A Portal is added to the home screen (iOS 16.4+).
public sealed record WebPushSubscription(Guid Id, Guid UserId, string Endpoint, string P256dh, string Auth, string? Device, DateTime LastSeenAt);
public sealed record WebPushMessage(string Title, string Body, string? Url = null, string? Tag = null);
public sealed record WebPushVapid(string PublicKey, byte[] PrivateKey, string Subject);
public sealed record WebPushSendResult(int Sent, int Failed, int Removed);

public static class WebPushPolicy
{
    public const int MaxDevicesPerUser = 20, MaxBodyChars = 300, MaxTitleChars = 80;

    // Only the browsers' own push services: a subscription is data sent by the browser, and the server must never be
    // made to call another address (internal hosts, arbitrary sites).
    private static readonly string[] Hosts = ["fcm.googleapis.com", "web.push.apple.com", "updates.push.services.mozilla.com"];
    private static readonly string[] HostSuffixes = [".push.apple.com", ".push.services.mozilla.com", ".notify.windows.com"];

    public static bool IsAllowedEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 800) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return Hosts.Contains(host) || HostSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal));
    }

    // p256dh: an uncompressed P-256 point (65 bytes); auth: 16 bytes. Both base64url as the browser gives them.
    public static bool IsValidKeys(string? p256dh, string? auth) =>
        WebPushCrypto.TryDecode(p256dh, out var key) && key.Length == 65 && key[0] == 4 &&
        WebPushCrypto.TryDecode(auth, out var secret) && secret.Length == 16;

    public static string? Device(string? userAgent)
    {
        var ua = userAgent ?? "";
        var os = ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone" : ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("Android", StringComparison.Ordinal) ? "Android" : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS", StringComparison.Ordinal) ? "macOS" : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux" : null;
        var browser = ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge" : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("SamsungBrowser", StringComparison.Ordinal) ? "Samsung Internet" : ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("CriOS", StringComparison.Ordinal) || ua.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari" : null;
        return os is null && browser is null ? null : string.Join(" · ", new[] { browser, os }.Where(x => x is not null));
    }

    public static string Payload(WebPushMessage m) => JsonSerializer.Serialize(new
    {
        title = Clip(m.Title, MaxTitleChars),
        body = Clip(m.Body, MaxBodyChars),
        url = m.Url is { } u && u.StartsWith('/') && !u.StartsWith("//", StringComparison.Ordinal) ? u : "/",
        tag = m.Tag
    });

    private static string Clip(string? s, int max) { var t = (s ?? "").Trim(); return t.Length > max ? t[..(max - 1)] + "…" : t; }
}

public static class WebPushCrypto
{
    public const int RecordSize = 4096;

    public static string Encode(ReadOnlySpan<byte> data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? text, out byte[] data)
    {
        data = [];
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200) return false;
        var s = text.Trim().Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { data = Convert.FromBase64String(s); return true; } catch (FormatException) { return false; }
    }

    // A new VAPID key pair: (public key as the uncompressed point, base64url; private scalar d).
    public static (string PublicKey, byte[] PrivateKey) NewVapidKeys()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        return (Encode(Point(p.Q)), p.D!);
    }

    // "Authorization: vapid t=<JWT ES256>, k=<public key>" for the push service at the endpoint's origin.
    public static string VapidHeader(string endpoint, WebPushVapid vapid, DateTimeOffset now)
    {
        var uri = new Uri(endpoint);
        var header = Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new { aud = uri.GetLeftPart(UriPartial.Authority), exp = now.AddHours(12).ToUnixTimeSeconds(), sub = vapid.Subject }));
        var input = Encoding.ASCII.GetBytes(header + "." + claims);
        using var ec = ECDsa.Create(Parameters(vapid));
        var signature = ec.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={header}.{claims}.{Encode(signature)}, k={vapid.PublicKey}";
    }

    // RFC 8291 + RFC 8188 (aes128gcm), one record: salt(16) | rs(4) | idlen(1) | keyid(65) | ciphertext.
    public static byte[] Encrypt(byte[] plaintext, byte[] receiverKey, byte[] authSecret)
    {
        if (plaintext.Length > RecordSize - 16 - 1 - 86) throw new ArgumentException("Push message too long.", nameof(plaintext));
        using var sender = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var senderKey = Point(sender.ExportParameters(false).Q);
        using var receiver = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = receiverKey[1..33], Y = receiverKey[33..65] }
        });
        var shared = sender.DeriveRawSecretAgreement(receiver.PublicKey);
        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), receiverKey, senderKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, authSecret, keyInfo);
        var salt = RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 2;                                    // last record delimiter, no padding
        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, padded, cipher, tag);

        var head = new byte[16 + 4 + 1];
        salt.CopyTo(head, 0);
        head[16] = 0; head[17] = 0; head[18] = RecordSize >> 8 & 0xFF; head[19] = RecordSize & 0xFF;
        head[20] = (byte)senderKey.Length;
        return Concat(head, senderKey, cipher, tag);
    }

    private static ECParameters Parameters(WebPushVapid vapid)
    {
        TryDecode(vapid.PublicKey, out var q);
        return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = vapid.PrivateKey, Q = new ECPoint { X = q[1..33], Y = q[33..65] } };
    }

    private static byte[] Point(ECPoint q) => Concat([4], q.X!, q.Y!);

    private static byte[] Concat(params byte[][] parts)
    {
        var all = new byte[parts.Sum(p => p.Length)];
        var at = 0;
        foreach (var p in parts) { p.CopyTo(all, at); at += p.Length; }
        return all;
    }
}

public static class WebPushSql
{
    public const string Keys = "SELECT PublicKey,PrivateKey,Subject FROM dbo.WebPushKeys WHERE Id=1;";
    // Two first requests at once: the second insert is skipped and both read the stored pair.
    public const string AddKeys = """
IF NOT EXISTS (SELECT 1 FROM dbo.WebPushKeys WITH (UPDLOCK, HOLDLOCK) WHERE Id=1)
 INSERT dbo.WebPushKeys(Id,PublicKey,PrivateKey,Subject) VALUES(1,@public,@private,@subject);
SELECT PublicKey,PrivateKey,Subject FROM dbo.WebPushKeys WHERE Id=1;
""";
    // A device moves to whoever signed in last on it.
    public const string Save = """
UPDATE dbo.WebPushSubscriptions WITH (UPDLOCK, SERIALIZABLE) SET UserId=@user,P256dh=@p256dh,Auth=@auth,Device=@device,LastSeenAt=SYSUTCDATETIME(),FailCount=0 WHERE Endpoint=@endpoint;
IF @@ROWCOUNT=0
BEGIN
 IF (SELECT COUNT(1) FROM dbo.WebPushSubscriptions WHERE UserId=@user) >= @max
  DELETE FROM dbo.WebPushSubscriptions WHERE Id=(SELECT TOP 1 Id FROM dbo.WebPushSubscriptions WHERE UserId=@user ORDER BY LastSeenAt);
 INSERT dbo.WebPushSubscriptions(UserId,Endpoint,P256dh,Auth,Device) VALUES(@user,@endpoint,@p256dh,@auth,@device);
END
""";
    public const string Remove = "DELETE FROM dbo.WebPushSubscriptions WHERE Endpoint=@endpoint AND UserId=@user;";
    public const string RemoveGone = "DELETE FROM dbo.WebPushSubscriptions WHERE Id=@id;";
    public const string ForUser = "SELECT Id,UserId,Endpoint,P256dh,Auth,Device,LastSeenAt FROM dbo.WebPushSubscriptions WHERE UserId=@user ORDER BY LastSeenAt DESC;";
    public const string Sent = "UPDATE dbo.WebPushSubscriptions SET LastSentAt=SYSUTCDATETIME(),FailCount=0 WHERE Id=@id;";
    public const string Failed = "UPDATE dbo.WebPushSubscriptions SET FailCount=FailCount+1 WHERE Id=@id;";
}

public interface IWebPushStore
{
    // null when the push tables are not available (migration 059 not run).
    Task<WebPushVapid?> VapidAsync(string subject);
    Task<bool> SaveAsync(Guid user, string endpoint, string p256dh, string auth, string? device);
    Task RemoveAsync(Guid user, string endpoint);
    Task<IReadOnlyList<WebPushSubscription>?> ForUserAsync(Guid user);
    Task MarkAsync(Guid subscription, bool sent, bool gone);
}

public sealed class SqlWebPushStore(IConfiguration configuration, ISecretProtector protector, ILogger<SqlWebPushStore> logger) : IWebPushStore
{
    private static WebPushVapid? cached;
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<WebPushVapid?> VapidAsync(string subject)
    {
        if (cached is not null) return cached;
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            var stored = await ReadKeys(new SqlCommand(WebPushSql.Keys, cn));
            if (stored is null)
            {
                var (pub, priv) = WebPushCrypto.NewVapidKeys();
                var cmd = new SqlCommand(WebPushSql.AddKeys, cn);
                cmd.Parameters.Add("@public", System.Data.SqlDbType.VarChar, 100).Value = pub;
                cmd.Parameters.Add("@private", System.Data.SqlDbType.VarBinary, -1).Value = protector.Protect(new Dictionary<string, string> { ["d"] = Convert.ToBase64String(priv) });
                cmd.Parameters.Add("@subject", System.Data.SqlDbType.NVarChar, 200).Value = subject;
                stored = await ReadKeys(cmd);
                logger.LogInformation("Web push VAPID key pair ready.");
            }
            return cached = stored;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Web push tables unavailable.");
            return null;
        }
    }

    private async Task<WebPushVapid?> ReadKeys(SqlCommand cmd)
    {
        await using (cmd)
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            var d = Convert.FromBase64String(protector.Unprotect((byte[])r.GetValue(1))["d"]);
            return new WebPushVapid(r.GetString(0), d, r.GetString(2));
        }
    }

    public async Task<bool> SaveAsync(Guid user, string endpoint, string p256dh, string auth, string? device)
    {
        try
        {
            await Execute(WebPushSql.Save, ("@user", user), ("@endpoint", endpoint), ("@p256dh", p256dh), ("@auth", auth),
                ("@device", (object?)device ?? DBNull.Value), ("@max", WebPushPolicy.MaxDevicesPerUser));
            return true;
        }
        catch (SqlException ex) { logger.LogWarning(ex, "Web push subscription not saved."); return false; }
    }

    public Task RemoveAsync(Guid user, string endpoint) => Execute(WebPushSql.Remove, ("@user", user), ("@endpoint", endpoint));

    public async Task<IReadOnlyList<WebPushSubscription>?> ForUserAsync(Guid user)
    {
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(WebPushSql.ForUser, cn);
            cmd.Parameters.AddWithValue("@user", user);
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<WebPushSubscription>();
            while (await r.ReadAsync())
                list.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTime(6)));
            return list;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Web push tables unavailable.");
            return null;
        }
    }

    public Task MarkAsync(Guid subscription, bool sent, bool gone) =>
        Execute(gone ? WebPushSql.RemoveGone : sent ? WebPushSql.Sent : WebPushSql.Failed, ("@id", subscription));

    private async Task Execute(string sql, params (string Name, object Value)[] args)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            // Endpoint and keys are varchar columns (index seek); the device label is Unicode.
            if (value is string s) cmd.Parameters.Add(name, name == "@device" ? System.Data.SqlDbType.NVarChar : System.Data.SqlDbType.VarChar, Math.Max(s.Length, 1)).Value = s;
            else if (name == "@device") cmd.Parameters.Add(name, System.Data.SqlDbType.NVarChar, 100).Value = value;
            else cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}

public interface IWebPushSender
{
    // Sends to every device of the user; devices the push service no longer knows are removed.
    Task<WebPushSendResult> SendAsync(Guid user, WebPushMessage message, CancellationToken ct = default);
}

public sealed class WebPushSender(IWebPushStore store, IHttpClientFactory http, IConfiguration configuration, ILogger<WebPushSender> logger) : IWebPushSender
{
    public const string ClientName = "WebPush";
    public const string DefaultSubject = "https://portal.tuvangiaiphapta.vn";

    public async Task<WebPushSendResult> SendAsync(Guid user, WebPushMessage message, CancellationToken ct = default)
    {
        var vapid = await store.VapidAsync(Subject(configuration));
        var devices = vapid is null ? null : await store.ForUserAsync(user);
        if (vapid is null || devices is null || devices.Count == 0) return new(0, 0, 0);
        var payload = Encoding.UTF8.GetBytes(WebPushPolicy.Payload(message));
        int sent = 0, failed = 0, removed = 0;
        var client = http.CreateClient(ClientName);
        foreach (var d in devices)
        {
            if (!WebPushPolicy.IsAllowedEndpoint(d.Endpoint) || !WebPushCrypto.TryDecode(d.P256dh, out var key) || !WebPushCrypto.TryDecode(d.Auth, out var auth))
            {
                await store.MarkAsync(d.Id, false, true); removed++; continue;
            }
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, d.Endpoint) { Content = new ByteArrayContent(WebPushCrypto.Encrypt(payload, key, auth)) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                request.Content.Headers.ContentEncoding.Add("aes128gcm");
                request.Headers.TryAddWithoutValidation("TTL", "86400");
                request.Headers.TryAddWithoutValidation("Urgency", "high");
                request.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.VapidHeader(d.Endpoint, vapid, DateTimeOffset.UtcNow));
                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) { await store.MarkAsync(d.Id, true, false); sent++; }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { await store.MarkAsync(d.Id, false, true); removed++; }
                else
                {
                    logger.LogWarning("WEB_PUSH_REJECTED {Status} by {Host}", (int)response.StatusCode, new Uri(d.Endpoint).Host);
                    await store.MarkAsync(d.Id, false, false); failed++;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or ArgumentException)
            {
                logger.LogWarning(ex, "WEB_PUSH_FAILED to {Host}", new Uri(d.Endpoint).Host);
                await store.MarkAsync(d.Id, false, false); failed++;
            }
        }
        return new(sent, failed, removed);
    }

    public static string Subject(IConfiguration configuration) =>
        configuration["WebPush:Subject"] is { Length: > 0 } s ? s : DefaultSubject;
}
