using System.Text.RegularExpressions;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Rule (operator 2026-11-04): nothing a customer sees in the Customer Portal names T.A's integration partners (Tingee, HENO).
// Source scan of every view and every string literal of the Customer Portal, plus the scrubber for partner-supplied text.
// Rendered pages are checked in SafeCustomerPaymentsTests.PaymentPagesNeverNamePartners.
public sealed class SafeCustomerPartnerNeutralityTests
{
    private static readonly Regex Partner = new(@"(?i)\b(?:tingee|heno)\b");
    private static readonly Regex Literal = new(@"\$?@?""(?:[^""\\]|\\.)*""");

    [Fact]
    public void CustomerViewsNeverNameAPartner()
    {
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Web(), "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var text = Regex.Replace(File.ReadAllText(file), @"@\*.*?\*@", "", RegexOptions.Singleline);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                if (!lines[i].TrimStart().StartsWith("@using", StringComparison.Ordinal) && Partner.IsMatch(lines[i]))
                    found.Add($"{Path.GetRelativePath(Web(), file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(found.Count == 0, "Partner name in a customer view:\n" + string.Join('\n', found));
    }

    [Fact]
    public void CustomerMessagesNeverNameAPartner()
    {
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Web(), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Web(), file);
            if (rel.StartsWith("bin", StringComparison.Ordinal) || rel.StartsWith("obj", StringComparison.Ordinal) || rel.EndsWith("PartnerText.cs", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var code = line.Contains("//", StringComparison.Ordinal) ? line[..line.IndexOf("//", StringComparison.Ordinal)] : line;
                if (Regex.IsMatch(code, @"\blogger\.|\bLog(?:Warning|Error|Information|Debug|Critical)\(")) continue;   // server logs only
                if (Literal.Matches(code).Any(m => Partner.IsMatch(m.Value))) found.Add($"{rel}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.True(found.Count == 0, "Partner name in a customer-visible string:\n" + string.Join('\n', found));
    }

    [Theory]
    [InlineData("Tingee error 1133: Không có quyền")]
    [InlineData("TINGEE từ chối, liên hệ HENO")]
    [InlineData("Xem https://open-api.tingee.vn/v1/create-va để biết thêm")]
    [InlineData("Liên hệ support@tingee.vn")]
    public void PartnerSuppliedTextIsScrubbed(string text)
    {
        var shown = PartnerText.Scrub(text);
        Assert.False(PartnerText.Mentions(shown), shown);
        Assert.DoesNotContain("tingee", shown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PartnerText.Neutral, shown);
    }

    [Fact]
    public void OrdinaryTextIsLeftAlone()
    {
        Assert.Equal("Số tài khoản 0123456789 không hợp lệ", PartnerText.Scrub("Số tài khoản 0123456789 không hợp lệ"));
        Assert.Equal(string.Empty, PartnerText.Scrub(null));
    }

    private static string Web()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) return Path.Combine(dir.FullName, "src", "TACustomerPortal.Web");
        throw new InvalidOperationException("TAPortal.sln not found");
    }
}
