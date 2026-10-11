using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

[Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Departments.Index")]
[Route("hr/departments")]
public sealed class DepartmentsController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var permissions = await db.GetEffectivePermissionsAsync(CurrentUserId() ?? Guid.Empty);
        return View(new DepartmentPageVm(
            await db.GetDepartmentsAsync(),
            permissions.Contains("HR.DEPARTMENT.CREATE"),
            permissions.Contains("HR.DEPARTMENT.UPDATE"),
            permissions.Contains("HR.DEPARTMENT.DELETE")));
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Departments.Create")]
    public async Task<IActionResult> Create(DepartmentCreateVm vm)
    {
        Normalize(vm);
        if (ModelState.IsValid && await db.DepartmentCodeExistsAsync(vm.Code))
            ModelState.AddModelError(nameof(vm.Code), "Mã phòng ban đã tồn tại.");
        if (!ModelState.IsValid) return ValidationRedirect();

        var result = await db.CreateDepartmentAsync(vm, CurrentUserId());
        TempData[result == DepartmentWriteResult.Success ? "Success" : "Error"] = result == DepartmentWriteResult.Success
            ? "Đã tạo phòng ban mới."
            : "Mã phòng ban đã tồn tại. Vui lòng sử dụng mã khác.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("update")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Departments.Update")]
    public async Task<IActionResult> Update(DepartmentUpdateVm vm)
    {
        Normalize(vm);
        if (!DepartmentRules.TryDecodeRowVersion(vm.RowVersion, out var rowVersion))
            ModelState.AddModelError(nameof(vm.RowVersion), "Phiên bản dữ liệu không hợp lệ. Vui lòng tải lại trang.");
        if (ModelState.IsValid && await db.DepartmentCodeExistsAsync(vm.Code, vm.Id))
            ModelState.AddModelError(nameof(vm.Code), "Mã phòng ban đã tồn tại.");
        if (!ModelState.IsValid) return ValidationRedirect();

        return MutationRedirect(await db.UpdateDepartmentAsync(vm, rowVersion, CurrentUserId()), "Đã cập nhật phòng ban.");
    }

    [HttpPost("toggle-status")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Departments.Update")]
    public async Task<IActionResult> ToggleStatus(DepartmentMutationVm vm)
    {
        if (!DepartmentRules.TryDecodeRowVersion(vm.RowVersion, out var rowVersion))
        {
            TempData["Error"] = "Phiên bản dữ liệu không hợp lệ. Vui lòng tải lại trang.";
            return RedirectToAction(nameof(Index));
        }
        return MutationRedirect(await db.SetDepartmentActiveAsync(vm.Id, vm.IsActive, rowVersion, CurrentUserId()), vm.IsActive ? "Đã kích hoạt phòng ban." : "Đã ngừng hoạt động phòng ban.");
    }

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.FunctionActionPrefix + "Departments.Delete")]
    public async Task<IActionResult> Delete(DepartmentMutationVm vm)
    {
        if (!DepartmentRules.TryDecodeRowVersion(vm.RowVersion, out var rowVersion))
        {
            TempData["Error"] = "Phiên bản dữ liệu không hợp lệ. Vui lòng tải lại trang.";
            return RedirectToAction(nameof(Index));
        }
        return MutationRedirect(await db.SoftDeleteDepartmentAsync(vm.Id, rowVersion, CurrentUserId()), "Đã xóa mềm phòng ban.");
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static void Normalize(DepartmentCreateVm vm)
    {
        vm.Code = DepartmentRules.NormalizeCode(vm.Code);
        vm.Name = vm.Name?.Trim() ?? string.Empty;
        vm.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
    }

    private IActionResult ValidationRedirect()
    {
        TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage).Where(x => !string.IsNullOrWhiteSpace(x)));
        return RedirectToAction(nameof(Index));
    }

    private IActionResult MutationRedirect(DepartmentWriteResult result, string success)
    {
        if (result == DepartmentWriteResult.Success) TempData["Success"] = success;
        else TempData["Error"] = result switch
        {
            DepartmentWriteResult.DuplicateCode => "Mã phòng ban đã tồn tại. Vui lòng sử dụng mã khác.",
            DepartmentWriteResult.HasDependencies => "Không thể xóa phòng ban đang được sử dụng bởi đội nhóm hoặc phân công nhân sự.",
            _ => "Dữ liệu phòng ban đã được thay đổi bởi người khác. Vui lòng tải lại và thử lại."
        };
        return RedirectToAction(nameof(Index));
    }
}
