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
/// An order that may receive a payment: open (or expired) on the receiving account the money arrived in. MerchantId,
/// QrAccount and BillId are set for a dynamic QR, whose amount paid is read back from Tingee.
/// </summary>
public sealed record TingeePaymentCandidate(Guid CustomerId, Guid OrderId, string OrderNo, string? BillId, decimal Amount, decimal PaidAmount,
    string? QrAccount = null, long? MerchantId = null)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);
    public bool IsDynamicQr => BillId is not null && QrAccount is not null && MerchantId is not null;
}

public static class TingeePaymentMatcher
{
    /// <summary>
    /// Finds the order a payment belongs to, most certain first: the dynamic QR billId in additionalData; then the order
    /// number in the transfer content (letters and digits only, so DH261105-0001 matches "DH2611050001" and "dh261105 0001");
    /// then, only when the bank sends no content (VietinBank), the single order on that account still owing exactly that amount.
    /// Null with a reason when nothing (or more than one order) fits.
    /// </summary>
    public static (TingeePaymentCandidate? Order, string How) Match(TingeePaymentNotice notice, IReadOnlyList<TingeePaymentCandidate> candidates)
    {
        if (candidates.Count == 0) return (null, "no open order on the receiving account");
        var byBill = candidates.Where(c => c.BillId is not null && notice.AdditionalValues.Contains(c.BillId, StringComparer.OrdinalIgnoreCase)).ToList();
        if (byBill.Count == 1) return (byBill[0], "billId");

        if (notice.Content is string content)
        {
            var text = Key(content);
            var byContent = candidates.Where(c => Key(c.OrderNo) is { Length: > 0 } no && text.Contains(no, StringComparison.Ordinal)).ToList();
            // DH2611050001 is also found inside DH26110500012: prefer the longest order number.
            if (byContent.Count > 0)
            {
                var longest = byContent.Max(c => Key(c.OrderNo).Length);
                var best = byContent.Where(c => Key(c.OrderNo).Length == longest).ToList();
                return best.Count == 1 ? (best[0], "content") : (null, "content matches several orders");
            }
            return (null, "order number not found in the transfer content");
        }

        var byAmount = candidates.Where(c => c.Remaining > 0 && c.Remaining == notice.Amount).ToList();
        return byAmount.Count == 1 ? (byAmount[0], "amount (no transfer content)") : (null, byAmount.Count == 0 ? "no order owes this amount" : "several orders owe this amount");
    }

    /// <summary>Letters and digits only, upper case: the form the order number takes in a bank transfer content.</summary>
    public static string Key(string value)
    {
        var b = new StringBuilder(value.Length);
        foreach (var ch in value) if (char.IsAsciiLetterOrDigit(ch)) b.Append(char.ToUpperInvariant(ch));
        return b.ToString();
    }
}

/// <summary>Order states used by the payment processor (same values as the Customer Portal's OrderStatus).</summary>
public static class TingeeOrderStatus
{
    public const string Awaiting = "AWAITING_PAYMENT", Partial = "PARTIALLY_PAID", Paid = "PAID", Expired = "EXPIRED";

    public static string For(decimal amount, decimal paid) => paid >= amount ? Paid : paid > 0 ? Partial : Awaiting;
}


/// <summary>Store behind the payment processor: pending payment webhooks, candidate orders, and the atomic apply.</summary>
public interface ITingeePaymentLedger
{
    Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct);
    /// <summary>Orders that could take the payment; only orders created before the webhook arrived (old money never pays a new order).</summary>
    Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct);
    /// <summary>
    /// In one transaction: sets the order's amount paid from <paramref name="expectedPaid"/> to <paramref name="newPaid"/> (status,
    /// PaidAt and the pending e-invoice follow) and marks the webhook PROCESSED. False, with nothing changed, when the webhook
    /// was already taken or the order's amount paid moved meanwhile (the webhook stays pending and is retried).
    /// </summary>
    Task<bool> ApplyAsync(long inboxId, TingeePaymentCandidate order, decimal expectedPaid, decimal newPaid, string note, CancellationToken ct);
    /// <summary>Sets the webhook's status without touching an order and counts the attempt (RECEIVED again means retry later).</summary>
    Task MarkAsync(long inboxId, string status, string note, CancellationToken ct);
}

public sealed record TingeePendingPayment(long Id, string RawBody, int Attempts, DateTime ReceivedAtUtc);

public static class TingeeInboxStatus
{
    public const string Received = "RECEIVED", Processed = "PROCESSED", Unmatched = "UNMATCHED", Ignored = "IGNORED", Failed = "FAILED";
}

/// <summary>
/// Applies stored payment webhooks to customer orders, so payments are recorded whether or not anyone has the order open.
/// Each inbox row is handled once (its status changes in the same transaction as the order), so a retried webhook or a
/// second worker never counts money twice.
/// - Dynamic QR order: the webhook is the signal; the amount paid is read from Tingee (the same rule as "Kiểm tra thanh
///   toán"), so a manual check and the webhook can never add the same money twice.
/// - Static QR order: the webhook amount is added (nothing else reports these payments).
/// Unmatched money is kept as UNMATCHED with the reason, for T.A to reconcile.
/// </summary>
public sealed class TingeePaymentProcessor(ITingeePaymentLedger ledger, ITingeeApi tingee, ILogger<TingeePaymentProcessor> logger)
{
    public const int MaxAttempts = 10;
    /// <summary>A dynamic QR whose amount paid on Tingee has not moved yet is retried this many times, then taken as already counted.</summary>
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
                await ledger.MarkAsync(pending.Id, pending.Attempts + 1 >= MaxAttempts ? TingeeInboxStatus.Failed : TingeeInboxStatus.Received,
                    Cut(ex.GetType().Name + ": " + ex.Message), ct);
            }
        }
        return applied;
    }

    private async Task<bool> HandleAsync(TingeePendingPayment pending, CancellationToken ct)
    {
        var id = pending.Id;
        var notice = TingeePaymentNotice.Parse(pending.RawBody);
        if (notice is null) { await ledger.MarkAsync(id, TingeeInboxStatus.Failed, "body is not a payment JSON object", ct); return false; }
        if (!notice.IsCredit || notice.Amount <= 0) { await ledger.MarkAsync(id, TingeeInboxStatus.Ignored, notice.IsCredit ? "amount <= 0" : "debit", ct); return false; }

        var (order, how) = TingeePaymentMatcher.Match(notice, await ledger.CandidatesAsync(notice, pending.ReceivedAtUtc, ct));
        if (order is null)
        {
            await ledger.MarkAsync(id, TingeeInboxStatus.Unmatched, how, ct);
            logger.LogWarning("TINGEE_PAYMENT_UNMATCHED inbox={Id} tx={Tx} amount={Amount} reason={Reason}", id, notice.TransactionCode, notice.Amount, how);
            return false;
        }

        decimal paid;
        if (order.IsDynamicQr)
        {
            var bill = (await tingee.GetDynamicQrStatusAsync(order.MerchantId!.Value, order.QrAccount!, order.BillId!, ct)).BillInfo;
            // A QR re-issued for the remainder covers Amount(order) - Amount(QR) already paid before it.
            var paidBefore = bill is { Amount: > 0 } && bill.Amount < order.Amount ? order.Amount - bill.Amount : 0;
            paid = Math.Max(order.PaidAmount, paidBefore + (bill?.TotalAmountPaid ?? 0));
            if (paid <= order.PaidAmount)
            {
                if (pending.Attempts + 1 < LagRetries)
                {
                    await ledger.MarkAsync(id, TingeeInboxStatus.Received, $"{order.OrderNo}: amount paid not updated on the partner yet", ct);
                    return false;
                }
                await ledger.MarkAsync(id, TingeeInboxStatus.Processed, $"{order.OrderNo} by {how}: already counted", ct);
                return false;
            }
        }
        else paid = order.PaidAmount + notice.Amount;

        if (!await ledger.ApplyAsync(id, order, order.PaidAmount, paid, $"{order.OrderNo} by {how}", ct)) return false;
        logger.LogInformation("TINGEE_PAYMENT_APPLIED inbox={Id} tx={Tx} order={OrderNo} amount={Amount} paid={Paid} by={How}",
            id, notice.TransactionCode, order.OrderNo, notice.Amount, paid, how);
        if (order.IsDynamicQr && paid >= order.Amount)
        {
            // Best effort, as on the portal: Tingee only deletes some banks' dynamic QRs.
            try { await tingee.DeleteDynamicQrAsync(order.MerchantId!.Value, order.QrAccount!, order.BillId!, ct); }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException) { logger.LogInformation(ex, "Dynamic QR of {OrderNo} not closed.", order.OrderNo); }
        }
        return true;
    }

    private static string Cut(string text) => text.Length <= 900 ? text : text[..900];
}