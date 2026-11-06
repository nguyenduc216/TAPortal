using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

/// <summary>dbo.CustomerInvoiceSettings and the invoice columns of dbo.CustomerOrders (migration 022). Every call is filtered by customer.</summary>
public sealed class SqlOrderInvoiceStore(IConfiguration configuration) : IOrderInvoiceStore
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

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);

    public async Task<CustomerInvoiceSettings> SettingsAsync(Guid customerId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
SELECT Provider,ProviderAccountId,ProviderTaxCode,ProviderUsername,InvoicePattern,InvoiceSeries,AutoIssueEnabled,SendEmail,AutoIssueFrom,ConnectedAt
FROM dbo.CustomerInvoiceSettings WHERE CustomerId=@c
""", ("@c", customerId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)
            ? new(customerId, r.GetString(0), r.IsDBNull(1) ? null : r.GetInt64(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5), r.GetBoolean(6), r.GetBoolean(7), D(r, 8), D(r, 9))
            : CustomerInvoiceSettings.Empty(customerId);
    }

    public async Task SaveSettingsAsync(CustomerInvoiceSettings s, Guid actor, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
UPDATE dbo.CustomerInvoiceSettings SET Provider=@p,ProviderAccountId=@acc,ProviderTaxCode=@tax,ProviderUsername=@user,InvoicePattern=@pattern,InvoiceSeries=@series,
 AutoIssueEnabled=@auto,SendEmail=@email,AutoIssueFrom=@from,ConnectedAt=@connected,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor WHERE CustomerId=@c;
IF @@ROWCOUNT=0 INSERT dbo.CustomerInvoiceSettings(CustomerId,Provider,ProviderAccountId,ProviderTaxCode,ProviderUsername,InvoicePattern,InvoiceSeries,AutoIssueEnabled,SendEmail,AutoIssueFrom,ConnectedAt,UpdatedBy)
 VALUES(@c,@p,@acc,@tax,@user,@pattern,@series,@auto,@email,@from,@connected,@actor);
""", ("@c", s.CustomerId), ("@p", s.Provider), ("@acc", s.ProviderAccountId), ("@tax", s.ProviderTaxCode), ("@user", s.ProviderUsername), ("@pattern", s.InvoicePattern),
            ("@series", s.InvoiceSeries), ("@auto", s.AutoIssueEnabled), ("@email", s.SendEmail), ("@from", s.AutoIssueFrom), ("@connected", s.ConnectedAt), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid CustomerId, Guid OrderId)>> DueAsync(int max, DateTime utcNow, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, """
SELECT TOP (@max) o.CustomerId,o.Id
FROM dbo.CustomerOrders o JOIN dbo.CustomerInvoiceSettings s ON s.CustomerId=o.CustomerId
WHERE o.Status='PAID' AND o.AutoIssueInvoice=1 AND o.InvoiceStatus IN ('NONE','PENDING')
  AND s.AutoIssueEnabled=1 AND s.ProviderAccountId IS NOT NULL AND s.InvoicePattern IS NOT NULL AND s.InvoiceSeries IS NOT NULL
  AND s.AutoIssueFrom IS NOT NULL AND o.PaidAt>=s.AutoIssueFrom
  AND (o.InvoiceNextAttemptAt IS NULL OR o.InvoiceNextAttemptAt<=@now) AND (o.InvoiceLockedUntil IS NULL OR o.InvoiceLockedUntil<@now)
ORDER BY o.PaidAt
""", ("@max", max), ("@now", utcNow));
        var list = new List<(Guid, Guid)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add((r.GetGuid(0), r.GetGuid(1)));
        return list;
    }

    public async Task<OrderInvoiceJob?> ClaimAsync(Guid customerId, Guid orderId, DateTime utcNow, DateTime leaseUntil, bool manual, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        string orderNo;
        string? code;
        await using (var pick = Command(cn, tx, """
SELECT OrderNo,InvoiceCode FROM dbo.CustomerOrders WITH (UPDLOCK,ROWLOCK)
WHERE Id=@id AND CustomerId=@c AND Status='PAID'
  AND (InvoiceStatus IN ('NONE','PENDING') OR (@manual=1 AND InvoiceStatus='FAILED'))
  AND (InvoiceLockedUntil IS NULL OR InvoiceLockedUntil<@now)
""", ("@id", orderId), ("@c", customerId), ("@manual", manual), ("@now", utcNow)))
        await using (var r = await pick.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            orderNo = r.GetString(0);
            code = S(r, 1);
        }
        code ??= OrderInvoiceRules.InvoiceCode(orderNo, orderId);
        await using (var claim = Command(cn, tx, """
UPDATE dbo.CustomerOrders SET InvoiceStatus='PENDING',InvoiceCode=@code,InvoiceLockedUntil=@lease,
 InvoiceAttempts=CASE WHEN @manual=1 THEN 1 ELSE InvoiceAttempts+1 END,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND CustomerId=@c
""", ("@id", orderId), ("@c", customerId), ("@code", code), ("@lease", leaseUntil), ("@manual", manual)))
            await claim.ExecuteNonQueryAsync(ct);

        // The tax code and invoice name the payer corrected on the order's payment page (migration 024) replace the buyer
        // profile; a tax code given for a retail or unknown buyer makes the invoice a company invoice.
        OrderInvoiceJob job;
        await using (var read = Command(cn, tx, """
SELECT o.OrderNo,o.Description,o.InvoiceCode,o.InvoiceAttempts,m.MerchantId,
 CASE WHEN o.InvoiceBuyerTaxCode IS NOT NULL AND ISNULL(b.BuyerType,'INDIVIDUAL') NOT IN ('HOUSEHOLD','BUSINESS','ORGANIZATION') THEN 'BUSINESS' ELSE b.BuyerType END,
 COALESCE(o.InvoiceBuyerName,b.Name),COALESCE(o.InvoiceBuyerName,b.LegalName),COALESCE(o.InvoiceBuyerTaxCode,b.TaxCode),b.Address,b.Phone,b.Email
FROM dbo.CustomerOrders o
LEFT JOIN dbo.CustomerBuyers b ON b.Id=o.ContactId AND b.CustomerId=o.CustomerId
OUTER APPLY (SELECT TOP 1 tm.MerchantId FROM dbo.TingeeMerchants tm WHERE tm.CustomerId=o.CustomerId ORDER BY tm.CreatedAt DESC) m
WHERE o.Id=@id AND o.CustomerId=@c
""", ("@id", orderId), ("@c", customerId)))
        await using (var r = await read.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            job = new(customerId, orderId, r.GetString(0), S(r, 1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11), []);
        }
        var lines = new List<OrderInvoiceLine>();
        await using (var linesCmd = Command(cn, tx, "SELECT LineNumber,ItemName,UnitName,Quantity,UnitPrice,TaxRateCode FROM dbo.CustomerOrderLines WHERE OrderId=@id ORDER BY LineNumber", ("@id", orderId)))
        await using (var r = await linesCmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) lines.Add(new(r.GetInt32(0), r.GetString(1), S(r, 2), r.GetDecimal(3), r.GetDecimal(4), r.GetString(5)));
        await tx.CommitAsync(ct);
        return job with { Lines = lines };
    }

    public Task CompleteAsync(Guid customerId, Guid orderId, OrderInvoiceIssued issued, string? note, CancellationToken ct = default) => Execute(ct, """
UPDATE dbo.CustomerOrders SET InvoiceStatus='ISSUED',InvoiceNumber=ISNULL(@number,InvoiceNumber),InvoiceLookupCode=ISNULL(@lookup,InvoiceLookupCode),
 InvoiceTrackingCode=ISNULL(@tracking,InvoiceTrackingCode),InvoiceIssuedAt=ISNULL(InvoiceIssuedAt,SYSUTCDATETIME()),InvoiceError=@note,
 InvoiceLockedUntil=NULL,InvoiceNextAttemptAt=NULL,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND CustomerId=@c
""", ("@id", orderId), ("@c", customerId), ("@number", Cut(issued.InvoiceNumber, 40)), ("@lookup", Cut(issued.LookupCode, 60)), ("@tracking", Cut(issued.TrackingCode, 100)), ("@note", Cut(note, 500)));

    public Task RetryLaterAsync(Guid customerId, Guid orderId, DateTime nextAttemptAt, string error, CancellationToken ct = default) => Execute(ct, """
UPDATE dbo.CustomerOrders SET InvoiceStatus='PENDING',InvoiceError=@error,InvoiceNextAttemptAt=@next,InvoiceLockedUntil=NULL,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND CustomerId=@c AND InvoiceStatus='PENDING'
""", ("@id", orderId), ("@c", customerId), ("@error", Cut(error, 500)), ("@next", nextAttemptAt));

    public Task FailAsync(Guid customerId, Guid orderId, string error, CancellationToken ct = default) => Execute(ct, """
UPDATE dbo.CustomerOrders SET InvoiceStatus='FAILED',InvoiceError=@error,InvoiceNextAttemptAt=NULL,InvoiceLockedUntil=NULL,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND CustomerId=@c AND InvoiceStatus='PENDING'
""", ("@id", orderId), ("@c", customerId), ("@error", Cut(error, 500)));

    public Task MarkEmailedAsync(Guid customerId, Guid orderId, CancellationToken ct = default) =>
        Execute(ct, "UPDATE dbo.CustomerOrders SET InvoiceEmailedAt=SYSUTCDATETIME() WHERE Id=@id AND CustomerId=@c", ("@id", orderId), ("@c", customerId));

    private async Task Execute(CancellationToken ct, string sql, params (string Name, object? Value)[] args)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, null, sql, args);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? Cut(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}

/// <summary>Issues automatic invoices of paid orders in the webhook site, shortly after the payment that completed them.</summary>
public sealed class OrderInvoiceWorker(IServiceScopeFactory scopes, ILogger<OrderInvoiceWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ORDER_INVOICE_WORKER_STARTED interval={Interval}", Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OrderInvoicer>().RunOnceAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "ORDER_INVOICE_WORKER_ERROR"); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
