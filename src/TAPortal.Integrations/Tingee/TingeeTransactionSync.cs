using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

/// <param name="Accounts">Receiving accounts (VA) that were queried.</param>
/// <param name="Found">Transactions the partner reported on them in the period.</param>
/// <param name="Added">Of those, how many the system had not received (no webhook) and were queued now.</param>
/// <param name="Pending">Queued transactions still waiting for the payment processor when the call returned.</param>
public sealed record TingeeSyncResult(int Accounts, int Found, int Added, int AddedCredit, decimal AddedCreditAmount, int AddedDebit, decimal AddedDebitAmount,
    int Pending, string? Error = null, string? ErrorCode = null)
{
    public bool Ok => Error is null;
    public static TingeeSyncResult Failed(string error, string? code = null) => new(0, 0, 0, 0, 0, 0, 0, 0, error, code);
}

/// <summary>
/// "Đồng bộ giao dịch" (operator 2026-11-12): a payment whose webhook never arrived (the partner did not send it, or our
/// site was down) is fetched from the partner's transaction history (transaction/get-paging) and queued in
/// dbo.TingeeWebhookInbox exactly as the webhook would have been — same body fields, same dedup key (bank transaction
/// code) — so the payment processor in the webhook site matches it to an order or leaves it for reconciliation, and a
/// webhook that arrives later is a duplicate. Only the receiving accounts of the given customer (or of every customer)
/// are queried; ReceivedAt is the bank's transaction time, so the transaction lands on the day it happened.
/// </summary>
public sealed class TingeeTransactionSync(ITingeeApi tingee, IConfiguration configuration, TimeProvider clock, ILogger<TingeeTransactionSync> logger)
{
    /// <summary>Longest period one sync may cover; the partner allows 10 days per call, so it is split.</summary>
    public const int MaxDays = 31;
    private const int WindowDays = 9, PageSize = 200, MaxPages = 25, VaPerCall = 50;
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastRun = new();

    public sealed record Target(Guid CustomerId, long? MerchantId, string VaAccountNumber);

    /// <summary>Syncs [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) for one customer, or all when null; waits up to <paramref name="settle"/> for the processor.</summary>
    public async Task<TingeeSyncResult> SyncAsync(Guid? customerId, DateTime fromUtc, DateTime toUtc, TimeSpan? settle = null, CancellationToken ct = default)
    {
        if (!tingee.IsConfigured) return TingeeSyncResult.Failed("Dịch vụ thanh toán chưa được cấu hình.");
        if (string.IsNullOrWhiteSpace(configuration["Database:ConnectionString"])) return TingeeSyncResult.Failed("Chưa cấu hình cơ sở dữ liệu.");
        var now = clock.GetUtcNow();
        if (toUtc > now.UtcDateTime) toUtc = now.UtcDateTime;
        if (fromUtc >= toUtc) return TingeeSyncResult.Failed("Khoảng thời gian không hợp lệ.");
        if (toUtc - fromUtc > TimeSpan.FromDays(MaxDays)) return TingeeSyncResult.Failed($"Mỗi lần đồng bộ tối đa {MaxDays} ngày.");

        var key = customerId?.ToString() ?? "*";
        if (LastRun.TryGetValue(key, out var last) && now - last < MinInterval)
            return TingeeSyncResult.Failed("Vừa đồng bộ xong, vui lòng thử lại sau ít giây.");
        LastRun[key] = now;

        var targets = await TargetsAsync(customerId, ct);
        if (targets.Count == 0) return TingeeSyncResult.Failed("Chưa có tài khoản nhận tiền nào được liên kết.");

        var items = new Dictionary<string, TingeeTransactionItem>(StringComparer.Ordinal);
        try
        {
            foreach (var group in targets.GroupBy(t => t.MerchantId))
            foreach (var vas in group.Select(t => t.VaAccountNumber).Distinct().Chunk(VaPerCall))
            for (var start = fromUtc; start < toUtc; start = start.AddDays(WindowDays))
            {
                var end = start.AddDays(WindowDays) < toUtc ? start.AddDays(WindowDays) : toUtc;
                for (var page = 0; page < MaxPages; page++)
                {
                    var result = await tingee.GetTransactionsAsync(new TingeeTransactionQuery
                    {
                        SkipCount = page * PageSize, MaxResultCount = PageSize, MerchantId = group.Key, VaAccountNumbers = vas,
                        StartTime = Local(start), EndTime = Local(end.AddSeconds(-1)),
                    }, ct);
                    foreach (var item in result.Items)
                        if (Code(item) is string code && (vas.Contains(item.VaAccountNumber ?? "", StringComparer.Ordinal) || vas.Contains(item.AccountNumber ?? "", StringComparer.Ordinal))) items[code] = item;
                    if (result.Items.Count < PageSize || (page + 1) * PageSize >= result.TotalCount) break;
                }
            }
        }
        catch (TingeeApiException ex)
        {
            logger.LogWarning(ex, "TINGEE_SYNC_FAILED customer={Customer} code={Code} log={Log}", key, ex.Code, ex.LogCode);
            return TingeeSyncResult.Failed("Không lấy được lịch sử giao dịch từ dịch vụ thanh toán.", ex.LogCode ?? ex.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TingeeNotConfiguredException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "TINGEE_SYNC_UNREACHABLE customer={Customer}", key);
            return TingeeSyncResult.Failed("Không kết nối được dịch vụ thanh toán, vui lòng thử lại sau.");
        }

        var added = await QueueAsync(items.Values, now, ct);
        var credit = added.Where(i => !IsDebit(i)).ToList();
        var debit = added.Where(IsDebit).ToList();
        var pending = await SettleAsync(added.Select(i => DedupKey(Code(i)!)).ToList(), settle ?? TimeSpan.FromSeconds(8), ct);
        logger.LogInformation("TINGEE_SYNC customer={Customer} accounts={Accounts} found={Found} added={Added} pending={Pending}",
            key, targets.Count, items.Count, added.Count, pending);
        return new(targets.Select(t => t.VaAccountNumber).Distinct().Count(), items.Count, added.Count, credit.Count, credit.Sum(i => i.Amount),
            debit.Count, debit.Sum(i => i.Amount), pending);
    }

    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"]);
        await cn.OpenAsync(ct);
        return cn;
    }

    private async Task<List<Target>> TargetsAsync(Guid? customerId, CancellationToken ct)
    {
        await using var cn = await Open(ct);
        await using var cmd = new SqlCommand("""
SELECT b.CustomerId,m.MerchantId,b.VaAccountNumber
FROM dbo.CustomerBankAccounts b
LEFT JOIN dbo.TingeeMerchants m ON m.CustomerId=b.CustomerId
WHERE b.Status<>'DELETED' AND b.VaAccountNumber IS NOT NULL AND b.VaAccountNumber<>'' AND (@c IS NULL OR b.CustomerId=@c)
""", cn);
        cmd.Parameters.AddWithValue("@c", (object?)customerId ?? DBNull.Value);
        var list = new List<Target>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new(r.GetGuid(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2)));
        return list;
    }

    /// <summary>Inserts the transactions the inbox does not have yet; returns the ones inserted.</summary>
    private async Task<List<TingeeTransactionItem>> QueueAsync(IEnumerable<TingeeTransactionItem> items, DateTimeOffset now, CancellationToken ct)
    {
        var added = new List<TingeeTransactionItem>();
        await using var cn = await Open(ct);
        foreach (var item in items)
        {
            var code = Code(item)!;
            var body = Body(item, code);
            await using var cmd = new SqlCommand("""
IF NOT EXISTS (SELECT 1 FROM dbo.TingeeWebhookInbox WHERE Kind='Payment' AND DedupKey=@dedup)
 AND NOT EXISTS (SELECT 1 FROM dbo.PaymentTransactions WHERE TransactionCode=@code)
BEGIN
 INSERT dbo.TingeeWebhookInbox(Kind,DedupKey,RequestId,RequestTimestamp,RawBody,ReceivedAt) VALUES('Payment',@dedup,@requestId,@timestamp,@body,@received);
 SELECT 1;
END
ELSE SELECT 0;
""", cn);
            cmd.Parameters.AddWithValue("@dedup", DedupKey(code));
            cmd.Parameters.AddWithValue("@code", code.Length <= 100 ? code : code[..100]);
            cmd.Parameters.AddWithValue("@requestId", "sync:" + Cut(item.TransactionId ?? code, 90));
            cmd.Parameters.AddWithValue("@timestamp", TingeeSignature.Timestamp(now));
            cmd.Parameters.AddWithValue("@body", body);
            cmd.Parameters.AddWithValue("@received", TransactionAtUtc(item) ?? now.UtcDateTime);
            try { if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1) added.Add(item); }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) { /* the webhook arrived meanwhile */ }
        }
        return added;
    }

    /// <summary>Waits for the webhook site's processor to handle the queued rows; returns how many are still waiting.</summary>
    private async Task<int> SettleAsync(IReadOnlyList<string> keys, TimeSpan wait, CancellationToken ct)
    {
        if (keys.Count == 0) return 0;
        var until = clock.GetUtcNow() + wait;
        while (true)
        {
            await using var cn = await Open(ct);
            await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.TingeeWebhookInbox WHERE Kind='Payment' AND Status='RECEIVED' AND DedupKey IN ({string.Join(",", keys.Select((_, i) => "@k" + i))})", cn);
            for (var i = 0; i < keys.Count; i++) cmd.Parameters.AddWithValue("@k" + i, keys[i]);
            var left = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (left == 0 || clock.GetUtcNow() >= until) return left;
            await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        }
    }

    /// <summary>The webhook body for a history item (Tingee "Webhook Payment Callback" fields), marked as synced.</summary>
    public static string Body(TingeeTransactionItem item, string code) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["transactionCode"] = code,
        ["amount"] = item.Amount,
        ["content"] = item.Content ?? "",
        ["type"] = IsDebit(item) ? "debit" : "credit",
        ["bank"] = TingeeBankCatalog.Find(item.BankBin)?.Code,
        ["bankBin"] = item.BankBin,
        ["accountNumber"] = item.AccountNumber,
        ["vaAccountNumber"] = item.VaAccountNumber,
        ["transactionDate"] = item.TransactionDate,
        ["billId"] = item.BillId,
        ["tingeeTransactionId"] = item.TransactionId,
        ["source"] = "TA_SYNC",
    });

    /// <summary>Same key as <see cref="SqlTingeeWebhookInbox.DedupKey"/> for a payment webhook with this transactionCode.</summary>
    public static string DedupKey(string code) => "tx:" + Cut(code.Trim(), 140);

    public static string? Code(TingeeTransactionItem item) =>
        !string.IsNullOrWhiteSpace(item.Code) ? item.Code.Trim() : !string.IsNullOrWhiteSpace(item.TransactionId) ? item.TransactionId.Trim() : null;

    private static bool IsDebit(TingeeTransactionItem item) => string.Equals(item.Type, "debit", StringComparison.OrdinalIgnoreCase);

    private static DateTime? TransactionAtUtc(TingeeTransactionItem item) =>
        DateTime.TryParseExact(item.TransactionDate, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? DateTime.SpecifyKind(local - Vietnam, DateTimeKind.Utc) : null;

    private static string Local(DateTime utc) => (utc + Vietnam).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    /// <summary>Result line for staff (never names the partner).</summary>
    public static string Message(TingeeSyncResult r, DateOnly from, DateOnly to)
    {
        var period = from == to ? $"ngày {from:dd/MM/yyyy}" : $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";
        if (!r.Ok) return r.Error + (r.ErrorCode is string code ? $" Mã tra cứu: {code}." : "");
        if (r.Found == 0) return $"Đồng bộ {period}: ngân hàng không có giao dịch nào trên tài khoản nhận tiền.";
        if (r.Added == 0) return $"Đồng bộ {period}: {r.Found} giao dịch, hệ thống đã nhận đủ, không thiếu giao dịch nào.";
        var parts = new List<string>();
        if (r.AddedCredit > 0) parts.Add($"{r.AddedCredit} tiền vào {Vnd(r.AddedCreditAmount)}");
        if (r.AddedDebit > 0) parts.Add($"{r.AddedDebit} tiền ra {Vnd(r.AddedDebitAmount)}");
        return $"Đồng bộ {period}: đã lấy về {r.Added} giao dịch chưa được báo về hệ thống ({string.Join(", ", parts)})."
            + (r.Pending > 0 ? " Một số giao dịch đang được xử lý, tải lại trang sau ít phút." : "");
    }

    private static string Vnd(decimal v) => v.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
