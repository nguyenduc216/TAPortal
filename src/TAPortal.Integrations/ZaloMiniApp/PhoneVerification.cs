using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Notifications;

namespace TAPortal.Integrations.ZaloMiniApp;

/// <summary>Sends one ZNS template message; null when sent, else the reason (T.A's SYSTEM Zalo OA channel in production).</summary>
public interface IZnsSender
{
    Task<string?> SendAsync(string phone, string templateId, IReadOnlyDictionary<string, string> data, CancellationToken ct = default);
}

public sealed class SystemZnsSender(NotificationSettings settings) : IZnsSender
{
    public Task<string?> SendAsync(string phone, string templateId, IReadOnlyDictionary<string, string> data, CancellationToken ct = default) =>
        settings.SendSystemZnsAsync(phone, templateId, data, ct);
}

public sealed record OtpRequest(bool Ok, string? Error, Guid? ChallengeId = null, string? MaskedPhone = null);

/// <summary>
/// Proves that the phone on an account belongs to its user: a 6-digit code sent by ZNS to the account's own phone
/// (never a number typed in the request), valid 5 minutes, 5 tries; at most one code a minute and 5 an hour per account.
/// </summary>
public sealed class PhoneVerification(IMiniAppStore store, IZnsSender sender, IOptions<ZaloMiniAppOptions> options, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);
    public const int MaxPerHour = 5, MaxAttempts = 5;
    public const string Purpose = "VERIFY_PHONE";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>True when the account's current phone is the one it verified.</summary>
    public async Task<bool> IsVerifiedAsync(Guid userId, CancellationToken ct = default) =>
        await store.AccountAsync(userId, ct) is { NormalizedPhone: string phone } a && await store.VerifiedPhoneAsync(a.UserId, ct) == phone;

    public async Task<OtpRequest> RequestAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.OtpTemplateId)) return new(false, "Chưa cấu hình mẫu tin OTP Zalo, vui lòng liên hệ bộ phận hỗ trợ.");
        if (await store.AccountAsync(userId, ct) is not { } account) return new(false, "Tài khoản không còn hoạt động.");
        if (account.NormalizedPhone is not string phone) return new(false, "Tài khoản chưa có số điện thoại di động hợp lệ (ví dụ 0912345678). Vui lòng cập nhật số điện thoại trước.");
        var (count, last) = await store.OtpStatsAsync(userId, Now.AddHours(-1), ct);
        if (last is DateTime l && Now - l < MinInterval) return new(false, $"Vui lòng đợi {Math.Max(1, (int)Math.Ceiling((MinInterval - (Now - l)).TotalSeconds))} giây rồi gửi lại mã.");
        if (count >= MaxPerHour) return new(false, "Bạn đã yêu cầu quá nhiều mã trong một giờ. Vui lòng thử lại sau.");
        var id = Guid.NewGuid();
        var code = MiniAppSecrets.NewOtp();
        await store.CreateOtpAsync(id, userId, phone, Purpose, MiniAppSecrets.OtpHash(id, code), Now + Lifetime, ip, ct);
        var error = await sender.SendAsync(phone, options.Value.OtpTemplateId!, new Dictionary<string, string> { [options.Value.OtpTemplateParam] = code }, ct);
        return error is null ? new(true, null, id, MiniAppPhone.Masked(phone)) : new(false, "Không gửi được mã qua Zalo: " + error);
    }

    /// <summary>Null when the code is right (the phone is then verified), else the message for the user.</summary>
    public async Task<string?> VerifyAsync(Guid userId, Guid challengeId, string? code, CancellationToken ct = default)
    {
        var c = await store.OtpAsync(challengeId, userId, ct);
        if (c is null || c.ConsumedAtUtc is not null || c.ExpiresAtUtc <= Now) return "Mã đã hết hạn, vui lòng gửi lại mã mới.";
        if (c.Attempts >= MaxAttempts) return "Bạn đã nhập sai quá nhiều lần, vui lòng gửi lại mã mới.";
        var digits = (code ?? "").Trim();
        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit) || !CryptographicOperations.FixedTimeEquals(MiniAppSecrets.OtpHash(challengeId, digits), c.CodeHash))
        {
            await store.FailOtpAsync(challengeId, ct);
            var left = MaxAttempts - c.Attempts - 1;
            return left > 0 ? $"Mã không đúng, bạn còn {left} lần thử." : "Bạn đã nhập sai quá nhiều lần, vui lòng gửi lại mã mới.";
        }
        if (await store.AccountAsync(userId, ct) is not { } account || account.NormalizedPhone != c.Phone) return "Số điện thoại của tài khoản đã thay đổi, vui lòng gửi lại mã mới.";
        if (!await store.ConsumeOtpAsync(challengeId, Now, ct)) return "Mã đã hết hạn, vui lòng gửi lại mã mới.";
        await store.SetVerifiedPhoneAsync(userId, c.Phone, VerifiedPhoneMethod.ZnsOtp, ct);
        return null;
    }
}

public static class ZaloMiniAppServiceCollectionExtensions
{
    /// <summary>Mini App sign-in, QR and phone OTP (needs AddNotifications for the SYSTEM ZNS sender). The site that hosts the Mini App API also registers MiniAppAuth and its IMiniAppPasswordCheck..</summary>
    public static IServiceCollection AddZaloMiniApp(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<ZaloMiniAppOptions>(configuration.GetSection(ZaloMiniAppOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient<IZaloGraph, ZaloGraphClient>(c => c.Timeout = TimeSpan.FromSeconds(10)).RemoveAllLoggers();   // headers carry tokens and the secret key
        services.TryAddScoped<IMiniAppStore, SqlMiniAppStore>();
        services.TryAddScoped<IZnsSender, SystemZnsSender>();
        services.TryAddScoped<PhoneVerification>();
        services.TryAddScoped<ZaloAccountService>();
        return services;
    }
}
