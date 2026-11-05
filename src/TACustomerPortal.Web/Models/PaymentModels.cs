using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Models;

public sealed record BanksVm(CustomerMembership Customer, bool Linked, bool ApiConfigured, bool CanManage, IReadOnlyList<BankAccount> Accounts);

public sealed record BankAddVm(CustomerMembership Customer, bool Linked, bool ApiConfigured, TingeeBank? Bank, string? AccountType, BankLinkInput Input)
{
    public TingeeBankRule? Rule => Bank is not null && AccountType is not null && Bank.AccountTypes.Contains(AccountType) ? Bank.Rule(AccountType) : null;
}

public sealed class BankLinkInput
{
    public string? Bin { get; set; }
    public string? AccountType { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountName { get; set; }
    public string? Identity { get; set; }
    public string? Mobile { get; set; }
    public string? VaSuffix { get; set; }
    public string? MerchantName { get; set; }
    public string? VaAccountNumber { get; set; }
    public string? AcbUserId { get; set; }
    public bool NotifyRootAccount { get; set; }
}

public sealed record BankConfirmVm(BankAccount Account, bool Deleting);

public sealed record ContactsVm(CustomerMembership Customer, bool CanManage, IReadOnlyList<Contact> Contacts);

public sealed class ContactInput
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã khách hàng."), StringLength(50, ErrorMessage = "Mã tối đa 50 ký tự."), RegularExpression("^[A-Za-z0-9_.-]+$", ErrorMessage = "Mã chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang, gạch dưới.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng."), StringLength(250, ErrorMessage = "Tên tối đa 250 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Tên đơn vị tối đa 250 ký tự.")]
    public string? LegalName { get; set; }

    [RegularExpression(@"^\d{10}(-\d{3})?$|^\d{12}$", ErrorMessage = "Mã số thuế gồm 10 chữ số (hoặc 10 chữ số-3 chữ số), hoặc 12 chữ số với cá nhân.")]
    public string? TaxCode { get; set; }

    [RegularExpression("^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)]
    public string? Email { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ tối đa 500 ký tự.")]
    public string? Address { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    public string? Note { get; set; }

    /// <summary>INDIVIDUAL / HOUSEHOLD / BUSINESS / ORGANIZATION (<see cref="BuyerTypes"/>); only a business must give a tax code.</summary>
    public string BuyerType { get; set; } = BuyerTypes.Individual;

    [StringLength(100, ErrorMessage = "Nhóm khách tối đa 100 ký tự.")]
    public string? BuyerGroup { get; set; }

    [RegularExpression("^[0-9]{9}$|^[0-9]{12}$|^[A-Za-z][0-9]{7,8}$", ErrorMessage = "Số CCCD gồm 12 chữ số (CMND 9 chữ số; hộ chiếu: 1 chữ cái + 7–8 chữ số).")]
    public string? IdentityNumber { get; set; }

    [StringLength(100, ErrorMessage = "Zalo tối đa 100 ký tự.")]
    public string? Zalo { get; set; }

    [StringLength(100, ErrorMessage = "Tên ngân hàng tối đa 100 ký tự.")]
    public string? BankName { get; set; }

    [RegularExpression("^[0-9A-Za-z]{4,30}$", ErrorMessage = "Số tài khoản gồm 4–30 chữ số/chữ cái, không dấu cách.")]
    public string? BankAccountNumber { get; set; }

    [StringLength(150, ErrorMessage = "Tên chủ tài khoản tối đa 150 ký tự.")]
    public string? BankAccountName { get; set; }
}

public sealed record OrdersVm(CustomerMembership Customer, bool CanManage, bool Linked, string? Filter, IReadOnlyList<Order> Orders, DateTime UtcNow,
    IReadOnlyList<OrderSummaryCard> Cards);

/// <summary>A summary card on the order list: orders in one state (or with one problem), their total and amount collected.</summary>
public sealed record OrderSummaryCard(string? Code, string Label, string Icon, string Tone, int Count, decimal Amount, decimal Paid);

public static class OrderSummary
{
    /// <summary>Filters that are not a status: overpaid orders and orders whose partner amount differs.</summary>
    public const string Overpaid = "OVERPAID", Mismatch = "MISMATCH";

    private static readonly (string? Code, string Label, string Icon, string Tone)[] Definitions =
    [
        (null, "Tất cả đơn", "file-invoice", ""),
        (Services.OrderStatus.Awaiting, "Chờ thanh toán", "clock", "cp-blob--blue"),
        (Services.OrderStatus.Partial, "Thanh toán một phần", "report-money", "cp-blob--gold"),
        (Services.OrderStatus.Paid, "Đã thanh toán", "circle-check", "cp-blob--green"),
        (Services.OrderStatus.Expired, "Hết hạn", "clock", "cp-blob--red"),
        (Services.OrderStatus.ExpiredPaid, "QR hết hạn có giao dịch", "alert-triangle", "cp-blob--red"),
        (Services.OrderStatus.Cancelled, "Đã hủy", "ban", ""),
        (Services.OrderStatus.CancelledPaid, "Đơn hủy có giao dịch", "alert-triangle", "cp-blob--red"),
        (Services.OrderStatus.Draft, "Chưa có QR", "qrcode", ""),
        (Overpaid, "Trả dư", "alert-triangle", "cp-blob--gold"),
        (Mismatch, "Lệch đối soát", "refresh", "cp-blob--gold"),
    ];

    public static bool Matches(Order o, string code) => code switch
    {
        Overpaid => o.Overpaid > 0,
        Mismatch => o.PartnerDiffers,
        _ => o.Status == code
    };

    /// <summary>Cards over every order (statuses already effective); "Tất cả" first.</summary>
    public static IReadOnlyList<OrderSummaryCard> Cards(IReadOnlyList<Order> orders) =>
        Definitions.Select(d =>
        {
            var set = d.Code is null ? orders : orders.Where(o => Matches(o, d.Code)).ToList();
            return new OrderSummaryCard(d.Code, d.Label, d.Icon, d.Tone, set.Count, set.Sum(o => o.Amount), set.Sum(o => o.PaidAmount));
        }).ToList();
}

public sealed record OrderCreateVm(CustomerMembership Customer, bool Linked, bool ApiConfigured, IReadOnlyList<Contact> Contacts, IReadOnlyList<BankAccount> Banks, OrderInput Input);

public sealed class OrderInput
{
    public Guid? ContactId { get; set; }
    public Guid? BankAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }

    [Range(1, 43200, ErrorMessage = QrLifetime.RangeMessage)]
    public int QrExpireMinutes { get; set; } = 1440;

    // The form asks days/hours/minutes; when any is posted they replace QrExpireMinutes (QrLifetime.Apply).
    [Range(0, 30, ErrorMessage = "Số ngày từ 0 đến 30.")] public int? QrExpireDays { get; set; }
    [Range(0, 23, ErrorMessage = "Số giờ từ 0 đến 23.")] public int? QrExpireHours { get; set; }
    [Range(0, 59, ErrorMessage = "Số phút từ 0 đến 59.")] public int? QrExpireMins { get; set; }

    public bool AutoIssueInvoice { get; set; } = true;

    public List<OrderLineInput> Lines { get; set; } = [new()];
}

public sealed class OrderLineInput
{
    public string? ItemName { get; set; }
    public string? UnitName { get; set; }
    public decimal? Quantity { get; set; } = 1;
    public decimal? UnitPrice { get; set; }
    public string TaxRate { get; set; } = "0";
}

/// <summary>Banks: the active receiving accounts offered when (re)issuing the QR of a draft or expired order.</summary>
public sealed record OrderDetailsVm(CustomerMembership Customer, bool CanManage, Order Order, string Status, BankAccount? Bank, Contact? Contact, string? QrSvg, DateTime UtcNow,
    IReadOnlyList<BankAccount> Banks);

public sealed class PaymentSettingsInput
{
    public bool AutoIssueInvoiceDefault { get; set; } = true;

    [Range(1, 43200, ErrorMessage = QrLifetime.RangeMessage)]
    public int DefaultQrExpireMinutes { get; set; } = 1440;

    [Range(0, 30, ErrorMessage = "Số ngày từ 0 đến 30.")] public int? DefaultQrExpireDays { get; set; }
    [Range(0, 23, ErrorMessage = "Số giờ từ 0 đến 23.")] public int? DefaultQrExpireHours { get; set; }
    [Range(0, 59, ErrorMessage = "Số phút từ 0 đến 59.")] public int? DefaultQrExpireMins { get; set; }
}

/// <summary>Model of the shared _QrLifetime partial: fields Prefix+"Days"/"Hours"/"Mins", errors under Prefix+"Minutes".</summary>
public sealed record QrLifetimeField(string Prefix, string Label, int Minutes, bool Disabled = false, bool Last = false);

/// <summary>QR lifetime entered as days + hours + minutes and stored as minutes (1 minute to 30 days).</summary>
public static class QrLifetime
{
    public const int MaxMinutes = 43200;
    public const string RangeMessage = "Thời hạn QR từ 1 phút đến 30 ngày.";

    public static (int Days, int Hours, int Minutes) Split(int minutes) => (minutes / 1440, minutes % 1440 / 60, minutes % 60);

    /// <summary>"1 ngày 2 giờ 30 phút"; zero parts are left out.</summary>
    public static string Text(int minutes)
    {
        var (d, h, m) = Split(minutes);
        var parts = new List<string>();
        if (d > 0) parts.Add($"{d} ngày");
        if (h > 0) parts.Add($"{h} giờ");
        if (m > 0 || parts.Count == 0) parts.Add($"{m} phút");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// When any of the day/hour/minute fields was posted, their total replaces the minutes field (<paramref name="key"/>)
    /// and is range-checked under that key. Forms that post only the minutes are left as they are.
    /// </summary>
    public static int Apply(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary state, string key, int minutes, int? days, int? hours, int? mins)
    {
        if (days is null && hours is null && mins is null) return minutes;
        var total = (days ?? 0) * 1440 + (hours ?? 0) * 60 + (mins ?? 0);
        state.Remove(key);
        if (total is < 1 or > MaxMinutes) state.AddModelError(key, RangeMessage);
        return total;
    }
}

public sealed record PaymentSettingsVm(CustomerMembership Customer, bool CanManage, PaymentSettingsInput Input);

public static class PaymentText
{
    /// <summary>Receiving account in a select: "VietinBank — 104005395693 (QR động) · mặc định".</summary>
    public static string BankOption(BankAccount b) =>
        $"{b.BankName} — {b.ReceiveAccount} ({(TingeeBankCatalog.SupportsDynamicQr(b.BankBin, b.AccountType) ? "QR động" : "QR tĩnh")}){(b.IsDefault ? " · mặc định" : "")}";

    public static (string Label, string Css) BankStatus(string status) => status switch
    {
        Services.BankStatus.Active => ("Đang hoạt động", "cp-badge--green"),
        Services.BankStatus.PendingOtp => ("Chờ nhập OTP", "cp-badge--gold"),
        Services.BankStatus.PendingBank => ("Chờ ngân hàng xác nhận", "cp-badge--gold"),
        Services.BankStatus.PendingDelete => ("Chờ xác nhận hủy", "cp-badge--gold"),
        Services.BankStatus.Failed => ("Liên kết thất bại", "cp-badge--red"),
        _ => (status, "cp-badge--soft")
    };

    public static (string Label, string Css) OrderStatus(string status) => status switch
    {
        Services.OrderStatus.Draft => ("Chưa có QR", "cp-badge--soft"),
        Services.OrderStatus.Awaiting => ("Chờ thanh toán", "cp-badge--blue"),
        Services.OrderStatus.Partial => ("Thanh toán một phần", "cp-badge--gold"),
        Services.OrderStatus.Paid => ("Đã thanh toán", "cp-badge--green"),
        Services.OrderStatus.Expired => ("Hết hạn", "cp-badge--red"),
        Services.OrderStatus.Cancelled => ("Đã hủy", "cp-badge--soft"),
        Services.OrderStatus.ExpiredPaid => ("QR hết hạn có giao dịch", "cp-badge--red"),
        Services.OrderStatus.CancelledPaid => ("Đơn hủy có giao dịch", "cp-badge--red"),
        _ => (status, "cp-badge--soft")
    };

    public static string Money(decimal value) => value.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

    public static string VietnamTime(DateTime? utc) => utc is { } v ? v.AddHours(7).ToString("dd/MM/yyyy HH:mm") : "—";
}
