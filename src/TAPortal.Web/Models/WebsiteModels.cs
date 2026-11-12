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

public sealed record WebsiteMediaVm(IReadOnlyList<WebsiteMediaItem> Items, string? Search, string? Error);

public sealed record WebsitePostsVm(IReadOnlyList<WebsitePostListItem> Items, IReadOnlyList<PostCategory> Categories, string? Status, Guid? Category,
    string? Search, DateTime NowUtc, string PublicUrl, string? Error);

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
