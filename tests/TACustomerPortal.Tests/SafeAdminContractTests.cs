extern alias admin;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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

// CONTRACTS-AND-TASKS: Admin → Quản lý khách hàng → Hợp đồng. No SQL host or connection: the contract store is an in-memory
// fake applying the same ContractPolicy rules as SqlContractStore. Real SQL (locks, RowVersion, filtered unique index) is
// NOT exercised here; the SQL text is checked for its guards instead.
public sealed class SafeAdminContractTests
{
    private const string TestScheme = "SafeContractTest";
    private static readonly Guid Actor = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid C1 = Guid.Parse("c1000000-0000-0000-0000-0000000000c1"), C2 = Guid.Parse("c2000000-0000-0000-0000-0000000000c2");
    private static readonly Guid E1 = Guid.Parse("e1000000-0000-0000-0000-0000000000e1");
    private static readonly Guid K1 = Guid.Parse("a1000000-0000-0000-0000-0000000000a1");
    private static readonly DateTime Today = DateTime.Today;
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    // ---- Policy ------------------------------------------------------------------------------------------------
    [Fact]
    public void PolicyValidatesRequiredFieldsDatesAndValue()
    {
        S.ContractInput Valid() => new() { CustomerId = C1, ContractNo = "  HD  01/2026 ", Title = " Dịch vụ kế toán ", Status = "active", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), ContractValue = 12_000_000 };
        var ok = Valid();
        ok.Normalize();
        Assert.Empty(S.ContractPolicy.Validate(ok));
        Assert.Equal(("HD 01/2026", "Dịch vụ kế toán", "ACTIVE"), (ok.ContractNo, ok.Title, ok.Status));

        string[] Keys(Action<S.ContractInput> change) { var i = Valid(); change(i); i.Normalize(); return S.ContractPolicy.Validate(i).Select(e => e.Key).ToArray(); }
        Assert.Equal(["Input.CustomerId"], Keys(i => i.CustomerId = null));
        Assert.Equal(["Input.ContractNo"], Keys(i => i.ContractNo = "   "));
        Assert.Equal(["Input.ContractNo"], Keys(i => i.ContractNo = new string('X', 51)));
        Assert.Equal(["Input.Title"], Keys(i => i.Title = null));
        Assert.Equal(["Input.EndDate"], Keys(i => i.EndDate = new DateTime(2025, 12, 31)));          // end before start
        Assert.Empty(Keys(i => i.EndDate = i.StartDate));                                             // same day is allowed
        Assert.Equal(["Input.SignedDate"], Keys(i => i.SignedDate = new DateTime(1899, 1, 1)));
        Assert.Equal(["Input.ContractValue"], Keys(i => i.ContractValue = -1));
        Assert.Equal(["Input.ContractValue"], Keys(i => i.ContractValue = 10.5m));                    // decimal(18,0): whole đồng
        Assert.Equal(["Input.ContractValue"], Keys(i => i.ContractValue = 1_000_000_000_000_000_000m));
        Assert.Empty(Keys(i => i.ContractValue = 0));
        Assert.Equal(["Input.Status"], Keys(i => i.Status = "APPROVED"));
        Assert.Equal(["Input.BillingCycle"], Keys(i => i.BillingCycle = "WEEKLY"));
        Assert.Empty(Keys(i => i.BillingCycle = "quarterly"));

        Assert.Equal(["Nháp", "Đang hiệu lực", "Hết hạn", "Thanh lý"], S.ContractPolicy.Statuses.Select(s => s.Label));
        Assert.Equal(["Một lần", "Hàng tháng", "Hàng quý", "Hàng năm"], S.ContractPolicy.BillingCycles.Select(s => s.Label));
        Assert.Equal(["DRAFT", "ACTIVE", "EXPIRED", "TERMINATED"], S.ContractPolicy.Statuses.Select(s => s.Code));
        Assert.Equal(["ONE_TIME", "MONTHLY", "QUARTERLY", "YEARLY"], S.ContractPolicy.BillingCycles.Select(s => s.Code));

        Assert.True(S.ContractPolicy.IsExpiringSoon("ACTIVE", Today.AddDays(30), Today));
        Assert.True(S.ContractPolicy.IsExpiringSoon("ACTIVE", Today, Today));
        Assert.False(S.ContractPolicy.IsExpiringSoon("ACTIVE", Today.AddDays(31), Today));
        Assert.False(S.ContractPolicy.IsExpiringSoon("ACTIVE", Today.AddDays(-1), Today));        // already ended
        Assert.False(S.ContractPolicy.IsExpiringSoon("DRAFT", Today.AddDays(5), Today));
        Assert.False(S.ContractPolicy.IsExpiringSoon("ACTIVE", null, Today));
        Assert.Equal("%50\\%\\_x\\[%", S.ContractPolicy.Pattern(" 50%_x[ "));
    }

    [Fact]
    public void FilesAreCheckedByExtensionSizeAndSignature()
    {
        Assert.Equal(("application/pdf", (string?)null), S.ContractPolicy.CheckFile("hop-dong.PDF", Pdf));
        Assert.Equal("image/png", S.ContractPolicy.CheckFile("scan.png", Png).ContentType);
        Assert.Equal("image/jpeg", S.ContractPolicy.CheckFile("scan.jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]).ContentType);
        Assert.Equal("image/jpeg", S.ContractPolicy.CheckFile("scan.jpg", [0xFF, 0xD8, 0xFF, 0xE1]).ContentType);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", S.ContractPolicy.CheckFile("hd.docx", Office("word/document.xml")).ContentType);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", S.ContractPolicy.CheckFile("pl.xlsx", Office("xl/workbook.xml")).ContentType);

        Assert.Null(S.ContractPolicy.CheckFile("hd.docx", Office("xl/workbook.xml")).ContentType);   // an XLSX renamed .docx
        Assert.Null(S.ContractPolicy.CheckFile("hd.pdf", Png).ContentType);                          // content does not match the extension
        Assert.Null(S.ContractPolicy.CheckFile("hd.xlsx", Encoding.ASCII.GetBytes("PK\u0003\u0004garbage")).ContentType);
        Assert.Contains("Chỉ nhận file", S.ContractPolicy.CheckFile("virus.exe", Pdf).Error);
        Assert.Contains("Chỉ nhận file", S.ContractPolicy.CheckFile("hd.pdf.html", Pdf).Error);
        Assert.Contains("Chỉ nhận file", S.ContractPolicy.CheckFile("hd.doc", Pdf).Error);
        Assert.Contains("Chọn file", S.ContractPolicy.CheckFile("hd.pdf", []).Error);
        var big = new byte[S.ContractPolicy.MaxFileBytes + 1];
        Pdf.CopyTo(big, 0);
        Assert.Contains("10 MB", S.ContractPolicy.CheckFile("hd.pdf", big).Error);
        var max = new byte[S.ContractPolicy.MaxFileBytes];
        Pdf.CopyTo(max, 0);
        Assert.NotNull(S.ContractPolicy.CheckFile("hd.pdf", max).ContentType);
        Assert.Equal(10485760, S.ContractPolicy.MaxFileBytes);                                       // CK_CustomerContractFiles_Size

        Assert.Equal("hop-dong.pdf", S.ContractPolicy.SafeFileName(@"C:\Users\x\Desktop\hop-dong.pdf"));
        Assert.Equal("passwd", S.ContractPolicy.SafeFileName("../../etc/passwd"));
        Assert.Equal("a b.pdf", S.ContractPolicy.SafeFileName("a\r\n b\".pdf"));
        var longName = S.ContractPolicy.SafeFileName(new string('x', 300) + ".pdf");
        Assert.Equal(255, longName.Length);
        Assert.EndsWith(".pdf", longName);
    }

    // ---- SQL guards ----------------------------------------------------------------------------------------------
    [Fact]
    public void ContractSqlGuardsWritesAndStaysOnSql2014()
    {
        Assert.Contains("RowVersion=@rv", S.ContractSql.Update);
        Assert.Contains("RowVersion=@rv", S.ContractSql.SoftDelete);
        Assert.Contains("IsDeleted=1", S.ContractSql.SoftDelete);
        Assert.Contains("IsDeleted=1", S.ContractSql.SoftDeleteFile);
        Assert.Contains("ContractId=@id", S.ContractSql.SoftDeleteFile);                  // a file id of another contract is never touched
        Assert.Contains("UPDLOCK", S.ContractSql.Lock);
        Assert.Contains("IsDeleted=0", S.ContractSql.NoTaken);                               // matches the filtered unique index
        Assert.Contains("ESCAPE '\\'", S.ContractSql.List);
        Assert.Contains("c.IsDeleted=0", S.ContractSql.List);
        Assert.Contains("DATEADD(day,30,@today)", S.ContractSql.List);
        Assert.Equal(30, S.ContractPolicy.ExpiringDays);
        Assert.Contains("c.IsDeleted=0", S.ContractSql.Download);
        Assert.Contains("f.ContractId=@id", S.ContractSql.Download);
        Assert.Contains("IsDeleted=0 AND IsActive=1", S.ContractSql.Employees);
        Assert.Contains("IsDeleted=0", S.ContractSql.Customers);
        foreach (var read in new[] { S.ContractSql.List, S.ContractSql.Contract, S.ContractSql.Files }) Assert.DoesNotContain("Data", read); // file bytes only on download
        var all = string.Join("\n", typeof(S.ContractSql).GetFields().Where(f => f.FieldType == typeof(string)).Select(f => (string)f.GetValue(null)!));
        Assert.DoesNotContain("DELETE FROM", all);                                            // soft delete only
        Assert.DoesNotMatch(@"(?i)\bDELETE\s+dbo\.", all);
        Assert.DoesNotContain("STRING_AGG", all);
        Assert.DoesNotMatch(@"\bOFFSET\b|\bFETCH\b|CREATE OR ALTER|DROP\s", all);
    }

    [Fact]
    public void Migration031ContainsTheContractCodesOfTheCatalogue()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TAPortal.sln"))) root = root.Parent!;
        var migration = File.ReadAllText(Path.Combine(root.FullName, "database", "migrations", "031-contracts-and-work-tasks.sql"));
        var module = AdminSecurity.AdminPermissions.Modules.Single(m => m.Key == AdminSecurity.AdminPermissions.ContractsModuleKey);
        var expected = module.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["CONTRACTS.CREATE", "CONTRACTS.DELETE", "CONTRACTS.UPDATE", "CONTRACTS.VIEW"], expected);
        Assert.Equal(expected, Regex.Matches(migration, @"\('(CONTRACTS\.[A-Z_]+)',").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.All(expected, c => Assert.Contains(c, AdminSecurity.AdminPermissions.Migration031Codes));
        Assert.Contains("dbo.CustomerContracts", migration);
        Assert.Contains("dbo.CustomerContractFiles", migration);
    }

    [Fact]
    public void ContractViewsUseVoiceInputAndHardFileForms()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TAPortal.sln"))) root = root.Parent!;
        var dir = Path.Combine(root.FullName, "src", "TAPortal.Web", "Views", "Contracts");
        var textareas = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cshtml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "<textarea\\b[^>]*>"))
            {
                textareas++;
                Assert.Contains("data-ta-voice", m.Value);
            }
        Assert.Equal(2, textareas);
        var edit = File.ReadAllText(Path.Combine(dir, "Edit.cshtml"));
        Assert.Matches("asp-for=\"Input.Title\"[^>]*data-ta-voice", edit);
        var details = File.ReadAllText(Path.Combine(dir, "Details.cshtml"));
        Assert.Matches("enctype=\"multipart/form-data\" data-ta-hard", details);
        Assert.Matches("asp-action=\"Download\"[^>]*download data-ta-hard", details);
    }

    // ---- HTTP: permissions -----------------------------------------------------------------------------------------
    [Theory]
    [InlineData("GET", "/contracts", "AUTH.ROLES.VIEW")]
    [InlineData("GET", "/contracts/details/a1000000-0000-0000-0000-0000000000a1", "CRM.CUSTOMERS.VIEW")]
    [InlineData("GET", "/contracts/download/a1000000-0000-0000-0000-0000000000a1?fileId=f1000000-0000-0000-0000-0000000000f1", "CRM.CUSTOMERS.VIEW")]
    [InlineData("GET", "/contracts/create", "CONTRACTS.VIEW")]
    [InlineData("POST", "/contracts/create", "CONTRACTS.VIEW")]
    [InlineData("GET", "/contracts/edit/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.VIEW")]
    [InlineData("POST", "/contracts/edit/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.VIEW")]
    [InlineData("POST", "/contracts/delete/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.VIEW")]
    [InlineData("POST", "/contracts/delete/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.UPDATE")]
    [InlineData("POST", "/contracts/uploadfile/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.VIEW")]
    [InlineData("POST", "/contracts/deletefile/a1000000-0000-0000-0000-0000000000a1", "CONTRACTS.VIEW")]
    public async Task UngrantedContractActionsAreForbidden(string method, string path, string permission)
    {
        var store = new MemoryContracts();
        using var f = Factory("STAFF", store, permission == "CONTRACTS.UPDATE" ? ["CONTRACTS.VIEW", permission] : [permission]);
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? new FormUrlEncodedContent([new("Input.Title", "x")]) : null };
        var response = await Client(f).SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ViewOnlySeesListAndDetailsWithoutWriteControls()
    {
        var store = new MemoryContracts();
        using var f = Factory("STAFF", store, "CONTRACTS.VIEW");
        var c = Client(f);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/contracts"));
        Assert.Contains("data-ta-contract=\"HD-001\"", list);
        Assert.Contains("Công ty An Phát", list);
        Assert.DoesNotContain("data-ta-contract-create", list);
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/details/{K1}"));
        Assert.Contains("Dịch vụ kế toán trọn gói", details);
        Assert.Contains("Đang hiệu lực", details);
        Assert.DoesNotContain("data-ta-contract-upload", details);
        Assert.DoesNotContain("/Contracts/DeleteFile/", details);
        Assert.DoesNotContain("/Contracts/Edit/", details);
        Assert.DoesNotContain("/Contracts/Delete/", details);
        var nav = Nav(list);
        Assert.Matches("aria-current=\"page\"[^>]*href=\"/Contracts\"", nav);
        Assert.Contains("Hợp đồng", nav);
        Assert.Equal("Contracts", AdminSecurity.AdminNavigation.FirstAllowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", "CONTRACTS.VIEW")], "t")))!.Controller);
        using var other = Factory("STAFF", new MemoryContracts(), "CRM.CUSTOMERS.VIEW");
        Assert.DoesNotContain("href=\"/Contracts\"", Nav(await Client(other).GetStringAsync("/account/accessdenied")));
    }

    // ---- HTTP: create, list, details, files -----------------------------------------------------------------------
    [Fact]
    public async Task CreateListAndDetails()
    {
        var store = new MemoryContracts();
        using var f = Factory("STAFF", store, "CONTRACTS.VIEW", "CONTRACTS.CREATE");
        var c = Client(f);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/create?customerId={C2}"));
        Assert.Matches($"<option value=\"{C2}\" selected=\"selected\"", form);              // ?customerId= preselects
        var token = Token(form);

        var missing = await c.PostAsync("/contracts/create", Form(token, ("Input.CustomerId", C2.ToString()), ("Input.ContractNo", "HD-002"), ("Input.Status", "DRAFT")));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("Vui lòng nhập tên hợp đồng.", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
        var dates = await c.PostAsync("/contracts/create", Form(token, ("Input.CustomerId", C2.ToString()), ("Input.ContractNo", "HD-002"), ("Input.Title", "Tư vấn thuế"),
            ("Input.Status", "ACTIVE"), ("Input.StartDate", "2026-06-01"), ("Input.EndDate", "2026-05-01")));
        Assert.Equal(HttpStatusCode.BadRequest, dates.StatusCode);
        Assert.Contains("Ngày kết thúc phải từ ngày bắt đầu trở đi.", WebUtility.HtmlDecode(await dates.Content.ReadAsStringAsync()));
        var value = await c.PostAsync("/contracts/create", Form(token, ("Input.CustomerId", C2.ToString()), ("Input.ContractNo", "HD-002"), ("Input.Title", "Tư vấn thuế"),
            ("Input.Status", "ACTIVE"), ("Input.ContractValue", "-5")));
        Assert.Equal(HttpStatusCode.BadRequest, value.StatusCode);
        var dup = await c.PostAsync("/contracts/create", Form(token, ("Input.CustomerId", C2.ToString()), ("Input.ContractNo", "hd-001"), ("Input.Title", "Tư vấn thuế"), ("Input.Status", "DRAFT")));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("Số hợp đồng đã tồn tại", WebUtility.HtmlDecode(await dup.Content.ReadAsStringAsync()));
        Assert.Equal(0, store.Writes);

        var end = Today.AddDays(10).ToString("yyyy-MM-dd");
        var ok = await c.PostAsync("/contracts/create", Form(token, ("Input.CustomerId", C2.ToString()), ("Input.ContractNo", " HD-002 "), ("Input.Title", "Tư vấn thuế năm 2026"),
            ("Input.ServiceDescription", "Quyết toán thuế TNDN"), ("Input.Status", "ACTIVE"), ("Input.StartDate", Today.AddDays(-355).ToString("yyyy-MM-dd")), ("Input.EndDate", end),
            ("Input.ContractValue", "24000000"), ("Input.BillingCycle", "QUARTERLY"), ("Input.OwnerEmployeeId", E1.ToString()), ("Input.Note", "Gia hạn hằng năm")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var created = store.Items.Single(x => x.ContractNo == "HD-002");
        Assert.EndsWith($"/Contracts/Details/{created.Id}", ok.Headers.Location!.OriginalString);
        Assert.Equal((C2, 24_000_000m, "QUARTERLY", "ACTIVE", (Guid?)E1), (created.CustomerId, created.ContractValue!.Value, created.BillingCycle!, created.Status, created.OwnerEmployeeId));

        var list = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts?customerId={C2}"));
        Assert.Contains("data-ta-contract=\"HD-002\"", list);
        Assert.DoesNotContain("data-ta-contract=\"HD-001\"", list);
        Assert.Contains("data-ta-expiring", list);
        Assert.Contains("24.000.000", list);
        var expiring = WebUtility.HtmlDecode(await c.GetStringAsync("/contracts?expiring=true"));
        Assert.Contains("data-ta-contract=\"HD-002\"", expiring);
        Assert.DoesNotContain("data-ta-contract=\"HD-001\"", expiring);                       // ends in 200 days
        var search = WebUtility.HtmlDecode(await c.GetStringAsync("/contracts?q=k%E1%BA%BF+to%C3%A1n"));
        Assert.Contains("data-ta-contract=\"HD-001\"", search);
        Assert.DoesNotContain("data-ta-contract=\"HD-002\"", search);
        var drafts = WebUtility.HtmlDecode(await c.GetStringAsync("/contracts?status=DRAFT"));
        Assert.DoesNotContain("data-ta-contract=", drafts);

        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/details/{created.Id}"));
        Assert.Contains("Tư vấn thuế năm 2026", details);
        Assert.Contains("Hàng quý", details);
        Assert.Contains("Nguyễn Văn Phụ Trách", details);
        Assert.Contains("Sắp hết hạn", details);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/contracts/details/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task EditUsesTheStampAndDeleteIsSoft()
    {
        var store = new MemoryContracts();
        using var f = Factory("STAFF", store, "CONTRACTS.VIEW", "CONTRACTS.UPDATE", "CONTRACTS.DELETE");
        var c = Client(f);
        var form = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/edit/{K1}"));
        var stamp = Regex.Match(form, "name=\"Input.Stamp\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(stamp);
        var stale = await c.PostAsync($"/contracts/edit/{K1}", Form(Token(form), ("Input.Stamp", "STALE"), ("Input.CustomerId", C1.ToString()), ("Input.ContractNo", "HD-001"),
            ("Input.Title", "Đổi tên"), ("Input.Status", "ACTIVE")));
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        Assert.Contains("vừa được thay đổi", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        var saved = await c.PostAsync($"/contracts/edit/{K1}", Form(Token(form), ("Input.Stamp", stamp), ("Input.CustomerId", C1.ToString()), ("Input.ContractNo", "HD-001"),
            ("Input.Title", "Đổi tên"), ("Input.Status", "TERMINATED")));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(("Đổi tên", "TERMINATED"), (store.Items.Single().Title, store.Items.Single().Status));

        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/details/{K1}"));
        Assert.Contains("Thanh lý", details);
        var newStamp = Regex.Match(details, "name=\"stamp\" value=\"([^\"]+)\"").Groups[1].Value;
        var staleDelete = await c.PostAsync($"/contracts/delete/{K1}", Form(Token(details), ("stamp", stamp)));
        Assert.Equal(HttpStatusCode.Redirect, staleDelete.StatusCode);
        Assert.False(store.Items.Single().Deleted);
        var deleted = await c.PostAsync($"/contracts/delete/{K1}", Form(Token(details), ("stamp", newStamp)));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.True(store.Items.Single().Deleted);                                            // soft: the row stays
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/contracts/details/{K1}")).StatusCode);
    }

    [Fact]
    public async Task UploadDownloadAndRemoveFiles()
    {
        var store = new MemoryContracts();
        using var f = Factory("STAFF", store, "CONTRACTS.VIEW", "CONTRACTS.UPDATE");
        var c = Client(f);
        var details = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/details/{K1}"));
        Assert.Contains("data-ta-contract-upload", details);
        var token = Token(details);

        var bad = await c.PostAsync($"/contracts/uploadfile/{K1}", Upload(token, "hop-dong.pdf", Png));
        Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
        Assert.Empty(store.Files);
        var exe = await c.PostAsync($"/contracts/uploadfile/{K1}", Upload(token, "tool.exe", Pdf));
        Assert.Equal(HttpStatusCode.Redirect, exe.StatusCode);
        Assert.Empty(store.Files);

        var ok = await c.PostAsync($"/contracts/uploadfile/{K1}", Upload(token, @"C:\scan\hop dong ky.pdf", Pdf));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var file = Assert.Single(store.Files);
        Assert.Equal(("hop dong ky.pdf", "application/pdf", Pdf.Length), (file.Info.FileName, file.Info.ContentType, file.Info.SizeBytes));

        var page = WebUtility.HtmlDecode(await c.GetStringAsync($"/contracts/details/{K1}"));
        Assert.Contains("hop dong ky.pdf", page);
        var download = await c.GetAsync($"/contracts/download/{K1}?fileId={file.Info.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/contracts/download/{Guid.NewGuid()}?fileId={file.Info.Id}")).StatusCode);

        var removed = await c.PostAsync($"/contracts/deletefile/{K1}", Form(Token(page), ("fileId", file.Info.Id.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.True(file.Deleted);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/contracts/download/{K1}?fileId={file.Info.Id}")).StatusCode);
    }

    // ---- Host and helpers ----------------------------------------------------------------------------------------
    private static byte[] Office(string part)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "[Content_Types].xml", part })
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write("<x/>");
            }
        }
        return ms.ToArray();
    }

    private static string Nav(string html)
    {
        html = WebUtility.HtmlDecode(html);
        var start = html.IndexOf("id=\"taNav\"", StringComparison.Ordinal);
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
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

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(string role, MemoryContracts contracts, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IContractStore>();
                s.AddSingleton<S.IContractStore>(contracts);
                s.AddSingleton(new TestPrincipal(role, permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    // In-memory contracts applying ContractPolicy like SqlContractStore. The actor is treated as holding the permission of
    // each call (the SQL store re-checks it in the transaction).
    private sealed class MemoryContracts : S.IContractStore
    {
        public sealed class Row
        {
            public Guid Id; public Guid CustomerId; public string ContractNo = ""; public string Title = ""; public string? ServiceDescription;
            public DateTime? SignedDate, StartDate, EndDate; public decimal? ContractValue; public string? BillingCycle; public string Status = "DRAFT";
            public Guid? OwnerEmployeeId; public string? Note; public bool Deleted; public string Stamp = NewStamp(); public DateTime CreatedAt = DateTime.UtcNow; public DateTime? UpdatedAt;
        }

        public sealed class FileRow(Guid contractId, S.ContractFileInfo info, byte[] data)
        {
            public Guid ContractId { get; } = contractId;
            public S.ContractFileInfo Info { get; } = info;
            public byte[] Data { get; } = data;
            public bool Deleted { get; set; }
        }

        private static readonly S.ContractLookups Lookups = new(
            [new(C1, "KH001", "Công ty An Phát"), new(C2, "KH002", "Công ty Bình Minh")],
            [new(E1, "NV001", "Nguyễn Văn Phụ Trách")]);

        public List<Row> Items { get; } =
        [
            new() { Id = K1, CustomerId = C1, ContractNo = "HD-001", Title = "Dịch vụ kế toán trọn gói", Status = "ACTIVE", StartDate = Today.AddDays(-100), EndDate = Today.AddDays(200), ContractValue = 60_000_000, BillingCycle = "MONTHLY" }
        ];

        public List<FileRow> Files { get; } = [];
        public int Writes { get; private set; }

        private static string NewStamp() => Guid.NewGuid().ToString("N").ToUpperInvariant();

        private S.ContractListItem Item(Row r) => new(r.Id, r.CustomerId, Lookups.Customers.First(x => x.Id == r.CustomerId).Code, Lookups.Customers.First(x => x.Id == r.CustomerId).Name,
            r.ContractNo, r.Title, r.SignedDate, r.StartDate, r.EndDate, r.ContractValue, r.BillingCycle, r.Status, r.OwnerEmployeeId,
            Lookups.Employees.FirstOrDefault(e => e.Id == r.OwnerEmployeeId)?.FullName, Files.Count(f => f.ContractId == r.Id && !f.Deleted));

        private S.ContractDetail Detail(Row r)
        {
            var i = Item(r);
            return new(r.Id, r.CustomerId, i.CustomerCode, i.CustomerName, r.ContractNo, r.Title, r.ServiceDescription, r.SignedDate, r.StartDate, r.EndDate, r.ContractValue,
                r.BillingCycle, r.Status, r.OwnerEmployeeId, i.OwnerName, r.Note, r.CreatedAt, r.UpdatedAt, r.Stamp,
                Files.Where(f => f.ContractId == r.Id && !f.Deleted).Select(f => f.Info).ToList());
        }

        private Row? Live(Guid id) => Items.FirstOrDefault(x => x.Id == id && !x.Deleted);

        public Task<IReadOnlyList<S.ContractListItem>> ListAsync(S.ContractFilter filter, DateTime today) =>
            Task.FromResult<IReadOnlyList<S.ContractListItem>>(Items.Where(x => !x.Deleted).Select(Item).Where(x => filter.Matches(x, today)).ToList());

        public Task<S.ContractDetail?> GetAsync(Guid id) => Task.FromResult(Live(id) is { } r ? Detail(r) : null);

        public Task<S.ContractLookups> LookupsAsync() => Task.FromResult(Lookups);

        private S.ContractOutcome? Check(S.ContractInput input, Guid? id)
        {
            input.Normalize();
            if (S.ContractPolicy.Validate(input).Count > 0) return S.ContractOutcome.Invalid;
            if (Lookups.Customers.All(c => c.Id != input.CustomerId) || (input.OwnerEmployeeId is Guid o && Lookups.Employees.All(e => e.Id != o))) return S.ContractOutcome.InvalidReference;
            if (Items.Any(x => !x.Deleted && x.Id != id && string.Equals(x.ContractNo, input.ContractNo, StringComparison.OrdinalIgnoreCase))) return S.ContractOutcome.DuplicateNo;
            return null;
        }

        private static void Apply(Row r, S.ContractInput i)
        {
            r.CustomerId = i.CustomerId!.Value; r.ContractNo = i.ContractNo!; r.Title = i.Title!; r.ServiceDescription = i.ServiceDescription; r.SignedDate = i.SignedDate;
            r.StartDate = i.StartDate; r.EndDate = i.EndDate; r.ContractValue = i.ContractValue; r.BillingCycle = i.BillingCycle; r.Status = i.Status!;
            r.OwnerEmployeeId = i.OwnerEmployeeId; r.Note = i.Note; r.Stamp = NewStamp();
        }

        public Task<S.ContractResult> CreateAsync(Guid actorId, S.ContractInput input, string? ip)
        {
            if (Check(input, null) is { } bad) return Task.FromResult(new S.ContractResult(bad));
            var row = new Row { Id = Guid.NewGuid() };
            Apply(row, input);
            Items.Add(row);
            Writes++;
            return Task.FromResult(new S.ContractResult(S.ContractOutcome.Saved, row.Id, true));
        }

        public Task<S.ContractResult> UpdateAsync(Guid actorId, Guid id, S.ContractInput input, string? ip)
        {
            if (Live(id) is not { } row) return Task.FromResult(new S.ContractResult(S.ContractOutcome.NotFound));
            if (row.Stamp != input.Stamp) return Task.FromResult(new S.ContractResult(S.ContractOutcome.Conflict, id));
            if (Check(input, id) is { } bad) return Task.FromResult(new S.ContractResult(bad, id));
            if (S.ContractPolicy.Unchanged(Detail(row), input)) return Task.FromResult(new S.ContractResult(S.ContractOutcome.NoChange, id));
            Apply(row, input);
            row.UpdatedAt = DateTime.UtcNow;
            Writes++;
            return Task.FromResult(new S.ContractResult(S.ContractOutcome.Saved, id, true));
        }

        public Task<S.ContractResult> DeleteAsync(Guid actorId, Guid id, string? stamp, string? ip)
        {
            if (Live(id) is not { } row) return Task.FromResult(new S.ContractResult(S.ContractOutcome.NotFound));
            if (row.Stamp != stamp) return Task.FromResult(new S.ContractResult(S.ContractOutcome.Conflict, id));
            row.Deleted = true;
            Writes++;
            return Task.FromResult(new S.ContractResult(S.ContractOutcome.Saved, id, true));
        }

        public Task<S.ContractResult> AddFileAsync(Guid actorId, Guid contractId, S.ContractUpload file, string? ip)
        {
            if (S.ContractPolicy.CheckFile(file.FileName, file.Data).ContentType is not string type) return Task.FromResult(new S.ContractResult(S.ContractOutcome.Invalid));
            if (Live(contractId) is null) return Task.FromResult(new S.ContractResult(S.ContractOutcome.NotFound));
            var info = new S.ContractFileInfo(Guid.NewGuid(), S.ContractPolicy.SafeFileName(file.FileName), type, file.Data.Length, DateTime.UtcNow);
            Files.Add(new FileRow(contractId, info, file.Data));
            Writes++;
            return Task.FromResult(new S.ContractResult(S.ContractOutcome.Saved, info.Id, true));
        }

        public Task<S.ContractResult> DeleteFileAsync(Guid actorId, Guid contractId, Guid fileId, string? ip)
        {
            var file = Files.FirstOrDefault(f => f.Info.Id == fileId && f.ContractId == contractId && !f.Deleted);
            if (Live(contractId) is null || file is null) return Task.FromResult(new S.ContractResult(S.ContractOutcome.NotFound));
            file.Deleted = true;
            Writes++;
            return Task.FromResult(new S.ContractResult(S.ContractOutcome.Saved, fileId, true));
        }

        public Task<S.ContractFileContent?> DownloadAsync(Guid contractId, Guid fileId)
        {
            var file = Files.FirstOrDefault(f => f.Info.Id == fileId && f.ContractId == contractId && !f.Deleted);
            return Task.FromResult(Live(contractId) is null || file is null ? null : new S.ContractFileContent(file.Info.FileName, file.Info.ContentType, file.Data));
        }
    }

    private sealed record TestPrincipal(string Role, string[] Permissions);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestPrincipal principal)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-contract-test"), new("display_name", "Hợp đồng"), new(ClaimTypes.Role, principal.Role) };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
