using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Services;

public static class OrderMath
{
    public static readonly IReadOnlyList<(string Code, string Label)> TaxRates =
        [("0", "0%"), ("5", "5%"), ("8", "8%"), ("10", "10%"), ("-1", "Không chịu thuế"), ("-2", "Không kê khai nộp thuế")];

    public static bool IsTaxRate(string? code) => TaxRates.Any(t => t.Code == code);

    public static OrderLine Line(int number, string item, string? unit, decimal quantity, decimal unitPrice, string taxRate)
    {
        var before = Math.Round(quantity * unitPrice, 0, MidpointRounding.AwayFromZero);
        var percent = int.TryParse(taxRate, out var p) && p > 0 ? p : 0;
        return new(number, item.Trim(), string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), quantity, unitPrice, taxRate, before, Math.Round(before * percent / 100m, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Open orders past their QR expiry with money still due are shown (and stored on the next change) as expired.</summary>
    public static string EffectiveStatus(Order o, DateTime utcNow) =>
        OrderStatus.IsOpen(o.Status) && o.Status != OrderStatus.Draft && o.ExpiresAt is { } expires && expires <= utcNow && o.PaidAmount < o.Amount ? OrderStatus.Expired : o.Status;

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
                    QrCodeType = TingeeQrTypes.Recurring, ExpireInMinute = order.QrExpireMinutes, Purpose = order.OrderNo, ExtraInfo = order.OrderNo
                });
                updated = order with { QrType = TingeeQrTypes.Recurring, QrPayload = qr.QrCode, QrAccount = qr.QrAccount, BillId = qr.BillId };
            }
            else
            {
                var qr = await tingee.GenerateStaticQrAsync(new TingeeStaticQrRequest(bank.BankBin, account, due, order.OrderNo));
                updated = order with { QrType = TingeeQrTypes.Static, QrPayload = qr.QrCode, QrAccount = account, BillId = null };
            }
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "QR not issued for order {OrderNo}.", order.OrderNo);
            var error = ex is TingeeApiException t ? $"Tingee từ chối tạo QR (mã {t.Code}): {t.TingeeMessage}"
                : ex is TingeeNotConfiguredException ? "Kết nối Tingee chưa được cấu hình." : "Không kết nối được Tingee.";
            var failed = order with { LastError = error };
            await store.UpdateOrderAsync(customerId, failed, actor);
            return new(failed, error);
        }
        if (string.IsNullOrEmpty(updated.QrPayload)) return new(order, "Tingee không trả về mã QR.");
        updated = updated with { Status = OrderMath.StatusFor(order.Amount, order.PaidAmount), ExpiresAt = now.AddMinutes(order.QrExpireMinutes), LastError = null };
        await store.UpdateOrderAsync(customerId, updated, actor);
        return new(updated, null);
    }

    /// <summary>Reads the dynamic QR status from Tingee and applies the amount paid. Static QR orders are settled by webhook/reconciliation.</summary>
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
            var paid = Math.Max(order.PaidAmount, paidBefore + (bill?.TotalAmountPaid ?? 0));
            var next = OrderMath.StatusFor(order.Amount, paid);
            if (next != OrderStatus.Paid && bill?.Status is "expired" or "cancelled") next = OrderStatus.Expired;
            var updated = order with { PaidAmount = paid, Status = next, LastError = null };
            if (next == OrderStatus.Paid) await CloseQrAsync(merchantId, updated);
            await store.UpdateOrderAsync(customerId, updated, actor);
            return new(updated, null);
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "QR status unavailable for order {OrderNo}.", order.OrderNo);
            return new(order, ex is TingeeApiException t ? $"Tingee (mã {t.Code}): {t.TingeeMessage}" : "Không kiểm tra được trạng thái QR lúc này.");
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
