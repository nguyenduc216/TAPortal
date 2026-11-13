using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

[Authorize(Policy = AdminPermissions.Policy.DashboardView)]
public sealed class HomeController(IDashboardStore dashboard, IWebsiteAdminStore website, AdminAuditOptions audit, AdminLoginThrottleOptions throttle, ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index()
    {
        // "Yêu cầu báo giá từ website" block: only for users allowed to see the requests; a failure hides the numbers, not the page.
        if (AdminPermissions.Has(User, AdminPermissions.WebsiteLeadsView))
        {
            try { ViewData["WebsiteLeads"] = await website.LeadSummaryAsync(DateTime.UtcNow) ?? (object)"not-ready"; }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
            {
                logger.LogError(ex, "Website lead summary could not be loaded.");
                ViewData["WebsiteLeads"] = "error";
            }
        }
        DashboardSnapshot? data = null;
        try
        {
            data = await dashboard.LoadAsync();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            // Never show exception details; the page renders an explicit "data unavailable" state instead of zeros.
            logger.LogError(ex, "Dashboard data could not be loaded.");
        }
        return View(new DashboardPageVm(data, audit.RequireDurableAudit, throttle.AccountPermitLimit, throttle.IpPermitLimit, throttle.Window));
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View(new ErrorVm(Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }
}

[Authorize(Policy = AdminPermissions.Policy.UsersView)]
public sealed class UsersController(PortalDb db, PasswordService passwords, IUserRoleStore roleStore, IUserAccountStore accounts, IEmployeeStore employees, ILogger<UsersController> logger) : Controller
{
    public async Task<IActionResult> Index() => await IndexView();

    // The list view needs the role catalogue (create modal), each user's role names and the linked employee (Nhân sự);
    // loaded here instead of in the view.
    private async Task<IActionResult> IndexView()
    {
        var users = await db.GetUsersAsync();
        ViewData["Roles"] = await db.GetRolesAsync();
        ViewData["RoleMap"] = await db.GetUserRoleMapAsync();
        try { ViewData["EmployeeMap"] = await employees.LinkedEmployeesAsync(); }
        catch (SqlException ex) { logger.LogWarning(ex, "Linked employees unavailable for the Users list."); }
        return View(nameof(Index), users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.CREATE")]
    public async Task<IActionResult> Create(UserCreateVm vm)
    {
        // Operator 2026-11-13: a weak/initial password is allowed here and must be changed at the first sign-in.
        var policy = (await HttpContext.RequestServices.GetRequiredService<IPasswordSecurityStore>().GetAsync()).ValidateAssigned(vm.Password);
        if (!policy.IsValid) ModelState.AddModelError(nameof(vm.Password), policy.Error);
        if (!string.IsNullOrWhiteSpace(vm.Username) && await db.UsernameExistsAsync(vm.Username)) ModelState.AddModelError(nameof(vm.Username), "Tên đăng nhập đã tồn tại.");
        if (ModelState.IsValid && await db.EmailInUseAsync(vm.Email, null)) ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác.");
        // Only a SYS_ADMIN may create an account holding SYS_ADMIN (re-checked again in SQL by PortalDb.CreateUserRoleSql).
        if (ModelState.IsValid && vm.RoleId is not null && !AdminPermissions.IsSystemAdmin(User))
            ModelState.AddModelError(nameof(vm.RoleId), SystemAdminGrantMessage);
        if (ModelState.IsValid)
        {
            try
            {
                await db.CreateUserAsync(vm, policy.MustChange ? passwords.HashTemporary(vm.Password) : passwords.Hash(vm.Password), ActorId(User), policy.MustChange);
                TempData["Success"] = "Đã tạo người dùng mới.";
                return RedirectToAction(nameof(Index));
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập/email đã tồn tại hoặc vai trò không hợp lệ.");
            }
            catch (AuditUnavailableException)
            {
                ModelState.AddModelError(string.Empty, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên tài khoản chưa được tạo. Liên hệ quản trị cơ sở dữ liệu.");
            }
            catch (UnauthorizedAccessException)
            {
                ModelState.AddModelError(nameof(vm.RoleId), "Vai trò đã chọn không hợp lệ, đã ngừng hoạt động hoặc bạn không được phép gán vai trò này.");
            }
        }
        // Re-render instead of redirecting so the entered values (except the password) are kept and the modal reopens.
        Response.StatusCode = StatusCodes.Status400BadRequest;
        ViewData["CreateUser"] = vm;
        ViewData["CreateUserErrors"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        return await IndexView();
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ViewBag.Account = account;
        return View(new UserEditVm { Id = account.Id, Username = account.Username, DisplayName = account.DisplayName, Email = account.Email, Phone = account.Phone, Status = account.Status, IsActive = account.IsActive, Stamp = account.Stamp });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, UserEditVm vm)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        vm.Id = id;
        if (ModelState.IsValid)
        {
            var result = await accounts.UpdateAsync(actor, vm, vm.Stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
            switch (result.Outcome)
            {
                case AccountChangeOutcome.Updated:
                    LogAudit("updated", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã cập nhật thông tin người dùng." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Index));
                case AccountChangeOutcome.NotFound: return NotFound();
                case AccountChangeOutcome.Forbidden: return Forbid();
                case AccountChangeOutcome.DuplicateEmail: ModelState.AddModelError(nameof(vm.Email), "Email đã được sử dụng bởi tài khoản khác."); break;
                case AccountChangeOutcome.SelfDeactivation: ModelState.AddModelError(nameof(vm.IsActive), "Không thể tự vô hiệu hóa tài khoản đang đăng nhập."); break;
                default: ModelState.AddModelError(string.Empty, AccountMessage(result.Outcome)); break;
            }
        }
        // Reload the authoritative state; a stale stamp is replaced so the next save compares against current data.
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ViewBag.Account = account;
        vm.Username = account.Username;
        vm.Status = account.Status;
        vm.Stamp = account.Stamp;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.DELETE")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await accounts.GetAsync(id);
        return account is null ? NotFound() : View(account);
    }

    // Soft delete only. Binds just the concurrency stamp.
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.USERS.DELETE")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, [FromForm] string? stamp)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        var result = await accounts.SoftDeleteAsync(actor, id, stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
        switch (result.Outcome)
        {
            case AccountChangeOutcome.Updated:
                LogAudit("soft-deleted", actor, id, result.AuditPersisted);
                TempData["Success"] = "Đã xóa (mềm) người dùng. Tài khoản không thể đăng nhập; dữ liệu lịch sử được giữ nguyên." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Index));
            case AccountChangeOutcome.NotFound: return NotFound();
            case AccountChangeOutcome.Forbidden: return Forbid();
        }
        var account = await accounts.GetAsync(id);
        if (account is null) return NotFound();
        ModelState.AddModelError(string.Empty, AccountMessage(result.Outcome));
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Delete), account);
    }

    internal const string SystemAdminGrantMessage = "Chỉ quản trị hệ thống (SYS_ADMIN) mới được gán vai trò cho tài khoản.";

    private static string AccountMessage(AccountChangeOutcome outcome) => outcome switch
    {
        AccountChangeOutcome.SelfDeletion => "Không thể tự xóa tài khoản đang đăng nhập.",
        AccountChangeOutcome.SelfDeactivation => "Không thể tự vô hiệu hóa tài khoản đang đăng nhập.",
        AccountChangeOutcome.LastSystemAdmin => "Không thể vô hiệu hóa hoặc xóa quản trị viên hệ thống (SYS_ADMIN) hoạt động cuối cùng.",
        AccountChangeOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        AccountChangeOutcome.DuplicateEmail => "Email đã được sử dụng bởi tài khoản khác.",
        _ => "Người dùng vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và thực hiện lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";

    private void LogAudit(string action, Guid actor, Guid target, bool persisted)
    {
        logger.LogInformation("User {Action}. Actor {ActorId}, target {UserId}, audit persisted {AuditPersisted}.", action, actor, target, persisted);
        if (!persisted) logger.LogWarning("dbo.AuditLogs unavailable; user change for {UserId} recorded only in application logs.", target);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.MANAGE")]
    public async Task<IActionResult> Roles(Guid id)
    {
        var snapshot = await roleStore.GetAsync(id);
        return snapshot is null ? NotFound() : View(snapshot);
    }

    // Binds only the selected role ids and the concurrency stamp; every other posted field is ignored.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "AUTH.ROLES.MANAGE")]
    public async Task<IActionResult> Roles(Guid id, [FromForm] List<Guid>? roleIds, [FromForm] string? stamp)
    {
        if (ActorId(User) is not Guid actor) return Forbid();
        if (!ModelState.IsValid)
            return await RolesError(id, "Danh sách vai trò gửi lên không hợp lệ.");
        var result = await roleStore.UpdateAsync(actor, id, (roleIds ?? []).Distinct().ToList(), stamp, HttpContext.Connection.RemoteIpAddress?.ToString());
        switch (result.Outcome)
        {
            case RoleChangeOutcome.NotFound:
                return NotFound();
            case RoleChangeOutcome.Forbidden:
                return Forbid();
            case RoleChangeOutcome.NoChange:
                TempData["Success"] = "Vai trò không thay đổi.";
                return RedirectToAction(nameof(Roles), new { id });
            case RoleChangeOutcome.Updated:
                logger.LogInformation("User roles updated. Actor {ActorId}, target {UserId}, added {Added}, removed {Removed}, audit persisted {AuditPersisted}.", actor, id, string.Join(",", result.Added ?? []), string.Join(",", result.Removed ?? []), result.AuditPersisted);
                if (!result.AuditPersisted)
                    logger.LogWarning("dbo.AuditLogs unavailable; role change for {UserId} recorded only in application logs.", id);
                TempData["Success"] = "Đã cập nhật vai trò. Quyền mới có hiệu lực từ request kế tiếp của người dùng." + (result.AuditPersisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).");
                return RedirectToAction(nameof(Roles), new { id });
            case RoleChangeOutcome.InvalidRole:
                return await RolesError(id, "Vai trò không hợp lệ hoặc đã ngừng hoạt động.");
            case RoleChangeOutcome.LastSystemAdmin:
                return await RolesError(id, "Không thể thu hồi vai trò Quản trị hệ thống (SYS_ADMIN) của quản trị viên hoạt động cuối cùng.");
            case RoleChangeOutcome.AuditUnavailable:
                return await RolesError(id, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi vai trò đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.");
            default:
                return await RolesError(id, "Vai trò của người dùng vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và lưu lại.");
        }
    }

    private async Task<IActionResult> RolesError(Guid id, string message)
    {
        var snapshot = await roleStore.GetAsync(id);
        if (snapshot is null) return NotFound();
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Roles), snapshot);
    }

    internal static Guid? ActorId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

// Every action needs CRM.CUSTOMERS.VIEW; each write action declares its own permission as well
// (SafeAdminPermissionTests: no POST action of a view-level controller lacks one).
[Authorize(Policy = AdminPermissions.Policy.CustomersView)]
public sealed partial class CustomersController(PortalDb db, ICustomerAdminStore customers, ICustomerPortalAccounts portal, PasswordService passwords, ILogger<CustomersController> logger) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetCustomersAsync());

    // Customers → tab "Tài khoản Portal": every membership of every customer (replaces the old standalone page).
    // Read-only overview; each row opens the customer's workspace tab where the membership is managed.
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.CustomerPortalManage)]
    public async Task<IActionResult> Accounts()
    {
        if (ActorOrNull() is not Guid actor) return Forbid();
        List<MembershipAdminRow>? rows = null;
        List<CustomerRow> customerRows = [];
        try
        {
            rows = await portal.ListAllAsync(actor);
            customerRows = await portal.CustomersAsync();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer portal account overview unavailable."); rows = null; }
        return View(new CustomerAccountsOverviewVm(rows, customerRows));
    }

    // ---- Customer Workspace ------------------------------------------------------------------------------
    // Every workspace action takes the customer from the route id; posted customer ids must match it (or be absent),
    // so one customer's page can never act on another customer's memberships.

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        PortalAccountSummary? summary = null;
        // The portal-account summary is only for accounts that may manage them; others see the overview without it.
        if (AdminPermissions.Has(User, AdminPermissions.CustomerPortalManage))
        try
        {
            var rows = await portal.ListAsync(actor, id);
            var effective = rows.Count(r => MembershipDisplay.Evaluate(r.Active, r.Status, Row(customer)).Effective);
            summary = new(rows.Count, effective, rows.Count(r => !r.Active));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer portal accounts unavailable for customer {CustomerId}.", id); }
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Overview);
        return View(new CustomerOverviewVm(customer, summary));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.CustomerPortalManage)]
    public async Task<IActionResult> PortalAccounts(Guid id)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        List<MembershipAdminRow>? rows = null;
        try { rows = await portal.ListAsync(actor, id); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer portal accounts unavailable for customer {CustomerId}.", id); }
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Portal);
        return View(new CustomerPortalAccountsVm(customer, rows?.Where(r => r.CustomerId == id).ToList(), rows is null ? [] : await portal.CandidateUsersAsync()));
    }

    // mode "assign": grant/update an existing user (UserId required). mode "create": create a new user (no TAPortal
    // admin role) and grant it. Creating a membership never silently creates a user.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.CustomerPortalManage)]
    public async Task<IActionResult> SavePortalAccount(Guid id, [FromForm] string? mode, MembershipInput input)
    {
        if (input.CustomerId != Guid.Empty && input.CustomerId != id) return BadRequest();
        if (await customers.GetAsync(id) is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        input.CustomerId = id;
        string? hash = null;
        if (mode == "assign")
        {
            if (input.UserId is not { } userId || userId == Guid.Empty) ModelState.AddModelError("", "Vui lòng chọn tài khoản cần cấp quyền.");
            else
            {
                // New grants only for non-staff accounts; an existing membership of this customer can still be updated.
                try
                {
                    var isMember = (await portal.ListAsync(actor, id)).Any(m => m.UserId == userId);
                    if (!isMember && !(await portal.CandidateUsersAsync()).Any(u => u.Id == userId))
                        ModelState.AddModelError("", StaffAccountMessage);
                }
                catch (UnauthorizedAccessException) { return Forbid(); }
            }
        }
        else if (mode == "create")
        {
            if (input.UserId.HasValue) return BadRequest();
            if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.DisplayName))
                ModelState.AddModelError("", "Vui lòng nhập tên đăng nhập và họ tên.");
            var policy = (await HttpContext.RequestServices.GetRequiredService<IPasswordSecurityStore>().GetAsync()).ValidateNew(input.Password);
            if (!policy.IsValid) ModelState.AddModelError("", policy.Error);
            else if (ModelState.IsValid) hash = passwords.Hash(input.Password!);
        }
        else return BadRequest();

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Dữ liệu không hợp lệ." : e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(PortalAccounts), new { id });
        }
        try
        {
            await portal.SaveAsync(input, actor, hash);
            TempData["Success"] = mode == "create" ? "Đã tạo tài khoản và cấp quyền truy cập Customer Portal cho khách hàng này." : "Đã cập nhật quyền truy cập Customer Portal.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627 or 547 or 51000)
        {
            TempData["Error"] = "Thông tin trùng lặp, hoặc tài khoản/khách hàng không hợp lệ hay đã ngừng hoạt động.";
        }
        catch (AuditUnavailableException) { TempData["Error"] = AuditUnavailableMessage; }
        return RedirectToAction(nameof(PortalAccounts), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.CustomerPortalManage)]
    public async Task<IActionResult> SetPortalAccountActive(Guid id, [FromForm] Guid userId, [FromForm] bool active, [FromForm] Guid? customerId)
    {
        if (customerId is { } posted && posted != id) return BadRequest();
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        // Granting access (Save) already requires an active customer in SQL; reactivation must follow the same rule.
        // Revoking is always allowed.
        if (active && !CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Portal).Operational)
        {
            TempData["Error"] = InactiveCustomerMessage;
            return RedirectToAction(nameof(PortalAccounts), new { id });
        }
        try
        {
            // Only memberships listed for this customer can be changed from its workspace.
            if (!(await portal.ListAsync(actor, id)).Any(r => r.UserId == userId && r.CustomerId == id))
            {
                TempData["Error"] = "Tài khoản này không có quyền truy cập khách hàng hiện tại.";
                return RedirectToAction(nameof(PortalAccounts), new { id });
            }
            await portal.SetActiveAsync(userId, id, active, actor);
            TempData["Success"] = active ? "Đã kích hoạt lại quyền truy cập Customer Portal." : "Đã thu hồi quyền truy cập Customer Portal của khách hàng này. Tài khoản dùng chung không bị khóa.";
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AuditUnavailableException) { TempData["Error"] = AuditUnavailableMessage; }
        return RedirectToAction(nameof(PortalAccounts), new { id });
    }

    /// <summary>
    /// Resets a Customer Portal password to a random temporary one shown once to the administrator, who hands it to the
    /// customer; the customer must change it at the next sign-in. Existing sessions end (the password stamp changes).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.CustomerPortalManage)]
    public async Task<IActionResult> ResetPortalPassword(Guid id, [FromForm] Guid userId)
    {
        if (await customers.GetAsync(id) is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        try
        {
            var member = (await portal.ListAsync(actor, id)).FirstOrDefault(r => r.UserId == userId && r.CustomerId == id);
            if (member is null || !(await portal.CandidateUsersAsync()).Any(u => u.Id == userId))
            {
                TempData["Error"] = member is null ? "Tài khoản này không có quyền truy cập khách hàng hiện tại." : "Không đặt lại mật khẩu tài khoản nhân viên TA ở đây; dùng màn hình Người dùng.";
                return RedirectToAction(nameof(PortalAccounts), new { id });
            }
            var temporary = PasswordPolicy.GenerateTemporary();
            if (!await portal.ResetPasswordAsync(userId, id, passwords.Hash(temporary), actor))
            {
                TempData["Error"] = "Không đặt lại được mật khẩu: tài khoản không còn thuộc khách hàng này hoặc là tài khoản nhân viên TA.";
                return RedirectToAction(nameof(PortalAccounts), new { id });
            }
            logger.LogInformation("CUSTOMER_PORTAL_PASSWORD_RESET customer={CustomerId} user={UserId} actor={Actor}", id, userId, actor);
            TempData["TempPasswordUser"] = member.Username;
            TempData["TempPassword"] = temporary;
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (AuditUnavailableException) { TempData["Error"] = AuditUnavailableMessage; }
        return RedirectToAction(nameof(PortalAccounts), new { id });
    }

    private const string AuditUnavailableMessage = "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.";

    internal const string StaffAccountMessage = "Tài khoản nhân viên TA (có vai trò TAPortal) không được cấp quyền truy cập Customer Portal.";
    internal const string InactiveCustomerMessage = "Khách hàng đang ngừng hoạt động: không thể kích hoạt lại quyền truy cập Customer Portal.";

    private Guid? ActorOrNull() => UsersController.ActorId(User);

    private static CustomerRow Row(CustomerEditVm c) => new(c.Id, c.Code, c.Name, c.TaxCode, c.Email, c.Phone, c.Status, c.IsActive);

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.CREATE")]
    public IActionResult Create() => View("Edit", new CustomerEditVm());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.CREATE")]
    public async Task<IActionResult> Create(CustomerEditVm vm)
    {
        vm.Id = Guid.Empty;
        if (ModelState.IsValid && Apply(await customers.CreateAsync(vm, UsersController.ActorId(User)), vm))
        {
            TempData["Success"] = "Đã tạo khách hàng mới.";
            return vm.Id == Guid.Empty ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id = vm.Id });
        }
        vm.Id = Guid.Empty;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", vm);
    }

    [HttpGet]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var vm = await customers.GetAsync(id);
        if (vm is null) return NotFound();
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(vm, CustomerWorkspace.Info);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Edit(Guid id, CustomerEditVm vm)
    {
        var current = await customers.GetAsync(id);
        if (current is null) return NotFound();
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(current, CustomerWorkspace.Info);
        vm.Id = id;
        vm.Code = current.Code;
        vm.Status = current.Status;
        ModelState.Remove(nameof(vm.Code));
        if (ModelState.IsValid)
        {
            var result = await customers.UpdateAsync(vm, UsersController.ActorId(User));
            if (result.Outcome == CustomerChangeOutcome.NotFound) return NotFound();
            if (Apply(result, vm))
            {
                TempData["Success"] = "Đã cập nhật thông tin khách hàng.";
                return RedirectToAction(nameof(Edit), new { id });
            }
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(vm);
    }

    private bool Apply(CustomerChangeResult result, CustomerEditVm vm)
    {
        switch (result.Outcome)
        {
            case CustomerChangeOutcome.Saved: return true;
            case CustomerChangeOutcome.DuplicateCode: ModelState.AddModelError(nameof(vm.Code), "Mã khách hàng đã tồn tại."); break;
            case CustomerChangeOutcome.InvalidTaxCode: ModelState.AddModelError(nameof(vm.TaxCode), "Mã số thuế phải gồm 10 chữ số, hoặc 10 chữ số + \"-\" + 3 chữ số đối với đơn vị phụ thuộc/chi nhánh (ví dụ 0101234567-001)."); break;
            case CustomerChangeOutcome.AuditUnavailable: ModelState.AddModelError(string.Empty, "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu."); break;
            case CustomerChangeOutcome.DuplicateTaxCode: ModelState.AddModelError(nameof(vm.TaxCode), "Mã số thuế đã được sử dụng bởi khách hàng khác" + (result.ConflictingCustomerCode is null ? "." : $" (mã KH {result.ConflictingCustomerCode}).")); break;
            default: ModelState.AddModelError(string.Empty, "Không thể lưu khách hàng."); break;
        }
        return false;
    }
}

[Authorize(Policy = PermissionPolicies.Prefix + "SYSTEM.MENUS.MANAGE")]
public sealed class MenusController(PortalDb db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetMenusAsync());
}
