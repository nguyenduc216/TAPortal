using System.IO.Compression;
using System.Security;
using System.Text;

namespace TAPortal.Web.Services;

// Minimal .xlsx writer (one sheet, text cells as inline strings, bold frozen header row, autofilter, column widths)
// without a third-party package. Cell text that starts with = + - @ is written as text, never as a formula, and XML-invalid
// control characters are dropped.
public static class XlsxWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows, IReadOnlyList<double>? widths = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Part(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Part(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Part(zip, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Part(zip, "xl/workbook.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Esc(SheetName(sheetName))}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Part(zip, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFFFF3C4"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="3"><xf/><xf fontId="1" fillId="2" applyFont="1" applyFill="1"/><xf applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf></cellXfs></styleSheet>""");

            var sheet = new StringBuilder();
            sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
            sheet.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
            if (widths is { Count: > 0 })
            {
                sheet.Append("<cols>");
                for (var i = 0; i < widths.Count; i++) sheet.Append($"""<col min="{i + 1}" max="{i + 1}" width="{widths[i].ToString(System.Globalization.CultureInfo.InvariantCulture)}" customWidth="1"/>""");
                sheet.Append("</cols>");
            }
            sheet.Append("<sheetData>");
            var rowNo = 1;
            Row(sheet, rowNo, headers, 1);
            foreach (var row in rows) Row(sheet, ++rowNo, row, 2);
            sheet.Append("</sheetData>");
            sheet.Append($"""<autoFilter ref="A1:{Column(headers.Count - 1)}{rowNo}"/></worksheet>""");
            Part(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    private static void Row(StringBuilder sb, int rowNo, IReadOnlyList<string?> cells, int style)
    {
        sb.Append($"""<row r="{rowNo}">""");
        for (var i = 0; i < cells.Count; i++)
        {
            var text = Clean(cells[i]);
            if (text.Length == 0) continue;
            sb.Append($"""<c r="{Column(i)}{rowNo}" t="inlineStr" s="{style}"><is><t xml:space="preserve">{Esc(text)}</t></is></c>""");
        }
        sb.Append("</row>");
    }

    public static string Column(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Replace("\r\n", "\n"))
            if (ch is '\n' or '\t' || ch >= ' ' && ch != '￾' && ch != '￿') sb.Append(ch);
        var text = sb.ToString();
        return text.Length > 32_000 ? text[..32_000] : text;
    }

    private static string SheetName(string name)
    {
        var cleaned = new string(name.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray()).Trim();
        return cleaned.Length == 0 ? "Sheet1" : cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string Esc(string value) => SecurityElement.Escape(value) ?? "";

    private static void Part(ZipArchive zip, string name, string xml)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        stream.Write(bytes);
    }
}
