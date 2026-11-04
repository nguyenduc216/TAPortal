using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.Tingee;

/// <summary>
/// Tingee webhook receivers: verify timestamp + HMAC over the raw body, persist to the inbox, then acknowledge.
/// Any failure before persistence returns non-200 so Tingee retries; nothing is processed inline.
/// Hosted by TAPortal.Webhooks and, for the Tingee Sandbox, by the Admin portal (same anonymous, signature-checked routes).
/// </summary>
public static class TingeeWebhookEndpoints
{
    public const long MaxBodyBytes = 64 * 1024;
    private static readonly object Success = new { code = "00", message = "Success" };

    /// <summary>Maps the receivers under <paramref name="prefix"/> (default /tingee, the webhook site's path).</summary>
    public static IEndpointRouteBuilder MapTingeeWebhooks(this IEndpointRouteBuilder app, string prefix = TingeeOptions.WebhookPathPrefix)
    {
        // Anonymous by design: every request is authenticated by the Tingee HMAC signature instead.
        var group = app.MapGroup(prefix).AllowAnonymous();
        // Typed as Delegate so the IResult is written; a plain HttpContext lambda binds as RequestDelegate and drops it.
        group.MapPost("/payment", (Delegate)((HttpContext http) => ReceiveAsync(http, TingeeWebhookKind.Payment)));
        group.MapPost("/va-status", (Delegate)((HttpContext http) => ReceiveAsync(http, TingeeWebhookKind.VaStatus)));
        group.MapPost("/bank-link", (Delegate)((HttpContext http) => ReceiveAsync(http, TingeeWebhookKind.BankLink)));
        return app;
    }

    private static async Task<IResult> ReceiveAsync(HttpContext http, TingeeWebhookKind kind)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<TingeeOptions>>().Value;
        var inbox = services.GetRequiredService<ITingeeWebhookInbox>();
        var clock = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TAPortal.Webhooks.Tingee");

        if (!options.IsConfigured || !inbox.IsAvailable)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        // Per-request cap, so hosts without a global body limit (the Admin portal) are bounded too.
        if (http.Request.ContentLength > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit && (limit.MaxRequestBodySize is null || limit.MaxRequestBodySize > MaxBodyBytes))
            limit.MaxRequestBodySize = MaxBodyBytes;

        string body;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync(http.RequestAborted);

        var timestamp = http.Request.Headers["x-request-timestamp"].ToString();
        var signature = http.Request.Headers["x-signature"].ToString();
        var requestId = http.Request.Headers["x-request-id"].ToString();
        var now = clock.GetUtcNow();

        if (!TingeeSignature.TryParseTimestamp(timestamp, out var sentAt)
            || !TingeeSignature.IsFresh(sentAt, now, TimeSpan.FromMinutes(options.WebhookMaxAgeMinutes))
            || !TingeeSignature.Verify(timestamp, body, signature, options.SecretKey))
        {
            logger.LogWarning("TINGEE_WEBHOOK_REJECTED kind={Kind} requestId={RequestId}", kind, requestId);
            return Results.Unauthorized();
        }

        try
        {
            await inbox.SaveAsync(new TingeeWebhookEnvelope(kind, string.IsNullOrEmpty(requestId) ? null : requestId, timestamp, body, now), http.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TINGEE_WEBHOOK_INBOX_FAILED kind={Kind} requestId={RequestId}", kind, requestId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(Success);
    }
}
