using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Integrations.Notifications;

/// <summary>
/// dbo.NotificationChannels / CustomerNotificationRecipients / NotificationOutbox (migration 020). Every channel call takes
/// its owner (scope + customer id) and filters by it in SQL.
/// </summary>
public interface INotificationStore
{
    Task<IReadOnlyList<NotificationChannel>> ChannelsAsync(string scope, Guid? customerId, CancellationToken ct = default);
    /// <summary>Encrypted secrets and their version of one owner's channel; null when it is not configured.</summary>
    Task<(byte[]? Secret, int Version)?> SecretAsync(string scope, Guid? customerId, string channel, CancellationToken ct = default);
    Task SaveChannelAsync(string scope, Guid? customerId, string channel, bool enabled, string events, IReadOnlyDictionary<string, string> settings, byte[]? secret, Guid actor, CancellationToken ct = default);
    /// <summary>Replaces the secrets only if nobody changed them since <paramref name="expectedVersion"/> (Zalo refresh tokens are single use).</summary>
    Task<bool> ReplaceSecretAsync(string scope, Guid? customerId, string channel, int expectedVersion, byte[] secret, CancellationToken ct = default);
    Task RecordTestAsync(string scope, Guid? customerId, string channel, string result, CancellationToken ct = default);

    Task<NotificationRecipients> RecipientsAsync(Guid customerId, CancellationToken ct = default);
    Task SaveRecipientsAsync(Guid customerId, NotificationRecipients recipients, Guid actor, CancellationToken ct = default);

    /// <summary>Recent applied/unmatched transactions not yet planned (not older than <paramref name="sinceUtc"/>, so enabling a channel never floods old ones).</summary>
    Task<IReadOnlyList<NotifiableTransaction>> UnnotifiedAsync(DateTime sinceUtc, int max, CancellationToken ct = default);
    /// <summary>Queues the messages (skipping EventKeys already queued) and marks the transaction notified, in one transaction.</summary>
    Task EnqueueAsync(long transactionId, IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default);
    Task<IReadOnlyList<PendingMessage>> DueAsync(int max, CancellationToken ct = default);
    Task MarkSentAsync(long id, CancellationToken ct = default);
    Task MarkFailedAsync(long id, string error, DateTime? retryAtUtc, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxEntry>> RecentAsync(string? scope, Guid? customerId, int max, CancellationToken ct = default);
}

public sealed class SqlNotificationStore(IConfiguration configuration) : INotificationStore
{
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync(ct);
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private const string Owner = "Scope=@scope AND ((@c IS NULL AND CustomerId IS NULL) OR CustomerId=@c)";

    public async Task<IReadOnlyList<NotificationChannel>> ChannelsAsync(string scope, Guid? customerId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, $"SELECT Id,Scope,CustomerId,Channel,IsEnabled,Events,Settings,Secret,SecretVersion,LastTestAt,LastTestResult FROM dbo.NotificationChannels WHERE {Owner}",
            ("@scope", scope), ("@c", customerId));
        var list = new List<NotificationChannel>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.GetString(3), r.GetBoolean(4), NotificationEvent.Parse(r.GetString(5)),
                JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(6)) ?? [], new HashSet<string>(), r.GetInt32(8),
                r.IsDBNull(9) ? null : DateTime.SpecifyKind(r.GetDateTime(9), DateTimeKind.Utc), r.IsDBNull(10) ? null : r.GetString(10)));
        return list;
    }

    public async Task<(byte[]? Secret, int Version)?> SecretAsync(string scope, Guid? customerId, string channel, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, $"SELECT Secret,SecretVersion FROM dbo.NotificationChannels WHERE {Owner} AND Channel=@ch", ("@scope", scope), ("@c", customerId), ("@ch", channel));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.IsDBNull(0) ? null : (byte[])r[0], r.GetInt32(1)) : null;
    }

    public async Task SaveChannelAsync(string scope, Guid? customerId, string channel, bool enabled, string events, IReadOnlyDictionary<string, string> settings, byte[]? secret, Guid actor, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, $"""
UPDATE dbo.NotificationChannels SET IsEnabled=@enabled,Events=@events,Settings=@settings,Secret=@secret,SecretVersion=SecretVersion+1,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE {Owner} AND Channel=@ch;
IF @@ROWCOUNT=0
 INSERT dbo.NotificationChannels(Scope,CustomerId,Channel,IsEnabled,Events,Settings,Secret,UpdatedAt,UpdatedBy) VALUES(@scope,@c,@ch,@enabled,@events,@settings,@secret,SYSUTCDATETIME(),@actor);
""", ("@scope", scope), ("@c", customerId), ("@ch", channel), ("@enabled", enabled), ("@events", events), ("@settings", JsonSerializer.Serialize(settings)), ("@actor", actor));
        cmd.Parameters.Add("@secret", System.Data.SqlDbType.VarBinary, 4000).Value = (object?)secret ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ReplaceSecretAsync(string scope, Guid? customerId, string channel, int expectedVersion, byte[] secret, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, $"UPDATE dbo.NotificationChannels SET Secret=@secret,SecretVersion=SecretVersion+1 WHERE {Owner} AND Channel=@ch AND SecretVersion=@v",
            ("@scope", scope), ("@c", customerId), ("@ch", channel), ("@v", expectedVersion));
        cmd.Parameters.Add("@secret", System.Data.SqlDbType.VarBinary, 4000).Value = secret;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task RecordTestAsync(string scope, Guid? customerId, string channel, string result, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, $"UPDATE dbo.NotificationChannels SET LastTestAt=SYSUTCDATETIME(),LastTestResult=@r WHERE {Owner} AND Channel=@ch",
            ("@scope", scope), ("@c", customerId), ("@ch", channel), ("@r", result.Length <= 500 ? result : result[..500]));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<NotificationRecipients> RecipientsAsync(Guid customerId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, "SELECT Email,ZaloPhone,TelegramChatId,Events FROM dbo.CustomerNotificationRecipients WHERE CustomerId=@c", ("@c", customerId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), NotificationEvent.Parse(r.GetString(3)))
            : NotificationRecipients.Empty;
    }

    public async Task SaveRecipientsAsync(Guid customerId, NotificationRecipients x, Guid actor, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
UPDATE dbo.CustomerNotificationRecipients SET Email=@email,ZaloPhone=@phone,TelegramChatId=@chat,Events=@events,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE CustomerId=@c;
IF @@ROWCOUNT=0 INSERT dbo.CustomerNotificationRecipients(CustomerId,Email,ZaloPhone,TelegramChatId,Events,UpdatedBy) VALUES(@c,@email,@phone,@chat,@events,@actor);
""", ("@c", customerId), ("@email", x.Email), ("@phone", x.ZaloPhone), ("@chat", x.TelegramChatId), ("@events", NotificationEvent.Join(x.Events)), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<NotifiableTransaction>> UnnotifiedAsync(DateTime sinceUtc, int max, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
SELECT TOP (@max) t.Id,t.Status,t.Flags,t.Reason,t.Amount,t.Content,ISNULL(t.VaAccountNumber,t.AccountNumber),ISNULL(t.TransactionAt,t.ReceivedAt),
 t.CustomerId,c.Name,t.OrderId,o.OrderNo,o.Amount,t.SystemPaidAfter,t.OrderStatusAfter,b.Name,b.Email,b.Phone
FROM dbo.PaymentTransactions t
LEFT JOIN dbo.Customers c ON c.Id=t.CustomerId
LEFT JOIN dbo.CustomerOrders o ON o.Id=t.OrderId AND o.CustomerId=t.CustomerId
LEFT JOIN dbo.CustomerBuyers b ON b.Id=o.ContactId AND b.CustomerId=o.CustomerId
WHERE t.NotifiedAt IS NULL AND t.Status IN ('MATCHED','REVIEW','UNMATCHED') AND t.CreatedAt>=@since
ORDER BY t.Id
""", ("@max", max), ("@since", sinceUtc));
        var list = new List<NotifiableTransaction>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetInt64(0), r.GetString(1), S(2), S(3), r.GetDecimal(4), S(5), S(6), DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Utc),
                r.IsDBNull(8) ? null : r.GetGuid(8), S(9), r.IsDBNull(10) ? null : r.GetGuid(10), S(11), r.IsDBNull(12) ? null : r.GetDecimal(12),
                r.IsDBNull(13) ? null : r.GetDecimal(13), S(14), S(15), S(16), S(17)));
        return list;
    }

    public async Task EnqueueAsync(long transactionId, IReadOnlyList<OutboxMessage> messages, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        foreach (var m in messages)
        {
            await using var insert = Command(cn, tx, """
IF NOT EXISTS (SELECT 1 FROM dbo.NotificationOutbox WHERE EventKey=@key)
 INSERT dbo.NotificationOutbox(EventKey,Scope,CustomerId,Channel,Recipient,Subject,Body,TemplateData,TransactionId) VALUES(@key,@scope,@c,@ch,@to,@subject,@body,@data,@tx);
""", ("@key", m.EventKey), ("@scope", m.Scope), ("@c", m.CustomerId), ("@ch", m.Channel), ("@to", m.Recipient), ("@subject", Cut(m.Subject, 300)), ("@body", Cut(m.Body, 4000)),
                ("@data", m.TemplateData is null ? null : JsonSerializer.Serialize(m.TemplateData)), ("@tx", m.TransactionId));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await using var mark = Command(cn, tx, "UPDATE dbo.PaymentTransactions SET NotifiedAt=SYSUTCDATETIME() WHERE Id=@id AND NotifiedAt IS NULL", ("@id", transactionId));
        await mark.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    public async Task<IReadOnlyList<PendingMessage>> DueAsync(int max, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
SELECT TOP (@max) Id,EventKey,Scope,CustomerId,Channel,Recipient,Subject,Body,TemplateData,TransactionId,Attempts
FROM dbo.NotificationOutbox WHERE Status='PENDING' AND NextAttemptAt<=SYSUTCDATETIME() ORDER BY Id
""", ("@max", max));
        var list = new List<PendingMessage>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetInt64(0), new(r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetGuid(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.GetString(7), r.IsDBNull(8) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(8)), r.IsDBNull(9) ? null : r.GetInt64(9)), r.GetInt32(10)));
        return list;
    }

    public async Task MarkSentAsync(long id, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, "UPDATE dbo.NotificationOutbox SET Status='SENT',SentAt=SYSUTCDATETIME(),Attempts=Attempts+1,LastError=NULL WHERE Id=@id", ("@id", id));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(long id, string error, DateTime? retryAtUtc, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, "UPDATE dbo.NotificationOutbox SET Status=CASE WHEN @retry IS NULL THEN 'FAILED' ELSE 'PENDING' END,NextAttemptAt=ISNULL(@retry,NextAttemptAt),Attempts=Attempts+1,LastError=@e WHERE Id=@id",
            ("@id", id), ("@retry", retryAtUtc), ("@e", Cut(error, 500)));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxEntry>> RecentAsync(string? scope, Guid? customerId, int max, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
SELECT TOP (@max) Id,Scope,CustomerId,Channel,Recipient,Subject,Status,Attempts,LastError,CreatedAt,SentAt FROM dbo.NotificationOutbox
WHERE (@scope IS NULL OR Scope=@scope) AND (@c IS NULL OR CustomerId=@c) ORDER BY Id DESC
""", ("@max", max), ("@scope", scope), ("@c", customerId));
        var list = new List<OutboxEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6),
                r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), DateTime.SpecifyKind(r.GetDateTime(9), DateTimeKind.Utc), r.IsDBNull(10) ? null : DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc)));
        return list;
    }
}
