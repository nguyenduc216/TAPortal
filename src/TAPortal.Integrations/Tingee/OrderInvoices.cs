using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

// Automatic e-invoice for paid QR orders (operator request 2026-11-06, MISA meInvoice; Viettel SInvoice added 2026-11-23): a
// company connects its own provider account and picks a template on the Customer Portal; when an order with "tự động xuất hóa đơn" is fully paid, the webhook
// site's worker issues the invoice through Tingee's e-invoice API. The invoice code is fixed per order, so a retry after a
// lost answer can never issue a second invoice (Tingee answers 2034 "already issued").

/// <summary>dbo.CustomerInvoiceSettings (migration 022). The provider password goes to the partner only and is never stored.</summary>
public sealed record CustomerInvoiceSettings(
    Guid CustomerId, string Provider, long? ProviderAccountId, string? ProviderTaxCode, string? ProviderUsername, string? InvoicePattern, string? InvoiceSeries,
    bool AutoIssueEnabled, bool SendEmail, DateTime? AutoIssueFrom = null, DateTime? ConnectedAt = null,
    string? IssueMode = null, DateTime? HsmConfirmedAt = null, Guid? HsmConfirmedBy = null)
{
    public static CustomerInvoiceSettings Empty(Guid customerId) => new(customerId, TingeeInvoiceProviders.Misa, null, null, null, null, null, false, true);

    public bool IsConnected => ProviderAccountId is > 0;
    public bool HasTemplate => !string.IsNullOrWhiteSpace(InvoicePattern) && !string.IsNullOrWhiteSpace(InvoiceSeries);
    /// <summary>Connected with a template: invoices can be issued (by hand, or automatically when <see cref="AutoIssueEnabled"/>).</summary>
    public bool IsReady => IsConnected && HasTemplate;
    /// <summary>How paid orders are invoiced (migration 027); settings saved before it follow <see cref="AutoIssueEnabled"/>.</summary>
    public string Mode => IssueMode ?? (AutoIssueEnabled ? InvoiceIssueModes.Signed : InvoiceIssueModes.None);
    /// <summary>The company's invoice provider (MISA when the stored code is unknown).</summary>
    public CustomerInvoiceProvider Info => CustomerInvoiceProviders.Get(Provider);
}

/// <summary>An e-invoice provider a company can connect on the Customer Portal (dbo.CustomerInvoiceSettings.Provider, CK_CIS_Provider).</summary>
/// <param name="Short">Short name in messages ("sai thông tin đăng nhập MISA").</param>
/// <param name="Signer">The provider's remote-signing (HSM) product needed for automatic signed invoices.</param>
/// <param name="SendsEmail">The provider emails the invoice to the buyer through the API (Tingee: only XCyber and MISA).</param>
public sealed record CustomerInvoiceProvider(string Code, string Name, string Short, string Signer, bool SendsEmail);

/// <summary>
/// Providers offered to companies (operator 2026-11-23: MISA meInvoice and Viettel SInvoice; VNPT is not offered by the
/// e-invoice partner and needs its own integration). Codes are the partner's provider codes.
/// </summary>
public static class CustomerInvoiceProviders
{
    public static readonly CustomerInvoiceProvider Misa = new(TingeeInvoiceProviders.Misa, "MISA meInvoice", "MISA", "chữ ký số HSM của MISA (MISA eSign)", true);
    public static readonly CustomerInvoiceProvider Viettel = new(TingeeInvoiceProviders.SInvoice, "Viettel SInvoice", "Viettel", "chữ ký số từ xa của Viettel-CA", false);
    public static readonly IReadOnlyList<CustomerInvoiceProvider> All = [Misa, Viettel];

    public static bool IsSupported(string? code) => All.Any(p => p.Code == code);
    public static CustomerInvoiceProvider Get(string? code) => All.FirstOrDefault(p => p.Code == code) ?? Misa;
}

/// <summary>When the company's invoices were last issued (signed) and last left as a draft because signing failed.</summary>
public sealed record InvoiceSigningHealth(DateTime? LastIssuedAt, DateTime? LastSigningFailedAt)
{
    public static readonly InvoiceSigningHealth None = new(null, null);
}

/// <summary>One line of the "Sẵn sàng xuất tự động" checklist. State: ok, warn (works but needs attention) or todo (missing).</summary>
public sealed record InvoiceReadinessItem(string Key, string State, string Title, string Detail)
{
    public const string Ok = "ok", Warn = "warn", Todo = "todo";
}

/// <summary>
/// What still stands between the company and fully automatic signed invoices (operator 2026-11-23): an account, a
/// template, a series of the current year (ký hiệu năm: C26TAA is 2026 only), a remote signature and its last result.
/// Computed from the stored settings only: nothing is sent to the provider or the tax authority.
/// </summary>
public static class InvoiceReadiness
{
    private static readonly System.Text.RegularExpressions.Regex SeriesYear = new(@"[CK](\d{2})[A-Z][A-Z0-9]{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The two-digit year of a series such as C26TAA or 1C26TAA; null when it does not follow Thông tư 78.</summary>
    public static int? Year(string? series) =>
        series is not null && SeriesYear.Match(series.Trim().ToUpperInvariant()) is { Success: true } m ? 2000 + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    public static bool FullyAutomatic(IReadOnlyList<InvoiceReadinessItem> items) => items.All(i => i.State == InvoiceReadinessItem.Ok);

    public static IReadOnlyList<InvoiceReadinessItem> Check(CustomerInvoiceSettings s, DateOnly today, InvoiceSigningHealth health)
    {
        var p = s.Info;
        var items = new List<InvoiceReadinessItem>
        {
            s.IsConnected
                ? new("account", InvoiceReadinessItem.Ok, $"Đã kết nối {p.Name}", $"Mã số thuế {s.ProviderTaxCode}.")
                : new("account", InvoiceReadinessItem.Todo, $"Kết nối tài khoản {p.Name}", "Doanh nghiệp cần có hợp đồng hóa đơn điện tử và đã được cơ quan thuế chấp nhận đăng ký sử dụng (Mẫu 01/ĐKTĐ-HĐĐT)."),
            s.HasTemplate
                ? new("template", InvoiceReadinessItem.Ok, $"Đã chọn mẫu hóa đơn, ký hiệu {s.InvoiceSeries}", "Dùng \"Xem thử hóa đơn nháp\" để kiểm tra mẫu trước khi bật tự động.")
                : new("template", InvoiceReadinessItem.Todo, "Chọn mẫu hóa đơn", $"Bấm \"Lấy mẫu hóa đơn\" để chọn mẫu số và ký hiệu đã đăng ký trên {p.Name}.")
        };
        if (s.HasTemplate && Year(s.InvoiceSeries) is int year)
        {
            if (year < today.Year)
                items.Add(new("series-year", InvoiceReadinessItem.Todo, $"Ký hiệu {s.InvoiceSeries} là của năm {year}",
                    $"Hóa đơn lập năm {today.Year} phải dùng ký hiệu năm {today.Year} (2 số {today.Year % 100:00} sau chữ C/K). Tạo ký hiệu mới trên {p.Name} rồi chọn lại mẫu."));
            else if (year == today.Year && today.Month == 12)
                items.Add(new("series-year", InvoiceReadinessItem.Warn, $"Sắp sang năm {today.Year + 1}: chuẩn bị ký hiệu mới",
                    $"Từ 01/01/{today.Year + 1}, ký hiệu {s.InvoiceSeries} không dùng được nữa. Tạo ký hiệu năm {today.Year + 1} trên {p.Name} và chọn lại mẫu ngay đầu năm."));
            else
                items.Add(new("series-year", InvoiceReadinessItem.Ok, $"Ký hiệu đúng năm {year}", "Hằng năm, đầu tháng 1 cần chuyển sang ký hiệu của năm mới."));
        }
        items.Add(s.Mode switch
        {
            InvoiceIssueModes.Signed when health.LastSigningFailedAt is DateTime failed && (health.LastIssuedAt is not DateTime ok || failed > ok) =>
                new("signing", InvoiceReadinessItem.Todo, "Lần xuất gần nhất chưa ký số được",
                    $"Lúc {failed.AddHours(7):HH:mm dd/MM/yyyy} {p.Name} không ký được hóa đơn nên chỉ tạo bản nháp. Kiểm tra: đã có {p.Signer} loại cho phép ký tự động, đã liên kết với tài khoản {p.Name}, và chứng thư số đã được đăng ký với cơ quan thuế."),
            InvoiceIssueModes.Signed when health.LastIssuedAt is DateTime ok =>
                new("signing", InvoiceReadinessItem.Ok, "Ký số và phát hành tự động đang hoạt động", $"Hóa đơn gần nhất được ký lúc {ok.AddHours(7):HH:mm dd/MM/yyyy}."),
            InvoiceIssueModes.Signed =>
                new("signing", InvoiceReadinessItem.Warn, "Đã xác nhận có chữ ký số từ xa, chưa có hóa đơn nào được ký tự động",
                    $"Hóa đơn đầu tiên sẽ cho biết chữ ký đã dùng được chưa. Nếu không ký được, hệ thống tự tạo bản nháp và báo tại đây."),
            InvoiceIssueModes.Draft =>
                new("signing", InvoiceReadinessItem.Warn, "Đang xuất hóa đơn nháp, kế toán ký tay",
                    $"Để ký và phát hành tự động hoàn toàn, doanh nghiệp cần {p.Signer}, rồi chọn \"Đã có chữ ký số từ xa\"."),
            _ => new("signing", InvoiceReadinessItem.Todo, "Chưa bật xuất hóa đơn tự động", "Chọn cách xuất hóa đơn ở mục 2.")
        });
        return items;
    }
}

/// <summary>
/// dbo.CustomerInvoiceSettings.IssueMode (operator 2026-11-07). MISA issues signed invoices through the API only with its HSM
/// signature: SIGNED = the company bought it (choosing it is recorded as HsmConfirmedAt/By), DRAFT = no HSM yet, drafts
/// are created for the accountant to approve and sign on MISA, NONE = no automatic invoice.
/// </summary>
public static class InvoiceIssueModes
{
    public const string Signed = "SIGNED", Draft = "DRAFT", None = "NONE";
    public static bool IsValid(string? mode) => mode is Signed or Draft or None;
}

/// <summary>dbo.CustomerOrders.InvoiceStatus. DRAFT (migration 026): the invoice could not be signed, so a draft with the
/// order's invoice code was created at the provider instead; it reaches the tax authority only once it is issued there.</summary>
public static class OrderInvoiceStatus
{
    public const string None = "NONE", Pending = "PENDING", Issued = "ISSUED", Failed = "FAILED", Draft = "DRAFT";
}

public sealed record OrderInvoiceLine(int LineNumber, string ItemName, string? UnitName, decimal Quantity, decimal UnitPrice, string TaxRateCode);

/// <summary>Everything needed to issue one order's invoice, read when the order is claimed.</summary>
public sealed record OrderInvoiceJob(
    Guid CustomerId, Guid OrderId, string OrderNo, string? Description, string InvoiceCode, int Attempts, long? MerchantId,
    string? BuyerType, string? BuyerName, string? BuyerLegalName, string? BuyerTaxCode, string? BuyerAddress, string? BuyerPhone, string? BuyerEmail,
    IReadOnlyList<OrderInvoiceLine> Lines, bool FromDraft = false);

public sealed record OrderInvoiceIssued(string? InvoiceNumber, string? LookupCode, string? TrackingCode, bool AlreadyIssued);

public interface IOrderInvoiceStore
{
    Task<CustomerInvoiceSettings> SettingsAsync(Guid customerId, CancellationToken ct = default);
    Task SaveSettingsAsync(CustomerInvoiceSettings settings, Guid actor, CancellationToken ct = default);
    /// <summary>Paid orders waiting for an automatic invoice: auto-issue on for the order and the company, paid since it was turned on.</summary>
    Task<IReadOnlyList<(Guid CustomerId, Guid OrderId)>> DueAsync(int max, DateTime utcNow, CancellationToken ct = default);
    /// <summary>
    /// Takes a paid order for issuing (status PENDING, invoice code fixed on first claim as <see cref="OrderInvoiceRules.InvoiceCode"/>,
    /// lease until <paramref name="leaseUntil"/>). Null when the order is not paid, already issued, failed (unless
    /// <paramref name="manual"/>) or held by another run.
    /// </summary>
    Task<OrderInvoiceJob?> ClaimAsync(Guid customerId, Guid orderId, DateTime utcNow, DateTime leaseUntil, bool manual, CancellationToken ct = default);
    Task CompleteAsync(Guid customerId, Guid orderId, OrderInvoiceIssued issued, string? note, CancellationToken ct = default);
    /// <summary>Signing failed and a draft was created at the provider: status DRAFT with <paramref name="note"/>, no automatic retry.</summary>
    Task DraftAsync(Guid customerId, Guid orderId, string note, CancellationToken ct = default);
    /// <summary>Leaves the order PENDING and tries again from <paramref name="nextAttemptAt"/>.</summary>
    Task RetryLaterAsync(Guid customerId, Guid orderId, DateTime nextAttemptAt, string error, CancellationToken ct = default);
    Task FailAsync(Guid customerId, Guid orderId, string error, CancellationToken ct = default);
    Task MarkEmailedAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
    /// <summary>Last signed invoice and last draft left because signing failed (<see cref="OrderInvoiceRules.SigningFailedPrefix"/>).</summary>
    Task<InvoiceSigningHealth> SigningHealthAsync(Guid customerId, CancellationToken ct = default);
}

public sealed record OrderInvoiceOutcome(string Status, string Message, string? InvoiceNumber = null)
{
    public bool Issued => Status == OrderInvoiceStatus.Issued;
}

public static class OrderInvoiceRules
{
    /// <summary>Retail buyer without details (Nghị định 123/2020: "Người mua không lấy hóa đơn").</summary>
    public const string AnonymousBuyer = "Người mua không lấy hóa đơn";
    /// <summary>Bank transfer: the order was paid by QR.</summary>
    public const int PaymentMethodTransfer = 2;

    /// <summary>Fixed per order: letters/digits of the order number plus the start of the order id (unique across companies).</summary>
    public static string InvoiceCode(string orderNo, Guid orderId) =>
        TingeePaymentMatcher.Key(orderNo) + "-" + orderId.ToString("N")[..8].ToUpperInvariant();

    /// <summary>Partner codes that retrying cannot fix: settings or data must change first.</summary>
    public static bool IsPermanent(string code) => code is "2032" or "2037" or "2038" or "2007" or "1057" or "1005" or "400" or "1001" or "1002";

    /// <summary>
    /// The provider could not sign the invoice (MISA answers 400 "CallSignServiceFail" when the company has no signing
    /// certificate usable for API issuing, e.g. no HSM): a draft can still be created (operator 2026-11-07).
    /// </summary>
    public static bool IsSigningFailure(TingeeApiException ex) => ex.TingeeMessage.Contains("SignService", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stored on a DRAFT order created on purpose (no HSM: drafts are approved and signed by hand).</summary>
    public static string ManualDraftNote(CustomerInvoiceProvider p) =>
        $"Đã tạo hóa đơn nháp trên {p.Name}, chờ duyệt và ký số thủ công (chưa gửi cơ quan thuế). Duyệt, ký và phát hành bản nháp trên {p.Short}.";

    /// <summary>Start of <see cref="DraftNote"/>: the readiness check finds signing failures by it (SqlOrderInvoiceStore.SigningHealthAsync).</summary>
    public const string SigningFailedPrefix = "Chưa ký số được";

    /// <summary>Stored on a DRAFT order whose signing failed, and shown to the company.</summary>
    public static string DraftNote(CustomerInvoiceProvider p) =>
        $"{SigningFailedPrefix} nên đã tạo hóa đơn nháp trên {p.Name} (chưa gửi cơ quan thuế). Ký và phát hành bản nháp trên {p.Short}, hoặc cài chữ ký số từ xa dùng được cho phát hành tự động rồi bấm Xuất lại hóa đơn.";

    /// <summary>Wait before the next automatic attempt: 1, 2, 4, 8 … minutes, at most an hour.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempts - 1))));

    /// <summary>
    /// The invoice for one paid order. Businesses and organisations are invoiced to their legal name and tax code; retail
    /// buyers by name (a household with a tax code too); an order without a buyer to "Người mua không lấy hóa đơn".
    /// </summary>
    public static TingeeInvoiceRequest Build(OrderInvoiceJob job, CustomerInvoiceSettings settings, DateOnly invoiceDate)
    {
        var company = job.BuyerType is "BUSINESS" or "ORGANIZATION" || (job.BuyerType is null && job.BuyerTaxCode is not null);
        var taxCode = Blank(job.BuyerTaxCode);
        var note = $"Thanh toán đơn hàng {job.OrderNo}" + (Blank(job.Description) is string d ? $" — {d}" : "");
        return new TingeeInvoiceRequest
        {
            MerchantId = job.MerchantId,
            AccountId = settings.ProviderAccountId,
            InvoicePattern = settings.InvoicePattern!.Trim(),
            InvoiceSeries = settings.InvoiceSeries!.Trim(),
            InvoiceCode = job.InvoiceCode,
            InvoiceDate = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BuyerName = company ? null : Blank(job.BuyerName) ?? AnonymousBuyer,
            BuyerLegalName = company ? Blank(job.BuyerLegalName) ?? Blank(job.BuyerName) : taxCode is null ? null : Blank(job.BuyerLegalName) ?? Blank(job.BuyerName),
            BuyerTaxCode = taxCode,
            BuyerAddress = Blank(job.BuyerAddress),
            BuyerPhone = Blank(job.BuyerPhone),
            BuyerEmail = Blank(job.BuyerEmail),
            PaymentMethod = PaymentMethodTransfer,
            Note = note.Length <= 250 ? note : note[..250],
            Items = job.Lines.OrderBy(l => l.LineNumber).Select(l => new TingeeInvoiceItem
            {
                ItemName = l.ItemName.Trim(),
                UnitName = Blank(l.UnitName),
                Quantity = l.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
                UnitPrice = l.UnitPrice.ToString("0", CultureInfo.InvariantCulture),
                TaxRateCode = l.TaxRateCode
            }).ToList()
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Issues the invoice of a paid order: from the webhook site's worker (automatic) or the order page (by hand). Partner
/// failures never lose the order: a temporary one is retried with a growing delay (automatic) and a refusal is stored as
/// FAILED with the reason, for the company to fix its settings and issue again. When only the signing fails, a draft is
/// created instead and the order shows DRAFT, not an error (operator 2026-11-07).
/// </summary>
public sealed class OrderInvoicer(IOrderInvoiceStore store, ITingeeInvoiceApi invoices, TimeProvider clock, ILogger<OrderInvoicer> logger)
{
    public const int MaxAutoAttempts = 6;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);

    /// <summary>Issues up to <paramref name="max"/> due automatic invoices; returns how many were issued.</summary>
    public async Task<int> RunOnceAsync(int max = 20, CancellationToken ct = default)
    {
        var issued = 0;
        foreach (var (customerId, orderId) in await store.DueAsync(max, clock.GetUtcNow().UtcDateTime, ct))
        {
            try { if ((await IssueAsync(customerId, orderId, manual: false, ct)).Issued) issued++; }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "ORDER_INVOICE_FAILED order={Order}", orderId); }
        }
        return issued;
    }

    public async Task<OrderInvoiceOutcome> IssueAsync(Guid customerId, Guid orderId, bool manual, CancellationToken ct = default)
    {
        var settings = await store.SettingsAsync(customerId, ct);
        if (!settings.IsReady) return new(OrderInvoiceStatus.None, $"Chưa cài đặt hóa đơn điện tử: kết nối tài khoản {settings.Info.Name} và chọn mẫu hóa đơn trước.");
        var now = clock.GetUtcNow();
        var job = await store.ClaimAsync(customerId, orderId, now.UtcDateTime, now.UtcDateTime + Lease, manual, ct);
        if (job is null) return new(OrderInvoiceStatus.None, "Đơn chưa thanh toán đủ, đã có hóa đơn hoặc đang được xuất.");
        if (job.MerchantId is null)
        {
            const string notLinked = "Doanh nghiệp chưa được T.A kích hoạt dịch vụ thanh toán.";
            await store.FailAsync(customerId, orderId, notLinked, ct);
            return new(OrderInvoiceStatus.Failed, notLinked);
        }
        if (job.Lines.Count == 0)
        {
            const string empty = "Đơn hàng không có dòng hàng hóa/dịch vụ.";
            await store.FailAsync(customerId, orderId, empty, ct);
            return new(OrderInvoiceStatus.Failed, empty);
        }

        var request = OrderInvoiceRules.Build(job, settings, DateOnly.FromDateTime(now.ToOffset(TingeeSignature.VietnamOffset).DateTime));
        if (settings.Mode == InvoiceIssueModes.Draft)
        {
            // No remote signature: only a draft, for the accountant to approve and sign at the provider.
            try { await invoices.CreateDraftAsync(request, ct); }
            catch (TingeeApiException draft) when (draft.Code == "2034") { }   // the draft exists already (an earlier attempt)
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException && !ct.IsCancellationRequested)
            {
                return await NotIssued(customerId, orderId, job, manual, ex, now.UtcDateTime, ct, settings.Info);
            }
            return await Draft(customerId, orderId, job, manual, ct, OrderInvoiceRules.ManualDraftNote(settings.Info),
                $"Đã tạo hóa đơn nháp trên {settings.Info.Name}, chờ duyệt và ký số thủ công.");
        }
        OrderInvoiceIssued result;
        try
        {
            var issued = await invoices.CreateAndIssueAsync(request, ct);
            result = new(issued.InvoiceNumber, issued.ReservationCode, issued.TrackingCode, false);
        }
        catch (TingeeApiException api) when (api.Code == "2034" && job.FromDraft)
        {
            // The code is taken by this order's own draft: from here it can only be issued at the provider.
            logger.LogWarning(api, "ORDER_INVOICE_DRAFT_EXISTS order={OrderNo} code={Code}", job.OrderNo, job.InvoiceCode);
            return await Draft(customerId, orderId, job, manual, ct, settings.Info);
        }
        catch (TingeeApiException api) when (api.Code == "2034")
        {
            // Issued by an earlier attempt whose answer was lost: the fixed invoice code proves it.
            result = new(null, null, null, true);
        }
        catch (TingeeApiException api) when (OrderInvoiceRules.IsSigningFailure(api))
        {
            logger.LogWarning(api, "ORDER_INVOICE_NOT_SIGNED order={OrderNo} code={Code} manual={Manual}; creating a draft", job.OrderNo, job.InvoiceCode, manual);
            try { await invoices.CreateDraftAsync(request, ct); }
            catch (TingeeApiException draft) when (draft.Code == "2034") { }   // the draft exists already (an earlier attempt)
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException && !ct.IsCancellationRequested)
            {
                return await NotIssued(customerId, orderId, job, manual, ex, now.UtcDateTime, ct, settings.Info);
            }
            return await Draft(customerId, orderId, job, manual, ct, settings.Info);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException && !ct.IsCancellationRequested)
        {
            return await NotIssued(customerId, orderId, job, manual, ex, now.UtcDateTime, ct, settings.Info);
        }

        string? note = result.AlreadyIssued ? "Hóa đơn đã được phát hành ở lần xuất trước." : null;
        await store.CompleteAsync(customerId, orderId, result, note, ct);
        logger.LogInformation("ORDER_INVOICE_ISSUED order={OrderNo} code={Code} number={Number} again={Again} manual={Manual}", job.OrderNo, job.InvoiceCode, result.InvoiceNumber, result.AlreadyIssued, manual);

        // Only providers that email through the API (Viettel SInvoice does not: the buyer looks the invoice up instead).
        if (settings.SendEmail && settings.Info.SendsEmail && job.BuyerEmail is { Length: > 0 } email && !result.AlreadyIssued)
        {
            try
            {
                await invoices.SendEmailAsync(job.MerchantId.Value, job.InvoiceCode, email, request.BuyerLegalName ?? request.BuyerName, settings.ProviderAccountId, ct);
                await store.MarkEmailedAsync(customerId, orderId, ct);
            }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ORDER_INVOICE_EMAIL_FAILED order={OrderNo}", job.OrderNo);
                await store.CompleteAsync(customerId, orderId, result, "Đã xuất hóa đơn nhưng chưa gửi được email cho người mua: " + Reason(ex, settings.Info), ct);
            }
        }
        return new(OrderInvoiceStatus.Issued, result.InvoiceNumber is string n ? $"Đã xuất hóa đơn số {n}." : "Đã xuất hóa đơn.", result.InvoiceNumber);
    }

    private Task<OrderInvoiceOutcome> Draft(Guid customerId, Guid orderId, OrderInvoiceJob job, bool manual, CancellationToken ct, CustomerInvoiceProvider p) =>
        Draft(customerId, orderId, job, manual, ct, OrderInvoiceRules.DraftNote(p), $"Đã tạo hóa đơn nháp vì chưa ký số được hóa đơn. Ký và phát hành bản nháp trên {p.Name}.");

    private async Task<OrderInvoiceOutcome> Draft(Guid customerId, Guid orderId, OrderInvoiceJob job, bool manual, CancellationToken ct, string note, string message)
    {
        await store.DraftAsync(customerId, orderId, note, ct);
        logger.LogInformation("ORDER_INVOICE_DRAFT order={OrderNo} code={Code} manual={Manual}", job.OrderNo, job.InvoiceCode, manual);
        return new(OrderInvoiceStatus.Draft, message);
    }

    private async Task<OrderInvoiceOutcome> NotIssued(Guid customerId, Guid orderId, OrderInvoiceJob job, bool manual, Exception ex, DateTime utcNow, CancellationToken ct,
        CustomerInvoiceProvider p)
    {
        var reason = Reason(ex, p);
        var permanent = ex is TingeeApiException a && OrderInvoiceRules.IsPermanent(a.Code) || ex is TingeeNotConfiguredException;
        logger.LogWarning(ex, "ORDER_INVOICE_NOT_ISSUED order={OrderNo} code={Code} attempt={Attempt} manual={Manual}", job.OrderNo, job.InvoiceCode, job.Attempts, manual);
        if (!manual && !permanent && job.Attempts < MaxAutoAttempts)
        {
            await store.RetryLaterAsync(customerId, orderId, utcNow + OrderInvoiceRules.Backoff(job.Attempts), reason, ct);
            return new(OrderInvoiceStatus.Pending, reason);
        }
        await store.FailAsync(customerId, orderId, reason, ct);
        return new(OrderInvoiceStatus.Failed, reason);
    }

    /// <summary>Text stored on the order and shown to the company (the portal removes partner names before showing it).</summary>
    public static string Reason(Exception ex, CustomerInvoiceProvider? provider = null)
    {
        var text = ex switch
        {
            TingeeApiException api => $"Nhà cung cấp hóa đơn từ chối (mã {api.Code}): {api.TingeeMessage}{Hint(api.Code, (provider ?? CustomerInvoiceProviders.Misa).Short)}",
            TingeeNotConfiguredException => "Dịch vụ hóa đơn chưa sẵn sàng. Vui lòng liên hệ T.A.",
            _ => "Không kết nối được dịch vụ hóa đơn, sẽ thử lại."
        };
        return text.Length <= 480 ? text : text[..480];
    }

    private static string Hint(string code, string provider) => code switch
    {
        "2032" => $" — chưa có tài khoản hóa đơn mặc định, kết nối lại tài khoản {provider}",
        "2037" => $" — sai thông tin đăng nhập {provider} hoặc tài khoản chưa đăng ký hóa đơn",
        "1057" => $" — tài khoản {provider} đã bị gỡ, kết nối lại",
        "2007" => $" — {provider} không hỗ trợ chức năng này",
        "2038" => " — loại dòng hàng không được hỗ trợ",
        _ => ""
    };
}
