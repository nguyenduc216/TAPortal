using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Admin → Nhân sự → Nhân viên. Viewing needs HR.EMPLOYEE.VIEW; every change declares its own HR.* permission and is
// re-checked in SQL by SqlEmployeeStore. Private fields (CCCD, MST cá nhân, BHXH, addresses) are read only with
// HR.EMPLOYEE.VIEW_PRIVATE and written only with VIEW_PRIVATE + UPDATE_PRIVATE.
[Authorize(Policy = AdminPermissions.Policy.EmployeesView)]
public sealed class EmployeesController(IEmployeeStore employees, PasswordService passwords, ILogger<EmployeesController> logger) : Controller
{
    internal const string StoreUnavailable = "Không đọc được dữ liệu nhân sự. Chi tiết đã ghi vào nhật ký ứng dụng.";

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] EmployeeFilter filter)
    {
        try { return View(new EmployeesIndexVm(await employees.ListAsync(filter), filter, await employees.LookupsAsync(), null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "EMPLOYEES_UNAVAILABLE");
            return View(new EmployeesIndexVm(null, filter, EmployeeLookups.Empty, StoreUnavailable));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var vm = await DetailsVm(id, new EmployeeAccountInput(), []);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesCreate)]
    public async Task<IActionResult> Create()
    {
        var lookups = await employees.LookupsAsync();
        var suggested = await employees.SuggestCodeAsync();
        var input = new EmployeeInput
        {
            Code = suggested, StartDate = DateTime.Today,
            StatusId = lookups.Statuses.FirstOrDefault(s => s.Code == "ACTIVE" && s.IsActive)?.Id,
            BranchId = lookups.Branches.Count(b => b.IsActive) == 1 ? lookups.Branches.First(b => b.IsActive).Id : null
        };
        return View("Edit", new EmployeeEditVm(null, input, lookups, CanEditPrivate, suggested));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesCreate)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] EmployeeInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        GatePrivate(input);
        var code = EmployeePolicy.NormalizeCode(input.Code);
        if (code.Length == 0) ModelState.AddModelError("Input.Code", "Vui lòng nhập mã nhân viên.");
        else if (!EmployeePolicy.IsValidCode(code)) ModelState.AddModelError("Input.Code", Message(EmployeeOutcome.InvalidCode));
        if (input.StatusId is null) ModelState.AddModelError("Input.StatusId", "Vui lòng chọn trạng thái.");
        if (input.BranchId is null) ModelState.AddModelError("Input.BranchId", "Vui lòng chọn chi nhánh.");
        if (!EmployeePolicy.IsValidGender(input.Gender)) ModelState.AddModelError("Input.Gender", "Giới tính không hợp lệ.");
        if (ModelState.IsValid)
        {
            input.Code = code;
            var result = await employees.CreateAsync(actor, input, Ip);
            if (result.Outcome == EmployeeOutcome.Forbidden) return Forbid();
            if (result.Outcome == EmployeeOutcome.Saved)
            {
                logger.LogInformation("Employee created. Actor {ActorId}, employee {EmployeeId}, audit persisted {AuditPersisted}.", actor, result.Id, result.AuditPersisted);
                TempData["Success"] = "Đã thêm nhân viên." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            ModelState.AddModelError(result.Outcome == EmployeeOutcome.DuplicateCode ? "Input.Code" : string.Empty, Message(result.Outcome));
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new EmployeeEditVm(null, input, await employees.LookupsAsync(), CanEditPrivate, null));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesUpdate)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var employee = await employees.GetAsync(id, CanViewPrivate);
        if (employee is null) return NotFound();
        return View(new EmployeeEditVm(employee, EmployeeInput.From(employee), await employees.LookupsAsync(), CanEditPrivate, null));
    }

    // Binds the basic fields, the private fields (only with VIEW_PRIVATE + UPDATE_PRIVATE) and the concurrency stamp; code,
    // status and assignment posted here are ignored.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesUpdate)]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] EmployeeInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        GatePrivate(input);
        if (!EmployeePolicy.IsValidGender(input.Gender)) ModelState.AddModelError("Input.Gender", "Giới tính không hợp lệ.");
        var reload = false;
        if (ModelState.IsValid)
        {
            var result = await employees.UpdateAsync(actor, id, input, Ip);
            switch (result.Outcome)
            {
                case EmployeeOutcome.NotFound: return NotFound();
                case EmployeeOutcome.Forbidden: return Forbid();
                case EmployeeOutcome.NoChange:
                    TempData["Success"] = "Thông tin nhân viên không thay đổi.";
                    return RedirectToAction(nameof(Details), new { id });
                case EmployeeOutcome.Saved:
                    logger.LogInformation("Employee updated. Actor {ActorId}, employee {EmployeeId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã lưu thông tin nhân viên." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Details), new { id });
                default:
                    ModelState.AddModelError(string.Empty, Message(result.Outcome));
                    reload = result.Outcome == EmployeeOutcome.Conflict;
                    break;
            }
        }
        var employee = await employees.GetAsync(id, CanViewPrivate);
        if (employee is null) return NotFound();
        if (reload) { ModelState.Clear(); ModelState.AddModelError(string.Empty, Message(EmployeeOutcome.Conflict)); input = EmployeeInput.From(employee); }
        else { input.Code = employee.Code; input.Stamp = employee.Stamp; if (!CanEditPrivate) input.Private = employee.Private; }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new EmployeeEditVm(employee, input, await employees.LookupsAsync(), CanEditPrivate, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesChangeAssignment)]
    public async Task<IActionResult> ChangeAssignment(Guid id, EmployeeAssignmentInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (input.BranchId is null) return Back(id, "Vui lòng chọn chi nhánh.");
        if (input.ValidFrom is null) return Back(id, "Vui lòng chọn ngày hiệu lực.");
        if (!ModelState.IsValid) return Back(id, FirstError());
        var result = await employees.ChangeAssignmentAsync(actor, id, input, Ip);
        return Outcome(id, result, "Đã cập nhật phân công (chi nhánh, phòng ban, nhóm, chức danh).", "Phân công không thay đổi.", "assignment changed");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesChangeStatus)]
    public async Task<IActionResult> ChangeStatus(Guid id, EmployeeStatusInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (input.StatusId is null) return Back(id, "Vui lòng chọn trạng thái mới.");
        if (input.EffectiveDate is null) return Back(id, "Vui lòng chọn ngày hiệu lực.");
        if (!ModelState.IsValid) return Back(id, FirstError());
        var before = await employees.GetAsync(id, false);
        if (before is null) return NotFound();
        var result = await employees.ChangeStatusAsync(actor, id, input, Ip);
        var action = Outcome(id, result, "Đã đổi trạng thái nhân viên và ghi lịch sử trạng thái.", "Trạng thái không thay đổi.", "status changed");
        if (result.Outcome == EmployeeOutcome.Saved && before.Account is { LoginEligible: true } account
            && EmployeePolicy.IsLeaving((await employees.LookupsAsync()).Status(input.StatusId)?.Code))
            TempData["Success"] += $" Lưu ý: tài khoản đăng nhập \"{account.Username}\" vẫn đang hoạt động. Khóa tài khoản ở màn hình Người dùng hoặc gỡ liên kết nếu nhân viên không còn làm việc.";
        return action;
    }

    // Creating a login account is user creation: it needs AUTH.USERS.CREATE as well as HR.EMPLOYEE.ACCOUNT_LINK, and runs
    // through PortalDb.InsertUserAsync (password policy, SYS_ADMIN only by SYS_ADMIN, USER_CREATED audit).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesAccountLink)]
    [Authorize(Policy = AdminPermissions.Policy.UsersCreate)]
    public async Task<IActionResult> CreateAccount(Guid id, EmployeeAccountInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Username)) errors.Add("Vui lòng nhập tên đăng nhập.");
        var policy = PasswordPolicy.Validate(input.Password);
        if (!policy.IsValid) errors.Add(policy.Error);
        if (!ModelState.IsValid) errors.Add(FirstError());
        if (errors.Count == 0)
        {
            var options = await employees.AccountOptionsAsync();
            var roleCheck = EmployeePolicy.EvaluateRoles(options.Roles, input.RoleIds, AdminPermissions.IsSystemAdmin(User));
            if (roleCheck != EmployeeOutcome.Saved) errors.Add(Message(roleCheck));
        }
        if (errors.Count == 0)
        {
            var result = await employees.CreateAccountAsync(actor, id, input, passwords.Hash(input.Password!), Ip);
            switch (result.Outcome)
            {
                case EmployeeOutcome.NotFound: return NotFound();
                case EmployeeOutcome.Forbidden: return Forbid();
                case EmployeeOutcome.Saved:
                    logger.LogInformation("Employee account created and linked. Actor {ActorId}, employee {EmployeeId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã tạo tài khoản đăng nhập và liên kết với nhân viên. Giao mật khẩu ban đầu cho nhân viên qua kênh an toàn." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Details), new { id });
                default:
                    errors.Add(Message(result.Outcome));
                    break;
            }
        }
        var vm = await DetailsVm(id, new EmployeeAccountInput { Username = input.Username, Email = input.Email, RoleIds = input.RoleIds }, errors);
        if (vm is null) return NotFound();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Details), vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesAccountLink)]
    public async Task<IActionResult> LinkAccount(Guid id, [FromForm] Guid? userId, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (userId is not Guid user || user == Guid.Empty) return Back(id, "Vui lòng chọn tài khoản cần liên kết.");
        var result = await employees.LinkAccountAsync(actor, id, user, stamp, Ip);
        return Outcome(id, result, "Đã liên kết tài khoản đăng nhập với nhân viên.", "", "account linked");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesAccountLink)]
    public async Task<IActionResult> UnlinkAccount(Guid id, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await employees.UnlinkAccountAsync(actor, id, stamp, Ip);
        return Outcome(id, result, "Đã gỡ liên kết. Tài khoản vẫn tồn tại và giữ nguyên trạng thái, vai trò: khóa hoặc xóa ở màn hình Người dùng nếu cần.", "", "account unlinked");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.EmployeesDelete)]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await employees.DeleteAsync(actor, id, stamp, Ip);
        switch (result.Outcome)
        {
            case EmployeeOutcome.NotFound: return NotFound();
            case EmployeeOutcome.Forbidden: return Forbid();
            case EmployeeOutcome.Saved:
                logger.LogInformation("Employee soft-deleted. Actor {ActorId}, employee {EmployeeId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                TempData["Success"] = "Đã xóa (mềm) hồ sơ nhân viên; lịch sử phân công và trạng thái được giữ nguyên." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Index));
            default:
                return Back(id, Message(result.Outcome));
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------
    private bool CanViewPrivate => AdminPermissions.Has(User, AdminPermissions.EmployeesViewPrivate);
    private bool CanEditPrivate => CanViewPrivate && AdminPermissions.Has(User, AdminPermissions.EmployeesUpdatePrivate);
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Private fields are never bound for an actor who may not both see and change them (forged form fields are dropped).
    private void GatePrivate(EmployeeInput input)
    {
        if (CanEditPrivate) { input.Private ??= new EmployeePrivateInput(); return; }
        input.Private = null;
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Input.Private", StringComparison.Ordinal)).ToList()) ModelState.Remove(key);
    }

    private async Task<EmployeeDetailsVm?> DetailsVm(Guid id, EmployeeAccountInput draft, IReadOnlyList<string> errors)
    {
        var employee = await employees.GetAsync(id, CanViewPrivate);
        if (employee is null) return null;
        EmployeeAccountOptions? accounts = null;
        if (AdminPermissions.Has(User, AdminPermissions.EmployeesAccountLink))
        {
            try { accounts = await employees.AccountOptionsAsync(); }
            catch (SqlException ex) { logger.LogWarning(ex, "Employee account options unavailable for {EmployeeId}.", id); }
        }
        draft.Email ??= employee.CompanyEmail;
        return new(employee, await employees.LookupsAsync(), accounts, draft, errors);
    }

    private IActionResult Outcome(Guid id, EmployeeResult result, string saved, string noChange, string what)
    {
        switch (result.Outcome)
        {
            case EmployeeOutcome.NotFound: return NotFound();
            case EmployeeOutcome.Forbidden: return Forbid();
            case EmployeeOutcome.Saved:
                logger.LogInformation("Employee {What}. Employee {EmployeeId}, audit persisted {AuditPersisted}.", what, id, result.AuditPersisted);
                TempData["Success"] = saved + AuditNotice(result.AuditPersisted);
                break;
            case EmployeeOutcome.NoChange when noChange.Length > 0:
                TempData["Success"] = noChange;
                break;
            default:
                TempData["Error"] = Message(result.Outcome);
                break;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private RedirectToActionResult Back(Guid id, string error)
    {
        TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private string FirstError() => ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "Dữ liệu không hợp lệ.";

    internal static string Message(EmployeeOutcome outcome) => outcome switch
    {
        EmployeeOutcome.DuplicateCode => "Mã nhân viên đã tồn tại.",
        EmployeeOutcome.InvalidCode => "Mã nhân viên chỉ gồm chữ in hoa không dấu, chữ số, dấu chấm, gạch ngang, gạch dưới (tối đa 50 ký tự).",
        EmployeeOutcome.InvalidReference => "Chi nhánh, phòng ban, nhóm, chức danh, trạng thái hoặc loại hình không hợp lệ: phòng ban và nhóm phải thuộc chi nhánh đã chọn, nhóm phải thuộc phòng ban đã chọn, và danh mục phải đang hoạt động.",
        EmployeeOutcome.InvalidDate => "Ngày hiệu lực không được trước ngày bắt đầu của phân công hiện tại.",
        EmployeeOutcome.LinkedAccount => "Nhân viên đang liên kết tài khoản đăng nhập: gỡ liên kết trước khi xóa (tài khoản được quản lý ở màn hình Người dùng).",
        EmployeeOutcome.AlreadyLinked => "Nhân viên đã có tài khoản đăng nhập được liên kết.",
        EmployeeOutcome.NotLinked => "Nhân viên chưa có tài khoản đăng nhập được liên kết.",
        EmployeeOutcome.AccountTaken => "Tài khoản đã được liên kết với nhân viên khác, hoặc tên đăng nhập/email đã tồn tại.",
        EmployeeOutcome.UsernameTaken => "Tên đăng nhập đã tồn tại.",
        EmployeeOutcome.EmailTaken => "Email đã được sử dụng bởi tài khoản khác.",
        EmployeeOutcome.InvalidRole => "Vai trò không hợp lệ hoặc đã ngừng hoạt động.",
        EmployeeOutcome.RoleForbidden => UsersController.SystemAdminGrantMessage,
        EmployeeOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        EmployeeOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        _ => "Hồ sơ nhân viên vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và thực hiện lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";
}
