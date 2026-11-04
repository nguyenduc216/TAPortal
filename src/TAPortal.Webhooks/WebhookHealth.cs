using System.Net;
using System.Reflection;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;

/// <summary>
/// State of the webhook site for /health (JSON, used by the deploy health check and monitoring) and / (the same as a page).
/// Healthy = Tingee keys configured and the inbox database reachable. Never shows a key, a connection string or a payload.
/// </summary>
public sealed record WebhookHealthReport(
    string Status, string Service, string Version, DateTime StartedAtUtc, DateTime CheckedAtUtc,
    string Database, string TingeeKeys, string ApiKey, string Signature, DateTime? LastWebhookAtUtc, string[] Receivers)
{
    public bool Healthy => Status == "ok";
}

public static class WebhookHealth
{
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;

    // Informational version carries the source commit (e.g. 1.0.0+e04040d...), so the page shows what is deployed.
    private static readonly string Version =
        typeof(WebhookHealth).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static async Task<WebhookHealthReport> CheckAsync(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<TingeeOptions>>().Value;
        var inbox = services.GetRequiredService<ITingeeWebhookInbox>();
        string database;
        DateTime? last = null;
        if (!inbox.IsAvailable) database = "not configured";
        else if (inbox is ITingeeWebhookInboxProbe probe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                last = await probe.LastReceivedAtAsync(timeout.Token);
                database = "ok";
            }
            catch (Exception ex)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger("TAPortal.Webhooks.Health").LogError(ex, "WEBHOOKS_HEALTH_DATABASE_FAILED");
                database = "unreachable";
            }
        }
        else database = "ok";
        var keys = options.IsConfigured ? "configured" : "missing";
        return new(
            database == "ok" && options.IsConfigured ? "ok" : "unhealthy",
            "TAPortal.Webhooks", Version, StartedAtUtc, DateTime.UtcNow, database, keys,
            options.HasWebhookApiKey ? $"required ({options.WebhookApiKeyHeader})" : "not set",
            options.HasWebhookApiKey ? "verified when sent" : "required",
            last,
            new[] { "payment", "va-status", "bank-link" }.Select(k => TingeeOptions.WebhookPathPrefix + "/" + k).ToArray());
    }

    public static string Page(WebhookHealthReport r)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        static string T(DateTime? utc) => utc is DateTime d ? d.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss") + " (UTC+7)" : "—";
        string Row(string label, string value, bool? good = null) =>
            $"<tr><th>{E(label)}</th><td{(good is null ? "" : good.Value ? " class=\"ok\"" : " class=\"bad\"")}>{E(value)}</td></tr>";
        return $$"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="30"><title>T.A Webhooks — {{(r.Healthy ? "OK" : "LỖI")}}</title>
            <style>
            body{font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;background:#f5f7fb;color:#1d2433;margin:0;padding:32px 16px}
            main{max-width:640px;margin:0 auto;background:#fff;border:1px solid #e3e8f0;border-radius:12px;padding:24px}
            h1{font-size:20px;margin:0 0 4px}.sub{color:#667382;margin:0 0 20px;font-size:14px}
            .badge{display:inline-block;padding:4px 12px;border-radius:999px;font-weight:700;font-size:14px;color:#fff;background:{{(r.Healthy ? "#2fb344" : "#d63939")}}}
            table{width:100%;border-collapse:collapse;margin-top:16px;font-size:14px}th,td{text-align:left;padding:8px 6px;border-top:1px solid #eef1f6;vertical-align:top}
            th{width:42%;color:#667382;font-weight:500}td.ok{color:#2b8a3e;font-weight:600}td.bad{color:#c92a2a;font-weight:600}code{font-size:13px}
            </style></head><body><main>
            <h1>T.A Webhooks (Tingee)</h1><p class="sub">Trang tự làm mới mỗi 30 giây · JSON: <code>/health</code></p>
            <span class="badge">{{(r.Healthy ? "Hoạt động bình thường" : "Có lỗi")}}</span>
            <table>
            {{Row("Cơ sở dữ liệu (hộp thư webhook)", r.Database, r.Database == "ok")}}
            {{Row("Khóa Tingee", r.TingeeKeys, r.TingeeKeys == "configured")}}
            {{Row("API key webhook", r.ApiKey, r.ApiKey != "not set")}}
            {{Row("Chữ ký HMAC (x-signature)", r.Signature)}}
            {{Row("Webhook gần nhất", T(r.LastWebhookAtUtc))}}
            {{Row("Địa chỉ nhận", string.Join(", ", r.Receivers))}}
            {{Row("Phiên bản", r.Version)}}
            {{Row("Khởi động lúc", T(r.StartedAtUtc))}}
            {{Row("Kiểm tra lúc", T(r.CheckedAtUtc))}}
            </table></main></body></html>
            """;
    }
}
