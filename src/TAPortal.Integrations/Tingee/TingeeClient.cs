using System.Net.Http.Headers;
using System.Text;
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
}

public sealed class TingeeNotConfiguredException : InvalidOperationException
{
    public TingeeNotConfiguredException() : base("Tingee ClientId/SecretKey are not configured.") { }
}

/// <summary>Signs and sends Tingee Open API requests. The signed body is exactly the bytes sent.</summary>
public sealed class TingeeClient(HttpClient http, IOptions<TingeeOptions> options, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

        var timestamp = TingeeSignature.Timestamp(clock.GetUtcNow());
        using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.BaseUrl), path));
        request.Headers.Add("x-client-id", settings.ClientId);
        request.Headers.Add("x-request-timestamp", timestamp);
        request.Headers.Add("x-signature", TingeeSignature.Sign(timestamp, body, settings.SecretKey));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<TingeeResponse<T>>(stream, Json, cancellationToken)
            ?? new TingeeResponse<T> { Code = "EMPTY", Message = "Empty Tingee response." };
    }
}
