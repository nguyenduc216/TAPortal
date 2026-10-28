using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace TAPortal.Web.Models;

public sealed class LoginVm
{
    [Required(ErrorMessage = "Vui lòng nhập tài khoản.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class ChangePasswordVm
{
    [Required]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class UserCreateVm
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required] public string DisplayName { get; set; } = string.Empty;
    [EmailAddress] public string? Email { get; set; }
    public string? Phone { get; set; }
    [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    public Guid? RoleId { get; set; }
}

// Username, Status, password and roles are intentionally not bindable from the edit form.
public sealed class UserEditVm
{
    public Guid Id { get; set; }
    [ValidateNever] public string Username { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")] public string DisplayName { get; set; } = string.Empty;
    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] public string? Email { get; set; }
    [StringLength(50, ErrorMessage = "Số điện thoại tối đa 50 ký tự.")] public string? Phone { get; set; }
    [ValidateNever] public string Status { get; set; } = "ACTIVE";
    public bool IsActive { get; set; } = true;
}

// Used for both create and edit; Code is only accepted on create (immutable afterwards).
public sealed class CustomerEditVm
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập mã khách hàng."), StringLength(50, ErrorMessage = "Mã khách hàng tối đa 50 ký tự."), RegularExpression("^[A-Za-z0-9_.-]+$", ErrorMessage = "Mã khách hàng chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang, gạch dưới.")]
    public string Code { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng."), StringLength(250, ErrorMessage = "Tên khách hàng tối đa 250 ký tự.")] public string Name { get; set; } = string.Empty;
    [StringLength(50, ErrorMessage = "MST tối đa 50 ký tự.")] public string? TaxCode { get; set; }
    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] public string? Email { get; set; }
    [StringLength(50, ErrorMessage = "Số điện thoại tối đa 50 ký tự.")] public string? Phone { get; set; }
    [StringLength(1000, ErrorMessage = "Địa chỉ tối đa 1000 ký tự.")] public string? Address { get; set; }
    [ValidateNever] public string Status { get; set; } = "ACTIVE";
    public bool IsActive { get; set; } = true;
}

public sealed record PortalUser(Guid Id, string Username, string DisplayName, string? Email, string? Phone, string Status, bool IsActive);
public sealed record PortalRole(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsActive);
public sealed record PortalMenu(Guid Id, Guid? ParentId, string Code, string Name, string? Icon, string? Route, int SortOrder, bool IsVisible, bool IsActive);
public sealed record CustomerRow(Guid Id, string Code, string Name, string? TaxCode, string? Email, string? Phone, string Status, bool IsActive = true);
public sealed record DashboardVm(int CustomerCount, int UserCount, int ActiveUserCount, int RoleCount);
public sealed record ErrorVm(string? RequestId);
