using TAPortal.Web.Services;
using TAWebsite.Core.Content;
using TAWebsite.Core.Data;

namespace TAPortal.Web.Models;

public sealed record WebsiteIndexVm(WebsiteOverview? Overview, IReadOnlyList<RenderedPage> Pages, string PublicUrl, string? Error);

public sealed record WebsitePageVm(WebsitePageEditor Editor, string PublicUrl);

public sealed record WebsiteSectionVm(PageDef Page, SectionDef Section, SectionContent Content, string? Stamp, bool IsEdited, DateTime? UpdatedAt,
    IReadOnlyList<string> Errors, string PublicUrl);

public sealed record WebsiteFieldVm(FieldDef Field, SectionContent Content, string Name);

public sealed record WebsiteSettingsVm(SiteSettings Settings, string? Error);

public sealed record WebsiteMediaVm(IReadOnlyList<WebsiteMediaItem> Items, string? Search, string? Error, TAWebsite.Core.MediaKind Kind = TAWebsite.Core.MediaKind.Image);

public sealed record WebsitePostsVm(IReadOnlyList<WebsitePostListItem> Items, IReadOnlyList<PostCategory> Categories, string? Status, Guid? Category,
    string? Search, DateTime NowUtc, string PublicUrl, string? Error);

public sealed record WebsiteAiCreateVm(bool Configured, WebsiteAiForm Form, string? Error);

/// <summary>The "Soạn bài bằng AI" form. Kind 0 = tự biên soạn (Topic required), 1 = tóm tắt có trích nguồn (SourceUrl required).</summary>
public sealed class WebsiteAiForm
{
    public byte Kind { get; set; }
    public string? Topic { get; set; }
    public string? SourceUrl { get; set; }
    public string? Notes { get; set; }
    public string? Category { get; set; }

    public string? Validate()
    {
        if (Kind != TAWebsite.Core.Data.PostSource.Own && Kind != TAWebsite.Core.Data.PostSource.Summary) Kind = TAWebsite.Core.Data.PostSource.Own;
        if (!string.IsNullOrWhiteSpace(Category) && !PostLibrary.Categories.Any(c => c.Slug == Category)) Category = null;
        if (Topic?.Length > 500 || Notes?.Length > 1000) return "Chủ đề tối đa 500 ký tự, yêu cầu thêm tối đa 1000 ký tự.";
        if (Kind == TAWebsite.Core.Data.PostSource.Own)
            return string.IsNullOrWhiteSpace(Topic) || Topic.Trim().Length < 10 ? "Nhập chủ đề bài viết (ít nhất 10 ký tự)." : null;
        return string.IsNullOrWhiteSpace(SourceUrl) || SourceUrl.Trim().Length > 500 || !TAWebsite.Core.Data.PostSource.IsHttpUrl(SourceUrl.Trim())
            ? "Dán đường dẫn bài gốc bắt đầu bằng http:// hoặc https://." : null;
    }
}

public sealed record WebsitePostLibraryVm(IReadOnlyList<LibraryPost> Posts, IReadOnlyDictionary<string, Guid> Existing, string? Error,
    IReadOnlySet<string>? Drafts = null)
{
    public int Missing => Posts.Count(p => !Existing.ContainsKey(p.Slug));
    public bool IsDraft(string slug) => Drafts?.Contains(slug) == true;
    /// <summary>Library articles not live yet: not added, or added but still a draft.</summary>
    public int Unpublished => Posts.Count(p => !Existing.ContainsKey(p.Slug) || IsDraft(p.Slug));
}

public sealed record WebsitePostEditVm(Guid? Id, WebsitePostInput Input, WebsitePostEdit? Current, IReadOnlyList<PostCategory> Categories, string PublicUrl)
{
    public bool IsCreate => Id is null;
}

// Columns of the "Yêu cầu báo giá" Excel export.
public static class LeadExport
{
    public static readonly IReadOnlyList<string> Headers =
        ["Thời gian", "Họ tên", "Điện thoại", "Email", "Công ty", "Dịch vụ quan tâm", "Tình trạng DN", "Quy mô", "Thời gian liên hệ", "Nội dung", "Trang gửi", "Trạng thái", "Ghi chú nội bộ"];

    public static readonly IReadOnlyList<double> Widths = [17, 24, 14, 26, 28, 32, 20, 14, 18, 50, 22, 15, 40];

    public static IReadOnlyList<string?> Row(TAPortal.Web.Services.WebsiteLeadItem l) =>
        [WebsiteUi.Local(l.CreatedAt).ToString("dd/MM/yyyy HH:mm"), l.FullName, l.Phone, l.Email, l.CompanyName, l.Services, l.CompanyType, l.Scale, l.ContactTime,
         l.Message, l.SourcePage, TAWebsite.Core.Data.LeadStatus.Name(l.Status), l.Note];
}

public sealed record WebsiteLeadsVm(IReadOnlyList<WebsiteLeadItem> Items, byte? Status, string? Search, string? Error);

public static class WebsiteUi
{
    public const string MediaBase = "/WebsiteMedia/File";

    public static string ImageUrl(string? value) => ImageRef.Url(value, MediaBase);

    public static string ImageUrl(Guid? id) => id is Guid g ? $"{MediaBase}/{g:N}" : "";

    public static string PublicUrl(IConfiguration configuration) => (configuration["Website:PublicUrl"] ?? "https://tuvangiaiphapta.vn").TrimEnd('/');

    public static string Size(int bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    public static DateTime Local(DateTime utc) => utc + WebsitePostInput.VietnamOffset;

    public static string Message(WebsiteOutcome outcome) => outcome switch
    {
        WebsiteOutcome.Forbidden => "Bạn không có quyền thực hiện thao tác này.",
        WebsiteOutcome.NotFound => "Không tìm thấy dữ liệu (có thể đã bị xóa).",
        WebsiteOutcome.Conflict => "Nội dung vừa được người khác cập nhật. Mở lại trang để xem bản mới nhất rồi sửa tiếp.",
        WebsiteOutcome.Duplicate => "Đường dẫn (slug) đã được dùng, hãy chọn đường dẫn khác.",
        WebsiteOutcome.Invalid => "Dữ liệu không hợp lệ.",
        WebsiteOutcome.AuditUnavailable => "Không ghi được nhật ký thay đổi nên chưa lưu. Vui lòng báo quản trị hệ thống.",
        _ => "Đã lưu."
    };
}
