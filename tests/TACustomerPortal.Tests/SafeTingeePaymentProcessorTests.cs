using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Payment webhooks are applied to orders by the webhook site's worker, with nobody on the order page (2026-11-05), by order
// number only; every payment ends as a transaction record (MATCHED / REVIEW / UNMATCHED / IGNORED).
public sealed class SafeTingeePaymentProcessorTests
{
    private static readonly Guid Customer = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    [Fact]
    public void PaymentBodiesAreReadTolerantly()
    {
        var n = TingeePaymentNotice.Parse("""{"transactionCode":"FT1","amount":"150000","content":" CK DH2611050001 ","vaAccountNumber":"VQR1","transactionDate":"20261105143000","additionalData":[{"name":"billId","value":"BILL-7"}]}""")!;
        Assert.Equal(("FT1", 150_000m, "CK DH2611050001", "VQR1", true), (n.TransactionCode, n.Amount, n.Content, n.VaAccountNumber, n.IsCredit));
        Assert.Contains("BILL-7", n.AdditionalValues);
        Assert.Equal(new DateTime(2026, 11, 5, 7, 30, 0, DateTimeKind.Utc), n.TransactionAtUtc);
        Assert.False(TingeePaymentNotice.Parse("""{"amount":5,"type":"DEBIT"}""")!.IsCredit);
        Assert.Null(TingeePaymentNotice.Parse("not json"));
        Assert.Null(TingeePaymentNotice.Parse("[1,2]"));
    }

    [Fact]
    public void OrdersAreMatchedByOrderNumberOnlyNeverByAmount()
    {
        var a = Order("DH261105-0001", 2000);
        var b = Order("DH261105-00012", 2000, bill: "BILL-B");
        var c = Order("DH261105-0002", 5000);
        List<TingeePaymentCandidate> all = [a, b, c];
        Assert.Equal((b, TingeeMatchedBy.BillId), TingeePaymentMatcher.Match(Notice(2000, "x", "BILL-B"), all));
        Assert.Equal(a, TingeePaymentMatcher.Match(Notice(2000, "chuyen tien dh261105 0001 cam on"), all).Order);
        Assert.Equal(b, TingeePaymentMatcher.Match(Notice(2000, "DH26110500012"), all).Order);   // longest order number wins
        Assert.Null(TingeePaymentMatcher.Match(Notice(2000, "tien an trua"), all).Order);
        Assert.Null(TingeePaymentMatcher.Match(Notice(5000, null), all).Order);                // VietinBank: no content ⇒ staff, even if one order owes 5 000
        Assert.Null(TingeePaymentMatcher.Match(Notice(2000, "DH2611050001"), []).Order);
    }

    [Fact]
    public void TheOrderStateFollowsTheOperatorRules()
    {
        var now = DateTime.UtcNow;
        var n = Notice(100, "DH2611050001");
        TingeePaymentApplication D(TingeePaymentCandidate o, decimal? partner = null) => TingeePaymentRules.Decide(1, n, now, o, TingeeMatchedBy.Content, partner);

        var open = Order("DH261105-0001", 300) with { ExpiresAtUtc = now.AddHours(1) };
        Assert.Equal((TingeeOrderStatus.Partial, PaymentTxStatus.Matched, 100m), (D(open).OrderStatus, D(open).TxStatus, D(open).PaidAfter));
        Assert.Equal((TingeeOrderStatus.Paid, PaymentTxStatus.Matched), (D(open with { PaidAmount = 200 }).OrderStatus, D(open with { PaidAmount = 200 }).TxStatus));

        var over = D(open with { PaidAmount = 250 });
        Assert.Equal((TingeeOrderStatus.Paid, PaymentTxStatus.Review, PaymentTxFlag.Overpaid), (over.OrderStatus, over.TxStatus, over.Flags.Single()));

        var expired = D(open with { ExpiresAtUtc = now.AddMinutes(-1) });
        Assert.Equal((TingeeOrderStatus.ExpiredPaid, PaymentTxStatus.Review, PaymentTxFlag.QrExpired), (expired.OrderStatus, expired.TxStatus, expired.Flags.Single()));
        Assert.Equal(TingeeOrderStatus.ExpiredPaid, D(open with { Status = TingeeOrderStatus.Expired }).OrderStatus);

        var cancelled = D(open with { Status = TingeeOrderStatus.Cancelled });
        Assert.Equal((TingeeOrderStatus.CancelledPaid, PaymentTxFlag.OrderCancelled), (cancelled.OrderStatus, cancelled.Flags.Single()));

        var mismatch = D(open, partner: 0);
        Assert.Equal((PaymentTxStatus.Review, 0m, PaymentTxFlag.PartnerMismatch), (mismatch.TxStatus, mismatch.PartnerPaid, mismatch.Flags.Single()));
        Assert.Equal(PaymentTxStatus.Matched, D(open, partner: 100).TxStatus);
    }

    [Fact]
    public async Task StaticQrPaymentsAreAddedOnceAndCompleteTheOrder()
    {
        var (ledger, tingee, processor) = Setup();
        var order = Order("DH261105-0001", 300_000);
        ledger.Orders.Add(order);
        ledger.Add(1, """{"transactionCode":"FT1","amount":100000,"content":"DH2611050001","accountNumber":"12345678"}""");
        ledger.Add(2, """{"transactionCode":"FT2","amount":200000,"content":"DH2611050001 lan 2","accountNumber":"12345678"}""");

        Assert.Equal(2, await processor.RunOnceAsync());
        Assert.Equal((300_000m, TingeeOrderStatus.Paid), (ledger.Orders.Single().PaidAmount, ledger.Orders.Single().Status));
        Assert.All(ledger.Rows.Values, r => Assert.Equal(TingeeInboxStatus.Processed, r.Status));
        Assert.Equal([(0m, 100_000m), (100_000m, 300_000m)], ledger.Transactions.Select(t => (t.Before!.Value, t.After!.Value)));
        Assert.All(ledger.Transactions, t => Assert.Equal(PaymentTxStatus.Matched, t.Status));
        Assert.Equal(0, await processor.RunOnceAsync());   // nothing pending: never counted twice
        Assert.Equal(300_000, ledger.Orders.Single().PaidAmount);
        Assert.Empty(tingee.DeletedQrs);
    }

    [Fact]
    public async Task DynamicQrPaymentsAreCountedByTheSystemAndReconciledWithThePartnerTotal()
    {
        var (ledger, tingee, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0003", 300_000, bill: "BILL1", qrAccount: "QRA", merchant: 9001));
        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 100_000 } };
        ledger.Add(1, """{"transactionCode":"FT1","amount":100000,"vaAccountNumber":"VQR1","additionalData":[{"name":"billId","value":"BILL1"}]}""");
        Assert.Equal(1, await processor.RunOnceAsync());
        Assert.Equal((100_000m, TingeeOrderStatus.Partial), (ledger.Orders.Single().PaidAmount, ledger.Orders.Single().Status));
        Assert.Equal((100_000m, 100_000m, PaymentTxStatus.Matched), (ledger.Transactions[0].After!.Value, ledger.Transactions[0].Partner!.Value, ledger.Transactions[0].Status));

        // The partner does not count the next payment yet: retried, then applied anyway with the difference flagged.
        ledger.Add(2, """{"transactionCode":"FT2","amount":200000,"vaAccountNumber":"VQR1","additionalData":[{"name":"billId","value":"BILL1"}]}""");
        for (var i = 0; i < TingeePaymentProcessor.LagRetries - 1; i++)
        {
            Assert.Equal(0, await processor.RunOnceAsync());
            Assert.Equal(TingeeInboxStatus.Received, ledger.Rows[2].Status);
        }
        Assert.Equal(1, await processor.RunOnceAsync());
        Assert.Equal((300_000m, TingeeOrderStatus.Paid), (ledger.Orders.Single().PaidAmount, ledger.Orders.Single().Status));
        var second = ledger.Transactions[1];
        Assert.Equal((300_000m, 100_000m, PaymentTxStatus.Review, PaymentTxFlag.PartnerMismatch), (second.After!.Value, second.Partner!.Value, second.Status, second.Flags));
        Assert.Equal(("QRA", "BILL1"), tingee.DeletedQrs.Single());
    }

    [Fact]
    public async Task DebitsUnreadableBodiesAndUnknownOrdersAreRecordedForStaff()
    {
        var (ledger, _, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0001", 2000));
        ledger.Add(1, """{"transactionCode":"FT1","amount":2000,"type":"debit","content":"DH2611050001"}""");
        ledger.Add(2, "garbage");
        ledger.Add(3, """{"transactionCode":"FT3","amount":2000,"content":"tien nha"}""");
        ledger.Add(4, """{"transactionCode":"FT4","amount":2000}""");
        Assert.Equal(0, await processor.RunOnceAsync());
        Assert.Equal((TingeeInboxStatus.Ignored, TingeeInboxStatus.Unmatched, TingeeInboxStatus.Unmatched, TingeeInboxStatus.Unmatched),
            (ledger.Rows[1].Status, ledger.Rows[2].Status, ledger.Rows[3].Status, ledger.Rows[4].Status));
        Assert.Equal([PaymentTxStatus.Ignored, PaymentTxStatus.Unmatched, PaymentTxStatus.Unmatched, PaymentTxStatus.Unmatched], ledger.Transactions.Select(t => t.Status));
        Assert.Contains("mã đơn hàng", ledger.Transactions[2].Reason);
        Assert.Contains("không có nội dung", ledger.Transactions[3].Reason);
        Assert.Equal(0, ledger.Orders.Single().PaidAmount);
    }

    [Fact]
    public async Task ProcessingFailuresAreRetriedThenLeftUnmatchedForStaff()
    {
        var (ledger, _, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0001", 2000));
        ledger.CandidatesError = new InvalidOperationException("database down");
        ledger.Add(1, """{"transactionCode":"FT1","amount":2000,"content":"DH2611050001"}""");
        for (var i = 1; i < TingeePaymentProcessor.MaxAttempts; i++)
        {
            await processor.RunOnceAsync();
            Assert.Equal((TingeeInboxStatus.Received, i), (ledger.Rows[1].Status, ledger.Rows[1].Attempts));
        }
        await processor.RunOnceAsync();
        Assert.Equal(TingeeInboxStatus.Unmatched, ledger.Rows[1].Status);
        Assert.Equal(PaymentTxStatus.Unmatched, ledger.Transactions.Single().Status);
        Assert.Equal(0, ledger.Orders.Single().PaidAmount);
    }

    [Fact]
    public async Task AnUnreachablePartnerNeverBlocksThePayment()
    {
        var (ledger, tingee, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0003", 300_000, bill: "BILL1", qrAccount: "QRA", merchant: 9001));
        tingee.StatusError = new HttpRequestException("timeout");
        ledger.Add(1, """{"transactionCode":"FT1","amount":300000,"additionalData":["BILL1"]}""");
        for (var i = 0; i < TingeePaymentProcessor.LagRetries - 1; i++) Assert.Equal(0, await processor.RunOnceAsync());
        Assert.Equal(1, await processor.RunOnceAsync());
        Assert.Equal((300_000m, TingeeOrderStatus.Paid), (ledger.Orders.Single().PaidAmount, ledger.Orders.Single().Status));
        Assert.Null(ledger.Transactions.Single().Partner);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static (MemoryLedger, SafeCustomerPaymentsTests.FakeTingee, TingeePaymentProcessor) Setup()
    {
        var ledger = new MemoryLedger();
        var tingee = new SafeCustomerPaymentsTests.FakeTingee();
        return (ledger, tingee, new TingeePaymentProcessor(ledger, tingee, NullLogger<TingeePaymentProcessor>.Instance));
    }

    private static TingeePaymentCandidate Order(string no, decimal amount, string? bill = null, string? qrAccount = null, long? merchant = null) =>
        new(Customer, Guid.NewGuid(), no, bill, amount, 0, qrAccount, merchant);

    private static TingeePaymentNotice Notice(decimal amount, string? content, params string[] extra) =>
        new("FT", amount, content, null, null, "VQR1", null, true, extra);

    private sealed class MemoryLedger : ITingeePaymentLedger
    {
        public sealed class Row(string body) { public string Body = body; public string Status = TingeeInboxStatus.Received; public int Attempts; public string? Note; }
        public sealed record Tx(string Status, string? Flags, string Reason, decimal? Before, decimal? After, decimal? Partner);
        public SortedDictionary<long, Row> Rows { get; } = [];
        public List<TingeePaymentCandidate> Orders { get; } = [];
        public List<Tx> Transactions { get; } = [];
        public Exception? CandidatesError { get; set; }

        public void Add(long id, string body) => Rows[id] = new Row(body);

        public Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TingeePendingPayment>>(Rows.Where(r => r.Value.Status == TingeeInboxStatus.Received).Take(max).Select(r => new TingeePendingPayment(r.Key, r.Value.Body, r.Value.Attempts, DateTime.UtcNow)).ToList());

        public Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct) =>
            CandidatesError is not null ? throw CandidatesError : Task.FromResult<IReadOnlyList<TingeePaymentCandidate>>(Orders.ToList());

        public Task<bool> ApplyAsync(TingeePaymentApplication p, CancellationToken ct)
        {
            var row = Rows[p.InboxId];
            var i = Orders.FindIndex(o => o.OrderId == p.Order.OrderId);
            if (row.Status != TingeeInboxStatus.Received || Orders[i].PaidAmount != p.PaidBefore || Orders[i].Status != p.Order.Status) return Task.FromResult(false);
            (row.Status, row.Note) = (TingeeInboxStatus.Processed, p.Order.OrderNo);
            row.Attempts++;
            Orders[i] = Orders[i] with { PaidAmount = p.PaidAfter, Status = p.OrderStatus };
            Transactions.Add(new(p.TxStatus, p.Flags.Count == 0 ? null : string.Join(",", p.Flags), p.Order.OrderNo, p.PaidBefore, p.PaidAfter, p.PartnerPaid));
            return Task.FromResult(true);
        }

        public Task RecordAsync(long inboxId, TingeePaymentNotice? notice, DateTime receivedAtUtc, string inboxStatus, string txStatus, string reason, CancellationToken ct)
        {
            var row = Rows[inboxId];
            if (row.Status != TingeeInboxStatus.Received) return Task.CompletedTask;
            (row.Status, row.Note) = (inboxStatus, reason);
            row.Attempts++;
            Transactions.Add(new(txStatus, null, reason, null, null, null));
            return Task.CompletedTask;
        }

        public Task RetryLaterAsync(long inboxId, string note, CancellationToken ct) { var row = Rows[inboxId]; (row.Note, row.Attempts) = (note, row.Attempts + 1); return Task.CompletedTask; }

        public Task FailAsync(long inboxId, string note, CancellationToken ct) { var row = Rows[inboxId]; (row.Status, row.Note) = (TingeeInboxStatus.Failed, note); row.Attempts++; return Task.CompletedTask; }
    }
}
