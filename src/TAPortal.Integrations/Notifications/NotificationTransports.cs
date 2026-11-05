using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace TAPortal.Integrations.Notifications;

/// <summary>Result of one send. <see cref="Secrets"/> is set when the channel's tokens were refreshed and must be saved.</summary>
public sealed record SendResult(bool Ok, string? Error = null, Dictionary<string, string>? Secrets = null);

public interface INotificationTransport
{
    /// <summary>Sends one message through <paramref name="channel"/>; never throws for a channel-side failure.</summary>
    Task<SendResult> SendAsync(NotificationChannel channel, IReadOnlyDictionary<string, string> secrets, OutboxMessage message, CancellationToken ct);
}

/// <summary>
/// SMTP (System.Net.Mail, STARTTLS), Telegram Bot API sendMessage, and Zalo ZNS (message/template, with the OA access token
/// refreshed from the refresh token when Zalo says it expired; Zalo refresh tokens are single use, so the new pair is
/// returned to be saved). Only the fixed Telegram/Zalo hosts are called; a company's SMTP host must be public.
/// </summary>
public sealed class NotificationTransport(IHttpClientFactory http) : INotificationTransport
{
    public const string HttpClientName = "notifications";

    public async Task<SendResult> SendAsync(NotificationChannel channel, IReadOnlyDictionary<string, string> secrets, OutboxMessage message, CancellationToken ct)
    {
        try
        {
            return channel.Channel switch
            {
                NotificationChannelKind.Smtp => await SmtpAsync(channel, secrets, message, ct),
                NotificationChannelKind.TelegramBot or NotificationChannelKind.TelegramGroup => await TelegramAsync(secrets, message, ct),
                NotificationChannelKind.ZaloOa => await ZaloAsync(channel, secrets, message, ct),
                _ => new(false, "Kênh không được hỗ trợ")
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or SmtpException or IOException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            return new(false, Cut(ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string key) => d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    private static string Cut(string s) => s.Length <= 450 ? s : s[..450];

    private static async Task<SendResult> SmtpAsync(NotificationChannel c, IReadOnlyDictionary<string, string> secrets, OutboxMessage m, CancellationToken ct)
    {
        var host = Get(c.Settings, "host"); var from = Get(c.Settings, "fromAddress");
        if (host is null || from is null || !int.TryParse(Get(c.Settings, "port"), out var port)) return new(false, "Thiếu cấu hình SMTP");
        if (!NotificationCatalog.Mail(m.Recipient)) return new(false, "Email người nhận không hợp lệ");
        if (c.Scope == NotificationScope.Customer && !await PublicHostGuard.IsPublicAsync(host, ct)) return new(false, "Máy chủ SMTP phải là địa chỉ công khai trên Internet");
        using var mail = new MailMessage { From = new MailAddress(from, Get(c.Settings, "fromName") ?? from, Encoding.UTF8), Subject = m.Subject ?? "Thông báo", SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, Body = m.Body };
        mail.To.Add(m.Recipient);
        using var smtp = new SmtpClient(host, port) { EnableSsl = true, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20000 };
        if (Get(c.Settings, "username") is string user) smtp.Credentials = new NetworkCredential(user, Get(secrets, "password"));
        await smtp.SendMailAsync(mail, ct);
        return new(true);
    }

    private async Task<SendResult> TelegramAsync(IReadOnlyDictionary<string, string> secrets, OutboxMessage m, CancellationToken ct)
    {
        var token = Get(secrets, "botToken");
        if (!NotificationCatalog.TelegramToken(token)) return new(false, "Thiếu hoặc sai bot token");
        if (!NotificationCatalog.TelegramChat(m.Recipient)) return new(false, "Chat ID không hợp lệ");
        var text = m.Subject is null ? m.Body : m.Subject + "\n" + m.Body;
        using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage",
            new { chat_id = m.Recipient, text, disable_web_page_preview = true }, ct);
        if (response.IsSuccessStatusCode) return new(true);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return new(false, Cut($"Telegram {(int)response.StatusCode}: {(doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "")}"));
    }

    private async Task<SendResult> ZaloAsync(NotificationChannel c, IReadOnlyDictionary<string, string> secrets, OutboxMessage m, CancellationToken ct)
    {
        if (Get(c.Settings, "templateId") is not string templateId) return new(false, "Thiếu mã mẫu ZNS");
        if (NotificationCatalog.ZaloPhone(m.Recipient) is not string phone) return new(false, "Số điện thoại người nhận không hợp lệ");
        var data = m.TemplateData ?? new Dictionary<string, string>();
        Dictionary<string, string>? refreshed = null;
        var access = Get(secrets, "accessToken");
        if (access is null)
        {
            (refreshed, var error) = await RefreshAsync(c, secrets, ct);
            if (refreshed is null) return new(false, error);
            access = refreshed["accessToken"];
        }
        var (code, message) = await ZnsAsync(access, phone, templateId, data, m.EventKey, ct);
        if (code is -124 or -216 && refreshed is null)   // access token expired / invalid: refresh once
        {
            (refreshed, var error) = await RefreshAsync(c, secrets, ct);
            if (refreshed is null) return new(false, error);
            (code, message) = await ZnsAsync(refreshed["accessToken"], phone, templateId, data, m.EventKey, ct);
        }
        return code == 0 ? new(true, null, refreshed) : new(false, Cut($"Zalo {code}: {message}"), refreshed);
    }

    private async Task<(int Code, string? Message)> ZnsAsync(string accessToken, string phone, string templateId, IReadOnlyDictionary<string, string> data, string tracking, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://business.openapi.zalo.me/message/template")
        {
            Content = JsonContent.Create(new { phone, template_id = templateId, template_data = data, tracking_id = tracking.Length <= 48 ? tracking : tracking[^48..] })
        };
        request.Headers.Add("access_token", accessToken);
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        return (root.TryGetProperty("error", out var e) && e.TryGetInt32(out var code) ? code : (int)response.StatusCode, root.TryGetProperty("message", out var msg) ? msg.GetString() : null);
    }

    private async Task<(Dictionary<string, string>? Secrets, string? Error)> RefreshAsync(NotificationChannel c, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        if (Get(c.Settings, "appId") is not string appId || Get(secrets, "appSecret") is not string appSecret || Get(secrets, "refreshToken") is not string refresh)
            return (null, "Thiếu App ID / secret key / refresh token Zalo");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth.zaloapp.com/v4/oa/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["refresh_token"] = refresh, ["app_id"] = appId, ["grant_type"] = "refresh_token" })
        };
        request.Headers.Add("secret_key", appSecret);
        using var response = await http.CreateClient(HttpClientName).SendAsync(request, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("access_token", out var a) && a.GetString() is { Length: > 0 } access && root.TryGetProperty("refresh_token", out var r) && r.GetString() is { Length: > 0 } next)
            return (new Dictionary<string, string>(secrets) { ["accessToken"] = access, ["refreshToken"] = next }, null);
        return (null, Cut("Không làm mới được access token Zalo: " + (root.TryGetProperty("error_description", out var d) ? d.GetString() : root.GetRawText())));
    }
}
