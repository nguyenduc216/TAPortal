using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TAPortal.Shared;

/// <summary>
/// Operator 2026-11-13 (mandatory rule): a username has no diacritics (ASCII only) and no spaces; only letters, digits,
/// '.' and '_' are allowed. Applies to every account created in the Admin portal and Hóa đơn tự động.
/// </summary>
public static partial class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 100;
    public const string Error = "Tên đăng nhập chỉ gồm chữ không dấu, chữ số, dấu chấm (.) và gạch dưới (_), không có khoảng trắng, từ 3 đến 100 ký tự.";

    [GeneratedRegex("^[A-Za-z0-9._]+$")]
    private static partial Regex Allowed();

    public static bool IsValid(string? username) =>
        username is { Length: >= MinLength and <= MaxLength } && Allowed().IsMatch(username);

    /// <summary>"Lê Đông" → "ledong": lower case, diacritics removed (đ → d), only allowed characters kept.</summary>
    public static string Clean(string? text)
    {
        var decomposed = (text ?? "").Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var l = char.ToLowerInvariant(c);
            if (l is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_') sb.Append(l);
        }
        var s = sb.ToString().Trim('.');
        return s.Length > MaxLength - 4 ? s[..(MaxLength - 4)] : s;
    }

    /// <summary>
    /// Suggested staff username (operator 2026-11-13): given name + initials of the family and middle names,
    /// e.g. "Lê Văn An" → "anlv", "Phạm Thị Bích Ngọc" → "ngocptb".
    /// </summary>
    public static string FromFullName(string? fullName)
    {
        var parts = (fullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Clean).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "";
        var s = parts[^1] + string.Concat(parts.Take(parts.Count - 1).Select(p => p[0]));
        return s.Length >= MinLength ? s : s + new string('_', MinLength - s.Length);
    }

    /// <summary>A valid username built from the part of an email before '@' (padded when too short).</summary>
    public static string FromEmail(string? email)
    {
        var local = (email ?? "").Split('@')[0];
        var s = Clean(local);
        return s.Length >= MinLength ? s : s + "user";
    }
}
