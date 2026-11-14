using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TAPortal.Web.Services;

// One paragraph of a contract version. Key is stable across versions (P1..Pn from the original, Nn for inserted ones).
public sealed record ReviewParagraph(string Key, string Kind, string Text);

// Reads the paragraphs of a DOCX (word/document.xml) without any third-party package: body paragraphs in order, table
// cells as "TABLE" paragraphs, headings by style (Heading/Title/Tiêu đề) or numbering. Text keeps tabs as spaces and
// line breaks as "\n"; empty paragraphs are skipped. DTDs are refused (no XXE).
public static class DocxParagraphs
{
    public const int MaxParagraphs = 3000;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IReadOnlyList<ReviewParagraph> Read(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx, false), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("word/document.xml missing");
        if (entry.Length > 50 * 1024 * 1024) throw new InvalidDataException("document.xml too large");
        XDocument doc;
        using (var stream = entry.Open())
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc = XDocument.Load(reader);
        var body = doc.Root?.Element(W + "body") ?? throw new InvalidDataException("w:body missing");
        var result = new List<ReviewParagraph>();
        foreach (var block in body.Elements())
        {
            if (block.Name == W + "p") Add(result, block, inTable: false);
            else if (block.Name == W + "tbl")
                foreach (var p in block.Descendants(W + "p")) Add(result, p, inTable: true);
            else if (block.Name == W + "sdt")
                foreach (var p in block.Descendants(W + "p")) Add(result, p, inTable: p.Ancestors(W + "tbl").Any());
            if (result.Count > MaxParagraphs) throw new InvalidDataException("too many paragraphs");
        }
        return result;
    }

    private static void Add(List<ReviewParagraph> list, XElement p, bool inTable)
    {
        var text = Normalize(TextOf(p));
        if (text.Length == 0) return;
        var style = p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "";
        var numbered = p.Element(W + "pPr")?.Element(W + "numPr") is not null;
        var kind = inTable ? "TABLE"
            : Regex.IsMatch(style, "heading|title|tieude|tiêuđề|chuong|muc", RegexOptions.IgnoreCase) || ReviewText.LooksLikeHeading(text) ? "HEADING"
            : numbered ? "LIST" : "TEXT";
        list.Add(new("P" + (list.Count + 1), kind, text));
    }

    // Text runs in document order; deleted text of existing tracked changes (w:delText) is ignored, inserted text kept.
    private static string TextOf(XElement p)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Descendants())
        {
            if (node.Name == W + "t") sb.Append(node.Value);
            else if (node.Name == W + "tab") sb.Append(' ');
            else if (node.Name == W + "br" || node.Name == W + "cr") sb.Append('\n');
            else if (node.Name == W + "noBreakHyphen") sb.Append('-');
        }
        return sb.ToString();
    }

    public static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ').Split('\n')
            .Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim());
        return string.Join("\n", lines).Trim('\n', ' ');
    }
}

public static class ReviewText
{
    // "Điều 5.", "ĐIỀU 5:", "Chương II", "Mục 1" or a short all-caps line ("HỢP ĐỒNG DỊCH VỤ").
    public static bool LooksLikeHeading(string text)
    {
        if (text.Length > 160 || text.Contains('\n')) return false;
        if (Regex.IsMatch(text, @"^(Điều|ĐIỀU|Chương|CHƯƠNG|Mục|MỤC|Phần|PHẦN)\s+[0-9IVXLC]+\b")) return true;
        var letters = text.Where(char.IsLetter).ToList();
        return letters.Count >= 6 && letters.Count <= 120 && letters.All(char.IsUpper);
    }

    public static string Short(string? text, int max) => text is null ? "" : text.Length <= max ? text : text[..max].TrimEnd() + "…";
}

// Word-level diff (LCS over words and the spaces between them), used for the on-screen track changes and the DOCX export.
public enum DiffOp { Same, Insert, Delete }

public sealed record DiffPart(DiffOp Op, string Text);

public static class WordDiff
{
    private const int MaxCells = 4_000_000;

    public static IReadOnlyList<DiffPart> Compute(string? oldText, string? newText)
    {
        var a = Tokens(oldText ?? "");
        var b = Tokens(newText ?? "");
        if (a.Count == 0 && b.Count == 0) return [];
        if ((long)a.Count * b.Count > MaxCells) return Merge([new(DiffOp.Delete, oldText ?? ""), new(DiffOp.Insert, newText ?? "")]);
        var n = a.Count; var m = b.Count;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var parts = new List<DiffPart>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { parts.Add(new(DiffOp.Same, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) parts.Add(new(DiffOp.Delete, a[x++]));
            else parts.Add(new(DiffOp.Insert, b[y++]));
        }
        while (x < n) parts.Add(new(DiffOp.Delete, a[x++]));
        while (y < m) parts.Add(new(DiffOp.Insert, b[y++]));
        return Merge(parts);
    }

    public static List<string> Tokens(string text) => Regex.Matches(text, @"\s+|[^\s]+").Select(m => m.Value).ToList();

    // Joins neighbours of the same kind.
    private static IReadOnlyList<DiffPart> Merge(List<DiffPart> parts)
    {
        var merged = new List<DiffPart>();
        foreach (var p in parts.Where(p => p.Text.Length > 0))
        {
            if (merged.Count > 0 && merged[^1].Op == p.Op) merged[^1] = merged[^1] with { Text = merged[^1].Text + p.Text };
            else merged.Add(p);
        }
        return merged;
    }
}

// Writes a contract version as a new DOCX. With "previous" it is a Word track-changes document: changed words are real
// w:ins / w:del revisions (author "T.A Portal") and each change note becomes a Word comment, so the customer can accept or
// reject them in Word. Formatting of the original is not copied (headings bold, table cells as plain paragraphs).
public static class DocxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string Author = "T.A Portal";

    public sealed record Change(string Key, string? OldText, string? NewText, string? Note);

    public static byte[] Write(string title, IReadOnlyList<ReviewParagraph> paragraphs, IReadOnlyList<ReviewParagraph>? previous = null,
        IReadOnlyDictionary<string, string>? notes = null, DateTime? when = null)
    {
        var date = (when ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var body = new StringBuilder();
        var comments = new StringBuilder();
        var revision = 1; var commentId = 0;
        var oldByKey = previous?.ToDictionary(p => p.Key) ?? new Dictionary<string, ReviewParagraph>();
        var newKeys = paragraphs.Select(p => p.Key).ToHashSet();

        void Comment(string text, Action inner)
        {
            var id = commentId++;
            body.Append($"<w:commentRangeStart w:id=\"{id}\"/>");
            inner();
            body.Append($"<w:commentRangeEnd w:id=\"{id}\"/><w:r><w:rPr><w:rStyle w:val=\"CommentReference\"/></w:rPr><w:commentReference w:id=\"{id}\"/></w:r>");
            comments.Append($"<w:comment w:id=\"{id}\" w:author=\"{Author}\" w:date=\"{date}\" w:initials=\"TA\"><w:p><w:r><w:t xml:space=\"preserve\">{X(text)}</w:t></w:r></w:p></w:comment>");
        }

        void Paragraph(ReviewParagraph p, Action runs, bool deletedMark = false)
        {
            body.Append("<w:p>");
            var props = new StringBuilder();
            if (p.Kind == "HEADING") props.Append("<w:keepNext/><w:spacing w:before=\"160\" w:after=\"80\"/>");
            if (deletedMark) props.Append($"<w:rPr><w:del w:id=\"{revision++}\" w:author=\"{Author}\" w:date=\"{date}\"/></w:rPr>");
            if (props.Length > 0) body.Append("<w:pPr>").Append(props).Append("</w:pPr>");
            runs();
            body.Append("</w:p>");
        }

        string Rpr(ReviewParagraph p) => p.Kind == "HEADING" ? "<w:rPr><w:b/></w:rPr>" : "";

        void Runs(string text, string rpr, string kind)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) body.Append($"<w:r>{rpr}<w:br/></w:r>");
                if (lines[i].Length == 0) continue;
                var tag = kind == "del" ? "w:delText" : "w:t";
                body.Append($"<w:r>{rpr}<{tag} xml:space=\"preserve\">{X(lines[i])}</{tag}></w:r>");
            }
        }

        void Diff(ReviewParagraph p, string? oldText)
        {
            var rpr = Rpr(p);
            foreach (var part in WordDiff.Compute(oldText, p.Text))
            {
                switch (part.Op)
                {
                    case DiffOp.Same: Runs(part.Text, rpr, "t"); break;
                    case DiffOp.Insert:
                        body.Append($"<w:ins w:id=\"{revision++}\" w:author=\"{Author}\" w:date=\"{date}\">"); Runs(part.Text, rpr, "t"); body.Append("</w:ins>"); break;
                    case DiffOp.Delete:
                        body.Append($"<w:del w:id=\"{revision++}\" w:author=\"{Author}\" w:date=\"{date}\">"); Runs(part.Text, rpr, "del"); body.Append("</w:del>"); break;
                }
            }
        }

        body.Append($"<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t xml:space=\"preserve\">{X(title)}</w:t></w:r></w:p>");
        // Paragraphs deleted since "previous" are written where they stood, as deleted text.
        var oldOrder = previous?.Select(p => p.Key).ToList() ?? [];
        var emitted = new HashSet<string>();
        void EmitDeletedBefore(string key)
        {
            if (previous is null) return;
            var idx = oldOrder.IndexOf(key);
            for (var i = 0; i < (idx < 0 ? 0 : idx); i++)
            {
                var old = previous[i];
                if (newKeys.Contains(old.Key) || !emitted.Add(old.Key)) continue;
                var note = notes?.GetValueOrDefault(old.Key);
                Paragraph(old, () =>
                {
                    void Del() { body.Append($"<w:del w:id=\"{revision++}\" w:author=\"{Author}\" w:date=\"{date}\">"); Runs(old.Text, Rpr(old), "del"); body.Append("</w:del>"); }
                    if (note is null) Del(); else Comment(note, Del);
                }, deletedMark: true);
            }
        }

        foreach (var p in paragraphs)
        {
            EmitDeletedBefore(p.Key);
            emitted.Add(p.Key);
            var note = notes?.GetValueOrDefault(p.Key);
            if (previous is null) { Paragraph(p, () => Runs(p.Text, Rpr(p), "t")); continue; }
            var hadOld = oldByKey.TryGetValue(p.Key, out var oldP);
            if (hadOld && oldP!.Text == p.Text) { Paragraph(p, () => Runs(p.Text, Rpr(p), "t")); continue; }
            Paragraph(p, () => { if (note is null) Diff(p, oldP?.Text); else Comment(note, () => Diff(p, oldP?.Text)); });
        }
        if (previous is not null)
            foreach (var old in previous.Where(o => !newKeys.Contains(o.Key) && !emitted.Contains(o.Key)).ToList())
            {
                emitted.Add(old.Key);
                Paragraph(old, () => { body.Append($"<w:del w:id=\"{revision++}\" w:author=\"{Author}\" w:date=\"{date}\">"); Runs(old.Text, Rpr(old), "del"); body.Append("</w:del>"); }, deletedMark: true);
            }

        var hasComments = commentId > 0;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
""" + (hasComments ? "<Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/>" : "") + "</Types>");
            Entry(zip, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
""");
            Entry(zip, "word/_rels/document.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
""" + (hasComments ? "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/>" : "") + "</Relationships>");
            Entry(zip, "word/styles.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:cs="Times New Roman"/><w:sz w:val="26"/><w:lang w:val="vi-VN"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/><w:jc w:val="both"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="character" w:styleId="CommentReference"><w:name w:val="annotation reference"/><w:rPr><w:sz w:val="16"/></w:rPr></w:style></w:styles>
""");
            Entry(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
                + body + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1701\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
            if (hasComments)
                Entry(zip, "word/comments.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:comments xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" + comments + "</w:comments>");
        }
        return ms.ToArray();
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content.TrimStart());
    }

    // XML-escapes text and drops characters XML 1.0 does not allow.
    private static string X(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text) if (XmlConvert.IsXmlChar(ch) || char.IsSurrogate(ch)) sb.Append(ch);
        return SecurityElement.Escape(sb.ToString()) ?? "";
    }
}
