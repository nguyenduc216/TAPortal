using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;
using static TACustomerPortal.Tests.SafeCustomerPaymentsTests;

namespace TACustomerPortal.Tests;

// "Thu gộp công nợ" (operator 2026-11-27): several unpaid orders of one buyer in one QR; the money is split over them oldest
// first, the customer may pay only part. No SQL: the in-memory store below follows SqlPaymentCollectionStore's rules.
public sealed class SafePaymentCollectionTests
{
    private static readonly Guid Customer = A;

    // ---- rules -----------------------------------------------------------------------------------------
    [Fact]
    public void MoneyPaysTheOrdersInTheirOrderEachUpToWhatItOwes()
    {
        var first = Debt("DH261101-0001", 3_000_000, position: 1);
        var second = Debt("DH261102-0001", 2_000_000, position: 2);
        var (split, left) = TingeeCollectionRules.Allocate([second, first], 4_000_000);
        Assert.Equal([("DH261101-0001", 3_000_000m, TingeeOrderStatus.Paid), ("DH261102-0001", 1_000_000m, TingeeOrderStatus.Partial)],
            split.Select(s => (s.OrderNo, s.Amount, s.OrderStatus)));
        Assert.Equal(0, left);

        // An order already partly paid takes only its remainder; paid and cancelled orders take nothing; the rest is left over.
        var partly = Debt("DH261103-0001", 1_000_000, position: 1, paid: 400_000, status: TingeeOrderStatus.Partial);
        var paid = Debt("DH261104-0001", 500_000, position: 2, paid: 500_000, status: TingeeOrderStatus.Paid);
        var cancelled = Debt("DH261105-0001", 700_000, position: 3, status: TingeeOrderStatus.Cancelled);
        var expired = Debt("DH261106-0001", 200_000, position: 4, status: TingeeOrderStatus.Expired);
        (split, left) = TingeeCollectionRules.Allocate([partly, paid, cancelled, expired], 1_000_000);
        Assert.Equal([("DH261103-0001", 600_000m, 400_000m, 1_000_000m), ("DH261106-0001", 200_000m, 0m, 200_000m)],
            split.Select(s => (s.OrderNo, s.Amount, s.PaidBefore, s.PaidAfter)));
        Assert.Equal(200_000, left);
        Assert.Equal(800_000, TingeeCollectionRules.Due([partly, paid, cancelled, expired]));
    }

    [Fact]
    public void ACollectionIsPaidWhenTheAmountIsReachedOrNoOrderOwesAnyMore()
    {
        var orders = new[] { Debt("DH1", 3_000_000, 1), Debt("DH2", 2_000_000, 2) };
        var c = Candidate(5_000_000);
        var part = TingeeCollectionRules.Decide(1, Notice(1_500_000, "TT2611270001"), DateTime.UtcNow, c, orders, TingeeMatchedBy.Content, null);
        Assert.Equal((PaymentCollectionStatus.Partial, PaymentTxStatus.Matched, 1_500_000m), (part.CollectionStatus, part.TxStatus, part.PaidAfter));

        var full = TingeeCollectionRules.Decide(1, Notice(5_000_000, "TT2611270001"), DateTime.UtcNow, c, orders, TingeeMatchedBy.Content, null);
        Assert.Equal((PaymentCollectionStatus.Paid, PaymentTxStatus.Matched), (full.CollectionStatus, full.TxStatus));

        // The second order was paid through its own QR meanwhile: 3 000 000 settles everything.
        var settled = TingeeCollectionRules.Decide(1, Notice(3_000_000, "x"), DateTime.UtcNow, c, [orders[0], orders[1] with { PaidAmount = 2_000_000, Status = TingeeOrderStatus.Paid }],
            TingeeMatchedBy.Content, null);
        Assert.Equal(PaymentCollectionStatus.Paid, settled.CollectionStatus);

        // Paid twice (own QR and the collection): the extra money is counted but flagged for staff.
        var over = TingeeCollectionRules.Decide(1, Notice(5_000_000, "x"), DateTime.UtcNow, c, [orders[0], orders[1] with { PaidAmount = 2_000_000, Status = TingeeOrderStatus.Paid }],
            TingeeMatchedBy.Content, null);
        Assert.Equal((PaymentTxStatus.Review, 2_000_000m), (over.TxStatus, over.Unallocated));
        Assert.Contains(PaymentTxFlag.Overpaid, over.Flags);

        // Paid to a cancelled collection: still split over the debts, flagged; the collection stays cancelled.
        var late = TingeeCollectionRules.Decide(1, Notice(1_000_000, "x"), DateTime.UtcNow, c with { Status = PaymentCollectionStatus.Cancelled }, orders, TingeeMatchedBy.Content, null);
        Assert.Equal((PaymentCollectionStatus.Cancelled, PaymentTxStatus.Review, 1), (late.CollectionStatus, late.TxStatus, late.Allocations.Count));
        Assert.Contains(PaymentTxFlag.CollectionCancelled, late.Flags);
        Assert.Equal("Phiếu thu đã hủy có giao dịch", PaymentTxText.Flag(PaymentTxFlag.CollectionCancelled));
    }

    [Fact]
    public void CollectionsAreFoundByTheirNumberOrQrAndNeverByAnOrderNumber()
    {
        var a = Candidate(1_000, "TT261127-0001");
        var b = Candidate(1_000, "TT261127-0002", bill: "BILL-C");
        Assert.Equal((a, TingeeMatchedBy.Content), TingeeCollectionMatcher.Match(Notice(1_000, "ck tt261127 0001 cong no"), [a, b]));
        Assert.Equal((b, TingeeMatchedBy.BillId), TingeeCollectionMatcher.Match(Notice(1_000, "x", "BILL-C"), [a, b]));
        Assert.Null(TingeeCollectionMatcher.Match(Notice(1_000, "DH2611270001"), [a, b]).Collection);
        Assert.Null(TingeeCollectionMatcher.Match(Notice(1_000, null), [a, b]).Collection);
        Assert.True(PaymentCollectionNo.IsCollection("TT261127-0001"));
        Assert.False(PaymentCollectionNo.IsCollection("DH261127-0001"));
    }

    [Fact]
    public async Task ThePaymentProcessorSplitsACollectionPaymentOverItsOrders()
    {
        var ledger = new CollectionLedger();
        var processor = new TingeePaymentProcessor(ledger, new FakeTingee(), NullLogger<TingeePaymentProcessor>.Instance);
        ledger.Collections.Add(Candidate(5_000_000, "TT261127-0001"));
        ledger.Orders.AddRange([Debt("DH261101-0001", 3_000_000, 1), Debt("DH261102-0001", 2_000_000, 2)]);
        ledger.Add(1, """{"transactionCode":"FT1","amount":4000000,"content":"TT2611270001 thanh toan","vaAccountNumber":"VQR1"}""");
        Assert.Equal(1, await processor.RunOnceAsync());
        var applied = ledger.Applied.Single();
        Assert.Equal((4_000_000m, PaymentCollectionStatus.Partial, PaymentTxStatus.Matched), (applied.PaidAfter, applied.CollectionStatus, applied.TxStatus));
        Assert.Equal([3_000_000m, 1_000_000m], applied.Allocations.Select(x => x.Amount));
        Assert.Equal(TingeeInboxStatus.Processed, ledger.Rows[1].Status);

        // A payment without the collection number still goes to an order (no collection found).
        ledger.Add(2, """{"transactionCode":"FT2","amount":1000,"content":"tien an","vaAccountNumber":"VQR1"}""");
        await processor.RunOnceAsync();
        Assert.Single(ledger.Applied);
        Assert.Equal(TingeeInboxStatus.Unmatched, ledger.Rows[2].Status);
    }

    // ---- portal ----------------------------------------------------------------------------------------
    [Fact]
    public async Task TheSellerGathersUnpaidOrdersIntoOneQrAndTheBuyerSeesThemOnOnePage()
    {
        using var f = Factory("OWNER", out var store, out var tingee);
        var collections = f.Services.GetRequiredService<MemoryCollectionStore>();
        store.Merchants[A] = 9001;
        var bank = Bank("970418", BankStatus.Active, TingeeAccountTypes.Business) with { VaAccountNumber = "VA9", IsDefault = true };
        store.Banks.Add((A, bank));
        var buyer = new Contact(Guid.NewGuid(), "KH001", "Nhà hàng Sen", null, null, null, null, null, null, true);
        store.Contacts.Add((A, buyer));
        var older = Unpaid("DH261101-0001", buyer.Id, 3_000_000, DateTime.UtcNow.AddDays(-20));
        var newer = Unpaid("DH261115-0001", buyer.Id, 2_000_000, DateTime.UtcNow.AddDays(-5), paid: 500_000);
        var settled = Unpaid("DH261110-0001", buyer.Id, 900_000, DateTime.UtcNow.AddDays(-10)) with { Status = OrderStatus.Paid, PaidAmount = 900_000 };
        store.Orders.AddRange([(A, newer), (A, settled), (A, older)]);
        var c = Client(f);

        // The buyer is offered with its debt; its unpaid orders are listed oldest first, all ticked; the paid one is not offered.
        var form = WebUtility.HtmlDecode(await c.GetStringAsync($"/collections/create?contactId={buyer.Id}"));
        Assert.Contains("KH001 — Nhà hàng Sen · 2 đơn · nợ 4.500.000 ₫", form);
        Assert.True(form.IndexOf("DH261101-0001", StringComparison.Ordinal) < form.IndexOf("DH261115-0001", StringComparison.Ordinal));
        Assert.DoesNotContain("DH261110-0001", form);
        Assert.Equal(2, Regex.Matches(form, "name=\"OrderIds\" value=\"[^\"]+\" checked=\"checked\"").Count);

        // More than they owe is refused.
        var token = await Token(c, $"/collections/create?contactId={buyer.Id}");
        var tooMuch = await Post(c, "/collections/create", token, Fields(buyer.Id, bank.Id, "4500001", newer.Id, older.Id));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("tổng còn nợ 4.500.000 ₫", WebUtility.HtmlDecode(await tooMuch.Content.ReadAsStringAsync()));

        // Part of the debt, newest order first (the user moved it up): one QR for 2 000 000 with the collection number.
        var created = await Post(c, "/collections/create", token, Fields(buyer.Id, bank.Id, "2000000", newer.Id, older.Id));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var collection = collections.Items.Single();
        Assert.Equal(("TT", 2_000_000m, 2), (collection.CollectionNo[..2], collection.Amount, collection.Orders.Count));
        Assert.Equal([newer.Id, older.Id], collection.Orders.OrderBy(o => o.Position).Select(o => o.OrderId));
        var qr = tingee.DynamicQrRequests.Single();
        Assert.Equal((2_000_000m, CollectionMath.TransferContent(collection.CollectionNo)), (qr.Amount, qr.Purpose));

        // Details: the orders, the QR and the payment link; the order page points to the collection.
        var details = WebUtility.HtmlDecode(await c.GetStringAsync(created.Headers.Location!.OriginalString));
        Assert.Contains("Phiếu thu " + collection.CollectionNo, details);
        Assert.Contains("<svg", details);
        var link = Regex.Match(details, "https://localhost/pay/c/[A-Za-z0-9_-]{32}").Value;
        Assert.NotEmpty(link);
        Assert.Contains(collection.CollectionNo, WebUtility.HtmlDecode(await c.GetStringAsync($"/orders/details/{older.Id}")));

        // An order already in an open collection cannot be gathered again.
        var again = WebUtility.HtmlDecode(await c.GetStringAsync($"/collections/create?contactId={buyer.Id}"));
        Assert.Contains("Đang trong phiếu thu", again);
        var twice = await Post(c, "/collections/create", await Token(c, $"/collections/create?contactId={buyer.Id}"), Fields(buyer.Id, bank.Id, "", older.Id));
        Assert.Equal(HttpStatusCode.BadRequest, twice.StatusCode);
        Assert.Single(collections.Items);

        // The public page: both parties, every order with what it still owes, one QR, no partner name, the maker line.
        var anonymous = Client(f);
        var page = WebUtility.HtmlDecode(await anonymous.GetStringAsync(link));
        Assert.Contains("Nhà hàng Sen", page);
        Assert.Contains("DH261101-0001", page);
        Assert.Contains("DH261115-0001", page);
        Assert.Contains("2.000.000 ₫", page);
        Assert.Contains("<svg", page);
        Assert.Contains("Sản phẩm của Tư vấn giải pháp T.A", page);
        Assert.False(PartnerText.Mentions(Regex.Replace(page, "<script.*?</script>|<link[^>]*>", "", RegexOptions.Singleline)));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/pay/c/" + new string('x', 32))).StatusCode);

        // Lists and the cancel button; cancelled, the QR is gone and the orders keep what they received.
        Assert.Contains(collection.CollectionNo, await c.GetStringAsync("/collections"));
        var cancel = await Post(c, $"/collections/cancel/{collection.Id}", await Token(c, $"/collections/details/{collection.Id}"), []);
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal(PaymentCollectionStatus.Cancelled, collections.Items.Single().Status);
        Assert.Contains("đã hủy", WebUtility.HtmlDecode(await anonymous.GetStringAsync(link)));
    }

    [Fact]
    public async Task MembersSeeCollectionsButOnlyManagersCreateThem()
    {
        using var f = Factory("MEMBER", out _, out _);
        var c = Client(f);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/collections")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/collections/create")).StatusCode);
    }

    // ---- helpers ---------------------------------------------------------------------------------------
    private static Dictionary<string, string> Fields(Guid contact, Guid bank, string amount, params Guid[] orders)
    {
        var data = new Dictionary<string, string> { ["ContactId"] = contact.ToString(), ["BankAccountId"] = bank.ToString(), ["Amount"] = amount, ["QrExpireDays"] = "1", ["QrExpireHours"] = "0", ["QrExpireMins"] = "0" };
        for (var i = 0; i < orders.Length; i++) data[$"OrderIds[{i}]"] = orders[i].ToString();
        return data;
    }

    private static Order Unpaid(string no, Guid contact, decimal amount, DateTime created, decimal paid = 0) =>
        new(Guid.NewGuid(), no, contact, "Nhà hàng Sen", null, "Tiệc " + no, amount, paid, paid > 0 ? OrderStatus.Partial : OrderStatus.Awaiting, null, null, null, null,
            1440, DateTime.UtcNow.AddDays(1), true, OrderInvoiceStatus.None, null, created, [OrderMath.Line(1, "Hàng", null, 1, amount, "0")]);

    private static TingeeCollectionOrder Debt(string no, decimal amount, int position, decimal paid = 0, string status = TingeeOrderStatus.Awaiting) =>
        new(Guid.NewGuid(), no, position, amount, paid, status);

    private static TingeeCollectionCandidate Candidate(decimal amount, string no = "TT261127-0001", string? bill = null) =>
        new(Customer, Guid.NewGuid(), no, bill, amount, 0);

    private static TingeePaymentNotice Notice(decimal amount, string? content, params string[] extra) =>
        new("FT", amount, content, null, null, "VQR1", null, true, extra);

    /// <summary>Same rules as SqlPaymentCollectionStore, over MemoryPaymentStore's orders.</summary>
    public sealed class MemoryCollectionStore(MemoryPaymentStore payments) : IPaymentCollectionStore
    {
        public sealed record Item(Guid Id, string CollectionNo, Guid ContactId, Guid BankAccountId, string? Description, decimal Amount, decimal PaidAmount, string Status,
            int QrExpireMinutes, List<(Guid OrderId, int Position, decimal Due)> Orders, DateTime CreatedAt)
        {
            public string? QrType, QrPayload, QrAccount, BillId, LastError, Token;
            public DateTime? ExpiresAt;
        }

        public List<Item> Items { get; } = [];
        private Item[] Of(Guid customerId) => [.. Items];

        private Collection View(Guid customerId, Item i, bool withOrders) => new(i.Id, i.CollectionNo, i.ContactId,
            payments.Contacts.FirstOrDefault(c => c.Contact.Id == i.ContactId).Contact?.Name, i.BankAccountId, i.Description, i.Amount, i.PaidAmount, i.Status,
            i.QrType, i.QrPayload, i.QrAccount, i.BillId, i.QrExpireMinutes, i.ExpiresAt, i.LastError, i.CreatedAt, null,
            withOrders ? i.Orders.Select(o => (o, Order: payments.Orders.Single(x => x.Customer == customerId && x.Order.Id == o.OrderId).Order))
                .Select(x => new CollectionOrder(x.o.OrderId, x.Order.OrderNo, x.o.Position, x.o.Due, x.Order.Amount, x.Order.PaidAmount, x.Order.Status, x.Order.Description, x.Order.CreatedAt, 0))
                .OrderBy(o => o.Position).ToList() : [], i.Orders.Count);

        public Task<IReadOnlyList<Collection>> CollectionsAsync(Guid customerId) => Task.FromResult<IReadOnlyList<Collection>>(Of(customerId).Select(i => View(customerId, i, false)).ToList());
        public Task<Collection?> CollectionAsync(Guid customerId, Guid id) => Task.FromResult(Of(customerId).Where(i => i.Id == id).Select(i => View(customerId, i, true)).FirstOrDefault());
        public Task<IReadOnlyList<CollectionPaymentRow>> PaymentsAsync(Guid customerId, Guid id) => Task.FromResult<IReadOnlyList<CollectionPaymentRow>>([]);
        public Task<IReadOnlyDictionary<Guid, (Guid Id, string CollectionNo)>> OpenCollectionsByOrderAsync(Guid customerId) =>
            Task.FromResult<IReadOnlyDictionary<Guid, (Guid, string)>>(Items.Where(i => PaymentCollectionStatus.IsOpen(i.Status))
                .SelectMany(i => i.Orders.Select(o => (o.OrderId, (i.Id, i.CollectionNo)))).ToDictionary(x => x.OrderId, x => x.Item2));

        public Task<CollectionCreated> CreateAsync(Guid customerId, NewCollection c, Guid actor)
        {
            var open = OpenCollectionsByOrderAsync(customerId).Result;
            if (c.OrderIds.Any(open.ContainsKey)) return Task.FromResult(new CollectionCreated(Guid.Empty, "", "Có đơn hàng đã nằm trong một phiếu thu khác đang mở."));
            var orders = c.OrderIds.Select((id, n) => (id, n + 1, payments.Orders.Single(o => o.Order.Id == id).Order.Remaining)).ToList();
            var item = new Item(Guid.NewGuid(), $"TT{DateTime.UtcNow.AddHours(7):yyMMdd}-{Items.Count + 1:0000}", c.ContactId, c.BankAccountId, c.Description, c.Amount, 0,
                PaymentCollectionStatus.Awaiting, c.QrExpireMinutes, orders, DateTime.UtcNow);
            Items.Add(item);
            return Task.FromResult(new CollectionCreated(item.Id, item.CollectionNo));
        }

        public Task UpdateQrAsync(Guid customerId, Collection c, Guid actor)
        {
            var i = Items.Single(x => x.Id == c.Id);
            if (!PaymentCollectionStatus.IsOpen(i.Status)) return Task.CompletedTask;
            (i.QrType, i.QrPayload, i.QrAccount, i.BillId, i.ExpiresAt, i.LastError) = (c.QrType, c.QrPayload, c.QrAccount, c.BillId, c.ExpiresAt, c.LastError);
            return Task.CompletedTask;
        }

        public Task<bool> CancelAsync(Guid customerId, Guid id, Guid actor)
        {
            var n = Items.FindIndex(x => x.Id == id);
            if (n < 0 || !PaymentCollectionStatus.IsOpen(Items[n].Status)) return Task.FromResult(false);
            var old = Items[n];
            Items[n] = old with { Status = PaymentCollectionStatus.Cancelled };
            (Items[n].QrType, Items[n].QrPayload, Items[n].QrAccount, Items[n].BillId, Items[n].ExpiresAt, Items[n].Token) = (old.QrType, old.QrPayload, old.QrAccount, old.BillId, old.ExpiresAt, old.Token);
            return Task.FromResult(true);
        }

        public Task<string?> TokenAsync(Guid customerId, Guid id)
        {
            var i = Items.Single(x => x.Id == id);
            i.Token ??= SqlOrderPageStore.NewToken();
            return Task.FromResult<string?>(i.Token);
        }

        public Task<PayPageCollection?> FindAsync(string token) =>
            Task.FromResult(Items.Where(i => i.Token == token).Select(i => new PayPageCollection(A, i.Id, "Công ty A", null)).FirstOrDefault());
    }

    private sealed class CollectionLedger : ITingeePaymentLedger
    {
        public sealed class Row(string body) { public string Body = body; public string Status = TingeeInboxStatus.Received; }
        public SortedDictionary<long, Row> Rows { get; } = [];
        public List<TingeeCollectionCandidate> Collections { get; } = [];
        public List<TingeeCollectionOrder> Orders { get; } = [];
        public List<TingeeCollectionApplication> Applied { get; } = [];
        public void Add(long id, string body) => Rows[id] = new Row(body);

        public Task<IReadOnlyList<TingeePendingPayment>> PendingPaymentsAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TingeePendingPayment>>(Rows.Where(r => r.Value.Status == TingeeInboxStatus.Received).Select(r => new TingeePendingPayment(r.Key, r.Value.Body, 0, DateTime.UtcNow)).ToList());
        public Task<IReadOnlyList<TingeePaymentCandidate>> CandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<TingeePaymentCandidate>>([]);
        public Task<IReadOnlyList<TingeeCollectionCandidate>> CollectionCandidatesAsync(TingeePaymentNotice notice, DateTime receivedAtUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<TingeeCollectionCandidate>>(Collections);
        public Task<IReadOnlyList<TingeeCollectionOrder>> CollectionOrdersAsync(Guid customerId, Guid collectionId, CancellationToken ct) => Task.FromResult<IReadOnlyList<TingeeCollectionOrder>>(Orders);
        public Task<bool> ApplyCollectionAsync(TingeeCollectionApplication p, CancellationToken ct) { Rows[p.InboxId].Status = TingeeInboxStatus.Processed; Applied.Add(p); return Task.FromResult(true); }
        public Task<bool> ApplyAsync(TingeePaymentApplication payment, CancellationToken ct) => Task.FromResult(false);
        public Task RecordAsync(long inboxId, TingeePaymentNotice? notice, DateTime receivedAtUtc, string inboxStatus, string txStatus, string reason, CancellationToken ct) { Rows[inboxId].Status = inboxStatus; return Task.CompletedTask; }
        public Task RetryLaterAsync(long inboxId, string note, CancellationToken ct) => Task.CompletedTask;
        public Task FailAsync(long inboxId, string note, CancellationToken ct) { Rows[inboxId].Status = TingeeInboxStatus.Failed; return Task.CompletedTask; }
    }
}
