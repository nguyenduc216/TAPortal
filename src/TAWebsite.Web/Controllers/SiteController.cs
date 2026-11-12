using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TAWebsite.Core.Content;
using TAWebsite.Core.Data;
using TAWebsite.Web.Models;

namespace TAWebsite.Web.Controllers;

public sealed partial class SiteController(IWebsiteReader reader, ILogger<SiteController> logger) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Home()
    {
        var page = await reader.PageAsync("home");
        var site = await Site(page, "home", "/", jsonLd: OrganizationJsonLd(await reader.SettingsAsync()));
        return View("Page", new PageVm(page, site, await reader.LatestAsync(10)));
    }

    [HttpGet("/lien-he")]
    public async Task<IActionResult> Contact(string? dv)
    {
        var page = await reader.PageAsync("contact");
        if (!page.IsPublished) return NotFound();
        return View("Page", new PageVm(page, await Site(page, "contact", "/lien-he"), [], dv));
    }

    // Quote request from the 3-step form (fetch; JSON answer). Honeypot field "website" must stay empty.
    [HttpPost("/lien-he")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitLead([FromForm] LeadForm form)
    {
        if (!string.IsNullOrEmpty(form.Website)) return Json(new { ok = true }); // bot: pretend success, store nothing
        var error = form.Validate();
        if (error is not null) return BadRequest(new { ok = false, error });
        var outcome = await reader.AddLeadAsync(form.ToLead(), HttpContext.Connection.RemoteIpAddress?.ToString());
        return outcome switch
        {
            LeadOutcome.Saved => Json(new { ok = true }),
            LeadOutcome.TooMany => StatusCode(429, new { ok = false, error = "Bạn đã gửi nhiều yêu cầu trong thời gian ngắn. Vui lòng gọi hotline hoặc thử lại sau." }),
            _ => StatusCode(503, new { ok = false, error = "Hệ thống chưa nhận được yêu cầu. Vui lòng gọi hotline hoặc thử lại sau ít phút." })
        };
    }

    [HttpGet("/tin-tuc")]
    public async Task<IActionResult> News(string? cm, string? q, int trang = 1)
    {
        var page = await reader.PageAsync("news");
        if (!page.IsPublished) return NotFound();
        q = string.IsNullOrWhiteSpace(q) ? null : q.Trim()[..Math.Min(q.Trim().Length, 100)];
        cm = Slug.IsValid(cm) ? cm : null;
        var result = await reader.PostsAsync(new PostQuery(cm, q, Math.Clamp(trang, 1, 500)));
        var first = cm is null && q is null && result.Page == 1;
        var featured = first ? (await reader.LatestAsync(3)).ToList() : [];
        var site = await Site(page, "news", "/tin-tuc" + (cm is null ? "" : "?cm=" + cm));
        return View("News", new NewsVm(page, site, result, await reader.CategoriesAsync(), cm, q, featured, await reader.PopularAsync(4)));
    }

    [HttpGet("/tin-tuc/{slug}")]
    public async Task<IActionResult> Post(string slug)
    {
        var post = await reader.PostAsync(slug);
        if (post is null) return NotFound();
        var news = await reader.PageAsync("news");
        var (body, toc) = PostHtml.WithToc(PostHtml.Sanitize(post.BodyHtml));
        var settings = await reader.SettingsAsync();
        var global = await reader.PageAsync(SiteMap.GlobalPage);
        var title = (string.IsNullOrWhiteSpace(post.SeoTitle) ? post.Title : post.SeoTitle) + " | " + settings["seo.siteName"];
        var description = post.SeoDescription ?? post.Summary ?? PostHtml.PlainText(body, 160);
        var site = new SiteContext(settings, global.Section("footer") ?? EmptyContent, "news", title, description, "/tin-tuc/" + post.Slug,
            post.CoverMediaId is Guid c ? SiteContext.Img(c) : null, ArticleJsonLd(post, settings));
        var (previous, next) = await reader.NeighboursAsync(post);
        _ = CountView(post.Id);
        return View("Post", new PostVm(post, body, toc, site, await reader.RelatedAsync(post, 3), previous, next, news.Section("sidebar") ?? EmptyContent));
    }

    // Pages declared in TAWebsite.Core/Content/pages/*.json (Giới thiệu, Dịch vụ, dịch vụ chi tiết...). Lowest priority:
    // every explicit route above wins.
    [HttpGet("/{**path}", Order = 1000)]
    public async Task<IActionResult> ContentPage(string? path)
    {
        var def = SiteMap.FindByPath("/" + (path ?? ""));
        if (def is null || def.Key is "home" or "contact" or "news" || def.Path is null) return NotFound();
        var page = await reader.PageAsync(def.Key);
        if (!page.IsPublished) return NotFound();
        return View("Page", new PageVm(page, await Site(page, def.Nav, def.Path), await reader.LatestAsync(6)));
    }

    [HttpGet("/media/{id}")]
    public async Task<IActionResult> Media(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return NotFound();
        var file = await reader.MediaAsync(guid);
        if (file is null) return NotFound();
        var etag = new EntityTagHeaderValue("\"" + file.Sha256[..32] + "\"");
        if (Request.Headers.IfNoneMatch.ToString().Contains(etag.Tag.ToString(), StringComparison.Ordinal)) return StatusCode(304);
        Response.Headers.CacheControl = "public, max-age=604800, immutable"; // a media id always holds the same bytes
        Response.Headers.ETag = etag.ToString();
        return File(file.Data, file.ContentType);
    }

    [HttpGet("/robots.txt")]
    public async Task<IActionResult> Robots()
    {
        var settings = await reader.SettingsAsync();
        var text = settings.AllowIndexing
            ? $"User-agent: *\nAllow: /\nSitemap: {Request.Scheme}://{Request.Host}/sitemap.xml\n"
            : "User-agent: *\nDisallow: /\n";
        return Content(text, "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var root = $"{Request.Scheme}://{Request.Host}";
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var p in SiteMap.Pages.Where(p => p.Path is not null))
            sb.Append($"<url><loc>{root}{p.Path}</loc></url>\n");
        var page = 1;
        PostPage result;
        do
        {
            result = await reader.PostsAsync(new PostQuery(null, null, page++, 200));
            foreach (var post in result.Items)
                sb.Append($"<url><loc>{root}/tin-tuc/{post.Slug}</loc>{(post.PublishedAt is DateTime d ? $"<lastmod>{d:yyyy-MM-dd}</lastmod>" : "")}</url>\n");
        } while (result.Page < result.Pages && page <= 50);
        sb.Append("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    // Re-executed by UseStatusCodePagesWithReExecute for every empty error response (any method): 404 shows the "not found"
    // page; other codes (400 bad antiforgery token, 405...) keep their status with a short text.
    [AcceptVerbs("GET", "HEAD", "POST", Route = "/khong-tim-thay")]
    public async Task<IActionResult> NotFoundPage()
    {
        var original = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>()?.OriginalStatusCode ?? 404;
        if (original != 404) return StatusCode(original, original == 400 ? "Yêu cầu không hợp lệ hoặc đã hết hạn, vui lòng tải lại trang." : "Có lỗi xảy ra.");
        Response.StatusCode = StatusCodes.Status404NotFound;
        var home = await reader.PageAsync("home");
        var site = await Site(home, "", "/khong-tim-thay");
        return View("NotFound", site with { Title = "Không tìm thấy trang | " + site.Settings["seo.siteName"] });
    }

    [HttpGet("/loi")]
    public IActionResult Error() => Problem("Có lỗi xảy ra, vui lòng thử lại sau.");

    private static readonly SectionContent EmptyContent = new(new System.Text.Json.Nodes.JsonObject());

    private async Task<SiteContext> Site(RenderedPage page, string nav, string canonical, string? jsonLd = null)
    {
        var settings = await reader.SettingsAsync();
        var global = await reader.PageAsync(SiteMap.GlobalPage);
        return new SiteContext(settings, global.Section("footer") ?? EmptyContent, nav, page.Title, page.Description, canonical, null, jsonLd);
    }

    private async Task CountView(Guid id)
    {
        try { await reader.CountViewAsync(id); }
        catch (Exception ex) { logger.LogWarning(ex, "WEBSITE_VIEW_COUNT_FAILED"); }
    }

    private string OrganizationJsonLd(SiteSettings s) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "AccountingService",
        ["name"] = s["company.name"],
        ["url"] = $"{Request.Scheme}://{Request.Host}/",
        ["taxID"] = s["company.taxCode"],
        ["telephone"] = string.IsNullOrWhiteSpace(s["contact.hotline"]) ? null : s["contact.hotline"],
        ["email"] = string.IsNullOrWhiteSpace(s["contact.email"]) ? null : s["contact.email"],
        ["address"] = new Dictionary<string, string> { ["@type"] = "PostalAddress", ["streetAddress"] = s["office.address"], ["addressRegion"] = "Khánh Hòa", ["addressCountry"] = "VN" }
    }, SectionContent.JsonOptions);

    private string ArticleJsonLd(PostDetail p, SiteSettings s) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "Article",
        ["headline"] = p.Title,
        ["description"] = p.Summary,
        ["datePublished"] = p.PublishedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["dateModified"] = p.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["author"] = new Dictionary<string, string> { ["@type"] = "Organization", ["name"] = p.AuthorName ?? s["company.name"] },
        ["publisher"] = new Dictionary<string, string> { ["@type"] = "Organization", ["name"] = s["company.name"] },
        ["image"] = p.CoverMediaId is Guid c ? $"{Request.Scheme}://{Request.Host}{SiteContext.Img(c)}" : null
    }, SectionContent.JsonOptions);
}

public sealed partial class LeadForm
{
    public string[]? Service { get; set; }
    public string? Type { get; set; }
    public string? Industry { get; set; }
    public string? Volume { get; set; }
    public string? Staff { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Company { get; set; }
    public string? Time { get; set; }
    public string? Note { get; set; }
    public bool Consent { get; set; }
    public string? Source { get; set; }
    public string? Website { get; set; } // honeypot

    [GeneratedRegex(@"^(0|\+84)[0-9 .]{8,12}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 150) return "Vui lòng nhập họ tên (tối đa 150 ký tự).";
        if (string.IsNullOrWhiteSpace(Phone) || !PhonePattern().IsMatch(Phone.Trim())) return "Số điện thoại chưa đúng (bắt đầu bằng 0 hoặc +84).";
        if (!string.IsNullOrWhiteSpace(Email) && (Email.Trim().Length > 200 || !EmailPattern().IsMatch(Email.Trim()))) return "Email chưa đúng định dạng.";
        if (!Consent) return "Vui lòng đồng ý để T.A sử dụng thông tin nhằm liên hệ tư vấn.";
        if ((Service?.Length ?? 0) > 20 || (Note?.Length ?? 0) > 2000 || (Company?.Length ?? 0) > 250) return "Nội dung quá dài.";
        return null;
    }

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    public LeadInput ToLead() => new()
    {
        FullName = Name!.Trim(),
        Phone = Phone!.Trim(),
        Email = Email?.Trim(),
        CompanyName = Cut(Company?.Trim(), 250),
        Services = Cut(string.Join(", ", (Service ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())), 500),
        CompanyType = Cut(Type, 150),
        Scale = Cut(Join(string.IsNullOrWhiteSpace(Industry) ? null : "Ngành: " + Industry, string.IsNullOrWhiteSpace(Volume) ? null : "Chứng từ/tháng: " + Volume,
            string.IsNullOrWhiteSpace(Staff) ? null : "Lao động: " + Staff), 200),
        ContactTime = Cut(Time, 100),
        Message = Cut(Note?.Trim(), 2000),
        SourcePage = Cut(Source, 300)
    };
}
