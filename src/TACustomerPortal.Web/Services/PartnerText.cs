using System.Text.RegularExpressions;

namespace TACustomerPortal.Web.Services;

/// <summary>
/// Customer Portal rule: customers never see the names of T.A's integration partners (Tingee, its operator HENO, their
/// domains). Fixed texts are written neutrally; text that comes from a partner (error messages, bank notes from the shared
/// catalog) goes through <see cref="Scrub"/> before it is shown. SafeCustomerPartnerNeutralityTests enforces both.
/// </summary>
public static partial class PartnerText
{
    /// <summary>Partner names that must not appear in anything a customer sees (whole words, any case).</summary>
    public static readonly IReadOnlyList<string> Names = ["Tingee", "HENO"];

    public const string Neutral = "hệ thống thanh toán";

    [GeneratedRegex(@"(?i)\b(?:[a-z0-9-]+\.)*tingee\.(?:vn|com|io)\S*|\b(?:tingee|heno)\b")]
    private static partial Regex PartnerPattern();

    public static string Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : PartnerPattern().Replace(text, Neutral);

    public static bool Mentions(string? text) => !string.IsNullOrEmpty(text) && PartnerPattern().IsMatch(text);
}
