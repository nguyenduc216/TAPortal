using System.Text.Json;
using System.Text.Json.Nodes;

namespace TAWebsite.Core.Content;

public sealed record SectionDef(string Key, string Type, string Name, int Order, JsonObject Defaults)
{
    public SectionType SectionType => SectionTypes.Find(Type) ?? throw new InvalidOperationException($"Unknown section type {Type}");
}

public sealed record PageDef(string Key, string? Path, string Name, string Title, string Description, IReadOnlyList<SectionDef> Sections)
{
    public string MainClass { get; init; } = "";             // class of <main> (e.g. "dv-main" for the service pages)
    public string Nav { get; init; } = "";                   // highlighted menu item
    public IReadOnlyList<string> Styles { get; init; } = []; // extra stylesheets of the page (site paths)
    public IReadOnlyList<string> Scripts { get; init; } = [];

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
        var asm = typeof(SiteMap).Assembly;
        var pages = new List<PageDef>();
        // site.json (global, home, contact, news) first, then one file per page (Content/pages/*.json).
        var names = asm.GetManifestResourceNames().Where(n => n == "TAWebsite.site.json" || n.StartsWith("TAWebsite.pages.", StringComparison.Ordinal))
            .OrderBy(n => n == "TAWebsite.site.json" ? "" : n, StringComparer.Ordinal);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            var root = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!.AsObject();
            IEnumerable<JsonObject> list = root["pages"] is JsonArray array ? array.Select(p => p!.AsObject()) : [root];
            foreach (var p in list) pages.Add(Read(p));
        }
        if (pages.Select(p => p.Key).Distinct().Count() != pages.Count) throw new InvalidOperationException("Duplicate page key in site definitions");
        return pages;
    }

    private static PageDef Read(JsonObject p)
    {
        var sections = new List<SectionDef>();
        var order = 0;
        foreach (var s in p["sections"]!.AsArray())
        {
            var type = s!["type"]!.GetValue<string>();
            if (SectionTypes.Find(type) is null) throw new InvalidOperationException($"site definition: unknown section type {type}");
            sections.Add(new(s["key"]!.GetValue<string>(), type, s["name"]!.GetValue<string>(), order += 10,
                s["content"]?.DeepClone().AsObject() ?? new JsonObject()));
        }
        static IReadOnlyList<string> Strings(JsonNode? n) => n is JsonArray a ? a.Select(x => x!.GetValue<string>()).ToList() : [];
        return new(p["key"]!.GetValue<string>(), p["path"]?.GetValue<string>(), p["name"]!.GetValue<string>(),
            p["title"]!.GetValue<string>(), p["description"]!.GetValue<string>(), sections)
        {
            MainClass = p["main"]?.GetValue<string>() ?? "",
            Nav = p["nav"]?.GetValue<string>() ?? p["key"]!.GetValue<string>(),
            Styles = Strings(p["styles"]),
            Scripts = Strings(p["scripts"])
        };
    }

    public static PageDef? FindByPath(string path)
    {
        var p = path.Length > 1 ? path.TrimEnd('/') : path;
        return Pages.FirstOrDefault(x => x.Path is not null && string.Equals(x.Path, p, StringComparison.OrdinalIgnoreCase));
    }
}
