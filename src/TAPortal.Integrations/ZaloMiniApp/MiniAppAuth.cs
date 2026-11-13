using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.ZaloMiniApp;

/// <summary>Password check and per-username attempt limit of the hosting site (its PasswordService and login limiter).</summary>
public interface IMiniAppPasswordCheck
{
    bool TryAcquire(string username);
    bool Verify(string password, string? passwordHash);
}

public sealed record MiniAppAccountView(Guid UserId, string DisplayName, string Username);

/// <summary>
/// Outcome of a Mini App sign-in step: a session (signed in), accounts to pick from, nothing (show the sign-in screen),
/// or an error for the user.
/// </summary>
public sealed record MiniAppResult(bool Ok, string? Error = null, string? Session = null, MiniAppAccountView? Account = null, IReadOnlyList<MiniAppAccountView>? Accounts = null)
{
    public static MiniAppResult Fail(string error) => new(false, error);
}

public sealed record MiniAppSession(string ZaloUserId, Guid UserId, MiniAppAccount Account);

public sealed record MiniAppQr(bool Ok, string? Error, string? Link = null, DateTime? ExpiresAtUtc = null);

public sealed class MiniAppAuth(IMiniAppStore store, IZaloGraph graph, IMiniAppPasswordCheck passwords, IDataProtectionProvider protection,
    IOptions<ZaloMiniAppOptions> options, TimeProvider clock)
{
    public static readonly TimeSpan QrLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private const string Purpose = "TA.ZaloMiniApp.Session.v1";

    public const string NotConfigured = "Ứng dụng Zalo chưa được cấu hình, vui lòng liên hệ T.A.";
    public const string PhoneMismatch = "Số điện thoại Zalo của bạn không trùng với số điện thoại đăng ký của tài khoản.";
    public const string BadCredentials = "Tên đăng nhập hoặc mật khẩu không đúng.";
    public const string VerifyPhoneFirst = "Tài khoản chưa có số điện thoại đã xác minh. Vui lòng xác minh số điện thoại bằng mã OTP Zalo trước.";

    private IDataProtector Protector => protection.CreateProtector(Purpose);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The account's phone, when it is a mobile number and the user proved it is theirs.</summary>
    public async Task<string?> UsablePhoneAsync(MiniAppAccount account, CancellationToken ct = default) =>
        account.NormalizedPhone is string phone && await store.VerifiedPhoneAsync(account.UserId, ct) == phone ? phone : null;

    /// <summary>A one-time QR for the signed-in web user (TA Portal or Hoa don tu dong).</summary>
    public async Task<MiniAppQr> CreateQrAsync(Guid userId, string portal, CancellationToken ct = default)
    {
        if (!MiniAppPortal.Valid(portal)) throw new ArgumentException("Unknown portal", nameof(portal));
        if (!options.Value.SignInConfigured) return new(false, NotConfigured);
        if (await store.AccountAsync(userId, ct) is not { } account) return new(false, "Tài khoản không còn hoạt động.");
        if (await UsablePhoneAsync(account, ct) is null) return new(false, VerifyPhoneFirst);
        var token = MiniAppSecrets.NewToken();
        var expires = Now + QrLifetime;
        await store.CreateQrAsync(MiniAppSecrets.TokenHash(token), userId, portal, expires, ct);
        return new(true, null, MiniAppSecrets.QrLink(options.Value.AppId!, token), expires);
    }

    /// <summary>Way 1: the QR token from the web page, with the Zalo phone. The QR is used up even when the phone does not match.</summary>
    public async Task<MiniAppResult> QrSignInAsync(string? qrToken, string accessToken, string? phoneToken, CancellationToken ct = default)
    {
        if (!options.Value.SignInConfigured) return MiniAppResult.Fail(NotConfigured);
        if (string.IsNullOrWhiteSpace(qrToken) || qrToken.Length > 100 || string.IsNullOrWhiteSpace(phoneToken)) return MiniAppResult.Fail("Mã QR không hợp lệ.");
        var (identity, error) = await graph.IdentityAsync(accessToken, phoneToken, ct);
        if (identity?.Phone is not string phone) return MiniAppResult.Fail(error ?? "Không đọc được số điện thoại Zalo.");
        if (await store.ConsumeQrAsync(MiniAppSecrets.TokenHash(qrToken.Trim()), identity.ZaloUserId, Now, ct) is not Guid userId)
            return MiniAppResult.Fail("Mã QR đã hết hạn hoặc đã được dùng. Vui lòng tạo mã mới trên trang web.");
        if (await store.AccountAsync(userId, ct) is not { } account) return MiniAppResult.Fail("Tài khoản không còn hoạt động.");
        if (account.NormalizedPhone != phone) return MiniAppResult.Fail(PhoneMismatch);
        return await SignInAsync(identity.ZaloUserId, account, phone, MiniAppLinkSource.Qr, ct);
    }

    /// <summary>Way 2: username + password of TA Portal / Hoa don tu dong; the Zalo phone must still be the account's.</summary>
    public async Task<MiniAppResult> PasswordSignInAsync(string? username, string? password, string accessToken, string? phoneToken, CancellationToken ct = default)
    {
        if (!options.Value.SignInConfigured) return MiniAppResult.Fail(NotConfigured);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || username.Length > 256 || password.Length > 256) return MiniAppResult.Fail(BadCredentials);
        if (string.IsNullOrWhiteSpace(phoneToken)) return MiniAppResult.Fail("Vui lòng cho phép ứng dụng dùng số điện thoại Zalo.");
        if (!passwords.TryAcquire(username)) return MiniAppResult.Fail("Bạn đã thử đăng nhập quá nhiều lần. Vui lòng đợi 1 phút rồi thử lại.");
        var account = await store.AccountByUsernameAsync(username, ct);
        if (!passwords.Verify(password, account?.PasswordHash) || account is null) return MiniAppResult.Fail(BadCredentials);
        if (account.MustChangePassword) return MiniAppResult.Fail("Tài khoản cần đổi mật khẩu. Vui lòng đăng nhập trên trang web để đổi mật khẩu trước.");
        var (identity, error) = await graph.IdentityAsync(accessToken, phoneToken, ct);
        if (identity?.Phone is not string phone) return MiniAppResult.Fail(error ?? "Không đọc được số điện thoại Zalo.");
        if (account.NormalizedPhone != phone) return MiniAppResult.Fail(PhoneMismatch);
        return await SignInAsync(identity.ZaloUserId, account, phone, MiniAppLinkSource.Login, ct);
    }

    /// <summary>Opening the Mini App again: the accounts linked to this Zalo user whose phone is still the linked one.</summary>
    public async Task<MiniAppResult> ResumeAsync(string accessToken, CancellationToken ct = default)
    {
        if (!options.Value.SignInConfigured) return MiniAppResult.Fail(NotConfigured);
        var (identity, error) = await graph.IdentityAsync(accessToken, null, ct);
        if (identity is null) return MiniAppResult.Fail(error ?? "Phiên Zalo không hợp lệ.");
        var accounts = await LinkedAsync(identity.ZaloUserId, ct);
        return accounts.Count switch
        {
            0 => new(true),
            1 => Session(identity.ZaloUserId, accounts[0]),
            _ => new(true, Accounts: accounts.Select(View).ToList())
        };
    }

    /// <summary>Picks one of several linked accounts.</summary>
    public async Task<MiniAppResult> SelectAsync(string accessToken, Guid userId, CancellationToken ct = default)
    {
        if (!options.Value.SignInConfigured) return MiniAppResult.Fail(NotConfigured);
        var (identity, error) = await graph.IdentityAsync(accessToken, null, ct);
        if (identity is null) return MiniAppResult.Fail(error ?? "Phiên Zalo không hợp lệ.");
        var account = (await LinkedAsync(identity.ZaloUserId, ct)).FirstOrDefault(a => a.UserId == userId);
        return account is null ? MiniAppResult.Fail("Tài khoản này chưa được liên kết với Zalo của bạn.") : Session(identity.ZaloUserId, account);
    }

    /// <summary>The session behind an API call; null when expired, tampered, unlinked, or the account changed its phone.</summary>
    public async Task<MiniAppSession?> ValidateAsync(string? session, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(session) || session.Length > 4096) return null;
        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(Protector.Unprotect(session)); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or FormatException) { return null; }
        if (payload is not { Z.Length: > 0 } || payload.E <= Now.Ticks) return null;
        if (await store.AccountAsync(payload.U, ct) is not { } account) return null;
        var link = (await store.LinksAsync(payload.Z, ct)).FirstOrDefault(l => l.UserId == payload.U);
        if (link.Phone is null || account.NormalizedPhone != link.Phone) return null;
        return await store.TouchLinkAsync(payload.Z, payload.U, ct) ? new(payload.Z, payload.U, account) : null;
    }

    /// <summary>Sign-out in the Mini App: ends the link, so the next open asks for the QR or the password again.</summary>
    public async Task SignOutAsync(MiniAppSession session, CancellationToken ct = default) => await store.RevokeLinkAsync(session.ZaloUserId, session.UserId, ct);

    private async Task<MiniAppResult> SignInAsync(string zaloUserId, MiniAppAccount account, string phone, string source, CancellationToken ct)
    {
        await store.LinkAsync(zaloUserId, account.UserId, phone, source, ct);
        // Zalo proved the number belongs to this person and it is the account's: the phone counts as verified.
        if (await store.VerifiedPhoneAsync(account.UserId, ct) != phone) await store.SetVerifiedPhoneAsync(account.UserId, phone, VerifiedPhoneMethod.ZaloMiniApp, ct);
        return Session(zaloUserId, account);
    }

    private async Task<List<MiniAppAccount>> LinkedAsync(string zaloUserId, CancellationToken ct)
    {
        var accounts = new List<MiniAppAccount>();
        foreach (var (userId, phone) in await store.LinksAsync(zaloUserId, ct))
            if (await store.AccountAsync(userId, ct) is { } a && a.NormalizedPhone == phone) accounts.Add(a);
        return accounts;
    }

    private MiniAppResult Session(string zaloUserId, MiniAppAccount account) =>
        new(true, Session: Protector.Protect(JsonSerializer.Serialize(new Payload(zaloUserId, account.UserId, (Now + SessionLifetime).Ticks))), Account: View(account));

    private static MiniAppAccountView View(MiniAppAccount a) => new(a.UserId, a.DisplayName, a.Username);

    /// <summary>Zalo user, account, expiry (UTC ticks); encrypted and signed by ASP.NET Data Protection.</summary>
    private sealed record Payload(string Z, Guid U, long E);
}
