using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Hợp đồng dịch vụ → Kho văn bản pháp luật → Văn bản mới cần duyệt (LEGAL-WATCH, operator 2026-11-14). Viewing goes with
// CONTRACTS.REVIEW.VIEW like the library; every decision needs LEGAL.LIBRARY.MANAGE. A new document enters the library
// only through "Nạp vào kho" here (its Word file from Công báo, split into articles like an upload) or the library form.
[Authorize(Policy = AdminPermissions.Policy.ContractReviewView)]
public sealed class LegalWatchController(ILegalWatchStore watch, ILegalLibraryStore library, LegalWatchService service, LegalWatchTrigger trigger,
    ILogger<LegalWatchController> logger) : Controller
{
    internal static readonly TimeSpan ManualGap = TimeSpan.FromMinutes(10);

    [HttpGet]
    public async Task<IActionResult> Index(string? view)
    {
        var pending = view != "done";
        try { return View(new LegalWatchIndexVm(await watch.OverviewAsync(pending), pending, null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LEGAL_WATCH_UNAVAILABLE");
            return View(new LegalWatchIndexVm(null, pending, "Chưa đọc được hàng chờ (cơ sở dữ liệu chưa sẵn sàng hoặc migration 048 chưa chạy)."));
        }
    }

    // "Nạp vào kho": a NEW entry whose number is already in the library is only marked; otherwise its Word file is read and
    // saved through the library store (same checks, audit and article split as an upload).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Import(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var item = await watch.GetAsync(id);
        if (item is null || item.Kind != "NEW" || !item.IsPending) return NotFound();
        if (item.LibraryId is Guid existing)
            return Done(await watch.DecideAsync(actor, id, "IMPORTED", existing, "Đã có trong kho", Ip), $"Đã đánh dấu {item.DocumentNo}: văn bản đã có trong kho.");
        var (upload, error) = await service.FileAsync(item, HttpContext.RequestAborted);
        if (upload is null)
        {
            TempData["Error"] = $"{item.DocumentNo}: {error} Bấm \"Nhập tay\" để mở form đã điền sẵn và tải file khác.";
            return RedirectToAction(nameof(Index));
        }
        var (outcome, docId, summary) = await library.SaveAsync(actor, null, item.Input(), upload, Ip);
        if (outcome == LegalOutcome.Forbidden) return Forbid();
        if (outcome != LegalOutcome.Saved || docId is not Guid doc)
        {
            TempData["Error"] = outcome == LegalOutcome.Duplicate ? $"Số hiệu {item.DocumentNo} đã có trong kho." : "Không nạp được văn bản vào kho.";
            return RedirectToAction(nameof(Index));
        }
        var marked = await watch.DecideAsync(actor, id, "IMPORTED", doc, null, Ip);
        if (marked != LegalOutcome.Saved) logger.LogWarning("LEGAL_WATCH_MARK_FAILED {Outcome}", marked);
        TempData["Success"] = $"Đã nạp {item.DocumentNo} vào kho: {summary}. Kiểm tra lại ngày hiệu lực và các điều trước khi dùng.";
        return RedirectToAction(nameof(LegalLibraryController.Details), "LegalLibrary", new { id = doc });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Apply(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Done(await watch.ApplyStatusAsync(actor, id, Ip), "Đã cập nhật hiệu lực của văn bản trong kho.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Dismiss(Guid id, [FromForm] string? note)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Done(await watch.DecideAsync(actor, id, "DISMISSED", null, note, Ip), "Đã bỏ qua mục này.");
    }

    // "Kiểm tra ngay": reads Công báo and checks the library on vbpl.vn in the background (at most once per 10 minutes).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Check()
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var runs = (await watch.OverviewAsync(true)).Runs;
        if (runs.Any(r => r.Kind == "MANUAL" && (r.Status == "RUNNING" || r.StartedAt > DateTime.UtcNow - ManualGap)))
            TempData["Error"] = "Vừa có lượt kiểm tra trong 10 phút qua. Đợi lượt đó xong rồi tải lại trang.";
        else if (trigger.Request(actor))
            TempData["Success"] = "Đã bắt đầu kiểm tra Công báo và vbpl.vn. Việc này mất vài phút; tải lại trang để xem kết quả.";
        else
            TempData["Error"] = "Đang có một yêu cầu kiểm tra chờ chạy.";
        return RedirectToAction(nameof(Index));
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult Done(LegalOutcome outcome, string success)
    {
        if (outcome == LegalOutcome.Forbidden) return Forbid();
        if (outcome == LegalOutcome.NotFound) return NotFound();
        TempData[outcome == LegalOutcome.Saved ? "Success" : "Error"] = outcome switch
        {
            LegalOutcome.Saved => success,
            LegalOutcome.AuditUnavailable => "Không ghi được nhật ký audit nên thay đổi đã bị hủy.",
            _ => "Không thực hiện được thao tác."
        };
        return RedirectToAction(nameof(Index));
    }
}
