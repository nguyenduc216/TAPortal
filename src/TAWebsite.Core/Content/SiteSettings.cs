namespace TAWebsite.Core.Content;

public sealed record SettingDef(string Key, string Label, string Group, string Default, string? Help = null, bool Multiline = false);

// General settings shown on every page (header, footer, contact page, map). Stored in dbo.WebsiteSettings (key/value);
// a missing row uses the default below. Address from the business registration (masothue.com, MST 4202032682).
public static class SiteSettingDefs
{
    public static readonly IReadOnlyList<SettingDef> All =
    [
        new("company.name", "Tên công ty đầy đủ", "Doanh nghiệp", "Công ty TNHH Tư vấn Giải pháp T.A"),
        new("company.taxCode", "Mã số thuế", "Doanh nghiệp", "4202032682"),
        new("brand.name", "Tên thương hiệu (header)", "Doanh nghiệp", "Tư vấn giải pháp"),
        new("brand.slogan", "Khẩu hiệu (header)", "Doanh nghiệp", "Giải pháp tối ưu - Phát triển bền vững"),
        new("contact.hotline", "Hotline", "Liên hệ", "", "Để trống: website hiện nhãn đỏ 'T.A bổ sung'."),
        new("contact.email", "Email", "Liên hệ", ""),
        new("contact.zalo", "Zalo (số điện thoại hoặc link zalo.me)", "Liên hệ", ""),
        new("office.address", "Địa chỉ đầy đủ", "Văn phòng", "Lô NV-14_11, Khu đô thị mới Phước Long, Phường Nam Nha Trang, Tỉnh Khánh Hòa"),
        new("office.shortAddress", "Địa chỉ ngắn (footer, menu)", "Văn phòng", "Lô NV-14_11, KĐT Phước Long, P. Nam Nha Trang, Khánh Hòa"),
        new("office.mapQuery", "Vị trí trên Google Maps", "Văn phòng", "Khu đô thị mới Phước Long, Nam Nha Trang, Khánh Hòa",
            "Tên địa điểm hoặc tọa độ (vd 12.2088,109.1922). Dùng cho bản đồ và nút chỉ đường."),
        new("social.facebook", "Facebook", "Mạng xã hội", ""),
        new("social.youtube", "YouTube", "Mạng xã hội", ""),
        new("seo.siteName", "Tên website (SEO)", "SEO", "T.A – Tư vấn giải pháp"),
        new("seo.indexing", "Cho phép Google lập chỉ mục (yes/no)", "SEO", "no", "Đặt 'yes' khi website chính thức chạy.")
    ];

    public static SettingDef? Find(string key) => All.FirstOrDefault(s => s.Key == key);
}

public sealed class SiteSettings(IReadOnlyDictionary<string, string?> saved)
{
    public static SiteSettings Defaults { get; } = new(new Dictionary<string, string?>());

    public string this[string key] =>
        saved.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v!.Trim() : SiteSettingDefs.Find(key)?.Default ?? "";

    public string Or(string key, string placeholder) => string.IsNullOrWhiteSpace(this[key]) ? placeholder : this[key];

    public string MapEmbedUrl(int zoom = 16) => $"https://www.google.com/maps?q={Uri.EscapeDataString(this["office.mapQuery"])}&z={zoom}&output=embed";

    public string DirectionsUrl => $"https://www.google.com/maps/dir/?api=1&destination={Uri.EscapeDataString(this["office.mapQuery"])}";

    public string? HotlineHref => string.IsNullOrWhiteSpace(this["contact.hotline"]) ? null : "tel:" + new string(this["contact.hotline"].Where(c => char.IsDigit(c) || c == '+').ToArray());

    public string? ZaloHref
    {
        get
        {
            var z = this["contact.zalo"];
            if (string.IsNullOrWhiteSpace(z)) return null;
            if (z.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return z;
            var digits = new string(z.Where(char.IsDigit).ToArray());
            return digits.Length >= 9 ? "https://zalo.me/" + digits : null;
        }
    }

    public bool AllowIndexing => string.Equals(this["seo.indexing"], "yes", StringComparison.OrdinalIgnoreCase);
}
