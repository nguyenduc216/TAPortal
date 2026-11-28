using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Branding;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Models;

/// <summary>Done (green), Warn (yellow: needs a look), Todo (not reached yet), Off (does not apply to this order).</summary>
public enum TimelineState { Done, Warn, Todo, Off }

/// <summary>One step of the order timeline. <paramref name="Dialog"/>: id of a dialog on the page with the details.</summary>
public sealed record TimelineStep(string Key, string Icon, string Title, TimelineState State, string Summary, IReadOnlyList<string> Lines, string? Dialog = null, string? DialogLabel = null);

/// <summary>
/// The order's progress (operator 2026-11-28): created → paid (how many payments) → amount matched → buyer told about the
/// money → e-invoice issued (when automatic, or issued by hand) → invoice sent to the buyer.
/// </summary>
public static class OrderTimeline
{
    public static IReadOnlyList<TimelineStep> Build(Order o, string status, OrderActivity activity, string? invoiceProvider, bool invoiceSendsEmail, bool buyerHasEmail)
    {
        var steps = new List<TimelineStep>();
        var payments = activity.Payments;
        var count = payments.Select(p => p.TransactionId).Distinct().Count();
        var paidFull = o.Amount > 0 && o.PaidAmount >= o.Amount;
        var cancelled = status is OrderStatus.Cancelled;

        steps.Add(new("created", "file-invoice", "Tạo đơn hàng", TimelineState.Done, PaymentText.VietnamTime(o.CreatedAt), [$"Số tiền {PaymentText.Money(o.Amount)}"]));

        // Payment: how many transfers, and how much of the order they cover.
        var payLines = payments.Select(p => $"{PaymentText.VietnamTime(p.At)} · {PaymentText.Money(p.Amount)}"
            + (p.CollectionNo is string no ? $" (qua phiếu thu {no})" : "") + (p.Status == "REVIEW" ? " · cần đối soát" : "")).ToList();
        var times = count == 0 ? "" : $" · {count} lần thanh toán";
        steps.Add(paidFull
            ? new("paid", "circle-check", "Thanh toán", TimelineState.Done, $"Đã thu đủ {PaymentText.Money(o.PaidAmount)}{times}", payLines)
            : o.PaidAmount > 0
                ? new("paid", "clock", "Thanh toán", TimelineState.Warn, $"Đã thu {PaymentText.Money(o.PaidAmount)}/{PaymentText.Money(o.Amount)}, còn {PaymentText.Money(o.Remaining)}{times}", payLines)
                : new("paid", "clock", "Thanh toán", cancelled ? TimelineState.Off : TimelineState.Todo, cancelled ? "Đơn đã hủy" : "Chờ khách chuyển khoản", payLines));

        // Amount matched: the system's count against what the payment service reports, and no transfer left for review.
        var review = payments.Any(p => p.Status == "REVIEW");
        steps.Add(o.PaidAmount == 0 && count == 0
            ? new("matched", "refresh", "Khớp số tiền", cancelled ? TimelineState.Off : TimelineState.Todo, "Chưa có giao dịch", [])
            : o.PartnerDiffers || o.Overpaid > 0 || review
                ? new("matched", "alert-triangle", "Khớp số tiền", TimelineState.Warn,
                    o.Overpaid > 0 ? $"Khách chuyển dư {PaymentText.Money(o.Overpaid)}" : o.PartnerDiffers ? "Lệch với số dịch vụ thanh toán báo về" : "Có giao dịch cần đối soát",
                    o.PartnerPaidAmount is decimal pa ? [$"Dịch vụ thanh toán báo {PaymentText.Money(pa)} · hệ thống ghi {PaymentText.Money(o.PaidAmount)}"] : [])
                : o.PartnerPaidAmount is decimal partner
                    ? new("matched", "circle-check", "Khớp số tiền", TimelineState.Done, $"Khớp {PaymentText.Money(partner)}", [$"Đối chiếu lúc {PaymentText.VietnamTime(o.PartnerCheckedAt)}"])
                    : new("matched", "refresh", "Khớp số tiền", TimelineState.Todo, "Chưa đối chiếu với dịch vụ thanh toán", []));

        // The buyer told about the money (email / Zalo from the company's notification settings).
        var notices = activity.BuyerNotices;
        var noticeLines = notices.Select(n => $"{Channel(n.Channel)} → {n.Recipient}: {NoticeStatus(n)}").ToList();
        steps.Add(count == 0
            ? new("notified", "bell", "Báo khách đã nhận tiền", cancelled ? TimelineState.Off : TimelineState.Todo, "Gửi khi có tiền về", [])
            : notices.Count == 0
                ? new("notified", "bell", "Báo khách đã nhận tiền", TimelineState.Off, "Không gửi", ["Chưa bật kênh báo khách (Cài đặt thông báo) hoặc khách không có email / số Zalo."])
                : notices.Any(n => n.Status == "FAILED")
                    ? new("notified", "alert-triangle", "Báo khách đã nhận tiền", TimelineState.Warn, "Có tin gửi lỗi", noticeLines, "noticeDialog", "Xem nội dung")
                    : notices.All(n => n.Status == "SENT")
                        ? new("notified", "circle-check", "Báo khách đã nhận tiền", TimelineState.Done, $"Đã gửi {notices.Count} tin", noticeLines, "noticeDialog", "Xem nội dung")
                        : new("notified", "clock", "Báo khách đã nhận tiền", TimelineState.Todo, "Đang chờ gửi", noticeLines, "noticeDialog", "Xem nội dung"));

        // E-invoice, then the invoice sent to the buyer.
        var issued = o.InvoiceStatus == OrderInvoiceStatus.Issued;
        if (!o.AutoIssueInvoice && !issued && o.InvoiceStatus != OrderInvoiceStatus.Draft)
        {
            steps.Add(new("invoice", "file-text", "Xuất hóa đơn điện tử", TimelineState.Off, "Không tự động xuất", ["Có thể xuất tay ở trang này khi đơn đã thanh toán đủ."]));
            return steps;
        }
        var provider = invoiceProvider ?? "phần mềm hóa đơn";
        steps.Add(o.InvoiceStatus switch
        {
            OrderInvoiceStatus.Issued => new("invoice", "circle-check", "Xuất hóa đơn điện tử", TimelineState.Done,
                o.InvoiceNumber is string n ? $"Hóa đơn số {n}" : "Đã xuất hóa đơn", [$"{PaymentText.VietnamTime(o.InvoiceIssuedAt)} · {provider}"]),
            OrderInvoiceStatus.Draft => new("invoice", "alert-triangle", "Xuất hóa đơn điện tử", TimelineState.Warn, "Đã tạo bản nháp, chờ ký số", [$"Ký và phát hành trên {provider}."]),
            OrderInvoiceStatus.Failed => new("invoice", "alert-triangle", "Xuất hóa đơn điện tử", TimelineState.Warn, "Chưa xuất được", o.InvoiceError is string e ? [PartnerText.Scrub(e)] : []),
            _ when paidFull => new("invoice", "clock", "Xuất hóa đơn điện tử", TimelineState.Todo, "Đang xuất tự động", []),
            _ => new("invoice", "file-text", "Xuất hóa đơn điện tử", cancelled ? TimelineState.Off : TimelineState.Todo, "Tự động xuất khi thanh toán đủ", []),
        });
        steps.Add(o.InvoiceEmailedAt is not null
            ? new("invoice-email", "mail", "Gửi hóa đơn cho khách", TimelineState.Done, $"Đã yêu cầu {provider} gửi email", [PaymentText.VietnamTime(o.InvoiceEmailedAt)], "invoiceEmailDialog", "Xem email")
            : !issued
                ? new("invoice-email", "mail", "Gửi hóa đơn cho khách", cancelled ? TimelineState.Off : TimelineState.Todo, "Gửi email sau khi xuất hóa đơn", [])
                : !invoiceSendsEmail
                    ? new("invoice-email", "mail", "Gửi hóa đơn cho khách", TimelineState.Off, "Không gửi email", ["Tắt \"Gửi hóa đơn qua email cho người mua\" trong Hóa đơn điện tử."])
                    : !buyerHasEmail
                        ? new("invoice-email", "mail", "Gửi hóa đơn cho khách", TimelineState.Off, "Khách không có email", ["Thêm email cho khách để lần sau gửi được."])
                        : new("invoice-email", "alert-triangle", "Gửi hóa đơn cho khách", TimelineState.Warn, "Chưa gửi email", ["Xem ghi chú hóa đơn ở trên."]));
        return steps;
    }

    public static string Channel(string channel) => channel switch
    {
        "SMTP" => "Email",
        "ZALO_OA" => "Zalo",
        _ => channel,
    };

    public static string NoticeStatus(OrderNotice n) => n.Status switch
    {
        "SENT" => "đã gửi " + PaymentText.VietnamTime(n.SentAt),
        "FAILED" => "gửi lỗi",
        _ => "đang chờ gửi",
    };
}
