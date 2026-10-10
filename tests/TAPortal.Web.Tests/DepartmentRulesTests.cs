using TAPortal.Web.Models;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class DepartmentRulesTests
{
    [Theory]
    [InlineData(" kt ", "KT")]
    [InlineData("Tech", "TECH")]
    public void NormalizeCode_TrimsAndUppercases(string input, string expected) =>
        Assert.Equal(expected, DepartmentRules.NormalizeCode(input));

    [Fact]
    public void RowVersion_RoundTripsAsEightByteBase64()
    {
        var expected = new byte[] { 0, 0, 0, 0, 0, 0, 0, 9 };
        Assert.True(DepartmentRules.TryDecodeRowVersion(Convert.ToBase64String(expected), out var actual));
        Assert.Equal(expected, actual);
        Assert.False(DepartmentRules.TryDecodeRowVersion("not-base64", out _));
    }
}
