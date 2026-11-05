using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// A payment webhook body (Tingee "Webhook Payment Callback"): transactionCode, amount, content (absent for VietinBank),
/// bank, accountNumber, vaAccountNumber, transactionDate (yyyyMMddHHmmss), type (credit/debit, default credit) and
/// additionalData (e.g. the billId of a dynamic QR). Parsing is tolerant: unknown shapes give null fields, never throw.
/// </summary>
public sealed record TingeePaymentNotice(string? TransactionCode, decimal Amount, string? Content, string? Bank, string? AccountNumber, string? VaAccountNumber,
    string? TransactionDate, bool IsCredit, IReadOnlyList<string> AdditionalValues)
{
    /// <summary>transactionDate (yyyyMMddHHmmss, Vietnam time) as UTC; null when absent or malformed.</summary>
    public DateTime? TransactionAtUtc =>
        DateTime.TryParseExact(TransactionDate, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? DateTime.SpecifyKind(local.AddHours(-7), DateTimeKind.Utc) : null;

    public static TingeePaymentNotice? Parse(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var values = new List<string>();
            if (root.TryGetProperty("additionalData", out var extra)) Collect(extra, values);
            var type = Text(root, "type");
            return new(Text(root, "transactionCode"), Number(root, "amount"), Text(root, "content"), Text(root, "bank"), Text(root, "accountNumber"),
                Text(root, "vaAccountNumber"), Text(root, "transactionDate"), !string.Equals(type, "debit", StringComparison.OrdinalIgnoreCase), values);
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString()!.Trim(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    private static decimal Number(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? 0
        : v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d
        : v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0;

    // additionalData is an array of name/value pairs (or plain values): every string or number inside is kept.
    private static void Collect(JsonElement e, List<string> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array: foreach (var item in e.EnumerateArray()) Collect(item, into); break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Collect(p.Value, into); break;
            case JsonValueKind.String when !string.IsNullOrWhiteSpace(e.GetString()): into.Add(e.GetString()!.Trim()); break;
            case JsonValueKind.Number: into.Add(e.GetRawText()); break;
        }
    }
}

/// <summary>
/// An order on the receiving account the money arrived in, in any state: cancelled and expired orders are candidates too,
/// so money paid to them is recorded against them (CANCELLED_PAID / EXPIRED_PAID) instead of disappearing. MerchantId,
/// QrAccount and BillId are set for a dynamic QR, whose total is also read from Tingee for reconciliation.
/// </summary>
public sealed record TingeePaymentCandidate(Guid CustomerId, Guid OrderId, string OrderNo, string? BillId, decimal Amount, decimal PaidAmount,
    string? QrAccount = null, long? MerchantId = null, string Status = TingeeOrderStatus.Awaiting, DateTime? ExpiresAtUtc = null)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);
    public bool IsDynamicQr => BillId is not null && QrAccount is not null && MerchantId is not null;
}

public static class TingeePaymentMatcher
{
    /// <summary>
    /// Finds the order a payment belongs to by its order number only (operator decision 2026-11-05): the dynamic QR billId
    /// in additionalData, or the order number in the transfer content (letters and digits only, so DH261105-0001 matches
    /// "DH2611050001" and "dh261105 0001"). No content (VietinBank) or no order number ⇒ null with a reason, for staff.
    /// </summary>
    public static (TingeePaymentCandidate? Order, string How) Match(TingeePaymentNotice notice, IReadOnlyList<TingeePaymentCandidate> candidates)
    {
        if (candidates.Count == 0) return (null, "Không có đơn hàng nào trên tài khoản nhận tiền");
        var byBill = candidates.Where(c => c.BillId is not null && notice.AdditionalValues.Contains(c.BillId, StringComparer.OrdinalIgnoreCase)).ToList();
        if (byBill.Count == 1) return (byBill[0], TingeeMatchedBy.BillId);
        if (notice.Content is not string content) return (null, "Giao dịch không có nội dung chuyển khoản (không có mã đơn)");

        var text = Key(content);
        var byContent = candidates.Where(c => Key(c.OrderNo) is { Length: > 0 } no && text.Contains(no, StringComparison.Ordinal)).ToList();
        if (byContent.Count == 0) return (null, "Không tìm thấy mã đơn hàng trong nội dung chuyển khoản");
        // DH2611050001 is also found inside DH26110500012: prefer the longest order number.
        var longest = byContent.Max(c => Key(c.OrderNo).Length);
        var best = byContent.Where(c => Key(c.OrderNo).Length == longest).ToList();
        return best.Count == 1 ? (best[0], TingeeMatchedBy.Content) : (null, "Nội dung chuyển khoản khớp nhiều đơn hàng");
    }

    /// <summary>Letters and digits only, upper case: the form the order number takes in a bank transfer content.</summary>
    public static string Key(string value)
    {
        var b = new StringBuilder(value.Length);
        foreach (var ch in value) if (char.IsAsciiLetterOrDigit(ch)) b.Append(char.ToUpperInvariant(ch));
        return b.ToString();
    }
}

public static class TingeeMatchedBy
{
    public const string BillId = "BILL_ID", Content = "CONTENT", Staff = "STAFF";
}

/// <summary>Order states (same values as the Customer Portal's OrderStatus).</summary>
public static class TingeeOrderStatus
{
    public const string Draft = "DRAFT", Awaiting = "AWAITING_PAYMENT", Partial = "PARTIALLY_PAID", Paid = "PAID", Expired = "EXPIRED", Cancelled = "CANCELLED",
        ExpiredPaid = "EXPIRED_PAID", CancelledPaid = "CANCELLED_PAID";

    public static string For(decimal amount, decimal paid) => paid >= amount ? Paid : paid > 0 ? Partial : Awaiting;
}

/// <summary>dbo.PaymentTransactions.Status.</summary>
public static class PaymentTxStatus
{
    /// <summary>Applied to its order, nothing to check.</summary>
    public const string Matched = "MATCHED";
    /// <summary>Applied to its order, but staff should look at it (see Flags).</summary>
    public const string Review = "REVIEW";
    /// <summary>No order found: "chưa xử lý", for staff to assign or close.</summary>
    public const string Unmatched = "UNMATCHED";
    public const string Resolved = "RESOLVED";
    /// <summary>Money going out of the account (debit): kept for the record only.</summary>
    public const string Ignored = "IGNORED";

    public static bool NeedsStaff(string status) => status is Review or Unmatched;
}

/// <summary>dbo.PaymentTransactions.Flags (comma separated).</summary>
public static class PaymentTxFlag
{
    public const string Overpaid = "OVERPAID", OrderCancelled = "ORDER_CANCELLED", QrExpired = "QR_EXPIRED", PartnerMismatch = "PARTNER_MISMATCH";
}

/// <summary>What one payment does to its order: computed by <see cref="TingeePaymentRules.Decide"/>, written atomically by the ledger.</summary>
public sealed record TingeePaymentApplication(long InboxId, TingeePaymentNotice Notice, DateTime ReceivedAtUtc, TingeePaymentCandidate Order, string MatchedBy,
    decimal PaidBefore, decimal PaidAfter, decimal? PartnerPaid, string OrderStatus, string TxStatus, IReadOnlyList<string> Flags);

public static class TingeePaymentRules
{
    /// <summary>
    /// Operator decisions 2026-11-05. The system counts the money itself (amount paid before + this payment); when the
    /// partner also reports a total (dynamic QR) both are kept and a difference is flagged. The order becomes
    /// CANCELLED_PAID when it was cancelled, EXPIRED_PAID when its QR had expired before the money arrived, otherwise
    /// AWAITING_PAYMENT / PARTIALLY_PAID / PAID from the amount paid. Overpayment, a cancelled/expired order and a partner
    /// difference make the transaction REVIEW instead of MATCHED.
    /// </summary>
    public static TingeePaymentApplication Decide(long inboxId, TingeePaymentNotice notice, DateTime receivedAtUtc, TingeePaymentCandidate order, string matchedBy, decimal? partnerPaid)
    {
        var after = order.PaidAmount + notice.Amount;
        var flags = new List<string>();
        string status;
        if (order.Status is TingeeOrderStatus.Cancelled or TingeeOrderStatus.CancelledPaid) { status = TingeeOrderStatus.CancelledPaid; flags.Add(PaymentTxFlag.OrderCancelled); }
        else if (IsExpired(order, receivedAtUtc)) { status = TingeeOrderStatus.ExpiredPaid; flags.Add(PaymentTxFlag.QrExpired); }
        else status = TingeeOrderStatus.For(order.Amount, after);
        if (after > order.Amount) flags.Add(PaymentTxFlag.Overpaid);
        if (partnerPaid is decimal p && p != after) flags.Add(PaymentTxFlag.PartnerMismatch);
        return new(inboxId, notice, receivedAtUtc, order, matchedBy, order.PaidAmount, after, partnerPaid, status,
            flags.Count == 0 ? PaymentTxStatus.Matched : PaymentTxStatus.Review, flags);
    }

    /// <summary>Expired when stored so, or still open with money due after its QR expiry (the portal shows these as expired too).</summary>
    public static bool IsExpired(TingeePaymentCandidate o, DateTime atUtc) =>
        o.Status is TingeeOrderStatus.Expired or TingeeOrderStatus.ExpiredPaid
        || (o.Status is TingeeOrderStatus.Awaiting or TingeeOrderStatus.Partial && o.ExpiresAtUtc is { } e && e <= atUtc && o.PaidAmount < o.Amount);
}

/// <summary>Store behind the payment processor: pending payment webhooks, candidate orders, and the atomic writes.</summary>
public interface ITingeePaymentLedger
{
    Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct);
    /// <summary>Orders that could take the payment; only orders created before the webhook arrived (old money never pays a new order).</summary>
    Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct);
    /// <summary>
    /// In one transaction: marks the webhook PROCESSED, moves the order's amount paid from PaidBefore to PaidAfter with its
    /// new status (only if neither changed meanwhile), and records the transaction. False, with nothing changed, otherwise
    /// (the webhook stays pending and is retried).
    /// </summary>
    Task<bool> ApplyAsync(TingeePaymentApplication payment, CancellationToken ct);
    /// <summary>
    /// In one transaction: sets the webhook's final status and records a transaction that changed no order (UNMATCHED with
    /// the reason, or IGNORED for a debit), attributed to the customer that owns the receiving account when there is one.
    /// </summary>
    Task RecordAsync(long inboxId, TingeePaymentNotice? notice, DateTime receivedAtUtc, string inboxStatus, string txStatus, string reason, CancellationToken ct);
    /// <summary>Counts an attempt and leaves the webhook pending (retried on a later run).</summary>
    Task RetryLaterAsync(long inboxId, string note, CancellationToken ct);
    /// <summary>Last resort when even <see cref="RecordAsync"/> fails: the webhook is marked FAILED.</summary>
    Task FailAsync(long inboxId, string note, CancellationToken ct);
}

public sealed record TingeePendingPayment(long Id, string RawBody, int Attempts, DateTime ReceivedAtUtc);

public static class TingeeInboxStatus
{
    public const string Received = "RECEIVED", Processed = "PROCESSED", Unmatched = "UNMATCHED", Ignored = "IGNORED", Failed = "FAILED";
}

/// <summary>
/// Applies stored payment webhooks to customer orders in the webhook site, so payments are recorded whether or not anyone
/// has the order open. Each inbox row is handled once (its status changes in the same transaction as the order and the
/// transaction record), so a retried webhook or a second worker never counts money twice.
/// Every payment ends as a dbo.PaymentTransactions row: MATCHED/REVIEW on its order, UNMATCHED ("chưa xử lý") with the
/// reason when no order fits or processing kept failing, IGNORED for a debit.
/// </summary>
public sealed class TingeePaymentProcessor(ITingeePaymentLedger ledger, ITingeeApi tingee, ILogger<TingeePaymentProcessor> logger)
{
    public const int MaxAttempts = 10;
    /// <summary>A dynamic QR whose partner total does not include the payment yet (or cannot be read) is retried this many times, then applied with the difference flagged.</summary>
    public const int LagRetries = 3;

    /// <summary>Handles up to <paramref name="max"/> pending payments; returns how many changed an order.</summary>
    public async Task<int> RunOnceAsync(int max = 50, CancellationToken ct = default)
    {
        var applied = 0;
        foreach (var pending in await ledger.PendingPaymentsAsync(max, ct))
        {
            try { if (await HandleAsync(pending, ct)) applied++; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "TINGEE_PAYMENT_FAILED inbox={Id} attempt={Attempt}", pending.Id, pending.Attempts + 1);
                var note = Cut(ex.GetType().Name + ": " + ex.Message);
                if (pending.Attempts + 1 < MaxAttempts) { await ledger.RetryLaterAsync(pending.Id, note, ct); continue; }
                try
                {
                    await ledger.RecordAsync(pending.Id, TingeePaymentNotice.Parse(pending.RawBody), pending.ReceivedAtUtc, TingeeInboxStatus.Unmatched,
                        PaymentTxStatus.Unmatched, Cut($"Xử lý tự động thất bại sau {MaxAttempts} lần: {note}"), ct);
                }
                catch (Exception again) when (again is not OperationCanceledException)
                {
                    logger.LogError(again, "TINGEE_PAYMENT_RECORD_FAILED inbox={Id}", pending.Id);
                    await ledger.FailAsync(pending.Id, note, ct);
                }
            }
        }
        return applied;
    }

    private async Task<bool> HandleAsync(TingeePendingPayment pending, CancellationToken ct)
    {
        var id = pending.Id;
        var notice = TingeePaymentNotice.Parse(pending.RawBody);
        if (notice is null)
        {
            await ledger.RecordAsync(id, null, pending.ReceivedAtUtc, TingeeInboxStatus.Unmatched, PaymentTxStatus.Unmatched, "Không đọc được nội dung thông báo giao dịch", ct);
            return false;
        }
        if (!notice.IsCredit)
        {
            await ledger.RecordAsync(id, notice, pending.ReceivedAtUtc, TingeeInboxStatus.Ignored, PaymentTxStatus.Ignored, "Giao dịch ghi nợ (tiền ra)", ct);
            return false;
        }
        if (notice.Amount <= 0)
        {
            await ledger.RecordAsync(id, notice, pending.ReceivedAtUtc, TingeeInboxStatus.Unmatched, PaymentTxStatus.Unmatched, "Số tiền giao dịch không hợp lệ", ct);
            return false;
        }

        var (order, how) = TingeePaymentMatcher.Match(notice, await ledger.CandidatesAsync(notice, pending.ReceivedAtUtc, ct));
        if (order is null)
        {
            await ledger.RecordAsync(id, notice, pending.ReceivedAtUtc, TingeeInboxStatus.Unmatched, PaymentTxStatus.Unmatched, how, ct);
            logger.LogWarning("TINGEE_PAYMENT_UNMATCHED inbox={Id} tx={Tx} amount={Amount} reason={Reason}", id, notice.TransactionCode, notice.Amount, how);
            return false;
        }

        decimal? partner = null;
        if (order.IsDynamicQr)
        {
            var expected = order.PaidAmount + notice.Amount;
            try
            {
                var bill = (await tingee.GetDynamicQrStatusAsync(order.MerchantId!.Value, order.QrAccount!, order.BillId!, ct)).BillInfo;
                // A QR re-issued for the remainder covers Amount(order) - Amount(QR) already paid before it.
                var paidBefore = bill is { Amount: > 0 } && bill.Amount < order.Amount ? order.Amount - bill.Amount : 0;
                partner = paidBefore + (bill?.TotalAmountPaid ?? 0);
                if (partner < expected && pending.Attempts + 1 < LagRetries)
                {
                    await ledger.RetryLaterAsync(id, $"{order.OrderNo}: partner total {partner} < {expected}, waiting", ct);
                    return false;
                }
            }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
            {
                if (pending.Attempts + 1 < LagRetries)
                {
                    await ledger.RetryLaterAsync(id, Cut($"{order.OrderNo}: QR status unavailable: {ex.Message}"), ct);
                    return false;
                }
                logger.LogWarning(ex, "TINGEE_PAYMENT_PARTNER_UNCHECKED inbox={Id} order={OrderNo}", id, order.OrderNo);
            }
        }

        var payment = TingeePaymentRules.Decide(id, notice, pending.ReceivedAtUtc, order, how, partner);
        if (!await ledger.ApplyAsync(payment, ct)) return false;
        logger.LogInformation("TINGEE_PAYMENT_APPLIED inbox={Id} tx={Tx} order={OrderNo} amount={Amount} paid={Paid} partner={Partner} status={Status} flags={Flags}",
            id, notice.TransactionCode, order.OrderNo, notice.Amount, payment.PaidAfter, partner, payment.OrderStatus, string.Join(",", payment.Flags));
        if (order.IsDynamicQr && payment.PaidAfter >= order.Amount)
        {
            // Best effort, as on the portal: Tingee only deletes some banks' dynamic QRs.
            try { await tingee.DeleteDynamicQrAsync(order.MerchantId!.Value, order.QrAccount!, order.BillId!, ct); }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException) { logger.LogInformation(ex, "Dynamic QR of {OrderNo} not closed.", order.OrderNo); }
        }
        return true;
    }

    private static string Cut(string text) => text.Length <= 450 ? text : text[..450];
}
