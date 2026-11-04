using Microsoft.Extensions.Options;
using TAPortal.Shared;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = TingeeWebhookEndpoints.MaxBodyBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = TingeeWebhookEndpoints.MaxBodyBytes);
// Durable inbox when a connection string is configured; otherwise AddTingee keeps the "unavailable" inbox (503 + retry).
if (!string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
    builder.Services.AddSingleton<ITingeeWebhookInbox, SqlTingeeWebhookInbox>();
builder.Services.AddTingee(builder.Configuration);

var app = builder.Build();
app.UseTaSecurityHeaders();

var tingee = app.Services.GetRequiredService<IOptions<TingeeOptions>>().Value;
if (!tingee.IsConfigured)
    app.Logger.LogCritical("WEBHOOKS_TINGEE_NOT_CONFIGURED: Tingee__ClientId/Tingee__SecretKey missing; Tingee webhooks will be refused with 503.");
if (!app.Services.GetRequiredService<ITingeeWebhookInbox>().IsAvailable)
    app.Logger.LogCritical("WEBHOOKS_INBOX_UNAVAILABLE: webhook inbox not configured; Tingee webhooks will be refused with 503 so Tingee retries.");

// /health: JSON state for the deploy health check and monitoring (503 when keys or the inbox database are missing);
// /: the same state as a page. Neither shows secrets.
app.MapGet("/health", async (HttpContext http) =>
{
    var report = await WebhookHealth.CheckAsync(http.RequestServices, http.RequestAborted);
    http.Response.Headers.CacheControl = "no-store";
    return Results.Json(report, statusCode: report.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/", async (HttpContext http) =>
{
    var report = await WebhookHealth.CheckAsync(http.RequestServices, http.RequestAborted);
    http.Response.Headers.CacheControl = "no-store";
    return Results.Content(WebhookHealth.Page(report), "text/html; charset=utf-8", statusCode: report.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});
app.MapTingeeWebhooks();

app.Run();

public partial class Program
{
}
