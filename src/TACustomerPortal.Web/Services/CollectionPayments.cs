using System.Collections.Concurrent;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Services;

/// <summary>
/// QR of a payment collection, as for an order (OrderPayments): a dynamic recurring QR (several payments until the amount is
/// reached) where the bank supports it, otherwise a static VietQR whose content is the collection number. The QR asks for
/// what is still due; the customer may pay less, the processor then splits what arrives over the orders oldest first.
/// Partner failures never lose the collection: it stays open with the error shown and the QR can be created again.
/// </summary>
public sealed class CollectionPayments(IPaymentCollectionStore store, IPaymentStore payments, ITingeeApi tingee, TimeProvider clock, ILogger<CollectionPayments> logger)
{
    private static readonly ConcurrentDictionary<Guid, DateTime> LastLiveCheck = new();

    /// <summary>
    /// A new QR for what is still due, valid <paramref name="minutes"/> from now on <paramref name="bank"/>. The previous
    /// dynamic QR is closed once the new one exists (best effort); money sent to it is still matched by account and content.
    /// </summary>
    public async Task<(Collection Collection, string? Error)> IssueQrAsync(Guid customerId, long merchantId, Collection c, BankAccount bank, int minutes, Guid actor)
    {
        if (!PaymentCollectionStatus.IsOpen(c.Status)) return (c, "Phiếu thu đã thu đủ hoặc đã hủy.");
        if (c.Due <= 0) return (c, "Các đơn trong phiếu thu đã được thanh toán đủ, không cần tạo QR.");
        if (bank.ReceiveAccount is not string account || !bank.IsActive) return (c, "Tài khoản nhận tiền chưa hoạt động.");
        var content = CollectionMath.TransferContent(c.CollectionNo);
        var now = clock.GetUtcNow().UtcDateTime;
        Collection updated;
        try
        {
            if (TingeeBankCatalog.SupportsDynamicQr(bank.BankBin, bank.AccountType) && bank.VaAccountNumber is not null)
            {
                var qr = await tingee.GenerateDynamicQrAsync(new TingeeDynamicQrRequest
                {
                    MerchantId = merchantId, VaAccountNumber = bank.VaAccountNumber, BankBin = bank.BankBin, Amount = c.Due,
                    QrCodeType = TingeeQrTypes.Recurring, ExpireInMinute = minutes, Purpose = content, ExtraInfo = c.CollectionNo
                });
                updated = c with { QrType = TingeeQrTypes.Recurring, QrPayload = qr.QrCode, QrAccount = qr.QrAccount, BillId = qr.BillId };
            }
            else
            {
                var qr = await tingee.GenerateStaticQrAsync(new TingeeStaticQrRequest(bank.BankBin, account, c.Due, content));
                updated = c with { QrType = TingeeQrTypes.Static, QrPayload = qr.QrCode, QrAccount = account, BillId = null };
            }
        }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogWarning(ex, "QR not issued for collection {No}.", c.CollectionNo);
            var error = ex is TingeeApiException t ? PartnerText.Scrub($"Không tạo được QR (mã {t.Code}): {t.TingeeMessage}")
                : ex is TingeeNotConfiguredException ? "Dịch vụ thanh toán chưa sẵn sàng. Vui lòng liên hệ bộ phận hỗ trợ Hóa đơn tự động." : "Không kết nối được dịch vụ thanh toán.";
            await store.UpdateQrAsync(customerId, c with { LastError = error }, actor);
            return (c with { LastError = error }, error);
        }
        if (string.IsNullOrEmpty(updated.QrPayload)) return (c, "Không nhận được mã QR, vui lòng thử lại.");
        updated = updated with { BankAccountId = bank.Id, QrExpireMinutes = minutes, ExpiresAt = now.AddMinutes(minutes), LastError = null };
        await store.UpdateQrAsync(customerId, updated, actor);
        LastLiveCheck[c.Id] = now;
        if (c.BillId is not null && c.BillId != updated.BillId) await CloseQrAsync(merchantId, c);
        return (updated, null);
    }

    /// <summary>
    /// Before a dynamic QR is shown, checks at most every <see cref="QrLiveness.CheckInterval"/> that it still exists at the
    /// bank (operator 2026-11-13: some banks drop QRs early); a QR that is gone is replaced for what is still due, keeping
    /// the collection's expiry. Failures keep the current QR.
    /// </summary>
    public async Task<Collection> EnsureLiveQrAsync(Guid customerId, Collection c, Guid actor)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (c.QrType != TingeeQrTypes.Recurring || c.QrAccount is null || c.BillId is null || c.Due <= 0 || c.ExpiresAt is not DateTime expires
            || CollectionMath.EffectiveStatus(c, now) is not (PaymentCollectionStatus.Awaiting or PaymentCollectionStatus.Partial))
            return c;
        if (LastLiveCheck.TryGetValue(c.Id, out var last) && now - last < QrLiveness.CheckInterval) return c;
        LastLiveCheck[c.Id] = now;
        if (await payments.MerchantIdAsync(customerId) is not long merchantId) return c;
        string? code = null, billStatus = null;
        try { billStatus = (await tingee.GetDynamicQrStatusAsync(merchantId, c.QrAccount, c.BillId)).BillInfo?.Status; }
        catch (TingeeApiException ex) { code = ex.Code; }
        catch (Exception ex) when (ex is HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogInformation(ex, "QR liveness of collection {No} not checked.", c.CollectionNo);
            return c;
        }
        if (!QrLiveness.IsDead(code, billStatus)) return c;
        if (c.BankAccountId is not Guid b || await payments.BankAccountAsync(customerId, b) is not { } bank) return c;
        logger.LogWarning("QR_DEAD_REISSUE collection={No} code={Code} status={Status}", c.CollectionNo, code, billStatus);
        var (updated, error) = await IssueQrAsync(customerId, merchantId, c, bank, QrLiveness.MinutesLeft(expires, now), actor);
        return error is null ? updated : c;
    }

    /// <summary>Cancels an open collection and closes its dynamic QR (best effort). Money already received stays on its orders.</summary>
    public async Task<string?> CancelAsync(Guid customerId, Collection c, Guid actor)
    {
        if (!await store.CancelAsync(customerId, c.Id, actor)) return "Phiếu thu đã thu đủ hoặc đã hủy.";
        if (await payments.MerchantIdAsync(customerId) is long merchantId) await CloseQrAsync(merchantId, c);
        return null;
    }

    private async Task CloseQrAsync(long merchantId, Collection c)
    {
        if (c.QrType != TingeeQrTypes.Recurring || c.QrAccount is null || c.BillId is null) return;
        try { await tingee.DeleteDynamicQrAsync(merchantId, c.QrAccount, c.BillId); }
        catch (Exception ex) when (ex is TingeeApiException or HttpRequestException or TingeeNotConfiguredException)
        {
            logger.LogInformation(ex, "Dynamic QR of collection {No} not closed.", c.CollectionNo);
        }
    }
}
