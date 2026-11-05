using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Integrations.Notifications;

// Notification channels (operator decision 2026-11-05, migration 020):
// - SYSTEM channels belong to T.A (set in T.A Admin): Zalo OA, Telegram bot, Telegram group, SMTP. They notify the companies
//   that use the e-invoice portal (at the addresses each company gives in "Nhận thông báo từ T.A") and T.A's staff group.
// - CUSTOMER channels belong to one company (set in its Customer Portal): Zalo OA, Telegram group, SMTP. They notify that
//   company's own buyers (email / Zalo phone of the order's buyer) and its staff group. They are only ever read for that
//   company: every store call takes the owner (scope + customer id) and filters by it.
// Secrets (passwords, tokens) are encrypted with DPAPI before they reach SQL and are never sent back to a page.

public static class NotificationScope
{
    public const string System = "SYSTEM", Customer = "CUSTOMER";
}

public static class NotificationChannelKind
{
    public const string ZaloOa = "ZALO_OA", TelegramBot = "TELEGRAM_BOT", TelegramGroup = "TELEGRAM_GROUP", Smtp = "SMTP";
}

public static class NotificationEvent
{
    /// <summary>A bank notification was applied to an order (paid, partly paid, ...).</summary>
    public const string Payment = "PAYMENT";
    /// <summary>A transaction needs staff: not matched to any order, or applied but to be reviewed.</summary>
    public const string Attention = "ATTENTION";

    public static readonly IReadOnlyList<(string Code, string Label)> All = [(Payment, "Đơn hàng nhận tiền"), (Attention, "Giao dịch chưa xử lý / cần xem lại")];

    public static IReadOnlySet<string> Parse(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(e => All.Any(a => a.Code == e)).ToHashSet();

    public static string Join(IEnumerable<string> events) => string.Join(",", events.Where(e => All.Any(a => a.Code == e)).Distinct().Order());
}

public sealed record ChannelField(string Key, string Label, bool Secret, bool Required, string? Placeholder = null, string Icon = "pencil", string? Help = null);

/// <summary>What each channel needs, its label, and which channels each owner may configure.</summary>
public static class NotificationCatalog
{
    public static IReadOnlyList<string> Channels(string scope) => scope == NotificationScope.System
        ? [NotificationChannelKind.ZaloOa, NotificationChannelKind.TelegramBot, NotificationChannelKind.TelegramGroup, NotificationChannelKind.Smtp]
        : [NotificationChannelKind.ZaloOa, NotificationChannelKind.TelegramGroup, NotificationChannelKind.Smtp];

    public static bool Allowed(string scope, string channel) => Channels(scope).Contains(channel);

    public static string Label(string channel) => channel switch
    {
        NotificationChannelKind.ZaloOa => "Zalo OA (tin ZNS)",
        NotificationChannelKind.TelegramBot => "Telegram chatbot (tin riêng)",
        NotificationChannelKind.TelegramGroup => "Telegram nhóm",
        NotificationChannelKind.Smtp => "Email SMTP",
        _ => channel
    };

    public static string Icon(string channel) => channel switch
    {
        NotificationChannelKind.Smtp => "mail",
        NotificationChannelKind.ZaloOa => "phone",
        _ => "send"
    };

    /// <summary>Who receives a channel's messages, as shown next to it.</summary>
    public static string Audience(string scope, string channel) => (scope, channel) switch
    {
        (NotificationScope.System, NotificationChannelKind.TelegramGroup) => "Nhóm nhân viên T.A: mọi giao dịch cần xử lý của mọi khách hàng.",
        (NotificationScope.System, _) => "Doanh nghiệp dùng hóa đơn điện tử, tại địa chỉ doanh nghiệp khai báo ở mục \"Nhận thông báo từ T.A\".",
        (_, NotificationChannelKind.TelegramGroup) => "Nhóm nhân viên của doanh nghiệp: tiền vào đơn hàng và giao dịch cần xử lý.",
        (_, NotificationChannelKind.Smtp) => "Khách hàng của doanh nghiệp (email của khách trên đơn hàng) khi đơn nhận tiền.",
        _ => "Khách hàng của doanh nghiệp (số điện thoại của khách trên đơn hàng) khi đơn nhận tiền."
    };

    public const string TemplateHelp = "JSON tham số mẫu ZNS → giá trị. Dùng được: {order_no} {amount} {paid} {total} {status} {company} {buyer} {time}.";

    public static IReadOnlyList<ChannelField> Fields(string channel) => channel switch
    {
        NotificationChannelKind.Smtp =>
        [
            new("host", "Máy chủ SMTP", false, true, "smtp.example.com", "server"),
            new("port", "Cổng", false, true, "587", "hash", "587 hoặc 25 (STARTTLS). Cổng 465 (SSL ngầm định) không được hỗ trợ."),
            new("username", "Tài khoản đăng nhập", false, false, "no-reply@example.com", "user"),
            new("password", "Mật khẩu", true, false, null, "lock"),
            new("fromAddress", "Email gửi đi", false, true, "no-reply@example.com", "mail"),
            new("fromName", "Tên người gửi", false, false, "Phòng kế toán", "id"),
        ],
        NotificationChannelKind.TelegramBot =>
        [
            new("botToken", "Bot token", true, true, "123456:ABC...", "lock", "Lấy từ @BotFather. Người nhận phải bấm Start với bot trước."),
        ],
        NotificationChannelKind.TelegramGroup =>
        [
            new("botToken", "Bot token", true, true, "123456:ABC...", "lock", "Bot phải được thêm vào nhóm."),
            new("chatId", "Chat ID của nhóm", false, true, "-1001234567890", "hash"),
        ],
        NotificationChannelKind.ZaloOa =>
        [
            new("appId", "App ID", false, true, null, "hash"),
            new("appSecret", "Secret key của ứng dụng", true, true, null, "lock"),
            new("refreshToken", "Refresh token OA", true, true, null, "lock", "Hệ thống tự làm mới access token bằng refresh token."),
            new("accessToken", "Access token OA (không bắt buộc)", true, false, null, "lock"),
            new("templateId", "Mã mẫu ZNS", false, true, null, "file-text"),
            new("templateData", "Tham số mẫu ZNS (JSON)", false, true, "{\"order_code\":\"{order_no}\",\"amount\":\"{amount}\"}", "list-tree", TemplateHelp),
        ],
        _ => []
    };

    /// <summary>Field checks; <paramref name="secrets"/> are the secrets after the form was merged into the saved ones.</summary>
    public static List<string> Validate(string channel, IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> secrets, bool enabled)
    {
        var errors = new List<string>();
        string? V(string key) => settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : secrets.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
        if (enabled)
            foreach (var f in Fields(channel).Where(f => f.Required && V(f.Key) is null)) errors.Add($"Vui lòng nhập {f.Label.ToLowerInvariant()}.");
        if (V("port") is string port && (!int.TryParse(port, out var p) || p is not (25 or 587 or 2525))) errors.Add("Cổng SMTP chỉ nhận 587, 25 hoặc 2525 (STARTTLS).");
        if (V("host") is string host && !Regex.IsMatch(host, @"^(?=.{1,253}$)([a-zA-Z0-9-]{1,63}\.)+[a-zA-Z]{2,63}$")) errors.Add("Máy chủ SMTP phải là tên miền (ví dụ smtp.gmail.com).");
        if (V("fromAddress") is string from && !Mail(from)) errors.Add("Email gửi đi không hợp lệ.");
        if (V("botToken") is string token && !TelegramToken(token)) errors.Add("Bot token Telegram không đúng định dạng.");
        if (V("chatId") is string chat && !TelegramChat(chat)) errors.Add("Chat ID Telegram không đúng định dạng (ví dụ -1001234567890 hoặc @tennhom).");
        if (V("appId") is string app && !Regex.IsMatch(app, "^[0-9]{5,30}$")) errors.Add("App ID Zalo chỉ gồm chữ số.");
        if (V("templateId") is string tpl && !Regex.IsMatch(tpl, "^[0-9]{1,20}$")) errors.Add("Mã mẫu ZNS chỉ gồm chữ số.");
        if (V("templateData") is string data && TemplateData(data) is null) errors.Add("Tham số mẫu ZNS phải là JSON dạng {\"tên_tham_số\":\"giá trị\"}.");
        return errors;
    }

    public static bool Mail(string? value) => value is { Length: <= 256 } && Regex.IsMatch(value, @"^[^@\s<>""]+@[^@\s<>""]+\.[^@\s<>""]{2,}$");
    public static bool TelegramToken(string? value) => value is not null && Regex.IsMatch(value, "^[0-9]{5,15}:[A-Za-z0-9_-]{30,60}$");
    public static bool TelegramChat(string? value) => value is not null && Regex.IsMatch(value, "^(-?[0-9]{3,20}|@[A-Za-z][A-Za-z0-9_]{4,31})$");

    /// <summary>A Vietnamese mobile number as Zalo expects it (84xxxxxxxxx); null when it is not one.</summary>
    public static string? ZaloPhone(string? value)
    {
        var digits = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11) return digits;
        return digits.Length == 10 && digits[0] == '0' ? "84" + digits[1..] : null;
    }

    /// <summary>The ZNS parameter → value-template map, or null when it is not a flat JSON object of strings.</summary>
    public static Dictionary<string, string>? TemplateData(string json)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (map is null || map.Count is 0 or > 20) return null;
            var result = new Dictionary<string, string>();
            foreach (var (k, v) in map)
            {
                if (!Regex.IsMatch(k, "^[A-Za-z0-9_]{1,50}$") || v.ValueKind != JsonValueKind.String) return null;
                result[k] = v.GetString()!;
            }
            return result;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>A configured channel without its secrets: what a settings page shows.</summary>
public sealed record NotificationChannel(Guid Id, string Scope, Guid? CustomerId, string Channel, bool IsEnabled, IReadOnlySet<string> Events,
    IReadOnlyDictionary<string, string> Settings, IReadOnlySet<string> SecretKeys, int SecretVersion, DateTime? LastTestAtUtc, string? LastTestResult);

/// <summary>Where T.A's system channels reach a company.</summary>
public sealed record NotificationRecipients(string? Email, string? ZaloPhone, string? TelegramChatId, IReadOnlySet<string> Events)
{
    public static readonly NotificationRecipients Empty = new(null, null, null, new HashSet<string>());
}

/// <summary>One message to send (dbo.NotificationOutbox).</summary>
public sealed record OutboxMessage(string EventKey, string Scope, Guid? CustomerId, string Channel, string Recipient, string? Subject, string Body,
    IReadOnlyDictionary<string, string>? TemplateData, long? TransactionId);

public sealed record PendingMessage(long Id, OutboxMessage Message, int Attempts);

public sealed record OutboxEntry(long Id, string Scope, Guid? CustomerId, string Channel, string Recipient, string? Subject, string Status, int Attempts,
    string? LastError, DateTime CreatedAtUtc, DateTime? SentAtUtc);

/// <summary>A transaction to notify about, with the order, company and buyer it concerns.</summary>
public sealed record NotifiableTransaction(long Id, string Status, string? Flags, string? Reason, decimal Amount, string? Content, string? AccountNumber,
    DateTime AtUtc, Guid? CustomerId, string? CompanyName, Guid? OrderId, string? OrderNo, decimal? OrderAmount, decimal? PaidAfter, string? OrderStatus,
    string? BuyerName, string? BuyerEmail, string? BuyerPhone);

/// <summary>Encrypts channel secrets for SQL. DPAPI LocalMachine: the three web apps on the T.A server share it.</summary>
public interface ISecretProtector
{
    byte[] Protect(IReadOnlyDictionary<string, string> secrets);
    Dictionary<string, string> Unprotect(byte[]? data);
}

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TAPortal.NotificationChannels.v1");

    public byte[] Protect(IReadOnlyDictionary<string, string> secrets) =>
        ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(secrets), Entropy, DataProtectionScope.LocalMachine);

    public Dictionary<string, string> Unprotect(byte[]? data) =>
        data is null || data.Length == 0 ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine)) ?? [];
}

/// <summary>A company's SMTP server must be a public host (no loopback/private/link-local address): the web server never connects inside T.A's network for a customer.</summary>
public static class PublicHostGuard
{
    public static async Task<bool> IsPublicAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
        catch (SocketException) { return false; }
        return addresses.Length > 0 && addresses.All(IsPublic);
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC);
        var b = ip.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224);
    }
}

/// <summary>
/// Turns a transaction into the messages each configured channel should send. Messages to companies and buyers never
/// carry the processor's raw reason (it may name a partner): they get a fixed Vietnamese text.
/// </summary>
public static class NotificationPlanner
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public static string Money(decimal? v) => (v ?? 0).ToString("#,##0", Vi) + " ₫";
    private static string Time(DateTime utc) => utc.AddHours(7).ToString("dd/MM/yyyy HH:mm", Vi);

    public static string OrderStatusLabel(string? status) => status switch
    {
        TingeeOrderStatus.Paid => "Đã thanh toán",
        TingeeOrderStatus.Partial => "Thanh toán một phần",
        TingeeOrderStatus.Awaiting => "Chờ thanh toán",
        TingeeOrderStatus.ExpiredPaid => "QR hết hạn có giao dịch",
        TingeeOrderStatus.CancelledPaid => "Đơn hủy có giao dịch",
        _ => status ?? ""
    };

    public static IReadOnlySet<string> Events(NotifiableTransaction t)
    {
        var set = new HashSet<string>();
        if (t.OrderId is not null && t.Status is PaymentTxStatus.Matched or PaymentTxStatus.Review) set.Add(NotificationEvent.Payment);
        if (t.Status is PaymentTxStatus.Unmatched or PaymentTxStatus.Review) set.Add(NotificationEvent.Attention);
        return set;
    }

    private static string Problem(NotifiableTransaction t) => t.OrderId is null ? "chưa khớp được đơn hàng"
        : string.Join(", ", (t.Flags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(PaymentTxText.Flag));

    private static string PaymentText(NotifiableTransaction t) =>
        $"Đơn {t.OrderNo}: đã nhận {Money(t.Amount)}, tổng đã thu {Money(t.PaidAfter)}/{Money(t.OrderAmount)} — {OrderStatusLabel(t.OrderStatus)}.";

    private static string AttentionText(NotifiableTransaction t, bool forStaffOfTA) =>
        $"Giao dịch {Money(t.Amount)} vào tài khoản {t.AccountNumber} lúc {Time(t.AtUtc)} cần xử lý: "
        + (forStaffOfTA ? t.Reason ?? Problem(t) : Problem(t))
        + (t.OrderNo is null ? "" : $" (đơn {t.OrderNo})") + $". Nội dung: {t.Content ?? "(không có)"}. Xử lý tại mục Đối soát giao dịch.";

    public static Dictionary<string, string> Values(NotifiableTransaction t) => new()
    {
        ["order_no"] = t.OrderNo ?? "", ["amount"] = Money(t.Amount), ["paid"] = Money(t.PaidAfter), ["total"] = Money(t.OrderAmount),
        ["status"] = OrderStatusLabel(t.OrderStatus), ["company"] = t.CompanyName ?? "", ["buyer"] = t.BuyerName ?? "", ["time"] = Time(t.AtUtc),
    };

    /// <summary>Fills a ZNS parameter map: each "{name}" placeholder is replaced by its value (unknown ones are kept).</summary>
    public static Dictionary<string, string> Fill(IReadOnlyDictionary<string, string> template, IReadOnlyDictionary<string, string> values) =>
        template.ToDictionary(p => p.Key, p => Regex.Replace(p.Value, @"\{([a-z_]+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value));

    public static List<OutboxMessage> Build(NotifiableTransaction t, IReadOnlyList<NotificationChannel> system, IReadOnlyList<NotificationChannel> company, NotificationRecipients recipients)
    {
        var events = Events(t);
        var list = new List<OutboxMessage>();
        void Add(NotificationChannel c, string recipient, string? subject, string body, IReadOnlyDictionary<string, string>? template = null)
        {
            var key = $"tx:{t.Id}:{c.Scope}:{c.Channel}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(recipient)))[..16]}";
            if (list.All(m => m.EventKey != key)) list.Add(new(key, c.Scope, c.Scope == NotificationScope.Customer ? c.CustomerId : t.CustomerId, c.Channel, recipient, subject, body, template, t.Id));
        }
        bool On(NotificationChannel c, string e) => c.IsEnabled && c.Events.Contains(e) && events.Contains(e);

        foreach (var c in system)
        {
            if (c.Channel == NotificationChannelKind.TelegramGroup)
            {
                if (On(c, NotificationEvent.Attention) && c.Settings.TryGetValue("chatId", out var group))
                    Add(c, group, null, $"[{t.CompanyName ?? "Chưa gắn khách hàng"}] " + AttentionText(t, forStaffOfTA: true));
                if (On(c, NotificationEvent.Payment) && c.Settings.TryGetValue("chatId", out group))
                    Add(c, group, null, $"[{t.CompanyName}] " + PaymentText(t));
                continue;
            }
            if (t.CustomerId is null) continue;
            foreach (var e in events.Where(e => On(c, e) && recipients.Events.Contains(e)))
            {
                var body = e == NotificationEvent.Payment ? PaymentText(t) : AttentionText(t, forStaffOfTA: false);
                var subject = e == NotificationEvent.Payment ? $"Đơn {t.OrderNo} đã nhận {Money(t.Amount)}" : "Có giao dịch cần xử lý";
                switch (c.Channel)
                {
                    case NotificationChannelKind.Smtp when recipients.Email is string mail: Add(c, mail, subject, body); break;
                    case NotificationChannelKind.TelegramBot when recipients.TelegramChatId is string chat: Add(c, chat, null, body); break;
                    case NotificationChannelKind.ZaloOa when NotificationCatalog.ZaloPhone(recipients.ZaloPhone) is string phone && c.Settings.TryGetValue("templateData", out var tpl)
                        && NotificationCatalog.TemplateData(tpl) is { } map && e == NotificationEvent.Payment:
                        Add(c, phone, null, body, Fill(map, Values(t))); break;
                }
            }
        }

        foreach (var c in company.Where(c => c.CustomerId == t.CustomerId && t.CustomerId is not null))
        {
            switch (c.Channel)
            {
                case NotificationChannelKind.TelegramGroup when c.Settings.TryGetValue("chatId", out var group):
                    if (On(c, NotificationEvent.Payment)) Add(c, group, null, PaymentText(t));
                    if (On(c, NotificationEvent.Attention)) Add(c, group, null, AttentionText(t, forStaffOfTA: false));
                    break;
                // Buyers only hear about money on their own order.
                case NotificationChannelKind.Smtp when On(c, NotificationEvent.Payment) && NotificationCatalog.Mail(t.BuyerEmail):
                    Add(c, t.BuyerEmail!, $"{t.CompanyName}: đã nhận thanh toán đơn {t.OrderNo}",
                        $"Kính gửi {t.BuyerName ?? "Quý khách"},\n{t.CompanyName} đã nhận {Money(t.Amount)} cho đơn {t.OrderNo}. Tổng đã thanh toán {Money(t.PaidAfter)}/{Money(t.OrderAmount)}.\nTrân trọng.");
                    break;
                case NotificationChannelKind.ZaloOa when On(c, NotificationEvent.Payment) && NotificationCatalog.ZaloPhone(t.BuyerPhone) is string phone
                    && c.Settings.TryGetValue("templateData", out var tpl) && NotificationCatalog.TemplateData(tpl) is { } map:
                    Add(c, phone, null, PaymentText(t), Fill(map, Values(t)));
                    break;
            }
        }
        return list;
    }
}
