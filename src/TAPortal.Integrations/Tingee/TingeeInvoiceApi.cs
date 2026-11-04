using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TAPortal.Integrations.Tingee;

// Tingee e-invoice (Hóa đơn điện tử) Open API: the merchant's account at an invoice provider (XCyber, HiLo, SInvoice, MISA,
// BKAV), its registered templates, draft/issued invoices, PDF download and email. Same signing, envelope and merchantId rules
// as TingeeApi. Field names and amounts-as-strings follow developers.tingee.vn/docs/hoa-don-dien-tu (openapi v1.2.2).

public static class TingeeInvoiceProviders
{
    public const string XCyber = "x-cyber";
    public const string HiLo = "hilo";
    public const string SInvoice = "s-invoice";
    public const string Misa = "misa";
    public const string Bkav = "bkav";

    /// <summary>Provider codes Tingee accepts, in the order its documentation lists them.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Known =
        [(XCyber, "XCyber"), (HiLo, "HiLo"), (SInvoice, "SInvoice (Viettel)"), (Misa, "MISA meInvoice"), (Bkav, "BKAV eHoadon")];

    public static bool IsKnown(string? code) => Known.Any(p => p.Code == code);
    public static string Name(string? code) => Known.FirstOrDefault(p => p.Code == code).Name ?? code ?? "";
}

/// <summary>Tax rate codes of items[].taxRateCode: 0/5/8/10 percent, -1 not subject to VAT (KCT), -2 not declared (KKKNT).</summary>
public static class TingeeTaxRates
{
    public static readonly IReadOnlyList<(string Code, string Label)> All =
        [("10", "10%"), ("8", "8%"), ("5", "5%"), ("0", "0%"), ("-1", "Không chịu thuế (KCT)"), ("-2", "Không kê khai nộp thuế (KKKNT)")];

    public static bool IsKnown(string? code) => All.Any(r => r.Code == code);
}

public sealed record TingeeInvoiceProvider
{
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("urlLogo")] public string? UrlLogo { get; init; }
    [JsonPropertyName("lookupUrlPattern")] public string? LookupUrlPattern { get; init; }
}

public sealed record TingeeInvoiceAccount
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("merchantId")] public long? MerchantId { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("taxCode")] public string? TaxCode { get; init; }
    [JsonPropertyName("username")] public string? Username { get; init; }
    [JsonPropertyName("isDefault")] public bool IsDefault { get; init; }
    [JsonPropertyName("isActive")] public bool IsActive { get; init; }
    [JsonPropertyName("services")] public List<string> Services { get; init; } = [];
    [JsonPropertyName("creationTime")] public DateTime? CreationTime { get; init; }
    [JsonPropertyName("merchantName")] public string? MerchantName { get; init; }
}

/// <summary>create-or-update-account. The password goes to Tingee only (Tingee stores it encrypted); T.A never keeps it.</summary>
public sealed record TingeeInvoiceAccountRequest
{
    [JsonPropertyName("id")] public long? Id { get; init; }
    [JsonPropertyName("merchantId")] public long? MerchantId { get; init; }
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("taxCode")] public required string TaxCode { get; init; }
    [JsonPropertyName("username")] public required string Username { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("isDefault")] public bool IsDefault { get; init; }
    [JsonPropertyName("isActive")] public bool IsActive { get; init; } = true;
    [JsonPropertyName("services")] public List<string>? Services { get; init; }
}

public sealed record TingeeInvoiceTemplate
{
    [JsonPropertyName("invoicePattern")] public string? InvoicePattern { get; init; }
    [JsonPropertyName("invoiceSeries")] public string? InvoiceSeries { get; init; }
    /// <summary>Invoices still available on the template; only HiLo returns it.</summary>
    [JsonPropertyName("quota")] public int? Quota { get; init; }
}

public sealed record TingeeInvoiceItem
{
    [JsonPropertyName("itemCode")] public string? ItemCode { get; init; }
    [JsonPropertyName("itemName")] public required string ItemName { get; init; }
    [JsonPropertyName("unitName")] public string? UnitName { get; init; }
    [JsonPropertyName("quantity")] public required string Quantity { get; init; }
    [JsonPropertyName("unitPrice")] public required string UnitPrice { get; init; }
    [JsonPropertyName("discount")] public string? Discount { get; init; }
    [JsonPropertyName("taxRateCode")] public required string TaxRateCode { get; init; }
    /// <summary>1 goods (default), 2 promotion, 3 discount, 4 note, 5 service, 6 list, 7 other fee — providers support subsets (2038).</summary>
    [JsonPropertyName("itemType")] public int? ItemType { get; init; }
}

/// <summary>Body of both create (draft, returns a PDF) and create-and-issue (signed and sent to the tax authority).</summary>
public sealed record TingeeInvoiceRequest
{
    [JsonPropertyName("merchantId")] public long? MerchantId { get; init; }
    [JsonPropertyName("accountId")] public long? AccountId { get; init; }
    [JsonPropertyName("invoicePattern")] public required string InvoicePattern { get; init; }
    [JsonPropertyName("invoiceSeries")] public required string InvoiceSeries { get; init; }
    /// <summary>Our own unique key: later download/send-email use it; issuing the same code twice gives Tingee 2034.</summary>
    [JsonPropertyName("invoiceCode")] public required string InvoiceCode { get; init; }
    /// <summary>yyyy-MM-dd.</summary>
    [JsonPropertyName("invoiceDate")] public required string InvoiceDate { get; init; }
    [JsonPropertyName("buyerTaxCode")] public string? BuyerTaxCode { get; init; }
    [JsonPropertyName("buyerName")] public string? BuyerName { get; init; }
    [JsonPropertyName("buyerLegalName")] public string? BuyerLegalName { get; init; }
    [JsonPropertyName("buyerAddress")] public string? BuyerAddress { get; init; }
    [JsonPropertyName("buyerPhone")] public string? BuyerPhone { get; init; }
    [JsonPropertyName("buyerEmail")] public string? BuyerEmail { get; init; }
    /// <summary>1 cash, 2 bank transfer, 3 cash/transfer (Tingee default).</summary>
    [JsonPropertyName("paymentMethod")] public int? PaymentMethod { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("items")] public required List<TingeeInvoiceItem> Items { get; init; }
}

public sealed record TingeeInvoicePdf
{
    [JsonPropertyName("invoiceCode")] public string? InvoiceCode { get; init; }
    [JsonPropertyName("pdfBase64")] public string? PdfBase64 { get; init; }
}

public sealed record TingeeIssuedInvoice
{
    [JsonPropertyName("invoiceCode")] public string? InvoiceCode { get; init; }
    [JsonPropertyName("trackingCode")] public string? TrackingCode { get; init; }
    [JsonPropertyName("invoiceNumber")] public string? InvoiceNumber { get; init; }
    [JsonPropertyName("reservationCode")] public string? ReservationCode { get; init; }
    [JsonPropertyName("codeOfTax")] public string? CodeOfTax { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public interface ITingeeInvoiceApi
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<TingeeInvoiceProvider>> GetProvidersAsync(CancellationToken ct = default);
    /// <summary>Provider accounts of one child merchant (Master) or of the merchant itself (Direct).</summary>
    Task<TingeePage<TingeeInvoiceAccount>> GetAccountsAsync(long merchantId, CancellationToken ct = default);
    /// <summary>Creates (or updates when Id is set) a provider account; Tingee verifies the login first (2037). Returns its id.</summary>
    Task<long> SaveAccountAsync(TingeeInvoiceAccountRequest request, CancellationToken ct = default);
    Task DeleteAccountAsync(long merchantId, long accountId, CancellationToken ct = default);
    /// <summary>Templates registered at the provider; HiLo needs invoiceNoToCheck, BKAV has none (2007).</summary>
    Task<IReadOnlyList<TingeeInvoiceTemplate>> GetTemplatesAsync(long merchantId, long? accountId, int? invoiceNoToCheck = null, CancellationToken ct = default);
    Task<TingeeInvoicePdf> CreateDraftAsync(TingeeInvoiceRequest request, CancellationToken ct = default);
    Task<TingeeIssuedInvoice> CreateAndIssueAsync(TingeeInvoiceRequest request, CancellationToken ct = default);
    Task<TingeeInvoicePdf> DownloadAsync(long merchantId, string invoiceCode, long? accountId = null, CancellationToken ct = default);
    /// <summary>Only XCyber and MISA send email (others: 2007).</summary>
    Task SendEmailAsync(long merchantId, string invoiceCode, string recipientEmail, string? buyerName = null, long? accountId = null, CancellationToken ct = default);
}

public sealed class TingeeInvoiceApi(TingeeClient client, IOptions<TingeeOptions> options) : ITingeeInvoiceApi
{
    public bool IsConfigured => options.Value.IsConfigured;

    // Same rule as TingeeApi: Direct sends the configured own Merchant ID (or nothing), Master the child merchant's.
    private long? Mid(long? merchantId) => options.Value.IsDirectMerchant ? (options.Value.MerchantId is > 0 ? options.Value.MerchantId : null) : merchantId;

    public async Task<IReadOnlyList<TingeeInvoiceProvider>> GetProvidersAsync(CancellationToken ct = default) =>
        Data(await client.PostAsync<List<TingeeInvoiceProvider>>("/v1/e-invoice/get-provider", new { }, ct));

    public async Task<TingeePage<TingeeInvoiceAccount>> GetAccountsAsync(long merchantId, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeePage<TingeeInvoiceAccount>>("/v1/e-invoice/get-paging-account", new { merchantId = Mid(merchantId), skipCount = 0, maxResultCount = 200 }, ct));

    public async Task<long> SaveAccountAsync(TingeeInvoiceAccountRequest request, CancellationToken ct = default)
    {
        var response = await client.PostAsync<long?>("/v1/e-invoice/create-or-update-account", request with { MerchantId = Mid(request.MerchantId) }, ct);
        Check(response);
        return response.Data ?? request.Id ?? throw new TingeeApiException("EMPTY", "Tingee returned no account id.") { LogCode = response.LogCode };
    }

    public async Task DeleteAccountAsync(long merchantId, long accountId, CancellationToken ct = default) =>
        Check(await client.PostAsync<object>("/v1/e-invoice/delete-account", new { id = accountId, merchantId = Mid(merchantId) }, ct));

    public async Task<IReadOnlyList<TingeeInvoiceTemplate>> GetTemplatesAsync(long merchantId, long? accountId, int? invoiceNoToCheck = null, CancellationToken ct = default) =>
        Data(await client.PostAsync<List<TingeeInvoiceTemplate>>("/v1/e-invoice/invoice-templates", new { merchantId = Mid(merchantId), accountId, invoiceNoToCheck }, ct));

    public async Task<TingeeInvoicePdf> CreateDraftAsync(TingeeInvoiceRequest request, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeInvoicePdf>("/v1/e-invoice/create", request with { MerchantId = Mid(request.MerchantId) }, ct), required: true);

    public async Task<TingeeIssuedInvoice> CreateAndIssueAsync(TingeeInvoiceRequest request, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeIssuedInvoice>("/v1/e-invoice/create-and-issue", request with { MerchantId = Mid(request.MerchantId) }, ct), required: true);

    public async Task<TingeeInvoicePdf> DownloadAsync(long merchantId, string invoiceCode, long? accountId = null, CancellationToken ct = default) =>
        Data(await client.PostAsync<TingeeInvoicePdf>("/v1/e-invoice/download", new { merchantId = Mid(merchantId), accountId, invoiceCode }, ct), required: true);

    public async Task SendEmailAsync(long merchantId, string invoiceCode, string recipientEmail, string? buyerName = null, long? accountId = null, CancellationToken ct = default) =>
        Check(await client.PostAsync<object>("/v1/e-invoice/send-email", new { merchantId = Mid(merchantId), accountId, invoiceCode, recipientEmail, buyerName }, ct));

    private static T Data<T>(TingeeResponse<T> response, bool required = false) where T : class, new()
    {
        Check(response);
        return response.Data ?? (required ? throw new TingeeApiException("EMPTY", "Tingee returned no data.") { LogCode = response.LogCode } : new T());
    }

    private static void Check<T>(TingeeResponse<T> response)
    {
        if (!response.IsSuccess) throw new TingeeApiException(response.Code, response.Message, response.Raw) { LogCode = response.LogCode };
    }
}
