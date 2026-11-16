using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;

namespace TAPortal.Web.Controllers;

// Hợp đồng dịch vụ → Kho văn bản pháp luật. Viewing goes with CONTRACTS.REVIEW.VIEW; changes need LEGAL.LIBRARY.MANAGE.
// A law is entered by uploading its Word / PDF / text file (read on the server, no AI) or by pasting the text; every
// uploaded file is kept, the one in use is marked current, and an older one can be re-imported. Articles can be edited.
[Authorize(Policy = AdminPermissions.Policy.ContractReviewView)]
public sealed class LegalLibraryController(ILegalLibraryStore library, ILogger<LegalLibraryController> logger) : Controller
{
    private const long UploadLimit = LegalFileText.MaxBytes + 4 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        try { return View(new LegalLibraryIndexVm(await library.ListAsync(), null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "LEGAL_LIBRARY_UNAVAILABLE");
            return View(new LegalLibraryIndexVm(null, ContractReviewsController.StoreUnavailable));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var doc = await library.GetAsync(id);
        return doc is null ? NotFound() : View(doc);
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public IActionResult Create([FromQuery(Name = "Input")] LegalDocumentInput? input)
    {
        // "Nhập tay" from the review queue (LegalWatch) pre-fills the form from the query string; nothing is saved here.
        ModelState.Clear();
        return View("Edit", new LegalDocumentEditVm(null, input ?? new LegalDocumentInput(), 0));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit, ValueLengthLimit = 16 * 1024 * 1024)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] LegalDocumentInput input, IFormFile? file) => Save(null, input, file);

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var doc = await library.GetAsync(id);
        return doc is null ? NotFound() : View(new LegalDocumentEditVm(id, LegalDocumentInput.From(doc.Document), doc.Articles.Count));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit, ValueLengthLimit = 16 * 1024 * 1024)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] LegalDocumentInput input, IFormFile? file) => Save(id, input, file);

    // "Cập nhật bằng file mới" on the details page: replaces the articles with the new file; the old file stays in history.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? file)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var doc = await library.GetAsync(id);
        if (doc is null) return NotFound();
        var (upload, error) = await ReadFile(file);
        if (upload is null) return Back(id, error!);
        var (outcome, _, summary) = await library.SaveAsync(actor, id, LegalDocumentInput.From(doc.Document), upload, Ip);
        return Result(id, outcome, $"Đã cập nhật từ file {upload.FileName}: {summary}.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Reimport(Guid id, [FromForm] Guid fileId)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var (outcome, summary) = await library.ReimportAsync(actor, id, fileId, Ip);
        return Result(id, outcome, $"Đã dùng lại file đã chọn: {summary}.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> UpdateArticle(Guid id, [FromForm] long articleId, [FromForm] string? title, [FromForm] string? content)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var outcome = await library.UpdateArticleAsync(actor, id, articleId, title, content, Ip);
        return Result(id, outcome, "Đã lưu nội dung điều.", "a-" + articleId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> DeleteArticle(Guid id, [FromForm] long articleId)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        return Result(id, await library.DeleteArticleAsync(actor, id, articleId, Ip), "Đã xóa điều khỏi kho.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.LegalLibraryManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var outcome = await library.DeleteAsync(actor, id, Ip);
        if (outcome == LegalOutcome.Forbidden) return Forbid();
        if (outcome == LegalOutcome.NotFound) return NotFound();
        TempData[outcome == LegalOutcome.Saved ? "Success" : "Error"] = outcome == LegalOutcome.Saved ? "Đã xóa văn bản khỏi kho." : Message(outcome);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(Guid id, Guid fileId)
    {
        var file = await library.DownloadAsync(id, fileId);
        if (file is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Data, file.ContentType, ContractPolicy.SafeFileName(file.FileName));
    }

    private async Task<IActionResult> Save(Guid? id, LegalDocumentInput input, IFormFile? file)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        ModelState.Clear();
        LegalUpload? upload = null;
        var hasFile = file is { Length: > 0 };
        if (hasFile)
        {
            var (read, error) = await ReadFile(file);
            if (read is null) ModelState.AddModelError("file", error!);
            upload = read;
        }
        foreach (var (key, message) in input.Validate(id is null, hasFile)) ModelState.AddModelError(key, message);
        if (ModelState.IsValid)
        {
            var (outcome, savedId, summary) = await library.SaveAsync(actor, id, input, upload, Ip);
            switch (outcome)
            {
                case LegalOutcome.Forbidden: return Forbid();
                case LegalOutcome.NotFound: return NotFound();
                case LegalOutcome.Saved:
                    TempData["Success"] = summary is null ? "Đã lưu thông tin văn bản." : $"Đã lưu văn bản và {summary}.";
                    return RedirectToAction(nameof(Details), new { id = savedId });
                case LegalOutcome.Duplicate: ModelState.AddModelError("Input.DocumentNo", "Số hiệu văn bản đã có trong kho."); break;
                default: ModelState.AddModelError(string.Empty, Message(outcome)); break;
            }
        }
        var count = id is Guid g ? (await library.GetAsync(g))?.Articles.Count ?? 0 : 0;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new LegalDocumentEditVm(id, input, count));
    }

    private async Task<(LegalUpload? Upload, string? Error)> ReadFile(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, "Chọn file văn bản.");
        if (file.Length > LegalFileText.MaxBytes) return (null, "File vượt quá 20 MB.");
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
        return LegalFileText.Read(file.FileName, buffer.ToArray());
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult Result(Guid id, LegalOutcome outcome, string success, string? anchor = null)
    {
        if (outcome == LegalOutcome.Forbidden) return Forbid();
        if (outcome == LegalOutcome.NotFound) return NotFound();
        TempData[outcome == LegalOutcome.Saved ? "Success" : "Error"] = outcome == LegalOutcome.Saved ? success : Message(outcome);
        var url = Url.Action(nameof(Details), new { id })!;
        return Redirect(anchor is null ? url : url + "#" + anchor);
    }

    private RedirectToActionResult Back(Guid id, string error)
    {
        TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string Message(LegalOutcome outcome) => outcome switch
    {
        LegalOutcome.Invalid => "Dữ liệu không hợp lệ (nội dung điều trống, hoặc file không còn đọc được).",
        LegalOutcome.Duplicate => "Số hiệu văn bản đã có trong kho.",
        LegalOutcome.AuditUnavailable => "Không ghi được nhật ký audit nên thay đổi đã bị hủy.",
        _ => "Không thực hiện được thao tác."
    };
}
