using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace TAPortal.Web.Services;

// Minimal read-only .xlsx reader (Office Open XML is a zip of XML parts), enough to import the customer workbook
// without adding a third-party package: sheet names, cell text, numbers and date-formatted numbers. Formulas are read
// from their cached values. Size limits guard against zip bombs; DTDs are prohibited.
public sealed record XlsxCell(string Text, double? Number, bool IsDate)
{
    public DateOnly? Date => IsDate && Number is { } n && n is > 0 and < 2958466 ? DateOnly.FromDateTime(DateTime.FromOADate(n)) : null;
}

public sealed class XlsxSheet(string name, IReadOnlyDictionary<(int Row, int Col), XlsxCell> cells)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<(int Row, int Col), XlsxCell> Cells { get; } = cells;
    public int MaxRow { get; } = cells.Count == 0 ? 0 : cells.Keys.Max(k => k.Row);
    public XlsxCell? this[int row, int col] => Cells.TryGetValue((row, col), out var c) ? c : null;
    public string? Text(int row, int col) => this[row, col] is { } c && !string.IsNullOrWhiteSpace(c.Text) ? c.Text.Trim() : null;
}

public sealed class XlsxFormatException(string message) : Exception(message);

public static class XlsxReader
{
    public const long MaxPartBytes = 20 * 1024 * 1024;
    public const long MaxTotalBytes = 100 * 1024 * 1024;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<XlsxSheet> Read(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            XDocument? Part(string path)
            {
                var entry = zip.GetEntry(path);
                if (entry is null) return null;
                if (entry.Length > MaxPartBytes || (total += entry.Length) > MaxTotalBytes) throw new XlsxFormatException("File Excel quá lớn.");
                using var s = entry.Open();
                using var xml = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                return XDocument.Load(xml);
            }

            var workbook = Part("xl/workbook.xml") ?? throw new XlsxFormatException("Không phải file Excel .xlsx.");
            var targets = (Part("xl/_rels/workbook.xml.rels")?.Root?.Elements(PackageRel + "Relationship") ?? [])
                .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!);
            var shared = (Part("xl/sharedStrings.xml")?.Root?.Elements(Main + "si") ?? []).Select(InlineText).ToList();
            var dateStyles = DateStyles(Part("xl/styles.xml"));

            var sheets = new List<XlsxSheet>();
            foreach (var sheet in workbook.Root!.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
            {
                var name = (string?)sheet.Attribute("name") ?? "";
                if (sheet.Attribute(Rel + "id") is not { } rid || !targets.TryGetValue(rid.Value, out var target)) continue;
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                if (Part(path) is not { } doc) continue;
                var cells = new Dictionary<(int, int), XlsxCell>();
                foreach (var c in doc.Descendants(Main + "c"))
                {
                    if (Address((string?)c.Attribute("r")) is not { } at) continue;
                    var type = (string?)c.Attribute("t");
                    var raw = (string?)c.Element(Main + "v");
                    XlsxCell? cell = type switch
                    {
                        "s" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < shared.Count => new(shared[i], null, false),
                        "inlineStr" => new(InlineText(c.Element(Main + "is")), null, false),
                        "str" or "e" => raw is null ? null : new(raw, null, false),
                        "b" => raw is null ? null : new(raw == "1" ? "TRUE" : "FALSE", null, false),
                        _ when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) =>
                            new(n.ToString("0.###############", CultureInfo.InvariantCulture), n, int.TryParse((string?)c.Attribute("s"), out var st) && dateStyles.Contains(st)),
                        _ => null,
                    };
                    if (cell is not null && cell.Text.Length > 0) cells[at] = cell;
                }
                sheets.Add(new XlsxSheet(name, cells));
            }
            return sheets;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or InvalidOperationException or ArgumentException or OverflowException or FormatException)
        {
            throw new XlsxFormatException("Không đọc được file Excel (.xlsx). Hãy lưu lại file bằng Excel rồi thử lại.");
        }
    }

    private static string InlineText(XElement? e) => e is null ? "" : string.Concat(e.Descendants(Main + "t").Where(t => t.Parent?.Name != Main + "rPh").Select(t => t.Value));

    // "B12" → (12, 2).
    internal static (int Row, int Col)? Address(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        int col = 0, i = 0;
        for (; i < reference.Length && char.IsAsciiLetter(reference[i]); i++) col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
        return col > 0 && int.TryParse(reference.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out var row) && row > 0 ? (row, col) : null;
    }

    // Style indexes (cellXfs order) whose number format shows a date.
    private static HashSet<int> DateStyles(XDocument? styles)
    {
        var result = new HashSet<int>();
        if (styles?.Root is not { } root) return result;
        var custom = (root.Element(Main + "numFmts")?.Elements(Main + "numFmt") ?? [])
            .Where(f => int.TryParse((string?)f.Attribute("numFmtId"), out _))
            .ToDictionary(f => int.Parse((string)f.Attribute("numFmtId")!, CultureInfo.InvariantCulture), f => (string?)f.Attribute("formatCode") ?? "");
        var index = 0;
        foreach (var xf in root.Element(Main + "cellXfs")?.Elements(Main + "xf") ?? [])
        {
            if (int.TryParse((string?)xf.Attribute("numFmtId"), out var id) && IsDateFormat(id, custom.GetValueOrDefault(id))) result.Add(index);
            index++;
        }
        return result;
    }

    internal static bool IsDateFormat(int id, string? code)
    {
        if (id is >= 14 and <= 22 or >= 45 and <= 47) return true;
        if (string.IsNullOrEmpty(code)) return false;
        var plain = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]|\\\\.", "");
        return plain.IndexOfAny(['d', 'D', 'y', 'Y']) >= 0 || (plain.Contains('m', StringComparison.OrdinalIgnoreCase) && !plain.Contains('0') && !plain.Contains('#'));
    }
}
