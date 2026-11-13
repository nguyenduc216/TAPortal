using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Integrations.Tingee;

/// <summary>One row of dbo.PaymentTransactions (migration 019) with the customer and order it belongs to.</summary>
public sealed record PaymentTransaction(
    long Id, string Direction, string? TransactionCode, decimal Amount, string? Content, string? Bank, string? AccountNumber, string? VaAccountNumber,
    DateTime? TransactionAtUtc, DateTime ReceivedAtUtc, Guid? CustomerId, string? CustomerName, Guid? OrderId, string? OrderNo, decimal? OrderAmount,
    string Status, string? Flags, string? Reason, string? MatchedBy, decimal? SystemPaidBefore, decimal? SystemPaidAfter, decimal? PartnerPaidTotal,
    string? OrderStatusAfter, DateTime? ResolvedAtUtc, string? ResolvedVia, string? ResolutionNote, bool Synced = false)
{
    /// <summary>Synced = fetched from the partner's history by "Đồng bộ" because its webhook (IPN) never arrived.</summary>
    public IReadOnlyList<string> FlagList => string.IsNullOrEmpty(Flags) ? [] : Flags.Split(',', StringSplitOptions.RemoveEmptyEntries);
    /// <summary>The partner reported a different total than the system counted.</summary>
    public bool PartnerDiffers => PartnerPaidTotal is decimal p && SystemPaidAfter is decimal s && p != s;
}

/// <summary>
/// Search on the reconciliation pages. <see cref="CustomerId"/> is the scope: the Customer Portal always passes the
/// signed-in customer (it never sees another customer's or an unattributed transaction); T.A Admin passes null for all,
/// or a customer to filter.
/// </summary>
public sealed record PaymentTxFilter(Guid? CustomerId, string? Status = null, DateTime? FromUtc = null, DateTime? ToUtc = null, string? Search = null,
    bool UnattributedOnly = false, int Max = 300);

public sealed record PaymentTxSummary(int Unmatched, decimal UnmatchedAmount, int Review, decimal ReviewAmount, int Matched, decimal MatchedAmount,
    int Resolved, decimal ResolvedAmount, int Ignored, decimal IgnoredAmount, int Credit = 0, decimal CreditAmount = 0, int Debit = 0, decimal DebitAmount = 0);

public sealed record PaymentTxResult(bool Ok, string? Error = null)
{
    public static readonly PaymentTxResult Success = new(true);
}

public static class PaymentTxVia
{
    public const string Admin = "ADMIN", Portal = "PORTAL";
}

/// <summary>Staff actions on dbo.PaymentTransactions; every method is limited to <c>scopeCustomerId</c> when it is set.</summary>
public interface IPaymentTransactionStore
{
    Task<IReadOnlyList<PaymentTransaction>> SearchAsync(PaymentTxFilter filter, CancellationToken ct = default);
    Task<PaymentTxSummary> SummaryAsync(PaymentTxFilter filter, CancellationToken ct = default);
    Task<PaymentTransaction?> GetAsync(long id, Guid? scopeCustomerId, CancellationToken ct = default);
    /// <summary>T.A Admin only: customers for the filter and for assigning an unattributed transaction.</summary>
    Task<IReadOnlyList<(Guid Id, string Code, string Name)>> CustomersAsync(CancellationToken ct = default);
    /// <summary>
    /// UNMATCHED → RESOLVED: adds the amount to the order <paramref name="orderNo"/> of <paramref name="orderCustomerId"/> (staff
    /// decision, so an expired QR does not matter; a cancelled order is refused) and records the system amounts.
    /// </summary>
    Task<PaymentTxResult> AssignAsync(long id, Guid? scopeCustomerId, Guid orderCustomerId, string orderNo, Guid actor, string via, string? note, CancellationToken ct = default);
    /// <summary>REVIEW → RESOLVED: accepts the money on its order; an EXPIRED_PAID / CANCELLED_PAID order takes its status from the amount paid.</summary>
    Task<PaymentTxResult> AcceptAsync(long id, Guid? scopeCustomerId, Guid actor, string via, string? note, CancellationToken ct = default);
    /// <summary>UNMATCHED or REVIEW → RESOLVED without changing any order (not order money, refunded, ...); a note is required.</summary>
    Task<PaymentTxResult> CloseAsync(long id, Guid? scopeCustomerId, Guid actor, string via, string note, CancellationToken ct = default);
}

public sealed class SqlPaymentTransactionStore(IConfiguration configuration) : IPaymentTransactionStore
{
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync(ct);
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, cn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private const string Select = """
SELECT t.Id,t.Direction,t.TransactionCode,t.Amount,t.Content,t.Bank,t.AccountNumber,t.VaAccountNumber,t.TransactionAt,t.ReceivedAt,t.CustomerId,c.Name,
 t.OrderId,o.OrderNo,o.Amount,t.Status,t.Flags,t.Reason,t.MatchedBy,t.SystemPaidBefore,t.SystemPaidAfter,t.PartnerPaidTotal,t.OrderStatusAfter,
 t.ResolvedAt,t.ResolvedVia,t.ResolutionNote,CAST(CASE WHEN i.RequestId LIKE 'sync:%' THEN 1 ELSE 0 END AS bit)
FROM dbo.PaymentTransactions t
LEFT JOIN dbo.TingeeWebhookInbox i ON i.Id=t.InboxId
LEFT JOIN dbo.Customers c ON c.Id=t.CustomerId
LEFT JOIN dbo.CustomerOrders o ON o.Id=t.OrderId AND o.CustomerId=t.CustomerId
""";

    private static PaymentTransaction Read(SqlDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        decimal? D(int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);
        DateTime? T(int i) => r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
        return new(r.GetInt64(0), r.GetString(1), S(2), r.GetDecimal(3), S(4), S(5), S(6), S(7), T(8), T(9)!.Value, r.IsDBNull(10) ? null : r.GetGuid(10), S(11),
            r.IsDBNull(12) ? null : r.GetGuid(12), S(13), D(14), r.GetString(15), S(16), S(17), S(18), D(19), D(20), D(21), S(22), T(23), S(24), S(25), !r.IsDBNull(26) && r.GetBoolean(26));
    }

    private static (string Where, List<(string, object?)> Args) Where(PaymentTxFilter f, bool withStatus)
    {
        var where = new List<string> { "1=1" };
        var args = new List<(string, object?)>();
        if (f.CustomerId is Guid c) { where.Add("t.CustomerId=@c"); args.Add(("@c", c)); }
        if (f.UnattributedOnly) where.Add("t.CustomerId IS NULL");
        if (f.FromUtc is DateTime from) { where.Add("t.ReceivedAt>=@from"); args.Add(("@from", from)); }
        if (f.ToUtc is DateTime to) { where.Add("t.ReceivedAt<@to"); args.Add(("@to", to)); }
        if (withStatus && !string.IsNullOrEmpty(f.Status))
        {
            if (f.Status == "STAFF") where.Add("t.Status IN ('UNMATCHED','REVIEW')");
            else { where.Add("t.Status=@status"); args.Add(("@status", f.Status)); }
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            where.Add("(t.Content LIKE @q OR t.TransactionCode LIKE @q OR t.AccountNumber LIKE @q OR t.VaAccountNumber LIKE @q OR o.OrderNo LIKE @q)");
            args.Add(("@q", "%" + f.Search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%"));
        }
        return (string.Join(" AND ", where), args);
    }

    public async Task<IReadOnlyList<PaymentTransaction>> SearchAsync(PaymentTxFilter f, CancellationToken ct = default)
    {
        var (where, args) = Where(f, withStatus: true);
        args.Add(("@max", Math.Clamp(f.Max, 1, 1000)));
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, Select.Replace("SELECT t.Id", "SELECT TOP (@max) t.Id") + $" WHERE {where} ORDER BY t.ReceivedAt DESC, t.Id DESC", [.. args]);
        var list = new List<PaymentTransaction>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(Read(r));
        return list;
    }

    public async Task<PaymentTxSummary> SummaryAsync(PaymentTxFilter f, CancellationToken ct = default)
    {
        var (where, args) = Where(f, withStatus: false);
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, $"""
SELECT t.Status,t.Direction,COUNT(*),ISNULL(SUM(t.Amount),0)
FROM dbo.PaymentTransactions t LEFT JOIN dbo.CustomerOrders o ON o.Id=t.OrderId AND o.CustomerId=t.CustomerId
WHERE {where} GROUP BY t.Status,t.Direction
""", [.. args]);
        var by = new Dictionary<string, (int N, decimal Sum)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var dir = new Dictionary<string, (int N, decimal Sum)>();
        static void Add(Dictionary<string, (int N, decimal Sum)> d, string k, int n, decimal sum) => d[k] = d.TryGetValue(k, out var v) ? (v.N + n, v.Sum + sum) : (n, sum);
        while (await r.ReadAsync(ct))
        {
            var (n, sum) = (r.GetInt32(2), r.GetDecimal(3));
            Add(by, r.GetString(0), n, sum);
            Add(dir, r.GetString(1), n, sum);
        }
        (int, decimal) G(string s) => by.TryGetValue(s, out var v) ? v : (0, 0);
        var (un, unA) = G(PaymentTxStatus.Unmatched); var (rv, rvA) = G(PaymentTxStatus.Review); var (m, mA) = G(PaymentTxStatus.Matched);
        var (rs, rsA) = G(PaymentTxStatus.Resolved); var (ig, igA) = G(PaymentTxStatus.Ignored);
        var (cr, crA) = dir.TryGetValue("CREDIT", out var c) ? c : (0, 0m); var (db, dbA) = dir.TryGetValue("DEBIT", out var d) ? d : (0, 0m);
        return new(un, unA, rv, rvA, m, mA, rs, rsA, ig, igA, cr, crA, db, dbA);
    }

    public async Task<PaymentTransaction?> GetAsync(long id, Guid? scopeCustomerId, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, Select + " WHERE t.Id=@id AND (@scope IS NULL OR t.CustomerId=@scope)", ("@id", id), ("@scope", scopeCustomerId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<IReadOnlyList<(Guid Id, string Code, string Name)>> CustomersAsync(CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, "SELECT Id,Code,Name FROM dbo.Customers WHERE IsDeleted=0 ORDER BY Name");
        var list = new List<(Guid, string, string)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add((r.GetGuid(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    private static PaymentTxResult Result(object? code) => (code as string) switch
    {
        "OK" => PaymentTxResult.Success,
        "NOT_FOUND" => new(false, "Không tìm thấy giao dịch."),
        "WRONG_STATE" => new(false, "Giao dịch không còn ở trạng thái cần xử lý (có thể người khác vừa xử lý)."),
        "WRONG_CUSTOMER" => new(false, "Giao dịch thuộc tài khoản nhận tiền của doanh nghiệp khác."),
        "ORDER_NOT_FOUND" => new(false, "Không tìm thấy đơn hàng với mã này."),
        "ORDER_CANCELLED" => new(false, "Đơn hàng đã hủy: không thể gán tiền vào đơn đã hủy."),
        _ => new(false, "Không xử lý được giao dịch.")
    };

    private static string? Note(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim().Length <= 500 ? note.Trim() : note.Trim()[..500];

    public async Task<PaymentTxResult> AssignAsync(long id, Guid? scopeCustomerId, Guid orderCustomerId, string orderNo, Guid actor, string via, string? note, CancellationToken ct = default)
    {
        if (scopeCustomerId is Guid scope && scope != orderCustomerId) return Result("WRONG_CUSTOMER");
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, """
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @amount decimal(18,0), @txCustomer uniqueidentifier, @status varchar(20), @direction varchar(10);
SELECT @amount=Amount,@txCustomer=CustomerId,@status=Status,@direction=Direction FROM dbo.PaymentTransactions WITH (UPDLOCK,HOLDLOCK)
WHERE Id=@id AND (@scope IS NULL OR CustomerId=@scope);
IF @status IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND'; RETURN; END
IF @status<>'UNMATCHED' OR @direction<>'CREDIT' OR @amount<=0 BEGIN ROLLBACK; SELECT 'WRONG_STATE'; RETURN; END
IF @txCustomer IS NOT NULL AND @txCustomer<>@orderCustomer BEGIN ROLLBACK; SELECT 'WRONG_CUSTOMER'; RETURN; END
DECLARE @order uniqueidentifier, @before decimal(18,0), @orderAmount decimal(18,0), @orderStatus varchar(20);
SELECT TOP 1 @order=Id,@before=PaidAmount,@orderAmount=Amount,@orderStatus=Status FROM dbo.CustomerOrders WITH (UPDLOCK,HOLDLOCK)
WHERE CustomerId=@orderCustomer AND REPLACE(OrderNo,'-','')=@orderKey;
IF @order IS NULL BEGIN ROLLBACK; SELECT 'ORDER_NOT_FOUND'; RETURN; END
IF @orderStatus IN ('CANCELLED','CANCELLED_PAID') BEGIN ROLLBACK; SELECT 'ORDER_CANCELLED'; RETURN; END
DECLARE @after decimal(18,0) = @before + @amount;
DECLARE @new varchar(20) = CASE WHEN @after>=@orderAmount THEN 'PAID' ELSE 'PARTIALLY_PAID' END;
UPDATE dbo.CustomerOrders SET PaidAmount=@after,Status=@new,
 InvoiceStatus=CASE WHEN @new='PAID' AND AutoIssueInvoice=1 AND InvoiceStatus='NONE' THEN 'PENDING' ELSE InvoiceStatus END,
 PaidAt=CASE WHEN @new='PAID' AND PaidAt IS NULL THEN SYSUTCDATETIME() ELSE PaidAt END,
 UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE Id=@order;
UPDATE dbo.PaymentTransactions SET Status='RESOLVED',CustomerId=@orderCustomer,OrderId=@order,MatchedBy='STAFF',SystemPaidBefore=@before,SystemPaidAfter=@after,
 OrderStatusAfter=@new,Flags=CASE WHEN @after>@orderAmount THEN 'OVERPAID' ELSE NULL END,
 ResolvedAt=SYSUTCDATETIME(),ResolvedBy=@actor,ResolvedVia=@via,ResolutionNote=@note,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id;
COMMIT;
SELECT 'OK';
""", ("@id", id), ("@scope", scopeCustomerId), ("@orderCustomer", orderCustomerId), ("@orderKey", TingeePaymentMatcher.Key(orderNo)),
            ("@actor", actor), ("@via", via), ("@note", Note(note)));
        return Result(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<PaymentTxResult> AcceptAsync(long id, Guid? scopeCustomerId, Guid actor, string via, string? note, CancellationToken ct = default)
    {
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, """
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @status varchar(20), @order uniqueidentifier, @customer uniqueidentifier;
SELECT @status=Status,@order=OrderId,@customer=CustomerId FROM dbo.PaymentTransactions WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id AND (@scope IS NULL OR CustomerId=@scope);
IF @status IS NULL BEGIN ROLLBACK; SELECT 'NOT_FOUND'; RETURN; END
IF @status<>'REVIEW' OR @order IS NULL BEGIN ROLLBACK; SELECT 'WRONG_STATE'; RETURN; END
UPDATE dbo.CustomerOrders SET
 Status=CASE WHEN PaidAmount>=Amount THEN 'PAID' WHEN PaidAmount>0 THEN 'PARTIALLY_PAID' ELSE 'AWAITING_PAYMENT' END,
 InvoiceStatus=CASE WHEN PaidAmount>=Amount AND AutoIssueInvoice=1 AND InvoiceStatus='NONE' THEN 'PENDING' ELSE InvoiceStatus END,
 PaidAt=CASE WHEN PaidAmount>=Amount AND PaidAt IS NULL THEN SYSUTCDATETIME() ELSE PaidAt END,
 UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
WHERE Id=@order AND CustomerId=@customer AND Status IN ('EXPIRED_PAID','CANCELLED_PAID');
UPDATE dbo.PaymentTransactions SET Status='RESOLVED',ResolvedAt=SYSUTCDATETIME(),ResolvedBy=@actor,ResolvedVia=@via,ResolutionNote=@note,UpdatedAt=SYSUTCDATETIME(),
 OrderStatusAfter=(SELECT Status FROM dbo.CustomerOrders WHERE Id=@order)
WHERE Id=@id;
COMMIT;
SELECT 'OK';
""", ("@id", id), ("@scope", scopeCustomerId), ("@actor", actor), ("@via", via), ("@note", Note(note)));
        return Result(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<PaymentTxResult> CloseAsync(long id, Guid? scopeCustomerId, Guid actor, string via, string note, CancellationToken ct = default)
    {
        if (Note(note) is not string text) return new(false, "Vui lòng ghi chú lý do xử lý.");
        await using var cn = await Open(ct);
        await using var cmd = Command(cn, """
UPDATE dbo.PaymentTransactions SET Status='RESOLVED',ResolvedAt=SYSUTCDATETIME(),ResolvedBy=@actor,ResolvedVia=@via,ResolutionNote=@note,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@id AND (@scope IS NULL OR CustomerId=@scope) AND Status IN ('UNMATCHED','REVIEW');
SELECT CASE WHEN @@ROWCOUNT=1 THEN 'OK' WHEN EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE Id=@id AND (@scope IS NULL OR CustomerId=@scope)) THEN 'WRONG_STATE' ELSE 'NOT_FOUND' END;
""", ("@id", id), ("@scope", scopeCustomerId), ("@actor", actor), ("@via", via), ("@note", text));
        return Result(await cmd.ExecuteScalarAsync(ct));
    }
}

/// <summary>Vietnamese labels shared by the Admin and Customer Portal reconciliation pages (no partner names).</summary>
public static class PaymentTxText
{
    public static (string Label, string Tone) Status(string status) => status switch
    {
        PaymentTxStatus.Unmatched => ("Chưa xử lý", "red"),
        PaymentTxStatus.Review => ("Cần xem lại", "gold"),
        PaymentTxStatus.Matched => ("Đã khớp đơn", "green"),
        PaymentTxStatus.Resolved => ("Đã xử lý", "blue"),
        PaymentTxStatus.Ignored => ("Tiền ra (ghi nợ)", "soft"),
        _ => (status, "soft")
    };

    public static string Flag(string flag) => flag switch
    {
        PaymentTxFlag.Overpaid => "Trả dư",
        PaymentTxFlag.OrderCancelled => "Đơn hủy có giao dịch",
        PaymentTxFlag.QrExpired => "QR hết hạn có giao dịch",
        PaymentTxFlag.PartnerMismatch => "Lệch đối soát",
        _ => flag
    };

    /// <summary>Filters of the reconciliation pages: "STAFF" = UNMATCHED + REVIEW (the default), "" = every transaction.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Filters =
    [
        ("STAFF", "Cần xử lý"), (PaymentTxStatus.Unmatched, "Chưa xử lý"), (PaymentTxStatus.Review, "Cần xem lại"), (PaymentTxStatus.Matched, "Đã khớp đơn"),
        (PaymentTxStatus.Resolved, "Đã xử lý"), (PaymentTxStatus.Ignored, "Tiền ra"), ("", "Tất cả"),
    ];

    /// <summary>A Vietnam calendar day range (inclusive dates) as UTC bounds.</summary>
    public static (DateTime? FromUtc, DateTime? ToUtc) Days(DateOnly? from, DateOnly? to) =>
        (from?.ToDateTime(TimeOnly.MinValue).AddHours(-7), to?.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7));
}
