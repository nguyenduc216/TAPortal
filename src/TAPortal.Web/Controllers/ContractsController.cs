using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Admin → Quản lý khách hàng → Hợp đồng. Viewing (list, details, file download) needs CONTRACTS.VIEW; every change declares
// its own CONTRACTS.* permission and is re-checked in SQL by SqlContractStore. Files: PDF, PNG, JPG, DOCX, XLSX up to 10 MB.
[Authorize(Policy = AdminPermissions.Policy.ContractsView)]
public sealed class ContractsController(IContractStore contracts, ILogger<ContractsController> logger) : Controller
{
    internal const string StoreUnavailable = "Không đọc được dữ liệu hợp đồng. Chi tiết đã ghi vào nhật ký ứng dụng.";
    private const long UploadLimit = ContractPolicy.MaxFileBytes + 1024 * 1024;

    private static DateTime Today => DateTime.Today;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ContractFilter filter)
    {
        try { return View(new ContractsIndexVm(await contracts.ListAsync(filter, Today), filter, await contracts.LookupsAsync(), Today, null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "CONTRACTS_UNAVAILABLE");
            return View(new ContractsIndexVm(null, filter, ContractLookups.Empty, Today, StoreUnavailable));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var contract = await contracts.GetAsync(id);
        return contract is null ? NotFound() : View(new ContractDetailsVm(contract, Today));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.ContractsCreate)]
    public async Task<IActionResult> Create(Guid? customerId)
    {
        var lookups = await contracts.LookupsAsync();
        var input = new ContractInput { CustomerId = customerId is Guid c && lookups.Customers.Any(x => x.Id == c) ? c : null, SignedDate = Today };
        return View("Edit", new ContractEditVm(null, input, lookups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractsCreate)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ContractInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        Check(input);
        if (ModelState.IsValid)
        {
            var result = await contracts.CreateAsync(actor, input, Ip);
            if (result.Outcome == ContractOutcome.Forbidden) return Forbid();
            if (result.Outcome == ContractOutcome.Saved)
            {
                logger.LogInformation("Contract created. Actor {ActorId}, contract {ContractId}, audit persisted {AuditPersisted}.", actor, result.Id, result.AuditPersisted);
                TempData["Success"] = "Đã thêm hợp đồng." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            ModelState.AddModelError(result.Outcome == ContractOutcome.DuplicateNo ? "Input.ContractNo" : string.Empty, Message(result.Outcome));
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new ContractEditVm(null, input, await contracts.LookupsAsync()));
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.ContractsUpdate)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var contract = await contracts.GetAsync(id);
        if (contract is null) return NotFound();
        return View(new ContractEditVm(contract, ContractInput.From(contract), await contracts.LookupsAsync()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractsUpdate)]
    public async Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] ContractInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        Check(input);
        var reload = false;
        if (ModelState.IsValid)
        {
            var result = await contracts.UpdateAsync(actor, id, input, Ip);
            switch (result.Outcome)
            {
                case ContractOutcome.NotFound: return NotFound();
                case ContractOutcome.Forbidden: return Forbid();
                case ContractOutcome.NoChange:
                    TempData["Success"] = "Thông tin hợp đồng không thay đổi.";
                    return RedirectToAction(nameof(Details), new { id });
                case ContractOutcome.Saved:
                    logger.LogInformation("Contract updated. Actor {ActorId}, contract {ContractId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                    TempData["Success"] = "Đã lưu hợp đồng." + AuditNotice(result.AuditPersisted);
                    return RedirectToAction(nameof(Details), new { id });
                default:
                    ModelState.AddModelError(result.Outcome == ContractOutcome.DuplicateNo ? "Input.ContractNo" : string.Empty, Message(result.Outcome));
                    reload = result.Outcome == ContractOutcome.Conflict;
                    break;
            }
        }
        var contract = await contracts.GetAsync(id);
        if (contract is null) return NotFound();
        if (reload) { ModelState.Clear(); ModelState.AddModelError(string.Empty, Message(ContractOutcome.Conflict)); input = ContractInput.From(contract); }
        else input.Stamp = contract.Stamp;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new ContractEditVm(contract, input, await contracts.LookupsAsync()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractsDelete)]
    public async Task<IActionResult> Delete(Guid id, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var customerId = (await contracts.GetAsync(id))?.CustomerId;
        var result = await contracts.DeleteAsync(actor, id, stamp, Ip);
        switch (result.Outcome)
        {
            case ContractOutcome.NotFound: return NotFound();
            case ContractOutcome.Forbidden: return Forbid();
            case ContractOutcome.Saved:
                logger.LogInformation("Contract soft-deleted. Actor {ActorId}, contract {ContractId}, audit persisted {AuditPersisted}.", actor, id, result.AuditPersisted);
                TempData["Success"] = "Đã xóa (mềm) hợp đồng; file đính kèm được giữ lại trong cơ sở dữ liệu." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Index), new { customerId });
            default:
                return Back(id, Message(result.Outcome));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractsUpdate)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> UploadFile(Guid id, IFormFile? file)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (file is null || file.Length == 0) return Back(id, "Chọn file cần đính kèm.");
        if (file.Length > ContractPolicy.MaxFileBytes) return Back(id, "File vượt quá 10 MB.");
        byte[] data;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
            data = buffer.ToArray();
        }
        var (type, error) = ContractPolicy.CheckFile(file.FileName, data);
        if (type is null) return Back(id, error ?? "File không hợp lệ.");
        var result = await contracts.AddFileAsync(actor, id, new ContractUpload(file.FileName, type, data), Ip);
        switch (result.Outcome)
        {
            case ContractOutcome.NotFound: return NotFound();
            case ContractOutcome.Forbidden: return Forbid();
            case ContractOutcome.Saved:
                logger.LogInformation("Contract file added. Actor {ActorId}, contract {ContractId}, file {FileId}, {Size} bytes.", actor, id, result.Id, data.Length);
                TempData["Success"] = "Đã đính kèm file " + ContractPolicy.SafeFileName(file.FileName) + "." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Details), new { id });
            default:
                return Back(id, Message(result.Outcome));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractsUpdate)]
    public async Task<IActionResult> DeleteFile(Guid id, [FromForm] Guid fileId)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await contracts.DeleteFileAsync(actor, id, fileId, Ip);
        switch (result.Outcome)
        {
            case ContractOutcome.NotFound: return NotFound();
            case ContractOutcome.Forbidden: return Forbid();
            case ContractOutcome.Saved:
                logger.LogInformation("Contract file soft-deleted. Actor {ActorId}, contract {ContractId}, file {FileId}.", actor, id, fileId);
                TempData["Success"] = "Đã gỡ file đính kèm." + AuditNotice(result.AuditPersisted);
                return RedirectToAction(nameof(Details), new { id });
            default:
                return Back(id, Message(result.Outcome));
        }
    }

    // Always an attachment (never rendered inline) with the stored content type and nosniff.
    [HttpGet]
    public async Task<IActionResult> Download(Guid id, Guid fileId)
    {
        var file = await contracts.DownloadAsync(id, fileId);
        if (file is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Data, file.ContentType, ContractPolicy.SafeFileName(file.FileName));
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Normalizes the input and adds ContractPolicy errors; binding errors of number/date fields get a Vietnamese message.
    private void Check(ContractInput input)
    {
        foreach (var (key, message) in new[] { ("Input.ContractValue", "Giá trị hợp đồng phải là số nguyên không âm."), ("Input.SignedDate", "Ngày ký không hợp lệ."),
                     ("Input.StartDate", "Ngày bắt đầu không hợp lệ."), ("Input.EndDate", "Ngày kết thúc không hợp lệ."), ("Input.CustomerId", "Vui lòng chọn khách hàng."),
                     ("Input.OwnerEmployeeId", "Người phụ trách không hợp lệ.") })
        {
            if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0) { entry.Errors.Clear(); ModelState.AddModelError(key, message); }
        }
        input.Normalize();
        foreach (var (key, message) in ContractPolicy.Validate(input))
            if (!(ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0)) ModelState.AddModelError(key, message);
    }

    private RedirectToActionResult Back(Guid id, string error)
    {
        TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    internal static string Message(ContractOutcome outcome) => outcome switch
    {
        ContractOutcome.DuplicateNo => "Số hợp đồng đã tồn tại. Vui lòng nhập số khác.",
        ContractOutcome.InvalidReference => "Khách hàng hoặc người phụ trách không hợp lệ (khách hàng đã bị xóa hoặc nhân viên đã nghỉ).",
        ContractOutcome.Invalid => "Dữ liệu hợp đồng không hợp lệ. Vui lòng kiểm tra lại.",
        ContractOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        ContractOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        ContractOutcome.NotFound => "Không tìm thấy hợp đồng.",
        _ => "Hợp đồng vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng kiểm tra và lưu lại."
    };

    private static string AuditNotice(bool persisted) => persisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu (bảng AuditLogs chưa sẵn sàng).";
}
