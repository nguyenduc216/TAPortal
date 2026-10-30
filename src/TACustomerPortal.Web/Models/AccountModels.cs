using System.ComponentModel.DataAnnotations;

namespace TACustomerPortal.Web.Models;
public sealed class LoginVm
{
    [Required, StringLength(100)]
    public string Username { get; set; } = "";

    [Required, StringLength(256)]
    public string Password { get; set; } = "";
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
