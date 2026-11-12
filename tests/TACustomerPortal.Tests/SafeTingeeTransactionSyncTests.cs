using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Operator 2026-11-12: a payment whose webhook never arrived is fetched from the partner's transaction history and queued
// in the webhook inbox as if the webhook had come, so it is matched the same way and a late webhook is a duplicate.
public sealed class SafeTingeeTransactionSyncTests
{
    private static TingeeTransactionItem Item(string type = "credit") => new()
    {
        TransactionId = "3f1c2a9e-8b4d-4e57-9a61-2c7d5b0e8f14", BankBin = "970415", Code = "580T26B0BHCNTEST", Amount = 5_400_000, Type = type,
        Content = "TT DH261112-0001", AccountNumber = "100000000001", VaAccountNumber = "100000000001", TransactionDate = "20261112093000",
    };

    [Fact]
    public void QueuedBodyReadsLikeTheWebhookAndHasTheSameDedupKey()
    {
        var item = Item();
        var code = TingeeTransactionSync.Code(item)!;
        var body = TingeeTransactionSync.Body(item, code);
        var notice = TingeePaymentNotice.Parse(body);
        Assert.NotNull(notice);
        Assert.Equal("580T26B0BHCNTEST", notice!.TransactionCode);
        Assert.Equal(5_400_000m, notice.Amount);
        Assert.True(notice.IsCredit);
        Assert.Equal("100000000001", notice.VaAccountNumber);
        Assert.Equal("CTG", notice.Bank);
        Assert.Equal(new DateTime(2026, 11, 12, 2, 30, 0, DateTimeKind.Utc), notice.TransactionAtUtc);
        Assert.Equal("TA_SYNC", JsonDocument.Parse(body).RootElement.GetProperty("source").GetString());

        // A webhook for the same transaction would get exactly this inbox key, so whichever comes second is ignored.
        var webhook = new TingeeWebhookEnvelope(TingeeWebhookKind.Payment, "rq-1", "20261112093001000",
            "{\"transactionCode\":\"580T26B0BHCNTEST\",\"amount\":5400000,\"type\":\"credit\"}", DateTimeOffset.UtcNow);
        Assert.Equal(SqlTingeeWebhookInbox.DedupKey(webhook), TingeeTransactionSync.DedupKey(code));

        Assert.False(TingeePaymentNotice.Parse(TingeeTransactionSync.Body(Item("debit"), code))!.IsCredit);
        Assert.Equal("3f1c2a9e-8b4d-4e57-9a61-2c7d5b0e8f14", TingeeTransactionSync.Code(Item() with { Code = " " }));
    }

    [Fact]
    public async Task HistoryCallPostsTheDocumentedBodyAndReadsTheItems()
    {
        var handler = new Recorder("""{"code":"00","message":"Success","data":{"totalCount":1,"items":[{"transactionId":"t-1","bankBin":"970415","code":"FT1","amount":5400000,"type":"credit","content":"x","accountNumber":"1","vaAccountNumber":"1","transactionDate":"20261112093000","billId":null}]}}""");
        var options = Options.Create(new TingeeOptions { ClientId = "id", SecretKey = "secret", MerchantId = 99, BaseUrl = TingeeOptions.ProductionBaseUrl });
        var api = new TingeeApi(new TingeeClient(new HttpClient(handler), options, TimeProvider.System), options);
        var page = await api.GetTransactionsAsync(new TingeeTransactionQuery { MerchantId = 42, VaAccountNumbers = ["1"], StartTime = "20261110000000", EndTime = "20261112235959" });

        Assert.EndsWith("/v1/transaction/get-paging", handler.Path);
        using var sent = JsonDocument.Parse(handler.Body);
        Assert.Equal("20261110000000", sent.RootElement.GetProperty("startTime").GetString());
        Assert.Equal("20261112235959", sent.RootElement.GetProperty("endTime").GetString());
        Assert.Equal(50, sent.RootElement.GetProperty("maxResultCount").GetInt32());
        Assert.Equal(42, sent.RootElement.GetProperty("merchantId").GetInt64());
        Assert.Equal("1", sent.RootElement.GetProperty("vaAccountNumbers")[0].GetString());
        var tx = Assert.Single(page.Items);
        Assert.Equal(("FT1", 5_400_000m, "credit"), (tx.Code, tx.Amount, tx.Type));
    }

    // Operator 2026-11-12: money paid straight into a linked root account (MB notifies only its VA) is kept too.
    [Fact]
    public async Task RootAccountPaymentsBelongToTheSyncAndMerchantWideQueriesSendNoVaFilter()
    {
        var accounts = TingeeTransactionSync.Accounts([new(Guid.NewGuid(), 42, "VQRQTEST0001", "200000000008"), new(Guid.NewGuid(), 42, "100000000001", null)]);
        Assert.Equal(3, accounts.Count);
        Assert.True(TingeeTransactionSync.Belongs(Item() with { VaAccountNumber = null, AccountNumber = "200000000008" }, accounts));
        Assert.True(TingeeTransactionSync.Belongs(Item() with { VaAccountNumber = "VQRQTEST0001", AccountNumber = "200000000008" }, accounts));
        Assert.False(TingeeTransactionSync.Belongs(Item() with { VaAccountNumber = null, AccountNumber = "999999999999" }, accounts));

        var handler = new Recorder("""{"code":"00","message":"Success","data":{"totalCount":0,"items":[]}}""");
        var options = Options.Create(new TingeeOptions { ClientId = "id", SecretKey = "secret", MerchantId = 99, BaseUrl = TingeeOptions.ProductionBaseUrl });
        var api = new TingeeApi(new TingeeClient(new HttpClient(handler), options, TimeProvider.System), options);
        await api.GetTransactionsAsync(new TingeeTransactionQuery { MerchantId = 42, StartTime = "20261110000000", EndTime = "20261112235959" });
        using var sent = JsonDocument.Parse(handler.Body);
        Assert.False(sent.RootElement.TryGetProperty("vaAccountNumbers", out _));
        Assert.Equal(42, sent.RootElement.GetProperty("merchantId").GetInt64());
    }

    [Fact]
    public async Task SyncRefusesBadPeriodsWithoutCallingThePartner()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=none" }).Build();
        var sync = new TingeeTransactionSync(new Unused(), config, TimeProvider.System, NullLogger<TingeeTransactionSync>.Instance);
        var now = DateTime.UtcNow;
        Assert.Contains("tối đa 31 ngày", (await sync.SyncAsync(Guid.NewGuid(), now.AddDays(-40), now)).Error);
        Assert.Equal("Khoảng thời gian không hợp lệ.", (await sync.SyncAsync(Guid.NewGuid(), now, now.AddDays(-1))).Error);
    }

    [Fact]
    public void MessageTellsStaffWhatWasFetched()
    {
        var day = new DateOnly(2026, 11, 12);
        Assert.Equal("Đồng bộ ngày 12/11/2026: 3 giao dịch, hệ thống đã nhận đủ, không thiếu giao dịch nào.",
            TingeeTransactionSync.Message(new(1, 3, 0, 0, 0, 0, 0, 0), day, day));
        var added = TingeeTransactionSync.Message(new(1, 3, 2, 1, 5_400_000, 1, 200_000, 0), day.AddDays(-2), day);
        Assert.Contains("10/11/2026 – 12/11/2026", added);
        Assert.Contains("đã lấy về 2 giao dịch chưa được báo về hệ thống", added);
        Assert.Contains("1 tiền vào 5.400.000 ₫", added);
        Assert.Contains("1 tiền ra 200.000 ₫", added);
        Assert.Contains("Mã tra cứu: L-1.", TingeeTransactionSync.Message(TingeeSyncResult.Failed("Không lấy được.", "L-1"), day, day));
        Assert.DoesNotContain("Tingee", added + TingeeTransactionSync.Message(TingeeSyncResult.Failed("Không kết nối được dịch vụ thanh toán."), day, day));
    }

    private sealed class Unused : ITingeeApi
    {
        public bool IsConfigured => true;
        public Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeePage<TingeeMerchantItem>> GetMerchantsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> CreateBankLinkSessionAsync(TingeeBankLinkSessionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TingeePage<TingeeTransactionItem>> GetTransactionsAsync(TingeeTransactionQuery query, CancellationToken ct = default) => throw new InvalidOperationException("must not be called");
    }

    private sealed class Recorder(string response) : HttpMessageHandler
    {
        public string Path { get; private set; } = "";
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
