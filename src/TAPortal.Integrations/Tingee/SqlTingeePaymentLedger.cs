using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// dbo.TingeeWebhookInbox (migration 011, columns Status/Attempts/ProcessedAt/LastError) and dbo.CustomerOrders /
/// CustomerBankAccounts / TingeeMerchants (011-012). No schema change.
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
    /// Open or expired orders whose receiving account is the account the money arrived in (VA, or the root account),
    /// plus any order whose dynamic QR billId appears in the webhook.
    /// </summary>
    public async Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct)
    {
        var bills = notice.AdditionalValues.Where(v => v.Length <= 64).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        var billFilter = bills.Count == 0 ? "" : " OR o.BillId IN (" + string.Join(",", bills.Select((_, i) => "@b" + i)) + ")";
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand($"""
SELECT o.CustomerId,o.Id,o.OrderNo,o.BillId,o.Amount,o.PaidAmount,o.QrType,o.QrAccount,m.MerchantId
FROM dbo.CustomerOrders o
LEFT JOIN dbo.CustomerBankAccounts b ON b.Id=o.BankAccountId AND b.CustomerId=o.CustomerId
OUTER APPLY (SELECT TOP 1 tm.MerchantId FROM dbo.TingeeMerchants tm WHERE tm.CustomerId=o.CustomerId ORDER BY tm.CreatedAt DESC) m
WHERE o.Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID','EXPIRED') AND o.CreatedAt<=@received
  AND ((@va IS NOT NULL AND b.VaAccountNumber=@va)
    OR (@acc IS NOT NULL AND (b.VaAccountNumber=@acc OR (b.AccountNumber=@acc AND b.IsNotifyAccountNumber=1))){billFilter})
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
                recurring && !r.IsDBNull(7) ? r.GetString(7) : null, recurring && !r.IsDBNull(8) ? r.GetInt64(8) : null));
        }
        return list;
    }

    public async Task<bool> ApplyAsync(long inboxId, TingeePaymentCandidate order, decimal expectedPaid, decimal newPaid, string note, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("""
SET XACT_ABORT ON;
BEGIN TRAN;
UPDATE dbo.TingeeWebhookInbox SET Status='PROCESSED',ProcessedAt=SYSUTCDATETIME(),Attempts=Attempts+1,LastError=@note WHERE Id=@id AND Status='RECEIVED';
IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END
UPDATE dbo.CustomerOrders SET PaidAmount=@paid,
 Status=CASE WHEN @paid>=Amount THEN 'PAID' WHEN @paid>0 THEN 'PARTIALLY_PAID' ELSE Status END,
 InvoiceStatus=CASE WHEN @paid>=Amount AND AutoIssueInvoice=1 AND InvoiceStatus='NONE' THEN 'PENDING' ELSE InvoiceStatus END,
 PaidAt=CASE WHEN @paid>=Amount AND PaidAt IS NULL THEN SYSUTCDATETIME() ELSE PaidAt END,
 LastError=NULL,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@order AND CustomerId=@c AND PaidAmount=@expected AND Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID','EXPIRED');
IF @@ROWCOUNT=0 BEGIN ROLLBACK; SELECT 0; RETURN; END
COMMIT;
SELECT 1;
""", cn);
        cmd.Parameters.AddWithValue("@id", inboxId);
        cmd.Parameters.AddWithValue("@note", note);
        cmd.Parameters.AddWithValue("@order", order.OrderId);
        cmd.Parameters.AddWithValue("@c", order.CustomerId);
        cmd.Parameters.AddWithValue("@paid", newPaid);
        cmd.Parameters.AddWithValue("@expected", expectedPaid);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task MarkAsync(long inboxId, string status, string note, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("""
UPDATE dbo.TingeeWebhookInbox SET Status=@status,Attempts=Attempts+1,LastError=@note,
 ProcessedAt=CASE WHEN @status='RECEIVED' THEN ProcessedAt ELSE SYSUTCDATETIME() END
WHERE Id=@id AND Status='RECEIVED'
""", cn);
        cmd.Parameters.AddWithValue("@id", inboxId);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@note", note);
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
