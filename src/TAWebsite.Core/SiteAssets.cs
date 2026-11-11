using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TAWebsite.Core;

// Default images of the mockups, embedded in this assembly and served by both apps at /site-assets/{file}
// (content value "asset:<file>"). Long cache: the files only change with a new build.
public static class SiteAssets
{
    public const string PathPrefix = "/site-assets";
    private const string ResourcePrefix = "TAWebsite.assets.";

    private static readonly Dictionary<string, byte[]> Files = Load();

    public static IReadOnlyCollection<string> Names => Files.Keys;

    public static bool Exists(string name) => Files.ContainsKey(name);

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };

    public static IEndpointConventionBuilder MapSiteAssets(this IEndpointRouteBuilder app) =>
        app.MapGet(PathPrefix + "/{name}", (string name, HttpContext context) =>
        {
            if (!Files.TryGetValue(name, out var bytes)) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=604800";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Bytes(bytes, ContentType(name));
        }).AllowAnonymous();

    private static Dictionary<string, byte[]> Load()
    {
        var asm = typeof(SiteAssets).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var resource in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(resource)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            files[resource[ResourcePrefix.Length..]] = ms.ToArray();
        }
        return files;
    }
}
