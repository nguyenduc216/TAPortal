using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Payment webhooks are applied to orders by the webhook site's worker, with nobody on the order page (2026-11-05).
public sealed class SafeTingeePaymentProcessorTests
{
    private static readonly Guid Customer = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    [Fact]
    public void PaymentBodiesAreReadTolerantly()
    {
        var n = TingeePaymentNotice.Parse("""{"transactionCode":"FT1","amount":"150000","content":" CK DH2611050001 ","vaAccountNumber":"VQR1","additionalData":[{"name":"billId","value":"BILL-7"}]}""")!;
        Assert.Equal(("FT1", 150_000m, "CK DH2611050001", "VQR1", true), (n.TransactionCode, n.Amount, n.Content, n.VaAccountNumber, n.IsCredit));
        Assert.Contains("BILL-7", n.AdditionalValues);
        Assert.False(TingeePaymentNotice.Parse("""{"amount":5,"type":"DEBIT"}""")!.IsCredit);
        Assert.Null(TingeePaymentNotice.Parse("not json"));
        Assert.Null(TingeePaymentNotice.Parse("[1,2]"));
    }

    [Fact]
    public void OrdersAreMatchedByBillIdThenTransferContentThenAmountWhenTheBankSendsNoContent()
    {
        var a = Order("DH261105-0001", 2000);
        var b = Order("DH261105-00012", 2000, bill: "BILL-B");
        var c = Order("DH261105-0002", 5000);
        Candidates all = [a, b, c];
        Assert.Equal((b, "billId"), TingeePaymentMatcher.Match(Notice(2000, "x", "BILL-B"), all));
        Assert.Equal(a, TingeePaymentMatcher.Match(Notice(2000, "chuyen tien dh261105 0001 cam on"), all).Order);
        Assert.Equal(b, TingeePaymentMatcher.Match(Notice(2000, "DH26110500012"), all).Order);   // longest order number wins
        Assert.Null(TingeePaymentMatcher.Match(Notice(2000, "tien an trua"), all).Order);
        Assert.Equal(c, TingeePaymentMatcher.Match(Notice(5000, null), all).Order);          // VietinBank: no content
        Assert.Null(TingeePaymentMatcher.Match(Notice(2000, null), all).Order);              // two orders owe 2 000
        Assert.Null(TingeePaymentMatcher.Match(Notice(2000, "DH2611050001"), []).Order);
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
        Assert.Equal(300_000, ledger.Orders.Single().PaidAmount);
        Assert.Equal(TingeeOrderStatus.Paid, ledger.Statuses[order.OrderId]);
        Assert.All(ledger.Rows.Values, r => Assert.Equal(TingeeInboxStatus.Processed, r.Status));
        Assert.Equal(0, await processor.RunOnceAsync());   // nothing pending: never counted twice
        Assert.Equal(300_000, ledger.Orders.Single().PaidAmount);
        Assert.Empty(tingee.DeletedQrs);
    }

    [Fact]
    public async Task DynamicQrPaymentsTakeTheAmountPaidFromThePartnerSoAManualCheckIsNotCountedAgain()
    {
        var (ledger, tingee, processor) = Setup();
        var order = Order("DH261105-0003", 300_000, bill: "BILL1", qrAccount: "QRA", merchant: 9001);
        ledger.Orders.Add(order);
        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 100_000 } };
        ledger.Add(1, """{"transactionCode":"FT1","amount":100000,"vaAccountNumber":"VQR1","additionalData":[{"name":"billId","value":"BILL1"}]}""");
        Assert.Equal(1, await processor.RunOnceAsync());
        Assert.Equal((100_000m, TingeeOrderStatus.Partial), (ledger.Orders.Single().PaidAmount, ledger.Statuses[order.OrderId]));

        // The partner has not counted the next payment yet: retried, then taken as already counted (e.g. by "Kiểm tra thanh toán").
        ledger.Add(2, """{"transactionCode":"FT2","amount":200000,"vaAccountNumber":"VQR1","additionalData":[{"name":"billId","value":"BILL1"}]}""");
        for (var i = 0; i < TingeePaymentProcessor.LagRetries - 1; i++)
        {
            Assert.Equal(0, await processor.RunOnceAsync());
            Assert.Equal(TingeeInboxStatus.Received, ledger.Rows[2].Status);
        }
        tingee.Status = new() { BillInfo = new() { Amount = 300_000, TotalAmountPaid = 300_000 } };
        Assert.Equal(1, await processor.RunOnceAsync());
        Assert.Equal((300_000m, TingeeOrderStatus.Paid), (ledger.Orders.Single().PaidAmount, ledger.Statuses[order.OrderId]));
        Assert.Equal(("QRA", "BILL1"), tingee.DeletedQrs.Single());
    }

    [Fact]
    public async Task DebitsUnreadableBodiesAndUnknownOrdersAreSetAsideWithAReason()
    {
        var (ledger, tingee, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0001", 2000));
        ledger.Add(1, """{"transactionCode":"FT1","amount":2000,"type":"debit","content":"DH2611050001"}""");
        ledger.Add(2, "garbage");
        ledger.Add(3, """{"transactionCode":"FT3","amount":2000,"content":"tien nha"}""");
        Assert.Equal(0, await processor.RunOnceAsync());
        Assert.Equal((TingeeInboxStatus.Ignored, TingeeInboxStatus.Failed, TingeeInboxStatus.Unmatched), (ledger.Rows[1].Status, ledger.Rows[2].Status, ledger.Rows[3].Status));
        Assert.Contains("not found in the transfer content", ledger.Rows[3].Note);
        Assert.Equal(0, ledger.Orders.Single().PaidAmount);
    }

    [Fact]
    public async Task PartnerFailuresAreRetriedThenMarkedFailed()
    {
        var (ledger, tingee, processor) = Setup();
        ledger.Orders.Add(Order("DH261105-0003", 300_000, bill: "BILL1", qrAccount: "QRA", merchant: 9001));
        tingee.StatusError = new HttpRequestException("timeout");
        ledger.Add(1, """{"transactionCode":"FT1","amount":1,"additionalData":["BILL1"]}""");
        for (var i = 1; i < TingeePaymentProcessor.MaxAttempts; i++)
        {
            await processor.RunOnceAsync();
            Assert.Equal((TingeeInboxStatus.Received, i), (ledger.Rows[1].Status, ledger.Rows[1].Attempts));
        }
        await processor.RunOnceAsync();
        Assert.Equal(TingeeInboxStatus.Failed, ledger.Rows[1].Status);
        Assert.Equal(0, ledger.Orders.Single().PaidAmount);
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

    private sealed class Candidates : List<TingeePaymentCandidate>;

    private sealed class MemoryLedger : ITingeePaymentLedger
    {
        public sealed class Row(string body) { public string Body = body; public string Status = TingeeInboxStatus.Received; public int Attempts; public string? Note; }
        public SortedDictionary<long, Row> Rows { get; } = [];
        public List<TingeePaymentCandidate> Orders { get; } = [];
        public Dictionary<Guid, string> Statuses { get; } = [];

        public void Add(long id, string body) => Rows[id] = new Row(body);

        public Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TingeePendingPayment>>(Rows.Where(r => r.Value.Status == TingeeInboxStatus.Received).Take(max).Select(r => new TingeePendingPayment(r.Key, r.Value.Body, r.Value.Attempts, DateTime.UtcNow)).ToList());

        public Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TingeePaymentCandidate>>(Orders.Where(o => !Statuses.TryGetValue(o.OrderId, out var s) || s != TingeeOrderStatus.Paid).ToList());

        public Task<bool> ApplyAsync(long inboxId, TingeePaymentCandidate order, decimal expectedPaid, decimal newPaid, string note, CancellationToken ct)
        {
            var row = Rows[inboxId];
            var i = Orders.FindIndex(o => o.OrderId == order.OrderId);
            if (row.Status != TingeeInboxStatus.Received || Orders[i].PaidAmount != expectedPaid) return Task.FromResult(false);
            (row.Status, row.Note) = (TingeeInboxStatus.Processed, note);
            row.Attempts++;
            Orders[i] = Orders[i] with { PaidAmount = newPaid };
            Statuses[order.OrderId] = TingeeOrderStatus.For(order.Amount, newPaid);
            return Task.FromResult(true);
        }

        public Task MarkAsync(long inboxId, string status, string note, CancellationToken ct)
        {
            var row = Rows[inboxId];
            if (row.Status == TingeeInboxStatus.Received) { (row.Status, row.Note) = (status, note); row.Attempts++; }
            return Task.CompletedTask;
        }
    }
}
