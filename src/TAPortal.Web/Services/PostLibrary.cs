using System.Reflection;
using System.Text.Json;

namespace TAPortal.Web.Services;

// Ready-written articles shipped with the Portal (Content/post-library/*.json, embedded). Staff add them to the website as
// drafts from "Bài viết → Bài soạn sẵn", review, then publish; nothing reaches the public site without that review.
// Sources and Uncertain are for the reviewer only (shown on the library page, never saved to the article).
public sealed record LibraryPost(int Order, string Slug, string Title, string Summary, string Category, string Tags, string SeoTitle, string SeoDescription,
    string BodyHtml, IReadOnlyList<string>? Sources = null, IReadOnlyList<string>? Uncertain = null);

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
