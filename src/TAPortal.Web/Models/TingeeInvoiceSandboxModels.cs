using System.ComponentModel.DataAnnotations;
using TAPortal.Integrations.Tingee;

namespace TAPortal.Web.Models;

// Hóa đơn Sandbox (Admin → Tingee): T.A tests Tingee's e-invoice API end to end — the provider account (XCyber, HiLo,
// SInvoice, MISA, BKAV), its templates, a draft PDF, a real issue, download and email — before customers configure their own
// invoice settings and orders issue invoices automatically. Nothing is stored here; Tingee keeps the provider account.

public sealed record TingeeInvoiceSandboxVm(
    TingeeSandboxConfig Config,
    IReadOnlyList<TingeeSandboxMerchant> Merchants,
    long? SelectedMerchant,
    string? ScopeLabel,
    string? ScopeError,
    IReadOnlyList<TingeeInvoiceProvider> Providers,
    IReadOnlyList<TingeeInvoiceAccount>? Accounts,
    string? AccountsError,
    TingeeInvoiceAccountInput AccountInput,
    long? TemplatesAccount,
    IReadOnlyList<TingeeInvoiceTemplate>? Templates,
    TingeeInvoiceSandboxInput Invoice,
    TingeeInvoiceSandboxDraft? Draft,
    TingeeIssuedInvoice? Issued,
    string? Dialog = null);

public sealed record TingeeInvoiceSandboxDraft(string InvoiceCode, string PdfBase64);

public static class TingeeInvoiceSandboxDialogs
{
    public const string Account = "account";
}

public sealed class TingeeInvoiceAccountInput
{
    /// <summary>Set to update an existing provider account; empty creates one.</summary>
    public long? Id { get; set; }
    [Required(ErrorMessage = "Chọn nhà cung cấp hóa đơn.")] public string? Provider { get; set; }
    [Required(ErrorMessage = "Nhập mã số thuế của đơn vị bán.")]
    [RegularExpression("^[0-9]{10}(-[0-9]{3})?$", ErrorMessage = "Mã số thuế gồm 10 chữ số (chi nhánh: 10 số-3 số).")]
    public string? TaxCode { get; set; }
    [Required(ErrorMessage = "Nhập tên đăng nhập tại nhà cung cấp."), StringLength(150)] public string? Username { get; set; }
    /// <summary>Sent to Tingee only; never stored, logged or echoed back into the page.</summary>
    [Required(ErrorMessage = "Nhập mật khẩu tại nhà cung cấp."), StringLength(200)] public string? Password { get; set; }
    public bool IsDefault { get; set; } = true;
}

public sealed class TingeeInvoiceSandboxLine
{
    [StringLength(300)] public string? Name { get; set; }
    [StringLength(50)] public string? Unit { get; set; }
    [Range(0.0001, 1_000_000, ErrorMessage = "Số lượng phải lớn hơn 0.")] public decimal Quantity { get; set; } = 1;
    [Range(0, 10_000_000_000, ErrorMessage = "Đơn giá không hợp lệ.")] public decimal UnitPrice { get; set; }
    public string TaxRate { get; set; } = "10";
}

public sealed class TingeeInvoiceSandboxInput
{
    public const int LineCount = 3;

    /// <summary>Provider account at Tingee; empty uses the merchant's default account.</summary>
    public long? AccountId { get; set; }
    [Required(ErrorMessage = "Nhập mẫu số hóa đơn (bấm \"Lấy mẫu\" để chọn)."), StringLength(50)] public string? Pattern { get; set; }
    [Required(ErrorMessage = "Nhập ký hiệu hóa đơn (bấm \"Lấy mẫu\" để chọn)."), StringLength(20)] public string? Series { get; set; }
    [Required(ErrorMessage = "Nhập mã hóa đơn."), RegularExpression("^[A-Za-z0-9_-]{4,40}$", ErrorMessage = "Mã hóa đơn 4–40 ký tự: chữ, số, - hoặc _.")]
    public string? Code { get; set; }
    [Required(ErrorMessage = "Chọn ngày lập hóa đơn.")] public DateOnly? Date { get; set; }
    [RegularExpression("^[0-9]{10}(-[0-9]{3})?$", ErrorMessage = "Mã số thuế người mua gồm 10 chữ số (chi nhánh: 10 số-3 số).")] public string? BuyerTaxCode { get; set; }
    [StringLength(150)] public string? BuyerName { get; set; }
    [StringLength(250)] public string? BuyerLegalName { get; set; }
    [StringLength(300)] public string? BuyerAddress { get; set; }
    [EmailAddress(ErrorMessage = "Email người mua không hợp lệ."), StringLength(150)] public string? BuyerEmail { get; set; }
    [Range(1, 3)] public int PaymentMethod { get; set; } = 2;
    [StringLength(250)] public string? Note { get; set; }
    public List<TingeeInvoiceSandboxLine> Lines { get; set; } = [];
    /// <summary>Issuing signs a legal invoice at the provider; the operator must tick this first.</summary>
    public bool ConfirmIssue { get; set; }
}
