using System.Globalization;
using System.Text;

namespace TAPortal.Web.Services;

/// <summary>
/// Suggests a customer code from the customer name, in the style already used (ADHOME, AGRISEA, BYNHORIZON): the brand part
/// of the name without diacritics, upper case, no spaces. Legal-form and generic trade words, a trailing province and the
/// owner after " - " (household businesses) are dropped. Staff can always overwrite the suggestion.
/// </summary>
public static class CustomerCodeSuggest
{
    public const int MaxLength = 20;

    // Matched as whole words at the start, repeatedly (longest first).
    private static readonly string[] Prefixes =
    [
        "HO KINH DOANH CA THE", "HO KINH DOANH", "DOANH NGHIEP TU NHAN", "DNTN", "VAN PHONG DAI DIEN", "CHI NHANH", "TAP DOAN",
        "CONG TY", "CTY", "TRACH NHIEM HUU HAN", "TNHH", "CO PHAN", "CP", "MOT THANH VIEN", "HAI THANH VIEN TRO LEN", "MTV", "HTX", "HOP TAC XA",
        "DAU TU", "XAY DUNG", "THUONG MAI", "DICH VU", "TMDV", "TM", "DV", "SAN XUAT", "SX", "XUAT NHAP KHAU", "XNK", "KINH DOANH",
        "DU LICH", "CONG NGHE", "PHAT TRIEN", "TU VAN", "VA", "NHA HANG", "QUAN", "CUA HANG",
    ];

    // Dropped at the end, repeatedly.
    private static readonly string[] Suffixes =
    [
        "VIET NAM", "VN", "NHA TRANG", "KHANH HOA", "HA NOI", "HO CHI MINH", "TP HCM", "HCM", "SAI GON", "DA NANG", "CAM RANH", "MIEN TRUNG",
    ];

    public static string FromName(string? name)
    {
        var text = Plain(name);
        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0) text = text[..dash];
        text = text.Replace('-', ' ');
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new string(w.Where(char.IsAsciiLetterOrDigit).ToArray())).Where(w => w.Length > 0).ToList();
        var all = words.ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var p in Prefixes)
                if (StartsWith(words, p)) { words.RemoveRange(0, p.Split(' ').Length); changed = true; break; }
        }
        for (var changed = true; changed && words.Count > 1;)
        {
            changed = false;
            foreach (var s in Suffixes)
            {
                var n = s.Split(' ').Length;
                if (words.Count > n && string.Join(' ', words.Skip(words.Count - n)) == s) { words.RemoveRange(words.Count - n, n); changed = true; break; }
            }
        }
        if (words.Count == 0) words = all;                    // the name was only generic words: keep them
        var code = new StringBuilder();
        foreach (var w in words.Take(3))
        {
            if (code.Length + w.Length > MaxLength) break;
            code.Append(w);
        }
        if (code.Length == 0 && words.Count > 0) code.Append(words[0].AsSpan(0, Math.Min(MaxLength, words[0].Length)));
        return code.ToString();
    }

    /// <summary>The base code, or BASE2, BASE3… when the base is already used.</summary>
    public static string Unique(string code, IEnumerable<string> taken)
    {
        if (code.Length == 0) return code;
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(code)) return code;
        for (var i = 2; ; i++)
        {
            var tail = i.ToString(CultureInfo.InvariantCulture);
            var next = code[..Math.Min(code.Length, MaxLength - tail.Length)] + tail;
            if (!used.Contains(next)) return next;
        }
    }

    private static bool StartsWith(List<string> words, string phrase)
    {
        var parts = phrase.Split(' ');
        if (words.Count <= parts.Length) return false;     // never strip the whole name
        for (var i = 0; i < parts.Length; i++) if (words[i] != parts[i]) return false;
        return true;
    }

    private static string Plain(string? value)
    {
        var s = (value ?? "").Replace('đ', 'd').Replace('Đ', 'D').Replace('&', ' ').Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(s.Length);
        foreach (var c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c);
        return b.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
