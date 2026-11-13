using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.ZaloMiniApp;

/// <summary>Zalo Graph API calls of the Mini App sign-in (replaceable in tests, which never use the network).</summary>
public interface IZaloGraph
{
    /// <summary>
    /// The Zalo user behind <paramref name="accessToken"/> (zmp-sdk getAccessToken) and, when <paramref name="phoneToken"/>
    /// (zmp-sdk getPhoneNumber) is given, the Zalo-verified phone. Error is a message for the user.
    /// </summary>
    Task<(ZaloIdentity? Identity, string? Error)> IdentityAsync(string accessToken, string? phoneToken, CancellationToken ct = default);
}

public sealed class ZaloGraphClient(HttpClient http, IOptions<ZaloMiniAppOptions> options) : IZaloGraph
{
    public const string HttpClientName = "zalo-graph";
    private const string Me = "https://graph.zalo.me/v2.0/me?fields=id";
    private const string Phone = "https://graph.zalo.me/v2.0/me/info";

    public async Task<(ZaloIdentity? Identity, string? Error)> IdentityAsync(string accessToken, string? phoneToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 2048) return (null, "Phiên Zalo không hợp lệ, vui lòng mở lại ứng dụng.");
        using var me = new HttpRequestMessage(HttpMethod.Get, Me);
        me.Headers.TryAddWithoutValidation("access_token", accessToken);
        using var meDoc = await GetAsync(me, ct);
        if (meDoc is null) return (null, "Không kết nối được Zalo, vui lòng thử lại.");
        var root = meDoc.RootElement;
        if (ErrorCode(root) != 0 || !root.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 and <= 64 } zaloUserId)
            return (null, "Phiên Zalo đã hết hạn, vui lòng mở lại ứng dụng.");
        if (phoneToken is null) return (new(zaloUserId, null), null);

        if (string.IsNullOrWhiteSpace(options.Value.SecretKey)) return (null, "Ứng dụng chưa được cấu hình đầy đủ, vui lòng liên hệ T.A.");
        if (phoneToken.Length > 2048) return (null, "Không đọc được số điện thoại Zalo.");
        using var info = new HttpRequestMessage(HttpMethod.Get, Phone);
        info.Headers.TryAddWithoutValidation("access_token", accessToken);
        info.Headers.TryAddWithoutValidation("code", phoneToken);
        info.Headers.TryAddWithoutValidation("secret_key", options.Value.SecretKey);
        using var infoDoc = await GetAsync(info, ct);
        if (infoDoc is null) return (null, "Không kết nối được Zalo, vui lòng thử lại.");
        var data = infoDoc.RootElement;
        var number = ErrorCode(data) == 0 && data.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("number", out var n) ? n.GetString() : null;
        return MiniAppPhone.Normalize(number) is string phone ? (new(zaloUserId, phone), null) : (null, "Không đọc được số điện thoại Zalo, vui lòng cho phép ứng dụng dùng số điện thoại.");
    }

    private async Task<JsonDocument?> GetAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    private static int ErrorCode(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object ? -1 : root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;
}
