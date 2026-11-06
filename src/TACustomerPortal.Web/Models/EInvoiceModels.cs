using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Models;

/// <summary>The company's MISA meInvoice login. The password is sent to the e-invoice service once and never stored by T.A.</summary>
public sealed class EInvoiceAccountInput
{
    [Required(ErrorMessage = "Nhập mã số thuế của doanh nghiệp trên MISA.")]
    [RegularExpression(@"^\d{10}(-\d{3})?$", ErrorMessage = "Mã số thuế gồm 10 số, hoặc 10 số và 3 số chi nhánh (0101234567-001).")]
    public string? TaxCode { get; set; }

    [Required(ErrorMessage = "Nhập tên đăng nhập MISA meInvoice.")]
    [StringLength(150, ErrorMessage = "Tên đăng nhập tối đa 150 ký tự.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Nhập mật khẩu MISA meInvoice.")]
    [StringLength(200, ErrorMessage = "Mật khẩu tối đa 200 ký tự.")]
    public string? Password { get; set; }
}

public sealed class EInvoiceTemplateInput
{
    // MISA identifies a template by a GUID (36 characters), so mẫu số takes up to 50.
    [RegularExpression(@"^[A-Za-z0-9/._-]{1,50}$", ErrorMessage = "Mẫu số chỉ gồm chữ, số và / . _ - (tối đa 50 ký tự). Bấm \"Lấy mẫu hóa đơn từ MISA\" để chọn.")]
    public string? Pattern { get; set; }

    [RegularExpression(@"^[A-Za-z0-9/._-]{1,20}$", ErrorMessage = "Ký hiệu chỉ gồm chữ, số và / . _ - (tối đa 20 ký tự), VD: C26TAA.")]
    public string? Series { get; set; }

    /// <summary>A template picked from the MISA list as "mẫu số|ký hiệu"; replaces the two fields above.</summary>
    public string? Choice { get; set; }

    public bool AutoIssueEnabled { get; set; }
    public bool SendEmail { get; set; } = true;
}

public sealed record EInvoiceVm(CustomerMembership Customer, bool CanManage, bool Linked, bool ServiceReady, CustomerInvoiceSettings Settings,
    EInvoiceAccountInput Account, EInvoiceTemplateInput Template, IReadOnlyList<TingeeInvoiceTemplate>? Templates = null, bool ShowConnect = false);

public static class InvoiceText
{
    /// <summary>Badge for an order's invoice state.</summary>
    public static (string Label, string Css) Status(string status) => status switch
    {
        OrderInvoiceStatus.Pending => ("Đang xuất hóa đơn", "cp-badge--gold"),
        OrderInvoiceStatus.Issued => ("Đã xuất hóa đơn", "cp-badge--green"),
        OrderInvoiceStatus.Failed => ("Xuất hóa đơn lỗi", "cp-badge--red"),
        _ => ("Chưa xuất hóa đơn", "cp-badge--soft")
    };
}
