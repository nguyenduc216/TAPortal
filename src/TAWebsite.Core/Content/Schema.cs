namespace TAWebsite.Core.Content;

// Kinds of field an editor fills in the Admin "Website TA" section. The Admin form is generated from these definitions and
// the public site reads the same keys, so a section type is the contract between both apps.
public enum FieldKind
{
    Text,      // one line
    TextArea,  // a paragraph; line breaks are kept
    Lines,     // a list of short strings, one per line (e.g. bullet points)
    Image,     // "asset:<file>" (default image shipped with the site) or "media:<guid>" (uploaded in the Admin)
    Url,       // site path ("/lien-he"), anchor ("#services") or https:// link
    List,      // repeated group of sub-fields (cards, steps, FAQ items...)
    Html       // a whole section edited visually in the Admin portal (sanitized with PostHtml.SanitizeSection)
}

public sealed record FieldDef(string Key, string Label, FieldKind Kind, string? Help = null, IReadOnlyList<FieldDef>? Items = null, int MaxItems = 30)
{
    public static FieldDef Text(string key, string label, string? help = null) => new(key, label, FieldKind.Text, help);
    public static FieldDef Area(string key, string label, string? help = null) => new(key, label, FieldKind.TextArea, help);
    public static FieldDef Lines(string key, string label, string? help = null) => new(key, label, FieldKind.Lines, help ?? "Mỗi dòng một ý.");
    public static FieldDef Image(string key, string label, string? help = null) => new(key, label, FieldKind.Image, help);
    public static FieldDef Url(string key, string label, string? help = null) => new(key, label, FieldKind.Url, help ?? "Đường dẫn trong website (/lien-he), mục trong trang (#dich-vu) hoặc https://…");
    public static FieldDef List(string key, string label, int max, params FieldDef[] items) => new(key, label, FieldKind.List, null, items, max);
    public static FieldDef Html(string key, string label, string? help = null) => new(key, label, FieldKind.Html, help);
}

public sealed record SectionType(string Key, string Name, string Description, IReadOnlyList<FieldDef> Fields);

// Every section layout the public site can render. TAWebsite.Web has one partial view per key (Views/Sections/<key>.cshtml).
public static class SectionTypes
{
    private static readonly FieldDef[] Heading =
    [
        FieldDef.Text("eyebrow", "Dòng nhỏ phía trên tiêu đề"),
        FieldDef.Text("title", "Tiêu đề"),
        FieldDef.Area("intro", "Đoạn giới thiệu")
    ];

    public static readonly IReadOnlyList<SectionType> All =
    [
        new("home-hero", "Banner đầu trang chủ", "Tiêu đề lớn, cam kết, 2 nút và ảnh chuyên viên.",
        [
            FieldDef.Text("kicker", "Dòng nhỏ phía trên"),
            FieldDef.Area("title", "Tiêu đề lớn", "Xuống dòng để ngắt tiêu đề."),
            FieldDef.Text("ribbon", "Dải chữ dưới tiêu đề"),
            FieldDef.Lines("promises", "Các cam kết (có dấu ✓)"),
            FieldDef.Text("primaryLabel", "Nút chính – chữ"), FieldDef.Url("primaryUrl", "Nút chính – liên kết"),
            FieldDef.Text("secondaryLabel", "Nút phụ – chữ"), FieldDef.Url("secondaryUrl", "Nút phụ – liên kết"),
            FieldDef.Image("image", "Ảnh bên phải"), FieldDef.Text("imageAlt", "Mô tả ảnh (cho người khiếm thị, SEO)"),
            FieldDef.Lines("tags", "Dải dịch vụ cuối banner")
        ]),
        new("image-cards", "Thẻ ảnh + chữ", "Hàng thẻ có ảnh, tiêu đề và mô tả ngắn.",
        [
            FieldDef.List("items", "Các thẻ", 6, FieldDef.Image("image", "Ảnh"), FieldDef.Text("imageAlt", "Mô tả ảnh"),
                FieldDef.Text("title", "Tiêu đề"), FieldDef.Area("text", "Mô tả"))
        ]),
        new("home-featured", "Dịch vụ nổi bật + quy trình + banner đối tác", "Cột dịch vụ nổi bật, 4 bước quy trình và banner ảnh.",
        [
            FieldDef.Text("featuredTitle", "Tiêu đề cột dịch vụ"),
            FieldDef.List("featured", "Dịch vụ nổi bật", 8, FieldDef.Text("label", "Tên"), FieldDef.Url("url", "Liên kết")),
            FieldDef.Text("allLabel", "Chữ nút xem tất cả"), FieldDef.Url("allUrl", "Liên kết xem tất cả"),
            FieldDef.Text("stepsTitle", "Tiêu đề quy trình"),
            FieldDef.List("steps", "Các bước", 4, FieldDef.Text("title", "Tên bước"), FieldDef.Text("text", "Mô tả")),
            FieldDef.Image("bannerImage", "Ảnh banner"), FieldDef.Text("bannerImageAlt", "Mô tả ảnh banner"),
            FieldDef.Text("bannerTitle", "Tiêu đề banner"), FieldDef.Area("bannerText", "Mô tả banner"),
            FieldDef.Text("bannerLinkLabel", "Chữ liên kết banner"), FieldDef.Url("bannerLinkUrl", "Liên kết banner")
        ]),
        new("image-text", "Ảnh + nội dung + giá trị", "Ảnh bên trái, tiêu đề, đoạn văn, các giá trị và liên kết.",
        [
            FieldDef.Image("image", "Ảnh"), FieldDef.Text("imageAlt", "Mô tả ảnh"),
            ..Heading,
            FieldDef.List("values", "Giá trị", 4, FieldDef.Text("title", "Tiêu đề"), FieldDef.Text("text", "Mô tả")),
            FieldDef.Text("linkLabel", "Chữ liên kết"), FieldDef.Url("linkUrl", "Liên kết")
        ]),
        new("service-cards", "Thẻ dịch vụ chi tiết", "Lưới thẻ dịch vụ có ảnh, mô tả, gạch đầu dòng và nút liên hệ.",
        [
            ..Heading,
            FieldDef.List("items", "Dịch vụ", 12, FieldDef.Image("image", "Ảnh"), FieldDef.Text("imageAlt", "Mô tả ảnh"),
                FieldDef.Text("eyebrow", "Dòng nhỏ"), FieldDef.Text("title", "Tên dịch vụ"), FieldDef.Text("lead", "Câu nhấn mạnh"),
                FieldDef.Area("text", "Mô tả"), FieldDef.Lines("points", "Gạch đầu dòng"),
                FieldDef.Text("linkLabel", "Chữ nút"), FieldDef.Url("linkUrl", "Liên kết nút"))
        ]),
        new("latest-posts", "Bài viết mới (tự động)", "Lấy các bài viết đã xuất bản mới nhất; chỉ sửa tiêu đề.",
        [
            ..Heading,
            FieldDef.Text("linkLabel", "Chữ liên kết xem thêm"),
            FieldDef.Text("feedTitle", "Tiêu đề cột bên phải")
        ]),
        new("faq", "Câu hỏi thường gặp", "Danh sách câu hỏi – trả lời dạng mở/đóng.",
        [
            ..Heading,
            FieldDef.List("items", "Câu hỏi", 20, FieldDef.Text("q", "Câu hỏi"), FieldDef.Area("a", "Trả lời"))
        ]),
        new("testimonials-news", "Khách hàng nói về T.A + tin mới", "Trích dẫn khách hàng (chuyển qua lại) và 3 bài viết mới nhất.",
        [
            FieldDef.Text("title", "Tiêu đề phần khách hàng"),
            FieldDef.List("quotes", "Trích dẫn", 10, FieldDef.Area("text", "Nội dung"), FieldDef.Text("author", "Người nói / công ty")),
            FieldDef.Text("note", "Ghi chú dưới trích dẫn"),
            FieldDef.Text("newsTitle", "Tiêu đề phần tin tức")
        ]),
        new("page-hero", "Banner đầu trang con", "Đường dẫn, tiêu đề và đoạn giới thiệu của trang.",
        [
            ..Heading
        ]),
        new("contact-channels", "Kênh liên hệ", "Các ô liên hệ lấy từ Cài đặt chung (hotline, email, Zalo, văn phòng).",
        [
            FieldDef.Text("hotlineNote", "Ghi chú ô Hotline"), FieldDef.Text("emailNote", "Ghi chú ô Email"),
            FieldDef.Text("zaloNote", "Ghi chú ô Zalo"), FieldDef.Text("officeNote", "Ghi chú ô Văn phòng")
        ]),
        new("quote-form", "Form yêu cầu báo giá", "Form 3 bước; danh sách lựa chọn và khung 'sau khi gửi'.",
        [
            ..Heading,
            FieldDef.List("services", "Dịch vụ để chọn", 12, FieldDef.Text("value", "Mã (không dấu, dùng cho ?dv=)"),
                FieldDef.Text("label", "Tên"), FieldDef.Text("note", "Mô tả ngắn")),
            FieldDef.List("types", "Loại hình / tình trạng", 10, FieldDef.Text("label", "Tên"), FieldDef.Text("note", "Mô tả ngắn")),
            FieldDef.Lines("volumes", "Lựa chọn số hóa đơn / tháng"),
            FieldDef.Lines("staff", "Lựa chọn số nhân viên"),
            FieldDef.Lines("times", "Lựa chọn thời gian liên hệ"),
            FieldDef.Text("nextTitle", "Tiêu đề khung 'Sau khi gửi'"),
            FieldDef.List("nextSteps", "Các bước sau khi gửi", 6, FieldDef.Text("title", "Bước"), FieldDef.Text("text", "Mô tả")),
            FieldDef.Area("consent", "Câu đồng ý sử dụng thông tin"),
            FieldDef.Area("thanks", "Lời cảm ơn sau khi gửi")
        ]),
        new("office", "Văn phòng + bản đồ", "Bản đồ, địa chỉ (từ Cài đặt chung) và giờ làm việc.",
        [
            ..Heading,
            FieldDef.List("hours", "Giờ làm việc", 7, FieldDef.Text("day", "Ngày"), FieldDef.Text("time", "Giờ")),
            FieldDef.Text("hoursNote", "Ghi chú dưới giờ làm việc"),
            FieldDef.Text("bringTitle", "Tiêu đề 'mang theo khi đến'"), FieldDef.Text("bringText", "Nội dung")
        ]),
        new("cta", "Kêu gọi liên hệ", "Dải màu xanh với tiêu đề và 2 nút.",
        [
            ..Heading,
            FieldDef.Text("primaryLabel", "Nút chính – chữ"), FieldDef.Url("primaryUrl", "Nút chính – liên kết"),
            FieldDef.Text("secondaryLabel", "Nút phụ – chữ"), FieldDef.Url("secondaryUrl", "Nút phụ – liên kết")
        ]),
        new("news-sidebar", "Cột phải trang Tin tức", "Hạn nộp sắp tới, ô đăng ký bản tin, kêu gọi liên hệ.",
        [
            FieldDef.Text("deadlinesTitle", "Tiêu đề hạn nộp"),
            FieldDef.List("deadlines", "Hạn nộp", 8, FieldDef.Text("date", "Ngày (vd 20/12)"), FieldDef.Text("title", "Nội dung"), FieldDef.Text("note", "Ghi chú")),
            FieldDef.Text("subscribeTitle", "Tiêu đề đăng ký bản tin"), FieldDef.Text("subscribeText", "Mô tả"),
            FieldDef.Text("ctaTitle", "Tiêu đề hộp liên hệ"), FieldDef.Text("ctaText", "Mô tả hộp liên hệ"), FieldDef.Text("ctaLabel", "Chữ nút")
        ]),
        new("rich-html", "Nội dung trực quan", "Section giữ nguyên thiết kế mockup; sửa trực tiếp chữ, ảnh, liên kết và nhân bản/xóa từng khối trên bản xem trước.",
        [
            FieldDef.Html("html", "Nội dung", "Bấm vào chữ để sửa, bấm vào ảnh để đổi ảnh. Chọn một khối (thẻ, bước, câu hỏi…) để nhân bản, xóa hoặc đổi thứ tự.")
        ]),
        new("footer", "Chân trang", "Giới thiệu ngắn và 2 cột liên kết (cột Liên hệ lấy từ Cài đặt chung).",
        [
            FieldDef.Text("brand", "Tên thương hiệu"), FieldDef.Text("tagline", "Dòng mô tả"), FieldDef.Text("slogan", "Khẩu hiệu"),
            FieldDef.Text("col1Title", "Tiêu đề cột 1"),
            FieldDef.List("col1", "Liên kết cột 1", 8, FieldDef.Text("label", "Chữ"), FieldDef.Url("url", "Liên kết")),
            FieldDef.Text("col2Title", "Tiêu đề cột 2"),
            FieldDef.List("col2", "Liên kết cột 2", 8, FieldDef.Text("label", "Chữ"), FieldDef.Url("url", "Liên kết")),
            FieldDef.Text("copyright", "Dòng bản quyền", "{year} được thay bằng năm hiện tại.")
        ])
    ];

    private static readonly Dictionary<string, SectionType> ByKey = All.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static SectionType? Find(string key) => ByKey.GetValueOrDefault(key);
}
