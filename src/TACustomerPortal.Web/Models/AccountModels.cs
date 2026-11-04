using System.ComponentModel.DataAnnotations;

namespace TACustomerPortal.Web.Models;
public sealed class LoginVm
{
    [Required, StringLength(100)]
    public string Username { get; set; } = "";

    [Required, StringLength(256)]
    public string Password { get; set; } = "";
}

public sealed class ForgotPasswordVm
{
    [Required(ErrorMessage = "Nhập tên đăng nhập hoặc email."), StringLength(256)]
    public string Login { get; set; } = "";

    public bool Enabled { get; set; }
    public bool Sent { get; set; }
}

public sealed class ResetPasswordVm
{
    public string? Token { get; set; }

    [Required, StringLength(256)]
    public string NewPassword { get; set; } = "";

    [Required, Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = "";

    public bool Valid { get; set; }
    public string? Username { get; set; }
}

public sealed class ChangePasswordVm
{
    [Required, StringLength(256)]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(256)]
    public string NewPassword { get; set; } = "";

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = "";
}
