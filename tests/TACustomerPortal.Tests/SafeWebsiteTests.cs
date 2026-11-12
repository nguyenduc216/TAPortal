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
            Assert.All(Regex.Matches(html, "src=\"/site-assets/([^\"]+)\"").Select(m => m.Groups[1].Value), a => Assert.True(SiteAssets.Exists(a), a));
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
            Assert.Contains("Mẫu email</span>", editor);                                   // sidebar group "Hóa đơn tự động"
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

    private static WebApplicationFactory<admin::Program> AdminFactory(MemoryAdminStore store, params string[] permissions) =>
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
        public Task<IReadOnlyList<S.WebsiteMediaItem>> MediaAsync(string? search, int take) => Task.FromResult<IReadOnlyList<S.WebsiteMediaItem>>(Media);
        public Task<MediaFile?> MediaFileAsync(Guid id) => Task.FromResult<MediaFile?>(null);
        public Task<S.WebsiteResult> UploadMediaAsync(Guid actor, string? ip, string fileName, string contentType, byte[] data, int? width, int? height, string? alt)
        {
            var id = Guid.NewGuid();
            Media.Add(new(id, fileName, contentType, data.Length, width, height, alt, DateTime.UtcNow));
            return Task.FromResult(Ok(id));
        }
        public Task<S.WebsiteResult> UpdateMediaAltAsync(Guid actor, string? ip, Guid id, string? alt) => Task.FromResult(Ok(id));
        public Task<S.WebsiteResult> DeleteMediaAsync(Guid actor, string? ip, Guid id) => Task.FromResult(Ok(id));
        public Task<IReadOnlyList<PostCategory>> CategoriesAsync() => Task.FromResult<IReadOnlyList<PostCategory>>([new(Guid.NewGuid(), "ke-toan-thue", "Kế toán – thuế", 10, 0)]);
        public Task<S.WebsiteResult> SaveCategoryAsync(Guid actor, string? ip, Guid? id, string name, string slug, int sortOrder) => Task.FromResult(Ok());
        public Task<S.WebsiteResult> DeleteCategoryAsync(Guid actor, string? ip, Guid id) => Task.FromResult(Ok());
        public Task<IReadOnlyList<S.WebsitePostListItem>> PostsAsync(string? status, Guid? category, string? search) =>
            Task.FromResult<IReadOnlyList<S.WebsitePostListItem>>(Posts.Select(p => new S.WebsitePostListItem(p.Key, p.Value.Input.Slug!, p.Value.Input.Title!, null,
                (byte)(p.Value.Input.Publish ? 1 : 0), null, false, 0, DateTime.UtcNow, null)).ToList());
        public Task<S.WebsitePostEdit?> PostAsync(Guid id) => Task.FromResult(Posts.TryGetValue(id, out var p)
            ? new S.WebsitePostEdit(id, p.Input.Slug!, p.Input.Title!, p.Input.Summary, p.Input.BodyHtml, null, null, null, null, (byte)(p.Input.Publish ? 1 : 0), null, false,
                null, null, 0, DateTime.UtcNow, DateTime.UtcNow, p.Stamp) : null);
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
