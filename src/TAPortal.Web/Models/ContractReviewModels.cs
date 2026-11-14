using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

public sealed record ContractReviewsIndexVm(IReadOnlyList<ReviewListItem>? Items, ReviewFilter Filter, ReviewLookups Lookups, string? Error);

public sealed record ContractReviewCreateVm(ReviewInput Input, ReviewLookups Lookups);

// Mode "findings" highlights the clauses flagged on this version; "changes" shows the track changes from the version before.
// Mode "compare": Original (v0) beside Draft = the current version with every accepted, not yet applied finding applied.
public sealed record ContractReviewWorkspaceVm(ReviewDetail Review, int VersionNo, string Mode, IReadOnlyList<ReviewParagraph> Paragraphs,
    IReadOnlyList<ReviewParagraph>? Previous, IReadOnlyList<ReviewFinding> Findings, IReadOnlyList<ReviewChange> Changes, bool AiConfigured, string AiModel,
    IReadOnlyDictionary<string, LegalArticleLink>? Legal = null, IReadOnlyList<ReviewParagraph>? Original = null, IReadOnlyList<ReviewParagraph>? Draft = null,
    int DraftDecided = 0, IReadOnlyDictionary<string, List<ReviewFinding>>? RowFindings = null)
{
    public IReadOnlyList<ReviewFinding> FindingsOf(string key) => RowFindings?.GetValueOrDefault(key) ?? (IReadOnlyList<ReviewFinding>)[];
    public int SuggestionCount => RowFindings?.Values.SelectMany(v => v).Select(f => f.Id).Distinct().Count() ?? 0;
    public IReadOnlyDictionary<string, LegalArticleLink> LegalLinks => Legal ?? new Dictionary<string, LegalArticleLink>();

    // Rows of the side-by-side view, aligned by paragraph key (paragraphs removed in the draft stay where they stood).
    public IEnumerable<(string Key, string Kind, string? Left, string? Right)> CompareRows()
    {
        var left = Original ?? [];
        var right = Draft ?? [];
        var leftByKey = left.ToDictionary(p => p.Key);
        var rightKeys = right.Select(p => p.Key).ToHashSet();
        var leftIndex = left.Select((p, i) => (p.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var shown = new HashSet<string>();
        IEnumerable<(string, string, string?, string?)> RemovedBefore(int limit)
        {
            for (var i = 0; i < limit; i++)
                if (!rightKeys.Contains(left[i].Key) && shown.Add(left[i].Key)) yield return (left[i].Key, left[i].Kind, left[i].Text, null);
        }
        foreach (var p in right)
        {
            if (leftIndex.TryGetValue(p.Key, out var at)) foreach (var r in RemovedBefore(at)) yield return r;
            shown.Add(p.Key);
            yield return (p.Key, p.Kind, leftByKey.GetValueOrDefault(p.Key)?.Text, p.Text);
        }
        foreach (var r in RemovedBefore(left.Count)) yield return r;
    }

    public bool IsCurrent => VersionNo == Review.CurrentVersionNo;
    public ReviewVersionInfo? Version => Review.Versions.FirstOrDefault(v => v.No == VersionNo);
    public int OpenCount => Findings.Count(f => f.Status == ContractReviewPolicy.Open);
    public int DecidedCount => Findings.Count(f => f.IsDecided && f.AppliedInVersionNo is null);
    public ReviewRunInfo? ActiveRun => Review.Runs.FirstOrDefault(r => r.Status is "QUEUED" or "RUNNING");

    // Paragraphs in reading order for the "changes" mode: paragraphs deleted in this version stay where they stood.
    public IEnumerable<(string Key, string Kind, string? OldText, string? NewText, ReviewChange? Change)> Rows()
    {
        var byKey = Changes.GroupBy(c => c.ParagraphKey).ToDictionary(g => g.Key, g => g.First());
        var keys = Paragraphs.Select(p => p.Key).ToHashSet();
        var old = Previous ?? [];
        var oldIndex = old.Select((p, i) => (p.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var shown = new HashSet<string>();
        IEnumerable<(string, string, string?, string?, ReviewChange?)> DeletedBefore(int limit)
        {
            for (var i = 0; i < limit; i++)
                if (!keys.Contains(old[i].Key) && shown.Add(old[i].Key)) yield return (old[i].Key, old[i].Kind, old[i].Text, null, byKey.GetValueOrDefault(old[i].Key));
        }
        foreach (var p in Paragraphs)
        {
            if (oldIndex.TryGetValue(p.Key, out var at)) foreach (var d in DeletedBefore(at)) yield return d;
            shown.Add(p.Key);
            var c = byKey.GetValueOrDefault(p.Key);
            yield return (p.Key, p.Kind, c is null ? p.Text : c.OldText, p.Text, c);
        }
        foreach (var d in DeletedBefore(old.Count)) yield return d;
    }
}

public sealed record LegalLibraryIndexVm(IReadOnlyList<LegalDocumentItem>? Items, string? Error);

public sealed record LegalDocumentEditVm(Guid? Id, LegalDocumentInput Input, int ArticleCount);

public sealed record LegalWatchIndexVm(LegalWatchOverview? Data, bool Pending, string? Error);

// HTML of one contract paragraph, like Word's review view. Open findings are yellow; a decided finding shows its effect in
// place (accepted: old words struck through, new words underlined green; rejected: plain with a dotted line). Every mark is a
// button (data-ta-pop) that opens the decision panel of ta-review.js; the hover note stays for a quick look. All text is
// HTML-encoded here.
public static class ReviewHtml
{
    private static readonly HtmlEncoder E = HtmlEncoder.Default;

    public static IHtmlContent Findings(ReviewParagraph p, IReadOnlyList<ReviewFinding> findings)
    {
        var sb = new StringBuilder();
        // An accepted whole-paragraph change (rewrite or removal) is shown as the change itself.
        var whole = findings.FirstOrDefault(f => f.IsDecided && f.Quote is null && f.Action is ContractReviewPolicy.Replace or ContractReviewPolicy.Delete);
        var spans = new List<(int Start, int End, ReviewFinding F)>();
        if (whole is null)
            foreach (var f in findings.Where(f => f.Quote is not null))
            {
                var at = p.Text.IndexOf(f.Quote!, StringComparison.Ordinal);
                if (at < 0) continue;
                if (spans.Any(s => at < s.End && at + f.Quote!.Length > s.Start)) continue;   // overlapping quotes: the first one wins
                spans.Add((at, at + f.Quote!.Length, f));
            }
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        if (whole is not null)
        {
            Open(sb, whole, "ta-rv-mark ta-rv-whole");
            if (whole.Action == ContractReviewPolicy.Delete) { sb.Append("<del class=\"ta-rv-del\">"); Text(sb, p.Text); sb.Append("</del>"); }
            else Diff(sb, p.Text, whole.Replacement ?? p.Text);
            Tip(sb, whole);
            sb.Append("</span>");
        }
        else
        {
            var pos = 0;
            foreach (var (start, end, f) in spans)
            {
                Text(sb, p.Text[pos..start]);
                Open(sb, f, "ta-rv-mark");
                var quote = p.Text[start..end];
                if (f.IsDecided && f.Action == ContractReviewPolicy.Replace && f.Replacement is not null) Diff(sb, quote, f.Replacement);
                else if (f.IsDecided && f.Action == ContractReviewPolicy.Delete) { sb.Append("<del class=\"ta-rv-del\">"); Text(sb, quote); sb.Append("</del>"); }
                else Text(sb, quote);
                Tip(sb, f);
                sb.Append("</span>");
                pos = end;
            }
            Text(sb, p.Text[pos..]);
        }
        foreach (var f in findings.Where(f => f != whole && !spans.Any(s => s.F.Id == f.Id)))
        {
            sb.Append(' ');
            Open(sb, f, "ta-rv-flag");
            sb.Append(E.Encode(f.Action == ContractReviewPolicy.Insert ? "+ thêm đoạn" : f.Action == ContractReviewPolicy.Delete ? "bỏ đoạn" : f.Action == ContractReviewPolicy.Replace ? "sửa cả đoạn" : "góp ý"));
            Tip(sb, f);
            sb.Append("</span>");
        }
        return new HtmlString(sb.ToString());
    }

    // Track changes between two texts; with popId the whole change is one button opening that panel (customer answers).
    public static IHtmlContent Changes(string? oldText, string? newText, string? note, string? popId = null, string? state = null)
    {
        var sb = new StringBuilder();
        if (popId is not null) sb.Append("<span class=\"ta-rv-chg").Append(state is null ? "" : " is-" + state.ToLowerInvariant()).Append("\" data-ta-pop=\"").Append(E.Encode(popId)).Append("\" role=\"button\" tabindex=\"0\">");
        foreach (var part in WordDiff.Compute(oldText, newText))
        {
            switch (part.Op)
            {
                case DiffOp.Same: Text(sb, part.Text); break;
                case DiffOp.Insert: sb.Append("<ins class=\"ta-rv-ins\" tabindex=\"0\">"); Text(sb, part.Text); NoteTip(sb, note); sb.Append("</ins>"); break;
                case DiffOp.Delete: sb.Append("<del class=\"ta-rv-del\" tabindex=\"0\">"); Text(sb, part.Text); NoteTip(sb, note); sb.Append("</del>"); break;
            }
        }
        if (popId is not null) sb.Append("</span>");
        return new HtmlString(sb.ToString());
    }

    // Plain encoded text with line breaks (for previews of inserted paragraphs).
    public static IHtmlContent Plain(string? text)
    {
        var sb = new StringBuilder();
        Text(sb, text ?? "");
        return new HtmlString(sb.ToString());
    }

    private static void Open(StringBuilder sb, ReviewFinding f, string cls) =>
        sb.Append("<span class=\"").Append(cls).Append(" ta-rv-sev-").Append(f.Severity.ToLowerInvariant()).Append(Status(f))
          .Append("\" data-ta-pop=\"f-").Append(f.Id).Append("\" role=\"button\" tabindex=\"0\">");

    private static void Diff(StringBuilder sb, string oldText, string newText)
    {
        foreach (var part in WordDiff.Compute(oldText, newText))
        {
            switch (part.Op)
            {
                case DiffOp.Same: Text(sb, part.Text); break;
                case DiffOp.Insert: sb.Append("<ins class=\"ta-rv-ins\">"); Text(sb, part.Text); sb.Append("</ins>"); break;
                case DiffOp.Delete: sb.Append("<del class=\"ta-rv-del\">"); Text(sb, part.Text); sb.Append("</del>"); break;
            }
        }
    }

    private static string Status(ReviewFinding f) => f.Status switch
    {
        ContractReviewPolicy.Rejected => " is-rejected",
        ContractReviewPolicy.Accepted or ContractReviewPolicy.Edited => " is-accepted",
        _ => ""
    };

    private static void Tip(StringBuilder sb, ReviewFinding f)
    {
        sb.Append("<span class=\"ta-rv-tip\" role=\"tooltip\"><strong>").Append(E.Encode(ContractReviewPolicy.Label(ContractReviewPolicy.Severities, f.Severity)))
          .Append(" · ").Append(E.Encode(ContractReviewPolicy.Label(ContractReviewPolicy.FindingStatuses, f.Status))).Append("</strong>");
        Line(sb, f.Issue);
        if (f.Replacement is not null) Line(sb, "Đề xuất: " + f.Replacement);
        Line(sb, "Bấm để xem chi tiết và quyết định.");
        sb.Append("</span>");
    }

    // Citations of a finding: an article found in the law library links to it (opened and expanded on the library page);
    // anything else is shown as text marked "chưa có trong kho".
    public static IHtmlContent Legal(string? legalBasis, IReadOnlyDictionary<string, LegalArticleLink> links, string detailsUrl)
    {
        var sb = new StringBuilder();
        foreach (var c in LegalCitation.Parse(legalBasis))
        {
            if (c.Key is not null && links.TryGetValue(c.Key, out var l))
            {
                var label = $"{l.ArticleNo}{(c.Clause is null ? "" : ", " + c.Clause)}{(l.ArticleTitle is null ? "" : ". " + l.ArticleTitle)} — {l.DocumentTitle} ({l.DocumentNo})";
                // ?k / ?d: the law page scrolls to the article and highlights that clause (khoản) and point (điểm).
                var query = string.Join("&", new[] { c.Khoan is null ? null : "k=" + Uri.EscapeDataString(c.Khoan), c.Diem is null ? null : "d=" + Uri.EscapeDataString(c.Diem) }.Where(x => x is not null));
                var href = detailsUrl.TrimEnd('/') + "/" + l.DocumentId + (query.Length == 0 ? "" : "?" + query) + "#a-" + l.ArticleId;
                sb.Append("<span class=\"ta-rv-legal\"><a class=\"ta-rv-legal-link\" target=\"_blank\" rel=\"noopener\" href=\"").Append(E.Encode(href))
                  .Append("\" title=\"").Append(E.Encode("Mở " + l.ArticleNo + " trong kho văn bản pháp luật và tô sáng phần được trích" + (l.SourceFile is null ? "" : " (file nguồn: " + l.SourceFile + ")"))).Append("\">")
                  .Append(E.Encode(label)).Append(" ↗</a>");
                if (!string.IsNullOrWhiteSpace(l.SourceUrl) && Uri.TryCreate(l.SourceUrl, UriKind.Absolute, out var src) && src.Scheme == Uri.UriSchemeHttps)
                    sb.Append("<a class=\"ta-rv-legal-web\" target=\"_blank\" rel=\"noopener noreferrer\" href=\"").Append(E.Encode(src.AbsoluteUri)).Append("\">Văn bản gốc trên vbpl.vn ↗</a>");
                sb.Append("<a class=\"ta-rv-legal-web\" target=\"_blank\" rel=\"noopener noreferrer\" href=\"")
                  .Append(E.Encode("https://www.google.com/search?q=" + Uri.EscapeDataString($"\"{l.ArticleNo}\" \"{l.DocumentNo}\""))).Append("\">Tìm điều này trên mạng ↗</a></span>");
            }
            else sb.Append("<span class=\"ta-rv-legal-link text-secondary\">").Append(E.Encode(c.Text)).Append(" <em>(chưa có trong kho luật)</em></span>");
        }
        return new HtmlString(sb.ToString());
    }

    // One side of the side-by-side view: the original keeps deleted words (struck), the draft keeps inserted words.
    public static IHtmlContent Side(string? left, string? right, bool original)
    {
        var sb = new StringBuilder();
        if (left == right) { Text(sb, (original ? left : right) ?? ""); return new HtmlString(sb.ToString()); }
        foreach (var part in WordDiff.Compute(left, right))
        {
            if (part.Op == DiffOp.Same) Text(sb, part.Text);
            else if (original && part.Op == DiffOp.Delete) { sb.Append("<del class=\"ta-rv-del\">"); Text(sb, part.Text); sb.Append("</del>"); }
            else if (!original && part.Op == DiffOp.Insert) { sb.Append("<ins class=\"ta-rv-ins\">"); Text(sb, part.Text); sb.Append("</ins>"); }
        }
        return new HtmlString(sb.ToString());
    }

    private static void NoteTip(StringBuilder sb, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        sb.Append("<span class=\"ta-rv-tip\" role=\"tooltip\">"); Line(sb, note); sb.Append("</span>");
    }

    private static void Line(StringBuilder sb, string text) => sb.Append("<span class=\"ta-rv-tip__line\">").Append(E.Encode(text)).Append("</span>");

    private static void Text(StringBuilder sb, string text) => sb.Append(string.Join("<br />", text.Split('\n').Select(E.Encode)));
}
