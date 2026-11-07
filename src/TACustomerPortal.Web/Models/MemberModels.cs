using System.ComponentModel.DataAnnotations;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;

namespace TACustomerPortal.Web.Models;

/// <summary>"Tài khoản & phân quyền": the company's staff accounts.</summary>
public sealed record MembersVm(CustomerMembership Customer, bool CanManage, Guid Actor, IReadOnlyList<CompanyMember> Members);

/// <summary>One account: its permission matrix, password and active state. Editable is false for the OWNER, oneself, an
/// account with more rights than the viewer, or a viewer without manage right on the module.</summary>
public sealed record MemberDetailsVm(CustomerMembership Customer, CompanyMember Member, bool Editable, string? ReadOnlyReason,
    IReadOnlyDictionary<string, CustomerAccessLevel> Levels, bool CanResetPassword);

/// <summary>The permission matrix partial: one row per module, Xem / Quản lý, with the quick presets when editable.</summary>
public sealed record PermissionMatrixVm(IReadOnlyDictionary<string, CustomerAccessLevel> Levels, bool Editable);

public sealed class MemberInput
{
    [Required(ErrorMessage = "Nhập họ tên."), StringLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Nhập tên đăng nhập."),
     RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9._@-]{2,99}$", ErrorMessage = "Tên đăng nhập 3–100 ký tự: chữ không dấu, số và . _ - @, không có khoảng trắng.")]
    public string Username { get; set; } = "";

    [EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string? Email { get; set; }

    [RegularExpression("^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Nhập mật khẩu."), StringLength(256)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Nhập lại mật khẩu."), Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = "";

    public string[] View { get; set; } = [];
    public string[] Manage { get; set; } = [];
}

public sealed record MemberCreateVm(CustomerMembership Customer, MemberInput Input)
{
    public IReadOnlyDictionary<string, CustomerAccessLevel> Levels => CustomerAccess.FromForm(Input.View, Input.Manage);
}
