using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// dbo.TingeeWebhookInbox (migration 011; Status UNMATCHED from 019), dbo.CustomerOrders / CustomerBankAccounts /
/// TingeeMerchants (011-012) and dbo.PaymentTransactions (019).
/// </summary>
public sealed class SqlTingeePaymentLedger(IConfiguration configuration) : ITingeePaymentLedger
{
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"]);
        await cn.OpenAsync(ct);
        return cn;
    }

    public async Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("SELECT TOP (@max) Id,RawBody,Attempts,ReceivedAt FROM dbo.TingeeWebhookInbox WHERE Kind='Payment' AND Status='RECEIVED' ORDER BY Id", cn);
        cmd.Parameters.AddWithValue("@max", max);
        var list = new List<TingeePendingPayment>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetInt32(2), DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)));
        return list;
    }

    /// <summary>
    /// Orders (any state) whose receiving account is the account the money arrived in (VA, or its root account number:
    /// a payment straight into a root account still belongs to that account's orders; operator 2026-11-12),
    /// plus any order whose dynamic QR billId appears in the webhook.
    /// </summary>
    public async Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct)
    {
        var bills = notice.AdditionalValues.Where(v => v.Length <= 64).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        var billFilter = bills.Count == 0 ? "" : " OR o.BillId IN (" + string.Join(",", bills.Select((_, i) => "@b" + i)) + ")";
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand($"""
SELECT o.CustomerId,o.Id,o.OrderNo,o.BillId,o.Amount,o.PaidAmount,o.QrType,o.QrAccount,m.MerchantId,o.Status,o.ExpiresAt
FROM dbo.CustomerOrders o
LEFT JOIN dbo.CustomerBankAccounts b ON b.Id=o.BankAccountId AND b.CustomerId=o.CustomerId
OUTER APPLY (SELECT TOP 1 tm.MerchantId FROM dbo.TingeeMerchants tm WHERE tm.CustomerId=o.CustomerId ORDER BY tm.CreatedAt DESC) m
WHERE o.CreatedAt<=@received
  AND ((@va IS NOT NULL AND b.VaAccountNumber=@va)
    OR (@acc IS NOT NULL AND (b.VaAccountNumber=@acc OR b.AccountNumber=@acc)){billFilter})
""", cn);
        cmd.Parameters.AddWithValue("@va", (object?)notice.VaAccountNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acc", (object?)notice.AccountNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@received", receivedAtUtc);
        for (var i = 0; i < bills.Count; i++) cmd.Parameters.AddWithValue("@b" + i, bills[i]);
        var list = new List<TingeePaymentCandidate>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var recurring = !r.IsDBNull(6) && r.GetString(6) == TingeeQrTypes.Recurring;
            list.Add(new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetDecimal(4), r.GetDecimal(5),
                recurring && !r.IsDBNull(7) ? r.GetString(7) : null, recurring && !r.IsDBNull(8) ? r.GetInt64(8) : null, r.GetString(9),
                r.IsDBNull(10) ? null : DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc)));
        }
        return list;
    }

    // Columns copied from the webhook into dbo.PaymentTransactions (cut to the column sizes).
    private static void AddNotice(SqlCommand cmd, TingeePaymentNotice? n, DateTime receivedAtUtc)
    {
        cmd.Parameters.AddWithValue("@direction", n is null || n.IsCredit ? "CREDIT" : "DEBIT");
        cmd.Parameters.AddWithValue("@tx", (object?)Cut(n?.TransactionCode, 100) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@amount", Math.Max(0, Math.Round(n?.Amount ?? 0, 0)));
        cmd.Parameters.AddWithValue("@content", (object?)Cut(n?.Content, 500) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bank", (object?)Cut(n?.Bank, 30) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@acc", (object?)Cut(n?.AccountNumber, 50) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@va", (object?)Cut(n?.VaAccountNumber, 50) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@txAt", (object?)n?.TransactionAtUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@received", receivedAtUtc);
    }

    private static string? Cut(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    public async Task<bool> ApplyAsync(TingeePaymentApplication p, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("""
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE dbo.TingeeWebhookInbox SET Status='PROCESSED',ProcessedAt=SYSUTCDATETIME(),Attempts=Attempts+1,LastError=@note WHERE Id=@id AND Status='RECEIVED';
IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END
UPDATE dbo.CustomerOrders SET PaidAmount=@paid,Status=@status,
 PartnerPaidAmount=CASE WHEN @partner IS NULL THEN PartnerPaidAmount ELSE @partner END,
 PartnerCheckedAt=CASE WHEN @partner IS NULL THEN PartnerCheckedAt ELSE SYSUTCDATETIME() END,
 InvoiceStatus=CASE WHEN @status='PAID' AND AutoIssueInvoice=1 AND InvoiceStatus='NONE' THEN 'PENDING' ELSE InvoiceStatus END,
 PaidAt=CASE WHEN @paid>=Amount AND PaidAt IS NULL THEN SYSUTCDATETIME() ELSE PaidAt END,
 LastError=NULL,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@order AND CustomerId=@c AND PaidAmount=@expected AND Status=@expectedStatus;
IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END
INSERT dbo.PaymentTransactions(InboxId,Direction,TransactionCode,Amount,Content,Bank,AccountNumber,VaAccountNumber,TransactionAt,ReceivedAt,
 CustomerId,OrderId,Status,Flags,Reason,MatchedBy,SystemPaidBefore,SystemPaidAfter,PartnerPaidTotal,OrderStatusAfter)
VALUES(@id,@direction,@tx,@amount,@content,@bank,@acc,@va,@txAt,@received,@c,@order,@txStatus,@flags,@note,@by,@expected,@paid,@partner,@status);
COMMIT;
SELECT 1;
""", cn);
        AddNotice(cmd, p.Notice, p.ReceivedAtUtc);
        cmd.Parameters.AddWithValue("@id", p.InboxId);
        cmd.Parameters.AddWithValue("@note", p.Order.OrderNo);
        cmd.Parameters.AddWithValue("@order", p.Order.OrderId);
        cmd.Parameters.AddWithValue("@c", p.Order.CustomerId);
        cmd.Parameters.AddWithValue("@paid", p.PaidAfter);
        cmd.Parameters.AddWithValue("@expected", p.PaidBefore);
        cmd.Parameters.AddWithValue("@expectedStatus", p.Order.Status);
        cmd.Parameters.AddWithValue("@status", p.OrderStatus);
        cmd.Parameters.AddWithValue("@partner", (object?)p.PartnerPaid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@txStatus", p.TxStatus);
        cmd.Parameters.AddWithValue("@flags", p.Flags.Count == 0 ? DBNull.Value : string.Join(",", p.Flags));
        cmd.Parameters.AddWithValue("@by", p.MatchedBy);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task RecordAsync(long inboxId, TingeePaymentNotice? notice, DateTime receivedAtUtc, string inboxStatus, string txStatus, string reason, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        // The customer is the single owner of the receiving account (none when unknown or shared): it sees the transaction
        // on its own reconciliation page; T.A sees every transaction.
        await using var cmd = new SqlCommand("""
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE dbo.TingeeWebhookInbox SET Status=@inboxStatus,ProcessedAt=SYSUTCDATETIME(),Attempts=Attempts+1,LastError=@reason WHERE Id=@id AND Status='RECEIVED';
IF @@ROWCOUNT=0 BEGIN ROLLBACK; RETURN; END
DECLARE @owners int, @customer uniqueidentifier;
SELECT @owners=COUNT(DISTINCT b.CustomerId) FROM dbo.CustomerBankAccounts b
WHERE b.Status<>'DELETED' AND ((@va IS NOT NULL AND b.VaAccountNumber=@va) OR (@acc IS NOT NULL AND (b.VaAccountNumber=@acc OR b.AccountNumber=@acc)));
IF @owners=1
 SELECT TOP 1 @customer=b.CustomerId FROM dbo.CustomerBankAccounts b
 WHERE b.Status<>'DELETED' AND ((@va IS NOT NULL AND b.VaAccountNumber=@va) OR (@acc IS NOT NULL AND (b.VaAccountNumber=@acc OR b.AccountNumber=@acc)));
INSERT dbo.PaymentTransactions(InboxId,Direction,TransactionCode,Amount,Content,Bank,AccountNumber,VaAccountNumber,TransactionAt,ReceivedAt,CustomerId,Status,Reason)
VALUES(@id,@direction,@tx,@amount,@content,@bank,@acc,@va,@txAt,@received,@customer,@txStatus,@reason);
COMMIT;
""", cn);
        AddNotice(cmd, notice, receivedAtUtc);
        cmd.Parameters.AddWithValue("@id", inboxId);
        cmd.Parameters.AddWithValue("@inboxStatus", inboxStatus);
        cmd.Parameters.AddWithValue("@txStatus", txStatus);
        cmd.Parameters.AddWithValue("@reason", Cut(reason, 500)!);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task RetryLaterAsync(long inboxId, string note, CancellationToken ct) => Mark(inboxId, TingeeInboxStatus.Received, note, ct);

    public Task FailAsync(long inboxId, string note, CancellationToken ct) => Mark(inboxId, TingeeInboxStatus.Failed, note, ct);

    private async Task Mark(long inboxId, string status, string note, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("""
UPDATE dbo.TingeeWebhookInbox SET Status=@status,Attempts=Attempts+1,LastError=@note,
 ProcessedAt=CASE WHEN @status='RECEIVED' THEN ProcessedAt ELSE SYSUTCDATETIME() END
WHERE Id=@id AND Status='RECEIVED'
""", cn);
        cmd.Parameters.AddWithValue("@id", inboxId);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@note", Cut(note, 1000)!);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// Runs <see cref="TingeePaymentProcessor"/> every few seconds in the webhook site, so a payment is applied to its order
/// shortly after Tingee reports it, with nobody on the order page. Also picks up rows left pending by a restart.
/// </summary>
public sealed class TingeePaymentWorker(IServiceScopeFactory scopes, ILogger<TingeePaymentWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TINGEE_PAYMENT_WORKER_STARTED interval={Interval}", Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TingeePaymentProcessor>().RunOnceAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "TINGEE_PAYMENT_WORKER_ERROR"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
