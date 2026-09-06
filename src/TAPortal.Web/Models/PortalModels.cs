using System.ComponentModel.DataAnnotations;

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

public sealed record PortalUser(Guid Id, string Username, string DisplayName, string? Email, string? Phone, string Status, bool IsActive);
public sealed record PortalRole(Guid Id, string Code, string Name, string? Description, bool IsSystem, bool IsActive);
public sealed record PortalMenu(Guid Id, Guid? ParentId, string Code, string Name, string? Icon, string? Route, int SortOrder, bool IsVisible, bool IsActive);
public sealed record CustomerRow(Guid Id, string Code, string Name, string? TaxCode, string? Email, string? Phone, string Status);
public sealed record DashboardVm(int CustomerCount, int UserCount, int ActiveUserCount, int RoleCount);
