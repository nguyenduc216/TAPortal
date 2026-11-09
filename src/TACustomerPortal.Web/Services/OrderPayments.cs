using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Services;

public static class OrderMath
{
    public static readonly IReadOnlyList<(string Code, string Label)> TaxRates =
        [("0", "0%"), ("5", "5%"), ("8", "8%"), ("10", "10%"), ("-1", "Không chịu thuế"), ("-2", "Không kê khai nộp thuế")];

    public static bool IsTaxRate(string? code) => TaxRates.Any(t => t.Code == code);

    /// <summary>Tax percent of a rate code (0 for 0%, KCT and KKKNT).</summary>
    public static int Percent(string? taxRate) => int.TryParse(taxRate, out var p) && p > 0 ? p : 0;

    /// <summary>
    /// Unit price before tax of a price that includes VAT, in whole dong (catalogue prices include VAT; invoices take the
    /// price before tax): 100 000 at 8% → 92 593.
    /// </summary>
    public static decimal PriceBeforeTax(decimal priceWithTax, string? taxRate) =>
        Math.Round(priceWithTax * 100m / (100 + Percent(taxRate)), 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A line priced including VAT: the customer pays exactly quantity × price (2 × 35 000 = 70 000, not 69 999 from a
    /// rounded price before tax); the tax is the part of that total above the amount before tax. The unit price stored
    /// (and sent to the invoice) is the rounded price before tax.
    /// </summary>
    public static OrderLine LineWithTax(int number, string item, string? unit, decimal quantity, decimal priceWithTax, string taxRate)
    {
        var total = Math.Round(quantity * priceWithTax, 0, MidpointRounding.AwayFromZero);
        var before = PriceBeforeTax(total, taxRate);
        return new(number, item.Trim(), string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), quantity, PriceBeforeTax(priceWithTax, taxRate), taxRate, before, total - before);
    }

    public static OrderLine Line(int number, string item, string? unit, decimal quantity, decimal unitPrice, string taxRate)
    {
        var before = Math.Round(quantity * unitPrice, 0, MidpointRounding.AwayFromZero);
        var percent = int.TryParse(taxRate, out var p) && p > 0 ? p : 0;
        return new(number, item.Trim(), string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), quantity, unitPrice, taxRate, before, Math.Round(before * percent / 100m, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Open orders past their QR expiry with money still due are shown (and stored on the next change) as expired.</summary>
    public static string EffectiveStatus(Order o, DateTime utcNow) =>
        OrderStatus.IsOpen(o.Status) && o.Status != OrderStatus.Draft && o.ExpiresAt is { } expires && expires <= utcNow && o.PaidAmount < o.Amount ? OrderStatus.Expired : o.Status;

    /// <summary>
    /// Transfer content for an order's QR: the order number with letters and digits only (DH261105-0001 → DH2611050001).
    /// Banks reject other characters in the payment purpose (Tingee 400 "Purpose of transaction is wrong format").
    /// </summary>
    public static string TransferContent(string orderNo) => new(orderNo.Where(char.IsAsciiLetterOrDigit).ToArray());

    public static string StatusFor(decimal amount, decimal paid) => paid >= amount ? OrderStatus.Paid : paid > 0 ? OrderStatus.Partial : OrderStatus.Awaiting;
}

public sealed record OrderPaymentResult(Order Order, string? Error);

/// <summary>
/// QR lifecycle of an order (operator decisions 2026-10-30): a dynamic recurring QR accepts several payments until the
/// order is fully paid, then the QR is closed; banks without dynamic QR get a static VietQR whose content is the order number.
/// Tingee failures never lose the order: it stays DRAFT/open with the error shown and can be retried.
/// </summary>
public sealed class OrderPayments(IPaymentStore store, ITingeeApi tingee, TimeProvider clock, ILogger<OrderPayments> logger)
{
    public async Task<OrderPaymentResult> IssueQrAsync(Guid customerId, long merchantId, Order order, BankAccount bank, Guid actor)
    {
        if (!OrderStatus.IsOpen(order.Status) && order.Status != OrderStatus.Expired) return new(order, "Đơn hàng không còn mở để tạo QR.");
        if (bank.ReceiveAccount is not string account || !bank.IsActive) return new(order, "Tài khoản nhận tiền chưa hoạt động.");
        var now = clock.GetUtcNow().UtcDateTime;
        var due = order.Remaining;
        Order updated;
        try
        {
            if (TingeeBankCatalog.SupportsDynamicQr(bank.BankBin, bank.AccountType) && bank.VaAccountNumber is not null)
            {
                var qr = await tingee.GenerateDynamicQrAsync(new TingeeDynamicQrRequest
                {
                    MerchantId = merchantId, VaAccountNumber = bank.VaAccountNumber, BankBin = bank.BankBin, Amount = due,
                    QrCodeType = TingeeQrTypes.Recurring, ExpireInMinute = order.QrExpireMinutes, Purpose = OrderMath.TransferContent(order.OrderNo), ExtraInfo = order.OrderNo
                });
                updated = order with { QrType = TingeeQrTypes.Recurring, QrPayload = qr.QrCode, QrAccount = qr.QrAccount, BillId = qr.BillId };
            }
            else
            {
                var qr = await tingee.GenerateStaticQrAsync(new TingeeStaticQrRequest(bank.BankBin, account, due, OrderMath.TransferContent(order.OrderNo)));
                updated = order with { QrType = TingeeQrTypes.Static, QrPayload = qr.QrCode, QrAccount = account, BillId = null };
            }
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "QR not issued for order {OrderNo}.", order.OrderNo);
            var error = ex is TingeeApiException t ? PartnerText.Scrub($"Không tạo được QR (mã {t.Code}): {t.TingeeMessage}")
                : ex is TingeeNotConfiguredException ? "Dịch vụ thanh toán chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động." : "Không kết nối được dịch vụ thanh toán.";
            var failed = order with { LastError = error };
            await store.UpdateOrderAsync(customerId, failed, actor);
            return new(failed, error);
        }
        if (string.IsNullOrEmpty(updated.QrPayload)) return new(order, "Không nhận được mã QR, vui lòng thử lại.");
        updated = updated with { Status = OrderMath.StatusFor(order.Amount, order.PaidAmount), ExpiresAt = now.AddMinutes(order.QrExpireMinutes), LastError = null };
        await store.UpdateOrderAsync(customerId, updated, actor);
        return new(updated, null);
    }

    /// <summary>
    /// Reads the amount paid on the dynamic QR from Tingee and stores it next to the system's own count (operator decision
    /// 2026-11-05): money is only counted from bank notifications (webhook site) or staff reconciliation, so this check can
    /// never count a payment twice; a difference is shown for reconciliation. Static QR orders have nothing to read.
    /// </summary>
    public async Task<OrderPaymentResult> RefreshAsync(Guid customerId, long merchantId, Order order, Guid actor)
    {
        if (order.QrType != TingeeQrTypes.Recurring || order.QrAccount is null || order.BillId is null)
            return new(order, "Đơn dùng QR tĩnh: trạng thái được cập nhật khi nhận thông báo giao dịch từ ngân hàng.");
        try
        {
            var status = await tingee.GetDynamicQrStatusAsync(merchantId, order.QrAccount, order.BillId);
            // A QR re-issued for the remainder carries Amount = what was still due; money paid before it is Amount(order) - Amount(QR).
            var bill = status.BillInfo;
            var paidBefore = bill is { Amount: > 0 } && bill.Amount < order.Amount ? order.Amount - bill.Amount : 0;
            var partner = paidBefore + (bill?.TotalAmountPaid ?? 0);
            await store.RecordPartnerPaidAsync(customerId, order.Id, partner);
            var updated = await store.OrderAsync(customerId, order.Id) ?? order with { PartnerPaidAmount = partner };
            return new(updated, null);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "QR status unavailable for order {OrderNo}.", order.OrderNo);
            return new(order, ex is TingeeApiException t ? PartnerText.Scrub($"Không kiểm tra được trạng thái QR (mã {t.Code}): {t.TingeeMessage}") : "Không kiểm tra được trạng thái QR lúc này.");
        }
    }

    public async Task<OrderPaymentResult> CancelAsync(Guid customerId, long? merchantId, Order order, Guid actor)
    {
        if (order.Status is OrderStatus.Paid or OrderStatus.Cancelled) return new(order, "Đơn hàng đã hoàn tất hoặc đã hủy.");
        if (order.PaidAmount > 0) return new(order, "Đơn hàng đã nhận một phần tiền: không thể hủy trên Portal, vui lòng xử lý hoàn tiền/đối soát trước.");
        if (merchantId is long id) await CloseQrAsync(id, order);
        var cancelled = order with { Status = OrderStatus.Cancelled, LastError = null };
        await store.UpdateOrderAsync(customerId, cancelled, actor);
        return new(cancelled, null);
    }

    // Best effort: Tingee only deletes MB/BIDV/VietinBank dynamic QRs and answers success for inactive ones.
    private async Task CloseQrAsync(long merchantId, Order order)
    {
        if (order.QrType != TingeeQrTypes.Recurring || order.QrAccount is null || order.BillId is null) return;
        try { await tingee.DeleteDynamicQrAsync(merchantId, order.QrAccount, order.BillId); }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogInformation(ex, "Dynamic QR for order {OrderNo} not closed on Tingee.", order.OrderNo);
        }
    }
}
