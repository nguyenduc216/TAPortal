extern alias admin;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security;
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
using AdminServices = admin::TAPortal.Web.Services;

namespace TACustomerPortal.Tests;

// Admin Customers → "Nhập từ Excel" (CUSTOMER-PROFILE phase 2). The workbook is built in memory with the same layout
// as the internal "Thông tin khách hàng.xlsx" (fictitious data only). No SQL: the import store is a fake that records
// what the controller asks it to save.
public sealed class SafeCustomerImportTests
{
    private const string TestScheme = "SafeCustomerImport";
    private const string Secret = "Th1s-is-a-TEST!";
    private static readonly Guid Actor = Guid.Parse("11111111-2222-3333-4444-555555555556");
    private static readonly Guid Existing = Guid.Parse("cccccccc-0000-0000-0000-0000000000c3");

    // ---- parser -------------------------------------------------------------------------------------------

    [Fact]
    public void ParserReadsBlocksByLabelAndRepairsExcelNumbers()
    {
        var list = Parse(Workbook());
        var c = Assert.Single(list);
        Assert.Equal("01.Demo", c.SheetName);
        Assert.Equal("DEMO", c.SuggestedCode);
        Assert.Equal("0312345678", c.TaxCode);                     // stored as a number: the leading 0 is restored
        Assert.Contains(c.Warnings, w => w.Contains("mất số 0 đầu"));
        Assert.Equal("Công ty TNHH Demo", c.Name);
        Assert.Equal("ketoan@demo.example", c.Email);
        Assert.Equal("LLC_2", c.Profile.LegalForm);
        Assert.Equal(1_000_000_000m, c.Profile.CharterCapital);
        Assert.Equal(new DateOnly(2019, 5, 20), c.Profile.EstablishedDate);
        Assert.Equal(("5610", "Nhà hàng và dịch vụ ăn uống"), (c.Profile.MainIndustryCode, c.Profile.MainIndustryName));
        Assert.Null(c.Profile.BusinessStatus);                      // the "Hoạt động/ Tạm ngưng/ Giải thể" hint is ignored

        var director = Assert.Single(c.Representatives);
        Assert.Equal(("DIRECTOR", true, "Nguyễn Văn Demo", "MALE"), (director.RoleCode, director.IsLegalRepresentative, director.FullName, director.Gender));
        Assert.Equal("079000000001", director.IdentityNumber);     // 11 digits from a number cell → 12 with the leading 0
        Assert.Equal(new DateOnly(1980, 1, 2), director.DateOfBirth);
        Assert.Equal("0901000001", director.Phone);

        Assert.Equal(["TAX_PORTAL", "DIGITAL_SIGNATURE", "EINVOICE_PROVIDER", "EINVOICE_PROVIDER", "EMAIL"], c.Accounts.Select(a => a.Input.SystemType));
        var tax = c.Accounts[0];
        Assert.Equal(("0312345678", Secret), (tax.Input.Username, tax.Password));
        var cert = c.Accounts[1];
        Assert.Equal(("Viettel", "REMOTE_SIGNING", new DateOnly(2027, 3, 31)), (cert.Input.ProviderName, cert.Input.CertificateType, cert.Input.ExpiresAt));
        Assert.Equal("https://einvoice.demo.example/login", c.Accounts[2].Input.LoginUrl); // "einvoice.demo.example/login" gets https://
        Assert.Equal("https://second.demo.example/", c.Accounts[3].Input.LoginUrl);        // a second "Link" starts a second account
        Assert.Equal("Gmail", c.Accounts[4].Input.SystemName);
        // A note that looks like a password is not imported anywhere, only flagged.
        Assert.Contains(c.Warnings, w => w.Contains("ghi chú giống mật khẩu"));
        Assert.DoesNotContain(c.Accounts, a => (a.Input.Note ?? "").Contains("Zz9-Hidden!"));
        Assert.DoesNotContain("Zz9-Hidden!", c.Profile.Note ?? "");
    }

    [Theory]
    [InlineData("0101234567", false, "0101234567")]
    [InlineData("101234567", true, "0101234567")]
    [InlineData("0101234567-001", false, "0101234567-001")]
    [InlineData("079123456789", false, "079123456789")]
    [InlineData("79123456789", true, "079123456789")]
    [InlineData("12AB", false, null)]
    public void TaxCodesKeepLeadingZerosAndPersonalTwelveDigitCodes(string text, bool fromNumber, string? expected)
    {
        var cell = fromNumber ? new AdminServices.XlsxCell(text, double.Parse(text), false) : new AdminServices.XlsxCell(text, null, false);
        Assert.Equal(expected, AdminServices.CustomerWorkbookParser.TaxCode(text, cell, out _));
        if (expected is not null) Assert.True(AdminServices.TaxCodePolicy.IsValid(expected));
    }

    [Fact]
    public void PasswordsUnderOtherLabelsAreEncryptedFieldsNeverPlainNotes()
    {
        var rows = Template();
        rows[1][3] = new Cell("Công ty Demo");
        rows[1][2] = new Cell("Tên:");
        rows.Add([new("hoadondientu.gdt.gov.vn"), new("1"), new("Tên đăng nhập"), new("312345678", 312345678), null]); // number cell: leading 0 lost
        rows.Add([null, new("2"), new("Pass:"), new(Secret), null]);
        rows.Add([null, new("3"), new("Ghi chú khác:"), new("Qw3rty-Zx!"), null]);                                   // unknown label, secret-looking value
        rows.Add([new("Phần mềm khác"), new("1"), new("Tên phần mềm:"), new(new string('X', 400)), null]);
        var c = Assert.Single(Parse(Xlsx([("Demo", rows)])));
        var portal = c.Accounts.Single(a => a.Input.SystemType == "EINVOICE_PORTAL");
        Assert.Equal(("0312345678", Secret), (portal.Input.Username, portal.Password));
        Assert.DoesNotContain("Qw3rty-Zx!", portal.Input.Note ?? "");
        Assert.Contains(c.Warnings, w => w.Contains("giống mật khẩu"));
        Assert.Contains(c.Warnings, w => w.Contains("lưu dạng số"));
        Assert.Equal(150, c.Accounts.Single(a => a.Input.SystemType == "OTHER").Input.SystemName!.Length); // nvarchar(150)
    }

    [Fact]
    public void OperationalSheetsAndEmptyTemplatesAreSkipped()
    {
        var sheets = new[] { ("Phí dv", Profile()), ("CHECK Q3.26", Profile()), ("Sheet2", Template()), ("HKD Demo", Profile(taxCode: "0398765432", name: "Hộ kinh doanh Demo")) };
        var list = Parse(Xlsx(sheets));
        Assert.Equal("HKD Demo", Assert.Single(list).SheetName); // "HKD" alone is a table, "HKD <name>" is a profile
    }

    [Fact]
    public void ANonXlsxFileIsRejectedCleanly()
    {
        Assert.Throws<AdminServices.XlsxFormatException>(() => AdminServices.XlsxReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));
    }

    // ---- web flow -----------------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewNeverShowsPasswordsAndConfirmSavesOnlyTheSelectedSheets()
    {
        var store = new FakeImports();
        using var f = Factory(store);
        var c = Client(f);
        var token = await Token(c, "/customers/import");
        var preview = await Upload(c, token, Xlsx([("01.Demo", Profile()), ("02.Other", Profile(taxCode: "0101234567", name: "Công ty Đã Có"))]));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var html = WebUtility.HtmlDecode(await preview.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Secret, html);
        Assert.DoesNotContain("079000000001", html);
        Assert.Contains("Tạo mới", html);
        Assert.Contains("Đã có: KH-OLD", html);                  // matched by tax code
        Assert.Contains("name=\"codes[0]\" value=\"DEMO\"", html);
        var importToken = Regex.Match(html, "name=\"token\" value=\"([0-9a-f-]+)\"").Groups[1].Value;

        var form = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("token", importToken), new("include", "0"), new("codes[0]", "demo-01") };
        var result = await c.PostAsync("/customers/importconfirm", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var saved = Assert.Single(store.Items);
        Assert.Equal("DEMO-01", saved.NewCode);
        Assert.Equal(Secret, saved.Data.Accounts[0].Password);    // plaintext goes to the store, which encrypts it
        Assert.DoesNotContain(Secret, await result.Content.ReadAsStringAsync());

        // The upload is used once.
        var again = await c.PostAsync("/customers/importconfirm", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Single(store.Items);
    }

    [Fact]
    public async Task AnExistingCustomerIsUpdatedNotCreated()
    {
        var store = new FakeImports();
        using var f = Factory(store);
        var c = Client(f);
        var token = await Token(c, "/customers/import");
        var html = await (await Upload(c, token, Xlsx([("02.Other", Profile(taxCode: "0101234567", name: "Công ty Đã Có"))]))).Content.ReadAsStringAsync();
        var importToken = Regex.Match(html, "name=\"token\" value=\"([0-9a-f-]+)\"").Groups[1].Value;
        await c.PostAsync("/customers/importconfirm", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("token", importToken), new("include", "0"), new("codes[0]", "IGNORED")]));
        var saved = Assert.Single(store.Items);
        Assert.Equal((Existing, (string?)null), (saved.CustomerId, saved.NewCode));
    }

    [Fact]
    public async Task WrongFilesAreRefusedWithAMessage()
    {
        using var f = Factory(new FakeImports());
        var c = Client(f);
        var token = await Token(c, "/customers/import");
        var csv = await Upload(c, token, Encoding.UTF8.GetBytes("a,b"), "data.csv");
        Assert.Equal(HttpStatusCode.BadRequest, csv.StatusCode);
        Assert.Contains("Chỉ nhận file Excel .xlsx", WebUtility.HtmlDecode(await csv.Content.ReadAsStringAsync()));
        var broken = await Upload(c, token, Encoding.UTF8.GetBytes("not a zip"));
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Contains("Không đọc được file Excel", WebUtility.HtmlDecode(await broken.Content.ReadAsStringAsync()));
    }

    // ---- workbook builder (fictitious data) ------------------------------------------------------------------

    private sealed record Cell(string? Text, double? Number = null, bool Date = false);

    private static List<AdminServices.ImportedCustomer> Parse(byte[] xlsx) => AdminServices.CustomerWorkbookParser.Parse(AdminServices.XlsxReader.Read(new MemoryStream(xlsx)));

    private static byte[] Workbook() => Xlsx([("01.Demo", Profile())]);

    private static double Oa(int y, int m, int d) => new DateTime(y, m, d).ToOADate();

    // Rows of [B block, C stt, D label, E value, F note], the layout of the real workbook.
    private static List<Cell?[]> Profile(string? taxCode = null, string name = "Công ty TNHH Demo") =>
    [
        [new("DANH MỤC"), new("STT"), new("NỘI DUNG"), null, new("GHI CHÚ")],
        [new("Thông tin chung"), new("1"), new("Hình thức"), new("Công ty TNHH 2 TV"), null],
        [null, new("2"), new("Mã số thuế:"), taxCode is null ? new Cell("312345678", 312345678) : new Cell(taxCode), null],
        [null, new("3"), new("Tên:"), new(name), null],
        [null, new("4"), new("Mail công ty:"), new("ketoan@demo.example"), null],
        [null, new("5"), new("Vốn điều lệ:"), new("1000000000", 1_000_000_000), null],
        [null, new("6"), new("Năm thành lập:"), new("20/05/2019"), null],
        [null, new("7"), new("Ngành nghề kinh doanh chính:"), new("5610: Nhà hàng và dịch vụ ăn uống"), null],
        [null, new("8"), new("Tình trạng hoạt động"), new("Hoạt động/ Tạm ngưng/ Giải thể"), null],
        [new("Giám đốc"), new("1"), new("Giám đốc:"), new("Nguyễn Văn Demo"), null],
        [null, new("2"), new("CCCD:"), new("79000000001", 79000000001), null],
        [null, new("3"), new("Năm sinh:"), new("x", Oa(1980, 1, 2), true), null],
        [null, new("4"), new("Giới tính:"), new("Nam"), null],
        [null, new("5"), new("Số điện thoại:"), new("901000001", 901000001), null],
        [new("thuedientu.gdt.gov.vn"), new("1"), new("Tên đăng nhập:"), new("0312345678"), null],
        [null, new("2"), new("Mật khẩu:"), new(Secret), null],
        [new("Chứng thư số"), new("1"), new("Nhà cung cấp:"), new("viettel-CA"), null],
        [null, new("2"), new("Hạn chứng thư số:"), new("x", Oa(2027, 3, 31), true), null],
        [null, new("3"), new("Loại ký số:"), new("Chữ ký số từ xa"), null],
        [null, new("4"), new("Mật khẩu:"), new("123456", 123456), new("Zz9-Hidden!")],
        [new("Link xuất hoá đơn"), new("1"), new("Link xuất hoá đơn:"), new("einvoice.demo.example/login"), null],
        [null, new("2"), new("Mail đăng nhập:"), new("hd@demo.example"), null],
        [null, new("3"), new("Link xuất hoá đơn:"), new("https://second.demo.example/"), null],
        [new("Phần mềm khác"), new("1"), new("Tên phần mềm:"), new("Gmail"), null],
        [null, new("2"), new("Tên đăng nhập:"), new("demo@gmail.example"), null],
        [new("Dịch vụ sử dụng"), new("1"), new("Loại dịch vụ"), new("Kế toán tháng/…..."), null],
    ];

    private static List<Cell?[]> Template() => [[new("DANH MỤC"), new("STT"), new("NỘI DUNG"), null, null], [new("Thông tin chung"), new("1"), new("Mã số thuế:"), null, null]];

    private static byte[] Xlsx(IReadOnlyList<(string Name, List<Cell?[]> Rows)> sheets)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Part(string path, string xml) { using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); w.Write(xml); }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Part("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"{r}\"><sheets>" + string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
            Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{r}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) + "</Relationships>");
            Part("xl/styles.xml", $"<styleSheet xmlns=\"{ns}\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>");
            for (var i = 0; i < sheets.Count; i++)
            {
                var sb = new StringBuilder($"<worksheet xmlns=\"{ns}\"><sheetData>");
                for (var row = 0; row < sheets[i].Rows.Count; row++)
                {
                    sb.Append($"<row r=\"{row + 1}\">");
                    var cells = sheets[i].Rows[row];
                    for (var col = 0; col < cells.Length; col++)
                    {
                        if (cells[col] is not { } cell) continue;
                        var at = $"{(char)('B' + col)}{row + 1}";
                        sb.Append(cell.Number is { } n
                            ? $"<c r=\"{at}\"{(cell.Date ? " s=\"1\"" : "")}><v>{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>"
                            : $"<c r=\"{at}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(cell.Text)}</t></is></c>");
                    }
                    sb.Append("</row>");
                }
                Part($"xl/worksheets/sheet{i + 1}.xml", sb.Append("</sheetData></worksheet>").ToString());
            }
        }
        return ms.ToArray();
    }

    // ---- host -----------------------------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> Upload(HttpClient c, string token, byte[] bytes, string fileName = "Thong tin khach hang.xlsx")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return await c.PostAsync("/customers/importpreview", content);
    }

    private static async Task<string> Token(HttpClient c, string path) =>
        WebUtility.HtmlDecode(Regex.Match(await c.GetStringAsync(path), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static WebApplicationFactory<admin::Program> Factory(FakeImports store) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<AdminServices.ICustomerImportStore>();
                services.AddSingleton<AdminServices.ICustomerImportStore>(store);
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    private sealed class FakeImports : AdminServices.ICustomerImportStore
    {
        public List<AdminServices.CustomerImportItem> Items { get; } = [];
        public Task<IReadOnlyDictionary<string, AdminServices.CustomerMatch>> CustomersByTaxCodeAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, AdminServices.CustomerMatch>>(new Dictionary<string, AdminServices.CustomerMatch> { ["0101234567"] = new(Existing, "KH-OLD", "Công ty Đã Có") });
        public Task<AdminServices.CustomerImportResult> ImportAsync(AdminServices.CustomerImportItem item, Guid actor)
        {
            Items.Add(item);
            return Task.FromResult(new AdminServices.CustomerImportResult(item.NewCode is null ? AdminServices.CustomerImportStatus.Updated : AdminServices.CustomerImportStatus.Created,
                item.Data.Representatives.Count, 0, item.Data.Accounts.Count, 0, 0, item.Data.Accounts.Count(a => a.Password is not null)));
        }
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Actor.ToString()), new Claim(ClaimTypes.Name, "safe-import"), new Claim("display_name", "Safe Import"), new Claim(ClaimTypes.Role, "SYS_ADMIN") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
