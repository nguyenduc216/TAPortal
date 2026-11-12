using System.Globalization;
using System.Text;
using Ganss.Xss;

namespace TAWebsite.Core.Content;

public static class Slug
{
    // "Doanh nghiệp mới thành lập?" -> "doanh-nghiep-moi-thanh-lap" (a-z, 0-9 and single hyphens; matches CK_WPo_Slug).
    public static string From(string? text, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = text.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var dash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            if (sb.Length >= max) break;
        }
        return sb.ToString().Trim('-');
    }

    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= 200 && slug.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !slug.StartsWith('-') && !slug.EndsWith('-') && !slug.Contains("--");
}

// Article bodies are written by staff in the Admin portal; they are still sanitized (allow-list) before storage and before
// rendering, so a pasted script, event handler or javascript: link can never run on the public site.
public static class PostHtml
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "p", "br", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "a", "ul", "ol", "li", "blockquote",
                     "table", "thead", "tbody", "tr", "th", "td", "img", "figure", "figcaption", "hr", "span", "div", "sup", "sub" })
            s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        foreach (var a in new[] { "href", "title", "src", "alt", "width", "height", "colspan", "rowspan", "id", "class", "target", "rel" })
            s.AllowedAttributes.Add(a);
        s.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto", "tel" }) s.AllowedSchemes.Add(scheme);
        s.AllowedCssProperties.Clear();
        s.AllowedClasses.Clear();
        foreach (var c in new[] { "ar-callout", "info", "warn", "ok", "lead" }) s.AllowedClasses.Add(c);
        s.AllowDataAttributes = false;
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a && a.GetAttribute("target") == "_blank") a.SetAttribute("rel", "noopener");
            if (e.Node is AngleSharp.Html.Dom.IHtmlImageElement img) img.SetAttribute("loading", "lazy");
        };
        return s;
    }

    public static string Sanitize(string? html) => string.IsNullOrWhiteSpace(html) ? "" : Sanitizer.Sanitize(html);

    // Whole page sections (rich-html): the mockup markup with its classes, ids, ARIA attributes, inline SVG icons and the
    // small inline styles of the design. Still no script, iframe, form, event handler or javascript:/data: link.
    private static readonly HtmlSanitizer SectionSanitizer = CreateSection();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> SectionCache = new();

    private static HtmlSanitizer CreateSection()
    {
        var s = new HtmlSanitizer();
        foreach (var t in new[] { "section", "article", "aside", "header", "footer", "nav", "main", "figure", "figcaption", "details", "summary", "small",
                     "mark", "time", "dl", "dt", "dd", "caption", "colgroup", "col", "tfoot", "button", "label", "input", "picture", "source",
                     "svg", "g", "path", "circle", "rect", "line", "polyline", "polygon", "ellipse", "defs", "lineargradient", "radialgradient", "stop", "use", "symbol", "title" })
            s.AllowedTags.Add(t);
        s.AllowedTags.Remove("form");
        foreach (var a in new[] { "class", "id", "role", "hidden", "tabindex", "type", "checked", "disabled", "value", "for", "name", "loading", "fetchpriority",
                     "viewbox", "xmlns", "fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-dasharray", "stroke-dashoffset", "d",
                     "cx", "cy", "r", "rx", "ry", "x", "y", "x1", "y1", "x2", "y2", "points", "transform", "opacity", "offset", "stop-color", "stop-opacity",
                     "gradientunits", "fill-rule", "clip-rule", "preserveaspectratio", "focusable", "open", "srcset", "sizes", "media", "width", "height" })
            s.AllowedAttributes.Add(a);
        s.AllowDataAttributes = true;
        s.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto", "tel" }) s.AllowedSchemes.Add(scheme);
        s.RemovingAttribute += (_, e) =>
        {
            // ARIA attributes are not in the default allow-list.
            if (e.Attribute.Name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a && a.GetAttribute("target") == "_blank") a.SetAttribute("rel", "noopener");
        };
        return s;
    }

    public static string SanitizeSection(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        if (SectionCache.TryGetValue(html, out var cached)) return cached;
        var clean = SectionSanitizer.Sanitize(html);
        if (SectionCache.Count > 500) SectionCache.Clear();
        SectionCache[html] = clean;
        return clean;
    }

    public static string PlainText(string? html, int max = 300)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ")).Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    // About 200 words a minute; at least 1.
    public static int ReadMinutes(string? html) =>
        Math.Max(1, (int)Math.Round(PlainText(html, int.MaxValue).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / 200.0));

    // h2 headings of the body for the table of contents; gives each an id when it has none.
    public static (string Html, IReadOnlyList<(string Id, string Title)> Toc) WithToc(string html)
    {
        var toc = new List<(string, string)>();
        var n = 0;
        var result = System.Text.RegularExpressions.Regex.Replace(html, @"<h2(\s[^>]*)?>(.*?)</h2>", m =>
        {
            var title = PlainText(m.Groups[2].Value, 200);
            var idMatch = System.Text.RegularExpressions.Regex.Match(m.Groups[1].Value, "id=\"([^\"]+)\"");
            var id = idMatch.Success ? idMatch.Groups[1].Value : $"muc-{++n}";
            toc.Add((id, title));
            return idMatch.Success ? m.Value : $"<h2 id=\"{id}\"{m.Groups[1].Value}>{m.Groups[2].Value}</h2>";
        }, System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return (result, toc);
    }
}
