using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Services;
using TAPortal.Integrations.Tingee;

namespace TACustomerPortal.Web.Models;

/// <summary>
/// The company's login at its e-invoice provider. MISA/Viettel: the password is sent to the e-invoice service once and never
/// stored. SoftDreams EasyInvoice needs it on every call: kept encrypted (operator 2026-11-26).
/// </summary>
public sealed class EInvoiceAccountInput
{
    /// <summary><see cref="CustomerInvoiceProviders"/> code; when not posted, the provider already saved.</summary>
    public string? Provider { get; set; }

    [Required(ErrorMessage = "Nhập mã số thuế của doanh nghiệp trên phần mềm hóa đơn.")]
    [RegularExpression(@"^\d{10}(-\d{3})?$", ErrorMessage = "Mã số thuế gồm 10 số, hoặc 10 số và 3 số chi nhánh (0101234567-001).")]
    public string? TaxCode { get; set; }

    [Required(ErrorMessage = "Nhập tên đăng nhập phần mềm hóa đơn.")]
    [StringLength(150, ErrorMessage = "Tên đăng nhập tối đa 150 ký tự.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Nhập mật khẩu phần mềm hóa đơn.")]
    [StringLength(200, ErrorMessage = "Mật khẩu tối đa 200 ký tự.")]
    public string? Password { get; set; }

    /// <summary>SoftDreams EasyInvoice only: connect to SoftDreams' demo system (testing) instead of production.</summary>
    public bool Demo { get; set; }
}

public sealed class EInvoiceTemplateInput
{
    // MISA identifies a template by a GUID (36 characters), so mẫu số takes up to 50.
    [RegularExpression(@"^[A-Za-z0-9/._-]{1,50}$", ErrorMessage = "Mẫu số chỉ gồm chữ, số và / . _ - (tối đa 50 ký tự). Bấm \"Lấy mẫu hóa đơn\" để chọn.")]
    public string? Pattern { get; set; }

    [RegularExpression(@"^[A-Za-z0-9/._-]{1,20}$", ErrorMessage = "Ký hiệu chỉ gồm chữ, số và / . _ - (tối đa 20 ký tự), VD: C26TAA.")]
    public string? Series { get; set; }

    /// <summary>A template picked from the provider's list as "mẫu số|ký hiệu"; replaces the two fields above.</summary>
    public string? Choice { get; set; }

    public bool AutoIssueEnabled { get; set; }
    public bool SendEmail { get; set; } = true;
    /// <summary><see cref="InvoiceIssueModes"/>; when not posted, follows <see cref="AutoIssueEnabled"/> (SIGNED / NONE).</summary>
    public string? IssueMode { get; set; }
}

public sealed record EInvoiceVm(CustomerMembership Customer, bool CanManage, bool Linked, bool ServiceReady, CustomerInvoiceSettings Settings,
    EInvoiceAccountInput Account, EInvoiceTemplateInput Template, IReadOnlyList<TingeeInvoiceTemplate>? Templates = null, bool ShowConnect = false,
    IReadOnlyList<InvoiceReadinessItem>? Readiness = null, IReadOnlyList<SavedInvoiceTemplate>? Saved = null, string Environment = SavedInvoiceTemplate.Live, IReadOnlyList<InvoiceEnvironmentVm>? Environments = null)
{
    public CustomerInvoiceProvider Provider => Settings.Info;
}

/// <summary>A SoftDreams environment on the e-invoice page: LIVE (production) or DEMO, its saved login, and whether it is in use.</summary>
public sealed record InvoiceEnvironmentVm(string Code, bool Connected, bool Active, string? Username, DateTime? ConnectedAt)
{
    public bool IsDemo => Code == SavedInvoiceTemplate.Demo;
}

public static class InvoiceText
{
    /// <summary>The provider's logo mark under wwwroot/images/einvoice (taken from the provider's own site, operator 2026-11-28).</summary>
    public static string Logo(string providerCode) => providerCode switch
    {
        TingeeInvoiceProviders.Misa => "~/images/einvoice/misa.svg",
        TingeeInvoiceProviders.SInvoice => "~/images/einvoice/s-invoice.png",
        CustomerInvoiceProviders.SoftDreamsCode => "~/images/einvoice/softdreams.png",
        _ => "~/images/einvoice/softdreams.png",
    };

    /// <summary>Badge for an order's invoice state.</summary>
    public static (string Label, string Css) Status(string status) => status switch
    {
        OrderInvoiceStatus.Pending => ("Đang xuất hóa đơn", "cp-badge--gold"),
        OrderInvoiceStatus.Issued => ("Đã xuất hóa đơn", "cp-badge--green"),
        OrderInvoiceStatus.Failed => ("Xuất hóa đơn lỗi", "cp-badge--red"),
        OrderInvoiceStatus.Draft => ("Đã xuất nháp", "cp-badge--blue"),
        _ => ("Chưa xuất hóa đơn", "cp-badge--soft")
    };
}
