using System.Text.Json;
using System.Text.Json.Nodes;

namespace TAWebsite.Core.Content;

// Read access to the content of one section (or one item of a list field). Missing keys read as empty strings so a view
// never fails on content an editor cleared.
public sealed class SectionContent(JsonObject data)
{
    public JsonObject Data { get; } = data;

    public string this[string key] => Text(key);

    public string Text(string key) => Data[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public bool Has(string key) => !string.IsNullOrWhiteSpace(Text(key));

    public IReadOnlyList<string> Lines(string key) =>
        Text(key).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public IReadOnlyList<SectionContent> List(string key) =>
        Data[key] is JsonArray a ? a.OfType<JsonObject>().Select(o => new SectionContent(o)).ToList() : [];

    // Saved content replaces the defaults field by field (a list is replaced as a whole). Unknown keys are dropped.
    public static JsonObject Merge(SectionType type, JsonObject defaults, JsonObject? saved)
    {
        var result = new JsonObject();
        foreach (var field in type.Fields)
        {
            var node = saved is not null && saved.ContainsKey(field.Key) ? saved[field.Key] : defaults[field.Key];
            if (node is not null) result[field.Key] = node.DeepClone();
        }
        return result;
    }

    public static JsonObject? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keeps Vietnamese readable in SQL
        WriteIndented = false
    };
}

// An image reference stored in content: "asset:<file>" (embedded default image) or "media:<guid>" (dbo.WebsiteMedia).
public static class ImageRef
{
    public const string AssetPrefix = "asset:";
    public const string MediaPrefix = "media:";

    public static Guid? MediaId(string? value) =>
        value is not null && value.StartsWith(MediaPrefix, StringComparison.Ordinal) && Guid.TryParse(value[MediaPrefix.Length..], out var id) ? id : null;

    public static string? AssetName(string? value) =>
        value is not null && value.StartsWith(AssetPrefix, StringComparison.Ordinal) && SiteAssets.Exists(value[AssetPrefix.Length..]) ? value[AssetPrefix.Length..] : null;

    // mediaBase: "/media" on the public site, "/WebsiteMedia/File" in the Admin portal.
    public static string Url(string? value, string mediaBase) =>
        MediaId(value) is Guid id ? $"{mediaBase}/{id:N}" : AssetName(value) is string name ? $"{SiteAssets.PathPrefix}/{name}" : "";

    public static bool IsValid(string? value) => string.IsNullOrEmpty(value) || MediaId(value) is not null || AssetName(value) is not null;
}

// Links typed by editors: site paths, anchors, tel:/mailto:/zalo and http(s) links only (never javascript: or data:).
public static class SafeUrl
{
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal)) return false;
        if (url.StartsWith('/') || url.StartsWith('#') || url.StartsWith('?')) return true;
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    public static string Or(string? url, string fallback = "#") => IsAllowed(url) && !string.IsNullOrWhiteSpace(url) ? url.Trim() : fallback;
}
