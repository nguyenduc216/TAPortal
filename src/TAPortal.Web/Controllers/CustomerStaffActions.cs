using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Khách hàng → Tổng quan → "Nhân sự phụ trách": one manager and the executing employees of a customer (migration 041).
public sealed partial class CustomersController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.CustomersUpdate)]
    public async Task<IActionResult> Staff(Guid id, Guid? manager, List<Guid>? executors, [FromServices] ICustomerStaffStore staff)
    {
        if (ActorOrNull() is not Guid actor) return Forbid();
        try
        {
            var outcome = await staff.SaveAsync(id, manager is { } m && m != Guid.Empty ? m : null, executors ?? [], actor);
            if (outcome == CustomerStaffOutcome.NotFound) return NotFound();
            if (outcome == CustomerStaffOutcome.Invalid) TempData["Error"] = "Nhân viên được chọn không còn hoạt động hoặc chọn quá nhiều người; chưa lưu.";
            else
            {
                logger.LogInformation("Customer staff saved for {CustomerId} by {Actor}: manager {Manager}, {Executors} executors.", id, actor, manager, executors?.Count ?? 0);
                TempData["Success"] = "Đã lưu nhân sự phụ trách.";
            }
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Customer staff could not be saved for {CustomerId}.", id);
            TempData["Error"] = "Chưa lưu được nhân sự phụ trách (cơ sở dữ liệu chưa sẵn sàng). Vui lòng thử lại sau.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
