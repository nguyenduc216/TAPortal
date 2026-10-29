using System.Text;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Webhooks;

/// <summary>
/// Tingee webhook receivers: verify timestamp + HMAC over the raw body, persist to the inbox, then acknowledge.
/// Any failure before persistence returns non-200 so Tingee retries; nothing is processed inline.
/// </summary>
public static class TingeeWebhookEndpoints
{
    public const long MaxBodyBytes = 64 * 1024;
    private static readonly object Success = new { code = "00", message = "Success" };

    public static IEndpointRouteBuilder MapTingeeWebhooks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hooks/tingee");
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
