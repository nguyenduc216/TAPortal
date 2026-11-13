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

/// <summary>
/// Self sign-up (operator decisions 2026-11-06 and 2026-11-12): a business, a household business or an individual. An
/// individual may have no tax code (payments only). No password is typed: the system creates one and emails it.
/// </summary>
public sealed class SignupVm
{
    public const string Business = "BUSINESS";
    public const string Household = "HOUSEHOLD";
    public const string Individual = "INDIVIDUAL";

    /// <summary>BUSINESS (MST 10 or 10-3 digits), HOUSEHOLD (MST 10 or 12 digits) or INDIVIDUAL (MST optional, 10 or 12 digits).</summary>
    public string CustomerType { get; set; } = Business;

    [StringLength(20)]
    public string? TaxCode { get; set; }

    [Required(ErrorMessage = "Nhập tên doanh nghiệp, hộ kinh doanh hoặc họ tên."), StringLength(250, ErrorMessage = "Tên tối đa 250 ký tự.")]
    public string CompanyName { get; set; } = "";

    [StringLength(1000, ErrorMessage = "Địa chỉ tối đa 1000 ký tự.")]
    public string? Address { get; set; }

    [Required(ErrorMessage = "Nhập họ tên người liên hệ."), StringLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")]
    public string ContactName { get; set; } = "";

    [Required(ErrorMessage = "Nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(100, ErrorMessage = "Email tối đa 100 ký tự.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Nhập số điện thoại."), RegularExpression("^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.")]
    public string Phone { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "Vui lòng đồng ý với điều khoản sử dụng.")]
    public bool AcceptTerms { get; set; }
}

/// <summary>After a sign-up: where the login details went and whether the email could be sent.</summary>
public sealed record SignupDoneVm(string Email, string Name, bool EmailSent);
