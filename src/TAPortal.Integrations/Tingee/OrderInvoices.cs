using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TAPortal.Integrations.Tingee;

// Automatic e-invoice for paid QR orders (operator request 2026-11-06, MISA meInvoice only): a company connects its own MISA
// account and picks a template on the Customer Portal; when an order with "tự động xuất hóa đơn" is fully paid, the webhook
// site's worker issues the invoice through Tingee's e-invoice API. The invoice code is fixed per order, so a retry after a
// lost answer can never issue a second invoice (Tingee answers 2034 "already issued").

/// <summary>dbo.CustomerInvoiceSettings (migration 022). The provider password goes to the partner only and is never stored.</summary>
public sealed record CustomerInvoiceSettings(
    Guid CustomerId, string Provider, long? ProviderAccountId, string? ProviderTaxCode, string? ProviderUsername, string? InvoicePattern, string? InvoiceSeries,
    bool AutoIssueEnabled, bool SendEmail, DateTime? AutoIssueFrom = null, DateTime? ConnectedAt = null)
{
    public static CustomerInvoiceSettings Empty(Guid customerId) => new(customerId, TingeeInvoiceProviders.Misa, null, null, null, null, null, false, true);

    public bool IsConnected => ProviderAccountId is > 0;
    public bool HasTemplate => !string.IsNullOrWhiteSpace(InvoicePattern) && !string.IsNullOrWhiteSpace(InvoiceSeries);
    /// <summary>Connected with a template: invoices can be issued (by hand, or automatically when <see cref="AutoIssueEnabled"/>).</summary>
    public bool IsReady => IsConnected && HasTemplate;
}

/// <summary>dbo.CustomerOrders.InvoiceStatus.</summary>
public static class OrderInvoiceStatus
{
    public const string None = "NONE", Pending = "PENDING", Issued = "ISSUED", Failed = "FAILED";
}

public sealed record OrderInvoiceLine(int LineNumber, string ItemName, string? UnitName, decimal Quantity, decimal UnitPrice, string TaxRateCode);

/// <summary>Everything needed to issue one order's invoice, read when the order is claimed.</summary>
public sealed record OrderInvoiceJob(
    Guid CustomerId, Guid OrderId, string OrderNo, string? Description, string InvoiceCode, int Attempts, long? MerchantId,
    string? BuyerType, string? BuyerName, string? BuyerLegalName, string? BuyerTaxCode, string? BuyerAddress, string? BuyerPhone, string? BuyerEmail,
    IReadOnlyList<OrderInvoiceLine> Lines);

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
    /// <summary>Leaves the order PENDING and tries again from <paramref name="nextAttemptAt"/>.</summary>
    Task RetryLaterAsync(Guid customerId, Guid orderId, DateTime nextAttemptAt, string error, CancellationToken ct = default);
    Task FailAsync(Guid customerId, Guid orderId, string error, CancellationToken ct = default);
    Task MarkEmailedAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
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
/// FAILED with the reason, for the company to fix its settings and issue again.
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
        if (!settings.IsReady) return new(OrderInvoiceStatus.None, "Chưa cài đặt hóa đơn điện tử: kết nối tài khoản MISA meInvoice và chọn mẫu hóa đơn trước.");
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
        OrderInvoiceIssued result;
        try
        {
            var issued = await invoices.CreateAndIssueAsync(request, ct);
            result = new(issued.InvoiceNumber, issued.ReservationCode, issued.TrackingCode, false);
        }
        catch (TingeeApiException api) when (api.Code == "2034")
        {
            // Issued by an earlier attempt whose answer was lost: the fixed invoice code proves it.
            result = new(null, null, null, true);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException or TingeeNotConfiguredException && !ct.IsCancellationRequested)
        {
            var reason = Reason(ex);
            var permanent = ex is TingeeApiException a && OrderInvoiceRules.IsPermanent(a.Code) || ex is TingeeNotConfiguredException;
            logger.LogWarning(ex, "ORDER_INVOICE_NOT_ISSUED order={OrderNo} code={Code} attempt={Attempt} manual={Manual}", job.OrderNo, job.InvoiceCode, job.Attempts, manual);
            if (!manual && !permanent && job.Attempts < MaxAutoAttempts)
            {
                await store.RetryLaterAsync(customerId, orderId, now.UtcDateTime + OrderInvoiceRules.Backoff(job.Attempts), reason, ct);
                return new(OrderInvoiceStatus.Pending, reason);
            }
            await store.FailAsync(customerId, orderId, reason, ct);
            return new(OrderInvoiceStatus.Failed, reason);
        }

        string? note = result.AlreadyIssued ? "Hóa đơn đã được phát hành ở lần xuất trước." : null;
        await store.CompleteAsync(customerId, orderId, result, note, ct);
        logger.LogInformation("ORDER_INVOICE_ISSUED order={OrderNo} code={Code} number={Number} again={Again} manual={Manual}", job.OrderNo, job.InvoiceCode, result.InvoiceNumber, result.AlreadyIssued, manual);

        if (settings.SendEmail && job.BuyerEmail is { Length: > 0 } email && !result.AlreadyIssued)
        {
            try
            {
                await invoices.SendEmailAsync(job.MerchantId.Value, job.InvoiceCode, email, request.BuyerLegalName ?? request.BuyerName, settings.ProviderAccountId, ct);
                await store.MarkEmailedAsync(customerId, orderId, ct);
            }
            catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ORDER_INVOICE_EMAIL_FAILED order={OrderNo}", job.OrderNo);
                await store.CompleteAsync(customerId, orderId, result, "Đã xuất hóa đơn nhưng chưa gửi được email cho người mua: " + Reason(ex), ct);
            }
        }
        return new(OrderInvoiceStatus.Issued, result.InvoiceNumber is string n ? $"Đã xuất hóa đơn số {n}." : "Đã xuất hóa đơn.", result.InvoiceNumber);
    }

    /// <summary>Text stored on the order and shown to the company (the portal removes partner names before showing it).</summary>
    public static string Reason(Exception ex)
    {
        var text = ex switch
        {
            TingeeApiException api => $"Nhà cung cấp hóa đơn từ chối (mã {api.Code}): {api.TingeeMessage}{Hint(api.Code)}",
            TingeeNotConfiguredException => "Dịch vụ hóa đơn chưa sẵn sàng. Vui lòng liên hệ T.A.",
            _ => "Không kết nối được dịch vụ hóa đơn, sẽ thử lại."
        };
        return text.Length <= 480 ? text : text[..480];
    }

    private static string Hint(string code) => code switch
    {
        "2032" => " — chưa có tài khoản hóa đơn mặc định, kết nối lại tài khoản MISA",
        "2037" => " — sai thông tin đăng nhập MISA hoặc tài khoản chưa đăng ký hóa đơn",
        "1057" => " — tài khoản MISA đã bị gỡ, kết nối lại",
        "2038" => " — loại dòng hàng không được hỗ trợ",
        _ => ""
    };
}
