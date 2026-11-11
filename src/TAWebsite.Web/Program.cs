using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.WebEncoders;
using TAWebsite.Core;
using TAWebsite.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
// Vietnamese text is written as-is (not as &#x...; entities): smaller pages, readable source.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IWebsiteReader, SqlWebsiteReader>();
builder.Services.AddAntiforgery(o => { o.Cookie.Name = "ta-web-af"; o.HeaderName = "X-CSRF-TOKEN"; });
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // IIS (ANCM in-process) already provides the client address; behind a reverse proxy the operator sets KnownProxies.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration["Database:ConnectionString"]))
    app.Logger.LogWarning("WEBSITE_DB_NOT_CONFIGURED: Database:ConnectionString is empty; the site shows its default content and cannot save contact requests");

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/loi");
    app.UseHsts();
}
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h.XFrameOptions = "SAMEORIGIN";
        // Pages always revalidate, so an edit saved in TA Portal shows on the next load (images and CSS keep their own cache).
        if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            h.CacheControl = "no-cache, max-age=0";
        return Task.CompletedTask;
    });
    return next(context);
});
app.UseStatusCodePagesWithReExecute("/khong-tim-thay");
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=86400"
});
app.UseRouting();
app.MapSiteAssets();
app.MapGet("/health", () => Results.Text("OK"));
app.MapControllers();

app.Run();

public partial class Program;
