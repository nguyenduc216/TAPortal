using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

/// <summary>Read-only view of dbo.TingeeWebhookInbox for the Tingee Sandbox page.</summary>
public interface ITingeeSandboxInbox
{
    Task<IReadOnlyList<TingeeSandboxWebhook>> RecentAsync(int take, CancellationToken ct = default);
}

public sealed class SqlTingeeSandboxInbox(IConfiguration configuration) : ITingeeSandboxInbox
{
    public async Task<IReadOnlyList<TingeeSandboxWebhook>> RecentAsync(int take, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(configuration["Database:ConnectionString"]);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT TOP (@take) Id,Kind,Status,ReceivedAt,RawBody FROM dbo.TingeeWebhookInbox ORDER BY Id DESC", cn);
        cmd.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 100));
        var rows = new List<TingeeSandboxWebhook>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(TingeeSandboxWebhookParser.Parse(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDateTime(3), r.GetString(4)));
        return rows;
    }
}

/// <summary>
/// Pulls the usual Tingee transaction fields out of a stored webhook body for display. Field names differ between
/// webhook kinds and banks, so several aliases are tried and anything missing stays null; the raw body is always kept.
/// </summary>
public static class TingeeSandboxWebhookParser
{
    public static TingeeSandboxWebhook Parse(long id, string kind, string status, DateTime receivedAtUtc, string rawBody)
    {
        decimal? amount = null;
        string? content = null, account = null, code = null, time = null, bank = null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                amount = Number(root, "amount", "transferAmount", "creditAmount");
                content = Text(root, "content", "description", "transactionContent", "remark");
                account = Text(root, "vaAccountNumber", "accountNumber", "virtualAccount");
                code = Text(root, "transactionCode", "transactionId", "referenceCode");
                time = Text(root, "transactionDate", "transactionTime", "time");
                bank = Text(root, "bankName", "bankCode", "bankBin", "bank");
            }
        }
        catch (JsonException) { /* not JSON: show the raw body only */ }
        return new(id, kind, status, receivedAtUtc, amount, content, account, code, time, bank, rawBody);
    }

    private static string? Text(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            {
                var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        return null;
    }

    private static decimal? Number(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
            }
        return null;
    }
}
