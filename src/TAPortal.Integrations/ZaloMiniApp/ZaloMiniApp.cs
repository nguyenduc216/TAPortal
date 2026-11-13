using System.Security.Cryptography;
using System.Text;
using TAPortal.Integrations.Notifications;

namespace TAPortal.Integrations.ZaloMiniApp;

// T.A Zalo Mini App sign-in (operator decisions 2026-11-13, migration 038):
// - Only the phone number counts: the Zalo account's phone (verified by Zalo) must equal the phone registered on the
//   TA Portal / Hoa don tu dong account (dbo.Users.Phone). Otherwise nobody gets in.
// - Way 1, QR: the user signs in on the web site, opens the Mini App QR of their account and scans it with Zalo. The QR
//   carries a one-time token that lives 5 minutes and opens only for the Zalo account whose phone is that account's.
// - Way 2, direct: username + password of TA Portal / Hoa don tu dong, and the Zalo phone must still match.
// - After a successful sign-in the Zalo user stays linked to the account (next opens go straight in); a Zalo user linked
//   to several accounts picks one. Unlinking on the web or signing out in the Mini App ends the link.
// - Phone numbers are verified by a ZNS OTP on the web when they are entered or changed (dbo.UserVerifiedPhones); a Mini
//   App sign-in that matched the Zalo phone also counts as verified, since Zalo already proved the number.

public sealed class ZaloMiniAppOptions
{
    public const string SectionName = "ZaloMiniApp";
    /// <summary>Mini App ID (mini.zalo.me); also used in the QR deep link.</summary>
    public string? AppId { get; set; }
    /// <summary>Mini App secret key: decodes the phone-number token at graph.zalo.me. Configuration only, never logged.</summary>
    public string? SecretKey { get; set; }
    /// <summary>ZNS template of the OTP, sent through T.A's SYSTEM Zalo OA channel.</summary>
    public string? OtpTemplateId { get; set; }
    /// <summary>Name of the OTP parameter in that template.</summary>
    public string OtpTemplateParam { get; set; } = "otp";
    /// <summary>
    /// Accounts must verify their phone before using the web sites (operator 2026-11-13). Off until the OTP template works,
    /// so nobody is locked out; the gate also stays off while OtpTemplateId is empty.
    /// </summary>
    public bool RequirePhoneVerification { get; set; }
    /// <summary>Origins the Mini App calls the API from (Zalo serves Mini Apps from h5.zdn.vn).</summary>
    public string[] AllowedOrigins { get; set; } = ["https://h5.zdn.vn", "zbrowser://h5.zdn.vn"];

    public bool SignInConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(SecretKey);
}

public static class MiniAppPortal
{
    public const string Portal = "PORTAL", Invoice = "INVOICE";
    public static bool Valid(string? value) => value is Portal or Invoice;
}

public static class MiniAppLinkSource
{
    public const string Qr = "QR", Login = "LOGIN";
}

public static class VerifiedPhoneMethod
{
    public const string ZnsOtp = "ZNS_OTP", ZaloMiniApp = "ZALO_MINIAPP";
}

/// <summary>An active T.A account (dbo.Users) as the Mini App sees it.</summary>
public sealed record MiniAppAccount(Guid UserId, string Username, string DisplayName, string? Phone, string? PasswordHash, bool MustChangePassword)
{
    /// <summary>The account's phone as Zalo writes it (84xxxxxxxxx), or null when it has none or it is not a mobile number.</summary>
    public string? NormalizedPhone => MiniAppPhone.Normalize(Phone);
}

/// <summary>Who opened the Mini App: Zalo user id, and the Zalo-verified phone when the user allowed it.</summary>
public sealed record ZaloIdentity(string ZaloUserId, string? Phone);

public sealed record OtpChallenge(Guid Id, Guid UserId, string Phone, byte[] CodeHash, int Attempts, DateTime ExpiresAtUtc, DateTime? ConsumedAtUtc);

public static class MiniAppPhone
{
    public static string? Normalize(string? value) => NotificationCatalog.ZaloPhone(value);

    /// <summary>Last three digits only, for messages (no full number on screen or in logs).</summary>
    public static string Masked(string? phone) => phone is { Length: > 3 } p ? "…" + p[^3..] : "…";
}

public static class MiniAppSecrets
{
    /// <summary>A 256-bit random token, URL safe.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the database keeps of a QR token: its SHA-256.</summary>
    public static byte[] TokenHash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static string NewOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>What the database keeps of an OTP: HMAC-SHA256 keyed by the challenge id.</summary>
    public static byte[] OtpHash(Guid challengeId, string code) => HMACSHA256.HashData(challengeId.ToByteArray(), Encoding.ASCII.GetBytes(code.Trim()));

    /// <summary>Deep link that opens the Mini App with the QR token (encoded in the QR image).</summary>
    public static string QrLink(string appId, string token) => $"https://zalo.me/s/{Uri.EscapeDataString(appId)}/?qr={Uri.EscapeDataString(token)}";
}
