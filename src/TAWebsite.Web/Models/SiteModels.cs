using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using TAWebsite.Core.Content;
using TAWebsite.Core.Data;

namespace TAWebsite.Web.Models;

// Everything the layout needs on every page: settings (header, footer contact column, map), the shared footer section and
// the current navigation item.
public sealed record SiteContext(SiteSettings Settings, SectionContent Footer, string Nav, string Title, string Description, string CanonicalPath,
    string? OgImage = null, string? JsonLd = null)
{
    public const string MediaBase = "/media";

    public static string Img(string? value) => ImageRef.Url(value, MediaBase);

    public static string Img(Guid? id) => id is Guid g ? $"{MediaBase}/{g:N}" : TAWebsite.Core.SiteAssets.PathPrefix + "/service-1.webp";

    public static string Link(string? url, string fallback = "#") => SafeUrl.Or(url, fallback);
}

public sealed record SectionVm(RenderedSection Section, SiteContext Site, IReadOnlyList<PostCard> Posts)
{
    public SectionContent C => Section.Content;
    public string Key => Section.Def.Key;
}

public sealed record PageVm(RenderedPage Page, SiteContext Site, IReadOnlyList<PostCard> Posts, string? Preselect = null);

public sealed record NewsVm(RenderedPage Page, SiteContext Site, PostPage Result, IReadOnlyList<PostCategory> Categories, string? Category,
    string? Search, IReadOnlyList<PostCard> Featured, IReadOnlyList<PostCard> Popular);

public sealed record PostVm(PostDetail Post, string BodyHtml, IReadOnlyList<(string Id, string Title)> Toc, SiteContext Site,
    IReadOnlyList<PostCard> Related, PostCard? Previous, PostCard? Next, SectionContent Sidebar);

public static class SiteHtml
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

    // Encoded text with line breaks kept (titles typed on several lines).
    public static IHtmlContent Br(string? text) =>
        new HtmlString(string.Join("<br/>", (text ?? "").Replace("\r", "").Split('\n').Select(l => Encoder.Encode(l))));

    // "Nhận đề xuất & báo giá" → the part after "&" in <em> like the mockup headings; otherwise the plain text.
    public static IHtmlContent Heading(string? text)
    {
        var t = text ?? "";
        var i = t.IndexOf(" & ", StringComparison.Ordinal);
        if (i < 0) return new HtmlString(Encoder.Encode(t));
        var rest = t[(i + 3)..];
        var space = rest.IndexOf(' ');
        var em = space < 0 ? rest : rest[..space];
        var tail = space < 0 ? "" : rest[space..];
        return new HtmlString(Encoder.Encode(t[..i]) + " &amp; <em>" + Encoder.Encode(em) + "</em>" + Encoder.Encode(tail));
    }

    private static readonly System.Globalization.CultureInfo Vi = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

    public static string Date(DateTime? utc) => utc is DateTime d ? (d + TimeSpan.FromHours(7)).ToString("dd/MM/yyyy", Vi) : "";

    public static string PostUrl(PostCard p) => "/tin-tuc/" + p.Slug;
}
