using System.Text.Json;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Tests;

// Operator 2026-11-13: MB dynamic QRs created for 4 days were gone at the bank (1076) within hours while the page still
// showed days left; a dead QR is replaced before it is shown, and a shortened lifetime is reported when the QR is made.
public sealed class SafeQrLivenessTests
{
    [Fact]
    public void ADeadQrIsOnlyOneThePartnerSaysIsGoneOrExpired()
    {
        Assert.True(QrLiveness.IsDead("1076", null));
        Assert.True(QrLiveness.IsDead(null, "Expired"));
        Assert.False(QrLiveness.IsDead(null, "fully-paid"));
        Assert.False(QrLiveness.IsDead(null, null));
        Assert.False(QrLiveness.IsDead("90", null));
    }

    [Fact]
    public void AShortenedLifetimeIsReportedOnlyWhenClearlyBelowTheRequest()
    {
        Assert.Equal(1440, QrLiveness.Shortened(5760, 1440));
        Assert.Null(QrLiveness.Shortened(5760, 5759));
        Assert.Null(QrLiveness.Shortened(1440, null));
        Assert.Null(QrLiveness.Shortened(1440, 0));
        Assert.Equal("1 ngày 0 giờ", QrLiveness.Lifetime(1440));
        Assert.Equal("2 giờ 5 phút", QrLiveness.Lifetime(125));
    }

    [Fact]
    public void TheReplacementKeepsTheExpiryTheCustomerWasGiven()
    {
        var now = new DateTime(2026, 11, 13, 5, 0, 0, DateTimeKind.Utc);
        Assert.Equal(3435, QrLiveness.MinutesLeft(new DateTime(2026, 11, 15, 14, 15, 0, DateTimeKind.Utc), now));
        Assert.Equal(1, QrLiveness.MinutesLeft(now.AddSeconds(10), now));
    }

    [Fact]
    public void TheStatusReadsTheMinutesLeft()
    {
        var status = JsonSerializer.Deserialize<TingeeQrStatus>("""{"billInfo":{"billId":"QRD1","amount":"54000","status":"fully-paid","expireInMinute":1405}}""",
            new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString })!;
        Assert.Equal(1405, status.BillInfo!.ExpireInMinute);
    }
}
