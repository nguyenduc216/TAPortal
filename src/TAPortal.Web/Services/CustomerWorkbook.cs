using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — small standards-based XLSX using existing BCL, no new package or formulas.
public static class CustomerWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — serialize all external values as inline text, preserving leading zeros and preventing formula injection.
    public static byte[] Create(IEnumerable<CustomerRow> customers)
    {
        var data = new XElement(Ns + "sheetData");
        var headings = new[] { "Mã KH", "Tên khách hàng", "MST", "Email", "Điện thoại", "Trạng thái" };
        data.Add(Row(1, headings, true));
        var index = 2;
        foreach (var customer in customers)
        {
            if (index > 1_048_576) throw new InvalidOperationException("Too many export rows.");
            data.Add(Row(index++, new[] { customer.Code, customer.Name, customer.TaxCode, customer.Email, customer.Phone, customer.Status }, false));
        }
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml", """
<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
""");
            Add(zip, "_rels/.rels", """
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
""");
            Add(zip, "xl/workbook.xml", """
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Khách hàng" sheetId="1" r:id="rId1"/></sheets></workbook>
""");
            Add(zip, "xl/_rels/workbook.xml.rels", """
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
""");
            Add(zip, "xl/styles.xml", """
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF07345C"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="49" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="49" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
""");
            var sheet = new XElement(Ns + "worksheet",
                new XElement(Ns + "sheetViews", new XElement(Ns + "sheetView", new XAttribute("workbookViewId", 0), new XElement(Ns + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(Ns + "cols", Enumerable.Range(1, 6).Select(i => new XElement(Ns + "col", new XAttribute("min", i), new XAttribute("max", i), new XAttribute("width", i is 2 or 4 ? 34 : 22), new XAttribute("customWidth", 1)))),
                data, new XElement(Ns + "autoFilter", new XAttribute("ref", $"A1:F{index - 1}")));
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString(SaveOptions.DisableFormatting));
        }
        return stream.ToArray();
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — remove XML-illegal controls without interpreting user input as markup.
    private static XElement Row(int number, IEnumerable<string?> values, bool header) => new(Ns + "row", new XAttribute("r", number), values.Select((value, column) =>
        new XElement(Ns + "c", new XAttribute("r", $"{(char)('A' + column)}{number}"), new XAttribute("t", "inlineStr"), new XAttribute("s", header ? 1 : 0),
            new XElement(Ns + "is", new XElement(Ns + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), new string((value ?? "").Where(XmlConvert.IsXmlChar).ToArray()))))));
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — write UTF-8 OpenXML parts into the downloadable package.
    private static void Add(ZipArchive zip, string path, string value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(value);
    }
}
