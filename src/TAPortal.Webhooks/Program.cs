using Microsoft.Extensions.Options;
using TAPortal.Integrations;
using TAPortal.Integrations.Tingee;
using TAPortal.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = TingeeWebhookEndpoints.MaxBodyBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = TingeeWebhookEndpoints.MaxBodyBytes);
builder.Services.AddTingee(builder.Configuration);

var app = builder.Build();

var tingee = app.Services.GetRequiredService<IOptions<TingeeOptions>>().Value;
if (!tingee.IsConfigured)
    app.Logger.LogCritical("WEBHOOKS_TINGEE_NOT_CONFIGURED: Tingee__ClientId/Tingee__SecretKey missing; Tingee webhooks will be refused with 503.");
if (!app.Services.GetRequiredService<ITingeeWebhookInbox>().IsAvailable)
    app.Logger.LogCritical("WEBHOOKS_INBOX_UNAVAILABLE: webhook inbox not configured; Tingee webhooks will be refused with 503 so Tingee retries.");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapTingeeWebhooks();

app.Run();

public partial class Program
{
}
