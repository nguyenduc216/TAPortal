using System.ComponentModel.DataAnnotations;
using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

// Customer Workspace → "Tingee" tab. Link is null when the customer has no Tingee merchant yet;
// StoreAvailable=false when dbo.TingeeMerchants could not be read (migration 011 not run / no permission).
public sealed record CustomerTingeeVm(CustomerEditVm Customer, TingeeMerchantLink? Link, bool StoreAvailable, bool ApiConfigured, TingeeMerchantCreateInput Create);

public sealed class TingeeMerchantCreateInput
{
    [Required(ErrorMessage = "Vui lòng nhập tên hiển thị."), StringLength(500, ErrorMessage = "Tên tối đa 500 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại."), RegularExpression("^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)]
    public string? Email { get; set; }

    [StringLength(20, ErrorMessage = "Mã định danh tối đa 20 ký tự."), RegularExpression("^[A-Za-z0-9_.-]*$", ErrorMessage = "Mã định danh chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang, gạch dưới.")]
    public string? Code { get; set; }
}
