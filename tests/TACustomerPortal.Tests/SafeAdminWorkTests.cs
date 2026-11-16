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
using Microsoft.Extensions.Options;
using AdminSecurity = admin::TAPortal.Web.Security;
using S = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// CONTRACTS-AND-TASKS: Admin → Công việc (operator 2026-11-07). No SQL host or connection: WorkTaskPolicy is tested directly
// and the HTTP tests use an in-memory store applying the same policy as SqlWorkTaskStore. Real SQL (locks, RowVersion,
// filtered unique indexes, the TASK_CODE sequence) is NOT exercised here.
public sealed class SafeAdminWorkTests
{
    private const string TestScheme = "SafeWorkTest";
    private static readonly Guid Actor = Guid.Parse("51111111-1111-1111-1111-111111111111"), Other = Guid.Parse("52222222-2222-2222-2222-222222222222");
    private static readonly Guid E1 = Guid.Parse("e1000000-0000-0000-0000-000000000001"), E2 = Guid.Parse("e2000000-0000-0000-0000-000000000002");
    private static readonly DateOnly Today = new(2026, 11, 7);

    // ---- Rules ------------------------------------------------------------------------------------------------------
    [Fact]
    public void OnlyParticipantsCreatorAndSupervisorsSeeATask()
    {
        Assert.True(S.WorkTaskPolicy.CanSee(viewAll: false, isOwner: true, isMember: false, isCreator: false));
        Assert.True(S.WorkTaskPolicy.CanSee(false, false, true, false));          // co-worker ("việc làm chung")
        Assert.True(S.WorkTaskPolicy.CanSee(false, false, false, true));          // the person who assigned it
        Assert.True(S.WorkTaskPolicy.CanSee(true, false, false, false));          // supervisor (TASKS.VIEW_ALL)
        Assert.False(S.WorkTaskPolicy.CanSee(false, false, false, false));        // anyone else does not see it at all
        Assert.True(S.WorkTaskPolicy.CanEdit(viewAll: false, canAssign: false, isCreator: true));
        Assert.True(S.WorkTaskPolicy.CanEdit(true, true, false));
        Assert.False(S.WorkTaskPolicy.CanEdit(true, false, false));                // supervision alone does not re-assign
        Assert.False(S.WorkTaskPolicy.CanEdit(false, true, false));                // ASSIGN without VIEW_ALL only edits own assignments
    }

    [Fact]
    public void StatusFlowHasNoApprovalAndDoneNeedsTheWholeChecklist()
    {
        Assert.True(S.WorkTaskPolicy.CanSetStatus("NEW", "IN_PROGRESS", canEdit: false));
        Assert.True(S.WorkTaskPolicy.CanSetStatus("IN_PROGRESS", "DONE", false));
        Assert.True(S.WorkTaskPolicy.CanSetStatus("DONE", "IN_PROGRESS", false));   // reopen
        Assert.False(S.WorkTaskPolicy.CanSetStatus("IN_PROGRESS", "CANCELLED", false));
        Assert.True(S.WorkTaskPolicy.CanSetStatus("IN_PROGRESS", "CANCELLED", true));
        Assert.False(S.WorkTaskPolicy.CanSetStatus("CANCELLED", "NEW", false));
        Assert.False(S.WorkTaskPolicy.CanSetStatus("NEW", "APPROVED", true));
        Assert.Equal(S.WorkOutcome.Saved, S.WorkTaskPolicy.CheckDone(0, 0));                 // checklist optional (2026-11-16)
        Assert.Null(S.WorkTaskPolicy.DoneNote("   "));
        Assert.Equal("Nộp tờ khai xong", S.WorkTaskPolicy.DoneNote(" Nộp tờ khai xong "));
        Assert.Equal(S.WorkOutcome.ChecklistIncomplete, S.WorkTaskPolicy.CheckDone(3, 2));
        Assert.Equal(S.WorkOutcome.Saved, S.WorkTaskPolicy.CheckDone(3, 3));
        Assert.Equal(66, S.WorkTaskPolicy.Progress("IN_PROGRESS", 3, 2));
        Assert.Equal(100, S.WorkTaskPolicy.Progress("DONE", 3, 3));
        Assert.Equal(0, S.WorkTaskPolicy.Progress("NEW", 0, 0));
        Assert.True(S.WorkTaskPolicy.IsOverdue("IN_PROGRESS", Today.AddDays(-1), Today));
        Assert.False(S.WorkTaskPolicy.IsOverdue("DONE", Today.AddDays(-1), Today));
        Assert.True(S.WorkTaskPolicy.IsDueSoon("NEW", Today.AddDays(3), Today));
        Assert.False(S.WorkTaskPolicy.IsDueSoon("NEW", Today.AddDays(4), Today));
    }

    [Fact]
    public void MyWorkOrdersByNearestDueDateOrLatestActivity()
    {
        var tasks = new[] { ("a", (DateOnly?)null, new DateTime(2026, 11, 7, 9, 0, 0)), ("b", Today.AddDays(5), new DateTime(2026, 11, 1)),
            ("c", Today.AddDays(-2), new DateTime(2026, 11, 2)), ("d", Today.AddDays(5), new DateTime(2026, 11, 6)) };
        Assert.Equal("cdba", string.Concat(S.WorkTaskPolicy.Order(tasks, "due", t => t.Item2, t => t.Item3).Select(t => t.Item1)));
        Assert.Equal("adcb", string.Concat(S.WorkTaskPolicy.Order(tasks, "activity", t => t.Item2, t => t.Item3).Select(t => t.Item1)));
    }

    [Fact]
    public void ChecklistTextBecomesOneItemPerLine()
    {
        Assert.Equal(new[] { "Thu thập hóa đơn", "Lập tờ khai", "Nộp" }, S.WorkTaskPolicy.ChecklistLines("- Thu thập hóa đơn\r\n\r\n* Lập tờ khai\n• Nộp \n"));
        Assert.Empty(S.WorkTaskPolicy.ChecklistLines(null));
        Assert.Equal(S.WorkTaskPolicy.ChecklistItemsMax, S.WorkTaskPolicy.ChecklistLines(string.Join('\n', Enumerable.Range(1, 80).Select(i => $"Mục {i}"))).Count);
    }

    [Fact]
    public void RecurringPeriodsAndDueDates()
    {
        // VAT return of the month, due on the 20th of the next month.
        Assert.Equal(new DateOnly(2026, 11, 20), S.WorkTaskPolicy.DueDateOf(new DateOnly(2026, 10, 31), 20, 1));
        Assert.Equal(new DateOnly(2027, 2, 28), S.WorkTaskPolicy.DueDateOf(new DateOnly(2027, 1, 31), 31, 1));   // clamped to the month
        Assert.Equal(("2026-Q4", "Q4/2026"), (S.WorkTaskPolicy.PeriodOf(S.WorkFrequency.Quarterly, Today).Key, S.WorkTaskPolicy.PeriodOf(S.WorkFrequency.Quarterly, Today).Label));
        Assert.Equal("T11/2026", S.WorkTaskPolicy.PeriodOf(S.WorkFrequency.Monthly, Today).Label);

        // Created today with a 7-day lead: October's task (due 20/11) opens on 13/11, so nothing yet; with 20 days it opens now.
        Assert.Empty(S.WorkTaskPolicy.DuePeriods(S.WorkFrequency.Monthly, 20, 1, 7, new DateOnly(2026, 1, 1), null, Today, Today));
        var due = S.WorkTaskPolicy.DuePeriods(S.WorkFrequency.Monthly, 20, 1, 20, new DateOnly(2026, 1, 1), null, Today, Today);
        Assert.Equal(new[] { "2026-10" }, due.Select(p => p.Key));
        Assert.Equal(new DateOnly(2026, 11, 20), due[0].DueDate);
        Assert.Equal("T10/2026", due[0].Label);
        // No backlog: periods due before the template was created are skipped, and the end date stops new periods.
        Assert.DoesNotContain(S.WorkTaskPolicy.DuePeriods(S.WorkFrequency.Monthly, 20, 1, 60, new DateOnly(2026, 1, 1), null, Today, Today), p => p.DueDate < Today);
        Assert.Empty(S.WorkTaskPolicy.DuePeriods(S.WorkFrequency.Monthly, 20, 1, 20, new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30), Today, Today));
        // Quarterly Q3 due 30/10 (one month after the quarter), created long before: on 07/11 that period is already past due and
        // its task would have been made in October; Q4 (due 30/01/2027) opens with a 90-day lead.
        var quarterly = S.WorkTaskPolicy.DuePeriods(S.WorkFrequency.Quarterly, 30, 1, 90, new DateOnly(2026, 1, 1), null, new DateOnly(2026, 1, 1), Today);
        Assert.Contains(quarterly, p => p.Key == "2026-Q4" && p.DueDate == new DateOnly(2027, 1, 30));
        Assert.Null(S.WorkTemplatePolicy.Validate(new S.WorkTemplateInput { Title = "Thuế GTGT", OwnerEmployeeId = E1, StartDate = new DateTime(2026, 1, 1) }));
        Assert.NotNull(S.WorkTemplatePolicy.Validate(new S.WorkTemplateInput { Title = "X", OwnerEmployeeId = E1, StartDate = new DateTime(2026, 1, 1), DueDay = 32 }));
        Assert.NotNull(S.WorkTemplatePolicy.Validate(new S.WorkTemplateInput { Title = "X", OwnerEmployeeId = E1, StartDate = new DateTime(2026, 1, 1), Frequency = "DAILY" }));
    }

    // ---- Catalogue, migration and SQL guards ---------------------------------------------------------------------------
    [Fact]
    public void Migration031MatchesTheTaskAndContractCatalogue()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "031-contracts-and-work-tasks.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "051-verify-contracts-and-work-tasks.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "052-rollback-contracts-and-work-tasks.sql"));
        var expected = AdminSecurity.AdminPermissions.Migration031Codes.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(10, expected.Count);
        Assert.Equal(expected, Regex.Matches(migration, @"\('([A-Z._]+)',\s+N'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(verify, @"\('([A-Z._]+)'\)").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.All(expected, c => Assert.DoesNotContain(c, AdminSecurity.AdminPermissions.MigrationCodes));   // not part of 028
        const string marker = "A0000031-0000-4000-8000-000000000031";
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains(marker, sql));
        Assert.All(new[] { migration, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE|DROP)\b", migration);                                      // new tables and rows only
        Assert.Contains("Rollback refused: contracts, templates or tasks exist", rollback);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.True(migration.All(ch => ch < 128), "migration text must be ASCII (sqlcmd code page)");
    }

    [Fact]
    public void WorkSqlFiltersEveryReadByVisibilityAndGuardsWrites()
    {
        Assert.Contains(S.WorkTaskSql.Visible, S.WorkTaskSql.List);
        Assert.Contains(S.WorkTaskSql.Visible, S.WorkTaskSql.Detail);
        Assert.Contains(S.WorkTaskSql.Visible, S.WorkTaskSql.Card);
        Assert.Contains("@all=1 OR t.CreatedBy=@actor OR t.OwnerEmployeeId=@emp", S.WorkTaskSql.Visible);
        Assert.Contains("vm.IsActive=1", S.WorkTaskSql.Visible);
        Assert.Contains("RowVersion=@rv", S.WorkTaskSql.Update);
        Assert.Contains("RowVersion=@rv", S.WorkTaskSql.UpdateTemplate);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", S.WorkTaskSql.ForUpdate);
        Assert.Contains("OUTPUT inserted.Prefix,inserted.CurrentValue", S.WorkTaskSql.NextTaskNo);              // atomic task number
        Assert.Contains("DoneAt=CASE WHEN @done=1 THEN SYSUTCDATETIME()", S.WorkTaskSql.SetChecklist);          // completion time recorded
        Assert.Contains("ESCAPE '\\'", S.WorkTaskSql.List);
        var all = string.Join("\n", typeof(S.WorkTaskSql).GetFields().Select(f => (string)f.GetValue(null)!));
        Assert.DoesNotContain("STRING_AGG", all);
        Assert.DoesNotContain("DELETE FROM", all);                                                              // soft delete only
        Assert.DoesNotMatch(@"\bOFFSET\b|\bFETCH\b|CREATE OR ALTER|DROP\s", all);
        // Pieces joined from raw string literals (which drop their last line break) must never glue two tokens together:
        // production 2026-11-07 failed with "Incorrect syntax near 'SELECTt'".
        foreach (var sql in new[] { S.WorkTaskSql.MyOpen, S.WorkTaskSql.Card, S.WorkTaskSql.List, S.WorkTaskSql.Detail })
        {
            Assert.DoesNotMatch(@"SELECT(\s+TOP\s+\d+)?t\.", sql);
            Assert.DoesNotMatch(@"[A-Za-z0-9_)]WHERE\b|\bAND\(", sql);
            Assert.Matches(@"SELECT(\s+TOP\s+\d+)?\nt\.Id,t\.TaskNo", sql.Replace("\r", ""));
        }
    }

    [Fact]
    public void TextFieldsOfferVoiceInputAndTheAdminAllowsTheMicrophone()
    {
        var root = RepoRoot();
        var web = Path.Combine(root, "src", "TAPortal.Web");
        var views = Directory.GetFiles(Path.Combine(web, "Views", "Work"), "*.cshtml").Select(File.ReadAllText).ToList();
        Assert.All(views.SelectMany(v => Regex.Matches(v, "<textarea[^>]*>").Select(m => m.Value)), t => Assert.Contains("data-ta-voice", t));
        var js = File.ReadAllText(Path.Combine(web, "wwwroot", "js", "ta-work.js"));
        Assert.Contains("'vi-VN'", js);
        Assert.Contains("textarea:not([data-ta-voice=\"off\"])", js);                                          // every textarea of the portal
        Assert.Contains("ta-work.js", File.ReadAllText(Path.Combine(web, "Views", "Shared", "_Layout.cshtml")));
        Assert.Contains("microphone=(self)", S.AdminHeaders.PermissionsPolicy);
    }

    // ---- HTTP ---------------------------------------------------------------------------------------------------------------
    [Fact]
    public async Task MyWorkIsTheLandingPageAndListsOpenTasksWithQuickUpdates()
    {
        var store = new MemoryWork();
        using var f = Factory("STAFF", store, "TASKS.VIEW");
        var c = Client(f);
        var r = await c.GetAsync("/work");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("microphone=(self)", r.Headers.GetValues("Permissions-Policy").Single());
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("Lập tờ khai thuế GTGT", html);
        Assert.DoesNotContain("Việc đã xong", html);                                                            // only unfinished tasks
        Assert.Contains("data-ta-card-form", html);
        Assert.Contains("name=\"status\"", html);
        Assert.Contains("name=\"body\"", html);
        Assert.Matches("textarea[^>]*data-ta-voice", html);
        Assert.Equal("Work", AdminSecurity.AdminNavigation.Pages[0].Controller);                              // post-login landing page

        // Quick comment from the card: the response is the re-rendered card only.
        var token = Token(html);
        var post = new HttpRequestMessage(HttpMethod.Post, $"/work/comment/{MemoryWork.T1}") { Content = Form(token, ("body", "Đã nhận chứng từ"), ("returnUrl", "/work")) };
        post.Headers.Add("X-TA-Card", "1");
        var card = await c.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        var cardHtml = WebUtility.HtmlDecode(await card.Content.ReadAsStringAsync());
        Assert.StartsWith("<article", cardHtml.Trim());
        Assert.Contains("Đã nhận chứng từ", cardHtml);
        Assert.DoesNotContain("<html", cardHtml);

        // Done is refused while the checklist is incomplete; the card explains why.
        var done = new HttpRequestMessage(HttpMethod.Post, $"/work/status/{MemoryWork.T1}") { Content = Form(token, ("status", "DONE")) };
        done.Headers.Add("X-TA-Card", "1");
        var refused = WebUtility.HtmlDecode(await (await c.SendAsync(done)).Content.ReadAsStringAsync());
        Assert.Contains("mục checklist chưa hoàn thành", refused);
        Assert.Contains("data-ta-done-note", cardHtml);                                     // the card carries the result-note box

        // Without JavaScript the same form redirects back.
        var plain = await c.PostAsync($"/work/checklistset/{MemoryWork.T1}", Form(token, ("item", MemoryWork.I1.ToString()), ("done", "true"), ("returnUrl", "/work")));
        Assert.Equal(HttpStatusCode.Redirect, plain.StatusCode);
        Assert.Equal("/work", plain.Headers.Location!.OriginalString);
        Assert.True(store.Items[MemoryWork.I1].Done);
        // An external return URL is ignored.
        var evil = await c.PostAsync($"/work/comment/{MemoryWork.T1}", Form(token, ("body", "x"), ("returnUrl", "https://evil.example/")));
        Assert.Equal($"/Work/Details/{MemoryWork.T1}", evil.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task TheTaskNameIsPickedFromTheCatalogueWhenOneExists()
    {
        // Operator 2026-11-14: typing suggests catalogue entries; free text is refused. An empty catalogue keeps a plain box.
        var store = new MemoryWork();
        using (var f = Factory("STAFF", store, "TASKS.VIEW", "TASKS.CREATE"))
        {
            var plain = WebUtility.HtmlDecode(await (await Client(f).GetAsync("/work/create")).Content.ReadAsStringAsync());
            Assert.DoesNotContain("data-ta-combo", plain);
            Assert.Contains("name=\"Input.Title\"", plain);
        }
        store.Catalog.Add(new("Kéo hóa đơn", "Kéo hóa đơn đầu ra, đầu vào"));
        store.Catalog.Add(new("Nhập sao kê ngân hàng", null));
        using (var f = Factory("STAFF", store, "TASKS.VIEW", "TASKS.CREATE"))
        {
            var html = WebUtility.HtmlDecode(await (await Client(f).GetAsync("/work/create")).Content.ReadAsStringAsync());
            Assert.Contains("data-ta-combo-input", html);
            Assert.Contains("role=\"combobox\"", html);
            Assert.Contains("data-value=\"Kéo hóa đơn\"", html);
            Assert.Contains("data-value=\"Nhập sao kê ngân hàng\"", html);
            Assert.Contains("Chọn tên trong danh mục công việc", html);
            Assert.Contains("data-ta-new-checklist", html);                                                // template items to tick + add one by one
            Assert.Contains("type=\"hidden\" name=\"Input.ChecklistText\"", html);
            Assert.Contains("ta-form-grid--pair", html);                                                   // start and due on one row on phones
        }
    }

    [Fact]
    public async Task TheTaskListHasASearchBarAndAFilterPopup()
    {
        // Operator 2026-11-13: search box + search button + "Bộ lọc" beside it; the popup holds several statuses, employees
        // and customers, and a date range on a chosen date.
        using var f = Factory("STAFF", new MemoryWork(), "TASKS.VIEW", "TASKS.ASSIGN");
        var page = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/work/tasks"));
        Assert.Contains("data-ta-filter-open", page);
        Assert.Contains("<dialog class=\"ta-filter-dialog\" data-ta-filter-dialog", page);
        Assert.Contains("type=\"checkbox\" name=\"statuses\" value=\"overdue\"", page);
        Assert.Contains("name=\"employees\"", page);
        Assert.Contains("name=\"dateField\"", page);
        Assert.Contains("data-ta-filter-links", page);
    }

    [Fact]
    public async Task OtherPeoplesTasksAreInvisibleAndFeaturesFollowPermissions()
    {
        var store = new MemoryWork();
        using (var f = Factory("STAFF", store, "TASKS.VIEW"))
        {
            var c = Client(f);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/work/details/{MemoryWork.Hidden}")).StatusCode);
            var token = Token(await c.GetStringAsync("/work"));
            var post = new HttpRequestMessage(HttpMethod.Post, $"/work/comment/{MemoryWork.Hidden}") { Content = Form(token, ("body", "x")) };
            post.Headers.Add("X-TA-Card", "1");
            Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(post)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/work/create")).StatusCode);         // no TASKS.CREATE / ASSIGN
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/work/templates")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/work/report")).StatusCode);
        }
        using (var f = Factory("STAFF", store, "DASHBOARD.VIEW"))
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(f).GetAsync("/work")).StatusCode);
        using (var f = Factory("STAFF", store, "TASKS.VIEW", "TASKS.CREATE"))
        {
            var page = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/work/create"));
            Assert.DoesNotContain("name=\"Input.MemberIds\"", page);                                            // self-assignment only
            Assert.Contains("chưa có quyền giao việc", page);
        }
        using (var f = Factory("STAFF", store, "TASKS.VIEW", "TASKS.ASSIGN"))
        {
            var page = WebUtility.HtmlDecode(await Client(f).GetStringAsync("/work/create"));
            Assert.Contains("name=\"Input.MemberIds\"", page);
            // Visual picker: owner is a radio tile, co-workers are checkbox tiles, each with an avatar.
            Assert.Matches("type=\"radio\" name=\"Input.OwnerEmployeeId\"", page);
            Assert.Matches("type=\"checkbox\" name=\"Input.MemberIds\"", page);
            Assert.Contains("class=\"ta-avatar ta-avatar--sm", page);
            Assert.DoesNotContain("<select class=\"form-select\" id=\"wOwner\"", page);
        }
        using (var f = Factory("STAFF", store, "TASKS.REPORT.VIEW"))
            Assert.Equal(HttpStatusCode.OK, (await Client(f).GetAsync("/work/report")).StatusCode);
    }

    [Fact]
    public void PeoplePickerInitialsAndToneAreStable()
    {
        Assert.Equal("VA", admin::TAPortal.Web.Models.PeoplePickerVm.Initials("Nguyễn Văn An"));
        Assert.Equal("A", admin::TAPortal.Web.Models.PeoplePickerVm.Initials("An"));
        Assert.Equal("?", admin::TAPortal.Web.Models.PeoplePickerVm.Initials("  "));
        var id = Guid.NewGuid();
        Assert.Equal(admin::TAPortal.Web.Models.PeoplePickerVm.Tone(id), admin::TAPortal.Web.Models.PeoplePickerVm.Tone(id));
        Assert.InRange(admin::TAPortal.Web.Models.PeoplePickerVm.Tone(id), 0, 5);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------------
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))]);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task EachDeviceTurnsPushNotificationsOnForTheSignedInUser()
    {
        var push = new MemoryPush();
        using var f = Factory("STAFF", new MemoryWork(), "TASKS.VIEW").WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<S.IWebPushStore>(); s.AddSingleton<S.IWebPushStore>(push);
            s.RemoveAll<S.IWebPushSender>(); s.AddSingleton<S.IWebPushSender>(push);
        }));
        var c = Client(f);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync("/work"));
        Assert.Contains("data-ta-app-install", html);                                                          // "Cài ứng dụng" in the top bar
        Assert.Contains("data-ta-logout", html);
        var token = Token(html);

        using var ec = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = ec.ExportParameters(false).Q;
        var key = S.WebPushCrypto.Encode(new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray());
        var auth = S.WebPushCrypto.Encode(new byte[16]);
        const string endpoint = "https://fcm.googleapis.com/fcm/send/device-1";

        var bad = await c.PostAsync("/push/subscribe", Form(token, ("endpoint", "https://10.0.0.5/x"), ("p256dh", key), ("auth", auth)));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(push.Devices);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/push/subscribe", new FormUrlEncodedContent([new("endpoint", endpoint)]))).StatusCode);   // antiforgery

        var ok = await c.PostAsync("/push/subscribe", Form(token, ("endpoint", endpoint), ("p256dh", key), ("auth", auth)));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((Actor, endpoint), (push.Devices.Single().User, push.Devices.Single().Endpoint));

        var test = await c.PostAsync("/push/test", Form(token));
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.Contains("\"sent\":1", await test.Content.ReadAsStringAsync());
        Assert.Equal(Actor, push.Sent.Single().User);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/push/unsubscribe", Form(token, ("endpoint", endpoint)))).StatusCode);
        Assert.Empty(push.Devices);
    }

    private sealed class MemoryPush : S.IWebPushStore, S.IWebPushSender
    {
        public List<(Guid User, string Endpoint)> Devices { get; } = [];
        public List<(Guid User, S.WebPushMessage Message)> Sent { get; } = [];
        public Task<S.WebPushVapid?> VapidAsync(string subject) => Task.FromResult<S.WebPushVapid?>(new("BKey", new byte[32], subject));
        public Task<bool> SaveAsync(Guid user, string endpoint, string p256dh, string auth, string? device)
        {
            Devices.RemoveAll(d => d.Endpoint == endpoint);
            Devices.Add((user, endpoint));
            return Task.FromResult(true);
        }
        public Task RemoveAsync(Guid user, string endpoint) { Devices.RemoveAll(d => d.User == user && d.Endpoint == endpoint); return Task.CompletedTask; }
        public Task<IReadOnlyList<S.WebPushSubscription>?> ForUserAsync(Guid user) => Task.FromResult<IReadOnlyList<S.WebPushSubscription>?>([]);
        public Task MarkAsync(Guid subscription, bool sent, bool gone) => Task.CompletedTask;
        public Task<S.WebPushSendResult> SendAsync(Guid user, S.WebPushMessage message, CancellationToken ct = default)
        {
            Sent.Add((user, message));
            return Task.FromResult(new S.WebPushSendResult(Devices.Count(d => d.User == user), 0, 0));
        }
    }

    [Fact]
    public void TaskDocumentsAcceptImagesWordPdfAndExcelByContent()
    {
        Assert.Equal("application/pdf", S.WorkDocumentPolicy.CheckFile("hd.pdf", "%PDF-1.7 x"u8.ToArray()).ContentType);
        Assert.Equal("image/png", S.WorkDocumentPolicy.CheckFile("a.PNG", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1]).ContentType);
        Assert.Equal("application/msword", S.WorkDocumentPolicy.CheckFile("cv.doc", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0]).ContentType);
        Assert.Equal("application/vnd.ms-excel", S.WorkDocumentPolicy.CheckFile("bang.xls", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0]).ContentType);
        Assert.Equal("image/webp", S.WorkDocumentPolicy.CheckFile("x.webp", "RIFF\0\0\0\0WEBPVP8 "u8.ToArray()).ContentType);
        Assert.NotNull(S.WorkDocumentPolicy.CheckFile("fake.doc", "%PDF-1.7"u8.ToArray()).Error);       // content must match
        Assert.StartsWith("Chỉ nhận ảnh", S.WorkDocumentPolicy.CheckFile("run.exe", [0x4D, 0x5A]).Error);
        Assert.Null(S.WorkDocumentPolicy.Note("   "));
        Assert.Equal(S.WorkDocumentPolicy.NoteMax, S.WorkDocumentPolicy.Note(new string('a', 2000))!.Length);
        Assert.Contains("INSERT dbo.WorkTaskActivities(TaskId,Kind,Body,CreatedBy) VALUES(@task,'EDIT',N'Tải lên tài liệu: '+@name,@by)", S.WorkDocumentSql.Insert);
        Assert.Contains("SET IsDeleted=1", S.WorkDocumentSql.Delete);                                         // soft delete
        var sql = File.ReadAllText(Path.Combine(RepoRoot(), "database", "migrations", "058-work-task-documents.sql"));
        Assert.Contains("CONSTRAINT CK_WorkTaskDocuments_Size CHECK (SizeBytes > 0 AND SizeBytes <= 10485760)", sql);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "database", "scripts", "103-verify-work-task-documents.sql")));
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "database", "scripts", "104-rollback-work-task-documents.sql")));
    }

    [Fact]
    public async Task TaskPageHasDocumentsBesideTheHistoryAndUploadsThem()
    {
        var store = new MemoryWork();
        var docs = new MemoryDocs();
        using var f = Factory("STAFF", store, "TASKS.VIEW").WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<S.IWorkDocumentStore>();
            s.AddSingleton<S.IWorkDocumentStore>(docs);
        }));
        var c = Client(f);
        var first = await c.GetStringAsync($"/work/details/{MemoryWork.T1}");
        await c.PostAsync($"/work/comment/{MemoryWork.T1}", Form(Token(first), ("body", "Đã nhận"), ("returnUrl", $"/work/details/{MemoryWork.T1}")));
        var html = WebUtility.HtmlDecode(await c.GetStringAsync($"/work/details/{MemoryWork.T1}"));
        Assert.Contains("data-ta-tabbtn=\"documents\"", html);
        Assert.Contains("data-ta-tabbtn=\"thread\"", html);
        Assert.Contains("ta-timeline__avatar", html);                                                         // who wrote each entry
        Assert.Contains("Chưa có tài liệu nào.", html);

        var form = new MultipartFormDataContent
        {
            { new StringContent(Token(html)), "__RequestVerificationToken" },
            { new StringContent("Hóa đơn tháng 10"), "note" },
            { new ByteArrayContent("%PDF-1.7 test"u8.ToArray()), "file", "hoa-don.pdf" }
        };
        var r = await c.PostAsync($"/work/documentupload/{MemoryWork.T1}", form);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var saved = Assert.Single(docs.Items);
        Assert.Equal(("hoa-don.pdf", "application/pdf", "Hóa đơn tháng 10"), (saved.Doc.FileName, saved.Doc.ContentType, saved.Doc.Note));

        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/work/details/{MemoryWork.T1}"));
        Assert.Contains(">hoa-don.pdf</a>", page);
        Assert.Contains("Hóa đơn tháng 10", page);
        var file = await c.GetAsync($"/work/document/{MemoryWork.T1}?doc={saved.Doc.Id}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/work/document/{MemoryWork.Hidden}?doc={saved.Doc.Id}")).StatusCode);   // someone else's task

        var bad = new MultipartFormDataContent
        {
            { new StringContent(Token(page)), "__RequestVerificationToken" },
            { new ByteArrayContent([0x4D, 0x5A, 0, 0]), "file", "virus.exe" }
        };
        Assert.Equal(HttpStatusCode.Redirect, (await c.PostAsync($"/work/documentupload/{MemoryWork.T1}", bad)).StatusCode);
        Assert.Single(docs.Items);                                                                             // refused
    }

    private sealed class MemoryDocs : S.IWorkDocumentStore
    {
        public List<(Guid Task, S.WorkDocument Doc, byte[] Data)> Items { get; } = [];
        public Task<IReadOnlyList<S.WorkDocument>?> ListAsync(Guid taskId) => Task.FromResult<IReadOnlyList<S.WorkDocument>?>(Items.Where(i => i.Task == taskId).Select(i => i.Doc).ToList());
        public Task<string?> AddAsync(Guid actor, Guid taskId, string fileName, string contentType, byte[] data, string? note)
        {
            Items.Add((taskId, new S.WorkDocument(Guid.NewGuid(), fileName, contentType, data.Length, note, DateTime.UtcNow, actor, "An"), data));
            return Task.FromResult<string?>(null);
        }
        public Task<bool> SetNoteAsync(Guid actor, Guid taskId, Guid docId, string? note) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid actor, Guid taskId, Guid docId) => Task.FromResult(Items.RemoveAll(i => i.Doc.Id == docId) > 0);
        public Task<S.WorkDocumentContent?> ContentAsync(Guid taskId, Guid docId) =>
            Task.FromResult(Items.Where(i => i.Task == taskId && i.Doc.Id == docId).Select(i => new S.WorkDocumentContent(i.Doc.FileName, i.Doc.ContentType, i.Data)).FirstOrDefault());
    }

    [Fact]
    public async Task ChecklistItemsOfATaskAreReorderedByDragging()
    {
        var store = new MemoryWork();
        using var f = Factory("STAFF", store, "TASKS.VIEW");
        var c = Client(f);
        var html = await c.GetStringAsync($"/work/details/{MemoryWork.T1}");
        Assert.Contains("data-ta-step-handle", html);
        Assert.Contains($"data-ta-order=\"checkOrder{MemoryWork.T1:N}\"", html);
        Assert.Contains($"data-ta-item=\"{MemoryWork.I1}\"", html);
        Assert.Contains("data-ta-step-handle", await c.GetStringAsync("/work"));                             // task cards too
        var r = await c.PostAsync($"/work/checklistorder/{MemoryWork.T1}", Form(Token(html), ("items", MemoryWork.I2.ToString()), ("items", MemoryWork.I1.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal([MemoryWork.I2, MemoryWork.I1], store.LastOrder);
    }

    [Fact]
    public async Task TheCataloguePageListsEntriesAsTextAndEditsThemInATabbedPopup()
    {
        var entry = new S.WorkCatalogItem(Guid.Parse("c1000000-0000-0000-0000-000000000001"), "Kéo hóa đơn", "Hóa đơn đầu vào", 10, true, 2, "Tải XML",
            "[{\"title\":\"Tải XML\",\"guide\":\"Trên cổng thuế\"},{\"title\":\"Đối chiếu\"}]") { Used = [new("Nhập phần mềm", 1)] };
        using var f = Factory("ADMIN", new MemoryWork(), "TASKS.VIEW", "TASKS.CATALOG.MANAGE").WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<S.IWorkCatalogStore>();
            s.AddSingleton<S.IWorkCatalogStore>(new OneEntryCatalog(entry));
        }));
        var r = await Client(f).GetAsync("/WorkCatalog");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains(">Kéo hóa đơn</span>", html);                                                         // grid shows text
        Assert.Contains("data-ta-inline-active=\"catFormc1000000000000000000000000000001\"", html);
        Assert.Contains("id=\"catDlgnew\"", html);                                                           // add popup
        Assert.Contains("id=\"catDlgc1000000000000000000000000000001\"", html);                              // edit popup
        Assert.Contains("name=\"Input.Items[1].Title\" value=\"Đối chiếu\"", html);
        Assert.Contains(">Trên cổng thuế</textarea>", html);
        Assert.Contains("data-ta-step-suggest=\"Nhập phần mềm\"", html);
        Assert.Equal(2, Regex.Matches(html, "data-ta-tab=\"checklist\"").Count);
    }

    private sealed class OneEntryCatalog(S.WorkCatalogItem entry) : S.IWorkCatalogStore
    {
        public Task<IReadOnlyList<S.WorkCatalogItem>> ListAsync() => Task.FromResult<IReadOnlyList<S.WorkCatalogItem>>([entry]);
        public Task<S.WorkCatalogResult> SaveAsync(Guid actor, Guid? id, S.WorkCatalogInput input) => Task.FromResult(new S.WorkCatalogResult(S.WorkCatalogOutcome.Saved));
        public Task<S.WorkCatalogResult> DeleteAsync(Guid actor, Guid id) => Task.FromResult(new S.WorkCatalogResult(S.WorkCatalogOutcome.Saved));
        public Task<S.WorkCatalogResult> AppendChecklistAsync(Guid actor, string name, IReadOnlyList<string> items) => Task.FromResult(new S.WorkCatalogResult(S.WorkCatalogOutcome.NoChange));
        public Task<S.WorkGuideIndex> GuidesAsync() => Task.FromResult(S.WorkGuideIndex.Empty);
    }

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryWork store, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IWorkTaskStore>();
                s.AddSingleton<S.IWorkTaskStore>(store);
                s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                store.Permissions = permissions;
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    // In-memory tasks applying WorkTaskPolicy like SqlWorkTaskStore. The actor is employee E1; T1 is owned by E1 (open, one of
    // two checklist items done), T2 is E1's finished task, Hidden belongs to E2 and was created by someone else.
    private sealed class MemoryWork : S.IWorkTaskStore
    {
        public static readonly Guid T1 = Guid.Parse("71000000-0000-0000-0000-000000000001"), T2 = Guid.Parse("72000000-0000-0000-0000-000000000002"),
            Hidden = Guid.Parse("79000000-0000-0000-0000-000000000009"), I1 = Guid.Parse("11000000-0000-0000-0000-000000000001"), I2 = Guid.Parse("12000000-0000-0000-0000-000000000002");
        private readonly object gate = new();
        public string[] Permissions { get; set; } = [];
        public Dictionary<Guid, (Guid Task, string Title, bool Done, DateTime? At)> Items { get; } = new()
        {
            [I1] = (T1, "Thu thập hóa đơn", false, null), [I2] = (T1, "Lập tờ khai", true, new DateTime(2026, 11, 6, 3, 0, 0))
        };
        private readonly Dictionary<Guid, S.WorkTaskRow> tasks = new()
        {
            [T1] = Row(T1, "CV000001", "Lập tờ khai thuế GTGT", "IN_PROGRESS", E1, "Nguyễn Văn An", Actor),
            [T2] = Row(T2, "CV000002", "Việc đã xong", "DONE", E1, "Nguyễn Văn An", Actor),
            [Hidden] = Row(Hidden, "CV000003", "Việc của người khác", "NEW", E2, "Trần Thị Bình", Other)
        };
        private readonly List<(Guid Task, string Body)> comments = [];

        private static S.WorkTaskRow Row(Guid id, string no, string title, string status, Guid owner, string ownerName, Guid createdBy) =>
            new(id, no, title, status, "NORMAL", null, Today.AddDays(2), owner, ownerName, null, 50, DateTime.UtcNow, createdBy, 2, 1, false, 0, null, DateTime.UtcNow, null);

        private bool Has(string p) => Permissions.Contains(p);
        private S.WorkActor A() => new(Actor, E1, "Nguyễn Văn An", Has("TASKS.VIEW_ALL"), Has("TASKS.ASSIGN"), Has("TASKS.CREATE"));
        private bool Sees(S.WorkTaskRow t) => S.WorkTaskPolicy.CanSee(A().ViewAll, t.OwnerId == E1, false, t.CreatedBy == Actor);

        private S.WorkTaskCard CardOf(S.WorkTaskRow t)
        {
            var items = Items.Where(i => i.Value.Task == t.Id).Select(i => new S.WorkChecklistItem(i.Key, i.Value.Title, i.Value.Done, i.Value.At, "An")).ToList();
            var row = t with { ChecklistTotal = items.Count, ChecklistDone = items.Count(i => i.IsDone) };
            return new(row, items, comments.Where(x => x.Task == t.Id).Select(x => new S.WorkActivity(Guid.NewGuid(), "COMMENT", x.Body, null, null, DateTime.UtcNow, "An")).ToList(),
                S.WorkTaskPolicy.CanEdit(A().ViewAll, A().CanAssign, t.CreatedBy == Actor), t.OwnerId == E1);
        }

        public Task<S.WorkActor> ActorAsync(Guid actor) => Task.FromResult(A());
        public Task<IReadOnlyList<S.WorkTaskCard>> MyOpenAsync(Guid actor) { lock (gate) return Task.FromResult<IReadOnlyList<S.WorkTaskCard>>(tasks.Values.Where(t => t.OwnerId == E1 && t.IsOpen).Select(CardOf).ToList()); }
        public Task<S.WorkTaskCard?> CardAsync(Guid actor, Guid id) { lock (gate) return Task.FromResult(tasks.TryGetValue(id, out var t) && Sees(t) ? CardOf(t) : null); }
        public Task<IReadOnlyList<S.WorkTaskRow>> ListAsync(Guid actor, S.WorkTaskFilter filter) { lock (gate) return Task.FromResult<IReadOnlyList<S.WorkTaskRow>>(tasks.Values.Where(Sees).ToList()); }
        public Task<S.WorkTaskDetail?> GetAsync(Guid actor, Guid id)
        {
            lock (gate)
            {
                if (!tasks.TryGetValue(id, out var t) || !Sees(t)) return Task.FromResult<S.WorkTaskDetail?>(null);
                var c = CardOf(t);
                return Task.FromResult<S.WorkTaskDetail?>(new(c.Task, null, null, null, null, "An", [], c.Checklist, c.Recent, c.CanEdit, c.IsOwner, "00", null));
            }
        }
        public List<S.WorkCatalogEntry> Catalog { get; } = [];
        public Task<S.WorkLookups> LookupsAsync() => Task.FromResult(new S.WorkLookups([new(E1, "NV000001", "Nguyễn Văn An", true), new(E2, "NV000002", "Trần Thị Bình", false)], [], [], null, Catalog));
        public Task<S.WorkResult> CreateAsync(Guid actor, S.WorkTaskInput input) => Task.FromResult(new S.WorkResult(S.WorkOutcome.Forbidden));
        public Task<S.WorkResult> UpdateAsync(Guid actor, Guid id, S.WorkTaskInput input, string stamp) => Task.FromResult(new S.WorkResult(S.WorkOutcome.Forbidden));
        public Task<S.WorkResult> DeleteAsync(Guid actor, Guid id) => Task.FromResult(new S.WorkResult(S.WorkOutcome.Forbidden));

        private S.WorkResult Act(Guid id, Func<S.WorkTaskRow, S.WorkResult> act)
        {
            lock (gate) return tasks.TryGetValue(id, out var t) && Sees(t) ? act(t) : new(S.WorkOutcome.NotFound);
        }

        public Task<S.WorkResult> SetStatusAsync(Guid actor, Guid id, string status, string? note = null) => Task.FromResult(Act(id, t =>
        {
            var items = Items.Values.Where(i => i.Task == id).ToList();
            if (status == "DONE" && S.WorkTaskPolicy.CheckDone(items.Count, items.Count(i => i.Done)) != S.WorkOutcome.Saved)
                return new(S.WorkOutcome.ChecklistIncomplete, id, $"Còn {items.Count(i => !i.Done)} mục checklist chưa hoàn thành.");
            if (status == "DONE" && S.WorkTaskPolicy.DoneNote(note) is null) return new(S.WorkOutcome.Invalid, id, S.WorkTaskPolicy.DoneNoteRequired);
            tasks[id] = t with { Status = status };
            return new(S.WorkOutcome.Saved, id);
        }));
        public Task<S.WorkResult> AddChecklistAsync(Guid actor, Guid id, string title) => Task.FromResult(Act(id, t => { Items[Guid.NewGuid()] = (id, title, false, null); return new(S.WorkOutcome.Saved, id); }));
        public Task<S.WorkResult> SetChecklistAsync(Guid actor, Guid id, Guid itemId, bool done) => Task.FromResult(Act(id, t =>
        {
            if (!Items.TryGetValue(itemId, out var i) || i.Task != id) return new(S.WorkOutcome.NotFound);
            Items[itemId] = i with { Done = done, At = done ? DateTime.UtcNow : null };
            return new(S.WorkOutcome.Saved, id);
        }));
        public Task<S.WorkResult> RemoveChecklistAsync(Guid actor, Guid id, Guid itemId) => Task.FromResult(Act(id, t => Items.Remove(itemId) ? new(S.WorkOutcome.Saved, id) : new(S.WorkOutcome.NotFound)));
        public List<Guid> LastOrder { get; } = [];
        public Task<S.WorkResult> OrderChecklistAsync(Guid actor, Guid id, IReadOnlyList<Guid> items) => Task.FromResult(Act(id, t => { LastOrder.Clear(); LastOrder.AddRange(items); return new(S.WorkOutcome.Saved, id); }));
        public Task<S.WorkResult> CommentAsync(Guid actor, Guid id, string body) => Task.FromResult(Act(id, t => { comments.Add((id, body)); return new(S.WorkOutcome.Saved, id); }));
        public Task<IReadOnlyList<S.WorkTemplateRow>> TemplatesAsync() => Task.FromResult<IReadOnlyList<S.WorkTemplateRow>>([]);
        public Task<S.WorkTemplateRow?> TemplateAsync(Guid id) => Task.FromResult<S.WorkTemplateRow?>(null);
        public Task<S.WorkResult> SaveTemplateAsync(Guid actor, Guid? id, S.WorkTemplateInput input, string? stamp) => Task.FromResult(new S.WorkResult(S.WorkOutcome.Forbidden));
        public Task<S.WorkResult> DeleteTemplateAsync(Guid actor, Guid id) => Task.FromResult(new S.WorkResult(S.WorkOutcome.NotFound));
        public Task<int> GenerateAsync(DateOnly today) => Task.FromResult(0);
        public Task<IReadOnlyList<S.WorkReportRow>> ReportAsync(DateOnly from, DateOnly to, DateOnly today, Guid? department) =>
            Task.FromResult<IReadOnlyList<S.WorkReportRow>>([new(E1, "NV000001", "Nguyễn Văn An", "Kế toán", 3, 1, 2, 1, 0, 1, 50)]);
        public Task<IReadOnlyList<S.WorkDepartment>> DepartmentsAsync() => Task.FromResult<IReadOnlyList<S.WorkDepartment>>([]);
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-work-test"), new("display_name", "Nguyễn Văn An"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
