using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Controllers;

public sealed class PasswordSecurityVm
{
    public string WeakPasswords { get; set; } = "";
    public string TemporaryPassword { get; set; } = "";
    public int WeakCount { get; set; }
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Operator 2026-11-13: "Cài đặt bảo mật mật khẩu" (SYS_ADMIN): the weak password list (separated by ';') applied by the
/// Admin portal and Hóa đơn tự động, and the initial password staff may hand out for new accounts (default 123456).
/// </summary>
[Authorize(Policy = TAPortal.Web.Security.AdminPermissions.Policy.RolesManage)] // SYS_ADMIN only
public sealed class PasswordSecurityController(IPasswordSecurityStore store, ILogger<PasswordSecurityController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var s = await store.GetAsync();
        return View(new PasswordSecurityVm { WeakPasswords = PasswordSecuritySettings.Normalize(s.WeakPasswordsText), TemporaryPassword = s.TemporaryPassword, WeakCount = s.WeakPasswords.Count });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(PasswordSecurityVm vm, [FromForm] bool reset = false)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (reset)
        {
            vm.WeakPasswords = PasswordSecuritySettings.DefaultWeakPasswords;
            vm.TemporaryPassword = PasswordSecuritySettings.DefaultTemporaryPassword;
        }
        var weak = PasswordSecuritySettings.Normalize(vm.WeakPasswords);
        var temporary = (vm.TemporaryPassword ?? "").Trim();
        if (weak.Length == 0) vm.Errors.Add("Nhập ít nhất một mật khẩu yếu.");
        if (weak.Length > 8000) vm.Errors.Add("Danh sách mật khẩu yếu quá dài (tối đa 8.000 ký tự).");
        if (temporary.Length == 0) vm.Errors.Add("Nhập mật khẩu khởi tạo.");
        else if (temporary.Length > PasswordSecuritySettings.MaxTemporaryLength) vm.Errors.Add("Mật khẩu khởi tạo tối đa 128 ký tự.");
        if (vm.Errors.Count == 0)
        {
            try
            {
                await store.SaveAsync(weak, temporary, actor, HttpContext.Connection.RemoteIpAddress?.ToString());
                TempData["Success"] = reset ? "Đã khôi phục danh sách mặc định." : "Đã lưu cài đặt bảo mật mật khẩu. Áp dụng cho TAPortal và Hóa đơn tự động trong khoảng 30 giây.";
                return RedirectToAction(nameof(Index));
            }
            catch (AuditUnavailableException) { vm.Errors.Add("Không ghi được nhật ký audit nên chưa lưu. Liên hệ quản trị hệ thống."); }
            catch (SqlException ex)
            {
                logger.LogError(ex, "PASSWORD_SECURITY_SAVE_FAILED");
                vm.Errors.Add("Không lưu được cài đặt. Vui lòng thử lại.");
            }
        }
        vm.WeakPasswords = weak;
        vm.TemporaryPassword = temporary;
        vm.WeakCount = PasswordSecuritySettings.Parse(weak, temporary).Count;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }
}
