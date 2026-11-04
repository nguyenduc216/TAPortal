using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// A normal Merchant calls Tingee with its own key and, when configured, its own Merchant ID (never a customer's);
// a Master Merchant sends the child merchantId. No network, no SQL.
public sealed class SafeTingeeDirectMerchantTests
{
    private const long Merchant = 4242;

    [Theory]
    [InlineData("Direct", false)]
    [InlineData("direct", false)]
    [InlineData("Master", true)]
    public async Task MerchantIdIsSentOnlyInMasterMode(string mode, bool expected)
    {
        var handler = new Recorder();
        var api = Api(mode, handler);
        await api.CreateVaAsync(new TingeeCreateVaRequest { MerchantId = Merchant, BankBin = "970422" });
        await api.ConfirmVaAsync(Merchant, "970422", "c1", "123456");
        await api.DeleteVaAsync(Merchant, "970422", "VA1");
        await api.ConfirmDeleteVaAsync(Merchant, "970422", "c1", "123456");
        await api.GetVaPageAsync(Merchant);
        await api.GenerateDynamicQrAsync(new TingeeDynamicQrRequest { MerchantId = Merchant, VaAccountNumber = "VA1", BankBin = "970422", Amount = 1000 });
        await api.GetDynamicQrStatusAsync(Merchant, "QR1", "B1");
        await api.DeleteDynamicQrAsync(Merchant, "QR1", "B1");

        Assert.Equal(8, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(expected, request.Contains("merchantId", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DirectModeSendsTheOwnMerchantIdFromConfiguration()
    {
        var handler = new Recorder();
        var api = Api("Direct", handler, ownMerchantId: 777);
        await api.CreateVaAsync(new TingeeCreateVaRequest { MerchantId = Merchant, BankBin = "970422" });
        await api.DeleteVaAsync(Merchant, "970422", "VA1");
        await api.GenerateDynamicQrAsync(new TingeeDynamicQrRequest { MerchantId = Merchant, VaAccountNumber = "VA1", BankBin = "970422", Amount = 1000 });

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Matches(@"merchantId\W{1,3}777\b", request));
        Assert.All(handler.Requests, request => Assert.DoesNotContain("4242", request));
    }

    [Fact]
    public async Task VietnameseTextIsSentUnescapedSoTingeeSignaturesMatch()
    {
        var handler = new Recorder();
        // The recorder answers with no merchantId, so CreateMerchantAsync throws after the request was sent.
        await Assert.ThrowsAsync<TingeeApiException>(() => Api("Master", handler).CreateMerchantAsync(new TingeeCreateMerchantRequest("Tư vấn giải pháp T.A", "0898386060", "a+b@ta.example")));
        var body = Assert.Single(handler.Requests);
        Assert.Contains("\"name\":\"Tư vấn giải pháp T.A\"", body);
        Assert.DoesNotContain("\\u", body);
        Assert.Contains("a+b@ta.example", body);
    }

    [Fact]
    public async Task ErrorBodiesKeepTingeeDetailsEvenWhenDataIsAList()
    {
        var handler = new Fixed("{\"code\":\"400\",\"message\":\"Data input invalid\",\"data\":[{\"field\":\"taxCode\",\"error\":\"required\"}]}");
        var ex = await Assert.ThrowsAsync<TingeeApiException>(() => Api("Master", handler).CreateVaAsync(new TingeeCreateVaRequest { MerchantId = 1, BankBin = "970422" }));
        Assert.Equal(("400", "Data input invalid"), (ex.Code, ex.TingeeMessage));
        Assert.Contains("taxCode", ex.Details);
    }

    [Fact]
    public async Task EveryCallIsLoggedAsSentAndFailuresCarryTheLogCode()
    {
        var log = new Log();
        var handler = new Fixed("{\"code\":\"400\",\"message\":\"Verify string error\",\"data\":null}");
        var ex = await Assert.ThrowsAsync<TingeeApiException>(() => Api("Master", handler, log: log).CreateVaAsync(new TingeeCreateVaRequest { MerchantId = 5, BankBin = "970415", AccountName = "NGUYEN VAN A" }));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(ex.LogCode, entry.LogCode);
        Assert.Matches("^TG[0-9]{12}[0-9A-F]{6}$", entry.LogCode);
        Assert.Equal(entry.LogCode, TingeeLogCode.Of(ex));
        Assert.Equal(("POST", "https://open-api.tingee.vn/v1/create-va", "id", "400", (int?)200), (entry.Method, entry.Url, entry.ClientId, entry.ResponseCode, entry.HttpStatus));
        Assert.Contains("\"accountName\":\"NGUYEN VAN A\"", entry.RequestBody);
        Assert.Equal(TingeeSignature.Sign(entry.RequestTimestamp!, entry.RequestBody, "secret"), entry.Signature);
        Assert.Contains("Verify string error", entry.ResponseBody);
        Assert.DoesNotContain("secret", entry.RequestBody + entry.ResponseBody);
    }

    [Fact]
    public async Task NetworkFailuresAreLoggedWithTheirLogCode()
    {
        var log = new Log();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Api("Master", new Broken(), log: log).GetVaPageAsync(5));
        var entry = Assert.Single(log.Entries);
        Assert.Equal(entry.LogCode, TingeeLogCode.Of(ex));
        Assert.Contains("HttpRequestException", entry.Error);
        Assert.Null(entry.ResponseCode);
    }

    [Theory]
    [InlineData(" tg261104101500abcdef ", "TG261104101500ABCDEF")]
    [InlineData("x'--", null)]
    [InlineData("", null)]
    public void LogCodesAreNormalized(string input, string? expected) => Assert.Equal(expected, TingeeLogCode.Normalize(input));

    [Fact]
    public void ModeDefaultsToMaster()
    {
        Assert.False(new TingeeOptions().IsDirectMerchant);
        Assert.False(Api("Master", new Recorder()).IsDirectMerchant);
        Assert.True(Api("Direct", new Recorder()).IsDirectMerchant);
    }

    private static TingeeApi Api(string mode, HttpMessageHandler handler, long? ownMerchantId = null, ITingeeCallLog? log = null)
    {
        var options = Options.Create(new TingeeOptions { ClientId = "id", SecretKey = "secret", Mode = mode, MerchantId = ownMerchantId, BaseUrl = TingeeOptions.ProductionBaseUrl });
        return new TingeeApi(new TingeeClient(new HttpClient(handler), options, TimeProvider.System, log), options);
    }

    private sealed class Log : ITingeeCallLog
    {
        public List<TingeeCallLogEntry> Entries { get; } = [];
        public Task WriteAsync(TingeeCallLogEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<TingeeCallLogEntry?> FindAsync(string logCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TingeeCallLogEntry>> RecentAsync(int take, string? path, bool failedOnly, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Broken : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class Fixed(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(request.RequestUri!.Query + " " + body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":\"00\",\"data\":{}}", Encoding.UTF8, "application/json") };
        }
    }
}
