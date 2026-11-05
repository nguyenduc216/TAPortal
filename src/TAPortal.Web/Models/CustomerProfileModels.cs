using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TAPortal.Web.Models;

// Customer profile (CUSTOMER-PROFILE phase 1): the per-customer sheet of the old Excel workbook, split into the
// business profile, representatives and system accounts. Codes match the CHECK constraints of migration 013.
public static class CustomerProfileCodes
{
    public static readonly IReadOnlyList<(string Code, string Label)> LegalForms =
    [
        ("LLC_1", "Công ty TNHH một thành viên"), ("LLC_2", "Công ty TNHH hai thành viên trở lên"), ("JSC", "Công ty cổ phần"),
        ("PRIVATE", "Doanh nghiệp tư nhân"), ("PARTNERSHIP", "Công ty hợp danh"), ("HOUSEHOLD", "Hộ kinh doanh"),
        ("INDIVIDUAL", "Cá nhân"), ("OTHER", "Khác"),
    ];

    public static readonly IReadOnlyList<(string Code, string Label)> BusinessStatuses =
        [("OPERATING", "Đang hoạt động"), ("SUSPENDED", "Tạm ngừng"), ("DISSOLVING", "Đang giải thể"), ("DISSOLVED", "Đã giải thể")];

    public static readonly IReadOnlyList<(string Code, string Label)> RepresentativeRoles =
    [
        ("DIRECTOR", "Giám đốc"), ("GENERAL_DIRECTOR", "Tổng giám đốc"), ("HIRED_DIRECTOR", "Giám đốc (thuê)"), ("OWNER", "Chủ hộ kinh doanh"),
        ("CONTACT", "Người phụ trách"), ("CHIEF_ACCOUNTANT", "Kế toán trưởng"), ("ACCOUNTANT", "Kế toán"), ("OTHER", "Khác"),
    ];

    public static readonly IReadOnlyList<(string Code, string Label)> Genders = [("MALE", "Nam"), ("FEMALE", "Nữ")];

    // Order is the display order of the groups on the "Tài khoản hệ thống" tab.
    public static readonly IReadOnlyList<(string Code, string Label)> SystemTypes =
    [
        ("TAX_PORTAL", "Thuế điện tử (thuedientu.gdt.gov.vn)"), ("EINVOICE_PORTAL", "Hóa đơn điện tử (hoadondientu.gdt.gov.vn)"),
        ("ETAX_MOBILE", "eTax Mobile"), ("DIGITAL_SIGNATURE", "Chứng thư số"), ("EINVOICE_PROVIDER", "Phần mềm xuất hóa đơn"),
        ("SOCIAL_INSURANCE", "Phần mềm BHXH"), ("BANKING", "Ngân hàng điện tử"), ("SALES_POS", "Phần mềm bán hàng"),
        ("ACCOUNTING_SOFTWARE", "Phần mềm kế toán"), ("EMAIL", "Email"), ("GOV_OTHER", "Cổng cơ quan nhà nước khác"), ("OTHER", "Phần mềm khác"),
    ];

    public static readonly IReadOnlyList<(string Code, string Label)> CertificateTypes =
        [("USB_TOKEN", "USB Token"), ("REMOTE_SIGNING", "Ký số từ xa"), ("HSM", "HSM")];

    public static readonly IReadOnlyList<(string Code, string Label)> AccountStatuses =
    [
        ("ACTIVE", "Đang dùng"), ("PENDING", "Chờ chấp nhận"), ("LOCKED", "Bị khóa"), ("INVALID", "Sai mật khẩu / không đăng nhập được"), ("INACTIVE", "Ngừng dùng"),
    ];

    public static readonly IReadOnlyList<(string Code, string Label)> ManagedBy = [("TA", "T.A quản lý"), ("CUSTOMER", "Khách hàng tự làm")];

    // Suggestions only (datalist); the provider column accepts free text.
    public static readonly IReadOnlyList<string> Providers =
        ["MISA", "Viettel", "VNPT", "IntrustCA", "FPT", "BKAV", "KiotViet", "iPOS", "Sapo", "POS365", "Vietcombank", "Techcombank", "MB Bank", "ACB", "Sacombank", "Gmail"];

    public static bool IsKnown(IReadOnlyList<(string Code, string Label)> list, string? code) => code is not null && list.Any(x => x.Code == code);

    public static string Label(IReadOnlyList<(string Code, string Label)> list, string? code) =>
        code is null ? "—" : list.FirstOrDefault(x => x.Code == code).Label ?? code;

    // "Hết hạn" / "Sắp hết hạn" within 30 days; null when there is no expiry date.
    public static (string Label, string Css)? Expiry(DateOnly? expiresAt, DateOnly today) => expiresAt switch
    {
        null => null,
        { } d when d < today => ("Đã hết hạn", "ta-badge--danger"),
        { } d when d <= today.AddDays(30) => ("Sắp hết hạn", "ta-badge--warning"),
        _ => ("Còn hạn", "ta-badge--success"),
    };

    // List views never show a full identity number.
    public static string? MaskIdentity(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 4 ? new string('•', value.Length) : new string('•', value.Length - 4) + value[^4..];
}

public sealed class CustomerProfileVm
{
    [ValidateNever] public Guid CustomerId { get; set; }
    [StringLength(100, ErrorMessage = "Tên thường gọi tối đa 100 ký tự.")] public string? ShortName { get; set; }
    public string? LegalForm { get; set; }
    [Range(0, 999_999_999_999_999, ErrorMessage = "Vốn điều lệ không hợp lệ.")] public decimal? CharterCapital { get; set; }
    public DateOnly? EstablishedDate { get; set; }
    [StringLength(10, ErrorMessage = "Mã ngành tối đa 10 ký tự."), RegularExpression("^[0-9A-Za-z.]*$", ErrorMessage = "Mã ngành chỉ gồm chữ số (mã VSIC).")] public string? MainIndustryCode { get; set; }
    [StringLength(500)] public string? MainIndustryName { get; set; }
    [StringLength(200)] public string? BusinessLine { get; set; }
    [StringLength(200)] public string? TaxAuthority { get; set; }
    [StringLength(150)] public string? TaxOfficerName { get; set; }
    [StringLength(30)] public string? TaxOfficerPhone { get; set; }
    public string? BusinessStatus { get; set; }
    public DateOnly? BusinessStatusDate { get; set; }
    [StringLength(200)] public string? ScaleNote { get; set; }
    [StringLength(30), RegularExpression("^[A-Za-z0-9_.-]*$", ErrorMessage = "Mã nguồn khách chỉ gồm chữ không dấu, số, dấu chấm, gạch.")] public string? SourceCode { get; set; }
    [StringLength(250)] public string? SourceNote { get; set; }
    public Guid? ParentCustomerId { get; set; }
    [StringLength(4000, ErrorMessage = "Ghi chú tối đa 4000 ký tự.")] public string? Note { get; set; }
    // Hex rowversion of the stored profile; empty when the profile does not exist yet.
    [ValidateNever] public string? Stamp { get; set; }
    [ValidateNever] public DateTime? UpdatedAt { get; set; }
}

public sealed class RepresentativeInput
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn vai trò.")] public string RoleCode { get; set; } = "DIRECTOR";
    public bool IsLegalRepresentative { get; set; }
    public bool IsPrimaryContact { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")] public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    [StringLength(20, ErrorMessage = "Số CCCD/CMND tối đa 20 ký tự."), RegularExpression("^[0-9A-Za-z]*$", ErrorMessage = "Số CCCD/CMND chỉ gồm chữ số.")] public string? IdentityNumber { get; set; }
    public DateOnly? IdentityIssueDate { get; set; }
    [StringLength(200)] public string? IdentityIssuePlace { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] public string? Email { get; set; }
    [StringLength(30)] public string? Phone { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    public int SortOrder { get; set; }
    [ValidateNever] public string? Stamp { get; set; }
}

public sealed record RepresentativeRow(Guid Id, string RoleCode, bool IsLegalRepresentative, bool IsPrimaryContact, string FullName, string? Gender,
    DateOnly? DateOfBirth, string? IdentityNumber, DateOnly? IdentityIssueDate, string? IdentityIssuePlace, string? Address, string? Email, string? Phone,
    string? Note, int SortOrder, string Stamp)
{
    public RepresentativeInput ToInput() => new()
    {
        Id = Id, RoleCode = RoleCode, IsLegalRepresentative = IsLegalRepresentative, IsPrimaryContact = IsPrimaryContact, FullName = FullName, Gender = Gender,
        DateOfBirth = DateOfBirth, IdentityNumber = IdentityNumber, IdentityIssueDate = IdentityIssueDate, IdentityIssuePlace = IdentityIssuePlace,
        Address = Address, Email = Email, Phone = Phone, Note = Note, SortOrder = SortOrder, Stamp = Stamp,
    };
}

// Password / Pin are write-only: a blank value keeps the stored secret, ClearPassword / ClearPin remove it.
public sealed class SystemAccountInput
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn loại hệ thống.")] public string SystemType { get; set; } = "TAX_PORTAL";
    [StringLength(100)] public string? ProviderName { get; set; }
    [StringLength(150)] public string? SystemName { get; set; }
    [StringLength(500), Url(ErrorMessage = "Link đăng nhập phải là địa chỉ http(s) đầy đủ.")] public string? LoginUrl { get; set; }
    [StringLength(256, ErrorMessage = "Tên đăng nhập tối đa 256 ký tự.")] public string? Username { get; set; }
    [StringLength(30)] public string? LoginPhone { get; set; }
    [StringLength(150)] public string? StoreName { get; set; }
    [StringLength(50)] public string? UnitCode { get; set; }
    [StringLength(50)] public string? BankAccountNumber { get; set; }
    public string? CertificateType { get; set; }
    [StringLength(100)] public string? CertificateSerial { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    [StringLength(500, ErrorMessage = "Mật khẩu tối đa 500 ký tự.")] public string? Password { get; set; }
    public bool ClearPassword { get; set; }
    [StringLength(100, ErrorMessage = "PIN tối đa 100 ký tự.")] public string? Pin { get; set; }
    public bool ClearPin { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string ManagedBy { get; set; } = "TA";
    [StringLength(1000)] public string? Note { get; set; }
    public int SortOrder { get; set; }
    [ValidateNever] public string? Stamp { get; set; }
}

public sealed record SystemAccountRow(Guid Id, string SystemType, string? ProviderName, string? SystemName, string? LoginUrl, string? Username,
    string? LoginPhone, string? StoreName, string? UnitCode, string? BankAccountNumber, string? CertificateType, string? CertificateSerial,
    DateOnly? ValidFrom, DateOnly? ExpiresAt, bool HasPassword, bool HasPin, DateTime? SecretChangedAt, string Status, string ManagedBy,
    string? Note, int SortOrder, string Stamp)
{
    public SystemAccountInput ToInput() => new()
    {
        Id = Id, SystemType = SystemType, ProviderName = ProviderName, SystemName = SystemName, LoginUrl = LoginUrl, Username = Username, LoginPhone = LoginPhone,
        StoreName = StoreName, UnitCode = UnitCode, BankAccountNumber = BankAccountNumber, CertificateType = CertificateType, CertificateSerial = CertificateSerial,
        ValidFrom = ValidFrom, ExpiresAt = ExpiresAt, Status = Status, ManagedBy = ManagedBy, Note = Note, SortOrder = SortOrder, Stamp = Stamp,
    };
}

public sealed record CustomerOption(Guid Id, string Code, string Name);

// Store == false: the tables of migration 013 could not be read (not applied yet, or no permission).
public sealed record CustomerProfileTabVm(CustomerEditVm Customer, CustomerProfileVm Profile, IReadOnlyList<CustomerOption> Parents, bool StoreAvailable);

public sealed record CustomerRepresentativesVm(CustomerEditVm Customer, IReadOnlyList<RepresentativeRow>? Rows, RepresentativeInput? Edit);

public sealed record CustomerSystemAccountsVm(CustomerEditVm Customer, IReadOnlyList<SystemAccountRow>? Rows, SystemAccountInput? Edit, bool SecretsEnabled, bool CanReveal);
