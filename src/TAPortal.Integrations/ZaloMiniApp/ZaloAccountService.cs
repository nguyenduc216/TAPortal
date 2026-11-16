using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.ZaloMiniApp;

/// <summary>What the "Zalo Mini App" card of a profile page shows.</summary>
public sealed record ZaloPanel(string? Phone, bool PhoneIsMobile, bool Verified, bool OtpAvailable, bool QrAvailable, int LinkCount, bool VerificationRequired);

public sealed record MiniAppQr(bool Ok, string? Error, string? Link = null, DateTime? ExpiresAtUtc = null);

/// <summary>
/// Account side of the Mini App, the same on TA Portal and Hoa don tu dong (operator 2026-11-13): change one's own phone,
/// verify it by ZNS OTP, open the one-time QR, unlink Zalo. Every call acts on the signed-in user only.
/// </summary>
public sealed class ZaloAccountService(IMiniAppStore store, PhoneVerification otp, IOptions<ZaloMiniAppOptions> options, TimeProvider clock)
{
    public static readonly TimeSpan QrLifetime = TimeSpan.FromMinutes(5);
    public const string VerifyPhoneFirst = "Tài khoản chưa có số điện thoại đã xác minh. Vui lòng xác minh số điện thoại bằng mã OTP Zalo trước.";

    public async Task<ZaloPanel?> PanelAsync(Guid userId, CancellationToken ct = default)
    {
        if (await store.AccountAsync(userId, ct) is not { } account) return null;
        var verified = account.NormalizedPhone is string p && await store.VerifiedPhoneAsync(userId, ct) == p;
        return new(account.Phone, account.NormalizedPhone is not null, verified, !string.IsNullOrWhiteSpace(options.Value.OtpTemplateId),
            options.Value.SignInConfigured, await store.ActiveLinkCountAsync(userId, ct), options.Value.RequirePhoneVerification);
    }

    /// <summary>Null when saved (the new phone then waits for its OTP), else the message for the user.</summary>
    public async Task<string?> ChangePhoneAsync(Guid userId, string? phone, string? ip, CancellationToken ct = default)
    {
        if (MiniAppPhone.Normalize(phone) is not string normalized) return "Số điện thoại di động không hợp lệ (ví dụ 0912345678).";
        if (await store.AccountAsync(userId, ct) is not { } account) return "Tài khoản không còn hoạt động.";
        if (account.NormalizedPhone == normalized) return "Đây đã là số điện thoại của tài khoản.";
        await store.ChangePhoneAsync(userId, "0" + normalized[2..], ip, ct);
        PhoneVerificationGate.Forget(userId);
        return null;
    }

    public Task<OtpRequest> SendOtpAsync(Guid userId, string? ip, CancellationToken ct = default) => otp.RequestAsync(userId, ip, ct);

    public async Task<string?> ConfirmOtpAsync(Guid userId, Guid challengeId, string? code, CancellationToken ct = default)
    {
        var error = await otp.VerifyAsync(userId, challengeId, code, ct);
        if (error is null) PhoneVerificationGate.Forget(userId);
        return error;
    }

    /// <summary>A one-time QR for the signed-in user; only for a verified phone.</summary>
    public async Task<MiniAppQr> CreateQrAsync(Guid userId, string portal, CancellationToken ct = default)
    {
        if (!MiniAppPortal.Valid(portal)) throw new ArgumentException("Unknown portal", nameof(portal));
        if (!options.Value.SignInConfigured) return new(false, MiniAppAuth.NotConfigured);
        if (await store.AccountAsync(userId, ct) is not { } account) return new(false, "Tài khoản không còn hoạt động.");
        if (account.NormalizedPhone is not string phone || await store.VerifiedPhoneAsync(userId, ct) != phone) return new(false, VerifyPhoneFirst);
        var token = MiniAppSecrets.NewToken();
        var expires = clock.GetUtcNow().UtcDateTime + QrLifetime;
        await store.CreateQrAsync(MiniAppSecrets.TokenHash(token), userId, portal, expires, ct);
        return new(true, null, MiniAppSecrets.QrLink(options.Value.AppId!, token), expires);
    }

    public Task<int> UnlinkAsync(Guid userId, CancellationToken ct = default) => store.RevokeLinksAsync(userId, userId, ct);
}

/// <summary>
/// Operator decision 2026-11-13: accounts must verify their phone at the next web sign-in. Only when
/// ZaloMiniApp:RequirePhoneVerification is on and the OTP template is configured (so nobody is locked out before ZNS
/// works): a signed-in user without a verified phone sees only the page that verifies it.
/// </summary>
public static class PhoneVerificationGate
{
    // Verified users are remembered for a few minutes so the gate does not query the database on every request.
    private static readonly ConcurrentDictionary<Guid, DateTime> Verified = new();
    private static readonly TimeSpan Remember = TimeSpan.FromMinutes(10);

    public static void Forget(Guid userId) => Verified.TryRemove(userId, out _);

    public static IApplicationBuilder UsePhoneVerificationGate(this IApplicationBuilder app, string verifyPath, params string[] allowed) => app.Use(async (context, next) =>
    {
        var options = context.RequestServices.GetRequiredService<IOptions<ZaloMiniAppOptions>>().Value;
        if (options.RequirePhoneVerification && !string.IsNullOrWhiteSpace(options.OtpTemplateId)
            && context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && !context.Request.Path.StartsWithSegments(verifyPath, StringComparison.OrdinalIgnoreCase)
            && !allowed.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            && !(Verified.TryGetValue(userId, out var until) && until > DateTime.UtcNow))
        {
            if (await context.RequestServices.GetRequiredService<PhoneVerification>().IsVerifiedAsync(userId, context.RequestAborted))
                Verified[userId] = DateTime.UtcNow + Remember;
            else
            {
                context.Response.Redirect(context.Request.PathBase + verifyPath);
                return;
            }
        }
        await next();
    });
}
