namespace TAPortal.Integrations.Tingee;

/// <summary>Tingee Open API settings. ClientId and SecretKey come from environment/secret configuration only.</summary>
public sealed class TingeeOptions
{
    public const string SectionName = "Tingee";
    public const string UatBaseUrl = "https://uat-open-api.tingee.vn";
    public const string ProductionBaseUrl = "https://open-api.tingee.vn";

    public string BaseUrl { get; set; } = UatBaseUrl;
    public string ClientId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhooks whose x-request-timestamp is older or newer than this window are rejected.</summary>
    public int WebhookMaxAgeMinutes { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(SecretKey);
}
