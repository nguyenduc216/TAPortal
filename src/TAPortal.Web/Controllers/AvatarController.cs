using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Ảnh đại diện nhân viên (operator 2026-11-14): HR sets anyone's photo on the employee page (HR.EMPLOYEE.UPDATE).
// Viewing photos and setting one's own live on AccountController (signed-in staff, like Hồ sơ cá nhân).
[Authorize(Policy = AdminPermissions.Policy.EmployeesUpdate)]
public sealed class AvatarController(IEmployeePhotoStore photos, ILogger<AvatarController> logger) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesUpdate)]
    public async Task<IActionResult> SetEmployee(Guid id, IFormFile? photo, bool remove = false)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        await PhotoUpload.ApplyAsync(this, photos, logger, actor, id, photo, remove);
        return RedirectToAction("Details", "Employees", new { id });
    }
}

internal static class PhotoUpload
{
    public static async Task ApplyAsync(Controller c, IEmployeePhotoStore photos, ILogger logger, Guid actor, Guid employee, IFormFile? photo, bool remove)
    {
        var ip = c.HttpContext.Connection.RemoteIpAddress?.ToString();
        try
        {
            if (remove)
            {
                var removed = await photos.DeleteAsync(actor, employee, ip);
                c.TempData[removed == EmployeePhotoOutcome.Saved ? "Success" : "Error"] = removed == EmployeePhotoOutcome.Saved ? "Đã xóa ảnh đại diện." : "Chưa có ảnh đại diện để xóa.";
                return;
            }
            if (photo is null || photo.Length == 0) { c.TempData["Error"] = "Chọn một ảnh."; return; }
            if (photo.Length > EmployeePhotoPolicy.MaxBytes) { c.TempData["Error"] = EmployeePhotoPolicy.TooLarge; return; }
            using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer);
            var (outcome, message) = await photos.SaveAsync(actor, employee, buffer.ToArray(), ip);
            c.TempData[outcome == EmployeePhotoOutcome.Saved ? "Success" : "Error"] = outcome switch
            {
                EmployeePhotoOutcome.Saved => "Đã cập nhật ảnh đại diện.",
                EmployeePhotoOutcome.NotFound => "Không tìm thấy nhân viên.",
                _ => message
            };
        }
        catch (AuditUnavailableException) { c.TempData["Error"] = "Không ghi được nhật ký audit nên chưa lưu ảnh."; }
        catch (SqlException ex) { logger.LogWarning(ex, "Employee photo save failed."); c.TempData["Error"] = "Chưa lưu được ảnh (bảng ảnh nhân viên chưa sẵn sàng)."; }
    }
}
