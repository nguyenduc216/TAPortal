extern alias webhooks;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Tingee signing (HMAC-SHA512 over "{timestamp}:{body}") and the TAPortal.Webhooks receivers.
// Expected signatures were computed independently with Python hmac. No network, no SQL.
public sealed class SafeTingeeIntegrationTests
{
    private const string Secret = "test-secret";
    private const string Timestamp = "20261030103000123";
    private const string Body = "{\"transactionCode\":\"FT1\",\"amount\":500000}";
    private const string BodySignature = "7d6d8eb81044c9505a395e890b52d3ffdcc5f5ae07d96e8e35fdcc82d3a85c1f27129e507d48a29b1996be13b5d3fff0b55dbf25c8127acc3f7034f513d9390e";
    private const string EmptySignature = "a2f5e0847cd4cdf92fc1723f553cc5a71b1800947b47509e0e85742bfc4d2792a1b58ffa1702d22d37803cd16caa0d73efce4893b3749a732830e39c7d320a99";
    private static readonly DateTimeOffset SentAt = new(2026, 10, 30, 3, 30, 0, 123, TimeSpan.Zero); // 10:30:00.123 UTC+7

    [Fact]
    public void SignatureMatchesAnIndependentHmacSha512()
    {
        Assert.Equal(BodySignature, TingeeSignature.Sign(Timestamp, Body, Secret));
        Assert.Equal(EmptySignature, TingeeSignature.Sign(Timestamp, "", Secret));   // empty body is signed as "{}"
        Assert.Equal(EmptySignature, TingeeSignature.Sign(Timestamp, null, Secret));
    }

    [Fact]
    public void VerifyRejectsTamperedBodiesWrongKeysAndMissingValues()
    {
        Assert.True(TingeeSignature.Verify(Timestamp, Body, BodySignature.ToUpperInvariant(), Secret));
        Assert.False(TingeeSignature.Verify(Timestamp, Body.Replace("500000", "900000"), BodySignature, Secret));
        Assert.False(TingeeSignature.Verify(Timestamp, Body, BodySignature, "other-secret"));
        Assert.False(TingeeSignature.Verify("20261030103000124", Body, BodySignature, Secret));
        Assert.False(TingeeSignature.Verify(Timestamp, Body, "", Secret));
        Assert.False(TingeeSignature.Verify(null, Body, BodySignature, Secret));
    }

    [Fact]
    public void TimestampUsesVietnamTimeAndRoundTrips()
    {
        Assert.Equal(Timestamp, TingeeSignature.Timestamp(SentAt));
        Assert.True(TingeeSignature.TryParseTimestamp(Timestamp, out var parsed));
        Assert.Equal(SentAt, parsed);
        Assert.False(TingeeSignature.TryParseTimestamp("2026-10-30", out _));
        Assert.True(TingeeSignature.IsFresh(parsed, SentAt.AddMinutes(9), TimeSpan.FromMinutes(10)));
        Assert.False(TingeeSignature.IsFresh(parsed, SentAt.AddMinutes(11), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task ValidWebhookIsStoredVerbatimBeforeTheSuccessAcknowledgement()
    {
        var inbox = new MemoryInbox();
        using var f = Factory(inbox);
        var response = await Post(f, "/hooks/tingee/payment", Body, Timestamp, BodySignature);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"code\":\"00\",\"message\":\"Success\"}", await response.Content.ReadAsStringAsync());
        var saved = Assert.Single(inbox.Saved);
        Assert.Equal(TingeeWebhookKind.Payment, saved.Kind);
        Assert.Equal(Body, saved.RawBody);
        Assert.Equal("req-1", saved.RequestId);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("stale")]
    [InlineData("missing")]
    public async Task InvalidOrStaleSignaturesAreRejectedAndNotStored(string mode)
    {
        var inbox = new MemoryInbox();
        using var f = Factory(inbox);
        var (body, timestamp, signature) = mode switch
        {
            "tampered" => (Body.Replace("500000", "1"), Timestamp, BodySignature),
            "stale" => (Body, "20261030100000123", TingeeSignature.Sign("20261030100000123", Body, Secret)),
            _ => (Body, Timestamp, "")
        };
        var response = await Post(f, "/hooks/tingee/va-status", body, timestamp, signature);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(inbox.Saved);
    }

    [Fact]
    public async Task UnconfiguredOrFailingInboxNeverAcknowledgesSoTingeeRetries()
    {
        using (var f = Factory(new MemoryInbox { Fail = true }))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(f, "/hooks/tingee/payment", Body, Timestamp, BodySignature)).StatusCode);
        using (var f = Factory(inbox: null))   // default UnavailableTingeeWebhookInbox
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(f, "/hooks/tingee/bank-link", Body, Timestamp, BodySignature)).StatusCode);
        using (var f = Factory(new MemoryInbox(), secret: ""))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(f, "/hooks/tingee/payment", Body, Timestamp, BodySignature)).StatusCode);
    }

    [Fact]
    public async Task HealthEndpointAnswersWithoutSecrets()
    {
        using var f = Factory(new MemoryInbox());
        var response = await f.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> Post(WebApplicationFactory<webhooks::Program> f, string path, string body, string timestamp, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-request-id", "req-1");
        request.Headers.Add("x-request-timestamp", timestamp);
        if (signature.Length > 0) request.Headers.Add("x-signature", signature);
        return await f.CreateClient().SendAsync(request);
    }

    private static WebApplicationFactory<webhooks::Program> Factory(MemoryInbox? inbox, string secret = Secret) =>
        new WebApplicationFactory<webhooks::Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tingee:ClientId"] = "test-client",
                ["Tingee:SecretKey"] = secret
            }));
            b.ConfigureTestServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedClock(SentAt.AddSeconds(5))));
                if (inbox is not null) s.Replace(ServiceDescriptor.Singleton<ITingeeWebhookInbox>(inbox));
            });
        });

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryInbox : ITingeeWebhookInbox
    {
        public bool Fail { get; init; }
        public List<TingeeWebhookEnvelope> Saved { get; } = [];
        public bool IsAvailable => true;
        public Task SaveAsync(TingeeWebhookEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("inbox down");
            Saved.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
