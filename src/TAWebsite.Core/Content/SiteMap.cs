using System.Text.Json;
using System.Text.Json.Nodes;

namespace TAWebsite.Core.Content;

public sealed record SectionDef(string Key, string Type, string Name, int Order, JsonObject Defaults)
{
    public SectionType SectionType => SectionTypes.Find(Type) ?? throw new InvalidOperationException($"Unknown section type {Type}");
}

public sealed record PageDef(string Key, string? Path, string Name, string Title, string Description, IReadOnlyList<SectionDef> Sections)
{
    public SectionDef? Section(string key) => Sections.FirstOrDefault(s => s.Key == key);
}

// Pages of the public site and their sections, with the default content taken from the approved mockups (Content/site.json,
// embedded). The database (dbo.WebsiteSections) only holds what an editor changed, so adding a page or a section is a code
// change and needs no seed data.
public static class SiteMap
{
    public const string GlobalPage = "global";

    public static IReadOnlyList<PageDef> Pages { get; } = Load();

    public static PageDef? Find(string key) => Pages.FirstOrDefault(p => p.Key == key);

    public static PageDef Get(string key) => Find(key) ?? throw new InvalidOperationException($"Unknown page {key}");

    private static IReadOnlyList<PageDef> Load()
    {
        using var stream = typeof(SiteMap).Assembly.GetManifestResourceStream("TAWebsite.site.json")
            ?? throw new InvalidOperationException("Embedded resource TAWebsite.site.json missing");
        var root = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
        var pages = new List<PageDef>();
        foreach (var p in root["pages"]!.AsArray())
        {
            var sections = new List<SectionDef>();
            var order = 0;
            foreach (var s in p!["sections"]!.AsArray())
            {
                var type = s!["type"]!.GetValue<string>();
                if (SectionTypes.Find(type) is null) throw new InvalidOperationException($"site.json: unknown section type {type}");
                sections.Add(new(s["key"]!.GetValue<string>(), type, s["name"]!.GetValue<string>(), order += 10,
                    s["content"]?.DeepClone().AsObject() ?? new JsonObject()));
            }
            pages.Add(new(p["key"]!.GetValue<string>(), p["path"]?.GetValue<string>(), p["name"]!.GetValue<string>(),
                p["title"]!.GetValue<string>(), p["description"]!.GetValue<string>(), sections));
        }
        return pages;
    }
}
