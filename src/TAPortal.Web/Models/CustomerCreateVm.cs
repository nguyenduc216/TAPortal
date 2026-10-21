using System.ComponentModel.DataAnnotations;

namespace TAPortal.Web.Models;

// CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — lengths match the audited live Customers schema; tax code is not unique.
public sealed class CustomerCreateVm
{
    [Required(ErrorMessage = "Vui lòng nhập mã khách hàng.")]
    [RegularExpression("[A-Za-z0-9]{6}", ErrorMessage = "Mã khách hàng phải gồm đúng 6 ký tự chữ hoặc số.")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(250, ErrorMessage = "Tên khách hàng tối đa 250 ký tự.")]
    public string Name { get; set; } = "";
    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự.")]
    public string? TaxCode { get; set; }
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256, ErrorMessage = "Email tối đa 256 ký tự.")]
    public string? Email { get; set; }
    [StringLength(50, ErrorMessage = "Điện thoại tối đa 50 ký tự.")]
    public string? Phone { get; set; }
    [StringLength(1000)] public string? Address { get; set; }
    [StringLength(500)] [Url] public string? LogoUrl { get; set; }
    public List<CustomerContactVm> Contacts { get; set; } = new();
    [Required, RegularExpression("ACTIVE|INACTIVE", ErrorMessage = "Trạng thái không hợp lệ.")]
    public string Status { get; set; } = "ACTIVE";
}

public sealed class CustomerContactVm
{
    [Required, StringLength(150)] public string Name { get; set; } = "";
    [StringLength(100)] public string? Role { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
}

public enum CustomerWriteResult { Success, DuplicateCode, Invalid, Forbidden, Failed }

public static class CustomerRules
{
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — normalize optional blanks without imposing unverified uniqueness rules.
    public static void Normalize(CustomerCreateVm value)
    {
        value.Code = value.Code?.Trim() ?? "";
        value.Name = value.Name?.Trim() ?? "";
        value.Code = value.Code.ToUpperInvariant();
        value.TaxCode = Blank(value.TaxCode); value.Email = Blank(value.Email); value.Phone = Blank(value.Phone);
        value.Address = Blank(value.Address); value.LogoUrl = Blank(value.LogoUrl);
        foreach (var contact in value.Contacts) { contact.Name = contact.Name.Trim(); contact.Role = Blank(contact.Role); contact.Email = Blank(contact.Email); contact.Phone = Blank(contact.Phone); }
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — keep blank nullable fields as SQL NULL.
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — persistence and both UI routes share validation.
    public static bool IsValid(CustomerCreateVm value) => value.Contacts.Count <= 20 && Validator.TryValidateObject(value, new ValidationContext(value), new List<ValidationResult>(), true) && value.Contacts.All(c => Validator.TryValidateObject(c, new ValidationContext(c), new List<ValidationResult>(), true));
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — one search definition drives lists and export, including optional contacts/status.
    public static IEnumerable<CustomerRow> Filter(IEnumerable<CustomerRow> rows, string? search)
    {
        var query = search?.Trim();
        return string.IsNullOrEmpty(query) ? rows : rows.Where(x => $"{x.Code} {x.Name} {x.TaxCode} {x.Email} {x.Phone} {x.Status}".Contains(query, StringComparison.OrdinalIgnoreCase));
    }
    // CHANGE_ID: TAPORTAL-20261012-CUSTOMERS-001 — safe Vietnamese persistence feedback never includes database exceptions.
    public static string Message(CustomerWriteResult result) => result switch
    {
        CustomerWriteResult.Success => "Đã thêm khách hàng thành công.",
        CustomerWriteResult.DuplicateCode => "Mã khách hàng đã tồn tại.",
        CustomerWriteResult.Forbidden => "Bạn không có quyền thêm khách hàng.",
        CustomerWriteResult.Invalid => "Vui lòng kiểm tra thông tin khách hàng.",
        _ => "Không thể lưu khách hàng lúc này. Vui lòng thử lại."
    };
}
