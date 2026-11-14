using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Quản lý khách hàng → Hợp đồng dịch vụ: contract revision requests ("yêu cầu chỉnh lý hợp đồng"). Viewing needs
// CONTRACTS.REVIEW.VIEW; every change declares its own CONTRACTS.REVIEW.* permission, re-checked in SQL by the store.
[Authorize(Policy = AdminPermissions.Policy.ContractReviewView)]
public sealed class ContractReviewsController(IContractReviewStore reviews, IContractReviewAi ai, ILogger<ContractReviewsController> logger) : Controller
{
    private const long UploadLimit = ContractReviewPolicy.MaxFileBytes + 1024 * 1024;
    internal const string StoreUnavailable = "Không đọc được dữ liệu Hợp đồng dịch vụ (migration 044 đã chạy chưa?). Chi tiết đã ghi vào nhật ký ứng dụng.";

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReviewFilter filter)
    {
        try { return View(new ContractReviewsIndexVm(await reviews.ListAsync(filter), filter, await reviews.LookupsAsync(), null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "CONTRACT_REVIEWS_UNAVAILABLE");
            return View(new ContractReviewsIndexVm(null, filter, ReviewLookups.Empty, StoreUnavailable));
        }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewCreate)]
    public async Task<IActionResult> Create(Guid? customerId)
    {
        var lookups = await reviews.LookupsAsync();
        return View(new ContractReviewCreateVm(new ReviewInput { CustomerId = lookups.Customers.Any(c => c.Id == customerId) ? customerId : null }, lookups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewCreate)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ReviewInput input, IFormFile? file)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ModelState.Clear();
        foreach (var (key, message) in input.Validate()) ModelState.AddModelError(key, message);
        ReviewUpload? upload = null;
        if (file is null || file.Length == 0) ModelState.AddModelError("file", "Chọn file hợp đồng (DOCX).");
        else if (file.Length > ContractReviewPolicy.MaxFileBytes) ModelState.AddModelError("file", "File vượt quá 10 MB.");
        else
        {
            var data = await Read(file);
            var (ok, error, paragraphs) = ContractReviewPolicy.CheckDocx(file.FileName, data);
            if (ok is null) ModelState.AddModelError("file", error ?? "File không hợp lệ.");
            else upload = new(file.FileName, ok, paragraphs);
        }
        if (ModelState.IsValid && upload is not null)
        {
            var result = await reviews.CreateAsync(actor, input, upload, Ip);
            if (result.Outcome == ReviewOutcome.Forbidden) return Forbid();
            if (result.Outcome == ReviewOutcome.Saved)
            {
                logger.LogInformation("Contract review created. Actor {ActorId}, review {ReviewId}, {Paragraphs} paragraphs.", actor, result.Id, upload.Paragraphs.Count);
                TempData["Success"] = $"Đã tạo yêu cầu chỉnh lý và đọc được {upload.Paragraphs.Count} đoạn từ file hợp đồng.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }
            ModelState.AddModelError(string.Empty, Message(result));
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(new ContractReviewCreateVm(input, await reviews.LookupsAsync()));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, int? v, string? mode)
    {
        var review = await reviews.GetAsync(id);
        if (review is null) return NotFound();
        var no = v is int x && review.Versions.Any(p => p.No == x) ? x : review.CurrentVersionNo;
        var paragraphs = await reviews.ParagraphsAsync(id, no);
        var previous = no > 0 ? await reviews.ParagraphsAsync(id, no - 1) : null;
        var findings = (await reviews.FindingsAsync(id)).Where(f => f.VersionNo == no).ToList();
        var changes = (await reviews.ChangesAsync(id)).Where(c => c.ToVersionNo == no).ToList();
        var view = mode is "changes" or "findings" ? mode : findings.Count > 0 || no == 0 ? "findings" : "changes";
        return View(new ContractReviewWorkspaceVm(review, no, view, paragraphs, previous, findings, changes, ai.IsConfigured, ai.Model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewRunAi)]
    public async Task<IActionResult> RunAi(Guid id, [FromForm] bool mask = true)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (!ai.IsConfigured) return Back(id, Message(new(ReviewOutcome.NotConfigured)));
        var result = await reviews.QueueRunAsync(actor, id, mask, Ip);
        return Done(id, result, "Đã gửi hợp đồng cho AI rà soát. Thường mất 1–5 phút; bấm \"Tải lại\" để xem kết quả.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> AddFinding(Guid id, [Bind(Prefix = "Finding")] ReviewFindingInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Done(id, await reviews.AddFindingAsync(actor, id, input, Ip), "Đã thêm góp ý.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> Decide(Guid id, [FromForm] Guid findingId, [FromForm] string? status, [FromForm] string? staffText, [FromForm] string? staffNote)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await reviews.DecideFindingAsync(actor, id, findingId, status ?? "", staffText, staffNote, Ip);
        return Done(id, result, "Đã lưu quyết định cho góp ý.", "f-" + findingId);
    }

    // "✎ Sửa trực tiếp" on a paragraph: recorded as a staff finding (whole paragraph rewritten) that is accepted at once, so
    // the current version stays unchanged and the edit goes into the next version like any other accepted finding.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> EditParagraph(Guid id, [FromForm] string? paragraphKey, [FromForm] string? text, [FromForm] string? note)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var input = new ReviewFindingInput
        {
            ParagraphKey = paragraphKey, Action = ContractReviewPolicy.Replace, Issue = string.IsNullOrWhiteSpace(note) ? "Nhân viên sửa trực tiếp đoạn này." : note,
            Severity = "LOW", Party = "BOTH", RevisedText = text
        };
        var added = await reviews.AddFindingAsync(actor, id, input, Ip);
        if (added.Outcome != ReviewOutcome.Saved || added.Id is not Guid findingId) return Done(id, added, "");
        var result = await reviews.DecideFindingAsync(actor, id, findingId, ContractReviewPolicy.Accepted, null, null, Ip);
        return Done(id, result, $"Đã ghi thay đổi đoạn {paragraphKey}; thay đổi sẽ vào phiên bản mới khi bấm \"Tạo phiên bản mới\".", "p-" + paragraphKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> CreateVersion(Guid id, [FromForm] string? note)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await reviews.CreateVersionAsync(actor, id, note, Ip);
        return Done(id, result, $"Đã tạo phiên bản {result.VersionNo} từ các góp ý được chấp nhận. Bản gốc và các bản trước được giữ nguyên.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> MarkSent(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Done(id, await reviews.MarkSentAsync(actor, id, Ip), "Đã ghi nhận gửi bản sửa cho khách hàng.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> CustomerAnswer(Guid id, [FromForm] Guid changeId, [FromForm] string? decision, [FromForm] string? customerText, [FromForm] string? customerNote)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await reviews.RecordCustomerAsync(actor, id, changeId, decision ?? "", customerText, customerNote, Ip);
        return Done(id, result, "Đã ghi nhận ý kiến của khách hàng.", "c-" + changeId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewEdit)]
    public async Task<IActionResult> CustomerVersion(Guid id, [FromForm] string? note)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await reviews.CreateCustomerVersionAsync(actor, id, note, Ip);
        return Done(id, result, $"Đã tạo phiên bản {result.VersionNo} theo phản hồi của khách hàng.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewFinalize)]
    public async Task<IActionResult> Finalize(Guid id, [FromForm] int versionNo, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await reviews.FinalizeAsync(actor, id, versionNo, stamp, Ip);
        return Done(id, result, $"Đã chốt phiên bản {versionNo} là bản cuối; file Word bản cuối đã được lưu.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.ContractReviewFinalize)]
    public async Task<IActionResult> Cancel(Guid id, [FromForm] string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Done(id, await reviews.CancelAsync(actor, id, stamp, Ip), "Đã hủy yêu cầu chỉnh lý.");
    }

    // Polled every 10 seconds by ta-review.js while an AI run is queued or running (no-store; small JSON).
    [HttpGet]
    public async Task<IActionResult> RunStatus(Guid id)
    {
        var review = await reviews.GetAsync(id);
        if (review is null) return NotFound();
        var run = review.Runs.FirstOrDefault();
        Response.Headers.CacheControl = "no-store";
        return Json(new
        {
            reviewStatus = review.Status,
            runStatus = run?.Status,
            active = run?.Status is "QUEUED" or "RUNNING",
            findings = run?.FindingCount,
            error = run?.Status == "FAILED" ? run.Error : null,
            since = Utc(run?.StartedAt ?? run?.RequestedAt)
        });
    }

    // Word file of a version: clean, or with real Word track changes and comments against the version before.
    [HttpGet]
    public async Task<IActionResult> Export(Guid id, int v, bool tracked = false)
    {
        var review = await reviews.GetAsync(id);
        if (review is null || review.Versions.All(x => x.No != v)) return NotFound();
        var paragraphs = await reviews.ParagraphsAsync(id, v);
        IReadOnlyList<ReviewParagraph>? previous = null;
        Dictionary<string, string>? notes = null;
        if (tracked && v > 0)
        {
            previous = await reviews.ParagraphsAsync(id, v - 1);
            notes = (await reviews.ChangesAsync(id)).Where(c => c.ToVersionNo == v && !string.IsNullOrWhiteSpace(c.Note))
                .GroupBy(c => c.ParagraphKey).ToDictionary(g => g.Key, g => string.Join("\n", g.Select(c => c.Note!)));
        }
        var data = DocxWriter.Write(review.Title, paragraphs, previous, notes);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        var name = $"{review.Code} - {review.Title} - v{v}{(previous is null ? "" : " - theo doi thay doi")}.docx";
        return File(data, DocxWriter.ContentType, ContractPolicy.SafeFileName(name));
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, Guid fileId)
    {
        var file = await reviews.DownloadAsync(id, fileId);
        if (file is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Data, file.ContentType, ContractPolicy.SafeFileName(file.FileName));
    }

    // ---- helpers --------------------------------------------------------------------------------------------
    // Times are stored as UTC (SYSUTCDATETIME) and read back without a kind.
    public static string? Utc(DateTime? value) => value is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("o") : null;

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private async Task<byte[]> Read(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
        return buffer.ToArray();
    }

    private IActionResult Done(Guid id, ReviewResult result, string success, string? anchor = null)
    {
        switch (result.Outcome)
        {
            case ReviewOutcome.NotFound: return NotFound();
            case ReviewOutcome.Forbidden: return Forbid();
            case ReviewOutcome.Saved:
                TempData["Success"] = success + (result.AuditPersisted ? "" : " Lưu ý: chưa ghi được nhật ký audit vào cơ sở dữ liệu.");
                break;
            default:
                TempData["Error"] = Message(result);
                break;
        }
        var url = Url.Action(nameof(Details), new { id })!;
        return Redirect(anchor is null ? url : url + "#" + anchor);
    }

    private RedirectToActionResult Back(Guid id, string error)
    {
        TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    internal static string Message(ReviewResult result) => result.Errors is { Count: > 0 } errors ? string.Join(" ", errors) : result.Outcome switch
    {
        ReviewOutcome.Invalid => "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.",
        ReviewOutcome.Locked => "Không thực hiện được ở trạng thái hiện tại của yêu cầu (đang chạy AI, đã chốt hoặc đã hủy, hoặc chưa đến bước này).",
        ReviewOutcome.AlreadyQueued => "AI đang rà soát yêu cầu này; chờ lượt hiện tại xong rồi chạy lại.",
        ReviewOutcome.NotConfigured => "Chưa cấu hình khóa Claude API trên máy chủ (ContractReview:Anthropic:ApiKey). Liên hệ quản trị hệ thống.",
        ReviewOutcome.AuditUnavailable => "Không thể ghi nhật ký audit (dbo.AuditLogs) nên thay đổi đã bị hủy. Liên hệ quản trị cơ sở dữ liệu.",
        ReviewOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        ReviewOutcome.NotFound => "Không tìm thấy yêu cầu chỉnh lý.",
        _ => "Yêu cầu vừa được thay đổi bởi thao tác khác. Đã tải lại dữ liệu mới nhất, vui lòng thử lại."
    };
}
