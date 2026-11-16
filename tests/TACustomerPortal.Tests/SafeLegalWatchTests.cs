extern alias admin;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// LEGAL-WATCH ("Văn bản mới cần duyệt", operator 2026-11-14). No network and no SQL host: the Công báo / vbpl.vn readers
// run on page fragments copied from the real sites, the job runs on in-memory fakes, and the pages run with fake stores.
// The SQL store and the live sites are NOT exercised here; migration 048 is checked for its guards.
public sealed class SafeLegalWatchTests
{
    private const string TestScheme = "SafeLegalWatchTest";
    private static readonly Guid Actor = Guid.Parse("62222222-2222-2222-2222-222222222222");
    private static readonly Guid Doc = Guid.Parse("d8000000-0000-0000-0000-0000000000d8");
    private const string Page = "https://congbao.chinhphu.vn/van-ban/thong-tu-so-21-2026-tt-btc-470950.htm";
    private const string Word = "https://g7.cdnchinhphu.vn/api/download/stream?Url=abc&file_name=2026_21%2f2026%2fTT-BTC.docx";

    // ---- Migration ----------------------------------------------------------------------------------------------
    [Fact]
    public void Migration048FollowsTheMigrationRules()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "048-legal-watch.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "085-verify-legal-watch.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "086-rollback-legal-watch.sql"));
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.DoesNotContain("CREATE OR ALTER", migration + verify + rollback);
        Assert.DoesNotContain("JSON_", migration + verify + rollback);                // SQL Server 2014
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|INSERT)\b", migration);         // schema only, no row changed
        Assert.Contains("SET XACT_ABORT ON", migration);
        Assert.Contains("UX_LegalWatchRuns_Period", migration);
        Assert.Contains("UX_LegalWatchItems_New ON dbo.LegalWatchItems(DocumentNo) WHERE Kind = 'NEW'", migration);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.Contains("VERIFY_STATUS=", verify);
        Assert.Contains("THROW 51002", rollback);                                      // refuses while decisions exist
        foreach (var name in new[] { "LegalWatchRuns", "LegalWatchItems", "SourceUrl", "EffectStatus", "StatusCheckedAt" })
        {
            Assert.Contains(name, migration);
            Assert.Contains(name, verify);
            Assert.Contains(name, rollback);
        }
        // The library store keeps explicit column lists, so the new nullable columns never change what it reads.
        var library = File.ReadAllText(Path.Combine(root, "src", "TAPortal.Web", "Services", "LegalLibraryStore.cs"));
        Assert.DoesNotContain("SELECT *", library);
    }

    // ---- Readers ------------------------------------------------------------------------------------------------
    [Fact]
    public void TheFeedKeepsOnlyCongBaoDocumentLinksAndPicksTaxEntries()
    {
        var entries = S.CongBaoParser.Feed(Feed);
        Assert.Equal(3, entries.Count);                                                // the foreign link is dropped
        Assert.True(S.CongBaoParser.Worth(entries[0]));                                // "thuế" in the summary
        Assert.True(S.CongBaoParser.Worth(entries[1]));                                // a -btc- number
        Assert.False(S.CongBaoParser.Worth(entries[2]));                               // neither
        Assert.Throws<System.Xml.XmlException>(() => S.CongBaoParser.Feed("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><rss>&e;</rss>"));
    }

    [Fact]
    public void TheDocumentPageGivesNumberDatesAgencyWordFileAndRelations()
    {
        var c = S.CongBaoParser.Detail(DetailPage(TaxSummary), Page)!;
        Assert.Equal("21/2026/TT-BTC", c.DocumentNo);
        Assert.Equal("CIRCULAR", c.DocType);
        Assert.Equal("Thông tư", c.TypeName);
        Assert.Equal("Bộ Tài Chính", c.Agency);
        Assert.Equal(new DateTime(2026, 11, 2), c.IssuedDate);
        Assert.Equal(new DateTime(2026, 12, 20), c.EffectiveDate);
        Assert.Equal(Word, c.FileUrl);
        Assert.Contains("thuế", c.Keywords);
        Assert.Equal("AMENDS:80/2021/TT-BTC;GUIDES:126/2020/NĐ-CP", c.Relations);
        Assert.StartsWith("Thông tư 21/2026/TT-BTC – Sửa đổi", c.LibraryTitle());
        Assert.True(c.LibraryTitle().Length <= 300);
        // A Word link on another host is not kept; a page without number is not a document.
        Assert.Null(S.CongBaoParser.Detail(DetailPage(TaxSummary).Replace("https://g7.cdnchinhphu.vn", "https://evil.example"), Page)!.FileUrl);
        Assert.Null(S.CongBaoParser.Detail("<html><body>Không có văn bản</body></html>", Page));
    }

    [Fact]
    public void RelationsTakeTheNearestVerbAndNoteListsThem()
    {
        var r = S.LegalWatchPolicy.Relations("Thay thế Nghị định số 123/2020/NĐ-CP và bãi bỏ Thông tư 78/2021/TT-BTC; quy định chi tiết Luật 108/2025/QH15, Quyết định 970/QĐ-TTg");
        Assert.Equal(new[] { "REPLACES:123/2020/NĐ-CP", "REPEALS:78/2021/TT-BTC", "GUIDES:108/2025/QH15", "GUIDES:970/QĐ-TTg" }, r.Select(x => x.Kind + ":" + x.DocumentNo));
        Assert.Equal(r, S.LegalWatchPolicy.Split(S.LegalWatchPolicy.Join(r)));
        var note = S.LegalWatchPolicy.Note(Page, r);
        Assert.Contains("Thay thế: 123/2020/NĐ-CP", note);
        Assert.EndsWith("Nguồn: " + Page, note);
        Assert.Empty(S.LegalWatchPolicy.Relations("Quy định về lệ phí trước bạ"));
        Assert.Equal("123-2020-nd-cp", S.LegalWatchPolicy.Slug("123/2020/NĐ-CP"));
        Assert.Equal(S.LegalWatchPolicy.Key("123/2020/nđ-cp "), S.LegalWatchPolicy.Key("123/2020/NĐ-CP"));
        Assert.Equal("2026-W46", S.LegalWatchPolicy.Week(new DateOnly(2026, 11, 14)));
        Assert.Equal("2026-11", S.LegalWatchPolicy.Month(new DateOnly(2026, 11, 14)));
    }

    [Fact]
    public void VbplPagesAndSitemapsAreReadWithoutTheApi()
    {
        var s = S.VbplParser.Detail(VbplPage)!;
        Assert.Equal("17/2025/NĐ-CP", s.DocumentNo);
        Assert.Equal("Hết hiệu lực một phần", s.EffectStatus);
        Assert.Equal(new DateTime(2025, 2, 6), s.EffectiveFrom);
        Assert.Null(s.ExpiredDate);
        Assert.Null(S.VbplParser.Detail("<html>no data</html>"));
        var docs = new List<(Guid, string, string)> { (Doc, "123/2020/NĐ-CP", "DECREE"), (Guid.Empty, "91/2015/QH13", "CODE") };
        var found = S.VbplParser.Match(Sitemap, docs).ToList();
        Assert.Equal(new[] { (Doc, "https://vbpl.vn/van-ban/chi-tiet/nghi-dinh-so-123-2020-nd-cp-quy-dinh-ve-hoa-don-chung-tu--146457"),
                             (Guid.Empty, "https://vbpl.vn/van-ban/chi-tiet/bo-luat-dan-su-so-91-2015-qh13--95942") }, found);
        // An amending decree names 123/2020/NĐ-CP later in its slug and a resolution shares the number of the civil code: neither matches.
        Assert.DoesNotContain(found, f => f.Item2.Contains("70-2025") || f.Item2.Contains("nghi-quyet"));
    }

    [Fact]
    public void OnlyHttpsOnTheFourSourceHostsIsAllowed()
    {
        Assert.True(S.LegalWatchPolicy.Allowed(Page));
        Assert.True(S.LegalWatchPolicy.Allowed(Word));
        Assert.True(S.LegalWatchPolicy.Allowed("https://vbpl.vn/sitemap/1.xml"));
        Assert.False(S.LegalWatchPolicy.Allowed("http://vbpl.vn/sitemap/1.xml"));
        Assert.False(S.LegalWatchPolicy.Allowed("https://vbpl.vn:8443/x"));
        Assert.False(S.LegalWatchPolicy.Allowed("https://thuvienphapluat.vn/x"));
        Assert.False(S.LegalWatchPolicy.Allowed("https://vbpl.vn.evil.example/x"));
        Assert.False(S.LegalWatchPolicy.Allowed("file:///c:/x"));
    }

    // ---- Job ------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task CollectQueuesNewTaxDocumentsOnceAndSkipsTheRest()
    {
        S.LegalWatchService.Pause = TimeSpan.Zero;
        var web = new FakeWeb();
        web.Text[S.LegalWatchPolicy.CongBaoFeed] = Feed;
        web.Text[Page] = DetailPage(TaxSummary);
        web.Text["https://congbao.chinhphu.vn/van-ban/thong-tu-so-166-2026-tt-btc-470909.htm"] = DetailPage("Bãi bỏ một số thông tư liên tịch liên quan đến lĩnh vực tài chính", "166/2026/TT-BTC");
        var store = new MemoryWatch();
        var service = new S.LegalWatchService(store, web, new ServiceCollection().BuildServiceProvider(), NullLogger<S.LegalWatchService>.Instance);
        var result = await service.CollectAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(3, result.Scanned);
        Assert.Equal(1, result.NewItems);
        Assert.Equal("21/2026/TT-BTC", Assert.Single(store.New).DocumentNo);           // the non-tax BTC circular is not queued
        Assert.DoesNotContain(web.Requested, u => u.Contains("quyet-dinh-so-2101"));    // a non-tax entry is never opened
        var again = await service.CollectAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(0, again.NewItems);                                               // same number: no second entry
        web.Text.Remove(S.LegalWatchPolicy.CongBaoFeed);
        Assert.NotNull((await service.CollectAsync(Guid.NewGuid(), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task StatusCheckRecordsABaselineAndQueuesOnlyChanges()
    {
        S.LegalWatchService.Pause = TimeSpan.Zero;
        var web = new FakeWeb();
        var normal = VbplPage.Replace("Hết hiệu lực một phần", "Còn hiệu lực");
        const string url17 = "https://vbpl.vn/van-ban/chi-tiet/nghi-dinh-so-17-2025-nd-cp-sua-doi--175145";
        web.Text[url17] = normal;
        var store = new MemoryWatch();
        store.Docs.Add(new(Doc, "17/2025/NĐ-CP", "DECREE", "Nghị định 17/2025/NĐ-CP", url17, null, null));
        var service = new S.LegalWatchService(store, web, new ServiceCollection().BuildServiceProvider(), NullLogger<S.LegalWatchService>.Instance);
        var first = await service.CheckStatusAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal((1, 0), (first.Scanned, first.StatusItems));
        Assert.Equal((url17, "Còn hiệu lực"), store.Checked[Doc]);                      // baseline written once, no queue entry
        Assert.DoesNotContain(web.Requested, u => u.Contains("sitemap"));               // the page is known: no sitemap read
        store.Docs[0] = store.Docs[0] with { EffectStatus = "Còn hiệu lực" };
        web.Text[url17] = VbplPage;
        var second = await service.CheckStatusAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(1, second.StatusItems);
        Assert.Equal("Hết hiệu lực một phần", Assert.Single(store.Status).EffectStatus);
        Assert.Equal((url17, (string?)null), store.Checked[Doc]);                       // the library status is not changed by the job
    }

    [Fact]
    public void TheMonthlyEmailPointsToTheQueueWithoutCustomerData()
    {
        var body = S.LegalWatchService.Body("2026-11", 3, 1, 5);
        Assert.Contains("3 văn bản thuế mới và 1 thay đổi hiệu lực", body);
        Assert.Contains("Văn bản mới cần duyệt", body);
        Assert.Contains("{name}", body);
    }

    // ---- Pages ----------------------------------------------------------------------------------------------------
    [Fact]
    public async Task TheQueueShowsActionsOnlyToManagersAndImportsThroughTheLibraryStore()
    {
        var watch = new MemoryWatch();
        var item = watch.Add(S.CongBaoParser.Detail(DetailPage(TaxSummary), Page)!);
        var library = new RecordingLibrary();
        var web = new FakeWeb();
        web.Bytes[Word] = S.DocxWriter.Write("Thông tư 21/2026/TT-BTC",
            [new("P1", "TEXT", "Điều 1. Sửa đổi khoản 2 Điều 5"), new("P2", "TEXT", "Nội dung sửa đổi."), new("P3", "TEXT", "Điều 2. Hiệu lực thi hành"), new("P4", "TEXT", "Có hiệu lực từ ngày ký.")]);
        using (var viewer = Factory(watch, library, web, "CONTRACTS.REVIEW.VIEW"))
        {
            var c = Client(viewer);
            var html = WebUtility.HtmlDecode(await c.GetStringAsync("/legalwatch"));
            Assert.Contains("21/2026/TT-BTC", html);
            Assert.Contains("Sửa đổi, bổ sung", html);
            Assert.DoesNotContain("data-ta-watch-check", html);
            Assert.DoesNotContain("/legalwatch/import/", html, StringComparison.OrdinalIgnoreCase);
            var lib = WebUtility.HtmlDecode(await c.GetStringAsync("/legallibrary"));
            Assert.Contains("data-ta-legal-watch", lib);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/legalwatch/import/{item}", Form(Token(await c.GetStringAsync("/legalwatch"))))).StatusCode);
        }
        using var manager = Factory(watch, library, web, "CONTRACTS.REVIEW.VIEW", "LEGAL.LIBRARY.MANAGE");
        var m = Client(manager);
        var page = await m.GetStringAsync("/legalwatch");
        Assert.Contains("data-ta-watch-check", page);
        Assert.Contains($"/LegalWatch/Import/{item}", page);
        var manual = WebUtility.HtmlDecode(Regex.Match(page, "href=\"(/LegalLibrary/Create\\?[^\"]+)\"").Groups[1].Value);
        var form = WebUtility.HtmlDecode(await m.GetStringAsync(manual));
        Assert.Contains("value=\"21/2026/TT-BTC\"", form);                               // "Nhập tay" pre-fills the library form
        Assert.Contains("value=\"2026-12-20\"", form);
        var import = await m.PostAsync($"/legalwatch/import/{item}", Form(Token(page)));
        Assert.Equal(HttpStatusCode.Redirect, import.StatusCode);
        Assert.Equal($"/LegalLibrary/Details/{RecordingLibrary.Saved}", import.Headers.Location!.OriginalString);
        Assert.Equal(("21/2026/TT-BTC", "CIRCULAR", 2), library.Last);
        Assert.Contains("Nguồn: " + Page, library.LastNote);
        Assert.Equal(("IMPORTED", RecordingLibrary.Saved), watch.Decisions[item]);
        // Decided entries leave the queue; dismissing needs the manager too.
        var other = watch.Add(S.CongBaoParser.Detail(DetailPage(TaxSummary, "22/2026/TT-BTC"), Page.Replace("21", "22"))!);
        var dismiss = await m.PostAsync($"/legalwatch/dismiss/{other}", Form(Token(await m.GetStringAsync("/legalwatch")), ("note", "Không liên quan")));
        Assert.Equal(HttpStatusCode.Redirect, dismiss.StatusCode);
        Assert.Equal(("DISMISSED", (Guid?)null), watch.Decisions[other]);
        var check = await m.PostAsync("/legalwatch/check", Form(Token(await m.GetStringAsync("/legalwatch"))));
        Assert.Equal(HttpStatusCode.Redirect, check.StatusCode);
    }

    [Fact]
    public async Task AnUnreadableWordFileNeverReachesTheLibrary()
    {
        var watch = new MemoryWatch();
        var item = watch.Add(S.CongBaoParser.Detail(DetailPage(TaxSummary), Page)!);
        var library = new RecordingLibrary();
        var web = new FakeWeb();
        web.Bytes[Word] = "not a word file"u8.ToArray();
        using var manager = Factory(watch, library, web, "CONTRACTS.REVIEW.VIEW", "LEGAL.LIBRARY.MANAGE");
        var m = Client(manager);
        var r = await m.PostAsync($"/legalwatch/import/{item}", Form(Token(await m.GetStringAsync("/legalwatch"))));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/LegalWatch", r.Headers.Location!.OriginalString);
        Assert.Null(library.Last.No);
        Assert.False(watch.Decisions.ContainsKey(item));
    }

    // ---- Fixtures (fragments of the real pages, 2026-11-14) ---------------------------------------------------------
    private const string TaxSummary = "Sửa đổi, bổ sung một số điều của Thông tư số 80/2021/TT-BTC hướng dẫn thi hành một số điều của Luật Quản lý thuế và Nghị định số 126/2020/NĐ-CP";

    private const string Feed = """
<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>Các văn bản được ban hành mới nhất</title>
<item><title></title><description><![CDATA[ Quy định về thuế giá trị gia tăng đối với hàng hóa xuất khẩu]]></description><pubDate>Fri, 06 Nov 2026 00:00:00 GMT</pubDate>
<link>https://congbao.chinhphu.vn/van-ban/thong-tu-so-21-2026-tt-btc-470950.htm</link></item>
<item><title></title><description><![CDATA[Bãi bỏ một số thông tư liên tịch]]></description><pubDate>Wed, 04 Nov 2026 00:00:00 GMT</pubDate>
<link>https://congbao.chinhphu.vn/van-ban/thong-tu-so-166-2026-tt-btc-470909.htm</link></item>
<item><title></title><description><![CDATA[Bãi bỏ Quyết định số 970/QĐ-TTg]]></description><pubDate>Wed, 04 Nov 2026 00:00:00 GMT</pubDate>
<link>https://congbao.chinhphu.vn/van-ban/quyet-dinh-so-2101-qd-ttg-470912.htm</link></item>
<item><title></title><description><![CDATA[Thuế]]></description><link>https://evil.example/van-ban/x.htm</link></item>
</channel></rss>
""";

    private static string DetailPage(string summary, string no = "21/2026/TT-BTC") => $$"""
<html><body><div class="box-child-diar"><a href="/x"><span class="name">Chủ tịch nước</span></a></div>
<h1 class="title"> Th&#xF4;ng t&#x1B0; s&#x1ED1; {{no}} </h1>
<a href="https://g7.cdnchinhphu.vn/api/download/stream?Url=abc&amp;file_name=2026_21%2f2026%2fTT-BTC.docx" data-atc="download" class="item-open">2026_21/2026/TT-BTC.docx</a>
<div class="table">
 <div class="row">
 <span class="name">
 Loại văn bản
 </span>

 <div class="value">
 <span class="child-value">
 Th&#xF4;ng t&#x1B0;
 </span>
 </div>
 </div>
 <div class="row">
 <span class="name">
 Số, ký hiệu
 </span>

 <div class="value">
 <span class="child-value">
 {{no}}
 </span>
 </div>
 </div>
 <div class="row">
 <span class="name">
 Cơ quan ban hành
 </span>

 <div class="value">
 <span class="child-value">
B&#x1ED8; T&#xC0;I CH&#xCD;NH </span>
 </div>
 </div>
 <div class="row">
 <span class="name">
 Ngày ban hành
 </span>

 <div class="value">
 <span class="child-value">
 02/11/2026
 </span>
 </div>
 </div>
 <div class="row">
 <span class="name">
 Trích yếu
 </span>

 <div class="value">
 <span class="child-value">
 {{summary}}
 </span>
 </div>
 </div>
 <div class="row">
 <span class="name">
 Ngày hiệu lực
 </span>

 <div class="value">
 <span class="child-value">
 20/12/2026
 </span>
 </div>
 </div>
</div></body></html>
""";

    private const string VbplPage = """
<script>self.__next_f.push([1,"{\"labels\":{\"effStatus\":\"Hiệu lực\",\"docNum\":\"Số hiệu\"},\"document\":{\"id\":\"175145\",\"docType\":{\"id\":\"0d08b84c-7de7-4800-8760-2a68265e7890\",\"name\":\"Nghị định\",\"code\":\"NĐ\",\"parentCode\":\"VBQPPL\",\"parentName\":null},\"docGroup\":\"VBQPPL\",\"docNum\":\"17/2025/NĐ-CP\",\"title\":\"Nghị định số 17/2025/NĐ-CP Sửa đổi, bổ sung một số điều của các Nghị định quy định chi tiết một số điều và biện pháp thi hành Luật Đấu thầu\",\"applicationNote\":null,\"issueDate\":\"2025-02-06T00:00:00\",\"effFrom\":\"2025-02-06T00:00:00\",\"effTo\":null,\"publicDate\":\"2026-11-14T00:00:00\",\"effStatus\":{\"id\":\"9c20e89d-e048-4f3a-b6c2-df87fe0b1ada\",\"name\":\"Hết hiệu lực một phần\",\"code\":\"HHL1P\",\"parentCode\":null,\"parentName\":null},\"status\":\"Publish\",\"agencyName\":\"Chính phủ\"}}"])</script>
""";

    private const string Sitemap = """
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
<url><loc>https://vbpl.vn/van-ban/chi-tiet/nghi-dinh-so-70-2025-nd-cp-sua-doi-bo-sung-mot-so-dieu-cua-nghi-dinh-so-123-2020-nd-cp-ngay-19-thang-10-nam-2020--177581</loc></url>
<url><loc>https://vbpl.vn/van-ban/chi-tiet/nghi-dinh-so-123-2020-nd-cp-quy-dinh-ve-hoa-don-chung-tu--146457</loc></url>
<url><loc>https://vbpl.vn/van-ban/chi-tiet/nghi-quyet-so-91-2015-qh13-ve-chuong-trinh-hoat-dong-giam-sat--70827</loc></url>
<url><loc>https://vbpl.vn/van-ban/chi-tiet/bo-luat-dan-su-so-91-2015-qh13--95942</loc></url>
</urlset>
""";

    // ---- Helpers ---------------------------------------------------------------------------------------------------
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))]);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(MemoryWatch watch, RecordingLibrary library, FakeWeb web, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.ILegalWatchStore>();
                s.AddSingleton<S.ILegalWatchStore>(watch);
                s.RemoveAll<S.ILegalLibraryStore>();
                s.AddSingleton<S.ILegalLibraryStore>(library);
                s.RemoveAll<S.ILegalWatchWeb>();
                s.AddSingleton<S.ILegalWatchWeb>(web);
                s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                s.AddSingleton(new TestPrincipal(permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed class FakeWeb : S.ILegalWatchWeb
    {
        public readonly Dictionary<string, string> Text = new();
        public readonly Dictionary<string, byte[]> Bytes = new();
        public readonly List<string> Requested = [];
        public Task<string?> GetTextAsync(string url, CancellationToken ct) { Requested.Add(url); return Task.FromResult(Text.TryGetValue(url, out var t) ? t : null); }
        public Task<byte[]?> GetBytesAsync(string url, int maxBytes, CancellationToken ct) { Requested.Add(url); return Task.FromResult(Bytes.TryGetValue(url, out var b) ? b : null); }
    }

    private sealed class MemoryWatch : S.ILegalWatchStore
    {
        public readonly List<S.LegalWatchCandidate> New = [];
        public readonly List<S.VbplStatus> Status = [];
        public readonly List<S.LegalWatchDocument> Docs = [];
        public readonly Dictionary<Guid, (string Url, string? Baseline)> Checked = new();
        public readonly Dictionary<Guid, (string Status, Guid? Library)> Decisions = new();
        private readonly List<S.LegalWatchItem> items = [];

        public Guid Add(S.LegalWatchCandidate c)
        {
            var id = Guid.NewGuid();
            items.Add(new(id, "NEW", c.Source, c.SourceUrl, c.FileUrl, c.DocumentNo, c.DocType, c.TypeName, c.Summary, c.Agency, c.IssuedDate, c.EffectiveDate, null, null,
                c.Relations, c.Keywords, "PENDING", DateTime.UtcNow, null, null, null, null, null, null, null));
            return id;
        }

        public Task<bool> RunExistsAsync(string kind, string period) => Task.FromResult(false);
        public Task<Guid?> ClaimRunAsync(string kind, string period, Guid? requestedBy) => Task.FromResult<Guid?>(Guid.NewGuid());
        public Task FinishRunAsync(Guid runId, int scanned, int newItems, int statusItems, string? error) => Task.CompletedTask;
        public Task<bool> IsKnownAsync(string documentNo) => Task.FromResult(New.Any(n => n.DocumentNo == documentNo));
        public Task<bool> AddNewAsync(Guid runId, S.LegalWatchCandidate candidate)
        {
            if (New.Any(n => n.DocumentNo == candidate.DocumentNo)) return Task.FromResult(false);
            New.Add(candidate);
            return Task.FromResult(true);
        }
        public Task<IReadOnlyList<S.LegalWatchDocument>> DocumentsAsync() => Task.FromResult<IReadOnlyList<S.LegalWatchDocument>>(Docs.ToList());
        public Task MarkCheckedAsync(Guid documentId, string sourceUrl, string? baselineStatus) { Checked[documentId] = (sourceUrl, baselineStatus); return Task.CompletedTask; }
        public Task<bool> AddStatusAsync(Guid runId, S.LegalWatchDocument document, string sourceUrl, S.VbplStatus status) { Status.Add(status); return Task.FromResult(true); }
        public Task<(int New, int Status)> PendingSinceAsync(DateTime sinceUtc) => Task.FromResult((New.Count, Status.Count));
        public Task<IReadOnlyList<S.LegalWatchRecipient>> RecipientsAsync() => Task.FromResult<IReadOnlyList<S.LegalWatchRecipient>>([]);
        public Task MarkNotifiedAsync(Guid runId) => Task.CompletedTask;
        public Task<S.LegalWatchOverview> OverviewAsync(bool pending)
        {
            var list = items.Where(i => pending == !Decisions.ContainsKey(i.Id)).ToList();
            return Task.FromResult(new S.LegalWatchOverview(list, [], items.Count(i => !Decisions.ContainsKey(i.Id))));
        }
        public Task<S.LegalWatchItem?> GetAsync(Guid id) => Task.FromResult(items.FirstOrDefault(i => i.Id == id && !Decisions.ContainsKey(i.Id)));
        public Task<int> PendingCountAsync() => Task.FromResult(items.Count(i => !Decisions.ContainsKey(i.Id)));
        public Task<S.LegalOutcome> DecideAsync(Guid actor, Guid id, string status, Guid? libraryId, string? note, string? ip)
        {
            Decisions[id] = (status, libraryId);
            return Task.FromResult(S.LegalOutcome.Saved);
        }
        public Task<S.LegalOutcome> ApplyStatusAsync(Guid actor, Guid id, string? ip) => Task.FromResult(S.LegalOutcome.Saved);
    }

    private sealed class RecordingLibrary : S.ILegalLibraryStore
    {
        public static readonly Guid Saved = Guid.Parse("d9000000-0000-0000-0000-0000000000d9");
        public (string? No, string? Type, int Articles) Last;
        public string? LastNote;
        public Task<IReadOnlyList<S.LegalDocumentItem>> ListAsync() => Task.FromResult<IReadOnlyList<S.LegalDocumentItem>>([]);
        public Task<S.LegalDocumentDetail?> GetAsync(Guid id) => Task.FromResult<S.LegalDocumentDetail?>(null);
        public Task<IReadOnlyList<S.LegalIndexArticle>> IndexAsync(DateTime today) => Task.FromResult<IReadOnlyList<S.LegalIndexArticle>>([]);
        public Task<(S.LegalOutcome Outcome, Guid? Id, string? Summary)> SaveAsync(Guid actor, Guid? id, S.LegalDocumentInput input, S.LegalUpload? upload, string? ip)
        {
            Assert.Empty(input.Validate(true, upload is not null));
            Last = (input.DocumentNo, input.DocType, upload is null ? 0 : S.LegalTextParser.Split(upload.Text).Count);
            LastNote = input.Note;
            return Task.FromResult<(S.LegalOutcome, Guid?, string?)>((S.LegalOutcome.Saved, Saved, "tách được 2 điều"));
        }
        public Task<(S.LegalOutcome Outcome, string? Summary)> ReimportAsync(Guid actor, Guid id, Guid fileId, string? ip) => Task.FromResult<(S.LegalOutcome, string?)>((S.LegalOutcome.Saved, ""));
        public Task<S.LegalOutcome> UpdateArticleAsync(Guid actor, Guid id, long articleId, string? title, string? content, string? ip) => Task.FromResult(S.LegalOutcome.Saved);
        public Task<S.LegalOutcome> DeleteArticleAsync(Guid actor, Guid id, long articleId, string? ip) => Task.FromResult(S.LegalOutcome.Saved);
        public Task<S.LegalOutcome> DeleteAsync(Guid actor, Guid id, string? ip) => Task.FromResult(S.LegalOutcome.Saved);
        public Task<S.ReviewFileContent?> DownloadAsync(Guid id, Guid fileId) => Task.FromResult<S.ReviewFileContent?>(null);
    }

    private sealed record TestPrincipal(string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-watch-test"), new("display_name", "Duyệt văn bản"), new(ClaimTypes.Role, "STAFF") };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
