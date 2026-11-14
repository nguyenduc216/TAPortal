using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Ảnh đại diện nhân viên (operator 2026-11-14): any signed-in staff sees the photos; everyone sets their own.
public sealed partial class AccountController
{
    // /Account/Avatar/{employeeId}?v=<version>: the URL changes with each upload, so the browser may keep it long.
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Avatar(Guid id, [FromServices] IEmployeePhotoStore photos)
    {
        EmployeePhoto? photo;
        try { photo = await photos.GetAsync(id); }
        catch (SqlException ex) { logger.LogWarning(ex, "Employee photo unavailable."); return NotFound(); }
        if (photo is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(photo.Data, photo.ContentType);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> SetAvatar(IFormFile? photo, [FromServices] IEmployeePhotoStore photos, bool remove = false)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (await photos.EmployeeOfUserAsync(actor) is not Guid employee)
        {
            TempData["Error"] = "Tài khoản này chưa gắn với hồ sơ nhân viên nên chưa đặt được ảnh đại diện.";
            return RedirectToAction(nameof(Profile));
        }
        await PhotoUpload.ApplyAsync(this, photos, logger, actor, employee, photo, remove);
        return RedirectToAction(nameof(Profile));
    }
}
