using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

public sealed record ContractReviewsIndexVm(IReadOnlyList<ReviewListItem>? Items, ReviewFilter Filter, ReviewLookups Lookups, string? Error);

public sealed record ContractReviewCreateVm(ReviewInput Input, ReviewLookups Lookups);

// Mode "findings" highlights the clauses flagged on this version; "changes" shows the track changes from the version before.
public sealed record ContractReviewWorkspaceVm(ReviewDetail Review, int VersionNo, string Mode, IReadOnlyList<ReviewParagraph> Paragraphs,
    IReadOnlyList<ReviewParagraph>? Previous, IReadOnlyList<ReviewFinding> Findings, IReadOnlyList<ReviewChange> Changes, bool AiConfigured, string AiModel)
{
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

// HTML of one contract paragraph: flagged quotes in yellow with the note on hover/focus, or word-level track changes
// (inserted words yellow, deleted words struck through) against the previous version. Everything is HTML-encoded here.
public static class ReviewHtml
{
    private static readonly HtmlEncoder E = HtmlEncoder.Default;

    public static IHtmlContent Findings(ReviewParagraph p, IReadOnlyList<ReviewFinding> findings)
    {
        var sb = new StringBuilder();
        var spans = new List<(int Start, int End, ReviewFinding F)>();
        foreach (var f in findings.Where(f => f.Quote is not null))
        {
            var at = p.Text.IndexOf(f.Quote!, StringComparison.Ordinal);
            if (at < 0) continue;
            if (spans.Any(s => at < s.End && at + f.Quote!.Length > s.Start)) continue;   // overlapping quotes: the first one wins
            spans.Add((at, at + f.Quote!.Length, f));
        }
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var pos = 0;
        foreach (var (start, end, f) in spans)
        {
            Text(sb, p.Text[pos..start]);
            sb.Append("<a class=\"ta-rv-mark ta-rv-sev-").Append(f.Severity.ToLowerInvariant()).Append(Status(f)).Append("\" href=\"#f-").Append(f.Id).Append("\">");
            Text(sb, p.Text[start..end]);
            Tip(sb, f);
            sb.Append("</a>");
            pos = end;
        }
        Text(sb, p.Text[pos..]);
        foreach (var f in findings.Where(f => !spans.Any(s => s.F.Id == f.Id)))
        {
            sb.Append(" <a class=\"ta-rv-flag ta-rv-sev-").Append(f.Severity.ToLowerInvariant()).Append(Status(f)).Append("\" href=\"#f-").Append(f.Id).Append("\">")
              .Append(E.Encode(f.Action == ContractReviewPolicy.Insert ? "+ thêm đoạn" : f.Action == ContractReviewPolicy.Delete ? "bỏ đoạn" : "góp ý"));
            Tip(sb, f);
            sb.Append("</a>");
        }
        return new HtmlString(sb.ToString());
    }

    public static IHtmlContent Changes(string? oldText, string? newText, string? note)
    {
        var sb = new StringBuilder();
        foreach (var part in WordDiff.Compute(oldText, newText))
        {
            switch (part.Op)
            {
                case DiffOp.Same: Text(sb, part.Text); break;
                case DiffOp.Insert: sb.Append("<ins class=\"ta-rv-ins\" tabindex=\"0\">"); Text(sb, part.Text); NoteTip(sb, note); sb.Append("</ins>"); break;
                case DiffOp.Delete: sb.Append("<del class=\"ta-rv-del\" tabindex=\"0\">"); Text(sb, part.Text); NoteTip(sb, note); sb.Append("</del>"); break;
            }
        }
        return new HtmlString(sb.ToString());
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
          .Append(" · ").Append(E.Encode(ContractReviewPolicy.Label(ContractReviewPolicy.Actions, f.Action))).Append("</strong>");
        Line(sb, f.Issue);
        if (f.LegalBasis is not null) Line(sb, "Căn cứ: " + f.LegalBasis + (f.LegalVerified ? "" : " (chưa đối chiếu kho luật)"));
        if (f.Replacement is not null) Line(sb, "Đề xuất: " + f.Replacement);
        sb.Append("</span>");
    }

    private static void NoteTip(StringBuilder sb, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        sb.Append("<span class=\"ta-rv-tip\" role=\"tooltip\">"); Line(sb, note); sb.Append("</span>");
    }

    private static void Line(StringBuilder sb, string text) => sb.Append("<span class=\"ta-rv-tip__line\">").Append(E.Encode(text)).Append("</span>");

    private static void Text(StringBuilder sb, string text) => sb.Append(string.Join("<br />", text.Split('\n').Select(E.Encode)));
}
