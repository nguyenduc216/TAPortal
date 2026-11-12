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

// Admin → Website TA → Thư viện ảnh. Images live in dbo.WebsiteMedia; the public site serves them at /media/{id}.
[Authorize(Policy = AdminPermissions.Policy.WebsiteView)]
public sealed class WebsiteMediaController(IWebsiteAdminStore store, ILogger<WebsiteMediaController> logger) : Controller
{
    private const long UploadLimit = SqlWebsiteAdminStore.MaxImageBytes + 1024 * 1024;

    public async Task<IActionResult> Index(string? q)
    {
        try { return View(new WebsiteMediaVm(await store.MediaAsync(q, 300), q, null)); }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return View(new WebsiteMediaVm([], q, WebsiteController.StoreUnavailable)); }
    }

    // Image bytes for previews inside the Admin portal (thumbnails, picker, editor).
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> File(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var file = await store.MediaFileAsync(guid);
        if (file is null) return NotFound();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Data, file.ContentType);
    }

    // JSON list for the image picker of the section and article editors.
    public async Task<IActionResult> Picker(string? q)
    {
        try
        {
            var items = await store.MediaAsync(q, 120);
            return Json(items.Select(m => new { id = m.Id.ToString("N"), value = ImageRef.MediaPrefix + m.Id.ToString("N"), url = WebsiteUi.ImageUrl(m.Id), name = m.FileName, alt = m.AltText, w = m.Width, h = m.Height }));
        }
        catch (SqlException ex) { logger.LogError(ex, "WEBSITE_ADMIN_UNAVAILABLE"); return StatusCode(503); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    [RequestSizeLimit(UploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimit)]
    public async Task<IActionResult> Upload(IFormFile? file, string? alt)
    {
        var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        string? error = null;
        byte[] data = [];
        (string ContentType, int? Width, int? Height)? probe = null;
        if (file is null || file.Length == 0) error = "Chọn một file ảnh.";
        else if (file.Length > SqlWebsiteAdminStore.MaxImageBytes) error = "Ảnh tối đa 5 MB. Hãy nén ảnh (vd. squoosh.app) rồi tải lại.";
        else
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            data = ms.ToArray();
            probe = ImageProbe.Inspect(data);
            if (probe is null) error = "Chỉ nhận ảnh PNG, JPEG, WEBP hoặc GIF.";
        }
        if (error is not null)
        {
            if (wantsJson) return BadRequest(new { error });
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }
        var name = Path.GetFileName(file!.FileName);
        var result = await store.UploadMediaAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), name, probe!.Value.ContentType, data, probe.Value.Width, probe.Value.Height,
            string.IsNullOrWhiteSpace(alt) ? Path.GetFileNameWithoutExtension(name).Replace('-', ' ').Replace('_', ' ') : alt);
        if (wantsJson)
            return result.Outcome == WebsiteOutcome.Saved && result.Id is Guid id
                ? Json(new { id = id.ToString("N"), value = ImageRef.MediaPrefix + id.ToString("N"), url = WebsiteUi.ImageUrl(id), name })
                : BadRequest(new { error = WebsiteUi.Message(result.Outcome) });
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã tải ảnh lên thư viện.";
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    public async Task<IActionResult> Alt(Guid id, string? alt, string? q)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.UpdateMediaAltAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id, alt);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã lưu mô tả ảnh."; else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { q });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AdminPermissions.Policy.WebsiteMediaEdit)]
    public async Task<IActionResult> Delete(Guid id, string? q)
    {
        if (UsersController.ActorId(User) is not Guid actor) return Forbid();
        var result = await store.DeleteMediaAsync(actor, HttpContext.Connection.RemoteIpAddress?.ToString(), id);
        if (result.Outcome == WebsiteOutcome.Saved) TempData["Success"] = "Đã xóa ảnh. Nơi nào đang dùng ảnh này sẽ không hiện ảnh nữa.";
        else TempData["Error"] = WebsiteUi.Message(result.Outcome);
        return RedirectToAction(nameof(Index), new { q });
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
