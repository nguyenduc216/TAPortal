using Microsoft.AspNetCore.Mvc;
using TACustomerPortal.Web.Models;
using TACustomerPortal.Web.Security;
using TACustomerPortal.Web.Services;
using TAPortal.Shared;

namespace TACustomerPortal.Web.Controllers;

// "Tài khoản & phân quyền" (operator decision 2026-11-07): the company manages its own staff accounts and decides, per
// function, whether each account may view (Xem) or manage (Quản lý) it. New accounts are MEMBER. The OWNER is shown but
// never changed here; nobody changes their own rights or account; a staff manager can neither grant nor change more than
// it holds itself. Everything is scoped to the signed-in company and audited by the store.
[CustomerModule(CustomerModules.Members)]
public sealed class MembersController(ICurrentCustomerContext customer, ICustomerMemberStore members, PasswordService passwords) : CustomerAreaController(customer)
{
    private const string Module = CustomerModules.Members;

    public async Task<IActionResult> Index()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return View(new MembersVm(m, CanManage(m, Module), Actor, await members.ListAsync(m.CustomerId)));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (await Current() is not { } m) return SelectCustomer();
        return CanManage(m, Module) ? View(new MemberCreateVm(m, new MemberInput())) : Denied(Module);
    }

    [HttpPost]
    public async Task<IActionResult> Create(MemberInput input)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m, Module)) return Denied(Module);
        input.Username = input.Username?.Trim() ?? "";
        input.DisplayName = input.DisplayName?.Trim() ?? "";
        input.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        input.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        var (passwordOk, passwordError) = PasswordPolicy.Validate(input.Password);
        if (!passwordOk) ModelState.AddModelError(nameof(input.Password), passwordError);
        var levels = CustomerAccess.FromForm(input.View, input.Manage);
        if (!CustomerAccess.Covers(m, levels)) ModelState.AddModelError("", "Bạn không thể cấp quyền cao hơn quyền của chính mình.");
        if (ModelState.IsValid)
        {
            var (outcome, _) = await members.CreateAsync(m.CustomerId,
                new(input.Username, input.DisplayName, input.Email, input.Phone, passwords.Hash(input.Password), CustomerAccess.Format(levels)), Actor, Ip);
            switch (outcome)
            {
                case MemberOutcome.Done:
                    Flash($"Đã tạo tài khoản {input.Username}. Người dùng đăng nhập bằng mật khẩu bạn đặt và đổi mật khẩu ở lần đầu.");
                    return RedirectToAction(nameof(Index));
                case MemberOutcome.UsernameTaken: ModelState.AddModelError(nameof(input.Username), "Tên đăng nhập này đã được dùng, vui lòng chọn tên khác."); break;
                case MemberOutcome.EmailTaken: ModelState.AddModelError(nameof(input.Email), "Email này đã được dùng cho một tài khoản khác."); break;
                default: ModelState.AddModelError("", "Chưa tạo được tài khoản, vui lòng thử lại sau."); break;
            }
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        input.Password = input.ConfirmPassword = "";
        return View(new MemberCreateVm(m, input));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (await members.GetAsync(m.CustomerId, id) is not { } target) return NotFound();
        var refusal = Refusal(m, target);
        return View(new MemberDetailsVm(m, target, refusal is null, refusal, CustomerAccess.Levels(target.Role, target.Permissions),
            refusal is null && !target.SharedWithOtherCompanies));
    }

    [HttpPost]
    public async Task<IActionResult> Permissions(Guid id, string[]? view, string[]? manage)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m, Module)) return Denied(Module);
        if (await Guarded(m, id) is { } refused) return refused;
        var levels = CustomerAccess.FromForm(view, manage);
        if (!CustomerAccess.Covers(m, levels))
        {
            Flash("Bạn không thể cấp quyền cao hơn quyền của chính mình.", error: true);
            return RedirectToAction(nameof(Edit), new { id });
        }
        Report(await members.SetPermissionsAsync(m.CustomerId, id, CustomerAccess.Format(levels), Actor, Ip), "Đã lưu phân quyền. Thay đổi có hiệu lực ngay ở lần tải trang tiếp theo của người dùng.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(Guid id, [FromForm] bool active)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m, Module)) return Denied(Module);
        if (await Guarded(m, id) is { } refused) return refused;
        Report(await members.SetActiveAsync(m.CustomerId, id, active, Actor, Ip),
            active ? "Đã kích hoạt lại tài khoản trong doanh nghiệp." : "Đã ngừng tài khoản trong doanh nghiệp. Người dùng không còn truy cập được doanh nghiệp này.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Password(Guid id, string? newPassword, string? confirmPassword)
    {
        if (await Current() is not { } m) return SelectCustomer();
        if (!CanManage(m, Module)) return Denied(Module);
        if (await Guarded(m, id) is { } refused) return refused;
        var (ok, error) = PasswordPolicy.Validate(newPassword);
        if (!ok || newPassword != confirmPassword)
        {
            Flash(ok ? "Mật khẩu xác nhận không khớp." : error, error: true);
            return RedirectToAction(nameof(Edit), new { id });
        }
        Report(await members.SetPasswordAsync(m.CustomerId, id, passwords.Hash(newPassword!), Actor, Ip),
            "Đã đặt mật khẩu mới. Người dùng đăng nhập lại bằng mật khẩu này và đổi mật khẩu ở lần đầu.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Why this account cannot be changed by the signed-in member, or null when it can.</summary>
    private string? Refusal(CustomerMembership m, CompanyMember target)
    {
        if (!CanManage(m, Module)) return "Bạn chỉ có quyền xem danh sách tài khoản.";
        if (target.Role == CustomerAccess.Owner) return "Tài khoản chủ sở hữu không thể thay đổi tại đây.";
        if (target.UserId == Actor) return "Bạn không thể tự thay đổi quyền hoặc tài khoản của chính mình.";
        if (!CustomerAccess.Covers(m, CustomerAccess.Levels(target.Role, target.Permissions))) return "Tài khoản này có quyền cao hơn quyền của bạn.";
        return null;
    }

    private async Task<IActionResult?> Guarded(CustomerMembership m, Guid id)
    {
        if (await members.GetAsync(m.CustomerId, id) is not { } target) return NotFound();
        if (Refusal(m, target) is not { } refusal) return null;
        Flash(refusal, error: true);
        return RedirectToAction(nameof(Index));
    }

    private void Report(MemberOutcome outcome, string success) => Flash(outcome switch
    {
        MemberOutcome.Done => success,
        MemberOutcome.NotFound => "Không tìm thấy tài khoản trong doanh nghiệp.",
        MemberOutcome.Protected => "Tài khoản chủ sở hữu không thể thay đổi tại đây.",
        MemberOutcome.SharedAccount => "Tài khoản này còn dùng cho doanh nghiệp khác nên không đặt lại mật khẩu tại đây được. Vui lòng liên hệ T.A.",
        _ => "Chưa lưu được thay đổi, vui lòng thử lại sau."
    }, error: outcome != MemberOutcome.Done);
}
