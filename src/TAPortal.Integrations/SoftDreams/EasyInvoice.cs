using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Integrations.SoftDreams;

// SoftDreams EasyInvoice (operator 2026-11-26: "thêm nhà phân phối hóa đơn SoftDreams, tích hợp API"), called directly, not
// through the e-invoice partner. Source: "EasyInvoice_TaiLieuTichHop_v8.0". Every request carries the company's own
// EasyInvoice login (username:password:taxCode) in the Authentication header, so the password is kept encrypted
// (IInvoiceSecretProtector, DPAPI LocalMachine) and never logged. The invoice key (Ikey) is the order's fixed invoice code:
// importAndIssueInvoice updates a not-yet-issued invoice with the same Ikey instead of creating a second one.

public sealed class EasyInvoiceOptions
{
    public const string SectionName = "EasyInvoice";
    /// <summary>Shared production domain since 01/01/2026 (replaces MST.easyinvoice.vn).</summary>
    public string ProductionBaseUrl { get; set; } = "https://api.easyinvoice.vn/";
    /// <summary>SoftDreams' demo system for testing.</summary>
    public string DemoBaseUrl { get; set; } = "http://api.softdreams.vn/";
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>A company's EasyInvoice login. <paramref name="Demo"/>: the SoftDreams demo system instead of production.</summary>
public sealed record EasyInvoiceCredentials(string Username, string Password, string TaxCode, bool Demo)
{
    public override string ToString() => $"EasyInvoiceCredentials {{ Username = {Username}, TaxCode = {TaxCode}, Demo = {Demo} }}";   // never the password
}

public static class EasyInvoiceAuth
{
    /// <summary>
    /// The Authentication header: signature:nonce:timestamp:username:password:taxCode, where timestamp is Unix seconds (UTC),
    /// nonce letters/digits only and signature = Base64(MD5(METHOD + timestamp + nonce)).
    /// </summary>
    public static string Header(string method, EasyInvoiceCredentials login, DateTimeOffset now, string nonce)
    {
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(method.ToUpperInvariant() + timestamp + nonce)));
        return $"{signature}:{nonce}:{timestamp}:{login.Username}:{login.Password}:{login.TaxCode}";
    }

    public static string NewNonce() => Guid.NewGuid().ToString("N");
}

/// <summary>An EasyInvoice answer other than Status 2. Status 4 = the request was refused (data, login, state): retrying will not help.</summary>
public sealed class EasyInvoiceApiException(string status, string code, string providerMessage, string? detail = null)
    : Exception($"EasyInvoice status {status} code {code}: {providerMessage}{(detail is null ? "" : " — " + detail)}")
{
    public string Status { get; } = status;
    public string Code { get; } = code;
    public string ProviderMessage { get; } = providerMessage;
    /// <summary>The per-invoice detail (Data.KeyInvoiceMsg[ikey]) when given.</summary>
    public string? Detail { get; } = detail;
    public bool Permanent => Status == "4";
}

/// <summary>The stored EasyInvoice login cannot be read (no secret, or encrypted on another server): the company must connect again.</summary>
public sealed class EasyInvoiceNotConnectedException() : InvalidOperationException("EasyInvoice login is missing or cannot be decrypted.");

/// <summary>One invoice as EasyInvoice reports it. <see cref="InvoiceStatus"/>: -1 reserved, 0 unsigned, 1 signed, 2 declared, 3 replaced, 4 adjusted, 5 cancelled.</summary>
public sealed record EasyInvoiceInfo(string Ikey, int? InvoiceStatus, string? No, string? LookupCode, string? LinkView, string? TaxCheckStatus, string? TaxError, string? TaxAuthorityCode)
{
    /// <summary>Signed: it exists at the tax authority's side as an invoice (signed, declared, replaced or adjusted).</summary>
    public bool IsIssued => InvoiceStatus is >= 1 and <= 4;
    public bool IsCancelled => InvoiceStatus == 5;
}

public sealed record EasyInvoiceIssued(string? No, string? LookupCode, string? LinkView);

public interface IEasyInvoiceApi
{
    /// <summary>importAndIssueInvoice: creates (or updates the unsigned invoice with the same Ikey) and signs it on the server.</summary>
    Task<EasyInvoiceIssued> ImportAndIssueAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default);
    /// <summary>importInvoice: an unsigned draft without number (updated when the Ikey exists unsigned).</summary>
    Task ImportDraftAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default);
    /// <summary>getInvoicesByIkeys: the invoices that exist for these keys (none: empty).</summary>
    Task<IReadOnlyList<EasyInvoiceInfo>> GetByIkeysAsync(EasyInvoiceCredentials login, IReadOnlyList<string> ikeys, CancellationToken ct = default);
    /// <summary>getInvoicePdf Option 0: the invoice PDF.</summary>
    Task<byte[]> GetPdfAsync(EasyInvoiceCredentials login, string pattern, string ikey, CancellationToken ct = default);
}

public sealed class EasyInvoiceApi(HttpClient http, IOptions<EasyInvoiceOptions> options, TimeProvider clock, ILogger<EasyInvoiceApi> logger) : IEasyInvoiceApi
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<EasyInvoiceIssued> ImportAndIssueAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default)
    {
        var data = await PostAsync(login, "api/publish/importAndIssueInvoice", new { XmlData = xmlData, Pattern = pattern, Serial = "" }, ikey, ct);
        var invoice = Invoices(data).FirstOrDefault(i => i.Ikey == ikey);
        var number = data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("KeyInvoiceNo", out var map) && map.ValueKind == JsonValueKind.Object
            && map.TryGetProperty(ikey, out var no) ? Text(no) : null;
        return new(number ?? invoice?.No, invoice?.LookupCode, invoice?.LinkView);
    }

    public async Task ImportDraftAsync(EasyInvoiceCredentials login, string pattern, string xmlData, string ikey, CancellationToken ct = default) =>
        await PostAsync(login, "api/publish/importInvoice", new { XmlData = xmlData, Pattern = pattern, Serial = "" }, ikey, ct);

    public async Task<IReadOnlyList<EasyInvoiceInfo>> GetByIkeysAsync(EasyInvoiceCredentials login, IReadOnlyList<string> ikeys, CancellationToken ct = default) =>
        Invoices(await PostAsync(login, "api/publish/getInvoicesByIkeys", new { Ikeys = ikeys }, null, ct));

    public async Task<byte[]> GetPdfAsync(EasyInvoiceCredentials login, string pattern, string ikey, CancellationToken ct = default)
    {
        using var response = await SendAsync(login, "api/publish/getInvoicePdf", new { Ikey = ikey, Pattern = pattern, Option = 0 }, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (EasyInvoiceResponse.IsPdf(bytes)) return bytes;
        var data = EasyInvoiceResponse.Parse(Encoding.UTF8.GetString(bytes), ikey, (int)response.StatusCode);
        return EasyInvoiceResponse.FindPdf(data) ?? throw new EasyInvoiceApiException("5", "EMPTY", "Không nhận được file PDF.");
    }

    private async Task<JsonElement?> PostAsync(EasyInvoiceCredentials login, string resource, object body, string? ikey, CancellationToken ct)
    {
        using var response = await SendAsync(login, resource, body, ct);
        return EasyInvoiceResponse.Parse(await response.Content.ReadAsStringAsync(ct), ikey, (int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(EasyInvoiceCredentials login, string resource, object body, CancellationToken ct)
    {
        var o = options.Value;
        var baseUrl = (login.Demo ? o.DemoBaseUrl : o.ProductionBaseUrl).TrimEnd('/') + "/";
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + resource)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authentication", EasyInvoiceAuth.Header("POST", login, clock.GetUtcNow(), EasyInvoiceAuth.NewNonce()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds)));
        var started = clock.GetTimestamp();
        var response = await http.SendAsync(request, timeout.Token);
        // Never log the request: its header holds the password.
        logger.LogInformation("EASYINVOICE_CALL resource={Resource} demo={Demo} tax={Tax} http={Http} ms={Ms}", resource, login.Demo, login.TaxCode, (int)response.StatusCode,
            (long)clock.GetElapsedTime(started).TotalMilliseconds);
        return response;
    }

    private static IReadOnlyList<EasyInvoiceInfo> Invoices(JsonElement? data) => EasyInvoiceResponse.Invoices(data);
    private static string? Text(JsonElement e) => EasyInvoiceResponse.Text(e);
}

/// <summary>Reads the EasyInvoice envelope {Status, Message, ErrorCode, Data}. Status is sent as a number or a string; 2 = success.</summary>
public static class EasyInvoiceResponse
{
    public static JsonElement? Parse(string body, string? ikey, int httpStatus)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException)
        {
            throw new EasyInvoiceApiException(httpStatus is >= 400 and < 500 ? "4" : "5", "HTTP" + httpStatus, "Phản hồi không đúng định dạng.");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var status = Prop(root, "Status") is { } s ? Text(s) : null;
            var data = Prop(root, "Data") is { } d ? d.Clone() : (JsonElement?)null;
            if (status == "2") return data;
            var code = Prop(root, "ErrorCode") is { } c ? Text(c) : null;
            var message = Prop(root, "Message") is { } m ? Text(m) : null;
            string? detail = null;
            if (data is { ValueKind: JsonValueKind.Object } dd && Prop(dd, "KeyInvoiceMsg") is { ValueKind: JsonValueKind.Object } msgs)
                detail = ikey is not null && Prop(msgs, ikey) is { } one ? Text(one) : msgs.EnumerateObject().Select(p => Text(p.Value)).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            throw new EasyInvoiceApiException(status ?? (httpStatus is >= 400 and < 500 ? "4" : "5"), string.IsNullOrWhiteSpace(code) ? "HTTP" + httpStatus : code!,
                string.IsNullOrWhiteSpace(message) ? "Không rõ lỗi." : message!, string.IsNullOrWhiteSpace(detail) ? null : detail);
        }
    }

    public static IReadOnlyList<EasyInvoiceInfo> Invoices(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } d || Prop(d, "Invoices") is not { ValueKind: JsonValueKind.Array } list) return [];
        return list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i => new EasyInvoiceInfo(
            S(i, "Ikey") ?? "", int.TryParse(S(i, "InvoiceStatus"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : null,
            S(i, "No"), S(i, "LookupCode"), S(i, "LinkView"), S(i, "TCTCheckStatus"), S(i, "TCTErrorMessage"), S(i, "TaxAuthorityCode"))).ToList();
    }

    public static bool IsPdf(byte[] bytes) => bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';

    /// <summary>The PDF inside a JSON answer: Data itself or one of its string properties, as Base64.</summary>
    public static byte[]? FindPdf(JsonElement? data)
    {
        if (data is not { } d) return null;
        IEnumerable<JsonElement> candidates = d.ValueKind switch
        {
            JsonValueKind.String => [d],
            JsonValueKind.Object => d.EnumerateObject().Select(p => p.Value).Where(v => v.ValueKind == JsonValueKind.String),
            _ => []
        };
        foreach (var c in candidates)
        {
            try { var bytes = Convert.FromBase64String(c.GetString()!); if (IsPdf(bytes)) return bytes; }
            catch (FormatException) { }
        }
        return null;
    }

    internal static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty(name, out var v)) return v;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? S(JsonElement e, string name) => Prop(e, name) is { } v ? Text(v) : null;

    public static string? Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };
}

/// <summary>
/// The XmlData of one order's invoice (cấu trúc XmlData, Thông tư 78). Amounts are the order lines' own amounts before tax
/// and tax, so the invoice total is what the buyer paid; per-rate totals and the amount in words are filled as the
/// document asks. Buyers follow <see cref="OrderInvoiceRules.Build"/>: a company by legal name and tax code, a retail buyer
/// by name, no buyer as "Người mua không lấy hóa đơn".
/// </summary>
public static class EasyInvoiceXml
{
    public const string PaymentTransfer = "Chuyển khoản";

    /// <summary>EasyInvoice symbol for Thông tư 78: mẫu số + ký hiệu in Pattern ("1" + "C26TAA" = "1C26TAA"), Serial empty.</summary>
    public static string Pattern(CustomerInvoiceSettings s) => (s.InvoicePattern ?? "").Trim() + (s.InvoiceSeries ?? "").Trim().ToUpperInvariant();

    /// <summary>VATRate of EasyInvoice: 0/5/8/10, -1 KCT, -2 KKKNT (same codes as the order lines).</summary>
    public static int VatRate(string? code) => int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r is 0 or 5 or 8 or 10 or -1 or -2 ? r : 0;

    public static (decimal Before, decimal Tax) Amounts(OrderInvoiceLine l)
    {
        var before = l.AmountBeforeTax ?? Math.Round(l.Quantity * l.UnitPrice, 0, MidpointRounding.AwayFromZero);
        var percent = Math.Max(0, VatRate(l.TaxRateCode));
        return (before, l.TaxAmount ?? Math.Round(before * percent / 100m, 0, MidpointRounding.AwayFromZero));
    }

    public static string Build(OrderInvoiceJob job, DateOnly invoiceDate, bool sendEmail)
    {
        var company = job.BuyerType is "BUSINESS" or "ORGANIZATION" || (job.BuyerType is null && job.BuyerTaxCode is not null);
        var taxCode = Blank(job.BuyerTaxCode);
        var legal = company || taxCode is not null ? Blank(job.BuyerLegalName) ?? Blank(job.BuyerName) : null;
        var buyer = company ? Blank(job.BuyerName) is string n && n != legal ? n : null : Blank(job.BuyerName) ?? (legal is null ? OrderInvoiceRules.AnonymousBuyer : null);
        var note = $"Thanh toán đơn hàng {job.OrderNo}" + (Blank(job.Description) is string d ? $" — {d}" : "");

        var lines = job.Lines.OrderBy(l => l.LineNumber).Select((l, i) => (Line: l, No: i + 1, Rate: VatRate(l.TaxRateCode), Amounts: Amounts(l))).ToList();
        var products = lines.Select(x => new XElement("Product",
            new XElement("No", x.No),
            new XElement("Feature", 1),
            new XElement("ProdName", Cut(x.Line.ItemName.Trim(), 500)),
            new XElement("ProdUnit", Cut(Blank(x.Line.UnitName) ?? "", 50)),
            new XElement("ProdQuantity", Num(x.Line.Quantity)),
            new XElement("ProdPrice", Num(x.Line.UnitPrice)),
            new XElement("Total", Num(x.Amounts.Before)),
            new XElement("VATRate", x.Rate),
            new XElement("VATAmount", Num(x.Amounts.Tax)),
            new XElement("Amount", Num(x.Amounts.Before + x.Amounts.Tax))));

        decimal Before(int rate) => lines.Where(x => x.Rate == rate).Sum(x => x.Amounts.Before);
        decimal Tax(int rate) => lines.Where(x => x.Rate == rate).Sum(x => x.Amounts.Tax);
        var total = lines.Sum(x => x.Amounts.Before);
        var vat = lines.Sum(x => x.Amounts.Tax);
        var rates = lines.Select(x => x.Rate).Distinct().ToList();
        // One rate on the whole invoice: that rate. Several: the highest percent (an open question to SoftDreams, #13).
        var invoiceRate = rates.Count == 1 ? rates[0] : rates.Where(r => r >= 0).DefaultIfEmpty(rates.Max()).Max();

        var invoice = new XElement("Invoice",
            new XElement("Ikey", job.InvoiceCode),
            buyer is null ? null : new XElement("Buyer", Cut(buyer, 100)),
            legal is null ? null : new XElement("CusName", Cut(legal, 400)),
            sendEmail && Blank(job.BuyerEmail) is string email ? new XElement("CusEmails", Cut(email, 255)) : null,
            Blank(job.BuyerAddress) is string address ? new XElement("CusAddress", Cut(address, 400)) : null,
            Blank(job.BuyerPhone) is string phone ? new XElement("CusPhone", Cut(phone, 20)) : null,
            taxCode is null ? null : new XElement("CusTaxCode", taxCode),
            new XElement("PaymentMethod", PaymentTransfer),
            new XElement("ArisingDate", invoiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
            new XElement("ExchangeRate", 1),
            new XElement("CurrencyUnit", "VND"),
            new XElement("Note", Cut(note, 500)),
            new XElement("Products", products),
            new XElement("Total", Num(total)),
            new XElement("VATRate", invoiceRate),
            new XElement("VATAmount", Num(vat)),
            new XElement("Amount", Num(total + vat)),
            new XElement("GrossValue", Num(Before(-1))),
            new XElement("GrossValue0", Num(Before(0))),
            new XElement("GrossValue5", Num(Before(5))),
            new XElement("GrossValue8", Num(Before(8))),
            new XElement("GrossValue10", Num(Before(10))),
            new XElement("GrossValueNDeclared", Num(Before(-2))),
            new XElement("VatAmount0", Num(Tax(0))),
            new XElement("VatAmount5", Num(Tax(5))),
            new XElement("VatAmount8", Num(Tax(8))),
            new XElement("VatAmount10", Num(Tax(10))),
            new XElement("Amount0", Num(Before(0) + Tax(0))),
            new XElement("Amount5", Num(Before(5) + Tax(5))),
            new XElement("Amount8", Num(Before(8) + Tax(8))),
            new XElement("Amount10", Num(Before(10) + Tax(10))),
            new XElement("AmountNDeclared", Num(Before(-2) + Tax(-2))),
            new XElement("AmountInWords", VietnameseMoney.Words(total + vat)));
        return new XElement("Invoices", new XElement("Inv", invoice)).ToString(SaveOptions.DisableFormatting);
    }

    private static string Num(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Amount in Vietnamese words for invoices: 1 250 000 → "Một triệu hai trăm năm mươi nghìn đồng".</summary>
public static class VietnameseMoney
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Groups = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string Words(decimal amount)
    {
        var n = (long)Math.Round(Math.Abs(amount), 0, MidpointRounding.AwayFromZero);
        if (n == 0) return "Không đồng";
        var parts = new List<long>();
        for (var v = n; v > 0; v /= 1000) parts.Add(v % 1000);
        var words = new List<string>();
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (parts[i] == 0) continue;
            words.Add(Three((int)parts[i], full: i < parts.Count - 1));
            if (Groups[i].Length > 0) words.Add(Groups[i]);
        }
        var text = string.Join(" ", words);
        return (amount < 0 ? "Âm " + text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("vi-VN")) + text[1..]) + " đồng";
    }

    private static string Three(int n, bool full)
    {
        int h = n / 100, t = n / 10 % 10, u = n % 10;
        var w = new List<string>();
        if (full || h > 0) w.Add(Digits[h] + " trăm");
        if (t == 0)
        {
            if (u > 0) w.Add(full || h > 0 ? "linh " + Digits[u] : Digits[u]);
        }
        else if (t == 1) w.Add("mười" + (u == 5 ? " lăm" : u > 0 ? " " + Digits[u] : ""));
        else w.Add(Digits[t] + " mươi" + u switch { 0 => "", 1 => " mốt", 4 => " tư", 5 => " lăm", _ => " " + Digits[u] });
        return string.Join(" ", w);
    }
}

/// <summary>Encrypts the provider login kept for EasyInvoice. DPAPI LocalMachine: the Customer Portal writes it, the webhook site's worker reads it.</summary>
public interface IInvoiceSecretProtector
{
    byte[] Protect(EasyInvoiceSecret secret);
    /// <summary>Null when there is nothing stored or it cannot be decrypted on this machine.</summary>
    EasyInvoiceSecret? Unprotect(byte[]? data);
}

/// <summary>What is kept encrypted for an EasyInvoice connection.</summary>
public sealed record EasyInvoiceSecret(string Password, bool Demo)
{
    public override string ToString() => $"EasyInvoiceSecret {{ Demo = {Demo} }}";
}

[SupportedOSPlatform("windows")]
public sealed class DpapiInvoiceSecretProtector : IInvoiceSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TAPortal.EInvoiceProviderLogin.v1");

    public byte[] Protect(EasyInvoiceSecret secret) =>
        ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(secret), Entropy, DataProtectionScope.LocalMachine);

    public EasyInvoiceSecret? Unprotect(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        try { return JsonSerializer.Deserialize<EasyInvoiceSecret>(ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { return null; }
    }
}
