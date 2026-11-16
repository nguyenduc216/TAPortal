using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TAPortal.Web.Services;

// LEGAL-WATCH (operator 2026-11-14, option A). Reading the two public sources, without AI:
//   Công báo (congbao.chinhphu.vn): RSS of newly issued documents (about 15 days, 50 entries) and the document page
//     (type, number, agency, issue/effect dates, summary, signed PDF and a Word file). robots.txt allows everything.
//   vbpl.vn (national law database, Ministry of Justice): sitemap (document URLs) and the document page, whose embedded
//     data carries the number, effect dates and effect status. robots.txt allows /van-ban/ and forbids /api/, so the
//     relation tables (loaded through /api/) are not used: relations are read from the summary instead.
// thuvienphapluat.vn (commercial, refuses automated access), gdt.gov.vn (no feed) and mof.gov.vn (script-only pages) are
// not read.

public sealed record LegalFeedEntry(string Link, string Summary, DateTime? Published);

public sealed record LegalWatchCandidate(string Source, string SourceUrl, string? FileUrl, string DocumentNo, string DocType, string? TypeName,
    string Summary, string? Agency, DateTime? IssuedDate, DateTime? EffectiveDate, string? Relations, string Keywords)
{
    // Library title: "Thông tư 166/2026/TT-BTC – Bãi bỏ ...", at most 300 characters (dbo.LegalDocuments.Title).
    public string LibraryTitle() => LegalWatchPolicy.Clip($"{TypeName ?? "Văn bản"} {DocumentNo} – {Summary}", 300);
}

public sealed record VbplStatus(string DocumentNo, string? EffectStatus, DateTime? EffectiveFrom, DateTime? ExpiredDate);

public sealed record LegalRelation(string Kind, string DocumentNo)
{
    public string Label => LegalWatchPolicy.RelationLabel(Kind);
}

public static class LegalWatchPolicy
{
    public const string UserAgent = "TAPortal-LegalWatch/1.0";
    public const string CongBaoFeed = "https://congbao.chinhphu.vn/cac-van-ban-moi-ban-hanh.rss";
    public const int VbplSitemapCount = 12;           // sitemap/1.xml … 12.xml: central documents (13+ are local)
    public static string VbplSitemap(int n) => $"https://vbpl.vn/sitemap/{n}.xml";

    // Hosts the watch may contact; any other URL (also after a redirect) is refused.
    public static readonly IReadOnlySet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "congbao.chinhphu.vn", "congbaocdn.chinhphu.vn", "g7.cdnchinhphu.vn", "vbpl.vn" };

    public static bool Allowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && Hosts.Contains(u.Host);

    // Tax subjects searched in the summary (lower case, NFC). A match puts the document in the review queue.
    public static readonly IReadOnlyList<string> Keywords =
    [
        "thuế", "hóa đơn", "hoá đơn", "chứng từ", "lệ phí", "thu phí", "hải quan", "kế toán",
        "thu nhập doanh nghiệp", "thu nhập cá nhân", "giá trị gia tăng", "tiêu thụ đặc biệt", "xuất khẩu, nhập khẩu"
    ];

    public static IReadOnlyList<string> MatchKeywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var t = text.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.GetCultureInfo("vi-VN"));
        return Keywords.Where(k => t.Contains(k, StringComparison.Ordinal)).ToList();
    }

    // "166 / 2026 / tt-btc" → "166/2026/TT-BTC" for comparisons (the stored number keeps the source's spelling).
    public static string Key(string? documentNo) =>
        Regex.Replace((documentNo ?? "").Normalize(NormalizationForm.FormC), @"\s+", "").ToUpperInvariant();

    // URL slug of a number, as vbpl.vn writes it: "123/2020/NĐ-CP" → "123-2020-nd-cp".
    public static string Slug(string documentNo)
    {
        var sb = new StringBuilder();
        foreach (var c in documentNo.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var ch = c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c);
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }
        return Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
    }

    public static string DocTypeOf(string? typeName)
    {
        var t = (typeName ?? "").Trim().ToLower(CultureInfo.GetCultureInfo("vi-VN"));
        return t switch
        {
            "bộ luật" => "CODE",
            "luật" => "LAW",
            "nghị định" => "DECREE",
            _ when t.StartsWith("thông tư", StringComparison.Ordinal) => "CIRCULAR",
            _ => "OTHER"
        };
    }

    // vbpl.vn slugs start with the type: a library document is looked up only among URLs of its own type.
    public static IReadOnlyList<string> SlugPrefixes(string docType) => docType switch
    {
        "CODE" => ["bo-luat-"],
        "LAW" => ["luat-"],
        "DECREE" => ["nghi-dinh-"],
        "CIRCULAR" => ["thong-tu-"],
        _ => [""]
    };

    // Document numbers named in a summary, with the verb in front of them: "Sửa đổi, bổ sung một số điều của Nghị định số
    // 123/2020/NĐ-CP" → AMENDS 123/2020/NĐ-CP. A number without a verb in front is RELATED.
    private static readonly Regex Number = new(@"\b\d{1,4}/(?:\d{4}/)?[A-ZĐ][A-ZĐa-zđ0-9]*(?:-[A-ZĐa-zđ0-9]+)*", RegexOptions.CultureInvariant);
    private static readonly (string Kind, Regex Verb)[] Verbs =
    [
        ("AMENDS", new(@"sửa đổi|bổ sung|đính chính", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("REPLACES", new(@"thay thế", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("REPEALS", new(@"bãi bỏ|hết hiệu lực|ngưng hiệu lực", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("GUIDES", new(@"hướng dẫn|quy định chi tiết|thi hành", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
    ];

    public static IReadOnlyList<LegalRelation> Relations(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return [];
        var text = summary.Normalize(NormalizationForm.FormC);
        var list = new List<LegalRelation>();
        foreach (Match m in Number.Matches(text))
        {
            var before = text[..m.Index];
            var kind = "RELATED";
            var at = -1;
            foreach (var (k, verb) in Verbs)
            {
                var last = verb.Matches(before).LastOrDefault();
                if (last is not null && last.Index > at) { at = last.Index; kind = k; }
            }
            var no = m.Value.TrimEnd('-');
            if (list.All(r => Key(r.DocumentNo) != Key(no))) list.Add(new(kind, no));
        }
        return list;
    }

    public static string RelationLabel(string kind) => kind switch
    {
        "AMENDS" => "Sửa đổi, bổ sung",
        "REPLACES" => "Thay thế",
        "REPEALS" => "Bãi bỏ",
        "GUIDES" => "Hướng dẫn",
        _ => "Liên quan"
    };

    // Stored as "AMENDS:123/2020/NĐ-CP;GUIDES:108/2025/QH15" (dbo.LegalWatchItems.Relations, at most 1000 characters).
    public static string? Join(IReadOnlyList<LegalRelation> relations) =>
        relations.Count == 0 ? null : Clip(string.Join(";", relations.Select(r => r.Kind + ":" + r.DocumentNo)), 1000);

    public static IReadOnlyList<LegalRelation> Split(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : stored.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(':', 2)).Where(p => p.Length == 2).Select(p => new LegalRelation(p[0], p[1])).ToList();

    // Library note of an imported document: source and relations.
    public static string Note(string sourceUrl, IReadOnlyList<LegalRelation> relations)
    {
        var parts = relations.Where(r => r.Kind != "RELATED").GroupBy(r => r.Kind).Select(g => $"{RelationLabel(g.Key)}: {string.Join(", ", g.Select(r => r.DocumentNo))}").ToList();
        parts.Add("Nguồn: " + sourceUrl);
        return Clip(string.Join(". ", parts), 1000);
    }

    // "Còn hiệu lực" (vbpl.vn) is the normal state; any other status of a library document is worth a person's look.
    public static bool IsNormal(string? status) => status is not null && status.Trim().Equals("Còn hiệu lực", StringComparison.OrdinalIgnoreCase);

    // Run periods (Vietnam time): ISO week for the weekly Công báo read, month for the vbpl check and the notice.
    public static string Week(DateOnly day) => $"{ISOWeek.GetYear(day.ToDateTime(TimeOnly.MinValue))}-W{ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue)):00}";
    public static string Month(DateOnly day) => $"{day.Year}-{day.Month:00}";

    public static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    internal static DateTime? Date(string? text) =>
        DateTime.TryParseExact((text ?? "").Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

public static class CongBaoParser
{
    public static IReadOnlyList<LegalFeedEntry> Feed(string xml)
    {
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml),
            new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
        var doc = XDocument.Load(reader);
        return doc.Descendants("item").Select(i => new LegalFeedEntry(
                ((string?)i.Element("link") ?? "").Trim(),
                Clean((string?)i.Element("description")),
                DateTime.TryParse((string?)i.Element("pubDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var p) ? p : null))
            .Where(e => LegalWatchPolicy.Allowed(e.Link) && e.Link.Contains("/van-ban/", StringComparison.Ordinal))
            .ToList();
    }

    // Worth opening: the summary names a tax subject, or the number (in the link) is a Ministry of Finance document.
    public static bool Worth(LegalFeedEntry e) => LegalWatchPolicy.MatchKeywords(e.Summary).Count > 0 || e.Link.Contains("-btc-", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex Row = new(@"<span class=""name"">\s*(?<name>[^<]+?)\s*</span>\s*<div class=""value"">(?<value>.*?)</div>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Docx = new(@"href=""(?<url>https://[^""]+?\.docx)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The document page. Null when it has no number (not a document page) or no summary.
    public static LegalWatchCandidate? Detail(string html, string url)
    {
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Row.Matches(html))
        {
            var name = Clean(m.Groups["name"].Value);
            if (name.Length > 0 && !rows.ContainsKey(name)) rows[name] = Clean(m.Groups["value"].Value);
        }
        var no = rows.GetValueOrDefault("Số, ký hiệu") ?? rows.GetValueOrDefault("Số ký hiệu");
        var summary = rows.GetValueOrDefault("Trích yếu");
        if (string.IsNullOrWhiteSpace(no) || string.IsNullOrWhiteSpace(summary)) return null;
        no = Regex.Replace(no, @"\s+", "");
        if (no.Length > 50) return null;
        var typeName = rows.GetValueOrDefault("Loại văn bản");
        var file = Docx.Match(html) is { Success: true } d ? WebUtility.HtmlDecode(d.Groups["url"].Value) : null;
        var keywords = LegalWatchPolicy.MatchKeywords(summary);
        return new("CONGBAO", url, LegalWatchPolicy.Allowed(file) ? file : null, no, LegalWatchPolicy.DocTypeOf(typeName), Blank(typeName),
            LegalWatchPolicy.Clip(summary, 1000), Blank(Title(rows.GetValueOrDefault("Cơ quan ban hành"))), LegalWatchPolicy.Date(rows.GetValueOrDefault("Ngày ban hành")),
            LegalWatchPolicy.Date(rows.GetValueOrDefault("Ngày hiệu lực")), LegalWatchPolicy.Join(LegalWatchPolicy.Relations(summary)), string.Join(", ", keywords));
    }

    internal static string Clean(string? html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " ")), @"\s+", " ").Trim().Normalize(NormalizationForm.FormC);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : LegalWatchPolicy.Clip(s.Trim(), 200);

    // "BỘ TÀI CHÍNH" → "Bộ Tài chính" style is not attempted; only upper case is lowered to title case for readability.
    private static string? Title(string? s) =>
        s is null ? null : s.Any(char.IsLower) ? s : CultureInfo.GetCultureInfo("vi-VN").TextInfo.ToTitleCase(s.ToLower(CultureInfo.GetCultureInfo("vi-VN")));
}

public static class VbplParser
{
    private static readonly Regex Loc = new(@"<loc>(?<url>https://vbpl\.vn/van-ban/chi-tiet/(?<slug>[^<]+))</loc>", RegexOptions.CultureInvariant);

    // URLs of one sitemap file whose slug names the document number right after its type: "nghi-dinh-so-123-2020-nd-cp-…".
    public static IEnumerable<(Guid DocumentId, string Url)> Match(string xml, IReadOnlyList<(Guid Id, string DocumentNo, string DocType)> docs)
    {
        var wanted = docs.Select(d => (d.Id, Slug: LegalWatchPolicy.Slug(d.DocumentNo), Prefixes: LegalWatchPolicy.SlugPrefixes(d.DocType))).ToList();
        foreach (Match m in Loc.Matches(xml))
        {
            var slug = m.Groups["slug"].Value;
            var first = Regex.Match(slug, @"\d{1,4}-(?:\d{4}-)?[a-z]");
            if (!first.Success) continue;
            foreach (var w in wanted)
            {
                if (!w.Prefixes.Any(p => slug.StartsWith(p, StringComparison.Ordinal))) continue;
                if (string.CompareOrdinal(slug, first.Index, w.Slug, 0, w.Slug.Length) != 0) continue;
                var end = first.Index + w.Slug.Length;
                if (end < slug.Length && slug[end] != '-') continue;
                yield return (w.Id, m.Groups["url"].Value);
            }
        }
    }

    private static readonly Regex Block = new(@"\\""docType\\"":\{\\""id\\"".{0,4000}", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // The document's embedded data (escaped JSON inside the page script). Null when the page has none.
    public static VbplStatus? Detail(string html)
    {
        var block = Block.Match(html);
        if (!block.Success) return null;
        var b = block.Value;
        var no = Field(b, "docNum");
        if (no is null) return null;
        var status = Regex.Match(b, @"\\""effStatus\\"":\{[^}]*?\\""name\\"":\\""(?<v>[^\\]*)\\""", RegexOptions.CultureInvariant);
        return new(no, status.Success ? Unescape(status.Groups["v"].Value) : null, IsoDate(Field(b, "effFrom")), IsoDate(Field(b, "effTo")));
    }

    private static string? Field(string block, string name)
    {
        var m = Regex.Match(block, $@"\\""{name}\\"":(?:\\""(?<v>[^\\]*)\\""|null)", RegexOptions.CultureInvariant);
        return m.Success && m.Groups["v"].Success ? Unescape(m.Groups["v"].Value) : null;
    }

    private static string Unescape(string s) =>
        Regex.Replace(s, @"\\u(?<h>[0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups["h"].Value, 16)).ToString()).Normalize(NormalizationForm.FormC);

    private static DateTime? IsoDate(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
}
