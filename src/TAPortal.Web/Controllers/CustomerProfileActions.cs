using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Security;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Customer Workspace → "Hồ sơ doanh nghiệp", "Người đại diện", "Tài khoản hệ thống" (CUSTOMER-PROFILE phase 1).
// Every action takes the customer from the route id and the store scopes every row by it. Passwords / PINs are
// write-only in forms; they are shown only through RevealSecret, which needs its own permission and a durable audit row.
public sealed partial class CustomersController
{
    public const string SecretRevealPermission = "CRM.CUSTOMERS.SECRETS.REVEAL";

    private const string ProfileStoreUnavailable = "Không đọc/ghi được hồ sơ khách hàng (bảng của migration 013). Kiểm tra migration và quyền cơ sở dữ liệu; chi tiết đã ghi vào nhật ký ứng dụng.";

    // ---- Hồ sơ doanh nghiệp ---------------------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Profile(Guid id, [FromServices] ICustomerProfileStore profiles)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        CustomerProfileVm? profile = null;
        IReadOnlyList<CustomerOption> parents = [];
        var available = true;
        try
        {
            profile = await profiles.GetProfileAsync(id);
            parents = await profiles.CustomerOptionsAsync(id);
        }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer profile unavailable for customer {CustomerId}.", id); available = false; }
        return ProfileView(customer, profile ?? new CustomerProfileVm { CustomerId = id }, parents, available);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> Profile(Guid id, [Bind(Prefix = "Profile")] CustomerProfileVm vm, [FromServices] ICustomerProfileStore profiles)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        vm.CustomerId = id;
        if (vm.LegalForm is not null && !CustomerProfileCodes.IsKnown(CustomerProfileCodes.LegalForms, vm.LegalForm)) ModelState.AddModelError("Profile.LegalForm", "Loại hình không hợp lệ.");
        if (vm.BusinessStatus is not null && !CustomerProfileCodes.IsKnown(CustomerProfileCodes.BusinessStatuses, vm.BusinessStatus)) ModelState.AddModelError("Profile.BusinessStatus", "Tình trạng hoạt động không hợp lệ.");
        if (vm.ParentCustomerId == id) ModelState.AddModelError("Profile.ParentCustomerId", "Không thể chọn chính khách hàng này làm đơn vị liên kết.");
        IReadOnlyList<CustomerOption> parents = [];
        try
        {
            if (ModelState.IsValid)
            {
                switch (await profiles.SaveProfileAsync(vm, actor))
                {
                    case ProfileChangeOutcome.Saved:
                        TempData["Success"] = "Đã lưu hồ sơ doanh nghiệp.";
                        return RedirectToAction(nameof(Profile), new { id });
                    case ProfileChangeOutcome.InvalidParent: ModelState.AddModelError("Profile.ParentCustomerId", "Đơn vị liên kết không tồn tại, đã bị xóa, hoặc đang liên kết ngược lại với khách hàng này."); break;
                    case ProfileChangeOutcome.AuditUnavailable: ModelState.AddModelError(string.Empty, AuditUnavailableMessage); break;
                    default:
                        // Someone else saved first: show their version, the user re-applies the change on top of it.
                        ModelState.Clear();
                        ModelState.AddModelError(string.Empty, ConflictMessage);
                        vm = await profiles.GetProfileAsync(id) ?? new CustomerProfileVm { CustomerId = id };
                        break;
                }
            }
            parents = await profiles.CustomerOptionsAsync(id);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer profile could not be saved for customer {CustomerId}.", id);
            ModelState.AddModelError(string.Empty, ProfileStoreUnavailable);
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return ProfileView(customer, vm, parents, true);
    }

    private ViewResult ProfileView(CustomerEditVm customer, CustomerProfileVm profile, IReadOnlyList<CustomerOption> parents, bool available)
    {
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Profile);
        return View(nameof(Profile), new CustomerProfileTabVm(customer, profile, parents, available));
    }

    // ---- Người đại diện ----------------------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Representatives(Guid id, Guid? edit, bool add, [FromServices] ICustomerProfileStore profiles)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        IReadOnlyList<RepresentativeRow>? rows = null;
        try { rows = await profiles.ListRepresentativesAsync(id); }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer representatives unavailable for customer {CustomerId}.", id); }
        var form = rows is null ? null : add ? new RepresentativeInput { SortOrder = rows.Count } : rows.FirstOrDefault(r => r.Id == edit)?.ToInput();
        return RepresentativesView(customer, rows, form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> SaveRepresentative(Guid id, [Bind(Prefix = "Edit")] RepresentativeInput input, [FromServices] ICustomerProfileStore profiles)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (!CustomerProfileCodes.IsKnown(CustomerProfileCodes.RepresentativeRoles, input.RoleCode)) ModelState.AddModelError("Edit.RoleCode", "Vai trò không hợp lệ.");
        if (input.Gender is not null && !CustomerProfileCodes.IsKnown(CustomerProfileCodes.Genders, input.Gender)) ModelState.AddModelError("Edit.Gender", "Giới tính không hợp lệ.");
        var conflict = false;
        try
        {
            if (ModelState.IsValid)
            {
                var isNew = input.Id is null;
                switch (await profiles.SaveRepresentativeAsync(id, input, actor))
                {
                    case ProfileChangeOutcome.Saved:
                        TempData["Success"] = isNew ? "Đã thêm người đại diện." : "Đã cập nhật người đại diện.";
                        return RedirectToAction(nameof(Representatives), new { id });
                    case ProfileChangeOutcome.NotFound: return NotFound();
                    case ProfileChangeOutcome.AuditUnavailable: ModelState.AddModelError(string.Empty, AuditUnavailableMessage); break;
                    default: ModelState.Clear(); ModelState.AddModelError(string.Empty, ConflictMessage); conflict = true; break;
                }
            }
            var rows = await profiles.ListRepresentativesAsync(id);
            if (conflict && input.Id is { } existing) input = rows.FirstOrDefault(r => r.Id == existing)?.ToInput() ?? input;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return RepresentativesView(customer, rows, input);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer representative could not be saved for customer {CustomerId}.", id);
            TempData["Error"] = ProfileStoreUnavailable;
            return RedirectToAction(nameof(Representatives), new { id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> DeleteRepresentative(Guid id, [FromForm] Guid representativeId, [FromForm] string? stamp, [FromServices] ICustomerProfileStore profiles)
    {
        if (await customers.GetAsync(id) is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        try
        {
            var outcome = await profiles.DeleteRepresentativeAsync(id, representativeId, stamp, actor);
            if (outcome == ProfileChangeOutcome.NotFound) return NotFound();
            Notify(outcome, "Đã xóa người đại diện.");
        }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer representative could not be deleted for customer {CustomerId}.", id); TempData["Error"] = ProfileStoreUnavailable; }
        return RedirectToAction(nameof(Representatives), new { id });
    }

    private ViewResult RepresentativesView(CustomerEditVm customer, IReadOnlyList<RepresentativeRow>? rows, RepresentativeInput? form)
    {
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.Representatives);
        return View(nameof(Representatives), new CustomerRepresentativesVm(customer, rows, form));
    }

    // ---- Tài khoản hệ thống ----------------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> SystemAccounts(Guid id, Guid? edit, string? add, [FromServices] ICustomerProfileStore profiles, [FromServices] ICustomerSecretProtector secrets, [FromServices] IAuthorizationService authorization)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        IReadOnlyList<SystemAccountRow>? rows = null;
        try { rows = await profiles.ListSystemAccountsAsync(id); }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer system accounts unavailable for customer {CustomerId}.", id); }
        SystemAccountInput? form = null;
        if (rows is not null)
            form = add is not null
                ? new SystemAccountInput { SystemType = CustomerProfileCodes.IsKnown(CustomerProfileCodes.SystemTypes, add) ? add : "OTHER", SortOrder = rows.Count }
                : rows.FirstOrDefault(r => r.Id == edit)?.ToInput();
        return await SystemAccountsView(customer, rows, form, secrets, authorization);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> SaveSystemAccount(Guid id, [Bind(Prefix = "Edit")] SystemAccountInput input, [FromServices] ICustomerProfileStore profiles, [FromServices] ICustomerSecretProtector secrets, [FromServices] IAuthorizationService authorization)
    {
        var customer = await customers.GetAsync(id);
        if (customer is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (!CustomerProfileCodes.IsKnown(CustomerProfileCodes.SystemTypes, input.SystemType)) ModelState.AddModelError("Edit.SystemType", "Loại hệ thống không hợp lệ.");
        if (input.CertificateType is not null && !CustomerProfileCodes.IsKnown(CustomerProfileCodes.CertificateTypes, input.CertificateType)) ModelState.AddModelError("Edit.CertificateType", "Loại ký số không hợp lệ.");
        if (!CustomerProfileCodes.IsKnown(CustomerProfileCodes.AccountStatuses, input.Status)) ModelState.AddModelError("Edit.Status", "Trạng thái không hợp lệ.");
        if (!CustomerProfileCodes.IsKnown(CustomerProfileCodes.ManagedBy, input.ManagedBy)) ModelState.AddModelError("Edit.ManagedBy", "Giá trị \"Quản lý bởi\" không hợp lệ.");
        if (!string.IsNullOrWhiteSpace(input.LoginUrl) && !(Uri.TryCreate(input.LoginUrl.Trim(), UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)))
            ModelState.AddModelError("Edit.LoginUrl", "Link đăng nhập phải bắt đầu bằng http:// hoặc https://.");
        if (input.ValidFrom is { } from && input.ExpiresAt is { } to && from > to) ModelState.AddModelError("Edit.ExpiresAt", "Ngày hết hạn phải sau ngày bắt đầu.");
        var setsSecret = !string.IsNullOrEmpty(input.Password) && !input.ClearPassword || !string.IsNullOrEmpty(input.Pin) && !input.ClearPin;
        if (setsSecret && !secrets.Enabled) ModelState.AddModelError(string.Empty, SecretsDisabledMessage);

        var conflict = false;
        try
        {
            if (ModelState.IsValid)
            {
                var accountId = input.Id ?? Guid.NewGuid();
                SecretChange Change(bool clear, string? value, SecretField field) => clear ? new(null, true) : string.IsNullOrEmpty(value) ? SecretChange.Keep
                    : new(secrets.Protect(value, CustomerSecretContext.For(id, accountId, field)), false);
                var password = Change(input.ClearPassword, input.Password, SecretField.Password);
                var pin = Change(input.ClearPin, input.Pin, SecretField.Pin);
                var isNew = input.Id is null;
                switch (await profiles.SaveSystemAccountAsync(id, accountId, input, password, pin, actor))
                {
                    case ProfileChangeOutcome.Saved:
                        TempData["Success"] = isNew ? "Đã thêm tài khoản hệ thống." : "Đã cập nhật tài khoản hệ thống.";
                        return RedirectToAction(nameof(SystemAccounts), new { id });
                    case ProfileChangeOutcome.NotFound: return NotFound();
                    case ProfileChangeOutcome.AuditUnavailable: ModelState.AddModelError(string.Empty, AuditUnavailableMessage); break;
                    default: ModelState.Clear(); ModelState.AddModelError(string.Empty, ConflictMessage); conflict = true; break;
                }
            }
            // Never send a typed secret back to the browser.
            input.Password = null;
            input.Pin = null;
            var rows = await profiles.ListSystemAccountsAsync(id);
            if (conflict && input.Id is { } existing) input = rows.FirstOrDefault(r => r.Id == existing)?.ToInput() ?? input;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return await SystemAccountsView(customer, rows, input, secrets, authorization);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer system account could not be saved for customer {CustomerId}.", id);
            TempData["Error"] = ProfileStoreUnavailable;
            return RedirectToAction(nameof(SystemAccounts), new { id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + "CRM.CUSTOMERS.UPDATE")]
    public async Task<IActionResult> DeleteSystemAccount(Guid id, [FromForm] Guid accountId, [FromForm] string? stamp, [FromServices] ICustomerProfileStore profiles)
    {
        if (await customers.GetAsync(id) is null) return NotFound();
        if (ActorOrNull() is not Guid actor) return Forbid();
        try
        {
            var outcome = await profiles.DeleteSystemAccountAsync(id, accountId, stamp, actor);
            if (outcome == ProfileChangeOutcome.NotFound) return NotFound();
            Notify(outcome, "Đã xóa tài khoản hệ thống (mật khẩu đã lưu cũng bị xóa).");
        }
        catch (SqlException ex) { logger.LogWarning(ex, "Customer system account could not be deleted for customer {CustomerId}.", id); TempData["Error"] = ProfileStoreUnavailable; }
        return RedirectToAction(nameof(SystemAccounts), new { id });
    }

    // Called by ta-portal.js ("Hiện" / "Sao chép"). Returns the plaintext once, as JSON, never cached (global no-store).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionPolicies.Prefix + SecretRevealPermission)]
    public async Task<IActionResult> RevealSecret(Guid id, [FromForm] Guid accountId, [FromForm] string? field, [FromServices] ICustomerProfileStore profiles, [FromServices] ICustomerSecretProtector secrets)
    {
        if (ActorOrNull() is not Guid actor) return Forbid();
        if (!Enum.TryParse<SecretField>(field, ignoreCase: false, out var which) || !Enum.IsDefined(which)) return BadRequest();
        if (await customers.GetAsync(id) is null) return NotFound();
        SecretRevealResult result;
        try { result = await profiles.RevealSecretAsync(id, accountId, which, actor, HttpContext.Connection.RemoteIpAddress?.ToString()); }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Customer secret could not be read for customer {CustomerId}.", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ProfileStoreUnavailable });
        }
        switch (result.Outcome)
        {
            case ProfileChangeOutcome.NotFound: return NotFound(new { error = "Tài khoản không tồn tại hoặc chưa lưu thông tin này." });
            case ProfileChangeOutcome.AuditUnavailable: return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Không ghi được nhật ký audit (dbo.AuditLogs) nên không hiển thị mật khẩu." });
        }
        logger.LogInformation("Customer secret revealed. Actor {ActorId}, customer {CustomerId}, account {AccountId}, field {Field}.", actor, id, accountId, which);
        if (secrets.Unprotect(result.Cipher!, CustomerSecretContext.For(id, accountId, which)) is not { } plaintext)
        {
            logger.LogError("CUSTOMER_SECRET_UNREADABLE customer={CustomerId} account={AccountId}: ciphertext cannot be decrypted with the configured CustomerSecrets key.", id, accountId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Không giải mã được: khóa mã hóa trên máy chủ (CustomerSecrets__Key) không khớp với khóa đã dùng khi lưu. Kiểm tra lại khóa hoặc nhập lại mật khẩu." });
        }
        return Json(new { value = plaintext });
    }

    private async Task<ViewResult> SystemAccountsView(CustomerEditVm customer, IReadOnlyList<SystemAccountRow>? rows, SystemAccountInput? form, ICustomerSecretProtector secrets, IAuthorizationService authorization)
    {
        ViewData["Workspace"] = CustomerWorkspaceHeader.From(customer, CustomerWorkspace.SystemAccounts);
        var canReveal = (await authorization.AuthorizeAsync(User, PermissionPolicies.Prefix + SecretRevealPermission)).Succeeded;
        return View(nameof(SystemAccounts), new CustomerSystemAccountsVm(customer, rows, form, secrets.Enabled, canReveal));
    }

    private void Notify(ProfileChangeOutcome outcome, string success)
    {
        if (outcome == ProfileChangeOutcome.Saved) TempData["Success"] = success;
        else TempData["Error"] = outcome == ProfileChangeOutcome.AuditUnavailable ? AuditUnavailableMessage : DeleteConflictMessage;
    }

    private const string DeleteConflictMessage = "Dữ liệu vừa được người khác thay đổi nên chưa xóa. Danh sách đã được tải lại, vui lòng kiểm tra rồi thử lại.";

    private const string ConflictMessage = "Dữ liệu vừa được người khác thay đổi. Form đã được tải lại theo dữ liệu mới nhất, thay đổi của bạn chưa được lưu: vui lòng nhập lại và lưu.";

    internal const string SecretsDisabledMessage = "Chưa thể lưu mật khẩu/PIN: máy chủ chưa cấu hình khóa mã hóa (CustomerSecrets__Key) hoặc khóa sai định dạng. Liên hệ quản trị hệ thống.";
}
