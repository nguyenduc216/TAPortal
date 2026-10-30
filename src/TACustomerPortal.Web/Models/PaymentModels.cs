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
}

public sealed record OrdersVm(CustomerMembership Customer, bool CanManage, bool Linked, string? Filter, IReadOnlyList<Order> Orders, DateTime UtcNow);

public sealed record OrderCreateVm(CustomerMembership Customer, bool Linked, bool ApiConfigured, IReadOnlyList<Contact> Contacts, IReadOnlyList<BankAccount> Banks, OrderInput Input);

public sealed class OrderInput
{
    public Guid? ContactId { get; set; }
    public Guid? BankAccountId { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự.")]
    public string? Description { get; set; }

    [Range(1, 43200, ErrorMessage = "Thời hạn QR từ 1 đến 43.200 phút (30 ngày).")]
    public int QrExpireMinutes { get; set; } = 1440;

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

public sealed record OrderDetailsVm(CustomerMembership Customer, bool CanManage, Order Order, string Status, BankAccount? Bank, Contact? Contact, string? QrSvg, DateTime UtcNow);

public sealed class PaymentSettingsInput
{
    public bool AutoIssueInvoiceDefault { get; set; } = true;

    [Range(1, 43200, ErrorMessage = "Thời hạn QR từ 1 đến 43.200 phút (30 ngày).")]
    public int DefaultQrExpireMinutes { get; set; } = 1440;
}

public sealed record PaymentSettingsVm(CustomerMembership Customer, bool CanManage, PaymentSettingsInput Input);

public static class PaymentText
{
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
        _ => (status, "cp-badge--soft")
    };

    public static string Money(decimal value) => value.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

    public static string VietnamTime(DateTime? utc) => utc is { } v ? v.AddHours(7).ToString("dd/MM/yyyy HH:mm") : "—";
}
