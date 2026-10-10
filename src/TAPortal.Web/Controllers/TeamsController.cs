using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

[Authorize(Policy=PermissionPolicies.FunctionActionPrefix+"Teams.Index")]
[Route("hr/teams")]
public sealed class TeamsController(PortalDb db):Controller
{
 public async Task<IActionResult> Index(){var id=UserId()??Guid.Empty;var permissions=await db.GetEffectivePermissionsAsync(id);var options=await db.GetTeamOptionsAsync();return View(new TeamPageVm(await db.GetTeamsAsync(),options.Departments,options.Teams,permissions.Contains("HR.TEAM.CREATE"),permissions.Contains("HR.TEAM.UPDATE"),permissions.Contains("HR.TEAM.DELETE")));}
 [HttpPost("create"),ValidateAntiForgeryToken,Authorize(Policy=PermissionPolicies.FunctionActionPrefix+"Teams.Create")]
 public async Task<IActionResult>Create(TeamCreateVm vm){Normalize(vm);if(ModelState.IsValid&&await db.TeamCodeExistsAsync(vm.Code))ModelState.AddModelError(nameof(vm.Code),"Mã Team đã tồn tại.");if(!ModelState.IsValid)return Invalid();return Result(await db.CreateTeamAsync(vm,UserId()),"Đã tạo Team mới.");}
 [HttpPost("update"),ValidateAntiForgeryToken,Authorize(Policy=PermissionPolicies.FunctionActionPrefix+"Teams.Update")]
 public async Task<IActionResult>Update(TeamUpdateVm vm){Normalize(vm);if(!TeamRules.TryDecodeRowVersion(vm.RowVersion,out var rv))ModelState.AddModelError(nameof(vm.RowVersion),"Phiên bản dữ liệu không hợp lệ.");if(ModelState.IsValid&&await db.TeamCodeExistsAsync(vm.Code,vm.Id))ModelState.AddModelError(nameof(vm.Code),"Mã Team đã tồn tại.");if(!ModelState.IsValid)return Invalid();return Result(await db.UpdateTeamAsync(vm,rv,UserId()),"Đã cập nhật Team.");}
 [HttpPost("toggle-status"),ValidateAntiForgeryToken,Authorize(Policy=PermissionPolicies.FunctionActionPrefix+"Teams.Update")]
 public async Task<IActionResult>ToggleStatus(TeamMutationVm vm){if(!TeamRules.TryDecodeRowVersion(vm.RowVersion,out var rv))return Result(TeamWriteResult.ConcurrencyConflict,"");return Result(await db.SetTeamActiveAsync(vm.Id,vm.IsActive,rv,UserId()),vm.IsActive?"Đã kích hoạt Team.":"Đã ngừng hoạt động Team.");}
 [HttpPost("delete"),ValidateAntiForgeryToken,Authorize(Policy=PermissionPolicies.FunctionActionPrefix+"Teams.Delete")]
 public async Task<IActionResult>Delete(TeamMutationVm vm){if(!TeamRules.TryDecodeRowVersion(vm.RowVersion,out var rv))return Result(TeamWriteResult.ConcurrencyConflict,"");return Result(await db.SoftDeleteTeamAsync(vm.Id,rv,UserId()),"Đã xóa mềm Team.");}
 private Guid? UserId()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:null;
 private static void Normalize(TeamCreateVm vm){vm.Code=TeamRules.NormalizeCode(vm.Code);vm.Name=vm.Name?.Trim()??string.Empty;vm.Description=string.IsNullOrWhiteSpace(vm.Description)?null:vm.Description.Trim();}
 private IActionResult Invalid(){TempData["Error"]=string.Join(" ",ModelState.Values.SelectMany(x=>x.Errors).Select(x=>x.ErrorMessage));return RedirectToAction(nameof(Index));}
 private IActionResult Result(TeamWriteResult result,string success){if(result==TeamWriteResult.Success)TempData["Success"]=success;else TempData["Error"]=result switch{TeamWriteResult.DuplicateCode=>"Mã Team đã tồn tại.",TeamWriteResult.InvalidDepartment=>"Phòng ban không tồn tại hoặc không hoạt động.",TeamWriteResult.InvalidParent=>"Team cha phải tồn tại và thuộc cùng phòng ban.",TeamWriteResult.SelfParent=>"Team không thể là Team cha của chính nó.",TeamWriteResult.Cycle=>"Quan hệ Team cha tạo thành chu trình không hợp lệ.",TeamWriteResult.HasDependencies=>"Không thể xóa Team vì đang có Team con hoặc nhân viên đang được phân công.",_=>"Dữ liệu Team đã được thay đổi bởi người khác. Vui lòng tải lại và thử lại."};return RedirectToAction(nameof(Index));}
}
