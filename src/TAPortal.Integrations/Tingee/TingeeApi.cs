using System.Text.Json.Serialization;

namespace TAPortal.Integrations.Tingee;

// Typed Tingee Open API calls used by Admin (Master Merchant) and Customer Portal. Every call is made with T.A's
// Master Merchant key and carries the child merchantId, or in Direct mode with a normal Merchant's own key and no merchantId. Field names follow developers.tingee.vn (openapi v1.2.2).

public sealed record TingeeCreateMerchantRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("website")] string? Website = null,
    [property: JsonPropertyName("code")] string? Code = null,
    [property: JsonPropertyName("fullAddress")] string? FullAddress = null);

public sealed record TingeeMerchantCreated
{
    [JsonPropertyName("merchantId")] public long MerchantId { get; init; }
}

public sealed record TingeeCreateVaRequest
{
    [JsonPropertyName("merchantId")] public long? MerchantId { get; init; }
    [JsonPropertyName("bankBin")] public required string BankBin { get; init; }
    [JsonPropertyName("accountType")] public string? AccountType { get; init; }
    [JsonPropertyName("accountNumber")] public string? AccountNumber { get; init; }
    [JsonPropertyName("accountName")] public string? AccountName { get; init; }
    [JsonPropertyName("identity")] public string? Identity { get; init; }
    [JsonPropertyName("mobile")] public string? Mobile { get; init; }
    [JsonPropertyName("isNotifyAccountNumber")] public bool IsNotifyAccountNumber { get; init; }
    [JsonPropertyName("appType")] public string AppType { get; init; } = "baas";
    [JsonPropertyName("vaSuffix")] public string? VaSuffix { get; init; }
    [JsonPropertyName("vaAccountNumber")] public string? VaAccountNumber { get; init; }
    [JsonPropertyName("merchantName")] public string? MerchantName { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("taxCode")] public string? TaxCode { get; init; }
    [JsonPropertyName("redirectUrl")] public string? RedirectUrl { get; init; }
    [JsonPropertyName("webhookUrl")] public string? WebhookUrl { get; init; }
    [JsonPropertyName("requestId")] public string? RequestId { get; init; }
    [JsonPropertyName("acbUserId")] public string? AcbUserId { get; init; }
}

public sealed record TingeeCreateVaResult
{
    [JsonPropertyName("confirmId")] public string? ConfirmId { get; init; }
    [JsonPropertyName("vaAccountNumber")] public string? VaAccountNumber { get; init; }
    [JsonPropertyName("shopId")] public long? ShopId { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("authorizeLink")] public string? AuthorizeLink { get; init; }
    [JsonPropertyName("deepLink")] public string? DeepLink { get; init; }
    [JsonPropertyName("otpMethod")] public string? OtpMethod { get; init; }
}

public sealed record TingeeConfirmVaResult
{
    [JsonPropertyName("accountNumber")] public string? AccountNumber { get; init; }
    [JsonPropertyName("vaAccountNumber")] public string? VaAccountNumber { get; init; }
    [JsonPropertyName("shopId")] public long? ShopId { get; init; }
}

public sealed record TingeeDeleteVaResult
{
    [JsonPropertyName("confirmId")] public string? ConfirmId { get; init; }
    [JsonPropertyName("authorizeLink")] public string? AuthorizeLink { get; init; }
}

public sealed record TingeeVaItem
{
    [JsonPropertyName("bankBin")] public string? BankBin { get; init; }
    [JsonPropertyName("accountType")] public string? AccountType { get; init; }
    [JsonPropertyName("accountName")] public string? AccountName { get; init; }
    [JsonPropertyName("accountNumber")] public string? AccountNumber { get; init; }
    [JsonPropertyName("vaAccountNumber")] public string? VaAccountNumber { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("shopId")] public long? ShopId { get; init; }
}

public sealed record TingeePage<T>
{
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("items")] public List<T> Items { get; init; } = [];
}

public sealed record TingeeDynamicQrRequest
{
    [JsonPropertyName("merchantId")] public long? MerchantId { get; init; }
    [JsonPropertyName("vaAccountNumber")] public required string VaAccountNumber { get; init; }
    [JsonPropertyName("qrCodeType")] public string QrCodeType { get; init; } = TingeeQrTypes.Recurring;
    [JsonPropertyName("bankBin")] public required string BankBin { get; init; }
    [JsonPropertyName("amount")] public decimal Amount { get; init; }
    [JsonPropertyName("purpose")] public string? Purpose { get; init; }
    [JsonPropertyName("expireInMinute")] public int ExpireInMinute { get; init; }
    [JsonPropertyName("extraInfo")] public string? ExtraInfo { get; init; }
}

public static class TingeeQrTypes
{
    public const string OneTime = "dynamic-one-time-payment";
    public const string Recurring = "dynamic-recurring-payment";
    public const string Static = "static-viet-qr";
}

public sealed record TingeeDynamicQr
{
    [JsonPropertyName("qrCode")] public string? QrCode { get; init; }
    [JsonPropertyName("qrAccount")] public string? QrAccount { get; init; }
    [JsonPropertyName("billId")] public string? BillId { get; init; }
}

public sealed record TingeeStaticQrRequest(
    [property: JsonPropertyName("bankBin")] string BankBin,
    [property: JsonPropertyName("accountNumber")] string AccountNumber,
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("content")] string? Content);

public sealed record TingeeStaticQr
{
    [JsonPropertyName("qrCode")] public string? QrCode { get; init; }
}

public sealed record TingeeQrStatus
{
    [JsonPropertyName("billInfo")] public TingeeBillInfo? BillInfo { get; init; }
}

public sealed record TingeeBillInfo
{
    [JsonPropertyName("billId")] public string? BillId { get; init; }
    [JsonPropertyName("amount")] public decimal Amount { get; init; }
    [JsonPropertyName("totalAmountPaid")] public decimal TotalAmountPaid { get; init; }
    [JsonPropertyName("totalPaymentsCount")] public int TotalPaymentsCount { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
}

/// <summary>A failed Tingee call: Code is Tingee's result code ("1065", "90", ...) and Message its text.</summary>
public sealed class TingeeApiException(string? code, string? message)
    : Exception($"Tingee error {code}: {message}")
{
    public string Code { get; } = code ?? "UNKNOWN";
    public string TingeeMessage { get; } = message ?? "";
}

public interface ITingeeApi
{
    bool IsConfigured { get; }
    bool IsDirectMerchant => false;
    Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default);
    Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default);
    Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default);
    Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default);
    Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default);
    Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default);
    Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default);
    Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default);
    Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default);
    Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default);
}

public sealed class TingeeApi(TingeeClient client, Microsoft.Extensions.Options.IOptions<TingeeOptions> options) : ITingeeApi
{
    public bool IsConfigured => options.Value.IsConfigured;
    public bool IsDirectMerchant => options.Value.IsDirectMerchant;

    private long? Mid(long? merchantId) => IsDirectMerchant ? null : merchantId;

    public async Task<TingeeMerchantCreated> CreateMerchantAsync(TingeeCreateMerchantRequest request, CancellationToken ct = default)
    {
        var created = Data(await client.PostAsync<TingeeMerchantCreated>("/v1/merchant/create", request, ct));
        return created.MerchantId > 0 ? created : throw new TingeeApiException("EMPTY", "Tingee returned no merchantId.");
    }

    public async Task<TingeeCreateVaResult> CreateVaAsync(TingeeCreateVaRequest request, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeCreateVaResult>("/v1/create-va", request with { MerchantId = Mid(request.MerchantId) }, ct));

    public async Task<TingeeConfirmVaResult> ConfirmVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeConfirmVaResult>("/v1/confirm-va", new { merchantId = Mid(merchantId), bankBin, confirmId, otpNumber = otp }, ct));

    public async Task<TingeeDeleteVaResult> DeleteVaAsync(long merchantId, string bankBin, string vaAccountNumber, CancellationToken ct = default) =>
        Data(await client.PostQueryAsync<TingeeDeleteVaResult>($"/v1/delete-va?{(IsDirectMerchant ? "" : $"merchantId={merchantId}&")}bankBin={Uri.EscapeDataString(bankBin)}&vaAccountNumber={Uri.EscapeDataString(vaAccountNumber)}", ct), allowEmpty: true);

    public async Task ConfirmDeleteVaAsync(long merchantId, string bankBin, string confirmId, string otp, CancellationToken ct = default) =>
        Check(await client.PostAsync<object>("/v1/confirm-delete-va", new { merchantId = Mid(merchantId), bankBin, confirmId, otpNumber = otp }, ct));

    public async Task<TingeePage<TingeeVaItem>> GetVaPageAsync(long merchantId, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeePage<TingeeVaItem>>("/v1/get-va-paging", new { merchantId = Mid(merchantId), skipCount = 0, maxResultCount = 200 }, ct), allowEmpty: true);

    public async Task<TingeeDynamicQr> GenerateDynamicQrAsync(TingeeDynamicQrRequest request, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeDynamicQr>("/v1/generate-dynamic-qr", request with { MerchantId = Mid(request.MerchantId) }, ct));

    public async Task<TingeeStaticQr> GenerateStaticQrAsync(TingeeStaticQrRequest request, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeStaticQr>("/v1/generate-viet-qr", request, ct));

    public async Task<TingeeQrStatus> GetDynamicQrStatusAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeQrStatus>("/v1/get-status-dynamic-qr", new { merchantId = Mid(merchantId), qrAccount, billId }, ct));

    public async Task DeleteDynamicQrAsync(long merchantId, string qrAccount, string billId, CancellationToken ct = default) =>
        Check(await client.PostAsync<object>("/v1/delete-dynamic-qr", new { merchantId = Mid(merchantId), qrAccount, billId }, ct));

    private static T Data<T>(TingeeResponse<T> response, bool allowEmpty = false) where T : class, new()
    {
        Check(response);
        return response.Data ?? (allowEmpty ? new T() : throw new TingeeApiException("EMPTY", "Tingee returned no data."));
    }

    private static void Check<T>(TingeeResponse<T> response)
    {
        if (!response.IsSuccess) throw new TingeeApiException(response.Code, response.Message);
    }
}
