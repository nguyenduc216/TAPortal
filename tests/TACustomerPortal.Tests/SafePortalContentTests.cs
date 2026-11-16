extern alias admin;

using System.Text.RegularExpressions;
using TAPortal.Shared;
using AdminSecurity = admin::TAPortal.Web.Security;

namespace TACustomerPortal.Tests;

// Operator 2026-11-12: emails of Hóa đơn tự động are edited as plain text in the Admin portal and wrapped in a fixed layout
// with the logo; the terms of service are a "## " list of articles. Migration 036 stores them and adds INVOICEAPP.*.
public sealed class SafePortalContentTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["NguoiLienHe"] = "An <b>", ["TenKhachHang"] = "Công ty \"A\" & B", ["MaKhachHang"] = "0101234567",
        ["DiaChiDangNhap"] = "https://hoadon.example.vn/account/login", ["TenDangNhap"] = "an@example.vn", ["MatKhau"] = "Xk7#pQ2m!<t9"
    };

    // Operator 2026-11-13: the footer names the product site and its maker, linking to T.A's website.
    [Fact]
    public void EveryEmailEndsWithTheProductSiteAndItsMaker()
    {
        foreach (var spec in PortalEmailTemplates.All)
        {
            var mail = PortalEmailRenderer.Render(spec, null, null, PortalEmailTemplates.Samples(spec), "https://hoadon.example.vn");
            Assert.Contains("<a href=\"https://hoadontudong.vn\" style=\"color:#1d4ed8;text-decoration:none\">hoadontudong.vn</a> - 1 sản phẩm của <a href=\"https://tuvangiaiphapta.vn\"", mail.HtmlBody);
            Assert.Contains(">Tư vấn giải pháp T.A</a>", mail.HtmlBody);
            Assert.EndsWith("hoadontudong.vn - 1 sản phẩm của Tư vấn giải pháp T.A (https://tuvangiaiphapta.vn)\n", mail.TextBody);
        }
    }

    [Fact]
    public void TheDefaultLoginEmailHasTheLogoTheLoginBoxAndTheButton()
    {
        var mail = PortalEmailRenderer.Render(PortalEmailTemplates.AccountCreated, null, null, Values, "https://hoadon.example.vn/");
        Assert.Equal("Thông tin đăng nhập Hóa đơn tự động - Công ty \"A\" & B", mail.Subject);
        Assert.Contains("<img src=\"https://hoadon.example.vn/images/logo.png\"", mail.HtmlBody);
        Assert.Contains("Mật khẩu tạm: Xk7#pQ2m!<t9", mail.TextBody);
        Assert.Contains("Xk7#pQ2m!&lt;t9", mail.HtmlBody);
        Assert.Contains(">Đăng nhập ngay</a>", mail.HtmlBody);
        Assert.Contains("<ul", mail.HtmlBody);
        // Values never become markup.
        Assert.DoesNotContain("<b>", mail.HtmlBody);
        Assert.Contains("An &lt;b&gt;", mail.HtmlBody);
        Assert.DoesNotContain("{{", mail.HtmlBody + mail.TextBody + mail.Subject);
    }

    [Fact]
    public void StaffTextIsEncodedAndARemovedLoginBoxComesBack()
    {
        var mail = PortalEmailRenderer.Render(PortalEmailTemplates.AccountCreated, "Chào {{NguoiLienHe}}", "Xin chào <script>alert(1)</script>\n\nMật khẩu: {{MatKhau}}", Values, "https://hoadon.example.vn");
        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.Contains("Tên đăng nhập: an@example.vn", mail.TextBody);   // the required box was added at the end
    }

    [Fact]
    public void SavingRejectsUnknownOrMissingPlaceholders()
    {
        var spec = PortalEmailTemplates.AccountCreated;
        Assert.Empty(PortalEmailTemplates.Validate(spec, spec.DefaultSubject, spec.DefaultBody));
        Assert.Empty(PortalEmailTemplates.Validate(PortalEmailTemplates.PasswordReset, PortalEmailTemplates.PasswordReset.DefaultSubject, PortalEmailTemplates.PasswordReset.DefaultBody));
        Assert.Contains(PortalEmailTemplates.Validate(spec, "x", "{{ThongTinDangNhap}}\n{{SoTien}}"), e => e.Contains("{{SoTien}}"));
        Assert.Contains(PortalEmailTemplates.Validate(spec, "x", "Không có khung"), e => e.Contains("{{ThongTinDangNhap}}"));
        Assert.Contains(PortalEmailTemplates.Validate(spec, "", "{{ThongTinDangNhap}}"), e => e.Contains("tiêu đề"));
        Assert.Contains(PortalEmailTemplates.Validate(spec, "{{NutBam}}", "{{ThongTinDangNhap}}"), e => e.Contains("tiêu đề"));
    }

    [Fact]
    public void TheResetEmailLinksToTheResetPage()
    {
        var link = "https://hoadon.example.vn/account/reset-password?token=abc&x=1";
        var mail = PortalEmailRenderer.Render(PortalEmailTemplates.PasswordReset, null, null,
            new Dictionary<string, string> { ["NguoiLienHe"] = "An", ["TenDangNhap"] = "an", ["LienKet"] = link, ["ThoiHanPhut"] = "30" }, "https://hoadon.example.vn");
        Assert.Contains("href=\"https://hoadon.example.vn/account/reset-password?token=abc&amp;x=1\"", mail.HtmlBody);
        Assert.Contains("Đặt mật khẩu mới: " + link, mail.TextBody);
        Assert.Contains("hiệu lực 30 phút", mail.TextBody);
    }

    [Fact]
    public void TheBuiltInTermsAreAListOfArticlesWithoutPartnerNames()
    {
        var articles = PortalTerms.Parse(PortalTermsDefault.Text);
        Assert.Equal(16, articles.Count);
        Assert.All(articles, a => Assert.Matches(@"^Điều \d+\. ", a.Title));
        Assert.Empty(PortalTerms.Validate(PortalTermsDefault.Text));
        Assert.DoesNotMatch(@"(?i)\b(tingee|heno|vietqr|misa)\b", PortalTermsDefault.Text);
        Assert.NotEmpty(PortalTerms.Validate("chỉ có chữ, không có điều nào"));
        var parsed = PortalTerms.Parse("Mở đầu\n\n## Điều 1. A\n- một\n- hai\n\nđoạn\ndòng 2");
        Assert.Equal(["", "Điều 1. A"], parsed.Select(a => a.Title));
        Assert.Equal(2, parsed[1].Paragraphs.Count);
    }

    [Fact]
    public void Migration036MatchesCatalogueAndFollowsTheMigrationRules()
    {
        var root = RepoRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "036-portal-content.sql"));
        var verify = File.ReadAllText(Path.Combine(root, "database", "scripts", "061-verify-portal-content.sql"));
        var rollback = File.ReadAllText(Path.Combine(root, "database", "scripts", "062-rollback-portal-content.sql"));
        var expected = AdminSecurity.AdminPermissions.Migration036Codes.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(2, expected.Count);
        Assert.Equal(expected, Regex.Matches(migration, @"\('([A-Z.]+)',\s+N'").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(expected, Regex.Matches(verify, @"\('([A-Z.]+)'\)").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList());
        Assert.DoesNotContain(expected, c => AdminSecurity.AdminPermissions.MigrationCodes.Contains(c));
        const string marker = "A0000036-0000-4000-8000-000000000036";
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains(marker, sql));
        Assert.All(new[] { migration, verify, rollback }, sql => Assert.Contains("IF DB_NAME() <> N'TAPortalSys'", sql));
        Assert.DoesNotContain("STRING_AGG", migration + verify + rollback);
        Assert.Contains("VERIFY_STATUS=NOT_APPLIED", verify);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|DELETE)\b", migration);
        Assert.All(Regex.Matches(rollback, @"(?im)^DELETE FROM [^\r\n]+").Select(m => m.Value), d => Assert.Contains("CreatedBy = @Marker", d));
        // Every code the check constraint allows is one the application uses.
        foreach (var code in new[] { PortalContentCodes.AccountCreatedEmail, PortalContentCodes.PasswordResetEmail, PortalContentCodes.Terms })
            Assert.Contains("'" + code + "'", migration);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TAPortal.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("TAPortal.sln not found");
    }
}
