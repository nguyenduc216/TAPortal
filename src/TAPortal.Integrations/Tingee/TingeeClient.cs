using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.Tingee;

/// <summary>Standard Tingee envelope. HTTP is always 200; success is Code == "00".</summary>
public sealed class TingeeResponse<T>
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
    [JsonIgnore] public bool IsSuccess => Code == "00";
    /// <summary>Raw response body of a failed call (truncated): Tingee puts validation details there (e.g. code 400).</summary>
    [JsonIgnore] public string? Raw { get; set; }
    /// <summary>Code of the dbo.TingeeApiLogs row recording this call (request and response), for Tingee support.</summary>
    [JsonIgnore] public string? LogCode { get; set; }
}

public sealed class TingeeNotConfiguredException : InvalidOperationException
{
    public TingeeNotConfiguredException() : base("Tingee ClientId/SecretKey are not configured.") { }
}

/// <summary>
/// Signs and sends Tingee Open API requests. The signed body is exactly the bytes sent. Every call is recorded in the call
/// log under a log code; a failure carries that code (TingeeResponse.LogCode, or exception Data[LogCodeKey]).
/// </summary>
public sealed class TingeeClient(HttpClient http, IOptions<TingeeOptions> options, TimeProvider clock, ITingeeCallLog? callLog = null)
{
    public const string LogCodeKey = "TingeeLogCode";

    // Non-ASCII text (Vietnamese names) must be sent as raw UTF-8: Tingee checks the signature against the decoded body,
    // so the default "ư" escaping makes every such request fail with 97 "Invalid signature" (verified 2026-11-04).
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Task<TingeeResponse<T>> PostAsync<T>(string path, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, JsonSerializer.Serialize(body, Json), cancellationToken);

    public Task<TingeeResponse<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    /// <summary>POST whose parameters travel in the query string (e.g. delete-va); the empty body is signed as "{}".</summary>
    public Task<TingeeResponse<T>> PostQueryAsync<T>(string pathAndQuery, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, pathAndQuery, null, cancellationToken);

    private async Task<TingeeResponse<T>> SendAsync<T>(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured) throw new TingeeNotConfiguredException();

        var started = clock.GetUtcNow();
        var timestamp = TingeeSignature.Timestamp(started);
        var signature = TingeeSignature.Sign(timestamp, body, settings.SecretKey);
        var url = new Uri(new Uri(settings.BaseUrl), path);
        var logCode = TingeeLogCode.New(started);
        int? status = null;
        string? text = null;
        TingeeResponse<T>? parsed = null;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("x-client-id", settings.ClientId);
            request.Headers.Add("x-request-timestamp", timestamp);
            request.Headers.Add("x-signature", signature);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            text = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            parsed = Parse<T>(text);
        }
        catch (Exception ex)
        {
            ex.Data[LogCodeKey] = logCode;
            await Log(ex.GetType().Name + ": " + ex.Message);
            throw;
        }
        parsed.LogCode = logCode;
        await Log(null);
        if (!parsed.IsSuccess) parsed.Raw = text!.Length <= 2000 ? text : text[..2000];
        return parsed;

        Task Log(string? error) => callLog?.WriteAsync(new TingeeCallLogEntry
        {
            LogCode = logCode,
            Method = method.Method,
            Url = url.ToString(),
            ClientId = settings.ClientId,
            RequestTimestamp = timestamp,
            Signature = signature,
            RequestBody = body,
            HttpStatus = status,
            ResponseCode = parsed?.Code,
            ResponseMessage = parsed?.Message,
            ResponseBody = text,
            Error = error,
            DurationMs = (int)(clock.GetUtcNow() - started).TotalMilliseconds,
            CreatedAtUtc = started.UtcDateTime
        }) ?? Task.CompletedTask;
    }

    private static TingeeResponse<T> Parse<T>(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<TingeeResponse<T>>(text, Json) ?? new TingeeResponse<T> { Code = "EMPTY", Message = "Empty Tingee response." };
        }
        catch (JsonException)
        {
            // An error body whose "data" does not fit T (e.g. a list of field errors): keep the code/message, never fail here.
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            return new TingeeResponse<T>
            {
                Code = root.TryGetProperty("code", out var c) ? c.ToString() : "BADJSON",
                Message = root.TryGetProperty("message", out var m) ? m.ToString() : "Unexpected Tingee response."
            };
        }
    }
}
