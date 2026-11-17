using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using TAWebsite.Core.Content;

namespace TAPortal.Web.Controllers;

// Admin → Website TA: content of the public website tuvangiaiphapta.vn (project TAWebsite.Web). Pages and their sections are
// declared in code (TAWebsite.Core SiteMap, with the mockup content as defaults); editors change the content, order and
// visibility of sections, the SEO of a page and the general settings. Writes re-check the permission in SQL and are audited.
// Catalogue: WEBSITE.* (migration 035).
[Authorize(Policy = AdminPermissions.Policy.WebsiteView)]
public sealed class WebsiteController(IWebsiteAdminStore store, IConfiguration configuration, ILogger<WebsiteController> logger) : Controller
{
    public const string StoreUnavailable = "Chưa đọc được dữ liệu website (bảng của migration 035 chưa có hoặc thiếu quyền). Website vẫn hiển thị nội dung mặc định. Chi tiết đã ghi vào nhật ký ứng dụng.";

    private string PublicUrl => WebsiteUi.PublicUrl(configuration);

    public async Task<IActionResult> Index()
    {
        try { return View(new WebsiteIndexVm(await store.OverviewAsync(), await store.PagesAsync(), PublicUrl, null)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE");
            return View(new WebsiteIndexVm(null, SiteMap.Pages.Select(p => TAWebsite.Core.Data.PageComposer.Compose(p, null, [])).ToList(), PublicUrl, StoreUnavailable));
        }
    }

    public async Task<IActionResult> Page(string id)
    {
        if (SiteMap.Find(id) is null) return NotFound();
        try { return View(new WebsitePageVm(await store.PageAsync(id), PublicUrl)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); TempData["Error"] = StoreUnavailable; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteContentEdit)]
    public async Task<IActionResult> PageSeo(string id, string? seoTitle, string? seoDescription, bool published)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (seoTitle?.Length > 200 || seoDescription?.Length > 400) { TempData["Error"] = "Tiêu đề SEO tối đa 200 ký tự, mô tả tối đa 400 ký tự."; return RedirectToAction(nameof(Page), new { id }); }
        var result = await store.SavePageSeoAsync(actor, Ip, id, seoTitle, seoDescription, published || id == "home");
        Flash(result.Outcome, "Đã lưu SEO của trang.");
        return RedirectToAction(nameof(Page), new { id });
    }

    // keys: every section of the page in the new order; visible: the keys that stay shown.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteContentEdit)]
    public async Task<IActionResult> Layout(string id, string[] keys, string[] visible)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var page = SiteMap.Find(id);
        if (page is null) return NotFound();
        if (keys.Length != page.Sections.Count || keys.Distinct().Count() != keys.Length) { TempData["Error"] = "Danh sách section không khớp, mở lại trang rồi thử lại."; return RedirectToAction(nameof(Page), new { id }); }
        var layout = keys.Select((k, i) => (k, (i + 1) * 10, visible.Contains(k))).ToList();
        Flash((await store.SaveLayoutAsync(actor, Ip, id, layout)).Outcome, "Đã lưu thứ tự và hiển thị các section.");
        return RedirectToAction(nameof(Page), new { id });
    }

    public async Task<IActionResult> Section(string page, string key)
    {
        var def = SiteMap.Find(page);
        var section = def?.Section(key);
        if (def is null || section is null) return NotFound();
        try
        {
            var editor = await store.PageAsync(page);
            var rendered = editor.Page.Sections.First(s => s.Def.Key == key);
            editor.Rows.TryGetValue(key, out var row);
            return View(new WebsiteSectionVm(def, section, rendered.Content, Stamp(row?.Stamp), rendered.IsEdited, row?.UpdatedAt, [], PublicUrl));
        }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); TempData["Error"] = StoreUnavailable; return RedirectToAction(nameof(Index)); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteContentEdit)]
    [RequestFormLimits(ValueCountLimit = 4000)]
    public async Task<IActionResult> Section(string page, string key, string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var def = SiteMap.Find(page);
        var section = def?.Section(key);
        if (def is null || section is null) return NotFound();
        var (content, errors) = WebsiteSectionForm.Read(section.SectionType, Request.Form);
        if (errors.Count > 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(new WebsiteSectionVm(def, section, new SectionContent(content), stamp, true, null, errors, PublicUrl));
        }
        var result = await store.SaveSectionAsync(actor, Ip, page, key, content, stamp);
        if (result.Outcome != WebsiteOutcome.Saved)
        {
            Response.StatusCode = result.Outcome == WebsiteOutcome.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return View(new WebsiteSectionVm(def, section, new SectionContent(content), stamp, true, null, [WebsiteUi.Message(result.Outcome)], PublicUrl));
        }
        TempData["Success"] = $"Đã lưu \"{section.Name}\". Website cập nhật trong vòng 1 phút.";
        return RedirectToAction(nameof(Page), new { id = page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteContentEdit)]
    public async Task<IActionResult> ResetSection(string page, string key)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        Flash((await store.ResetSectionAsync(actor, Ip, page, key)).Outcome, "Đã khôi phục nội dung mặc định của section.");
        return RedirectToAction(nameof(Page), new { id = page });
    }

    public async Task<IActionResult> Settings()
    {
        try { return View(new WebsiteSettingsVm(await store.SettingsAsync(), null)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsiteSettingsVm(SiteSettings.Defaults, StoreUnavailable)); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteSettingsEdit)]
    public async Task<IActionResult> Settings(IFormCollection form)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var def in SiteSettingDefs.All)
        {
            var value = form[$"s.{def.Key}"].ToString().Trim();
            if (value.Length > 1000) { TempData["Error"] = $"{def.Label}: tối đa 1000 ký tự."; return RedirectToAction(nameof(Settings)); }
            if (def.Key.StartsWith("social.", StringComparison.Ordinal) && value.Length > 0 && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { TempData["Error"] = $"{def.Label}: liên kết phải bắt đầu bằng https://"; return RedirectToAction(nameof(Settings)); }
            values[def.Key] = value.Length == 0 || value == def.Default ? null : value;
        }
        Flash((await store.SaveSettingsAsync(actor, Ip, values)).Outcome, "Đã lưu cài đặt chung. Website cập nhật trong vòng 1 phút.");
        return RedirectToAction(nameof(Settings));
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    internal static string? Stamp(byte[]? stamp) => stamp is null ? null : Convert.ToBase64String(stamp);

    private void Flash(WebsiteOutcome outcome, string success)
    {
        if (outcome == WebsiteOutcome.Saved) TempData["Success"] = success;
        else TempData["Error"] = WebsiteUi.Message(outcome);
    }
}

// Admin → Website TA → Media: images, videos (MP4 upload or YouTube link) and PDF files. Everything lives in
// dbo.WebsiteMedia; the public site serves it at /media/{id} (a YouTube entry redirects to the video).
[Authorize(Policy = AdminPermissions.Policy.WebsiteView)]
public sealed class WebsiteMediaController(IWebsiteAdminStore store, ILogger<WebsiteMediaController> logger) : Controller
{
    private const long UploadLimit = TAWebsite.Core.MediaKinds.MaxVideoBytes + 1024 * 1024;

    public static TAWebsite.Core.MediaKind ParseKind(string? kind) => kind switch
    {
        "video" => TAWebsite.Core.MediaKind.Video,
        "file" => TAWebsite.Core.MediaKind.File,
        _ => TAWebsite.Core.MediaKind.Image
    };

    public static string KindKey(TAWebsite.Core.MediaKind kind) => kind switch
    {
        TAWebsite.Core.MediaKind.Video => "video",
        TAWebsite.Core.MediaKind.File => "file",
        _ => "image"
    };

    public async Task<IActionResult> Index(string? q, string? kind)
    {
        var k = ParseKind(kind);
        try { return View(new WebsiteMediaVm(await store.MediaAsync(q, 300, k), q, null, k)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsiteMediaVm([], q, WebsiteController.StoreUnavailable, k)); }
    }

    // Bytes for previews inside the Admin portal (thumbnails, picker, editor, video player, PDF viewer).
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> File(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var file = await store.MediaFileAsync(guid);
        if (file is null) return NotFound();
        if (file.IsLink) return TAWebsite.Core.MediaKinds.YouTubeId(file.ExternalUrl) is string yt ? Redirect(TAWebsite.Core.MediaKinds.YouTubeWatchUrl(yt)) : NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        if (file.ContentType == TAWebsite.Core.MediaKinds.Pdf)
            Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
        return File(file.Data, file.ContentType, enableRangeProcessing: file.ContentType == TAWebsite.Core.MediaKinds.Mp4);
    }

    /// <summary>Picker / upload JSON: <c>url</c> previews in the Admin, <c>publicUrl</c> is what an article links to.</summary>
    public static object PickerItem(Guid id, string name, string contentType, string? alt, int? width, int? height, int bytes, string? externalUrl)
    {
        var kind = TAWebsite.Core.MediaKinds.KindOf(contentType);
        var yt = contentType == TAWebsite.Core.MediaKinds.YouTube ? TAWebsite.Core.MediaKinds.YouTubeId(externalUrl) : null;
        var adminUrl = WebsiteUi.ImageUrl(id);
        return new
        {
            id = id.ToString("N"),
            value = ImageRef.MediaPrefix + id.ToString("N"),
            url = adminUrl,
            kind = KindKey(kind),
            publicUrl = yt is not null ? TAWebsite.Core.MediaKinds.YouTubeWatchUrl(yt) : "/media/" + id.ToString("N"),
            thumb = kind == TAWebsite.Core.MediaKind.Image ? adminUrl : yt is not null ? TAWebsite.Core.MediaKinds.YouTubeThumbUrl(yt) : null,
            size = bytes > 0 ? WebsiteUi.Size(bytes) : null,
            name,
            alt,
            w = width,
            h = height
        };
    }

    // JSON list for the pickers of the section and article editors (images by default).
    public async Task<IActionResult> Picker(string? q, string? kind)
    {
        try
        {
            var items = await store.MediaAsync(q, 120, ParseKind(kind));
            return Json(items.Select(m => PickerItem(m.Id, m.FileName, m.ContentType, m.AltText, m.Width, m.Height, m.ByteSize, m.ExternalUrl)));
        }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return StatusCode(503); }
    }

    /// <summary>Recognises an uploaded file by its bytes: image, PDF or MP4, with the size limit of its kind.</summary>
    public static (string? ContentType, int? Width, int? Height, string? Error) Inspect(byte[] data, TAWebsite.Core.MediaKind expected)
    {
        string? type = null;
        int? w = null, h = null;
        if (ImageProbe.Inspect(data) is { } img) { type = img.ContentType; w = img.Width; h = img.Height; }
        else type = TAWebsite.Core.MediaKinds.Sniff(data);
        var kindName = expected switch { TAWebsite.Core.MediaKind.Video => "video MP4", TAWebsite.Core.MediaKind.File => "tệp PDF", _ => "ảnh PNG, JPEG, WEBP hoặc GIF" };
        if (type is null || TAWebsite.Core.MediaKinds.KindOf(type) != expected || type == TAWebsite.Core.MediaKinds.YouTube)
            return (null, null, null, $"Chỉ nhận {kindName}.");
        if (data.Length > TAWebsite.Core.MediaKinds.MaxBytes(type))
            return (null, null, null, expected switch
            {
                TAWebsite.Core.MediaKind.Video => "Video MP4 tối đa 25 MB. Video dài hơn hãy đưa lên YouTube rồi dán link.",
                TAWebsite.Core.MediaKind.File => "Tệp PDF tối đa 10 MB.",
                _ => "Ảnh tối đa 5 MB. Hãy nén ảnh (vd. squoosh.app) rồi tải lại."
            });
        return (type, w, h, null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Upload(IFormFile? file, string? alt, string? kind)
    {
        var k = ParseKind(kind);
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        string? error = null;
        byte[] data = [];
        (string? ContentType, int? Width, int? Height, string? Error) probe = default;
        if (file is null || file.Length == 0) error = k switch { TAWebsite.Core.MediaKind.Video => "Chọn một video MP4.", TAWebsite.Core.MediaKind.File => "Chọn một tệp PDF.", _ => "Chọn một file ảnh." };
        else if (file.Length > UploadLimit) error = "Tệp quá lớn.";
        else
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            data = ms.ToArray();
            probe = Inspect(data, k);
            error = probe.Error;
        }
        if (error is not null)
        {
            if (wantsJson) return BadRequest(new { error });
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index), new { kind = KindKey(k) });
        }
        var name = Path.GetFileName(file!.FileName);
        var label = string.IsNullOrWhiteSpace(alt) ? Path.GetFileNameWithoutExtension(name).Replace('-', ' ').Replace('_', ' ') : alt;
        var result = await store.UploadMediaAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), name, probe.ContentType!, data, probe.Width, probe.Height, label);
        if (wantsJson)
            return result.Outcome == WebsiteOutcome.Saved && result.Id is Guid id
                ? Json(PickerItem(id, name, probe.ContentType!, label, probe.Width, probe.Height, data.Length, null))
                : BadRequest(new { error = WebsiteUi.Message(result.Outcome) });
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = k switch { TAWebsite.Core.MediaKind.Video => "Đã tải video lên.", TAWebsite.Core.MediaKind.File => "Đã tải tệp PDF lên.", _ => "Đã tải ảnh lên thư viện." };
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { kind = KindKey(k) });
    }

    // A YouTube video: only the link is stored; the article embeds it with youtube-nocookie.com.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    public async Task<IActionResult> AddVideoLink(string? url, string? title)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (TAWebsite.Core.MediaKinds.YouTubeId(url) is not string yt)
        {
            TempData["Error"] = "Link YouTube không hợp lệ. Dán link dạng https://www.youtube.com/watch?v=... hoặc https://youtu.be/...";
            return RedirectToAction(nameof(Index), new { kind = "video" });
        }
        var name = string.IsNullOrWhiteSpace(title) ? "YouTube " + yt : title.Trim();
        var result = await store.AddMediaLinkAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), name, TAWebsite.Core.MediaKinds.YouTubeWatchUrl(yt), name);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã thêm video YouTube.";
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { kind = "video" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    public async Task<IActionResult> Alt(Guid id, string? alt, string? q, string? kind)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.UpdateMediaAltAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id, alt);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã lưu mô tả."; else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { q, kind = KindKey(ParseKind(kind)) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    public async Task<IActionResult> Delete(Guid id, string? q, string? kind)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.DeleteMediaAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã xóa. Nơi nào đang dùng mục này sẽ không hiện nữa.";
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { q, kind = KindKey(ParseKind(kind)) });
    }
}

// Admin → Website TA → Bài viết: daily articles of /tin-tuc, their categories, draft / publish / schedule.
[Authorize(Policy = AdminPermissions.Policy.WebsiteView)]
public sealed class WebsitePostsController(IWebsiteAdminStore store, IConfiguration configuration, TimeProvider clock, ILogger<WebsitePostsController> logger) : Controller
{
    private string PublicUrl => WebsiteUi.PublicUrl(configuration);

    public async Task<IActionResult> Index(string? status, Guid? category, string? q)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try { return View(new WebsitePostsVm(await store.PostsAsync(status, category, q), await store.CategoriesAsync(), status, category, q, now, PublicUrl, null)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsitePostsVm([], [], status, category, q, now, PublicUrl, WebsiteController.StoreUnavailable)); }
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> Create() =>
        View("Edit", new WebsitePostEditVm(null, new WebsitePostInput { AuthorName = User.Identity?.Name }, null, await SafeCategories(), PublicUrl));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    [RequestFormLimits(ValueLengthLimit = 4 * 1024 * 1024)]
    public Task<IActionResult> Create([Bind(Prefix = "Input")] WebsitePostInput input) => Save(null, input);

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var post = await store.PostAsync(id);
        if (post is null) return NotFound();
        var input = new WebsitePostInput
        {
            Title = post.Title, Slug = post.Slug, Summary = post.Summary, BodyHtml = post.BodyHtml, CoverMediaId = post.CoverMediaId, CategoryId = post.CategoryId,
            AuthorName = post.AuthorName, Tags = post.Tags, Publish = post.Status == 1, IsFeatured = post.IsFeatured, SeoTitle = post.SeoTitle,
            SeoDescription = post.SeoDescription, Stamp = Convert.ToBase64String(post.Stamp),
            PublishedAtLocal = post.PublishedAt is DateTime p ? WebsiteUi.Local(p) : null
        };
        input.SetSource(post.Source);
        return View(new WebsitePostEditVm(id, input, post, await SafeCategories(), PublicUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    [RequestFormLimits(ValueLengthLimit = 4 * 1024 * 1024)]
    public Task<IActionResult> Edit(Guid id, [Bind(Prefix = "Input")] WebsitePostInput input) => Save(id, input);

    private async Task<IActionResult> Save(Guid? id, WebsitePostInput input)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        foreach (var (field, message) in input.Normalize()) ModelState.AddModelError($"Input.{field}", message);
        var current = id is Guid existing ? await store.PostAsync(existing) : null;
        if (id is not null && current is null) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await store.SavePostAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id, input, clock.GetUtcNow().UtcDateTime);
            if (result.Outcome == WebsiteOutcome.Saved)
            {
                TempData["Success"] = input.Publish ? "Đã lưu và xuất bản bài viết." : "Đã lưu bản nháp.";
                return RedirectToAction(nameof(Edit), new { id = result.Id });
            }
            ModelState.AddModelError(result.Outcome == WebsiteOutcome.Duplicate ? "Input.Slug" : "", WebsiteUi.Message(result.Outcome));
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("Edit", new WebsitePostEditVm(id, input, current, await SafeCategories(), PublicUrl));
    }

    // Draft preview with the website stylesheets; only for signed-in staff (the public site never shows drafts).
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id)
    {
        var post = await store.PostAsync(id);
        if (post is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.DeletePostAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã xóa bài viết khỏi website."; else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> SaveCategory(Guid? id, string? name, string? slug, int sortOrder)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.SaveCategoryAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id, name ?? "", slug ?? "", sortOrder);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã lưu chuyên mục.";
        else TempData["Error"] = result.Outcome == WebsiteOutcome.Invalid ? "Nhập tên chuyên mục (tối đa 150 ký tự)." : WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.DeleteCategoryAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã xóa chuyên mục; bài viết của chuyên mục chuyển sang 'Chưa phân loại'.";
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index));
    }

    // "Bài soạn sẵn": articles shipped with the Portal; staff add them as drafts, review, then publish.
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> Library()
    {
        try
        {
            var posts = await store.PostsAsync(null, null, null);
            var existing = posts.ToDictionary(p => p.Slug, p => p.Id, StringComparer.Ordinal);
            var drafts = posts.Where(p => p.Status == 0).Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);
            return View(new WebsitePostLibraryVm(PostLibrary.All, existing, null, drafts));
        }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsitePostLibraryVm(PostLibrary.All, new Dictionary<string, Guid>(), WebsiteController.StoreUnavailable)); }
    }

    // "Soạn bài bằng AI": staff give a topic or the link of an article to summarise; WebsiteAiWorker writes it in the background
    // (Claude API, 79AI fallback) and saves a draft. Which provider answered is only in the server usage log.
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public IActionResult AiCreate([FromServices] WebsiteAiOptions ai) => View(new WebsiteAiCreateVm(ai.IsConfigured, new WebsiteAiForm(), null));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public IActionResult AiCreate([FromServices] WebsiteAiOptions ai, [FromServices] WebsiteAiJobs jobs, [Bind(Prefix = "Form")] WebsiteAiForm form)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        if (!ai.IsConfigured) return View(new WebsiteAiCreateVm(false, form, "Chưa cấu hình khóa AI trên máy chủ."));
        var error = form.Validate();
        if (error is not null) { Response.StatusCode = StatusCodes.Status400BadRequest; return View(new WebsiteAiCreateVm(true, form, error)); }
        var job = jobs.Enqueue(actor, HttpContext.Connection.RemoteIpAddress?.ToString(),
            new WebsiteAiRequest(form.Kind, form.Topic?.Trim(), form.SourceUrl?.Trim(), form.Notes?.Trim(), form.Category));
        return RedirectToAction(nameof(AiStatus), new { id = job.Id });
    }

    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public IActionResult AiStatus([FromServices] WebsiteAiJobs jobs, Guid id)
    {
        if (jobs.Find(id) is not { } job || UsersController.ActorId(User) is not Guid actor || job.Actor != actor) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return View(job);
    }

    // Preview of a ready-written article as the website would show it, with its sources, before anything is saved.
    [HttpGet]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public IActionResult LibraryPreview(string? slug)
    {
        if (PostLibrary.Find(slug) is not { } post) return NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var model = new WebsitePostEdit(Guid.Empty, post.Slug, post.Title, post.Summary, post.BodyHtml, null, null, PostLibrary.Author, post.Tags, 0, null, false,
            post.SeoTitle, post.SeoDescription, 0, now, now, [], post.Origin);
        ViewData["LibraryPost"] = post;
        Response.Headers.CacheControl = "no-store";
        return View("Preview", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> ImportLibrary(string[]? slugs, bool publish = false)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var wanted = slugs is { Length: > 0 } ? new HashSet<string>(slugs, StringComparer.Ordinal) : null;
        var current = await store.PostsAsync(null, null, null);
        var existing = current.Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);
        var picks = PostLibrary.All.Where(p => (wanted is null || wanted.Contains(p.Slug)) && !existing.Contains(p.Slug)).ToList();
        var categories = (await store.CategoriesAsync()).ToDictionary(c => c.Slug, c => c.Id, StringComparer.Ordinal);
        int added = 0, published = 0, failed = 0;
        if (publish)
        {
            // Library articles already added as drafts are published as they are now (staff edits kept).
            var librarySlugs = PostLibrary.All.Where(p => wanted is null || wanted.Contains(p.Slug)).Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);
            foreach (var draft in current.Where(p => p.Status == 0 && librarySlugs.Contains(p.Slug)))
            {
                if (await store.PostAsync(draft.Id) is not { } post) { failed++; continue; }
                var edit = new WebsitePostInput
                {
                    Title = post.Title, Slug = post.Slug, Summary = post.Summary, BodyHtml = post.BodyHtml, CoverMediaId = post.CoverMediaId, CategoryId = post.CategoryId,
                    AuthorName = post.AuthorName, Tags = post.Tags, Publish = true, IsFeatured = post.IsFeatured, SeoTitle = post.SeoTitle,
                    SeoDescription = post.SeoDescription, Stamp = Convert.ToBase64String(post.Stamp)
                };
                edit.SetSource(post.Source);
                if (edit.Normalize().Count > 0) { failed++; continue; }
                var saved = await store.SavePostAsync(actor, ip, post.Id, edit, clock.GetUtcNow().UtcDateTime);
                if (saved.Outcome == WebsiteOutcome.Saved) published++; else failed++;
            }
        }
        foreach (var post in picks)
        {
            if (!categories.TryGetValue(post.Category, out var categoryId))
            {
                var order = PostLibrary.Categories.Select(c => c.Slug).ToList().IndexOf(post.Category) + 1;
                var created = await store.SaveCategoryAsync(actor, ip, null, PostLibrary.CategoryName(post.Category), post.Category, order);
                if (created is { Outcome: WebsiteOutcome.Saved, Id: Guid newId }) categories[post.Category] = categoryId = newId;
            }
            var input = new WebsitePostInput
            {
                Title = post.Title, Slug = post.Slug, Summary = post.Summary, BodyHtml = post.BodyHtml, Tags = post.Tags, AuthorName = PostLibrary.Author,
                SeoTitle = post.SeoTitle, SeoDescription = post.SeoDescription, CategoryId = categoryId == Guid.Empty ? null : categoryId, Publish = publish
            };
            input.SetSource(post.Origin);
            if (input.Normalize().Count > 0) { failed++; continue; }
            input.CoverMediaId = await UploadCoverAsync(actor, ip, post);
            var result = await store.SavePostAsync(actor, ip, null, input, clock.GetUtcNow().UtcDateTime);
            if (result.Outcome == WebsiteOutcome.Saved) added++; else failed++;
        }
        if (publish && added + published > 0) TempData["Success"] = $"Đã đăng {added + published} bài lên website.";
        else if (added > 0) TempData["Success"] = $"Đã thêm {added} bài dưới dạng bản nháp. Đọc lại từng bài rồi bấm Xuất bản.";
        if (failed > 0) TempData["Error"] = $"{failed} bài chưa xử lý được, thử lại sau.";
        if (added == 0 && published == 0 && failed == 0) TempData["Success"] = publish ? "Các bài soạn sẵn đều đã được đăng." : "Các bài soạn sẵn đã có trong danh sách bài viết.";
        return RedirectToAction(nameof(Library));
    }

    // "Gắn ảnh bìa và rải ngày đăng": library articles already on the site get their shipped cover photo (only when they have
    // none) and, for published ones, a publication time spread over the last 45 days (working days, office hours; a cited
    // summary never before its original). Saved through SavePostAsync (audited); staff edits to the articles are kept.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> ApplyLibraryExtras()
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var bySlug = PostLibrary.All.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        var onSite = (await store.PostsAsync(null, null, null)).Where(p => bySlug.ContainsKey(p.Slug)).ToList();
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var schedule = PostLibrary.Schedule(onSite.Where(p => p.Status == 1).Select(p => bySlug[p.Slug]).ToList(), WebsiteUi.Local(nowUtc));
        int covers = 0, dated = 0, failed = 0;
        foreach (var item in onSite)
        {
            if (await store.PostAsync(item.Id) is not { } post) { failed++; continue; }
            var input = new WebsitePostInput
            {
                Title = post.Title, Slug = post.Slug, Summary = post.Summary, BodyHtml = post.BodyHtml, CoverMediaId = post.CoverMediaId, CategoryId = post.CategoryId,
                AuthorName = post.AuthorName, Tags = post.Tags, Publish = post.Status == 1, IsFeatured = post.IsFeatured, SeoTitle = post.SeoTitle,
                SeoDescription = post.SeoDescription, Stamp = Convert.ToBase64String(post.Stamp),
                PublishedAtLocal = post.PublishedAt is DateTime p ? WebsiteUi.Local(p) : null
            };
            input.SetSource(post.Source);
            var changed = false;
            if (input.CoverMediaId is null && await UploadCoverAsync(actor, ip, bySlug[post.Slug]) is Guid cover) { input.CoverMediaId = cover; covers++; changed = true; }
            if (input.Publish && schedule.TryGetValue(post.Slug, out var at) && input.PublishedAtLocal != at) { input.PublishedAtLocal = at; dated++; changed = true; }
            if (!changed) continue;
            if (input.Normalize().Count > 0) { failed++; continue; }
            var saved = await store.SavePostAsync(actor, ip, post.Id, input, nowUtc);
            if (saved.Outcome != WebsiteOutcome.Saved) failed++;
        }
        TempData["Success"] = $"Đã gắn ảnh bìa cho {covers} bài và rải ngày đăng cho {dated} bài.";
        if (failed > 0) TempData["Error"] = $"{failed} bài chưa cập nhật được, thử lại sau.";
        return RedirectToAction(nameof(Library));
    }

    // "Đổi sang ảnh bìa mới": library articles whose cover is still a previously shipped library cover (uploaded under an
    // older file name such as bai-07-slug.jpg) get the current one. Covers staff picked themselves are left alone; the
    // publication dates are not touched. Saved through SavePostAsync (audited).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsitePostsEdit)]
    public async Task<IActionResult> ReplaceLibraryCovers()
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var bySlug = PostLibrary.All.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        var mediaNames = (await store.MediaAsync("bai-", 500, TAWebsite.Core.MediaKind.Image)).ToDictionary(m => m.Id, m => m.FileName);
        var onSite = (await store.PostsAsync(null, null, null)).Where(p => bySlug.ContainsKey(p.Slug)).ToList();
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        int replaced = 0, failed = 0;
        foreach (var item in onSite)
        {
            var lib = bySlug[item.Slug];
            if (item.CoverMediaId is not Guid current || !mediaNames.TryGetValue(current, out var name) || !PostLibrary.IsOldCoverName(lib, name)) continue;
            if (await store.PostAsync(item.Id) is not { } post) { failed++; continue; }
            if (await UploadCoverAsync(actor, ip, lib) is not Guid cover) { failed++; continue; }
            var input = new WebsitePostInput
            {
                Title = post.Title, Slug = post.Slug, Summary = post.Summary, BodyHtml = post.BodyHtml, CoverMediaId = cover, CategoryId = post.CategoryId,
                AuthorName = post.AuthorName, Tags = post.Tags, Publish = post.Status == 1, IsFeatured = post.IsFeatured, SeoTitle = post.SeoTitle,
                SeoDescription = post.SeoDescription, Stamp = Convert.ToBase64String(post.Stamp),
                PublishedAtLocal = post.PublishedAt is DateTime p ? WebsiteUi.Local(p) : null
            };
            input.SetSource(post.Source);
            if (input.Normalize().Count > 0) { failed++; continue; }
            var saved = await store.SavePostAsync(actor, ip, post.Id, input, nowUtc);
            if (saved.Outcome == WebsiteOutcome.Saved) replaced++; else failed++;
        }
        TempData["Success"] = replaced > 0 ? $"Đã đổi ảnh bìa mới cho {replaced} bài." : "Không có bài nào còn dùng ảnh bìa cũ.";
        if (failed > 0) TempData["Error"] = $"{failed} bài chưa đổi được ảnh, thử lại sau.";
        return RedirectToAction(nameof(Library));
    }

    // Uploads the cover photo shipped with a library article to the media library; null when there is none or it fails.
    private async Task<Guid?> UploadCoverAsync(Guid actor, string? ip, LibraryPost post)
    {
        if (PostLibrary.Cover(post.Order) is not { } bytes) return null;
        var result = await store.UploadMediaAsync(actor, ip, PostLibrary.CoverFileName(post), "image/jpeg", bytes, 960, 540, post.Title.Length > 250 ? post.Title[..250] : post.Title);
        return result is { Outcome: WebsiteOutcome.Saved, Id: Guid id } ? id : null;
    }

    private async Task<IReadOnlyList<TAWebsite.Core.Data.PostCategory>> SafeCategories()
    {
        try { return await store.CategoriesAsync(); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return []; }
    }
}

// Admin → Website TA → Liên hệ – báo giá: requests sent from the website form.
[Authorize(Policy = AdminPermissions.Policy.WebsiteLeadsView)]
public sealed class WebsiteLeadsController(IWebsiteAdminStore store, ILogger<WebsiteLeadsController> logger) : Controller
{
    public async Task<IActionResult> Index(byte? status, string? q)
    {
        try { return View(new WebsiteLeadsVm(await store.LeadsAsync(status, q), status, q, null)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsiteLeadsVm([], status, q, WebsiteController.StoreUnavailable)); }
    }

    // Excel file of the requests matching the current filter (same filter as the list). The export is audited first;
    // if the audit cannot be written the file is not produced.
    public async Task<IActionResult> Export(byte? status, string? q)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        try
        {
            var audit = await store.AuditLeadsExportAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), status, q);
            if (audit.Outcome != WebsiteOutcome.Saved) { TempData["Error"] = WebsiteUi.Message(audit.Outcome); return RedirectToAction(nameof(Index), new { status, q }); }
            var leads = await store.LeadsForExportAsync(status, q);
            var bytes = XlsxWriter.Write("Yêu cầu báo giá", LeadExport.Headers, leads.Select(LeadExport.Row), LeadExport.Widths);
            return File(bytes, XlsxWriter.ContentType, $"yeu-cau-bao-gia-{WebsiteUi.Local(DateTime.UtcNow):yyyyMMdd-HHmm}.xlsx");
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE");
            TempData["Error"] = WebsiteController.StoreUnavailable;
            return RedirectToAction(nameof(Index), new { status, q });
        }
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var lead = await store.LeadAsync(id);
        return lead is null ? NotFound() : View(lead);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteLeadsEdit)]
    public async Task<IActionResult> Update(Guid id, byte status, string? note, string? stamp)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.UpdateLeadAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id, status, note, stamp);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã cập nhật yêu cầu."; else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Details), new { id });
    }
}
