using TAWebsite.Core.Content;

namespace TAWebsite.Core.Data;

public sealed record SectionRow(string PageKey, string SectionKey, string? ContentJson, int SortOrder, bool IsVisible, DateTime UpdatedAt, byte[]? Stamp = null);

public sealed record PageRow(string PageKey, string? SeoTitle, string? SeoDescription, bool IsPublished, DateTime UpdatedAt, byte[]? Stamp = null);

public sealed record RenderedSection(SectionDef Def, SectionContent Content, bool IsVisible, int Order, bool IsEdited, DateTime? UpdatedAt);

public sealed record RenderedPage(PageDef Def, string Title, string Description, bool IsPublished, IReadOnlyList<RenderedSection> Sections)
{
    public SectionContent? Section(string key) => Sections.FirstOrDefault(s => s.Def.Key == key && s.IsVisible)?.Content;
}

// Combines the code-declared page (defaults) with the edited rows: saved content over defaults, saved order/visibility.
public static class PageComposer
{
    public static RenderedPage Compose(PageDef def, PageRow? page, IEnumerable<SectionRow> rows)
    {
        var byKey = rows.Where(r => r.PageKey == def.Key).ToDictionary(r => r.SectionKey, StringComparer.Ordinal);
        var sections = def.Sections.Select(s =>
        {
            byKey.TryGetValue(s.Key, out var row);
            var content = SectionContent.Merge(s.SectionType, s.Defaults, SectionContent.Parse(row?.ContentJson));
            return new RenderedSection(s, new SectionContent(content), row?.IsVisible ?? true, row?.SortOrder ?? s.Order, row?.ContentJson is not null, row?.UpdatedAt);
        }).OrderBy(s => s.Order).ThenBy(s => s.Def.Order).ToList();
        return new(def, string.IsNullOrWhiteSpace(page?.SeoTitle) ? def.Title : page!.SeoTitle!.Trim(),
            string.IsNullOrWhiteSpace(page?.SeoDescription) ? def.Description : page!.SeoDescription!.Trim(), page?.IsPublished ?? true, sections);
    }
}

public sealed record PostCategory(Guid Id, string Slug, string Name, int SortOrder, int PostCount = 0);

public sealed record PostCard(Guid Id, string Slug, string Title, string? Summary, Guid? CoverMediaId, string? CategorySlug, string? CategoryName,
    DateTime? PublishedAt, int ReadMinutes, bool IsFeatured, int ViewCount);

public sealed record PostDetail(Guid Id, string Slug, string Title, string? Summary, string BodyHtml, Guid? CoverMediaId, string? CoverAlt,
    Guid? CategoryId, string? CategorySlug, string? CategoryName, string? AuthorName, string? Tags, DateTime? PublishedAt, DateTime UpdatedAt,
    string? SeoTitle, string? SeoDescription, int ViewCount, PostSource? Source = null)
{
    public IReadOnlyList<string> TagList => (Tags ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
}

/// <summary>Where an article comes from (WebsitePosts.Source*, migration 064). Kind 0 = T.A's own article (no source box).</summary>
public sealed record PostSource(byte Kind, string? Name, string? Url, string? Author, DateTime? PublishedOn)
{
    public const byte Own = 0, Summary = 1, Republished = 2;

    public static readonly IReadOnlyList<(byte Kind, string Label, string Hint)> Kinds =
    [
        (Own, "Tự biên soạn", "T.A tự viết. Không hiện khung nguồn."),
        (Summary, "Tóm tắt có trích nguồn", "T.A tóm tắt bằng lời của mình, trích nguyên văn ngắn trong ngoặc kép. Website hiện “Tổng hợp từ …” và liên kết bài gốc."),
        (Republished, "Đăng lại có cho phép", "Chỉ khi nơi đăng gốc đã đồng ý bằng văn bản cho T.A đăng lại. Website ghi rõ đăng lại và liên kết bài gốc.")
    ];

    public bool HasSource => Kind != Own && !string.IsNullOrWhiteSpace(Name);
    public string Label => Kinds.FirstOrDefault(k => k.Kind == Kind).Label ?? Kinds[0].Label;

    /// <summary>The original link when it is a safe http(s) URL; otherwise null (never rendered).</summary>
    public string? SafeUrl => IsHttpUrl(Url) ? Url : null;

    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    public static PostSource? From(byte? kind, string? name, string? url, string? author, DateTime? publishedOn) =>
        kind is null or Own ? null : new PostSource(kind.Value, name, url, author, publishedOn);
}

public sealed record PostQuery(string? CategorySlug, string? Search, int Page, int PageSize = 9);

public sealed record PostPage(IReadOnlyList<PostCard> Items, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record MediaFile(Guid Id, string ContentType, byte[] Data, string Sha256, string FileName = "", string? ExternalUrl = null)
{
    /// <summary>A YouTube video: no bytes are stored, the site redirects / embeds <see cref="ExternalUrl"/>.</summary>
    public bool IsLink => ContentType == MediaKinds.YouTube;
}

public sealed class LeadInput
{
    public string FullName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? CompanyName { get; set; }
    public string? Services { get; set; }
    public string? CompanyType { get; set; }
    public string? Scale { get; set; }
    public string? ContactTime { get; set; }
    public string? Message { get; set; }
    public string? SourcePage { get; set; }
}

public enum LeadOutcome { Saved, TooMany, Unavailable }

public static class LeadStatus
{
    public static readonly IReadOnlyList<(byte Code, string Name, string Css)> All =
    [
        (0, "Mới", "bg-blue-lt"), (1, "Đang liên hệ", "bg-yellow-lt"), (2, "Đã báo giá", "bg-purple-lt"),
        (3, "Đã chốt", "bg-green-lt"), (4, "Không phù hợp", "bg-secondary-lt")
    ];

    public static string Name(byte code) => All.FirstOrDefault(s => s.Code == code).Name ?? "?";
}
