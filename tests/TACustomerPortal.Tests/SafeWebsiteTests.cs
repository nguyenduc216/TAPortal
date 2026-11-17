extern alias admin;
extern alias website;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TAWebsite.Core;
using TAWebsite.Core.Content;
using TAWebsite.Core.Data;
using AdminSecurity = admin::TAPortal.Web.Security;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// WEBSITE-TA: Admin → Website TA and the public site TAWebsite.Web. No SQL host: the admin store and the site reader are
// in-memory fakes. The SQL itself (035 migration, verify, rollback, store text) is checked as text.
public sealed class SafeWebsiteTests
{
    private const string TestScheme = "SafeWebsiteTest";
    private static readonly Guid Actor = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 40, 0, 0, 0, 20, 8, 6, 0, 0, 0];

    // ---- Migration 035 / catalogue -------------------------------------------------------------------------------
    [Fact]
    public void Migration035MatchesCatalogueAndFollowsTheMigrationRules()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "035-website-cms.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "059-verify-website-cms.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "060-rollback-website-cms.sql"));
        var expected = AdminSecurity.AdminPermissions.Migration035Codes.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(7, expected.Count);
        Assert.Equal(expected, Regex.Matches(migration, @"\('([A-Z.]+)',\s+N'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(verify, @"\('([A-Z.]+)'\)").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.DoesNotContain(expected, c => AdminSecurity.AdminPermissions.MigrationCodes.Contains(c));
        const string marker = "A0000035-0000-4000-8000-000000000035";
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains(marker, sql));
        Assert.All(new[] { migration, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", verify);
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.DoesNotContain("CREATE OR ALTER", migration + verify + rollback);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE)\b", migration);
        Assert.All(Regex.Matches(rollback, @"(?im)^DELETE FROM [^\r\n]+").Select(m => m.Value), d => Assert.Contains("CreatedBy = @Marker", d));
        Assert.Contains("r.Code <> 'SYS_ADMIN'", rollback);
        Assert.Contains("THROW 51002", rollback); // refuses while website content exists
        foreach (var table in new[] { "WebsitePages", "WebsiteSections", "WebsiteMedia", "WebsitePostCategories", "WebsitePosts", "WebsiteSettings", "WebsiteLeads" })
        {
            Assert.Contains($"CREATE TABLE dbo.{table}", migration);
            Assert.Contains($"DROP TABLE dbo.{table}", rollback);
            Assert.Contains($"N'{table}'", verify);
        }
        // Next free numbers: no other file uses 035 / 059 / 060.
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "migrations"), "035-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "059-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "060-*.sql"));
    }

    [Fact]
    public void WebsiteNavigationIsGatedByItsPermissions()
    {
        Assert.Equal("Website", AdminSecurity.AdminNavigation.FirstAllowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "WEBSITE.VIEW")], "t")))!.Controller);
        Assert.Equal("WebsiteLeads", AdminSecurity.AdminNavigation.FirstAllowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "WEBSITE.LEADS.VIEW")], "t")))!.Controller);
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "TAPortal.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("Can(AdminPermissions.WebsiteView)", layout);
        Assert.Contains("ta-website.js", layout);
    }

    // ---- Core: site map, content, links, images, text --------------------------------------------------------------
    [Fact]
    public void SiteMapDefaultsMatchTheirSectionTypesAndEveryTypeHasAView()
    {
        var views = Path.Combine(RepoRoot(), "src", "TAWebsite.Web", "Views", "Sections");
        foreach (var type in SectionTypes.All) Assert.True(File.Exists(Path.Combine(views, type.Key + ".cshtml")), $"missing view for {type.Key}");
        Assert.Contains(SiteMap.Pages, p => p.Key == "home" && p.Path == "/");
        Assert.Contains(SiteMap.Pages, p => p.Key == "contact" && p.Path == "/lien-he");
        Assert.Contains(SiteMap.Pages, p => p.Key == "news" && p.Path == "/tin-tuc");
        Assert.NotNull(SiteMap.Find(SiteMap.GlobalPage)?.Section("footer"));
        foreach (var page in SiteMap.Pages)
        {
            Assert.Equal(page.Sections.Count, page.Sections.Select(s => s.Key).Distinct().Count());
            foreach (var section in page.Sections)
            {
                var keys = section.SectionType.Fields.Select(f => f.Key).ToHashSet();
                Assert.All(section.Defaults.Select(p => p.Key), k => Assert.True(keys.Contains(k), $"{page.Key}/{section.Key}: unknown field {k}"));
                foreach (var field in section.SectionType.Fields.Where(f => f.Kind == FieldKind.Image))
                    Assert.True(ImageRef.IsValid(section.Defaults[field.Key]?.GetValue<string>()), $"{page.Key}/{section.Key}.{field.Key}");
            }
        }
        Assert.True(SiteAssets.Exists("hero-consultant.webp"));
        Assert.True(SiteAssets.Exists("ta-logo.png"));
    }

    [Fact]
    public void SectionHtmlKeepsTheDesignButNotScripts()
    {
        var html = PostHtml.SanitizeSection("<section class=\"dv-section\" id=\"x\" style=\"padding-top:40px\"><div role=\"tablist\" aria-label=\"Giai đoạn\">"
            + "<button role=\"tab\" aria-selected=\"true\" aria-controls=\"p1\" onclick=\"evil()\">A</button></div><div id=\"p1\" role=\"tabpanel\" hidden>B</div>"
            + "<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\"><path d=\"M1 1h2\"/></svg><a href=\"javascript:alert(1)\">x</a>"
            + "<script>alert(1)</script><iframe src=\"https://evil\"></iframe><form action=\"https://evil\"><input name=\"q\"></form></section>");
        foreach (var kept in new[] { "class=\"dv-section\"", "id=\"x\"", "role=\"tablist\"", "aria-label=\"Giai đoạn\"", "aria-controls=\"p1\"", "hidden", "<svg", "<path", "padding-top" })
            Assert.Contains(kept, html);
        Assert.Matches("(?i)viewbox=\"0 0 24 24\"", html);
        foreach (var removed in new[] { "onclick", "javascript:", "<script", "<iframe", "<form" }) Assert.DoesNotContain(removed, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhaseTwoPagesComeFromTheMockups()
    {
        foreach (var (key, path, sections) in new[] { ("about", "/gioi-thieu", 5), ("services", "/dich-vu", 8), ("svc-setup", "/dich-vu/thanh-lap-doanh-nghiep", 8), ("svc-accounting", "/dich-vu/ke-toan-thue", 9) })
        {
            var page = SiteMap.Get(key);
            Assert.Equal(path, page.Path);
            Assert.Equal(sections, page.Sections.Count);
            Assert.Same(page, SiteMap.FindByPath(path + "/"));
            Assert.All(page.Sections, s => Assert.Equal("rich-html", s.Type));
            var html = string.Join("", page.Sections.Select(s => s.Defaults["html"]!.GetValue<string>()));
            Assert.DoesNotContain(".html\"", html); // every mockup link points to a site route
            Assert.DoesNotContain("src=\"assets/", html);
            Assert.All(Regex.Matches(html, "src=\"/site-assets/([^\"?]+)").Select(m => m.Groups[1].Value), a => Assert.True(SiteAssets.Exists(a), a));
        }
        Assert.Contains("/css/gioi-thieu.css", SiteMap.Get("about").Styles);
        Assert.Equal("dv-main", SiteMap.Get("services").MainClass);
    }

    [Fact]
    public void SavedContentReplacesDefaultsFieldByField()
    {
        var section = SiteMap.Get("home").Section("hero")!;
        var merged = new SectionContent(SectionContent.Merge(section.SectionType, section.Defaults, new JsonObject { ["title"] = "Tiêu đề mới", ["unknown"] = "x" }));
        Assert.Equal("Tiêu đề mới", merged["title"]);
        Assert.Equal(section.Defaults["kicker"]!.GetValue<string>(), merged["kicker"]);
        Assert.False(merged.Data.ContainsKey("unknown"));
        var page = PageComposer.Compose(SiteMap.Get("home"), new PageRow("home", "SEO", null, true, DateTime.UtcNow),
            [new SectionRow("home", "faq", null, 5, false, DateTime.UtcNow)]);
        Assert.Equal("SEO", page.Title);
        Assert.Equal("faq", page.Sections[0].Def.Key); // order 5 comes first
        Assert.False(page.Sections[0].IsVisible);
        Assert.Null(page.Section("faq"));
    }

    [Theory]
    [InlineData("/lien-he", true)]
    [InlineData("#services", true)]
    [InlineData("https://zalo.me/0900000000", true)]
    [InlineData("tel:0900000000", true)]
    [InlineData("", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JaVaScRiPt:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("//evil.example", false)]
    public void OnlySafeLinksAreAccepted(string url, bool allowed) => Assert.Equal(allowed, SafeUrl.IsAllowed(url));

    [Fact]
    public void ImagesAreRecognisedByContentNotName()
    {
        var png = ImageProbe.Inspect(Png)!.Value;
        Assert.Equal(("image/png", 40, 20), (png.ContentType, png.Width!.Value, png.Height!.Value));
        Assert.Null(ImageProbe.Inspect("<svg onload=alert(1)>"u8.ToArray()));
        Assert.Null(ImageProbe.Inspect("%PDF-1.7"u8.ToArray()));
        Assert.Equal("image/jpeg", ImageProbe.Inspect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10])!.Value.ContentType);
        Assert.True(ImageRef.IsValid("asset:service-1.webp"));
        Assert.True(ImageRef.IsValid("media:" + Guid.NewGuid().ToString("N")));
        Assert.False(ImageRef.IsValid("asset:../../web.config"));
        Assert.False(ImageRef.IsValid("https://evil.example/x.png"));
        Assert.Equal("/media/" + Guid.Empty.ToString("N"), ImageRef.Url("media:" + Guid.Empty.ToString("N"), "/media"));
    }

    [Fact]
    public void SlugsAndArticleHtmlAreSafe()
    {
        Assert.Equal("doanh-nghiep-moi-thanh-lap-can-lam-gi", Slug.From("Doanh nghiệp mới thành lập cần làm gì?"));
        Assert.Equal("dang-ky-hoa-don-dien-tu-2026", Slug.From("  Đăng ký hóa đơn điện tử — 2026 "));
        Assert.False(Slug.IsValid("Có dấu"));
        Assert.False(Slug.IsValid("a--b"));
        var html = PostHtml.Sanitize("<h2 onclick=\"x()\">Mục 1</h2><script>alert(1)</script><p style=\"color:red\">a <a href=\"javascript:alert(1)\">x</a>"
            + "<img src=\"/media/abc\" onerror=\"alert(1)\"></p><iframe src=\"https://evil\"></iframe><div class=\"ar-callout evil\">c</div>");
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("onerror", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("iframe", html);
        Assert.DoesNotContain("style=", html);
        Assert.DoesNotContain("evil", html);
        Assert.Contains("class=\"ar-callout\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        var (withIds, toc) = PostHtml.WithToc(html);
        Assert.Equal([("muc-1", "Mục 1")], toc);
        Assert.Contains("<h2 id=\"muc-1\">", withIds);
    }

    [Fact]
    public void SectionFormReadsListsInOrderAndRejectsUnsafeValues()
    {
        var type = SectionTypes.Find("faq")!;
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["c.title"] = "Hỏi đáp", ["c.items[1].q"] = "Câu B", ["c.items[1].a"] = "Trả lời B", ["c.items[0].q"] = "Câu A", ["c.items[0].a"] = "Trả lời A",
            ["c.items[2].q"] = "", ["c.items[2].a"] = "" // an empty item is dropped
        });
        var (content, errors) = S.WebsiteSectionForm.Read(type, form);
        Assert.Empty(errors);
        Assert.Equal(["Câu A", "Câu B"], new SectionContent(content).List("items").Select(i => i["q"]));
        var hero = SectionTypes.Find("home-hero")!;
        var (bad, badErrors) = S.WebsiteSectionForm.Read(hero, new FormCollection(new Dictionary<string, StringValues>
        {
            ["c.primaryUrl"] = "javascript:alert(1)", ["c.image"] = "https://evil.example/x.png", ["c.title"] = new string('x', 5000)
        }));
        Assert.Equal(3, badErrors.Count);
        Assert.Equal("", new SectionContent(bad)["primaryUrl"]);
        Assert.Equal("", new SectionContent(bad)["image"]);
    }

    [Fact]
    public void PostInputNormalizesAndValidates()
    {
        var input = new S.WebsitePostInput { Title = "  Hóa đơn điện tử: điều cần biết ", BodyHtml = "<p>Nội dung</p><script>x</script>", Tags = "a, b ,a,,", Publish = true };
        Assert.Empty(input.Normalize());
        Assert.Equal("hoa-don-dien-tu-dieu-can-biet", input.Slug);
        Assert.Equal("a, b", input.Tags);
        Assert.DoesNotContain("script", input.BodyHtml);
        var now = new DateTime(2026, 11, 12, 1, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now, input.PublishedAtUtc(now));
        input.PublishedAtLocal = new DateTime(2026, 12, 1, 8, 0, 0);
        Assert.Equal(new DateTime(2026, 12, 1, 1, 0, 0), input.PublishedAtUtc(now)); // 08:00 Vietnam = 01:00 UTC
        var empty = new S.WebsitePostInput { Title = "", Publish = true, BodyHtml = "<p> </p>" };
        var errors = empty.Normalize().Select(e => e.Field).ToList();
        Assert.Contains("Title", errors);
        Assert.Contains("BodyHtml", errors);
    }

    // ---- Admin pages (fake store) ---------------------------------------------------------------------------------
    [Fact]
    public async Task AdminCanOpenEveryWebsitePageAndEditASection()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.CONTENT.EDIT", "WEBSITE.POSTS.EDIT", "WEBSITE.MEDIA.EDIT", "WEBSITE.SETTINGS.EDIT", "WEBSITE.LEADS.VIEW", "WEBSITE.LEADS.EDIT");
        var client = AdminClient(f);
        foreach (var url in new[] { "/Website", "/Website/Page/home", "/Website/Page/contact", "/Website/Page/about", "/Website/Section?page=about&key=closing", "/Website/Section?page=home&key=hero", "/Website/Section?page=contact&key=form",
                     "/Website/Settings", "/WebsiteMedia", "/WebsitePosts", "/WebsitePosts/Create", "/WebsiteLeads" })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode}");
            // Razor code must never leak as text (e.g. "section@if" is read as an e-mail address, not code).
            var body = await response.Content.ReadAsStringAsync();
            Assert.False(Regex.IsMatch(body, @"@(if|foreach|for|switch)\s*\("), $"{url}: raw Razor code in output");
        }
        Assert.Contains("<span>8 section</span>", await client.GetStringAsync("/Website"));
        var editor = await client.GetStringAsync("/Website/Section?page=home&key=faq");
        Assert.Contains("name=\"c.items[0].q\"", editor);
        Assert.Contains("data-ta-web-template", editor);
        var post = await client.PostAsync("/Website/Section?page=home&key=faq", Form(Token(editor), ("stamp", ""), ("c.eyebrow", "HỎI ĐÁP"), ("c.title", "Câu hỏi"),
            ("c.intro", ""), ("c.items[0].q", "Q1"), ("c.items[0].a", "A1")));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var saved = store.Sections[("home", "faq")];
        Assert.Equal("Câu hỏi", new SectionContent(saved)["title"]);
        Assert.Single(new SectionContent(saved).List("items"));

        var visual = await client.GetStringAsync("/Website/Section?page=services&key=pillars");
        Assert.Contains("data-ta-visual", visual);
        Assert.Contains("/css/dich-vu.css", visual);
        var savedVisual = await client.PostAsync("/Website/Section?page=services&key=pillars", Form(Token(visual), ("stamp", ""),
            ("c.html", "<section class=\"dv-section\"><h2 onclick=\"x()\">Ba nhóm</h2><img src=\"/WebsiteMedia/File/abc\"><script>x</script></section>")));
        Assert.Equal(HttpStatusCode.Redirect, savedVisual.StatusCode);
        var stored = new SectionContent(store.Sections[("services", "pillars")])["html"];
        Assert.Contains("src=\"/media/abc\"", stored);
        Assert.DoesNotContain("onclick", stored);
        Assert.DoesNotContain("script", stored);

        var bad = await client.PostAsync("/Website/Section?page=home&key=hero", Form(Token(editor), ("c.primaryUrl", "javascript:alert(1)")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.False(store.Sections.ContainsKey(("home", "hero")));
    }

    // Operator 2026-11-12: Admin → Hóa đơn tự động (Mẫu email, Điều khoản dịch vụ). Uses the same admin host; without a database
    // the built-in texts are shown. Preview never writes; a viewer cannot post.
    [Fact]
    public async Task InvoiceAppContentPagesOpenAndPreviewWithoutSaving()
    {
        var store = new MemoryAdminStore();
        await using (var f = AdminFactory(store, AdminSecurity.AdminPermissions.InvoiceAppContentView, AdminSecurity.AdminPermissions.InvoiceAppContentEdit))
        {
            var client = AdminClient(f);
            foreach (var url in new[] { "/InvoiceAppContent", "/InvoiceAppContent/Email/EMAIL.ACCOUNT_CREATED", "/InvoiceAppContent/Email/EMAIL.PASSWORD_RESET", "/InvoiceAppContent/Terms" })
            {
                var response = await client.GetAsync(url);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode}");
                var body = await response.Content.ReadAsStringAsync();
                Assert.False(Regex.IsMatch(body, @"@(if|foreach|for|switch)\s*\("), $"{url}: raw Razor code in output");
            }
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/InvoiceAppContent/Email/EMAIL.NOPE")).StatusCode);
            var editor = await client.GetStringAsync("/InvoiceAppContent/Email/EMAIL.ACCOUNT_CREATED");
            // Operator 2026-11-13: the pages are tabs of "Cấu hình hệ thống" (one menu entry), not a menu group of their own.
            Assert.Contains("data-ta-tab=\"emails\" aria-current=\"page\" href=\"/InvoiceAppContent\"", editor);
            Assert.Contains("data-ta-tab=\"terms\"", editor);
            Assert.DoesNotContain("data-ta-tab=\"password\"", editor);                      // password security needs AUTH.ROLES.MANAGE
            Assert.Contains("title=\"Cấu hình hệ thống\" aria-current=\"page\" href=\"/SystemSettings\"", WebUtility.HtmlDecode(editor));
            Assert.DoesNotContain("ta-nav__section\">Hóa đơn tự động<", WebUtility.HtmlDecode(editor));
            var hub = await client.GetAsync("/SystemSettings");
            Assert.Equal(HttpStatusCode.Redirect, hub.StatusCode);
            Assert.Equal("/InvoiceAppContent", hub.Headers.Location?.OriginalString);
            Assert.Contains("srcdoc=\"", editor);
            Assert.Contains("images/logo.png", WebUtility.HtmlDecode(editor));
            var preview = await client.PostAsync("/InvoiceAppContent/Email/EMAIL.ACCOUNT_CREATED", Form(Token(editor), ("subject", "Chào {{NguoiLienHe}}"),
                ("body", "Nội dung thử {{SoTien}}"), ("command", "save")));
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);                          // refused: shown again with the reasons
            var refused = WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync());
            Assert.Contains("Không có giá trị {{SoTien}}", refused);
            Assert.Contains("phải giữ {{ThongTinDangNhap}}", refused);
            var terms = WebUtility.HtmlDecode(await client.GetStringAsync("/InvoiceAppContent/Terms"));
            Assert.Contains("Điều 16.", terms);
            Assert.Contains("[TÊN CÔNG TY]", terms);                                       // the warning lists what is left to fill in
        }
        await using (var f = AdminFactory(store, AdminSecurity.AdminPermissions.InvoiceAppContentView))
        {
            var client = AdminClient(f);
            var editor = await client.GetStringAsync("/InvoiceAppContent/Terms");
            Assert.Contains("<fieldset disabled", editor);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/InvoiceAppContent/Terms", Form(Token(editor), ("body", "## Điều 1. X"), ("command", "save")))).StatusCode);
        }
        await using (var f = AdminFactory(store, AdminSecurity.AdminPermissions.WebsiteView))
            Assert.Equal(HttpStatusCode.Forbidden, (await AdminClient(f).GetAsync("/InvoiceAppContent")).StatusCode);
    }

    [Fact]
    public async Task ViewOnlyUserCannotWrite()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW");
        var client = AdminClient(f);
        var editor = await client.GetStringAsync("/Website/Section?page=home&key=faq");
        Assert.Contains("<fieldset disabled", editor);
        var post = await client.PostAsync("/Website/Section?page=home&key=faq", Form(Token(editor), ("c.title", "x")));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/WebsiteLeads")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/WebsitePosts/Create")).StatusCode);
        Assert.Empty(store.Sections);
    }

    [Fact]
    public async Task UploadAcceptsRealImagesOnly()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.MEDIA.EDIT");
        var client = AdminClient(f);
        var token = Token(await client.GetStringAsync("/WebsiteMedia"));
        var ok = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/WebsiteMedia/Upload") { Content = Upload(token, "logo.png", Png), Headers = { Accept = { new("application/json") } } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("\"value\":\"media:", await ok.Content.ReadAsStringAsync());
        Assert.Equal(("image/png", 40), (store.Media.Single().ContentType, store.Media.Single().Width!.Value));
        var fake = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/WebsiteMedia/Upload") { Content = Upload(token, "x.png", "<svg onload=alert(1)>"u8.ToArray()), Headers = { Accept = { new("application/json") } } });
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);
        Assert.Single(store.Media);
    }

    [Fact]
    public async Task MediaLibraryKeepsImagesVideosAndPdfApart()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.MEDIA.EDIT");
        var client = AdminClient(f);
        var token = Token(await client.GetStringAsync("/WebsiteMedia?kind=file"));
        var pdf = "%PDF-1.7 %test"u8.ToArray();
        var mp4 = new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0 };
        HttpRequestMessage Up(string kind, string name, byte[] data)
        {
            var content = Upload(token, name, data);
            content.Add(new StringContent(kind), "kind");
            return new HttpRequestMessage(HttpMethod.Post, "/WebsiteMedia/Upload") { Content = content, Headers = { Accept = { new("application/json") } } };
        }
        var okPdf = await client.SendAsync(Up("file", "mau-to-khai.pdf", pdf));
        Assert.Equal(HttpStatusCode.OK, okPdf.StatusCode);
        Assert.Contains("\"publicUrl\":\"/media/", await okPdf.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Up("video", "gioi-thieu.mp4", mp4))).StatusCode);
        // Wrong kind for the tab, and a renamed file, are refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Up("file", "anh.pdf", Png))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Up("video", "x.mp4", pdf))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Up("image", "x.png", pdf))).StatusCode);
        var link = await client.PostAsync("/WebsiteMedia/AddVideoLink", Form(token, ("url", "https://youtu.be/dQw4w9WgXcQ?t=5"), ("title", "Giới thiệu T.A")));
        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", store.Media.Single(m => m.ContentType == TAWebsite.Core.MediaKinds.YouTube).ExternalUrl);
        await client.PostAsync("/WebsiteMedia/AddVideoLink", Form(token, ("url", "https://evil.example/watch?v=dQw4w9WgXcQ")));
        Assert.Equal(3, store.Media.Count);
        Assert.Equal(["application/pdf", "video/mp4", "video/youtube"], store.Media.Select(m => m.ContentType).Order().ToArray());
        // The image picker never offers a video or a PDF; the video picker gets both videos.
        Assert.Equal("[]", await client.GetStringAsync("/WebsiteMedia/Picker"));
        var videos = await client.GetStringAsync("/WebsiteMedia/Picker?kind=video");
        Assert.Contains("https://www.youtube.com/watch?v=dQw4w9WgXcQ", videos);
        Assert.Contains("i.ytimg.com/vi/dQw4w9WgXcQ", videos);
        Assert.Contains("Giới thiệu T.A", System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/WebsiteMedia?kind=video")));
    }

    [Fact]
    public void ArticleVideosBecomePlayersOnlyForYouTubeAndOwnMedia()
    {
        Assert.Equal("dQw4w9WgXcQ", TAWebsite.Core.MediaKinds.YouTubeId("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=x"));
        Assert.Equal("dQw4w9WgXcQ", TAWebsite.Core.MediaKinds.YouTubeId("https://www.youtube.com/shorts/dQw4w9WgXcQ"));
        Assert.Null(TAWebsite.Core.MediaKinds.YouTubeId("https://youtube.com.evil.example/watch?v=dQw4w9WgXcQ"));
        Assert.Null(TAWebsite.Core.MediaKinds.YouTubeId("javascript:alert(1)//youtu.be/dQw4w9WgXcQ"));
        var id = Guid.NewGuid().ToString("N");
        var html = PostHtml.WithMedia(PostHtml.Sanitize(
            "<figure class=\"ar-video\"><a href=\"https://youtu.be/dQw4w9WgXcQ\">▶ Video</a><figcaption>Giới thiệu</figcaption></figure>"
            + $"<figure class=\"ar-video\"><p><a href=\"/media/{id}\">▶ Video</a></p></figure>"
            + "<figure class=\"ar-video\"><a href=\"https://evil.example/x.mp4\">x</a></figure>"
            + $"<p><a class=\"ar-file\" href=\"/media/{id}\" target=\"_blank\">Tải về</a></p><iframe src=\"https://evil.example\"></iframe>"));
        Assert.Contains("<iframe src=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ\" title=\"Giới thiệu\"", html);
        Assert.Contains($"<video src=\"/media/{id}\" controls=\"\"", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<iframe"));
        Assert.Contains("href=\"https://evil.example/x.mp4\"", html);
        Assert.Contains("class=\"ar-file\"", html);
        Assert.DoesNotContain("evil.example\"></iframe>", html);
        Assert.Equal(TAWebsite.Core.MediaKinds.Pdf, TAWebsite.Core.MediaKinds.Sniff("%PDF-1.4"u8));
        Assert.Null(TAWebsite.Core.MediaKinds.Sniff("<svg>"u8));
    }

    [Fact]
    public async Task PostEditorSavesSanitizedArticles()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        var token = Token(await client.GetStringAsync("/WebsitePosts/Create"));
        var response = await client.PostAsync("/WebsitePosts/Create", Form(token, ("Input.Title", "Thủ tục sau thành lập"), ("Input.BodyHtml", "<h2>A</h2><p onclick=\"x\">b</p><script>x</script>"),
            ("Input.Publish", "true")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var post = store.Posts.Values.Single();
        Assert.Equal("thu-tuc-sau-thanh-lap", post.Input.Slug);
        Assert.DoesNotContain("script", post.Input.BodyHtml);
        Assert.DoesNotContain("onclick", post.Input.BodyHtml);
        var preview = await client.GetAsync($"/WebsitePosts/Preview/{store.Posts.Keys.Single()}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewHtml = await preview.Content.ReadAsStringAsync();
        Assert.Contains("XEM TRƯỚC", previewHtml);
        Assert.Contains("<h2 id=\"muc-1\">A</h2>", previewHtml);
        Assert.DoesNotContain("<script>x", previewHtml);
        var invalid = await client.PostAsync("/WebsitePosts/Create", Form(token, ("Input.Title", ""), ("Input.Publish", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single(store.Posts);
    }

    [Fact]
    public void PostLibraryArticlesAreValidAndSurviveTheSanitizer()
    {
        var posts = S.PostLibrary.All;
        Assert.Equal(30, posts.Count);
        Assert.Equal(20, posts.Count(p => p.Origin is null));                       // T.A's own articles
        Assert.Equal(10, posts.Count(p => p.Origin?.Kind == PostSource.Summary));   // summaries citing another site
        Assert.Equal(posts.Count, posts.Select(p => p.Slug).Distinct().Count());
        foreach (var p in posts)
        {
            if (p.Origin is { } origin)
            {
                var source = new S.WebsitePostInput { Title = p.Title };
                source.SetSource(origin);
                Assert.Empty(source.Normalize());                     // name + http(s) link of the original present
                Assert.NotNull(origin.PublishedOn);
                Assert.Contains(p.Sources!, s => s.Contains(origin.Url!));
                Assert.DoesNotContain("href=\"http", p.BodyHtml);    // the original is linked by the source box, not the body
            }
            Assert.True(Slug.IsValid(p.Slug), p.Slug);
            Assert.Contains(S.PostLibrary.Categories, c => c.Slug == p.Category);
            Assert.InRange(p.Title.Length, 10, 120);
            Assert.InRange(p.SeoDescription.Length, 50, 200);
            Assert.Contains("<h2", p.BodyHtml);
            Assert.Contains("/lien-he", p.BodyHtml);
            var input = new S.WebsitePostInput { Title = p.Title, Slug = p.Slug, Summary = p.Summary, BodyHtml = p.BodyHtml, Tags = p.Tags, SeoTitle = p.SeoTitle, SeoDescription = p.SeoDescription };
            Assert.Empty(input.Normalize());
            Assert.Equal(p.Slug, input.Slug);
            Assert.True(PostHtml.PlainText(input.BodyHtml, int.MaxValue).Length > (p.Origin is null ? 3000 : 2000), p.Slug);
            Assert.Contains("ar-callout", input.BodyHtml);
        }
    }

    [Fact]
    public async Task PostLibraryImportsMissingArticlesAsDraftsOnlyOnce()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        var page = await client.GetStringAsync("/WebsitePosts/Library");
        Assert.Contains("Chưa thêm", WebUtility.HtmlDecode(page));
        var token = Token(page);
        var first = S.PostLibrary.All[0];
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token, ("slugs", first.Slug)))).StatusCode);
        Assert.Single(store.Posts);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token))).StatusCode);
        Assert.Equal(S.PostLibrary.All.Count, store.Posts.Count);
        Assert.All(store.Posts.Values, p => Assert.False(p.Input.Publish));
        Assert.All(store.Posts.Values, p => Assert.Equal(S.PostLibrary.Author, p.Input.AuthorName));
        await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token));
        Assert.Equal(S.PostLibrary.All.Count, store.Posts.Count);
        var library = WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts/Library"));
        Assert.Contains("Thêm và đăng luôn " + S.PostLibrary.All.Count, library);
        Assert.Contains("/WebsitePosts/LibraryPreview?slug=" + first.Slug, library);
        Assert.Contains("luatvietnam.vn", library); // sources visible on the list, not only in the review notes

        // "Xem thử": the article as the website shows it, plus its sources (links) and review points for staff.
        // Every article has at least one linked source; plain entries (laws cited by name, search results) are listed as text.
        Assert.All(S.PostLibrary.All, p => Assert.Contains(p.Sources!, s => S.PostLibrary.SplitSource(s).Url is not null));
        Assert.Equal(("Bài gốc: X", "https://a.vn/x"), S.PostLibrary.SplitSource("Bài gốc: X - https://a.vn/x"));
        var previewResponse = await client.GetAsync("/WebsitePosts/LibraryPreview?slug=" + first.Slug);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = WebUtility.HtmlDecode(await previewResponse.Content.ReadAsStringAsync());
        Assert.Contains(first.Title, preview);
        Assert.Contains("XEM THỬ", preview);
        Assert.Contains("href=\"" + S.PostLibrary.SplitSource(first.Sources![0]).Url + "\"", preview);
        Assert.Contains("Cần T.A rà lại", preview);
        Assert.Contains("noindex", preview);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/WebsitePosts/LibraryPreview?slug=khong-co")).StatusCode);

        // "Thêm và đăng luôn": existing library drafts are published in place, nothing is duplicated.
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token, ("publish", "true")))).StatusCode);
        Assert.Equal(S.PostLibrary.All.Count, store.Posts.Count);
        Assert.All(store.Posts.Values, p => Assert.True(p.Input.Publish));
        Assert.DoesNotContain("Thêm và đăng luôn", WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts/Library")));

        var fresh = new MemoryAdminStore();
        await using (var f2 = AdminFactory(fresh, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT"))
        {
            var c2 = AdminClient(f2);
            var t2 = Token(await c2.GetStringAsync("/WebsitePosts/Library"));
            await c2.PostAsync("/WebsitePosts/ImportLibrary", Form(t2, ("publish", "true")));
            Assert.Equal(S.PostLibrary.All.Count, fresh.Posts.Count);
            Assert.All(fresh.Posts.Values, p => Assert.True(p.Input.Publish));
            // Cited summaries keep their source when added, so the website shows "Tổng hợp từ …" and the source box.
            foreach (var lib in S.PostLibrary.All.Where(p => p.Origin is not null))
            {
                var saved = fresh.Posts.Values.Single(p => p.Input.Slug == lib.Slug).Input;
                Assert.Equal(lib.SourceKind, saved.SourceKind);
                Assert.Equal(lib.SourceUrl, saved.SourceUrl);
            }
            var cited = S.PostLibrary.All.First(p => p.Origin is not null);
            var citedPreview = WebUtility.HtmlDecode(await c2.GetStringAsync("/WebsitePosts/LibraryPreview?slug=" + cited.Slug));
            Assert.Contains("Tổng hợp từ <b>" + cited.SourceName + "</b>", citedPreview);
            Assert.Contains("Tóm tắt có trích nguồn", WebUtility.HtmlDecode(await c2.GetStringAsync("/WebsitePosts/Library")));
        }

        await using var viewer = AdminFactory(new MemoryAdminStore(), "WEBSITE.VIEW");
        Assert.NotEqual(HttpStatusCode.OK, (await AdminClient(viewer).GetAsync("/WebsitePosts/Library")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await AdminClient(viewer).GetAsync("/WebsitePosts/LibraryPreview?slug=" + first.Slug)).StatusCode);
    }

    [Fact]
    public void PostLibraryShipsACoverPerArticleAndSpreadsPublicationTimes()
    {
        Assert.All(S.PostLibrary.All, p =>
        {
            var cover = S.PostLibrary.Cover(p.Order);
            Assert.NotNull(cover);
            Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, cover![..3]);   // JPEG
            Assert.InRange(cover.Length, 10_000, 400_000);
        });
        var now = new DateTime(2026, 11, 17, 10, 0, 0);
        var schedule = S.PostLibrary.Schedule(S.PostLibrary.All, now);
        Assert.Equal(S.PostLibrary.All.Count, schedule.Count);
        Assert.Equal(schedule, S.PostLibrary.Schedule(S.PostLibrary.All, now));          // same input, same times
        Assert.True(schedule.Values.Distinct().Count() == schedule.Count);
        Assert.True(schedule.Values.Select(d => d.Date).Distinct().Count() >= 15);        // spread, not all on one day
        foreach (var p in S.PostLibrary.All)
        {
            var at = schedule[p.Slug];
            Assert.True(at <= now.AddHours(-2) && at >= now.Date.AddDays(-45), $"{p.Slug} {at}");
            Assert.True(at.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{p.Slug} {at}");
            Assert.InRange(at.TimeOfDay, new TimeSpan(7, 45, 0), new TimeSpan(17, 15, 0));
            if (p.Origin?.PublishedOn is DateTime original) Assert.True(at.Date > original.Date, $"{p.Slug}: {at} before original {original}");
        }
    }

    [Fact]
    public async Task ApplyLibraryExtrasAddsCoversAndSpreadsDates()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        var token = Token(await client.GetStringAsync("/WebsitePosts/Library"));
        await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token, ("publish", "true")));
        Assert.Equal(S.PostLibrary.All.Count, store.Media.Count);                         // new posts get their cover on import
        Assert.All(store.Posts.Values, p => Assert.NotNull(p.Input.CoverMediaId));
        Assert.Contains("Gắn ảnh bìa và rải ngày đăng", WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts/Library")));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WebsitePosts/ApplyLibraryExtras", Form(token))).StatusCode);
        Assert.All(store.Posts.Values, p => Assert.True(p.Input.Publish));
        var dates = store.Posts.Values.Select(p => p.Input.PublishedAtLocal).ToList();
        Assert.All(dates, d => Assert.NotNull(d));
        Assert.True(dates.Select(d => d!.Value.Date).Distinct().Count() >= 15);

        await using var viewer = AdminFactory(new MemoryAdminStore(), "WEBSITE.VIEW");
        var viewerClient = AdminClient(viewer);
        Assert.NotEqual(HttpStatusCode.Redirect, (await viewerClient.PostAsync("/WebsitePosts/ApplyLibraryExtras", Form(token))).StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, (await viewerClient.PostAsync("/WebsitePosts/ReplaceLibraryCovers", Form(token))).StatusCode);
    }

    [Fact]
    public async Task ReplaceLibraryCoversSwapsOnlyOldShippedCovers()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        var token = Token(await client.GetStringAsync("/WebsitePosts/Library"));
        await client.PostAsync("/WebsitePosts/ImportLibrary", Form(token, ("publish", "true")));
        Assert.All(store.Media, m => Assert.EndsWith($"-v{S.PostLibrary.CoverVersion}.jpg", m.FileName));
        // Pretend the covers came from the first cover set, and one article got a cover picked by staff.
        for (var i = 0; i < store.Media.Count; i++) store.Media[i] = store.Media[i] with { FileName = store.Media[i].FileName.Replace($"-v{S.PostLibrary.CoverVersion}", "") };
        var custom = Guid.NewGuid();
        store.Media.Add(new(custom, "anh-rieng.jpg", "image/jpeg", 10, 960, 540, null, DateTime.UtcNow));
        var keep = store.Posts.Values.First();
        keep.Input.CoverMediaId = custom;
        var before = store.Posts.Values.ToDictionary(p => p.Input.Slug!, p => (p.Input.CoverMediaId, p.Input.PublishedAtLocal));

        Assert.Contains("Đổi sang ảnh bìa mới", WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts/Library")));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WebsitePosts/ReplaceLibraryCovers", Form(token))).StatusCode);
        var names = store.Media.ToDictionary(m => m.Id, m => m.FileName);
        foreach (var p in store.Posts.Values)
        {
            Assert.Equal(before[p.Input.Slug!].PublishedAtLocal, p.Input.PublishedAtLocal);
            if (ReferenceEquals(p.Input, keep.Input)) { Assert.Equal(custom, p.Input.CoverMediaId); continue; }
            Assert.NotEqual(before[p.Input.Slug!].CoverMediaId, p.Input.CoverMediaId);
            Assert.EndsWith($"-v{S.PostLibrary.CoverVersion}.jpg", names[p.Input.CoverMediaId!.Value]);
        }
        var lib = S.PostLibrary.All[0];
        Assert.True(S.PostLibrary.IsOldCoverName(lib, $"bai-{lib.Order:00}-{lib.Slug}.jpg"));
        Assert.False(S.PostLibrary.IsOldCoverName(lib, S.PostLibrary.CoverFileName(lib)));
        Assert.False(S.PostLibrary.IsOldCoverName(lib, "anh-rieng.jpg"));
    }

    [Fact]
    public async Task NewsPageHeroShowsItsIllustration()
    {
        await using var f = SiteFactory(new MemoryReader());
        var client = f.CreateClient();
        var html = await client.GetStringAsync("/tin-tuc");
        Assert.Contains("class=\"dv-hero-art\"", html);
        Assert.Contains("/site-assets/news-hero.jpg", html);
        var image = await client.GetAsync("/site-assets/news-hero.jpg");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("dv-hero-art", await client.GetStringAsync("/lien-he"));      // other page heroes stay text-only
    }

    // ---- Soạn bài bằng AI ----------------------------------------------------------------------------------------
    private const string AiArticleJson = """
{"title":"Hạn nộp tờ khai thuế quý 4/2026","slug":"han-nop-to-khai-quy-4-2026","summary":"Tóm tắt hạn nộp.","category":"thue","tags":"thuế, hạn nộp",
 "seoTitle":"Hạn nộp tờ khai quý 4/2026","seoDescription":"Các mốc nộp tờ khai thuế quý 4/2026 doanh nghiệp nhỏ cần biết để tránh bị phạt chậm nộp.",
 "bodyHtml":"<p class=\"lead\">Mở đầu.</p><h2>Hạn nộp</h2><p>Nội dung <script>x</script></p><p><a href=\"/lien-he\">liên hệ T.A</a></p>",
 "sourceName":"Báo Điện tử Chính phủ","sourceAuthor":"","sourcePublishedOn":"2026-11-10","sources":["Nghị định 126/2020 – https://chinhphu.vn/a"],"uncertain":["Kiểm tra ngày nghỉ lễ"]}
""";

    private sealed class FakeAiProvider(string name, Func<int, string?> answer) : S.IWebsiteAiProvider
    {
        public int Calls;
        public string Name => name;
        public bool IsConfigured => true;
        public Task<(string Json, S.WebsiteAiUsage Usage)> WriteAsync(string system, string user, CancellationToken ct)
        {
            Calls++;
            var usage = new S.WebsiteAiUsage(name, name + "-model", true, 1000, 500, 0, 2, 1, 0.02m, false, 10, null);
            var json = answer(Calls);
            if (json is null) throw new S.WebsiteAiProviderException(usage with { Ok = false, Error = "credit balance too low" }, "Gọi Claude API không thành công.");
            return Task.FromResult((json, usage));
        }
    }

    private static (S.WebsiteAiWriter Writer, string LogPath) AiWriter(params S.IWebsiteAiProvider[] providers)
    {
        var log = Path.Combine(Path.GetTempPath(), "ta-ai-usage-" + Guid.NewGuid().ToString("N") + ".log");
        var options = new S.WebsiteAiOptions { UsageLogPath = log, GommoToken = "test" };
        var usage = new S.WebsiteAiUsageLog(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<S.WebsiteAiUsageLog>.Instance);
        return (new S.WebsiteAiWriter(providers, usage, new NoHttp(), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<S.WebsiteAiWriter>.Instance), log);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("tests must not call the network");
    }

    [Fact]
    public async Task AiWriterFallsBackTo79AiAndLogsEachProviderCall()
    {
        var store = new MemoryAdminStore();
        var claude = new FakeAiProvider("claude", _ => null);              // Claude API fails (e.g. no credit left)
        var gommo = new FakeAiProvider("79ai", _ => "```json\n" + AiArticleJson + "\n```");
        var (writer, log) = AiWriter(claude, gommo);
        var job = new S.WebsiteAiJobs().Enqueue(Actor, null, new S.WebsiteAiRequest(PostSource.Own, "Hạn nộp tờ khai quý 4/2026", null, null, null));
        await writer.RunAsync(job, store, CancellationToken.None);

        Assert.Equal(S.WebsiteAiJobState.Done, job.State);
        Assert.Equal((1, 1), (claude.Calls, gommo.Calls));
        var saved = Assert.Single(store.Posts.Values).Input;
        Assert.False(saved.Publish);                                        // AI never publishes
        Assert.Equal("han-nop-to-khai-quy-4-2026", saved.Slug);
        Assert.DoesNotContain("<script", saved.BodyHtml);
        Assert.Equal(PostSource.Own, saved.SourceKind);                     // topic article: no source box
        Assert.Equal(["Kiểm tra ngày nghỉ lễ"], job.Uncertain);
        var lines = File.ReadAllLines(log);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"provider\":\"claude\"", lines[0]);
        Assert.Contains("\"ok\":false", lines[0]);
        Assert.Contains("\"provider\":\"79ai\"", lines[1]);
        Assert.Contains("\"ok\":true", lines[1]);
        Assert.Contains("\"input_tokens\":1000", lines[1]);
        File.Delete(log);

        // Claude answers: 79AI is not called; a second article on the same topic gets its own slug.
        var claudeOk = new FakeAiProvider("claude", _ => AiArticleJson);
        var unused = new FakeAiProvider("79ai", _ => AiArticleJson);
        (writer, log) = AiWriter(claudeOk, unused);
        var second = new S.WebsiteAiJobs().Enqueue(Actor, null, new S.WebsiteAiRequest(PostSource.Own, "Hạn nộp tờ khai quý 4/2026", null, null, null));
        await writer.RunAsync(second, store, CancellationToken.None);
        Assert.Equal(S.WebsiteAiJobState.Done, second.State);
        Assert.Equal(0, unused.Calls);
        Assert.Contains(store.Posts.Values, p => p.Input.Slug == "han-nop-to-khai-quy-4-2026-2");
        Assert.Single(File.ReadAllLines(log));
        File.Delete(log);

        // Both fail: the job fails with a readable message and nothing is saved.
        (writer, log) = AiWriter(new FakeAiProvider("claude", _ => null), new FakeAiProvider("79ai", _ => "xin lỗi, không có JSON"));
        var failed = new S.WebsiteAiJobs().Enqueue(Actor, null, new S.WebsiteAiRequest(PostSource.Own, "Chủ đề bất kỳ dài hơn mười ký tự", null, null, null));
        await writer.RunAsync(failed, store, CancellationToken.None);
        Assert.Equal(S.WebsiteAiJobState.Failed, failed.State);
        Assert.Contains("JSON", failed.Error);
        Assert.Equal(2, store.Posts.Count);
        File.Delete(log);
    }

    [Fact]
    public void AiHelpersParseAnswersTokensAndRefusePrivateAddresses()
    {
        var a = S.WebsiteAiPrompt.Parse("Đây là bài:\n" + AiArticleJson);
        Assert.Equal("thue", a.Category);
        Assert.Equal(new DateTime(2026, 11, 10), a.SourcePublishedOn);
        Assert.Null(a.SourceAuthor);
        Assert.Throws<S.WebsiteAiException>(() => S.WebsiteAiPrompt.Parse("{\"title\":\"\"}"));
        Assert.Contains("Nội dung chính", S.WebsiteAiPrompt.PageText("<html><script>bad()</script><nav>menu</nav><h1>Tiêu đề</h1><p>Nội dung chính &amp; phụ</p></html>"));
        Assert.DoesNotContain("bad()", S.WebsiteAiPrompt.PageText("<script>bad()</script><p>x</p>"));
        Assert.Equal((1200L, 340L, true), S.GommoWebsiteAiProvider.ReportedTokens(System.Text.Json.JsonDocument.Parse("""{"data":{"usage":{"prompt_tokens":1200,"completion_tokens":340}}}""").RootElement));
        Assert.False(S.GommoWebsiteAiProvider.ReportedTokens(System.Text.Json.JsonDocument.Parse("""{"data":{"text":"x"}}""").RootElement).Reported);
        foreach (var ip in new[] { "127.0.0.1", "10.1.2.3", "192.168.1.5", "172.20.0.1", "169.254.1.1", "::1" }) Assert.True(S.WebsiteAiWriter.IsPrivate(IPAddress.Parse(ip)), ip);
        Assert.False(S.WebsiteAiWriter.IsPrivate(IPAddress.Parse("113.160.249.61")));
        Assert.Contains("thue", S.WebsiteAiPrompt.System(new DateTime(2026, 11, 17)));
        var opt = new S.WebsiteAiOptions();   // Opus 5.5 list price: $4 / $20 per million tokens, $10 per 1,000 searches
        Assert.Equal(4m, opt.Cost(1_000_000, 0));
        Assert.Equal(20m, opt.Cost(0, 1_000_000));
        Assert.Equal(0.02m, opt.Cost(0, 0, 0, 2));
    }

    [Fact]
    public async Task AiCreatePageQueuesAJobForTheEditorOnly()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, s =>
        {
            s.RemoveAll<S.WebsiteAiOptions>();
            s.AddSingleton(new S.WebsiteAiOptions { GommoToken = "test", UsageLogPath = "" });
            s.RemoveAll<S.IWebsiteAiProvider>();
            s.AddSingleton<S.IWebsiteAiProvider>(new FakeAiProvider("claude", _ => AiArticleJson));
        }, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        Assert.Contains("Soạn bài bằng AI", WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts")));
        var page = await client.GetStringAsync("/WebsitePosts/AiCreate");
        Assert.Contains("Tóm tắt có trích nguồn", WebUtility.HtmlDecode(page));
        var token = Token(page);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/WebsitePosts/AiCreate", Form(token, ("Form.Kind", "0"), ("Form.Topic", "ngắn")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/WebsitePosts/AiCreate", Form(token, ("Form.Kind", "1"), ("Form.SourceUrl", "javascript:alert(1)")))).StatusCode);
        var queued = await client.PostAsync("/WebsitePosts/AiCreate", Form(token, ("Form.Kind", "0"), ("Form.Topic", "Hạn nộp tờ khai thuế quý 4/2026")));
        Assert.Equal(HttpStatusCode.Redirect, queued.StatusCode);
        var status = queued.Headers.Location!.ToString();
        Assert.Contains("/WebsitePosts/AiStatus/", status);
        string html = "";
        for (var i = 0; i < 50 && !html.Contains("Đã soạn xong"); i++) { await Task.Delay(100); html = WebUtility.HtmlDecode(await client.GetStringAsync(status)); }
        Assert.Contains("Đã soạn xong bản nháp", html);
        Assert.Contains("Kiểm tra ngày nghỉ lễ", html);
        Assert.DoesNotContain("claude", html, StringComparison.OrdinalIgnoreCase);   // which provider answered stays in the server log
        Assert.Single(store.Posts);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/WebsitePosts/AiStatus/" + Guid.NewGuid())).StatusCode);

        await using var viewer = AdminFactory(new MemoryAdminStore(), "WEBSITE.VIEW");
        Assert.NotEqual(HttpStatusCode.OK, (await AdminClient(viewer).GetAsync("/WebsitePosts/AiCreate")).StatusCode);
    }

    // ---- Public website (fake reader) -----------------------------------------------------------------------------
    [Fact]
    public async Task PublicSiteRendersPagesFromContentAndKeepsLinksSafe()
    {
        var reader = new MemoryReader();
        reader.Overrides[("home", "hero")] = new JsonObject { ["title"] = "Dòng 1\nDòng <2>", ["primaryLabel"] = "GỌI", ["primaryUrl"] = "javascript:alert(1)" };
        await using var f = SiteFactory(reader);
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var homeResponse = await client.GetAsync("/");
        Assert.Contains("no-cache", homeResponse.Headers.CacheControl!.ToString()); // edits show on the next load
        var home = await homeResponse.Content.ReadAsStringAsync();
        Assert.Contains("Dòng 1<br/>Dòng &lt;2&gt;", home);
        Assert.DoesNotContain("javascript:", home);
        Assert.Contains("href=\"/lien-he\">GỌI</a>", home); // unsafe link falls back to the contact page
        Assert.Contains("noindex", home); // indexing off until the operator enables it
        Assert.Contains("Lô NV-14_11", home);
        var contact = await client.GetStringAsync("/lien-he?dv=ke-toan");
        Assert.Matches("value=\"Kế toán – thuế trọn gói\" data-code=\"ke-toan\" checked", contact);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tin-tuc/khong-co")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/tin-tuc")).StatusCode);
        Assert.Contains("Disallow: /", await client.GetStringAsync("/robots.txt"));
        Assert.Equal("image/webp", (await client.GetAsync("/site-assets/service-1.webp")).Content.Headers.ContentType!.MediaType);
        foreach (var (url, nav) in new[] { ("/gioi-thieu", "GIỚI THIỆU"), ("/dich-vu", "DỊCH VỤ"), ("/dich-vu/thanh-lap-doanh-nghiep", "DỊCH VỤ"), ("/dich-vu/ke-toan-thue/", "DỊCH VỤ") })
        {
            var page = await client.GetStringAsync(url);
            Assert.Matches($"class=\"is-current\"[^>]*>(?:(?!</a>).)*<span>{nav}</span>", page);
        }
        Assert.Contains("/css/gioi-thieu.css", await client.GetStringAsync("/gioi-thieu"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/dich-vu/khong-co")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/global")).StatusCode);
    }

    [Fact]
    public async Task PublicArticleIsSanitizedAndHasATableOfContents()
    {
        var reader = new MemoryReader();
        reader.Post = new PostDetail(Guid.NewGuid(), "bai-mau", "Bài mẫu", "Tóm tắt", "<h2>Mục một</h2><p>x</p><script>alert(1)</script>", null, null, null, null, null,
            "Ban chuyên môn", "thuế, hóa đơn", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, 3);
        await using var f = SiteFactory(reader);
        var client = f.CreateClient();
        var html = await client.GetStringAsync("/tin-tuc/bai-mau");
        Assert.Contains("<h2 id=\"muc-1\">Mục một</h2>", html);
        Assert.Contains("href=\"#muc-1\"", html);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("\"@type\":\"Article\"", html);
        Assert.DoesNotContain("ar-source", html); // T.A's own article: no source box
    }

    [Fact]
    public async Task SummarisedArticleShowsItsSourceAndNeverAnUnsafeLink()
    {
        var reader = new MemoryReader();
        reader.Post = new PostDetail(Guid.NewGuid(), "bai-tom-tat", "Bài tóm tắt", null, "<p>Nội dung tóm tắt</p>", null, null, null, null, null,
            null, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, 0,
            new PostSource(PostSource.Summary, "Tạp chí Thuế", "https://thuenhanuoc.vn/bai-goc", "Nguyễn Văn A", new DateTime(2026, 11, 10)));
        await using var f = SiteFactory(reader);
        var client = f.CreateClient();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/tin-tuc/bai-tom-tat"));
        Assert.Contains("Tổng hợp từ <b>Tạp chí Thuế</b>", html);
        Assert.Contains("class=\"ar-source\"", html);
        Assert.Contains("href=\"https://thuenhanuoc.vn/bai-goc\" target=\"_blank\" rel=\"noopener nofollow\"", html);
        Assert.Contains("tác giả Nguyễn Văn A, đăng ngày 10/11/2026", html);

        reader.Post = reader.Post with { Source = new PostSource(PostSource.Republished, "Báo X", "javascript:alert(1)", null, null) };
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/tin-tuc/bai-tom-tat"));
        Assert.Contains("Đăng lại từ <b>Báo X</b>", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void PostSourceFieldsAreRequiredOnlyForSummariesAndRepublished()
    {
        var own = new S.WebsitePostInput { Title = "A", SourceKind = PostSource.Own, SourceName = "x", SourceUrl = "bad" };
        Assert.Empty(own.Normalize());
        Assert.Null(own.SourceName); // cleared for T.A's own articles
        var missing = new S.WebsitePostInput { Title = "A", SourceKind = PostSource.Summary };
        Assert.Contains(missing.Normalize(), e => e.Field == "SourceName");
        Assert.Contains(missing.Normalize(), e => e.Field == "SourceUrl");
        var badUrl = new S.WebsitePostInput { Title = "A", SourceKind = PostSource.Summary, SourceName = "Tạp chí Thuế", SourceUrl = "javascript:alert(1)" };
        Assert.Contains(badUrl.Normalize(), e => e.Field == "SourceUrl");
        var ok = new S.WebsitePostInput { Title = "A", SourceKind = PostSource.Republished, SourceName = " Tạp chí Thuế ", SourceUrl = "https://thuenhanuoc.vn/a", SourceAuthor = " " };
        Assert.Empty(ok.Normalize());
        Assert.Equal("Tạp chí Thuế", ok.SourceName);
        Assert.Null(ok.SourceAuthor);
        var unknown = new S.WebsitePostInput { Title = "A", SourceKind = 9, SourceName = "x" };
        Assert.Empty(unknown.Normalize());
        Assert.Equal(PostSource.Own, unknown.SourceKind);
    }

    [Fact]
    public async Task PostEditorSavesAndReloadsTheSource()
    {
        var store = new MemoryAdminStore();
        await using var f = AdminFactory(store, "WEBSITE.VIEW", "WEBSITE.POSTS.EDIT");
        var client = AdminClient(f);
        var form = await client.GetStringAsync("/WebsitePosts/Create");
        Assert.Contains("Tóm tắt có trích nguồn", WebUtility.HtmlDecode(form));
        var refused = await client.PostAsync("/WebsitePosts/Create", Form(Token(form), ("Input.Title", "Tóm tắt công văn"), ("Input.SourceKind", "1")));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Empty(store.Posts);
        var saved = await client.PostAsync("/WebsitePosts/Create", Form(Token(form), ("Input.Title", "Tóm tắt công văn"), ("Input.BodyHtml", "<p>x</p>"),
            ("Input.SourceKind", "1"), ("Input.SourceName", "Cục Thuế"), ("Input.SourceUrl", "https://gdt.gov.vn/cv"), ("Input.SourcePublishedOn", "2026-11-12")));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var post = store.Posts.Values.Single().Input;
        Assert.Equal(PostSource.Summary, post.SourceKind);
        Assert.Equal(new DateTime(2026, 11, 12), post.SourcePublishedOn);
        var edit = WebUtility.HtmlDecode(await client.GetStringAsync(saved.Headers.Location!.ToString()));
        Assert.Contains("value=\"https://gdt.gov.vn/cv\"", edit);
        var preview = WebUtility.HtmlDecode(await client.GetStringAsync("/WebsitePosts/Preview/" + store.Posts.Keys.Single()));
        Assert.Contains("Tổng hợp từ <b>Cục Thuế</b>", preview);
    }

    [Fact]
    public async Task QuoteFormValidatesAndStoresLeads()
    {
        var reader = new MemoryReader();
        await using var f = SiteFactory(reader);
        var client = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = Token(await client.GetStringAsync("/lien-he"));
        var bad = await client.PostAsync("/lien-he", Form(token, ("name", "An"), ("phone", "123"), ("consent", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var noConsent = await client.PostAsync("/lien-he", Form(token, ("name", "An"), ("phone", "0905123456")));
        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        var bot = await client.PostAsync("/lien-he", Form(token, ("name", "An"), ("phone", "0905123456"), ("consent", "true"), ("website", "spam")));
        Assert.Equal(HttpStatusCode.OK, bot.StatusCode);
        Assert.Empty(reader.Leads);
        var ok = await client.PostAsync("/lien-he", Form(token, ("name", "Nguyễn An"), ("phone", "0905 123 456"), ("consent", "true"), ("service", "Kế toán – thuế trọn gói"),
            ("service", "Hộ kinh doanh"), ("type", "Mới thành lập"), ("volume", "30 – 100"), ("industry", "Thương mại")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var lead = Assert.Single(reader.Leads);
        Assert.Equal("Kế toán – thuế trọn gói, Hộ kinh doanh", lead.Services);
        Assert.Equal("Ngành: Thương mại · Chứng từ/tháng: 30 – 100", lead.Scale);
        var noToken = await f.CreateClient().PostAsync("/lien-he", new FormUrlEncodedContent([new("name", "x")]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
    }

    private static S.WebsiteLeadItem SampleLead(string name, byte status) => new(Guid.NewGuid(), DateTime.UtcNow, name, "0905123456", "a@b.vn", "=HYPERLINK(\"x\")",
        "Kế toán – thuế trọn gói", "Mới thành lập", null, null, "Xin báo giá <b>gấp</b>", "/lien-he", status, null, null, [1]);

    [Fact]
    public async Task DashboardSummarisesWebsiteLeadsOnlyForUsersWhoMaySeeThem()
    {
        var store = new MemoryAdminStore();
        store.Leads.Add(SampleLead("Trần Bình", 0));
        store.Leads.Add(SampleLead("Lê Châu", 3));
        await using (var f = AdminFactory(store, AdminSecurity.AdminPermissions.DashboardView, AdminSecurity.AdminPermissions.WebsiteLeadsView))
        {
            var html = WebUtility.HtmlDecode(await AdminClient(f).GetStringAsync("/"));
            Assert.Contains("data-ta-dashboard-leads", html);
            Assert.Contains("Yêu cầu báo giá từ website", html);
            Assert.Contains("Trần Bình", html);
            Assert.Contains("Chưa xử lý", html);
            Assert.Contains("/WebsiteLeads/Details/" + store.Leads[0].Id, html);
        }
        await using (var f = AdminFactory(store, AdminSecurity.AdminPermissions.DashboardView))
        {
            var html = WebUtility.HtmlDecode(await AdminClient(f).GetStringAsync("/"));
            Assert.DoesNotContain("data-ta-dashboard-leads", html);
            Assert.DoesNotContain("Trần Bình", html);
        }
    }

    [Fact]
    public async Task LeadsExportIsAuditedAndProducesAWorkbookWithoutFormulas()
    {
        var store = new MemoryAdminStore();
        store.Leads.Add(SampleLead("Trần Bình", 0));
        await using var f = AdminFactory(store, AdminSecurity.AdminPermissions.WebsiteLeadsView);
        var client = AdminClient(f);
        Assert.Contains("Xuất Excel", await client.GetStringAsync("/WebsiteLeads"));
        var response = await client.GetAsync("/WebsiteLeads/Export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(S.XlsxWriter.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, store.Exports);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var sheet = Assert.Single(S.XlsxReader.Read(new MemoryStream(bytes)));
        Assert.Equal("Yêu cầu báo giá", sheet.Name);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("Trần Bình", xml);
        Assert.Contains("=HYPERLINK(&quot;x&quot;)", xml);
        Assert.DoesNotContain("<f>", xml);
        Assert.Contains("Xin báo giá &lt;b&gt;gấp&lt;/b&gt;", xml);
        await using var noAccess = AdminFactory(store, AdminSecurity.AdminPermissions.WebsiteView);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminClient(noAccess).GetAsync("/WebsiteLeads/Export")).StatusCode);
    }

    [Fact]
    public void XlsxColumnNamesFollowExcel()
    {
        Assert.Equal("A", S.XlsxWriter.Column(0));
        Assert.Equal("Z", S.XlsxWriter.Column(25));
        Assert.Equal("AA", S.XlsxWriter.Column(26));
        Assert.Equal("AM", S.XlsxWriter.Column(38));
    }

    [Fact]
    public async Task PublicPagesShowNoMockupNotesAndMarkMissingDataForTA()
    {
        await using var f = SiteFactory(new MemoryReader());
        var client = f.CreateClient();
        foreach (var page in SiteMap.Pages.Where(p => p.Path is not null && !p.Path.Contains('{')))
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync(page.Path));
            Assert.DoesNotContain("mockup", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("khi triển khai", html, StringComparison.OrdinalIgnoreCase);
        }
        var contact = WebUtility.HtmlDecode(await client.GetStringAsync("/lien-he"));
        Assert.Contains("<span class=\"ta-todo\">T.A bổ sung số hotline</span>", contact);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))]);

    private static MultipartFormDataContent Upload(string token, string fileName, byte[] data)
    {
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return content;
    }

    private static HttpClient AdminClient(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> AdminFactory(MemoryAdminStore store, params string[] permissions) => AdminFactory(store, _ => { }, permissions);

    private static WebApplicationFactory<admin::Program> AdminFactory(MemoryAdminStore store, Action<IServiceCollection> extra, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IWebsiteAdminStore>();
                s.AddSingleton<S.IWebsiteAdminStore>(store);
                s.AddSingleton(new TestPrincipal("STAFF", permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
                extra(s);
            }));

    private static WebApplicationFactory<website::Program> SiteFactory(MemoryReader reader) =>
        new WebApplicationFactory<website::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<IWebsiteReader>();
                s.AddSingleton<IWebsiteReader>(reader);
            }));

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-website-test"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }

    // In-memory admin store. The actor is treated as holding the permission of each call (the SQL store re-checks it).
    private sealed class MemoryAdminStore : S.IWebsiteAdminStore
    {
        public readonly Dictionary<(string Page, string Section), JsonObject> Sections = [];
        public readonly List<S.WebsiteMediaItem> Media = [];
        public readonly Dictionary<Guid, (S.WebsitePostInput Input, byte[] Stamp)> Posts = [];

        private static S.WebsiteResult Ok(Guid? id = null) => new(S.WebsiteOutcome.Saved, id);

        public Task<S.WebsiteOverview> OverviewAsync() => Task.FromResult(new S.WebsiteOverview(Posts.Count, 0, Media.Count, 0, 0, Sections.Count, true));
        public Task<IReadOnlyList<RenderedPage>> PagesAsync() => Task.FromResult<IReadOnlyList<RenderedPage>>(SiteMap.Pages.Select(p => Compose(p.Key).Page).ToList());
        public Task<S.WebsitePageEditor> PageAsync(string pageKey) => Task.FromResult(Compose(pageKey));

        private S.WebsitePageEditor Compose(string key)
        {
            var rows = Sections.Where(s => s.Key.Page == key).Select(s => new SectionRow(key, s.Key.Section, s.Value.ToJsonString(), 0, true, DateTime.UtcNow, [1])).ToList();
            return new(PageComposer.Compose(SiteMap.Get(key), null, rows), null, rows.ToDictionary(r => r.SectionKey));
        }

        public Task<S.WebsiteResult> SaveSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey, JsonObject content, string? stamp)
        { Sections[(pageKey, sectionKey)] = content; return Task.FromResult(Ok()); }
        public Task<S.WebsiteResult> ResetSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey) { Sections.Remove((pageKey, sectionKey)); return Task.FromResult(Ok()); }
        public Task<S.WebsiteResult> SaveLayoutAsync(Guid actor, string? ip, string pageKey, IReadOnlyList<(string Key, int Order, bool Visible)> layout) => Task.FromResult(Ok());
        public Task<S.WebsiteResult> SavePageSeoAsync(Guid actor, string? ip, string pageKey, string? title, string? description, bool published) => Task.FromResult(Ok());
        public Task<SiteSettings> SettingsAsync() => Task.FromResult(SiteSettings.Defaults);
        public Task<S.WebsiteResult> SaveSettingsAsync(Guid actor, string? ip, IReadOnlyDictionary<string, string?> values) => Task.FromResult(Ok());
        public Task<IReadOnlyList<S.WebsiteMediaItem>> MediaAsync(string? search, int take, TAWebsite.Core.MediaKind? kind = null) =>
            Task.FromResult<IReadOnlyList<S.WebsiteMediaItem>>(Media.Where(m => kind is null || m.Kind == kind).ToList());
        public Task<MediaFile?> MediaFileAsync(Guid id) => Task.FromResult<MediaFile?>(null);
        public Task<S.WebsiteResult> UploadMediaAsync(Guid actor, string? ip, string fileName, string contentType, byte[] data, int? width, int? height, string? alt)
        {
            var id = Guid.NewGuid();
            Media.Add(new(id, fileName, contentType, data.Length, width, height, alt, DateTime.UtcNow));
            return Task.FromResult(Ok(id));
        }
        public Task<S.WebsiteResult> AddMediaLinkAsync(Guid actor, string? ip, string title, string url, string? alt)
        {
            var id = Guid.NewGuid();
            Media.Add(new(id, title, TAWebsite.Core.MediaKinds.YouTube, 0, null, null, alt, DateTime.UtcNow, url));
            return Task.FromResult(Ok(id));
        }
        public Task<S.WebsiteResult> UpdateMediaAltAsync(Guid actor, string? ip, Guid id, string? alt) => Task.FromResult(Ok(id));
        public Task<S.WebsiteResult> DeleteMediaAsync(Guid actor, string? ip, Guid id) => Task.FromResult(Ok(id));
        public Task<IReadOnlyList<PostCategory>> CategoriesAsync() => Task.FromResult<IReadOnlyList<PostCategory>>([new(Guid.NewGuid(), "ke-toan-thue", "Kế toán – thuế", 10, 0)]);
        public Task<S.WebsiteResult> SaveCategoryAsync(Guid actor, string? ip, Guid? id, string name, string slug, int sortOrder) => Task.FromResult(Ok());
        public Task<S.WebsiteResult> DeleteCategoryAsync(Guid actor, string? ip, Guid id) => Task.FromResult(Ok());
        public Task<IReadOnlyList<S.WebsitePostListItem>> PostsAsync(string? status, Guid? category, string? search) =>
            Task.FromResult<IReadOnlyList<S.WebsitePostListItem>>(Posts.Select(p => new S.WebsitePostListItem(p.Key, p.Value.Input.Slug!, p.Value.Input.Title!, null,
                (byte)(p.Value.Input.Publish ? 1 : 0), null, false, 0, DateTime.UtcNow, p.Value.Input.CoverMediaId)).ToList());
        public Task<S.WebsitePostEdit?> PostAsync(Guid id) => Task.FromResult(Posts.TryGetValue(id, out var p)
            ? new S.WebsitePostEdit(id, p.Input.Slug!, p.Input.Title!, p.Input.Summary, p.Input.BodyHtml, p.Input.CoverMediaId, null, null, null, (byte)(p.Input.Publish ? 1 : 0), null, false,
                null, null, 0, DateTime.UtcNow, DateTime.UtcNow, p.Stamp,
                PostSource.From(p.Input.SourceKind, p.Input.SourceName, p.Input.SourceUrl, p.Input.SourceAuthor, p.Input.SourcePublishedOn)) : null);
        public Task<S.WebsiteResult> SavePostAsync(Guid actor, string? ip, Guid? id, S.WebsitePostInput input, DateTime nowUtc)
        {
            var key = id ?? Guid.NewGuid();
            Posts[key] = (input, [1]);
            return Task.FromResult(Ok(key));
        }
        public Task<S.WebsiteResult> DeletePostAsync(Guid actor, string? ip, Guid id) { Posts.Remove(id); return Task.FromResult(Ok(id)); }
        public readonly List<S.WebsiteLeadItem> Leads = [];
        public int Exports;
        public Task<IReadOnlyList<S.WebsiteLeadItem>> LeadsAsync(byte? status, string? search) => Task.FromResult<IReadOnlyList<S.WebsiteLeadItem>>(Leads);
        public Task<IReadOnlyList<S.WebsiteLeadItem>> LeadsForExportAsync(byte? status, string? search) => Task.FromResult<IReadOnlyList<S.WebsiteLeadItem>>(Leads);
        public Task<S.WebsiteLeadSummary?> LeadSummaryAsync(DateTime nowUtc) =>
            Task.FromResult<S.WebsiteLeadSummary?>(new(Leads.Count(l => l.Status == 0), Leads.Count, Leads.Count, Leads.Count, Leads.Take(6).ToList()));
        public Task<S.WebsiteResult> AuditLeadsExportAsync(Guid actor, string? ip, byte? status, string? search) { Exports++; return Task.FromResult(Ok()); }
        public Task<S.WebsiteLeadItem?> LeadAsync(Guid id) => Task.FromResult<S.WebsiteLeadItem?>(null);
        public Task<S.WebsiteResult> UpdateLeadAsync(Guid actor, string? ip, Guid id, byte status, string? note, string? stamp) => Task.FromResult(Ok(id));
    }

    private sealed class MemoryReader : IWebsiteReader
    {
        public readonly Dictionary<(string Page, string Section), JsonObject> Overrides = [];
        public readonly List<LeadInput> Leads = [];
        public PostDetail? Post;

        public Task<RenderedPage> PageAsync(string pageKey) => Task.FromResult(PageComposer.Compose(SiteMap.Get(pageKey), null,
            Overrides.Where(o => o.Key.Page == pageKey).Select(o => new SectionRow(pageKey, o.Key.Section, o.Value.ToJsonString(), 0, true, DateTime.UtcNow)).ToList()));
        public Task<SiteSettings> SettingsAsync() => Task.FromResult(SiteSettings.Defaults);
        public Task<IReadOnlyList<PostCategory>> CategoriesAsync() => Task.FromResult<IReadOnlyList<PostCategory>>([]);
        public Task<PostPage> PostsAsync(PostQuery query) => Task.FromResult(new PostPage([], 0, query.Page, query.PageSize));
        public Task<IReadOnlyList<PostCard>> LatestAsync(int count) => Task.FromResult<IReadOnlyList<PostCard>>([]);
        public Task<IReadOnlyList<PostCard>> PopularAsync(int count) => Task.FromResult<IReadOnlyList<PostCard>>([]);
        public Task<PostDetail?> PostAsync(string slug) => Task.FromResult(Post?.Slug == slug ? Post : null);
        public Task<IReadOnlyList<PostCard>> RelatedAsync(PostDetail post, int count) => Task.FromResult<IReadOnlyList<PostCard>>([]);
        public Task<(PostCard? Previous, PostCard? Next)> NeighboursAsync(PostDetail post) => Task.FromResult<(PostCard?, PostCard?)>((null, null));
        public Task<MediaFile?> MediaAsync(Guid id) => Task.FromResult<MediaFile?>(null);
        public Task CountViewAsync(Guid postId) => Task.CompletedTask;
        public Task<LeadOutcome> AddLeadAsync(LeadInput lead, string? clientIp) { Leads.Add(lead); return Task.FromResult(LeadOutcome.Saved); }
    }
}
