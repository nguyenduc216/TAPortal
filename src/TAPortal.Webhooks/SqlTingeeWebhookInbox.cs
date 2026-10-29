using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Webhooks;

/// <summary>
/// dbo.TingeeWebhookInbox (migration 011). One row per (Kind, DedupKey): a retried webhook hits the unique index and is
/// treated as already stored, so Tingee still gets "00" and nothing is processed twice.
/// </summary>
public sealed class SqlTingeeWebhookInbox(IConfiguration configuration) : ITingeeWebhookInbox
{
    private string? ConnectionString => configuration["Database:ConnectionString"];

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ConnectionString);

    public async Task SaveAsync(TingeeWebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand("""
INSERT dbo.TingeeWebhookInbox(Kind,DedupKey,RequestId,RequestTimestamp,RawBody,ReceivedAt)
VALUES(@kind,@dedup,@requestId,@timestamp,@body,@received)
""", cn);
        cmd.Parameters.AddWithValue("@kind", envelope.Kind.ToString());
        cmd.Parameters.AddWithValue("@dedup", DedupKey(envelope));
        cmd.Parameters.AddWithValue("@requestId", (object?)envelope.RequestId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", envelope.RequestTimestamp);
        cmd.Parameters.AddWithValue("@body", envelope.RawBody);
        cmd.Parameters.AddWithValue("@received", envelope.ReceivedAtUtc.UtcDateTime);
        try { await cmd.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { /* duplicate delivery: already stored */ }
    }

    /// <summary>Payment: Tingee transactionCode. Otherwise x-request-id, else a hash of the body.</summary>
    public static string DedupKey(TingeeWebhookEnvelope envelope)
    {
        if (envelope.Kind == TingeeWebhookKind.Payment)
        {
            try
            {
                using var doc = JsonDocument.Parse(envelope.RawBody);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("transactionCode", out var code)
                    && code.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(code.GetString()))
                    return "tx:" + Truncate(code.GetString()!.Trim(), 140);
            }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(envelope.RequestId)) return "rq:" + Truncate(envelope.RequestId.Trim(), 140);
        return "sha:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.RawBody))).ToLowerInvariant();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
