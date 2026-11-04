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

    /// <summary>"Master" (default) acts on child merchants through merchantId; "Direct" uses a normal Merchant's own key.</summary>
    public string Mode { get; set; } = "Master";

    /// <summary>Public URL of the va-status webhook, sent to banks that report the linking result later (VCB personal, TPBank).</summary>
    public string? VaStatusWebhookUrl { get; set; }

    /// <summary>Path of the Tingee receivers on the webhook site (https://webhook.hoadontudong.vn/tingee/payment ...).</summary>
    public const string WebhookPathPrefix = "/tingee";
    public const string DefaultWebhookBaseUrl = "https://webhook.hoadontudong.vn";

    /// <summary>
    /// Public address of the TAPortal.Webhooks site: the single place Tingee calls. The Admin sandbox shows and self-tests
    /// these addresses and bank links that report their result later use the va-status one.
    /// </summary>
    public string WebhookBaseUrl { get; set; } = DefaultWebhookBaseUrl;

    /// <summary>
    /// Shared key Tingee sends in a header with every webhook (Tingee endpoint "API key tuỳ chỉnh"). When set it is required;
    /// a request carrying x-signature must also verify, and an unsigned one is accepted only with this key.
    /// </summary>
    public string? WebhookApiKey { get; set; }
    public string WebhookApiKeyHeader { get; set; } = "x-api-key";
    public bool HasWebhookApiKey => !string.IsNullOrWhiteSpace(WebhookApiKey);

    public string WebhookUrl(string kind) => $"{WebhookBaseUrl.TrimEnd('/')}{WebhookPathPrefix}/{kind}";

    /// <summary>va-status receiver sent in create-va: an explicit VaStatusWebhookUrl, otherwise the webhook site's.</summary>
    public string EffectiveVaStatusWebhookUrl => string.IsNullOrWhiteSpace(VaStatusWebhookUrl) ? WebhookUrl("va-status") : VaStatusWebhookUrl;

    /// <summary>Direct mode: the Merchant's own Tingee Merchant ID. Tingee requires it on create-va (code 1096) even for a
    /// normal Merchant; when set it replaces the per-customer id on every call, when empty no merchantId is sent.</summary>
    public long? MerchantId { get; set; }

    public bool IsDirectMerchant => string.Equals(Mode, "Direct", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(SecretKey);
}
