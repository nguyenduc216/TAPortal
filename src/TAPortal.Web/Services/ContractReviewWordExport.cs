using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TAPortal.Web.Services;

// One change to mark in the customer's own Word file.
public sealed record WordMark(string ParagraphKey, string Action, string? Quote, string? Replacement, string Comment);

public sealed record WordExportResult(byte[] Data, int Applied, int CommentOnly);

// "Xuất Word chỉnh lý" (operator 2026-11-14): marks the suggestions directly in the ORIGINAL .docx the customer sent, so the
// file keeps its own structure, styles, tables, numbering, headers and footers. Nothing is rewritten or removed:
//   - text proposed for removal is struck through (w:strike) and stays in place;
//   - new or replacement text is inserted right after it, highlighted yellow (w:highlight="yellow");
//   - every change carries a Word comment with the reason, the replacement text and the legal basis.
// A quote that is no longer found in the paragraph (e.g. made on a later version) is kept as a comment on the paragraph.
// Paragraph keys (P1..Pn) are found exactly as DocxParagraphs.Read numbers them. Every other part of the package is copied
// byte for byte; only document.xml, comments.xml, the document relationships and [Content_Types].xml are written.
public static class ContractReviewWordExport
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string CommentsType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CommentsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";
    private const string Author = "T.A";

    public static WordExportResult Mark(byte[] original, IReadOnlyList<WordMark> marks, DateTime? when = null)
    {
        var date = (when ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ");
        using var source = new ZipArchive(new MemoryStream(original, false), ZipArchiveMode.Read);
        var doc = Load(source, "word/document.xml") ?? throw new InvalidDataException("word/document.xml missing");
        var body = doc.Root?.Element(W + "body") ?? throw new InvalidDataException("w:body missing");
        var commentsPath = "word/comments.xml";
        var rels = Load(source, "word/_rels/document.xml.rels") ?? new XDocument(new XElement(Rel + "Relationships"));
        var existingRel = rels.Root!.Elements(Rel + "Relationship").FirstOrDefault(r => (string?)r.Attribute("Type") == CommentsType);
        if (existingRel is not null) commentsPath = "word/" + ((string)existingRel.Attribute("Target")!).TrimStart('/').Replace("word/", "");
        var comments = Load(source, commentsPath) ?? new XDocument(new XElement(W + "comments", new XAttribute(XNamespace.Xmlns + "w", W)));
        var nextId = comments.Root!.Elements(W + "comment").Select(c => (int?)c.Attribute(W + "id") ?? -1).DefaultIfEmpty(-1).Max() + 1;
        nextId = Math.Max(nextId, body.Descendants().Where(e => e.Name == W + "commentRangeStart").Select(e => (int?)e.Attribute(W + "id") ?? -1).DefaultIfEmpty(-1).Max() + 1);

        var paragraphs = Paragraphs(body);
        var inserted = new HashSet<XElement>();
        int applied = 0, commentOnly = 0;
        foreach (var group in marks.GroupBy(m => m.ParagraphKey))
        {
            if (!paragraphs.TryGetValue(group.Key, out var p))
            {
                // A paragraph added in a later version is not in the original file: keep its suggestions as comments at the top.
                var top = paragraphs.Values.FirstOrDefault();
                foreach (var m in group)
                {
                    var cid = nextId++;
                    AddComment(comments, cid, $"[Đoạn {group.Key} của phiên bản sau, không có trong file gốc]\n" + m.Comment + (m.Replacement is null ? "" : "\nNội dung: " + m.Replacement), date);
                    if (top is not null) WrapComment(top, [], cid);
                    commentOnly++;
                }
                continue;
            }
            foreach (var m in group)
            {
                var id = nextId++;
                AddComment(comments, id, m.Comment, date);
                switch (m.Action)
                {
                    case ContractReviewPolicy.Insert:
                        var added = NewParagraphAfter(p, m.Replacement ?? "", inserted);
                        WrapComment(added, added.Elements(W + "r").ToList(), id);
                        applied++;
                        break;
                    case ContractReviewPolicy.Delete:
                        var all = TextRuns(p, inserted);
                        foreach (var r in all) Strike(r);
                        WrapComment(p, all, id);
                        applied++;
                        break;
                    case ContractReviewPolicy.Replace when m.Replacement is not null:
                        var range = string.IsNullOrEmpty(m.Quote) ? TextRuns(p, inserted) : FindAndSplit(p, m.Quote!, inserted);
                        if (range is null || range.Count == 0) { WrapComment(p, TextRuns(p, inserted), id); commentOnly++; break; }
                        foreach (var r in range) Strike(r);
                        var newRun = NewRun(range[^1], m.Replacement);
                        range[^1].AddAfterSelf(newRun);
                        inserted.Add(newRun);
                        WrapComment(p, [.. range, newRun], id);
                        applied++;
                        break;
                    default:   // NOTE, or a replacement without text: comment on the quote or the paragraph
                        var noted = string.IsNullOrEmpty(m.Quote) ? null : FindAndSplit(p, m.Quote!, inserted);
                        WrapComment(p, noted is { Count: > 0 } ? noted : TextRuns(p, inserted), id);
                        commentOnly++;
                        break;
                }
            }
        }

        // Package: the comments part, its relationship and its content type are added only when the file had none.
        var types = Load(source, "[Content_Types].xml")!;
        if (existingRel is null)
        {
            var ids = rels.Root.Elements(Rel + "Relationship").Select(r => (string?)r.Attribute("Id")).ToHashSet();
            var n = 1;
            while (ids.Contains("rIdTA" + n)) n++;
            rels.Root.Add(new XElement(Rel + "Relationship", new XAttribute("Id", "rIdTA" + n), new XAttribute("Type", CommentsType), new XAttribute("Target", "comments.xml")));
        }
        var partName = "/" + commentsPath;
        if (!types.Root!.Elements(Ct + "Override").Any(o => string.Equals((string?)o.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase)))
            types.Root.Add(new XElement(Ct + "Override", new XAttribute("PartName", partName), new XAttribute("ContentType", CommentsContentType)));

        var replaced = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase)
        {
            ["word/document.xml"] = doc, [commentsPath] = comments, ["word/_rels/document.xml.rels"] = rels, ["[Content_Types].xml"] = types
        };
        using var ms = new MemoryStream();
        using (var target = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                if (replaced.ContainsKey(entry.FullName)) continue;
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }
            foreach (var (name, xml) in replaced)
            {
                var e = target.CreateEntry(name, CompressionLevel.Optimal);
                using var to = e.Open();
                using var writer = XmlWriter.Create(to, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
                xml.Save(writer);
            }
        }
        return new(ms.ToArray(), applied, commentOnly);
    }

    // P1..Pn exactly as DocxParagraphs.Read numbers them (non-empty paragraphs of the body, tables and content controls).
    public static Dictionary<string, XElement> Paragraphs(XElement body)
    {
        var map = new Dictionary<string, XElement>();
        var n = 0;
        void Add(XElement p)
        {
            if (DocxParagraphs.Normalize(DocxParagraphs.TextOf(p)).Length == 0) return;
            map["P" + ++n] = p;
        }
        foreach (var block in body.Elements())
        {
            if (block.Name == W + "p") Add(block);
            else if (block.Name == W + "tbl" || block.Name == W + "sdt") foreach (var p in block.Descendants(W + "p").ToList()) Add(p);
        }
        return map;
    }

    // ---- locating a quote across runs ---------------------------------------------------------------------------
    // Characters of the paragraph as DocxParagraphs.Normalize sees them, each pointing back to its w:t and offset
    // (null for tabs, breaks and collapsed spaces), so a quote found in the normalized text maps to real runs.
    private sealed record Char(char C, XElement? T, int Offset);

    private static List<XElement>? FindAndSplit(XElement p, string quote, HashSet<XElement> inserted)
    {
        var raw = new List<Char>();
        foreach (var node in p.Descendants().ToList())
        {
            if (node.Name == W + "t" && node.Parent is { } run && !inserted.Contains(run)) { for (var i = 0; i < node.Value.Length; i++) raw.Add(new(node.Value[i] == ' ' ? ' ' : node.Value[i], node, i)); }
            else if (node.Name == W + "tab") raw.Add(new(' ', null, 0));
            else if (node.Name == W + "br" || node.Name == W + "cr") raw.Add(new('\n', null, 0));
            else if (node.Name == W + "noBreakHyphen") raw.Add(new('-', null, 0));
        }
        // Normalize: collapse spaces/tabs, trim each line (same rules as DocxParagraphs.Normalize).
        var norm = new List<Char>();
        var lineStart = 0;
        foreach (var c in raw)
        {
            if (c.C == '\n')
            {
                while (norm.Count > lineStart && norm[^1].C == ' ') norm.RemoveAt(norm.Count - 1);
                norm.Add(c);
                lineStart = norm.Count;
                continue;
            }
            if (char.IsWhiteSpace(c.C) && (norm.Count == lineStart || norm[^1].C == ' ')) continue;
            norm.Add(char.IsWhiteSpace(c.C) ? c with { C = ' ' } : c);
        }
        var text = new string(norm.Select(c => c.C).ToArray());
        var at = text.IndexOf(quote, StringComparison.Ordinal);
        if (at < 0) return null;
        var slice = norm.Skip(at).Take(quote.Length).Where(c => c.T is not null).ToList();
        if (slice.Count == 0) return null;
        // Split just after the end first (start offsets stay valid), then at the start; take every text run in between.
        var startT = slice[0].T!; var startOff = slice[0].Offset;
        var endT = slice[^1].T!; var endOff = slice[^1].Offset + 1;
        SplitAfter(endT, endOff);
        var firstT = startOff > 0 ? SplitAfter(startT, startOff) : startT;
        var firstRun = firstT.Parent!;
        var lastRun = (ReferenceEquals(startT, endT) ? firstT : endT).Parent!;
        var runs = TextRuns(p, inserted);
        var i0 = runs.IndexOf(firstRun); var i1 = runs.IndexOf(lastRun);
        return i0 < 0 || i1 < i0 ? null : runs.GetRange(i0, i1 - i0 + 1);
    }

    // Splits t's run so that t keeps text[..offset] and a new run (same formatting) after it holds the rest and every later
    // child of the run. Returns the w:t holding the rest (t itself when offset is 0 or at the end).
    private static XElement SplitAfter(XElement t, int offset)
    {
        if (offset <= 0) return t;
        var run = t.Parent!;
        var text = t.Value;
        var later = t.ElementsAfterSelf().ToList();
        if (offset >= text.Length && later.Count == 0) return t;
        var restT = new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text[Math.Min(offset, text.Length)..]);
        SetText(t, text[..Math.Min(offset, text.Length)]);
        var newRun = new XElement(run.Name, run.Attributes().Where(a => a.Name.LocalName is not ("rsidR" or "rsidRPr")), run.Element(W + "rPr") is { } rPr ? new XElement(rPr) : null, restT);
        foreach (var e in later) { e.Remove(); newRun.Add(e); }
        run.AddAfterSelf(newRun);
        return restT;
    }

    private static void SetText(XElement t, string value)
    {
        t.Value = value;
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))) t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
    }

    // Runs holding text in this paragraph (not those added by this export).
    private static List<XElement> TextRuns(XElement p, HashSet<XElement> inserted) =>
        p.Descendants(W + "r").Where(r => !inserted.Contains(r) && r.Elements(W + "t").Any(t => t.Value.Length > 0)).ToList();

    // ---- formatting ---------------------------------------------------------------------------------------------
    private static XElement RunProps(XElement run)
    {
        var rPr = run.Element(W + "rPr");
        if (rPr is null) { rPr = new XElement(W + "rPr"); run.AddFirst(rPr); }
        return rPr;
    }

    private static void Strike(XElement run)
    {
        var rPr = RunProps(run);
        rPr.Elements(W + "strike").Remove();
        rPr.Elements(W + "dstrike").Remove();
        rPr.Add(new XElement(W + "strike"));
        Order(rPr);
    }

    // New run with the formatting of "like" (without strike), yellow highlight, text with line breaks.
    private static XElement NewRun(XElement like, string text)
    {
        var rPr = like.Element(W + "rPr") is { } src ? new XElement(src) : new XElement(W + "rPr");
        rPr.Elements().Where(e => e.Name == W + "strike" || e.Name == W + "dstrike" || e.Name == W + "highlight" || e.Name == W + "shd" || e.Name == W + "rStyle" && ((string?)e.Attribute(W + "val"))?.Contains("Comment") == true).Remove();
        rPr.Add(new XElement(W + "highlight", new XAttribute(W + "val", "yellow")));
        Order(rPr);
        var run = new XElement(W + "r", rPr);
        var lines = (" " + text.Trim()).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Add(new XElement(W + "br"));
            if (lines[i].Length > 0) run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i]));
        }
        return run;
    }

    // A new paragraph after p, with p's paragraph properties, holding the highlighted text.
    private static XElement NewParagraphAfter(XElement p, string text, HashSet<XElement> inserted)
    {
        var like = TextRuns(p, inserted).LastOrDefault() ?? new XElement(W + "r");
        var run = NewRun(like, text);
        inserted.Add(run);
        var pPr = p.Element(W + "pPr") is { } src ? new XElement(src) : null;
        pPr?.Elements(W + "rPr").Remove();
        var added = new XElement(W + "p", pPr, run);
        p.AddAfterSelf(added);
        return added;
    }

    // Word expects rPr children in schema order; strike/highlight must come before size, colour etc. would not matter to
    // most readers, but keep the common order to stay valid: put known toggles first in their schema position.
    private static readonly string[] RPrOrder =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint", "noProof",
        "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd",
        "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath"
    ];

    private static void Order(XElement rPr)
    {
        var children = rPr.Elements().ToList();
        int Rank(XElement e) { var i = e.Name.Namespace == W ? Array.IndexOf(RPrOrder, e.Name.LocalName) : -1; return i < 0 ? RPrOrder.Length : i; }
        var sorted = children.Select((e, i) => (e, i)).OrderBy(x => Rank(x.e)).ThenBy(x => x.i).Select(x => x.e).ToList();
        foreach (var e in children) e.Remove();
        rPr.Add(sorted);
    }

    // ---- comments -----------------------------------------------------------------------------------------------
    private static void AddComment(XDocument comments, int id, string text, string date)
    {
        var c = new XElement(W + "comment", new XAttribute(W + "id", id), new XAttribute(W + "author", Author), new XAttribute(W + "date", date), new XAttribute(W + "initials", "TA"));
        foreach (var line in (string.IsNullOrWhiteSpace(text) ? "Góp ý" : text).Replace("\r\n", "\n").Split('\n'))
            c.Add(new XElement(W + "p", new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(line)))));
        comments.Root!.Add(c);
    }

    // Comment range around the given runs (or the paragraph start when there are none) and the reference mark after it.
    private static void WrapComment(XElement p, IReadOnlyList<XElement> runs, int id)
    {
        var start = new XElement(W + "commentRangeStart", new XAttribute(W + "id", id));
        var end = new XElement(W + "commentRangeEnd", new XAttribute(W + "id", id));
        var reference = new XElement(W + "r", new XElement(W + "rPr", new XElement(W + "rStyle", new XAttribute(W + "val", "CommentReference"))), new XElement(W + "commentReference", new XAttribute(W + "id", id)));
        if (runs.Count == 0)
        {
            var anchor = p.Element(W + "pPr");
            if (anchor is null) p.AddFirst(start, end, reference); else anchor.AddAfterSelf(start, end, reference);
            return;
        }
        runs[0].AddBeforeSelf(start);
        runs[^1].AddAfterSelf(end, reference);
    }

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) if (XmlConvert.IsXmlChar(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        if (entry.Length > 50 * 1024 * 1024) throw new InvalidDataException(name + " too large");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    // Comment text for a finding: reason, replacement and legal basis.
    public static string CommentFor(ReviewFinding f)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(ContractReviewPolicy.Label(ContractReviewPolicy.Severities, f.Severity)).Append("] ").AppendLine(f.Issue);
        var replacement = f.Replacement;
        switch (f.Action)
        {
            case ContractReviewPolicy.Replace when replacement is not null: sb.Append("Nội dung thay thế: ").AppendLine(replacement); break;
            case ContractReviewPolicy.Insert when replacement is not null: sb.Append("Đề xuất bổ sung: ").AppendLine(replacement); break;
            case ContractReviewPolicy.Delete: sb.AppendLine("Đề xuất bỏ đoạn này."); break;
        }
        if (f.LegalBasis is not null) sb.Append("Căn cứ: ").AppendLine(f.LegalBasis.Replace("\n", "; "));
        if (f.StaffNote is not null) sb.Append("Ghi chú: ").AppendLine(f.StaffNote);
        return sb.ToString().TrimEnd();
    }
}
