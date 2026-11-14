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

// CONTRACT-REVIEW ("Hợp đồng dịch vụ", operator 2026-11-14). No SQL host and no Claude API call: DOCX reading/writing,
// the editing rules, masking, the AI answer validation and the pages are exercised with in-memory fakes. The SQL store
// and the real API are NOT exercised here; migration 044 is checked for its guards.
public sealed class SafeContractReviewTests
{
    private const string TestScheme = "SafeContractReviewTest";
    private static readonly Guid Actor = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid R1 = Guid.Parse("a7000000-0000-0000-0000-0000000000a7");
    private static readonly Guid C1 = Guid.Parse("c7000000-0000-0000-0000-0000000000c7");

    // ---- Migration and permissions ------------------------------------------------------------------------------
    [Fact]
    public void Migration044MatchesCatalogueAndFollowsTheMigrationRules()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "044-contract-review.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "077-verify-contract-review.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "078-rollback-contract-review.sql"));
        var expected = AdminSecurity.AdminPermissions.Migration044Codes.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(6, expected.Count);
        Assert.Equal(expected, Regex.Matches(migration, @"\('([A-Z._]+)',\s+N'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(verify, @"\('([A-Z._]+)'\)").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(rollback, @"'((?:CONTRACTS|LEGAL)\.[A-Z._]+)'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.DoesNotContain(expected, c => AdminSecurity.AdminPermissions.MigrationCodes.Contains(c));
        const string marker = "A0000044-0000-4000-8000-000000000044";
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains(marker, sql));
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.DoesNotContain("CREATE OR ALTER", migration + verify + rollback);
        Assert.DoesNotContain("JSON_", migration + verify + rollback);               // SQL Server 2014
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE)\b", migration);              // only creates and inserts
        Assert.Contains("THROW 51002", rollback);                                     // refuses while real data exists
        Assert.Contains("CreatedBy = @Marker", rollback);
        foreach (var table in new[] { "ContractReviews", "ContractReviewFiles", "ContractReviewVersions", "ContractReviewParagraphs", "ContractReviewRuns",
                     "ContractReviewFindings", "ContractReviewChanges", "LegalDocuments", "LegalArticles" })
        {
            Assert.Contains($"CREATE TABLE dbo.{table}", migration);
            Assert.Contains($"DROP TABLE dbo.{table}", rollback);
            Assert.Contains($"N'dbo.{table}'", verify);
        }
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "migrations"), "044-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "077-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "078-*.sql"));
        // Every status and value the code writes is allowed by the CHECK constraints.
        foreach (var s in S.ContractReviewPolicy.Statuses) Assert.Contains($"'{s.Code}'", migration);
        foreach (var s in S.ContractReviewPolicy.Actions.Concat(S.ContractReviewPolicy.Severities).Concat(S.ContractReviewPolicy.FindingStatuses).Concat(S.ContractReviewPolicy.CustomerDecisions))
            Assert.Contains($"'{s.Code}'", migration);
    }

    [Fact]
    public void Migration046AddsLawFilesWithAGuardedRollback()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "046-legal-library-files.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "081-verify-legal-library-files.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "082-rollback-legal-library-files.sql"));
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.Contains("CREATE TABLE dbo.LegalDocumentFiles", migration);
        Assert.Contains("CK_LegalDocumentFiles_Size CHECK (SizeBytes > 0 AND SizeBytes <= 20971520)", migration);
        Assert.Equal(20971520, S.LegalFileText.MaxBytes);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE)\b", migration);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.Contains("THROW 51002", rollback);
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "migrations"), "046-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "081-*.sql"));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "database", "scripts"), "082-*.sql"));
    }

    [Fact]
    public void TheApiKeyIsNeverCommitted()
    {
        var root = RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "TAPortal.Web"), "appsettings*.json"))
            Assert.DoesNotContain("sk-ant-", File.ReadAllText(file));
        var options = S.ContractReviewAiOptions.From(new ConfigurationBuilder().Build());
        Assert.False(options.IsConfigured);
        Assert.Equal("claude-opus-5-5", options.Model);
        Assert.Equal(0.28m, options.Cost(20_000, 10_000));   // 20k in × $4 + 10k out × $20 per million
    }

    // ---- DOCX ---------------------------------------------------------------------------------------------
    [Fact]
    public void DocxWriterOutputIsReadBackParagraphByParagraph()
    {
        var paragraphs = Sample();
        var docx = S.DocxWriter.Write("HỢP ĐỒNG DỊCH VỤ", paragraphs);
        var read = S.DocxParagraphs.Read(docx);
        Assert.Equal(["HỢP ĐỒNG DỊCH VỤ", .. paragraphs.Select(p => p.Text)], read.Select(p => p.Text));
        Assert.Equal("HEADING", read[2].Kind);                   // "Điều 1. ..." is a heading
        Assert.Equal(["P1", "P2", "P3", "P4", "P5", "P6"], read.Select(p => p.Key));
        var (data, error, parsed) = S.ContractReviewPolicy.CheckDocx("hop-dong.docx", docx);
        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal(6, parsed.Count);
        Assert.NotNull(S.ContractReviewPolicy.CheckDocx("hop-dong.pdf", Encoding.ASCII.GetBytes("%PDF-1.7")).Error);
        Assert.NotNull(S.ContractReviewPolicy.CheckDocx("hop-dong.docx", [0x50, 0x4B, 0x03, 0x04, 0, 0]).Error);
    }

    [Fact]
    public void TrackedExportUsesRealWordRevisionsAndComments()
    {
        var before = Sample();
        var after = before.Select(p => p.Key == "P3" ? p with { Text = "Bên B thanh toán trong vòng 15 ngày kể từ ngày nhận hóa đơn hợp lệ." } : p)
            .Where(p => p.Key != "P4").ToList();
        var docx = S.DocxWriter.Write("HĐ", after, before, new Dictionary<string, string> { ["P3"] = "Rút ngắn thời hạn thanh toán." });
        using var zip = new ZipArchive(new MemoryStream(docx));
        var document = Read(zip, "word/document.xml");
        Assert.Contains("<w:ins ", document);
        Assert.Contains("<w:del ", document);
        Assert.Contains("<w:delText xml:space=\"preserve\">", document);
        Assert.Contains("w:author=\"T.A Portal\"", document);
        Assert.Contains("Rút ngắn thời hạn thanh toán.", Read(zip, "word/comments.xml"));
        Assert.Contains("comments.xml", Read(zip, "word/_rels/document.xml.rels"));
        // Reading the tracked file gives the new wording (deleted text is ignored).
        var read = S.DocxParagraphs.Read(docx).Select(p => p.Text).ToList();
        Assert.Contains(after[2].Text, read);
        Assert.DoesNotContain(before[3].Text, read);
    }

    [Fact]
    public void WordDiffMarksOnlyTheChangedWords()
    {
        var parts = S.WordDiff.Compute("thanh toán trong 30 ngày", "thanh toán trong 15 ngày làm việc");
        Assert.Equal("thanh toán trong ", string.Concat(parts.TakeWhile(p => p.Op == S.DiffOp.Same).Select(p => p.Text)));
        Assert.Contains(parts, p => p.Op == S.DiffOp.Delete && p.Text.Contains("30"));
        Assert.Contains(parts, p => p.Op == S.DiffOp.Insert && p.Text.Contains("15"));
        Assert.Equal("thanh toán trong 15 ngày làm việc", string.Concat(parts.Where(p => p.Op != S.DiffOp.Delete).Select(p => p.Text)));
        Assert.Equal("thanh toán trong 30 ngày", string.Concat(parts.Where(p => p.Op != S.DiffOp.Insert).Select(p => p.Text)));
    }

    // ---- Editing rules ---------------------------------------------------------------------------------------
    [Fact]
    public void AcceptedFindingsBuildTheNextVersionWithoutTouchingTheCurrentOne()
    {
        var current = Sample();
        var edits = new List<S.ReviewEdit>
        {
            new(Guid.NewGuid(), "P3", "REPLACE", "30 ngày", "15 ngày", "Thời hạn quá dài."),
            new(Guid.NewGuid(), "P3", "REPLACE", "chuyển khoản", "chuyển khoản vào tài khoản ghi trong hợp đồng", "Ghi rõ tài khoản."),
            new(Guid.NewGuid(), "P4", "DELETE", null, null, "Điều khoản trái luật."),
            new(Guid.NewGuid(), "P5", "INSERT", null, "Điều 3. Giải quyết tranh chấp", "Thiếu điều khoản tranh chấp."),
            new(Guid.NewGuid(), "P2", "NOTE", null, null, "Chỉ lưu ý.")
        };
        var result = S.ContractReviewPolicy.Apply(current, edits, 1);
        Assert.Empty(result.Errors);
        Assert.Equal(["P1", "P2", "P3", "P5", "N1"], result.Paragraphs.Select(p => p.Key));
        Assert.Equal("Bên B thanh toán trong 15 ngày bằng chuyển khoản vào tài khoản ghi trong hợp đồng.", result.Paragraphs[2].Text);
        Assert.Equal("HEADING", result.Paragraphs[4].Kind);
        Assert.Equal(3, result.Changes.Count);
        Assert.Contains(result.Changes, c => c.Key == "P4" && c.NewText is null && c.OldText == current[3].Text);
        Assert.Contains(result.Changes, c => c.Key == "N1" && c.OldText is null);
        Assert.Contains("Thời hạn quá dài.", result.Changes.Single(c => c.Key == "P3").Note);
        Assert.Equal("Bên B thanh toán trong 30 ngày bằng chuyển khoản.", current[2].Text);   // the source list is unchanged
    }

    [Fact]
    public void ConflictingOrUnmatchedEditsAreRefused()
    {
        var current = Sample();
        Assert.NotEmpty(S.ContractReviewPolicy.Apply(current, [new(Guid.NewGuid(), "P3", "REPLACE", "không có trong đoạn", "x", "")], 1).Errors);
        Assert.NotEmpty(S.ContractReviewPolicy.Apply(current, [new(Guid.NewGuid(), "P3", "REPLACE", "30 ngày", "a", ""), new(Guid.NewGuid(), "P3", "REPLACE", "trong 30", "b", "")], 1).Errors);
        Assert.NotEmpty(S.ContractReviewPolicy.Apply(current, [new(Guid.NewGuid(), "P3", "DELETE", null, null, ""), new(Guid.NewGuid(), "P3", "REPLACE", "30 ngày", "15 ngày", "")], 1).Errors);
        Assert.NotEmpty(S.ContractReviewPolicy.Apply(current, [new(Guid.NewGuid(), "P9", "DELETE", null, null, "")], 1).Errors);
        Assert.Equal(4, S.ContractReviewPolicy.NextInsertNo(["P1", "N1", "N3", "P2"]));
    }

    [Fact]
    public void TheCustomerReplyKeepsAgreedChangesAndRestoresDeclinedOnes()
    {
        var before = Sample();
        var current = S.ContractReviewPolicy.Apply(before, [
            new(Guid.NewGuid(), "P3", "REPLACE", "30 ngày", "15 ngày", "a"),
            new(Guid.NewGuid(), "P4", "DELETE", null, null, "b"),
            new(Guid.NewGuid(), "P5", "INSERT", null, "Đoạn mới.", "c"),
            new(Guid.NewGuid(), "P2", "REPLACE", "", "Bên B cung cấp dịch vụ kế toán trọn gói.", "d")], 1).Paragraphs;
        var answers = new List<S.ReviewCustomerAnswer>
        {
            new("P2", before[1].Text, "Bên B cung cấp dịch vụ kế toán trọn gói.", "AGREED", null),
            new("P3", before[2].Text, current[2].Text, "COUNTER", "Bên B thanh toán trong 20 ngày bằng chuyển khoản."),
            new("P4", before[3].Text, null, "DECLINED", null),
            new("N1", null, "Đoạn mới.", "DECLINED", null)
        };
        var result = S.ContractReviewPolicy.ApplyCustomer(current, before, answers);
        Assert.Empty(result.Errors);
        Assert.Equal(["P1", "P2", "P3", "P4", "P5"], result.Paragraphs.Select(p => p.Key));
        Assert.Equal("Bên B cung cấp dịch vụ kế toán trọn gói.", result.Paragraphs[1].Text);
        Assert.Equal("Bên B thanh toán trong 20 ngày bằng chuyển khoản.", result.Paragraphs[2].Text);
        Assert.Equal(before[3].Text, result.Paragraphs[3].Text);                        // deleted paragraph is back in place
        Assert.Equal(3, result.Changes.Count);
        Assert.NotEmpty(S.ContractReviewPolicy.ApplyCustomer(current, before, [answers[0] with { Decision = "PENDING" }]).Errors);
    }

    // ---- Privacy and AI answer --------------------------------------------------------------------------------
    [Fact]
    public void PersonalDataIsMaskedBeforeTheAiAndRestoredAfter()
    {
        var mask = new S.PersonalDataMask();
        var text = "Ông A, CCCD 001203004567, ĐT 0912 345 678, email a.nguyen@example.vn, TK 1903456789012. Giá 10.000.000 đồng, ngày 15/11/2026.";
        var masked = mask.Mask(text);
        Assert.DoesNotContain("001203004567", masked);
        Assert.DoesNotContain("0912 345 678", masked);
        Assert.DoesNotContain("a.nguyen@example.vn", masked);
        Assert.DoesNotContain("1903456789012", masked);
        Assert.Contains("10.000.000", masked);                                   // money and dates are kept
        Assert.Contains("15/11/2026", masked);
        Assert.Equal(masked, mask.Mask(text));                                    // same value, same placeholder
        Assert.Equal(text, mask.Unmask(masked));
    }

    [Fact]
    public void TheAiAnswerIsValidatedAgainstTheContractAndTheLawLibrary()
    {
        var paragraphs = Sample();
        var library = new List<S.LegalIndexArticle> { new("91/2015/QH13", "Bộ luật Dân sự", "Điều 418", "Thỏa thuận phạt vi phạm") };
        var json = """
{"summary":"Tóm tắt","findings":[
 {"paragraphKey":"P3","action":"REPLACE","quote":"30 ngày","issue":"Dài","severity":"HIGH","party":"A","legalRefs":[{"documentNo":"91/2015/qh13","article":"điều 418","clause":"","note":""}],"suggestion":"","revisedText":"15 ngày"},
 {"paragraphKey":"P4","action":"REPLACE","quote":"không nguyên văn","issue":"Sai","severity":"MEDIUM","party":"B","legalRefs":[{"documentNo":"54/2019/QH14","article":"Điều 1","clause":"","note":""}],"suggestion":"","revisedText":"x"},
 {"paragraphKey":"P99","action":"NOTE","quote":"","issue":"Không có đoạn","severity":"LOW","party":"BOTH","legalRefs":[],"suggestion":"","revisedText":""},
 {"paragraphKey":"P5","action":"DELETE","quote":"","issue":"Bỏ","severity":"WRONG","party":"X","legalRefs":[],"suggestion":"","revisedText":"bị bỏ"}
]}
""";
        var (summary, findings, dropped) = S.ContractReviewPrompt.Parse(json, paragraphs, library, s => s);
        Assert.Equal("Tóm tắt", summary);
        Assert.Equal(3, findings.Count);
        Assert.Single(dropped);
        Assert.True(findings[0].LegalVerified);                                   // number and article matched case-insensitively
        Assert.Equal("NOTE", findings[1].Action);                                 // quote not verbatim: kept as a note, nothing replaced
        Assert.Null(findings[1].Quote);
        Assert.False(findings[1].LegalVerified);                                  // not in the library
        Assert.Equal(("MEDIUM", "BOTH", null), (findings[2].Severity, findings[2].Party, findings[2].RevisedText));
        var schema = System.Text.Json.JsonSerializer.Serialize(S.ContractReviewPrompt.Schema());
        Assert.Contains("\"additionalProperties\":false", schema);
        var user = S.ContractReviewPrompt.User(new("HĐ", "A", paragraphs, library, true), s => s);
        Assert.Contains("[P3] Bên B thanh toán", user);
        Assert.Contains("Điều 418. Thỏa thuận phạt vi phạm", user);
        Assert.Contains("BÊN A", user);
    }

    [Fact]
    public void LawTextIsSplitIntoArticles()
    {
        var articles = S.LegalTextParser.Split("""
BỘ LUẬT DÂN SỰ
Chương I
Điều 1. Phạm vi điều chỉnh
Bộ luật này quy định địa vị pháp lý...
Điều 2: Công nhận, tôn trọng
1. Ở nước Cộng hòa...
2. Quyền dân sự...
Điều 3 Các nguyên tắc cơ bản
""");
        Assert.Equal(["Điều 1", "Điều 2", "Điều 3"], articles.Select(a => a.ArticleNo));
        Assert.Equal("Phạm vi điều chỉnh", articles[0].Title);
        Assert.Contains("2. Quyền dân sự...", articles[1].Content);
        Assert.Equal("Điều 385", S.LegalTextParser.NormalizeArticle("điều 385, khoản 2"));
    }

    [Fact]
    public void ArticlesFollowEachOtherAndCrossReferencesStayInTheBody()
    {
        var articles = S.LegalTextParser.Split("""
Chương I
NHỮNG QUY ĐỊNH CHUNG
Điều 1. Phạm vi
Nội dung theo quy định tại
Điều 5 của Luật này.
Điều 2. Đối tượng
Điều 2a. Đối tượng bổ sung
Điều 12 của Luật này áp dụng.
Chương II
QUYỀN VÀ NGHĨA VỤ
Điều 4. Quyền
Nội dung điều 4.Điều 5. Nghĩa vụ
Nội dung điều 5.
""");
        // "Điều 12 của ..." is a cross-reference (no heading dot): body text; "Điều 4. Quyền" is a heading after repealed
        // numbers; "...điều 4.Điều 5." glued in one paragraph is split.
        Assert.Equal(["Điều 1", "Điều 2", "Điều 2a", "Điều 4", "Điều 5"], articles.Select(a => a.ArticleNo));
        Assert.Contains("Điều 5 của Luật này.", articles[0].Content);
        Assert.Contains("Điều 12 của Luật này áp dụng.", articles[2].Content);
        Assert.DoesNotContain("NHỮNG QUY ĐỊNH CHUNG", string.Join("\n", articles.Select(a => a.Content)));
        Assert.DoesNotContain("QUYỀN VÀ NGHĨA VỤ", string.Join("\n", articles.Select(a => a.Content)));
        Assert.Equal(["Điều 2", "Điều 3"], S.LegalTextParser.Missing([new("Điều 1", null, "x"), new("Điều 4", null, "y")]));
        Assert.Contains("thiếu 2 điều (Điều 2, Điều 3)", S.LegalLibraryPolicy.Summary([new("Điều 1", null, "x"), new("Điều 4", null, "y")]));
    }

    [Fact]
    public void LawFilesAreReadOnTheServerWithoutAi()
    {
        const string law = "Điều 1. Phạm vi\nNội dung một.\nĐiều 2. Hiệu lực\nNội dung hai.";
        var docx = S.DocxWriter.Write("LUẬT MẪU", law.Split('\n').Select((t, i) => new S.ReviewParagraph("P" + (i + 1), "TEXT", t)).ToList());
        var (fromDocx, e1) = S.LegalFileText.Read("luat.docx", docx);
        Assert.Null(e1);
        Assert.Equal(2, S.LegalTextParser.Split(fromDocx!.Text).Count);
        var (fromTxt, e2) = S.LegalFileText.Read("luat.txt", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(law)).ToArray());
        Assert.Null(e2);
        Assert.Equal(("text/plain", 2), (fromTxt!.ContentType, S.LegalTextParser.Split(fromTxt.Text).Count));
        Assert.Contains(".docx", S.LegalFileText.Read("luat.doc", [1, 2, 3]).Error);
        Assert.NotNull(S.LegalFileText.Read("luat.pdf", Encoding.ASCII.GetBytes("%PDF-1.7 broken")).Error);
        Assert.NotNull(S.LegalFileText.Read("luat.txt", Encoding.UTF8.GetBytes("Không có điều nào.")).Error);
        Assert.NotNull(S.LegalFileText.Read("luat.exe", [1]).Error);
        // PDF lines wrapped mid-sentence are joined; items and headings keep their own line.
        Assert.Equal("Điều 1. Phạm vi\n1. Bên A thanh toán đúng hạn cho Bên B.\na) Trường hợp chậm\nTHỜI HẠN",
            S.LegalFileText.JoinWrapped(["Điều 1. Phạm vi", "1. Bên A thanh toán đúng hạn", "cho Bên B.", "a) Trường hợp chậm", "THỜI HẠN"]));
    }

    [Fact]
    public async Task TheLawPageOffersUploadReimportAndArticleEditsOnlyToManagers()
    {
        var store = new MemoryReviews();
        using (var manager = Factory(store, "CONTRACTS.REVIEW.VIEW", "LEGAL.LIBRARY.MANAGE"))
        {
            var c = Client(manager);
            var html = WebUtility.HtmlDecode(await c.GetStringAsync($"/legallibrary/details/{MemoryLibrary.Doc}"));
            Assert.Contains("data-ta-legal-upload", html);
            Assert.Contains("Dùng lại file này", html);                         // the older file
            Assert.Contains("Đang dùng", html);                                 // the current file
            Assert.Contains("name=\"content\"", html);                          // article edit form
            var create = WebUtility.HtmlDecode(await c.GetStringAsync("/legallibrary/create"));
            Assert.Contains("enctype=\"multipart/form-data\"", create);
            Assert.Contains("type=\"file\" name=\"file\"", create);
            var upload = await c.PostAsync($"/legallibrary/upload/{MemoryLibrary.Doc}", Upload(Token(html), "luat-moi.txt",
                Encoding.UTF8.GetBytes("Điều 1. A\nnội dung\nĐiều 2. B\nnội dung")));
            Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);
            Assert.Equal(("luat-moi.txt", 2), store.Library.LastUpload);
            var bad = await c.PostAsync($"/legallibrary/upload/{MemoryLibrary.Doc}", Upload(Token(html), "scan.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 x")));
            Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
            Assert.Equal(("luat-moi.txt", 2), store.Library.LastUpload);         // unreadable file never reaches the store
        }
        using var viewer = Factory(store, "CONTRACTS.REVIEW.VIEW");
        var v = Client(viewer);
        var page = WebUtility.HtmlDecode(await v.GetStringAsync($"/legallibrary/details/{MemoryLibrary.Doc}"));
        Assert.DoesNotContain("data-ta-legal-upload", page);
        Assert.DoesNotContain("name=\"content\"", page);
        Assert.Contains("Điều 418", page);
        Assert.Equal(HttpStatusCode.Forbidden, (await v.GetAsync("/legallibrary/create")).StatusCode);
    }

    // ---- Pages ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task TheWorkspaceShowsHighlightsTrackChangesAndOnlyPermittedSteps()
    {
        var store = new MemoryReviews();
        using var f = Factory(store, "CONTRACTS.REVIEW.VIEW", "CONTRACTS.REVIEW.EDIT", "CONTRACTS.REVIEW.RUN_AI");
        var c = Client(f);
        var list = WebUtility.HtmlDecode(await c.GetStringAsync("/contractreviews"));
        Assert.Contains("CL000001", list);
        Assert.Contains("href=\"/ContractReviews\"", Nav(list));

        var page = await c.GetStringAsync($"/contractreviews/details/{R1}?v=1");
        var html = WebUtility.HtmlDecode(page);
        Assert.Contains("class=\"ta-rv-mark ta-rv-sev-high\"", html);             // quote highlighted in yellow
        Assert.Contains("role=\"tooltip\"", html);                               // note on hover
        Assert.Contains("Chưa đối chiếu kho luật", html);
        Assert.Contains("data-ta-review-run", html);
        Assert.Contains("data-ta-review-ai-missing", html);                       // no API key in tests
        Assert.DoesNotContain("data-ta-review-finalize", html);                   // no FINALIZE permission
        Assert.DoesNotContain("<script", Regex.Match(page, "data-ta-review-doc[\\s\\S]*?</section>").Value);

        var changes = WebUtility.HtmlDecode(await c.GetStringAsync($"/contractreviews/details/{R1}?v=1&mode=changes"));
        Assert.Contains("class=\"ta-rv-ins\"", changes);
        Assert.Contains("class=\"ta-rv-del\"", changes);
        Assert.Contains("data-ta-review-export-tracked", changes);

        var export = await c.GetAsync($"/contractreviews/export/{R1}?v=1&tracked=true");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", export.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
        Assert.Contains("<w:ins ", Read(zip, "word/document.xml"));

        // Deciding a finding posts to the store with the actor; running the AI without a key is refused before the store.
        var token = Token(page);
        var decided = await c.PostAsync($"/contractreviews/decide/{R1}", Form(token, ("findingId", store.F1.ToString()), ("status", "ACCEPTED")));
        Assert.Equal(HttpStatusCode.Redirect, decided.StatusCode);
        Assert.Equal(("ACCEPTED", Actor), store.LastDecision);
        var run = await c.PostAsync($"/contractreviews/runai/{R1}", Form(token, ("mask", "true")));
        Assert.Equal(HttpStatusCode.Redirect, run.StatusCode);
        Assert.False(store.Queued);

        // In-place review: each mark opens its panel (template#pop-f-<id>) with Đồng ý / Bỏ qua / edit forms posted by fetch.
        Assert.Contains($"data-ta-pop=\"f-{store.F1}\"", html);
        Assert.Contains($"<template id=\"pop-f-{store.F1}\">", html);
        Assert.Contains("data-ta-review-accept", html);
        Assert.Contains("data-ta-pop=\"e-P3\"", html);                            // ✎ direct edit of a paragraph
        Assert.Contains("class=\"ta-rv-doc ta-rv-paper\"", html);
        Assert.Contains("/js/ta-review.js", html);
        var edited = await c.PostAsync($"/contractreviews/editparagraph/{R1}", Form(token, ("paragraphKey", "P3"), ("text", "Bên B thanh toán trong 10 ngày."), ("note", "Rút ngắn")));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Equal(("ACCEPTED", Actor), store.LastDecision);
        Assert.Equal(("REPLACE", "Bên B thanh toán trong 10 ngày."), (store.LastAdded?.Action, store.LastAdded?.RevisedText));
        var preview = Environment.GetEnvironmentVariable("TA_REVIEW_PREVIEW");
        if (!string.IsNullOrEmpty(preview)) File.WriteAllText(preview, page);
    }

    [Fact]
    public async Task ChangesNeedTheirOwnPermissionAndUploadsMustBeDocx()
    {
        var store = new MemoryReviews();
        using (var viewer = Factory(store, "CONTRACTS.REVIEW.VIEW"))
        {
            var c = Client(viewer);
            var page = await c.GetStringAsync($"/contractreviews/details/{R1}");
            Assert.DoesNotContain("data-ta-review-run", page);
            Assert.DoesNotContain("data-ta-review-add", page);
            var post = await c.PostAsync($"/contractreviews/decide/{R1}", Form(Token(page), ("findingId", store.F1.ToString()), ("status", "ACCEPTED")));
            Assert.NotEqual(HttpStatusCode.Redirect, post.StatusCode);
            Assert.Null(store.LastDecision.Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/contractreviews/create")).StatusCode);
        }
        using (var none = Factory(store, "CRM.CUSTOMERS.VIEW"))
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(none).GetAsync("/contractreviews")).StatusCode);

        using var creator = Factory(store, "CONTRACTS.REVIEW.VIEW", "CONTRACTS.REVIEW.CREATE");
        var cc = Client(creator);
        var form = await cc.GetStringAsync("/contractreviews/create");
        var bad = await cc.PostAsync("/contractreviews/create", Upload(Token(form), "hop-dong.pdf", Encoding.ASCII.GetBytes("%PDF-1.7"),
            ("Input.CustomerId", C1.ToString()), ("Input.Title", "HĐ dịch vụ"), ("Input.Role", "A")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Chỉ nhận file Word .docx", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        var ok = await cc.PostAsync("/contractreviews/create", Upload(Token(form), "hop-dong.docx", S.DocxWriter.Write("HĐ", Sample()),
            ("Input.CustomerId", C1.ToString()), ("Input.Title", "HĐ dịch vụ"), ("Input.Role", "A")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(6, store.CreatedParagraphs);                                // title line + 5 paragraphs
    }

    // ---- Helpers -------------------------------------------------------------------------------------------
    private static List<S.ReviewParagraph> Sample() =>
    [
        new("P1", "TEXT", "Hôm nay, các bên thống nhất ký hợp đồng dịch vụ như sau:"),
        new("P2", "HEADING", "Điều 1. Phạm vi dịch vụ"),
        new("P3", "TEXT", "Bên B thanh toán trong 30 ngày bằng chuyển khoản."),
        new("P4", "TEXT", "Bên A chịu phạt 50% giá trị hợp đồng nếu vi phạm."),
        new("P5", "TEXT", "Hợp đồng có hiệu lực từ ngày ký.")
    ];

    private static string Read(ZipArchive zip, string name)
    {
        using var r = new StreamReader(zip.GetEntry(name)!.Open());
        return r.ReadToEnd();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Nav(string html)
    {
        var start = html.IndexOf("id=\"taNav\"", StringComparison.Ordinal);
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))]);

    private static MultipartFormDataContent Upload(string token, string fileName, byte[] data, params (string Key, string Value)[] fields)
    {
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (k, v) in fields) content.Add(new StringContent(v), k);
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return content;
    }

    private static HttpClient Client(WebApplicationFactory<admin::Program> f) => f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static WebApplicationFactory<admin::Program> Factory(MemoryReviews store, params string[] permissions) =>
        new WebApplicationFactory<admin::Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }))
            .ConfigureTestServices(s =>
            {
                s.AddDataProtection().UseEphemeralDataProtectionProvider();
                s.RemoveAll<S.IContractReviewStore>();
                s.AddSingleton<S.IContractReviewStore>(store);
                s.RemoveAll<S.ILegalLibraryStore>();
                s.AddSingleton<S.ILegalLibraryStore>(store.Library);
                s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                s.AddSingleton(new TestPrincipal(permissions));
                s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                s.PostConfigure<AuthenticationOptions>(o => { o.DefaultScheme = TestScheme; o.DefaultAuthenticateScheme = TestScheme; o.DefaultChallengeScheme = TestScheme; o.DefaultForbidScheme = TestScheme; });
            }));

    // One review (CL000001) with the original (v0) and one staff version (v1, current); findings on v1.
    private sealed class MemoryReviews : S.IContractReviewStore
    {
        public readonly MemoryLibrary Library = new();
        public readonly Guid F1 = Guid.NewGuid(), F2 = Guid.NewGuid();
        public (string? Status, Guid Actor) LastDecision;
        public bool Queued;
        public int CreatedParagraphs;
        private static readonly List<S.ReviewParagraph> V0 = Sample();
        private static readonly List<S.ReviewParagraph> V1 = Sample().Select(p => p.Key == "P3" ? p with { Text = "Bên B thanh toán trong 15 ngày bằng chuyển khoản." } : p).ToList();

        private S.ReviewDetail Detail() => new(R1, "CL000001", C1, "Công ty An Phát", "KH001", null, null, "Hợp đồng dịch vụ kế toán", "A", "IN_REVIEW", 1, null, null,
            DateTime.UtcNow, "Nhân viên", "AA", [new(0, "ORIGINAL", "Bản gốc của khách hàng", null, DateTime.UtcNow, "Nhân viên", 5), new(1, "STAFF", "Bản sửa lần 1", null, DateTime.UtcNow, "Nhân viên", 5)],
            [new(Guid.NewGuid(), 0, "ORIGINAL", "hop-dong.docx", 1000, DateTime.UtcNow)], []);

        public Task<IReadOnlyList<S.ReviewListItem>> ListAsync(S.ReviewFilter filter) =>
            Task.FromResult<IReadOnlyList<S.ReviewListItem>>([new(R1, "CL000001", C1, "Công ty An Phát", "KH001", "Hợp đồng dịch vụ kế toán", "A", "IN_REVIEW", 1, 1, DateTime.UtcNow, "Nhân viên")]);
        public Task<S.ReviewLookups> LookupsAsync() => Task.FromResult(new S.ReviewLookups([new(C1, "KH001", "Công ty An Phát")], []));
        public Task<S.ReviewDetail?> GetAsync(Guid id) => Task.FromResult(id == R1 ? Detail() : null);
        public Task<IReadOnlyList<S.ReviewParagraph>> ParagraphsAsync(Guid id, int versionNo) => Task.FromResult<IReadOnlyList<S.ReviewParagraph>>(versionNo == 0 ? V0 : V1);
        public Task<IReadOnlyList<S.ReviewFinding>> FindingsAsync(Guid id) => Task.FromResult<IReadOnlyList<S.ReviewFinding>>(
        [
            new(F1, 1, "P3", "REPLACE", "15 ngày", "Thời hạn thanh toán quá dài <script>alert(1)</script>", "HIGH", "A", "Điều 418 – 91/2015/QH13", false, null, "10 ngày", "OPEN", null, null, "AI", null, DateTime.UtcNow, null, null),
            new(F2, 1, "P4", "DELETE", null, "Mức phạt vượt 8%", "MEDIUM", "A", null, false, null, null, "OPEN", null, null, "AI", null, DateTime.UtcNow, null, null),
            new(Guid.NewGuid(), 1, "P5", "REPLACE", "từ ngày ký", "Ghi rõ thời điểm", "LOW", "BOTH", null, false, null, "từ ngày hai bên ký và Bên B nhận đủ tạm ứng", "ACCEPTED", null, null, "AI", null, DateTime.UtcNow, DateTime.UtcNow, "Nhân viên")
        ]);
        public Task<IReadOnlyList<S.ReviewChange>> ChangesAsync(Guid id) => Task.FromResult<IReadOnlyList<S.ReviewChange>>(
            [new(Guid.NewGuid(), 0, 1, "P3", F1, V0[2].Text, V1[2].Text, "Thời hạn thanh toán quá dài", "PENDING", null, null, null)]);
        public Task<S.ReviewFileContent?> DownloadAsync(Guid id, Guid fileId) => Task.FromResult<S.ReviewFileContent?>(null);

        public Task<S.ReviewResult> CreateAsync(Guid actor, S.ReviewInput input, S.ReviewUpload upload, string? ip)
        {
            CreatedParagraphs = upload.Paragraphs.Count;
            return Task.FromResult(new S.ReviewResult(S.ReviewOutcome.Saved, R1, 0));
        }
        public Task<S.ReviewResult> QueueRunAsync(Guid actor, Guid id, bool mask, string? ip) { Queued = true; return Saved(); }
        public S.ReviewFindingInput? LastAdded;
        public Task<S.ReviewResult> AddFindingAsync(Guid actor, Guid id, S.ReviewFindingInput input, string? ip)
        {
            LastAdded = input;
            return Task.FromResult(new S.ReviewResult(S.ReviewOutcome.Saved, Guid.NewGuid(), 1, AuditPersisted: true));
        }
        public Task<S.ReviewResult> DecideFindingAsync(Guid actor, Guid id, Guid findingId, string status, string? staffText, string? staffNote, string? ip)
        {
            LastDecision = (status, actor);
            return Saved();
        }
        public Task<S.ReviewResult> CreateVersionAsync(Guid actor, Guid id, string? note, string? ip) => Saved();
        public Task<S.ReviewResult> MarkSentAsync(Guid actor, Guid id, string? ip) => Saved();
        public Task<S.ReviewResult> RecordCustomerAsync(Guid actor, Guid id, Guid changeId, string decision, string? customerText, string? customerNote, string? ip) => Saved();
        public Task<S.ReviewResult> CreateCustomerVersionAsync(Guid actor, Guid id, string? note, string? ip) => Saved();
        public Task<S.ReviewResult> FinalizeAsync(Guid actor, Guid id, int versionNo, string? stamp, string? ip) => Saved();
        public Task<S.ReviewResult> CancelAsync(Guid actor, Guid id, string? stamp, string? ip) => Saved();
        public Task<S.ReviewRunJob?> ClaimRunAsync() => Task.FromResult<S.ReviewRunJob?>(null);
        public Task CompleteRunAsync(S.ReviewRunJob job, S.ReviewAiResult result) => Task.CompletedTask;
        public Task FailRunAsync(Guid runId, Guid reviewId, string error) => Task.CompletedTask;
        public Task<int> RecoverStaleRunsAsync(TimeSpan olderThan) => Task.FromResult(0);
        private static Task<S.ReviewResult> Saved() => Task.FromResult(new S.ReviewResult(S.ReviewOutcome.Saved, R1, 1, AuditPersisted: true));
    }

    // One law (91/2015/QH13) with two articles and two uploaded files (current + older).
    private sealed class MemoryLibrary : S.ILegalLibraryStore
    {
        public static readonly Guid Doc = Guid.Parse("d7000000-0000-0000-0000-0000000000d7");
        public (string? FileName, int Articles) LastUpload;
        private static readonly S.LegalDocumentItem Item = new(Doc, "91/2015/QH13", "Bộ luật Dân sự", "CODE", null, new(2017, 1, 1), null, null, 2, DateTime.UtcNow);

        public Task<IReadOnlyList<S.LegalDocumentItem>> ListAsync() => Task.FromResult<IReadOnlyList<S.LegalDocumentItem>>([Item]);
        public Task<S.LegalDocumentDetail?> GetAsync(Guid id) => Task.FromResult<S.LegalDocumentDetail?>(id != Doc ? null : new(Item,
            [new(1, "Điều 418", "Thỏa thuận phạt vi phạm", "1. Phạt vi phạm là ...", null), new(2, "Điều 419", "Thiệt hại", "Nội dung", DateTime.UtcNow)],
            [new(Guid.NewGuid(), "blds-2015-v2.docx", "application/pdf", 1000, 689, true, null, DateTime.UtcNow, "Nhân viên"),
             new(Guid.NewGuid(), "blds-2015.pdf", "application/pdf", 900, 689, false, null, DateTime.UtcNow.AddDays(-1), "Nhân viên")]));
        public Task<IReadOnlyList<S.LegalIndexArticle>> IndexAsync(DateTime today) => Task.FromResult<IReadOnlyList<S.LegalIndexArticle>>([]);
        public Task<(S.LegalOutcome Outcome, Guid? Id, string? Summary)> SaveAsync(Guid actor, Guid? id, S.LegalDocumentInput input, S.LegalUpload? upload, string? ip)
        {
            LastUpload = (upload?.FileName, upload is null ? 0 : S.LegalTextParser.Split(upload.Text).Count);
            return Task.FromResult<(S.LegalOutcome, Guid?, string?)>((S.LegalOutcome.Saved, Doc, "tách được 2 điều"));
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
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Actor.ToString()), new(ClaimTypes.Name, "safe-review-test"), new("display_name", "Rà soát"), new(ClaimTypes.Role, "STAFF") };
            claims.AddRange(principal.Permissions.Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, TestScheme)), TestScheme)));
        }
    }
}
