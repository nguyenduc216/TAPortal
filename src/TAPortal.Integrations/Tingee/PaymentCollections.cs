namespace TAPortal.Integrations.Tingee;

// Payment collections ("Thu gộp công nợ", operator 2026-11-27, migration 069): one QR for several unpaid orders of one
// customer. The money paid to a collection is split over its orders in their position order (oldest first by default),
// each order taking at most what it still owes; an order that becomes fully paid gets its e-invoice like any other.

/// <summary>Collection numbers: TT-yyMMdd-NNNN, transferred as TTyyMMddNNNN (never inside an order number DH…).</summary>
public static class PaymentCollectionNo
{
    public const string Prefix = "TT";
    public static bool IsCollection(string? number) => number is not null && number.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Collection states (CK_PC_Status). An open collection past its QR expiry is shown as expired but stored as is.</summary>
public static class PaymentCollectionStatus
{
    public const string Awaiting = TingeeOrderStatus.Awaiting, Partial = TingeeOrderStatus.Partial, Paid = TingeeOrderStatus.Paid, Cancelled = TingeeOrderStatus.Cancelled;
    /// <summary>Display only: an open collection whose QR has expired.</summary>
    public const string Expired = TingeeOrderStatus.Expired;
    public static bool IsOpen(string status) => status is Awaiting or Partial;
}

/// <summary>An order of a collection as it stands now (stored status, not the displayed one).</summary>
public sealed record TingeeCollectionOrder(Guid OrderId, string OrderNo, int Position, decimal Amount, decimal PaidAmount, string Status)
{
    public decimal Remaining => Math.Max(0, Amount - PaidAmount);

    /// <summary>
    /// Orders that still take money from a collection: open (draft, awaiting, partly paid) or whose own QR expired. A cancelled
    /// order, one already paid, and one holding money paid after its expiry (EXPIRED_PAID, waiting for staff) take nothing.
    /// </summary>
    public bool TakesMoney => Remaining > 0 && Status is TingeeOrderStatus.Draft or TingeeOrderStatus.Awaiting or TingeeOrderStatus.Partial or TingeeOrderStatus.Expired;
}

/// <summary>A collection on the account the money arrived in (any state, so money paid to a cancelled one is still recorded).</summary>
public sealed record TingeeCollectionCandidate(Guid CustomerId, Guid CollectionId, string CollectionNo, string? BillId, decimal Amount, decimal PaidAmount,
    string? QrAccount = null, long? MerchantId = null, string Status = PaymentCollectionStatus.Awaiting, DateTime? ExpiresAtUtc = null)
{
    public bool IsDynamicQr => BillId is not null && QrAccount is not null && MerchantId is not null;
}

/// <summary>The part of one payment that goes to one order.</summary>
public sealed record TingeeAllocation(Guid OrderId, string OrderNo, decimal Amount, decimal PaidBefore, decimal PaidAfter, string ExpectedStatus, string OrderStatus);

/// <summary>What one payment does to a collection and its orders: computed by <see cref="TingeeCollectionRules.Decide"/>, written atomically by the ledger.</summary>
public sealed record TingeeCollectionApplication(long InboxId, TingeePaymentNotice Notice, DateTime ReceivedAtUtc, TingeeCollectionCandidate Collection, string MatchedBy,
    decimal PaidBefore, decimal PaidAfter, decimal? PartnerPaid, string CollectionStatus, IReadOnlyList<TingeeAllocation> Allocations, decimal Unallocated,
    string TxStatus, IReadOnlyList<string> Flags);

public static class TingeeCollectionMatcher
{
    /// <summary>
    /// The collection a payment belongs to: its dynamic QR billId in additionalData, or its number in the transfer content
    /// (letters and digits only). Null when neither is found: the payment is then matched to an order as before.
    /// </summary>
    public static (TingeeCollectionCandidate? Collection, string How) Match(TingeePaymentNotice notice, IReadOnlyList<TingeeCollectionCandidate> candidates)
    {
        if (candidates.Count == 0) return (null, "");
        var byBill = candidates.Where(c => c.BillId is not null && notice.AdditionalValues.Contains(c.BillId, StringComparer.OrdinalIgnoreCase)).ToList();
        if (byBill.Count == 1) return (byBill[0], TingeeMatchedBy.BillId);
        if (notice.Content is not string content) return (null, "");
        var text = TingeePaymentMatcher.Key(content);
        var found = candidates.Where(c => TingeePaymentMatcher.Key(c.CollectionNo) is { Length: > 0 } no && text.Contains(no, StringComparison.Ordinal)).ToList();
        if (found.Count == 0) return (null, "");
        var longest = found.Max(c => TingeePaymentMatcher.Key(c.CollectionNo).Length);
        var best = found.Where(c => TingeePaymentMatcher.Key(c.CollectionNo).Length == longest).ToList();
        return best.Count == 1 ? (best[0], TingeeMatchedBy.Content) : (null, "");
    }
}

public static class TingeeCollectionRules
{
    /// <summary>
    /// Splits <paramref name="amount"/> over the orders in position order: each order that still takes money gets at most
    /// what it owes. Returns the allocations and what is left over (paid beyond every order's debt).
    /// </summary>
    public static (IReadOnlyList<TingeeAllocation> Allocations, decimal Left) Allocate(IReadOnlyList<TingeeCollectionOrder> orders, decimal amount)
    {
        var list = new List<TingeeAllocation>();
        var left = amount;
        foreach (var o in orders.OrderBy(o => o.Position))
        {
            if (left <= 0) break;
            if (!o.TakesMoney) continue;
            var take = Math.Min(left, o.Remaining);
            var after = o.PaidAmount + take;
            list.Add(new(o.OrderId, o.OrderNo, take, o.PaidAmount, after, o.Status, TingeeOrderStatus.For(o.Amount, after)));
            left -= take;
        }
        return (list, left);
    }

    /// <summary>What the collection's orders still owe in total (orders that no longer take money count 0).</summary>
    public static decimal Due(IReadOnlyList<TingeeCollectionOrder> orders) => orders.Where(o => o.TakesMoney).Sum(o => o.Remaining);

    /// <summary>
    /// Operator 2026-11-27. The whole payment is counted on the collection; it is split over the orders oldest first. The
    /// collection is PAID once the amount asked is reached or no order owes anything more, otherwise PARTIALLY_PAID; a
    /// cancelled collection stays cancelled. The money is split in every case (it pays real debts), but a payment beyond the
    /// debts (OVERPAID), to a cancelled collection, after its QR expired, or that the partner reports differently makes the
    /// transaction REVIEW for staff instead of MATCHED.
    /// </summary>
    public static TingeeCollectionApplication Decide(long inboxId, TingeePaymentNotice notice, DateTime receivedAtUtc, TingeeCollectionCandidate collection,
        IReadOnlyList<TingeeCollectionOrder> orders, string matchedBy, decimal? partnerPaid)
    {
        var (allocations, left) = Allocate(orders, notice.Amount);
        var after = collection.PaidAmount + notice.Amount;
        var flags = new List<string>();
        var stillDue = Due(orders) - allocations.Sum(a => a.Amount);
        string status;
        if (collection.Status == PaymentCollectionStatus.Cancelled) { status = PaymentCollectionStatus.Cancelled; flags.Add(PaymentTxFlag.CollectionCancelled); }
        else
        {
            status = after >= collection.Amount || stillDue <= 0 ? PaymentCollectionStatus.Paid : after > 0 ? PaymentCollectionStatus.Partial : PaymentCollectionStatus.Awaiting;
            if (collection.ExpiresAtUtc is { } e && e <= receivedAtUtc && collection.PaidAmount < collection.Amount) flags.Add(PaymentTxFlag.QrExpired);
        }
        if (left > 0) flags.Add(PaymentTxFlag.Overpaid);
        if (partnerPaid is decimal p && p != after) flags.Add(PaymentTxFlag.PartnerMismatch);
        return new(inboxId, notice, receivedAtUtc, collection, matchedBy, collection.PaidAmount, after, partnerPaid, status, allocations, left,
            flags.Count == 0 ? PaymentTxStatus.Matched : PaymentTxStatus.Review, flags);
    }
}
