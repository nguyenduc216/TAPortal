using System.Reflection;
using System.Text.Json;

namespace TAPortal.Web.Services;

// Ready-written articles shipped with the Portal (Content/post-library/*.json, embedded). Staff add them to the website as
// drafts from "Bài viết → Bài soạn sẵn", review, then publish; nothing reaches the public site without that review.
// Sources and Uncertain are for the reviewer only (shown on the library page, never saved to the article).
public sealed record LibraryPost(int Order, string Slug, string Title, string Summary, string Category, string Tags, string SeoTitle, string SeoDescription,
    string BodyHtml, IReadOnlyList<string>? Sources = null, IReadOnlyList<string>? Uncertain = null,
    byte SourceKind = 0, string? SourceName = null, string? SourceUrl = null, string? SourceAuthor = null, DateTime? SourcePublishedOn = null)
{
    /// <summary>The source shown on the website ("Tổng hợp từ …"); null for T.A's own articles.</summary>
    public TAWebsite.Core.Data.PostSource? Origin => TAWebsite.Core.Data.PostSource.From(SourceKind, SourceName, SourceUrl, SourceAuthor, SourcePublishedOn);
}

public static class PostLibrary
{
    public const string Author = "Ban chuyên môn T.A";
    private const string ResourcePrefix = "TAPortal.postlib.";

    public static readonly IReadOnlyList<(string Slug, string Name)> Categories =
    [
        ("ke-toan-doanh-nghiep", "Kế toán doanh nghiệp"),
        ("thue", "Thuế"),
        ("hoa-don-dien-tu", "Hóa đơn điện tử"),
        ("ho-kinh-doanh", "Hộ kinh doanh"),
        ("doanh-nghiep-moi", "Doanh nghiệp mới")
    ];

    private static readonly Lazy<IReadOnlyList<LibraryPost>> Posts = new(Load);

    public static IReadOnlyList<LibraryPost> All => Posts.Value;

    public static string CategoryName(string slug) => Categories.FirstOrDefault(c => c.Slug == slug).Name ?? slug;

    public static LibraryPost? Find(string? slug) => All.FirstOrDefault(p => p.Slug == slug);

    /// <summary>The cover photo shipped for the article (JPEG 960x540), or null.</summary>
    public static byte[]? Cover(int order)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"TAPortal.postcover.{order:00}.jpg");
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Publication times spread over the last <paramref name="days"/> days (Vietnam time): working days, office hours, in the
    /// past, and never before the day after the original article for a cited summary. Deterministic for the same input.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTime> Schedule(IReadOnlyList<LibraryPost> posts, DateTime nowVn, int days = 45, int seed = 20261117)
    {
        var random = new Random(seed);
        var end = nowVn.AddHours(-2);
        var start = nowVn.Date.AddDays(-days);
        var workdays = Enumerable.Range(0, days + 1).Select(i => start.AddDays(i))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        DateTime At(DateTime day) => day.AddMinutes(7 * 60 + 45 + random.Next(0, 9 * 60 + 30));   // 07:45 – 17:15
        var slots = posts.Select(_ => At(workdays[random.Next(workdays.Count)])).Where(t => t <= end).ToList();
        while (slots.Count < posts.Count) slots.Add(At(workdays[random.Next(workdays.Count / 2)]));
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        // Cited summaries with the latest original first, so they get the late slots they need.
        foreach (var post in posts.OrderByDescending(p => p.Origin?.PublishedOn ?? DateTime.MinValue).ThenBy(p => p.Order))
        {
            var earliest = post.Origin?.PublishedOn?.Date.AddDays(1) ?? DateTime.MinValue;
            var fitting = slots.Where(s => s >= earliest).ToList();
            DateTime slot;
            if (fitting.Count > 0) { slot = fitting[random.Next(fitting.Count)]; slots.Remove(slot); }
            else { slot = earliest.AddHours(9).AddMinutes(random.Next(0, 240)); if (slot > end) slot = end; slots.RemoveAt(0); }
            result[post.Slug] = slot;
        }
        return result;
    }

    /// <summary>"Tên nguồn – https://…" → (label, url); url is null unless it is an http(s) link.</summary>
    public static (string Label, string? Url) SplitSource(string source)
    {
        var at = source.LastIndexOf(" – http", StringComparison.Ordinal);
        if (at < 0) at = source.LastIndexOf(" - http", StringComparison.Ordinal);
        if (at < 0) return (source, null);
        var url = source[(at + 3)..].Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? (source[..at].Trim(), url) : (source, null);
    }

    public static string Host(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : "";

    private static IReadOnlyList<LibraryPost> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = new List<LibraryPost>();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            list.Add(JsonSerializer.Deserialize<LibraryPost>(stream, options) ?? throw new InvalidDataException(name));
        }
        return list.OrderBy(p => p.Order).ToList();
    }
}
