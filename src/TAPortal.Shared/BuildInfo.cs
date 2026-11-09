using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TAPortal.Shared;

/// <summary>
/// Identity of the running build, for the connection notice (wwwroot/js/ta-update.js, like Blazor's "reconnecting" box):
/// the layout renders it in &lt;meta name="ta-build"&gt; and the page polls GET /_build. When the server stops answering
/// (deploy, restart, network) a small "Đang kết nối lại" box appears; when it answers with another build the page reloads.
/// </summary>
public static class BuildInfo
{
    public const string Path = "/_build";

    /// <summary>The web app's module version id: new on every build, the same for every request of one build.</summary>
    public static string Id { get; } = (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly).ManifestModule.ModuleVersionId.ToString("N")[..12];

    public static IEndpointConventionBuilder MapBuildInfo(this IEndpointRouteBuilder app) =>
        app.MapGet(Path, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return Results.Json(new { build = Id });
        }).AllowAnonymous();
}
