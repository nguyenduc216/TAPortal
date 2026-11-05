using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TACustomerPortal.Web.Services;

/// <summary>Registered name and address of a Vietnamese tax code (MST), used to fill sign-up and buyer forms.</summary>
public sealed record TaxCodeInfo(string TaxCode, string Name, string? ShortName, string? Address);

public enum TaxCodeLookupStatus { Found, NotFound, Invalid, Unavailable }

public sealed record TaxCodeLookupResult(TaxCodeLookupStatus Status, TaxCodeInfo? Info = null);

public interface ITaxCodeLookup
{
    Task<TaxCodeLookupResult> LookupAsync(string? taxCode, CancellationToken ct = default);
}

public static partial class TaxCodeRules
{
    /// <summary>Spaces removed; null when blank.</summary>
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>Enterprise (10 digits, branch 10-3) or personal/household (12 digits, the CCCD number since 2025).</summary>
    public static bool IsValid(string? normalized) => normalized is not null && Format().IsMatch(normalized);

    /// <summary>Enterprise tax codes can be looked up in the public business register; 12-digit personal codes cannot.</summary>
    public static bool IsLookupable(string? normalized) => normalized is not null && Business().IsMatch(normalized);

    [GeneratedRegex(@"^(\d{10}(-\d{3})?|\d{12})$")]
    private static partial Regex Format();

    [GeneratedRegex(@"^\d{10}(-\d{3})?$")]
    private static partial Regex Business();
}

/// <summary>
/// Public business register lookup through VietQR (GET https://api.vietqr.io/v2/business/{mst}, no key). The service
/// rate-limits per IP, so answers are cached (found 24 h, not found 1 h) and any failure is "Unavailable": the person
/// types the name instead. Only the tax code leaves the server.
/// </summary>
public sealed class VietQrTaxCodeLookup(IHttpClientFactory clients, TimeProvider clock, ILogger<VietQrTaxCodeLookup> logger) : ITaxCodeLookup
{
    public const string ClientName = "vietqr-business";
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Until, TaxCodeLookupResult Result)> Cache = new();

    public async Task<TaxCodeLookupResult> LookupAsync(string? taxCode, CancellationToken ct = default)
    {
        var code = TaxCodeRules.Normalize(taxCode);
        if (!TaxCodeRules.IsLookupable(code)) return new(TaxCodeLookupStatus.Invalid);
        var now = clock.GetUtcNow();
        if (Cache.TryGetValue(code!, out var hit) && hit.Until > now) return hit.Result;
        try
        {
            using var response = await clients.CreateClient(ClientName).GetAsync("v2/business/" + Uri.EscapeDataString(code!), ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || !response.IsSuccessStatusCode)
            {
                logger.LogWarning("TAXCODE_LOOKUP_HTTP {Status}", (int)response.StatusCode);
                return new(TaxCodeLookupStatus.Unavailable);
            }
            var body = await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
            TaxCodeLookupResult result = body?.Code == "00" && !string.IsNullOrWhiteSpace(body.Data?.Name)
                ? new(TaxCodeLookupStatus.Found, new(code!, body.Data.Name.Trim(), Blank(body.Data.ShortName), Blank(body.Data.Address)))
                : body?.Code == "51" ? new(TaxCodeLookupStatus.NotFound) : new(TaxCodeLookupStatus.Unavailable);
            if (result.Status != TaxCodeLookupStatus.Unavailable)
                Cache[code!] = (now.Add(result.Status == TaxCodeLookupStatus.Found ? TimeSpan.FromHours(24) : TimeSpan.FromHours(1)), result);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "TAXCODE_LOOKUP_FAILED");
            return new(TaxCodeLookupStatus.Unavailable);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Envelope
    {
        [JsonPropertyName("code")] public string? Code { get; init; }
        [JsonPropertyName("data")] public Business? Data { get; init; }
    }

    private sealed record Business
    {
        [JsonPropertyName("name")] public string? Name { get; init; }
        [JsonPropertyName("shortName")] public string? ShortName { get; init; }
        [JsonPropertyName("address")] public string? Address { get; init; }
    }
}
