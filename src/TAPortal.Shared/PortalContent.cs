using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace TAPortal.Shared;

// Operator 2026-11-12: the emails Hóa đơn tự động sends and its terms of service are edited by T.A staff in the Admin portal
// (section "Hóa đơn tự động"), stored in dbo.PortalContent (migration 036) and read by the Customer Portal. Defaults live in
// code: a row exists only once staff saved a change, and "Khôi phục mặc định" deletes it. Before migration 036 runs, or if
// the table cannot be read, the defaults are used.

/// <summary>Codes of dbo.PortalContent rows.</summary>
public static class PortalContentCodes
{
    public const string AccountCreatedEmail = "EMAIL.ACCOUNT_CREATED";
    public const string PasswordResetEmail = "EMAIL.PASSWORD_RESET";
    public const string Terms = "TERMS.SERVICE";
}

/// <summary>One value a template may use, written {{Key}}.</summary>
public sealed record EmailPlaceholder(string Key, string Description, string Sample);

/// <summary>
/// An email Hóa đơn tự động sends. <see cref="Required"/> placeholders must stay in the body (the email is useless without
/// them: the login details, the reset button); the renderer adds a missing one at the end anyway.
/// </summary>
public sealed record EmailTemplateSpec(string Code, string Name, string Description, string DefaultSubject, string DefaultBody,
    string? ButtonLabel, string? ButtonUrlKey, IReadOnlyList<EmailPlaceholder> Placeholders, IReadOnlyList<string> Required);

public static class PortalEmailTemplates
{
    /// <summary>Block placeholders: alone on their own line, they become a box or a button.</summary>
    public const string LoginBox = "ThongTinDangNhap";
    public const string Button = "NutBam";

    public static readonly EmailTemplateSpec AccountCreated = new(
        PortalContentCodes.AccountCreatedEmail,
        "Thông tin đăng nhập (tài khoản mới)",
        "Gửi ngay khi khách hàng tự đăng ký: địa chỉ đăng nhập, tên đăng nhập và mật khẩu tạm do hệ thống tạo.",
        "Thông tin đăng nhập Hóa đơn tự động - {{TenKhachHang}}",
        """
        Xin chào {{NguoiLienHe}},

        Cảm ơn bạn đã đăng ký sử dụng Hóa đơn tự động cho {{TenKhachHang}}. Tài khoản đã được tạo với thông tin đăng nhập dưới đây:

        {{ThongTinDangNhap}}

        Vì lý do bảo mật, hệ thống sẽ yêu cầu bạn đổi mật khẩu ở lần đăng nhập đầu tiên. Không chia sẻ mật khẩu cho bất kỳ ai, kể cả người tự xưng là nhân viên hỗ trợ.

        {{NutBam}}

        Sau khi đăng nhập, bạn có thể:
        - Chọn gói dịch vụ và chuyển khoản để kích hoạt.
        - Liên kết tài khoản ngân hàng nhận tiền.
        - Tạo đơn hàng, gửi mã QR thanh toán cho khách và tự động xuất hóa đơn điện tử khi nhận được tiền.
        - Tra cứu, tải về các hóa đơn điện tử đã xuất ở mục Hóa đơn điện tử.

        Nếu bạn không thực hiện đăng ký này, vui lòng bỏ qua email hoặc liên hệ bộ phận hỗ trợ Hóa đơn tự động.

        Trân trọng,
        Hóa đơn tự động
        """,
        "Đăng nhập ngay", "DiaChiDangNhap",
        [
            new("NguoiLienHe", "Họ tên người liên hệ", "Nguyễn Văn An"),
            new("TenKhachHang", "Tên doanh nghiệp, hộ kinh doanh hoặc cá nhân", "Công ty TNHH Bình Minh"),
            new("MaKhachHang", "Mã khách hàng (mã số thuế, hoặc mã CN… của cá nhân)", "0101234567"),
            new("DiaChiDangNhap", "Địa chỉ trang đăng nhập", "https://hoadon.tuvangiaiphapta.vn/account/login"),
            new("TenDangNhap", "Tên đăng nhập (email)", "an.nguyen@example.com"),
            new("MatKhau", "Mật khẩu tạm do hệ thống tạo", "Xk7#pQ2m!Rt9"),
            new(LoginBox, "Khung thông tin đăng nhập (địa chỉ, tên đăng nhập, mật khẩu tạm) - đặt riêng một dòng", ""),
            new(Button, "Nút \"Đăng nhập ngay\" - đặt riêng một dòng", "")
        ],
        [LoginBox]);

    public static readonly EmailTemplateSpec PasswordReset = new(
        PortalContentCodes.PasswordResetEmail,
        "Đặt lại mật khẩu",
        "Gửi khi khách bấm \"Quên mật khẩu\": liên kết đặt mật khẩu mới, dùng một lần.",
        "Đặt lại mật khẩu Hóa đơn tự động",
        """
        Xin chào {{NguoiLienHe}},

        Có yêu cầu đặt lại mật khẩu Hóa đơn tự động cho tài khoản {{TenDangNhap}}. Bấm nút dưới đây để đặt mật khẩu mới. Liên kết có hiệu lực {{ThoiHanPhut}} phút và chỉ dùng được một lần.

        {{NutBam}}

        Nếu bạn không yêu cầu, hãy bỏ qua email này; mật khẩu hiện tại vẫn giữ nguyên.

        Trân trọng,
        Hóa đơn tự động
        """,
        "Đặt mật khẩu mới", "LienKet",
        [
            new("NguoiLienHe", "Tên hiển thị của tài khoản", "Nguyễn Văn An"),
            new("TenDangNhap", "Tên đăng nhập", "an.nguyen@example.com"),
            new("LienKet", "Liên kết đặt mật khẩu mới", "https://hoadon.tuvangiaiphapta.vn/account/reset-password?token=..."),
            new("ThoiHanPhut", "Số phút liên kết còn hiệu lực", "30"),
            new(Button, "Nút \"Đặt mật khẩu mới\" - đặt riêng một dòng", "")
        ],
        [Button]);

    public static readonly IReadOnlyList<EmailTemplateSpec> All = [AccountCreated, PasswordReset];

    public static EmailTemplateSpec? Find(string? code) => All.FirstOrDefault(t => t.Code == code);

    public static Dictionary<string, string> Samples(EmailTemplateSpec spec) =>
        spec.Placeholders.Where(p => p.Sample.Length > 0).ToDictionary(p => p.Key, p => p.Sample);

    /// <summary>Problems that stop a save: unknown placeholders, a required one removed, empty subject or body.</summary>
    public static IReadOnlyList<string> Validate(EmailTemplateSpec spec, string? subject, string? body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(subject)) errors.Add("Nhập tiêu đề email.");
        else if (subject.Length > 300) errors.Add("Tiêu đề tối đa 300 ký tự.");
        if (string.IsNullOrWhiteSpace(body)) { errors.Add("Nhập nội dung email."); return errors; }
        if (body.Length > 20000) errors.Add("Nội dung tối đa 20.000 ký tự.");
        var known = spec.Placeholders.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in PortalEmailRenderer.Keys((subject ?? "") + "\n" + body).Distinct())
            if (!known.Contains(key)) errors.Add($"Không có giá trị {{{{{key}}}}} cho mẫu này.");
        foreach (var key in PortalEmailRenderer.Keys(subject ?? "").Where(k => k is LoginBox or Button).Distinct())
            errors.Add($"{{{{{key}}}}} chỉ dùng trong nội dung, không dùng trong tiêu đề.");
        foreach (var key in spec.Required)
            if (!PortalEmailRenderer.HasBlock(body, key)) errors.Add($"Nội dung phải giữ {{{{{key}}}}} trên một dòng riêng.");
        return errors;
    }
}

public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody);

/// <summary>
/// Turns a template into the email: plain text with {{Key}} values, paragraphs split by blank lines, lines starting with
/// "- " as a list, and the block placeholders as a login box or a button. Every value and every word of the template is
/// HTML-encoded, so neither staff nor customers can inject markup. The layout (logo, card, footer) is fixed.
/// </summary>
public static partial class PortalEmailRenderer
{
    [GeneratedRegex(@"\{\{([A-Za-z]+)\}\}")] private static partial Regex Placeholder();

    public static IEnumerable<string> Keys(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value);

    public static bool HasBlock(string body, string key) =>
        body.Replace("\r", "").Split('\n').Any(l => l.Trim() == "{{" + key + "}}");

    // Operator 2026-11-13: every email ends with "hoadontudong.vn - 1 sản phẩm của Tư vấn giải pháp T.A", the maker linking to T.A's website.
    public const string ProductHost = "hoadontudong.vn";
    public const string ProductUrl = "https://hoadontudong.vn";
    public const string MakerUrl = "https://tuvangiaiphapta.vn";

    public static RenderedEmail Render(EmailTemplateSpec spec, string? subject, string? body, IReadOnlyDictionary<string, string> values, string baseUrl)
    {
        subject = string.IsNullOrWhiteSpace(subject) ? spec.DefaultSubject : subject;
        body = string.IsNullOrWhiteSpace(body) ? spec.DefaultBody : body;
        foreach (var key in spec.Required.Where(k => !HasBlock(body, k)))
            body += "\n\n{{" + key + "}}";
        baseUrl = baseUrl.TrimEnd('/');
        string Value(string key) => values.TryGetValue(key, out var v) ? v : "";
        string Fill(string s) => Placeholder().Replace(s, m => Value(m.Groups[1].Value));
        var subjectText = Fill(subject).Replace("\r", " ").Replace("\n", " ").Trim();

        var text = new StringBuilder();
        var html = new StringBuilder();
        foreach (var block in Blocks(body))
        {
            var only = block.Count == 1 ? Placeholder().Match(block[0].Trim()) : null;
            if (only is { Success: true } && only.Value == block[0].Trim() && only.Groups[1].Value is PortalEmailTemplates.LoginBox or PortalEmailTemplates.Button)
            {
                if (only.Groups[1].Value == PortalEmailTemplates.LoginBox)
                {
                    var rows = new[] { ("Địa chỉ đăng nhập", Value("DiaChiDangNhap"), false), ("Tên đăng nhập", Value("TenDangNhap"), false), ("Mật khẩu tạm", Value("MatKhau"), true) };
                    foreach (var (label, value, _) in rows) text.Append(label).Append(": ").Append(value).Append('\n');
                    text.Append('\n');
                    html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:4px 0 18px;background:#f0f6ff;border:1px solid #c7dbff;border-radius:10px\"><tr><td style=\"padding:14px 18px\">");
                    foreach (var (label, value, mono) in rows)
                        html.Append("<div style=\"margin:4px 0\"><span style=\"display:inline-block;min-width:140px;color:#64748b;font-size:13px\">").Append(E(label))
                            .Append("</span><strong style=\"color:#0f172a;").Append(mono ? "font-family:Consolas,Menlo,monospace;font-size:16px;letter-spacing:.5px" : "word-break:break-all")
                            .Append("\">").Append(E(value)).Append("</strong></div>");
                    html.Append("</td></tr></table>");
                }
                else if (spec.ButtonLabel is string label && spec.ButtonUrlKey is string urlKey && Value(urlKey) is { Length: > 0 } url)
                {
                    text.Append(label).Append(": ").Append(url).Append("\n\n");
                    html.Append("<p style=\"margin:8px 0 6px\"><a href=\"").Append(E(url)).Append("\" style=\"display:inline-block;padding:11px 22px;background:#1d4ed8;color:#ffffff;border-radius:8px;text-decoration:none;font-weight:bold\">")
                        .Append(E(label)).Append("</a></p><p style=\"margin:0 0 16px;color:#64748b;font-size:12.5px\">Nếu nút không mở được, chép địa chỉ sau vào trình duyệt:<br><span style=\"word-break:break-all\">")
                        .Append(E(url)).Append("</span></p>");
                }
                continue;
            }
            // A paragraph may mix text lines and "- " list lines: each run becomes a <p> or a <ul>.
            foreach (var run in Runs(block))
            {
                if (run[0].StartsWith("- ", StringComparison.Ordinal))
                {
                    html.Append("<ul style=\"margin:0 0 16px;padding-left:22px\">");
                    foreach (var line in run)
                    {
                        text.Append("- ").Append(Fill(line[2..])).Append('\n');
                        html.Append("<li style=\"margin:3px 0\">").Append(E(Fill(line[2..]))).Append("</li>");
                    }
                    html.Append("</ul>");
                }
                else
                {
                    text.Append(string.Join("\n", run.Select(Fill))).Append('\n');
                    html.Append("<p style=\"margin:0 0 ").Append(run == block ? "16" : "6").Append("px\">").Append(string.Join("<br>", run.Select(l => E(Fill(l))))).Append("</p>");
                }
            }
            text.Append('\n');
        }

        var page = $"""
            <!doctype html>
            <html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(subjectText)}</title></head>
            <body style="margin:0;padding:0;background:#f1f4f9">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f1f4f9;padding:24px 10px"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border:1px solid #e2e8f0;border-radius:14px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:#1e293b">
            <tr><td style="padding:18px 28px;border-bottom:3px solid #1d4ed8">
            <img src="{E(baseUrl)}/images/logo.png" width="44" height="44" alt="Hóa đơn tự động" style="vertical-align:middle;border:0;border-radius:8px">
            <span style="vertical-align:middle;margin-left:10px;font-size:19px;font-weight:bold;color:#0b2e50">Hóa đơn tự động</span>
            </td></tr>
            <tr><td style="padding:26px 28px 10px">{html}</td></tr>
            <tr><td style="padding:14px 28px;background:#f8fafc;border-top:1px solid #e2e8f0;border-radius:0 0 14px 14px;font-size:12.5px;color:#64748b">
            Email tự động từ Hóa đơn tự động, vui lòng không trả lời email này.<br><a href="{ProductUrl}" style="color:#1d4ed8;text-decoration:none">{ProductHost}</a> - 1 sản phẩm của <a href="{MakerUrl}" style="color:#1d4ed8;text-decoration:none">Tư vấn giải pháp T.A</a>
            </td></tr>
            </table></td></tr></table>
            </body></html>
            """;
        return new(subjectText, text.ToString().TrimEnd() + $"\n\n--\n{ProductHost} - 1 sản phẩm của Tư vấn giải pháp T.A ({MakerUrl})\n", page);
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>Consecutive lines of the same kind (list item or text) of one paragraph.</summary>
    public static List<List<string>> Runs(List<string> block)
    {
        var runs = new List<List<string>>();
        foreach (var line in block)
        {
            var bullet = line.StartsWith("- ", StringComparison.Ordinal);
            if (runs.Count == 0 || runs[^1][0].StartsWith("- ", StringComparison.Ordinal) != bullet) runs.Add([]);
            runs[^1].Add(line);
        }
        return runs;
    }

    /// <summary>Paragraphs: runs of non-blank lines, each line trimmed at the end.</summary>
    internal static List<List<string>> Blocks(string body)
    {
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in body.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) { if (current.Count > 0) { blocks.Add(current); current = []; } continue; }
            current.Add(line.TrimStart());
        }
        if (current.Count > 0) blocks.Add(current);
        return blocks;
    }
}

/// <summary>One article of the terms: a heading and its paragraphs (a paragraph of "- " lines is a list).</summary>
public sealed record TermsArticle(string Title, IReadOnlyList<IReadOnlyList<string>> Paragraphs);

public static class PortalTerms
{
    public const string Title = "Điều khoản sử dụng dịch vụ Hóa đơn tự động";

    /// <summary>"## " lines start an article; text before the first heading goes into an untitled first article.</summary>
    public static IReadOnlyList<TermsArticle> Parse(string? text)
    {
        var articles = new List<TermsArticle>();
        string? title = null;
        var lines = new List<string>();
        void Flush()
        {
            var paragraphs = PortalEmailRenderer.Blocks(string.Join("\n", lines)).Select(b => (IReadOnlyList<string>)b).ToList();
            if (title is not null || paragraphs.Count > 0) articles.Add(new(title ?? "", paragraphs));
            lines.Clear();
        }
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## ", StringComparison.Ordinal)) { Flush(); title = raw[3..].Trim(); }
            else lines.Add(raw);
        }
        Flush();
        return articles;
    }

    public static IReadOnlyList<string> Validate(string? text)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) errors.Add("Nhập nội dung điều khoản.");
        else if (text.Length > 200000) errors.Add("Nội dung tối đa 200.000 ký tự.");
        else if (Parse(text).Count(a => a.Title.Length > 0) == 0) errors.Add("Cần ít nhất một điều, bắt đầu bằng \"## \" (ví dụ \"## Điều 1. Phạm vi áp dụng\").");
        return errors;
    }
}

public sealed record PortalContentEntry(string Code, string? Subject, string Body, DateTime UpdatedAt, Guid? UpdatedBy);

public interface IPortalContentStore
{
    /// <summary>The saved row, or null when the default applies (never saved, or dbo.PortalContent unreadable).</summary>
    Task<PortalContentEntry?> GetAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, PortalContentEntry>> AllAsync(CancellationToken ct = default);
    Task SaveAsync(string code, string? subject, string body, Guid actor, string? ip);
    /// <summary>Back to the default (deletes the row); false when there was nothing to reset.</summary>
    Task<bool> ResetAsync(string code, Guid actor, string? ip);
}

public sealed class SqlPortalContentStore(IConfiguration configuration) : IPortalContentStore
{
    public const string AuditSaved = "PORTAL_CONTENT_SAVED";
    public const string AuditReset = "PORTAL_CONTENT_RESET";

    private SqlConnection Connection() =>
        new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));

    public async Task<PortalContentEntry?> GetAsync(string code, CancellationToken ct = default) =>
        (await AllAsync(ct)).GetValueOrDefault(code);

    public async Task<IReadOnlyDictionary<string, PortalContentEntry>> AllAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, PortalContentEntry>(StringComparer.Ordinal);
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("IF OBJECT_ID(N'dbo.PortalContent',N'U') IS NOT NULL SELECT Code,Subject,Body,UpdatedAt,UpdatedBy FROM dbo.PortalContent", cn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result[r.GetString(0)] = new(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetDateTime(3), r.IsDBNull(4) ? null : r.GetGuid(4));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException) { result.Clear(); }
        return result;
    }

    public async Task SaveAsync(string code, string? subject, string body, Guid actor, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        await using (var cmd = new SqlCommand("""
            UPDATE dbo.PortalContent WITH (UPDLOCK, HOLDLOCK) SET Subject=@s, Body=@b, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@u WHERE Code=@c;
            IF @@ROWCOUNT = 0 INSERT dbo.PortalContent(Code,Subject,Body,UpdatedAt,UpdatedBy) VALUES(@c,@s,@b,SYSUTCDATETIME(),@u);
            """, cn, tx))
        {
            cmd.Parameters.AddWithValue("@c", code);
            cmd.Parameters.AddWithValue("@s", (object?)subject ?? DBNull.Value);
            cmd.Parameters.Add("@b", System.Data.SqlDbType.NVarChar, -1).Value = body;
            cmd.Parameters.AddWithValue("@u", actor);
            await cmd.ExecuteNonQueryAsync();
        }
        // Content itself is not copied into the audit row (it can be long); code and size are enough to trace the change.
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, AuditSaved, "dbo.PortalContent", code, "{}",
            AuditSql.Json(("code", code), ("subjectLength", subject?.Length ?? 0), ("bodyLength", body.Length)), ip);
        await tx.CommitAsync();
    }

    public async Task<bool> ResetAsync(string code, Guid actor, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        int deleted;
        await using (var cmd = new SqlCommand("DELETE FROM dbo.PortalContent WHERE Code=@c", cn, tx))
        {
            cmd.Parameters.AddWithValue("@c", code);
            deleted = await cmd.ExecuteNonQueryAsync();
        }
        if (deleted > 0)
            await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, AuditReset, "dbo.PortalContent", code, "{}", AuditSql.Json(("code", code)), ip);
        await tx.CommitAsync();
        return deleted > 0;
    }
}
