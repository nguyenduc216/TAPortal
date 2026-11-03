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
    public void ModeDefaultsToMaster()
    {
        Assert.False(new TingeeOptions().IsDirectMerchant);
        Assert.False(Api("Master", new Recorder()).IsDirectMerchant);
        Assert.True(Api("Direct", new Recorder()).IsDirectMerchant);
    }

    private static TingeeApi Api(string mode, HttpMessageHandler handler, long? ownMerchantId = null)
    {
        var options = Options.Create(new TingeeOptions { ClientId = "id", SecretKey = "secret", Mode = mode, MerchantId = ownMerchantId });
        return new TingeeApi(new TingeeClient(new HttpClient(handler), options, TimeProvider.System), options);
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
