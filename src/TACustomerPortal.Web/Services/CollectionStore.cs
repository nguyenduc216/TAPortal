using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Services;

// Payment collections ("Thu gộp công nợ", migration 069; operator 2026-11-27): one QR for several unpaid orders of one
// customer. The money is split over the orders by the payment processor (TingeeCollectionRules); this store only creates
// collections, keeps their QR and cancels them. Every method filters by the signed-in customer's id in SQL.

public sealed record CollectionOrder(Guid OrderId, string OrderNo, int Position, decimal DueAtCreate, decimal Amount, decimal PaidAmount, string Status,
    string? Description, DateTime CreatedAt, decimal Allocated)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);
    public TingeeCollectionOrder ForRules => new(OrderId, OrderNo, Position, Amount, PaidAmount, Status);
}

public sealed record Collection(
    Guid Id, string CollectionNo, Guid? ContactId, string? ContactName, Guid? BankAccountId, string? Description, decimal Amount, decimal PaidAmount,
    string Status, string? QrType, string? QrPayload, string? QrAccount, string? BillId, int QrExpireMinutes, DateTime? ExpiresAt, string? LastError,
    DateTime CreatedAt, decimal? PartnerPaidAmount, IReadOnlyList<CollectionOrder> Orders, int OrderCount)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);

    /// <summary>
    /// What is still to collect: the amount asked minus what was paid, but never more than the orders still owe (an order may
    /// have been paid through its own QR meanwhile). Only known when the orders were read.
    /// </summary>
    public decimal Due => Orders.Count == 0 ? Remaining : Math.Min(Remaining, TingeeCollectionRules.Due(Orders.Select(o => o.ForRules).ToList()));
}

/// <summary>One part of a payment to a collection: the bank transaction and the order it paid.</summary>
public sealed record CollectionAllocation(long TransactionId, DateTime AtUtc, decimal TransactionAmount, string? TransactionCode, string OrderNo, Guid OrderId, decimal Amount, string OrderStatusAfter);

/// <summary>A payment to a collection, with how it was split.</summary>
public sealed record CollectionPaymentRow(long TransactionId, DateTime AtUtc, decimal Amount, string? TransactionCode, string Status, string? Flags, IReadOnlyList<CollectionAllocation> Split)
{
    public decimal Unallocated => Amount - Split.Sum(s => s.Amount);
}

public sealed record NewCollection(Guid ContactId, Guid BankAccountId, string? Description, decimal Amount, int QrExpireMinutes, IReadOnlyList<Guid> OrderIds);

/// <param name="Error">Why nothing was created (an order changed meanwhile, or is already in another open collection).</param>
public sealed record CollectionCreated(Guid Id, string CollectionNo, string? Error = null);

/// <summary>A collection reached through its public page token.</summary>
public sealed record PayPageCollection(Guid CustomerId, Guid CollectionId, string SellerName, string? SellerTaxCode);

public static class CollectionMath
{
    /// <summary>An open collection past its QR expiry with money still due is shown as expired.</summary>
    public static string EffectiveStatus(Collection c, DateTime utcNow) =>
        PaymentCollectionStatus.IsOpen(c.Status) && c.ExpiresAt is { } e && e <= utcNow && c.PaidAmount < c.Amount ? PaymentCollectionStatus.Expired : c.Status;

    /// <summary>Orders a collection may take: this buyer's, still owing money, open or with an expired QR.</summary>
    public static bool IsDebt(Order o, DateTime utcNow) =>
        o.Remaining > 0 && OrderMath.EffectiveStatus(o, utcNow) is OrderStatus.Draft or OrderStatus.Awaiting or OrderStatus.Partial or OrderStatus.Expired;

    public static string TransferContent(string collectionNo) => OrderMath.TransferContent(collectionNo);
}

public interface IPaymentCollectionStore
{
    Task<IReadOnlyList<Collection>> CollectionsAsync(Guid customerId);
    /// <summary>The collection with its orders as they stand now (amount paid, status) and what each received from it.</summary>
    Task<Collection?> CollectionAsync(Guid customerId, Guid id);
    Task<IReadOnlyList<CollectionPaymentRow>> PaymentsAsync(Guid customerId, Guid id);
    /// <summary>Order id → the open collection it belongs to.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, string CollectionNo)>> OpenCollectionsByOrderAsync(Guid customerId);
    /// <summary>
    /// Creates the collection (TT-yyMMdd-NNNN) with its orders in the given order, in one transaction that re-checks every
    /// order: this buyer's, still owing, open or expired, and in no other open collection.
    /// </summary>
    Task<CollectionCreated> CreateAsync(Guid customerId, NewCollection collection, Guid actor);
    /// <summary>Writes the QR, its expiry, the receiving account and the last error; never the amount paid or the status.</summary>
    Task UpdateQrAsync(Guid customerId, Collection collection, Guid actor);
    /// <summary>Cancels an open collection (money already received stays on its orders). False when it is paid or already cancelled.</summary>
    Task<bool> CancelAsync(Guid customerId, Guid id, Guid actor);
    Task<string?> TokenAsync(Guid customerId, Guid id);
    Task<PayPageCollection?> FindAsync(string token);
}

public sealed class SqlPaymentCollectionStore(IConfiguration configuration) : IPaymentCollectionStore
{
    private async Task<SqlConnection> Open()
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private const string Select = """
SELECT pc.Id,pc.CollectionNo,pc.ContactId,b.Name,pc.BankAccountId,pc.Description,pc.Amount,pc.PaidAmount,pc.Status,pc.QrType,pc.QrPayload,pc.QrAccount,pc.BillId,
 pc.QrExpireMinutes,pc.ExpiresAt,pc.LastError,pc.CreatedAt,pc.PartnerPaidAmount,(SELECT COUNT(*) FROM dbo.PaymentCollectionOrders co WHERE co.CollectionId=pc.Id)
FROM dbo.PaymentCollections pc LEFT JOIN dbo.CustomerBuyers b ON b.Id=pc.ContactId AND b.CustomerId=pc.CustomerId
""";

    private static Collection Read(SqlDataReader r, IReadOnlyList<CollectionOrder> orders) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetGuid(2), S(r, 3),
        r.IsDBNull(4) ? null : r.GetGuid(4), S(r, 5), r.GetDecimal(6), r.GetDecimal(7), r.GetString(8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), r.GetInt32(13),
        r.IsDBNull(14) ? null : r.GetDateTime(14), S(r, 15), r.GetDateTime(16), r.IsDBNull(17) ? null : r.GetDecimal(17), orders, r.GetInt32(18));

    public async Task<IReadOnlyList<Collection>> CollectionsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, null, Select + " WHERE pc.CustomerId=@c ORDER BY pc.CreatedAt DESC", ("@c", customerId));
        var list = new List<Collection>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r, []));
        return list;
    }

    public async Task<Collection?> CollectionAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        var orders = new List<CollectionOrder>();
        await using (var oc = Command(cn, null, """
SELECT o.Id,o.OrderNo,co.Position,co.DueAtCreate,o.Amount,o.PaidAmount,o.Status,o.Description,o.CreatedAt,
 ISNULL((SELECT SUM(a.Amount) FROM dbo.PaymentAllocations a WHERE a.CollectionId=co.CollectionId AND a.OrderId=o.Id),0)
FROM dbo.PaymentCollectionOrders co
JOIN dbo.PaymentCollections pc ON pc.Id=co.CollectionId AND pc.CustomerId=@c
JOIN dbo.CustomerOrders o ON o.Id=co.OrderId AND o.CustomerId=@c
WHERE co.CollectionId=@id ORDER BY co.Position
""", ("@c", customerId), ("@id", id)))
        await using (var r = await oc.ExecuteReaderAsync())
            while (await r.ReadAsync())
                orders.Add(new(r.GetGuid(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5), r.GetString(6), S(r, 7), r.GetDateTime(8), r.GetDecimal(9)));
        await using var cmd = Command(cn, null, Select + " WHERE pc.CustomerId=@c AND pc.Id=@id", ("@c", customerId), ("@id", id));
        await using var cr = await cmd.ExecuteReaderAsync();
        return await cr.ReadAsync() ? Read(cr, orders) : null;
    }

    public async Task<IReadOnlyList<CollectionPaymentRow>> PaymentsAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        var rows = new List<(long Id, DateTime At, decimal Amount, string? Code, string Status, string? Flags)>();
        await using (var tc = Command(cn, null, """
SELECT t.Id,ISNULL(t.TransactionAt,t.ReceivedAt),t.Amount,t.TransactionCode,t.Status,t.Flags
FROM dbo.PaymentTransactions t WHERE t.CustomerId=@c AND t.CollectionId=@id ORDER BY t.Id
""", ("@c", customerId), ("@id", id)))
        await using (var r = await tc.ExecuteReaderAsync())
            while (await r.ReadAsync()) rows.Add((r.GetInt64(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.GetDecimal(2), S(r, 3), r.GetString(4), S(r, 5)));
        var split = new List<CollectionAllocation>();
        await using (var ac = Command(cn, null, """
SELECT a.TransactionId,a.CreatedAt,t.Amount,t.TransactionCode,o.OrderNo,o.Id,a.Amount,a.OrderStatusAfter
FROM dbo.PaymentAllocations a
JOIN dbo.PaymentTransactions t ON t.Id=a.TransactionId AND t.CustomerId=@c
JOIN dbo.CustomerOrders o ON o.Id=a.OrderId AND o.CustomerId=@c
WHERE a.CollectionId=@id ORDER BY a.Id
""", ("@c", customerId), ("@id", id)))
        await using (var r = await ac.ExecuteReaderAsync())
            while (await r.ReadAsync())
                split.Add(new(r.GetInt64(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc), r.GetDecimal(2), S(r, 3), r.GetString(4), r.GetGuid(5), r.GetDecimal(6), r.GetString(7)));
        return rows.Select(t => new CollectionPaymentRow(t.Id, t.At, t.Amount, t.Code, t.Status, t.Flags, split.Where(s => s.TransactionId == t.Id).ToList())).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, (Guid Id, string CollectionNo)>> OpenCollectionsByOrderAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, null, """
SELECT co.OrderId,pc.Id,pc.CollectionNo FROM dbo.PaymentCollectionOrders co
JOIN dbo.PaymentCollections pc ON pc.Id=co.CollectionId
WHERE pc.CustomerId=@c AND pc.Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID')
""", ("@c", customerId));
        var map = new Dictionary<Guid, (Guid, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetGuid(0)] = (r.GetGuid(1), r.GetString(2));
        return map;
    }

    public async Task<CollectionCreated> CreateAsync(Guid customerId, NewCollection c, Guid actor)
    {
        if (c.OrderIds.Count == 0) return new(Guid.Empty, "", "Chọn ít nhất một đơn hàng.");
        await using var cn = await Open();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        // Every order again, locked: this buyer's, owing money, open or expired (stored AWAITING/PARTIAL past expiry count
        // as expired), and in no other open collection.
        var list = string.Join(",", c.OrderIds.Select((_, i) => "@o" + i));
        var args = new List<(string, object?)> { ("@c", customerId), ("@contact", c.ContactId) };
        args.AddRange(c.OrderIds.Select((id, i) => ("@o" + i, (object?)id)));
        var due = new Dictionary<Guid, decimal>();
        await using (var check = Command(cn, tx, $"""
SELECT o.Id,o.Amount-o.PaidAmount,
 CASE WHEN EXISTS (SELECT 1 FROM dbo.PaymentCollectionOrders co JOIN dbo.PaymentCollections pc ON pc.Id=co.CollectionId
                   WHERE co.OrderId=o.Id AND pc.Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID')) THEN 1 ELSE 0 END
FROM dbo.CustomerOrders o WITH (UPDLOCK,HOLDLOCK)
WHERE o.CustomerId=@c AND o.ContactId=@contact AND o.Id IN ({list})
  AND o.Status IN ('DRAFT','AWAITING_PAYMENT','PARTIALLY_PAID','EXPIRED') AND o.Amount>o.PaidAmount
""", [.. args]))
        await using (var r = await check.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                if (r.GetInt32(2) == 1) return new(Guid.Empty, "", "Có đơn hàng đã nằm trong một phiếu thu khác đang mở. Hủy phiếu thu đó trước hoặc bỏ chọn đơn.");
                due[r.GetGuid(0)] = r.GetDecimal(1);
            }
        if (due.Count != c.OrderIds.Distinct().Count()) return new(Guid.Empty, "", "Có đơn hàng vừa thay đổi (đã thu đủ, đã hủy hoặc không thuộc khách hàng này). Vui lòng chọn lại.");

        var prefix = PaymentCollectionNo.Prefix + DateTime.UtcNow.AddHours(7).ToString("yyMMdd") + "-";
        await using var next = Command(cn, tx, "SELECT ISNULL(MAX(CAST(RIGHT(CollectionNo,4) AS int)),0)+1 FROM dbo.PaymentCollections WITH (UPDLOCK,HOLDLOCK) WHERE CustomerId=@c AND CollectionNo LIKE @p",
            ("@c", customerId), ("@p", prefix + "%"));
        var number = prefix + Convert.ToInt32(await next.ExecuteScalarAsync()).ToString("0000");
        await using var insert = Command(cn, tx, """
INSERT dbo.PaymentCollections(CustomerId,CollectionNo,ContactId,BankAccountId,Description,Amount,Status,QrExpireMinutes,CreatedBy)
OUTPUT inserted.Id VALUES(@c,@no,@contact,(SELECT Id FROM dbo.CustomerBankAccounts WHERE Id=@bank AND CustomerId=@c),@desc,@amount,'AWAITING_PAYMENT',@minutes,@actor)
""", ("@c", customerId), ("@no", number), ("@contact", c.ContactId), ("@bank", c.BankAccountId), ("@desc", c.Description), ("@amount", c.Amount), ("@minutes", c.QrExpireMinutes), ("@actor", actor));
        var id = (Guid)(await insert.ExecuteScalarAsync())!;
        var position = 0;
        foreach (var orderId in c.OrderIds.Distinct())
        {
            await using var add = Command(cn, tx, "INSERT dbo.PaymentCollectionOrders(CollectionId,OrderId,Position,DueAtCreate) VALUES(@id,@o,@p,@due)",
                ("@id", id), ("@o", orderId), ("@p", ++position), ("@due", due[orderId]));
            await add.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return new(id, number);
    }

    public async Task UpdateQrAsync(Guid customerId, Collection c, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, null, """
UPDATE dbo.PaymentCollections SET BankAccountId=(SELECT Id FROM dbo.CustomerBankAccounts WHERE Id=@bank AND CustomerId=@c),QrType=@qrType,QrPayload=@payload,
 QrAccount=@qrAccount,BillId=@bill,QrExpireMinutes=@minutes,ExpiresAt=@expires,LastError=@error,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE CustomerId=@c AND Id=@id AND Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID')
""", ("@c", customerId), ("@id", c.Id), ("@bank", c.BankAccountId), ("@qrType", c.QrType), ("@payload", c.QrPayload), ("@qrAccount", c.QrAccount), ("@bill", c.BillId),
            ("@minutes", c.QrExpireMinutes), ("@expires", c.ExpiresAt), ("@error", c.LastError), ("@actor", actor));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> CancelAsync(Guid customerId, Guid id, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, null, """
UPDATE dbo.PaymentCollections SET Status='CANCELLED',CancelledAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE CustomerId=@c AND Id=@id AND Status IN ('AWAITING_PAYMENT','PARTIALLY_PAID')
""", ("@c", customerId), ("@id", id), ("@actor", actor));
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<string?> TokenAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, null, """
UPDATE dbo.PaymentCollections SET PublicToken=@t WHERE CustomerId=@c AND Id=@id AND PublicToken IS NULL;
SELECT PublicToken FROM dbo.PaymentCollections WHERE CustomerId=@c AND Id=@id;
""", ("@c", customerId), ("@id", id), ("@t", SqlOrderPageStore.NewToken()));
        return await cmd.ExecuteScalarAsync() as string;
    }

    public async Task<PayPageCollection?> FindAsync(string token)
    {
        await using var cn = await Open();
        await using var cmd = new SqlCommand("""
SELECT pc.CustomerId,pc.Id,c.Name,c.TaxCode FROM dbo.PaymentCollections pc JOIN dbo.Customers c ON c.Id=pc.CustomerId
WHERE pc.PublicToken=@t AND c.IsDeleted=0 AND c.IsActive=1
""", cn);
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 40).Value = token;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)) : null;
    }
}
