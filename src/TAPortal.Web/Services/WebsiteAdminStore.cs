using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using TAPortal.Shared;
using TAPortal.Web.Security;
using TAWebsite.Core.Content;
using TAWebsite.Core.Data;

namespace TAPortal.Web.Services;

public enum WebsiteOutcome { Saved, NotFound, Forbidden, Conflict, Duplicate, Invalid, AuditUnavailable }

public sealed record WebsiteResult(WebsiteOutcome Outcome, Guid? Id = null);

public sealed record WebsitePageEditor(RenderedPage Page, PageRow? Row, IReadOnlyDictionary<string, SectionRow> Rows);

public sealed record WebsiteMediaItem(Guid Id, string FileName, string ContentType, int ByteSize, int? Width, int? Height, string? AltText, DateTime CreatedAt);

public sealed record WebsitePostListItem(Guid Id, string Slug, string Title, string? CategoryName, byte Status, DateTime? PublishedAt, bool IsFeatured,
    int ViewCount, DateTime UpdatedAt, Guid? CoverMediaId);

public sealed record WebsitePostEdit(Guid Id, string Slug, string Title, string? Summary, string? BodyHtml, Guid? CoverMediaId, Guid? CategoryId,
    string? AuthorName, string? Tags, byte Status, DateTime? PublishedAt, bool IsFeatured, string? SeoTitle, string? SeoDescription, int ViewCount,
    DateTime CreatedAt, DateTime UpdatedAt, byte[] Stamp);

public sealed record WebsiteLeadItem(Guid Id, DateTime CreatedAt, string FullName, string Phone, string? Email, string? CompanyName, string? Services,
    string? CompanyType, string? Scale, string? ContactTime, string? Message, string? SourcePage, byte Status, string? Note, DateTime? UpdatedAt, byte[] Stamp);

// Dashboard block "Yêu cầu báo giá từ website": counts by Vietnam day and the latest requests.
public sealed record WebsiteLeadSummary(int New, int Today, int Last7Days, int Total, IReadOnlyList<WebsiteLeadItem> Recent);

public sealed record WebsiteOverview(int PostsPublished, int PostsDraft, int Media, int LeadsNew, int LeadsTotal, int SectionsEdited, bool TablesReady);

public sealed class WebsitePostInput
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? Summary { get; set; }
    public string? BodyHtml { get; set; }
    public Guid? CoverMediaId { get; set; }
    public Guid? CategoryId { get; set; }
    public string? AuthorName { get; set; }
    public string? Tags { get; set; }
    public bool Publish { get; set; }
    public DateTime? PublishedAtLocal { get; set; } // Vietnam time (UTC+7) typed by the editor
    public bool IsFeatured { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? Stamp { get; set; }

    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    // Trims, derives the slug from the title when empty and sanitizes the body. Returns the validation errors (field, message).
    public List<(string Field, string Message)> Normalize()
    {
        var errors = new List<(string, string)>();
        Title = Title?.Trim();
        Summary = Summary?.Trim();
        AuthorName = AuthorName?.Trim();
        Tags = string.Join(", ", (Tags ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        SeoTitle = SeoTitle?.Trim();
        SeoDescription = SeoDescription?.Trim();
        Slug = string.IsNullOrWhiteSpace(Slug) ? TAWebsite.Core.Content.Slug.From(Title) : TAWebsite.Core.Content.Slug.From(Slug);
        BodyHtml = PostHtml.Sanitize(BodyHtml);
        if (string.IsNullOrWhiteSpace(Title)) errors.Add(("Title", "Nhập tiêu đề bài viết."));
        else if (Title.Length > 300) errors.Add(("Title", "Tiêu đề tối đa 300 ký tự."));
        if (!TAWebsite.Core.Content.Slug.IsValid(Slug)) errors.Add(("Slug", "Đường dẫn chỉ gồm chữ thường không dấu, số và dấu gạch ngang."));
        if (Summary?.Length > 1000) errors.Add(("Summary", "Tóm tắt tối đa 1000 ký tự."));
        if (AuthorName?.Length > 150) errors.Add(("AuthorName", "Tên tác giả tối đa 150 ký tự."));
        if (Tags.Length > 500) errors.Add(("Tags", "Thẻ tối đa 500 ký tự."));
        if (SeoTitle?.Length > 200) errors.Add(("SeoTitle", "Tiêu đề SEO tối đa 200 ký tự."));
        if (SeoDescription?.Length > 400) errors.Add(("SeoDescription", "Mô tả SEO tối đa 400 ký tự."));
        if (Publish && string.IsNullOrWhiteSpace(PostHtml.PlainText(BodyHtml))) errors.Add(("BodyHtml", "Bài viết cần có nội dung trước khi xuất bản."));
        return errors;
    }

    public DateTime? PublishedAtUtc(DateTime nowUtc) =>
        !Publish ? (PublishedAtLocal is DateTime d ? d - VietnamOffset : null) : PublishedAtLocal is DateTime local ? local - VietnamOffset : nowUtc;
}

public interface IWebsiteAdminStore
{
    Task<WebsiteOverview> OverviewAsync();
    Task<IReadOnlyList<RenderedPage>> PagesAsync();
    Task<WebsitePageEditor> PageAsync(string pageKey);
    Task<WebsiteResult> SaveSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey, JsonObject content, string? stamp);
    Task<WebsiteResult> ResetSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey);
    Task<WebsiteResult> SaveLayoutAsync(Guid actor, string? ip, string pageKey, IReadOnlyList<(string Key, int Order, bool Visible)> layout);
    Task<WebsiteResult> SavePageSeoAsync(Guid actor, string? ip, string pageKey, string? title, string? description, bool published);

    Task<SiteSettings> SettingsAsync();
    Task<WebsiteResult> SaveSettingsAsync(Guid actor, string? ip, IReadOnlyDictionary<string, string?> values);

    Task<IReadOnlyList<WebsiteMediaItem>> MediaAsync(string? search, int take);
    Task<MediaFile?> MediaFileAsync(Guid id);
    Task<WebsiteResult> UploadMediaAsync(Guid actor, string? ip, string fileName, string contentType, byte[] data, int? width, int? height, string? alt);
    Task<WebsiteResult> UpdateMediaAltAsync(Guid actor, string? ip, Guid id, string? alt);
    Task<WebsiteResult> DeleteMediaAsync(Guid actor, string? ip, Guid id);

    Task<IReadOnlyList<PostCategory>> CategoriesAsync();
    Task<WebsiteResult> SaveCategoryAsync(Guid actor, string? ip, Guid? id, string name, string slug, int sortOrder);
    Task<WebsiteResult> DeleteCategoryAsync(Guid actor, string? ip, Guid id);

    Task<IReadOnlyList<WebsitePostListItem>> PostsAsync(string? status, Guid? category, string? search);
    Task<WebsitePostEdit?> PostAsync(Guid id);
    Task<WebsiteResult> SavePostAsync(Guid actor, string? ip, Guid? id, WebsitePostInput input, DateTime nowUtc);
    Task<WebsiteResult> DeletePostAsync(Guid actor, string? ip, Guid id);

    Task<IReadOnlyList<WebsiteLeadItem>> LeadsAsync(byte? status, string? search);
    Task<IReadOnlyList<WebsiteLeadItem>> LeadsForExportAsync(byte? status, string? search);
    Task<WebsiteLeadSummary?> LeadSummaryAsync(DateTime nowUtc);
    Task<WebsiteResult> AuditLeadsExportAsync(Guid actor, string? ip, byte? status, string? search);
    Task<WebsiteLeadItem?> LeadAsync(Guid id);
    Task<WebsiteResult> UpdateLeadAsync(Guid actor, string? ip, Guid id, byte status, string? note, string? stamp);
}

public static class WebsiteAdminSql
{
    public const string Overview = """
SELECT
 (SELECT COUNT(1) FROM dbo.WebsitePosts WHERE IsDeleted=0 AND Status=1),
 (SELECT COUNT(1) FROM dbo.WebsitePosts WHERE IsDeleted=0 AND Status=0),
 (SELECT COUNT(1) FROM dbo.WebsiteMedia WHERE IsDeleted=0),
 (SELECT COUNT(1) FROM dbo.WebsiteLeads WHERE Status=0),
 (SELECT COUNT(1) FROM dbo.WebsiteLeads),
 (SELECT COUNT(1) FROM dbo.WebsiteSections WHERE ContentJson IS NOT NULL);
""";

    public const string AllSections = "SELECT PageKey, SectionKey, ContentJson, SortOrder, IsVisible, UpdatedAt, RowVersion FROM dbo.WebsiteSections;";
    public const string AllPages = "SELECT PageKey, SeoTitle, SeoDescription, IsPublished, UpdatedAt, RowVersion FROM dbo.WebsitePages;";

    public const string SectionLock = "SELECT ContentJson, RowVersion FROM dbo.WebsiteSections WITH (UPDLOCK, HOLDLOCK) WHERE PageKey=@page AND SectionKey=@section;";

    public const string UpsertSectionContent = """
IF EXISTS (SELECT 1 FROM dbo.WebsiteSections WHERE PageKey=@page AND SectionKey=@section)
 UPDATE dbo.WebsiteSections SET ContentJson=@json, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE PageKey=@page AND SectionKey=@section;
ELSE
 INSERT dbo.WebsiteSections(PageKey, SectionKey, ContentJson, SortOrder, IsVisible, UpdatedBy) VALUES(@page, @section, @json, @order, 1, @by);
""";

    public const string UpsertSectionLayout = """
IF EXISTS (SELECT 1 FROM dbo.WebsiteSections WHERE PageKey=@page AND SectionKey=@section)
 UPDATE dbo.WebsiteSections SET SortOrder=@order, IsVisible=@visible, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE PageKey=@page AND SectionKey=@section;
ELSE
 INSERT dbo.WebsiteSections(PageKey, SectionKey, ContentJson, SortOrder, IsVisible, UpdatedBy) VALUES(@page, @section, NULL, @order, @visible, @by);
""";

    public const string ResetSection = "UPDATE dbo.WebsiteSections SET ContentJson=NULL, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE PageKey=@page AND SectionKey=@section;";

    public const string UpsertPage = """
IF EXISTS (SELECT 1 FROM dbo.WebsitePages WHERE PageKey=@page)
 UPDATE dbo.WebsitePages SET SeoTitle=@title, SeoDescription=@description, IsPublished=@published, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE PageKey=@page;
ELSE
 INSERT dbo.WebsitePages(PageKey, SeoTitle, SeoDescription, IsPublished, UpdatedBy) VALUES(@page, @title, @description, @published, @by);
""";

    public const string UpsertSetting = """
IF EXISTS (SELECT 1 FROM dbo.WebsiteSettings WHERE SettingKey=@key)
 UPDATE dbo.WebsiteSettings SET Value=@value, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE SettingKey=@key AND ISNULL(Value, N'') <> ISNULL(@value, N'');
ELSE
 INSERT dbo.WebsiteSettings(SettingKey, Value, UpdatedBy) VALUES(@key, @value, @by);
""";

    public const string MediaList = """
SELECT TOP (@take) Id, FileName, ContentType, ByteSize, Width, Height, AltText, CreatedAt FROM dbo.WebsiteMedia
WHERE IsDeleted=0 AND (@q IS NULL OR FileName LIKE @q OR AltText LIKE @q) ORDER BY CreatedAt DESC;
""";

    public const string MediaInsert = """
INSERT dbo.WebsiteMedia(Id, FileName, ContentType, ByteSize, Width, Height, AltText, Data, Sha256, CreatedBy)
VALUES(@id, @name, @type, @size, @width, @height, @alt, @data, @sha, @by);
""";

    public const string MediaBySha = "SELECT TOP 1 Id FROM dbo.WebsiteMedia WHERE Sha256=@sha AND IsDeleted=0;";

    public const string Categories = """
SELECT c.Id, c.Slug, c.Name, c.SortOrder, (SELECT COUNT(1) FROM dbo.WebsitePosts p WHERE p.CategoryId=c.Id AND p.IsDeleted=0)
FROM dbo.WebsitePostCategories c WHERE c.IsDeleted=0 ORDER BY c.SortOrder, c.Name;
""";

    public const string PostList = """
SELECT p.Id, p.Slug, p.Title, c.Name, p.Status, p.PublishedAt, p.IsFeatured, p.ViewCount, p.UpdatedAt, p.CoverMediaId
FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId
WHERE p.IsDeleted=0 AND (@category IS NULL OR p.CategoryId=@category)
 AND (@status IS NULL OR (@status='draft' AND p.Status=0) OR (@status='published' AND p.Status=1 AND p.PublishedAt<=SYSUTCDATETIME())
      OR (@status='scheduled' AND p.Status=1 AND p.PublishedAt>SYSUTCDATETIME()))
 AND (@q IS NULL OR p.Title LIKE @q OR p.Slug LIKE @q OR p.Tags LIKE @q)
ORDER BY p.UpdatedAt DESC;
""";

    public const string PostDetail = """
SELECT Id, Slug, Title, Summary, BodyHtml, CoverMediaId, CategoryId, AuthorName, Tags, Status, PublishedAt, IsFeatured, SeoTitle, SeoDescription,
 ViewCount, CreatedAt, UpdatedAt, RowVersion FROM dbo.WebsitePosts WHERE Id=@id AND IsDeleted=0;
""";

    public const string PostInsert = """
INSERT dbo.WebsitePosts(Id, Slug, Title, Summary, BodyHtml, CoverMediaId, CategoryId, AuthorName, Tags, Status, PublishedAt, IsFeatured, SeoTitle, SeoDescription, CreatedBy, UpdatedBy)
VALUES(@id, @slug, @title, @summary, @body, @cover, @category, @author, @tags, @status, @published, @featured, @seoTitle, @seoDescription, @by, @by);
""";

    public const string PostUpdate = """
UPDATE dbo.WebsitePosts SET Slug=@slug, Title=@title, Summary=@summary, BodyHtml=@body, CoverMediaId=@cover, CategoryId=@category, AuthorName=@author,
 Tags=@tags, Status=@status, PublishedAt=@published, IsFeatured=@featured, SeoTitle=@seoTitle, SeoDescription=@seoDescription,
 UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0 AND RowVersion=@stamp;
""";

    public const string LeadColumns = "Id, CreatedAt, FullName, Phone, Email, CompanyName, Services, CompanyType, Scale, ContactTime, Message, SourcePage, Status, Note, UpdatedAt, RowVersion";

    public const int ExportLimit = 20_000;

    public const string Leads = "SELECT TOP (@take) " + LeadColumns + """
 FROM dbo.WebsiteLeads WHERE (@status IS NULL OR Status=@status)
 AND (@q IS NULL OR FullName LIKE @q OR Phone LIKE @q OR Email LIKE @q OR CompanyName LIKE @q) ORDER BY CreatedAt DESC;
""";

    // @today/@week: start of the Vietnam day (and 6 days earlier) expressed in UTC, like CreatedAt.
    public const string LeadSummary = """
SELECT SUM(CASE WHEN Status=0 THEN 1 ELSE 0 END), SUM(CASE WHEN CreatedAt>=@today THEN 1 ELSE 0 END),
 SUM(CASE WHEN CreatedAt>=@week THEN 1 ELSE 0 END), COUNT(*) FROM dbo.WebsiteLeads;
""";

    public const string LeadRecent = "SELECT TOP 6 " + LeadColumns + " FROM dbo.WebsiteLeads ORDER BY CASE WHEN Status=0 THEN 0 ELSE 1 END, CreatedAt DESC;";
}

public sealed class SqlWebsiteAdminStore(IConfiguration configuration, AdminAuditOptions audit) : IWebsiteAdminStore
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    // ---- overview / pages ---------------------------------------------------------------------------------------
    public async Task<WebsiteOverview> OverviewAsync()
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        if (Convert.ToInt32(await Scalar(cn, null, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebsiteLeads', N'U') IS NULL THEN 0 ELSE 1 END")) == 0)
            return new(0, 0, 0, 0, 0, 0, false);
        await using var cmd = new SqlCommand(WebsiteAdminSql.Overview, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), true);
    }

    public async Task<IReadOnlyList<RenderedPage>> PagesAsync()
    {
        var (pages, sections) = await LoadRowsAsync();
        return SiteMap.Pages.Select(p => PageComposer.Compose(p, pages.GetValueOrDefault(p.Key), sections)).ToList();
    }

    public async Task<WebsitePageEditor> PageAsync(string pageKey)
    {
        var def = SiteMap.Get(pageKey);
        var (pages, sections) = await LoadRowsAsync();
        var rows = sections.Where(s => s.PageKey == pageKey).ToDictionary(s => s.SectionKey, StringComparer.Ordinal);
        return new(PageComposer.Compose(def, pages.GetValueOrDefault(pageKey), sections), pages.GetValueOrDefault(pageKey), rows);
    }

    private async Task<(Dictionary<string, PageRow> Pages, List<SectionRow> Sections)> LoadRowsAsync()
    {
        var pages = new Dictionary<string, PageRow>(StringComparer.Ordinal);
        var sections = new List<SectionRow>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using (var cmd = new SqlCommand(WebsiteAdminSql.AllSections, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                sections.Add(new(r.GetString(0), r.GetString(1), Str(r, 2), r.GetInt32(3), r.GetBoolean(4), r.GetDateTime(5), (byte[])r.GetValue(6)));
        await using (var cmd = new SqlCommand(WebsiteAdminSql.AllPages, cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                pages[r.GetString(0)] = new(r.GetString(0), Str(r, 1), Str(r, 2), r.GetBoolean(3), r.GetDateTime(4), (byte[])r.GetValue(5));
        return (pages, sections);
    }

    public Task<WebsiteResult> SaveSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey, JsonObject content, string? stamp) =>
        Write(actor, AdminPermissions.WebsiteContentEdit, async (cn, tx) =>
        {
            var section = SiteMap.Find(pageKey)?.Section(sectionKey);
            if (section is null) return new(WebsiteOutcome.NotFound);
            string? oldJson = null;
            byte[]? current = null;
            await using (var cmd = new SqlCommand(WebsiteAdminSql.SectionLock, cn, tx))
            {
                cmd.Parameters.AddWithValue("@page", pageKey);
                cmd.Parameters.AddWithValue("@section", sectionKey);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) { oldJson = Str(r, 0); current = (byte[])r.GetValue(1); }
            }
            // Optimistic concurrency: the form carries the RowVersion it was opened with ("" when the row did not exist).
            var expected = string.IsNullOrEmpty(stamp) ? null : Convert.FromBase64String(stamp);
            if (current is not null && (expected is null || !current.AsSpan().SequenceEqual(expected))) return new(WebsiteOutcome.Conflict);
            var json = content.ToJsonString(SectionContent.JsonOptions);
            await Exec(cn, tx, WebsiteAdminSql.UpsertSectionContent, ("@page", pageKey), ("@section", sectionKey), ("@json", json),
                ("@order", section.Order), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_SECTION_UPDATE", "WebsiteSections", $"{pageKey}/{sectionKey}", oldJson ?? "", json);
            return new(WebsiteOutcome.Saved);
        });

    public Task<WebsiteResult> ResetSectionAsync(Guid actor, string? ip, string pageKey, string sectionKey) =>
        Write(actor, AdminPermissions.WebsiteContentEdit, async (cn, tx) =>
        {
            if (SiteMap.Find(pageKey)?.Section(sectionKey) is null) return new(WebsiteOutcome.NotFound);
            await Exec(cn, tx, WebsiteAdminSql.ResetSection, ("@page", pageKey), ("@section", sectionKey), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_SECTION_RESET", "WebsiteSections", $"{pageKey}/{sectionKey}", "", "default");
            return new(WebsiteOutcome.Saved);
        });

    public Task<WebsiteResult> SaveLayoutAsync(Guid actor, string? ip, string pageKey, IReadOnlyList<(string Key, int Order, bool Visible)> layout) =>
        Write(actor, AdminPermissions.WebsiteContentEdit, async (cn, tx) =>
        {
            var page = SiteMap.Find(pageKey);
            if (page is null) return new(WebsiteOutcome.NotFound);
            if (layout.Any(l => page.Section(l.Key) is null)) return new(WebsiteOutcome.Invalid);
            foreach (var (key, order, visible) in layout)
                await Exec(cn, tx, WebsiteAdminSql.UpsertSectionLayout, ("@page", pageKey), ("@section", key), ("@order", order), ("@visible", visible), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_PAGE_LAYOUT", "WebsiteSections", pageKey, "",
                AuditSql.Json(layout.Select(l => (l.Key, (object?)$"{l.Order}:{(l.Visible ? "on" : "off")}")).ToArray()));
            return new(WebsiteOutcome.Saved);
        });

    public Task<WebsiteResult> SavePageSeoAsync(Guid actor, string? ip, string pageKey, string? title, string? description, bool published) =>
        Write(actor, AdminPermissions.WebsiteContentEdit, async (cn, tx) =>
        {
            if (SiteMap.Find(pageKey) is null) return new(WebsiteOutcome.NotFound);
            await Exec(cn, tx, WebsiteAdminSql.UpsertPage, ("@page", pageKey), ("@title", Db(title)), ("@description", Db(description)),
                ("@published", published), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_PAGE_SEO", "WebsitePages", pageKey, "",
                AuditSql.Json(("title", title), ("description", description), ("published", published)));
            return new(WebsiteOutcome.Saved);
        });

    // ---- settings -----------------------------------------------------------------------------------------------
    public async Task<SiteSettings> SettingsAsync()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteSql.Settings, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) values[r.GetString(0)] = Str(r, 1);
        return new SiteSettings(values);
    }

    public Task<WebsiteResult> SaveSettingsAsync(Guid actor, string? ip, IReadOnlyDictionary<string, string?> values) =>
        Write(actor, AdminPermissions.WebsiteSettingsEdit, async (cn, tx) =>
        {
            if (values.Keys.Any(k => SiteSettingDefs.Find(k) is null)) return new(WebsiteOutcome.Invalid);
            foreach (var (key, value) in values)
                await Exec(cn, tx, WebsiteAdminSql.UpsertSetting, ("@key", key), ("@value", Db(value)), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_SETTINGS_UPDATE", "WebsiteSettings", "settings", "",
                AuditSql.Json(values.Select(v => (v.Key, (object?)v.Value)).ToArray()));
            return new(WebsiteOutcome.Saved);
        });

    // ---- media --------------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<WebsiteMediaItem>> MediaAsync(string? search, int take)
    {
        var list = new List<WebsiteMediaItem>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteAdminSql.MediaList, cn);
        cmd.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 500));
        cmd.Parameters.AddWithValue("@q", Like(search));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetInt32(5), Str(r, 6), r.GetDateTime(7)));
        return list;
    }

    public async Task<MediaFile?> MediaFileAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteSql.Media, cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new MediaFile(r.GetGuid(0), r.GetString(1), (byte[])r.GetValue(2), r.GetString(3)) : null;
    }

    public Task<WebsiteResult> UploadMediaAsync(Guid actor, string? ip, string fileName, string contentType, byte[] data, int? width, int? height, string? alt) =>
        Write(actor, AdminPermissions.WebsiteMediaEdit, async (cn, tx) =>
        {
            var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            // The same file uploaded twice is stored once.
            if (await Scalar(cn, tx, WebsiteAdminSql.MediaBySha, ("@sha", sha)) is Guid existing) return new(WebsiteOutcome.Saved, existing);
            var id = Guid.NewGuid();
            await Exec(cn, tx, WebsiteAdminSql.MediaInsert, ("@id", id), ("@name", Truncate(fileName, 260)!), ("@type", contentType), ("@size", data.Length),
                ("@width", (object?)width ?? DBNull.Value), ("@height", (object?)height ?? DBNull.Value), ("@alt", Db(Truncate(alt, 300))),
                ("@data", data), ("@sha", sha), ("@by", actor));
            await Audit(cn, tx, actor, ip, "WEBSITE_MEDIA_UPLOAD", "WebsiteMedia", id.ToString(), "",
                AuditSql.Json(("file", fileName), ("type", contentType), ("bytes", data.Length)));
            return new(WebsiteOutcome.Saved, id);
        });

    public Task<WebsiteResult> UpdateMediaAltAsync(Guid actor, string? ip, Guid id, string? alt) =>
        Write(actor, AdminPermissions.WebsiteMediaEdit, async (cn, tx) =>
        {
            var n = await Exec(cn, tx, "UPDATE dbo.WebsiteMedia SET AltText=@alt WHERE Id=@id AND IsDeleted=0;", ("@id", id), ("@alt", Db(Truncate(alt, 300))));
            if (n == 0) return new(WebsiteOutcome.NotFound);
            await Audit(cn, tx, actor, ip, "WEBSITE_MEDIA_ALT", "WebsiteMedia", id.ToString(), "", AuditSql.Json(("alt", alt)));
            return new(WebsiteOutcome.Saved, id);
        });

    // Soft delete. Content that still points to the image falls back to no image on the site.
    public Task<WebsiteResult> DeleteMediaAsync(Guid actor, string? ip, Guid id) =>
        Write(actor, AdminPermissions.WebsiteMediaEdit, async (cn, tx) =>
        {
            var n = await Exec(cn, tx, "UPDATE dbo.WebsiteMedia SET IsDeleted=1 WHERE Id=@id AND IsDeleted=0;", ("@id", id));
            if (n == 0) return new(WebsiteOutcome.NotFound);
            await Exec(cn, tx, "UPDATE dbo.WebsitePosts SET CoverMediaId=NULL WHERE CoverMediaId=@id;", ("@id", id));
            await Audit(cn, tx, actor, ip, "WEBSITE_MEDIA_DELETE", "WebsiteMedia", id.ToString(), "", "deleted");
            return new(WebsiteOutcome.Saved, id);
        });

    // ---- categories ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<PostCategory>> CategoriesAsync()
    {
        var list = new List<PostCategory>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteAdminSql.Categories, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
        return list;
    }

    public Task<WebsiteResult> SaveCategoryAsync(Guid actor, string? ip, Guid? id, string name, string slug, int sortOrder) =>
        Write(actor, AdminPermissions.WebsitePostsEdit, async (cn, tx) =>
        {
            name = name.Trim();
            slug = Slug.From(string.IsNullOrWhiteSpace(slug) ? name : slug, 120);
            if (name.Length is 0 or > 150 || !Slug.IsValid(slug)) return new(WebsiteOutcome.Invalid);
            var target = id ?? Guid.NewGuid();
            var n = id is null
                ? await Exec(cn, tx, "INSERT dbo.WebsitePostCategories(Id, Slug, Name, SortOrder, UpdatedBy) VALUES(@id, @slug, @name, @order, @by);",
                    ("@id", target), ("@slug", slug), ("@name", name), ("@order", sortOrder), ("@by", actor))
                : await Exec(cn, tx, "UPDATE dbo.WebsitePostCategories SET Slug=@slug, Name=@name, SortOrder=@order, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;",
                    ("@id", target), ("@slug", slug), ("@name", name), ("@order", sortOrder), ("@by", actor));
            if (n == 0) return new(WebsiteOutcome.NotFound);
            await Audit(cn, tx, actor, ip, id is null ? "WEBSITE_CATEGORY_CREATE" : "WEBSITE_CATEGORY_UPDATE", "WebsitePostCategories", target.ToString(), "",
                AuditSql.Json(("name", name), ("slug", slug), ("order", sortOrder)));
            return new(WebsiteOutcome.Saved, target);
        });

    public Task<WebsiteResult> DeleteCategoryAsync(Guid actor, string? ip, Guid id) =>
        Write(actor, AdminPermissions.WebsitePostsEdit, async (cn, tx) =>
        {
            var n = await Exec(cn, tx, "UPDATE dbo.WebsitePostCategories SET IsDeleted=1, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE Id=@id AND IsDeleted=0;", ("@id", id), ("@by", actor));
            if (n == 0) return new(WebsiteOutcome.NotFound);
            await Exec(cn, tx, "UPDATE dbo.WebsitePosts SET CategoryId=NULL WHERE CategoryId=@id;", ("@id", id));
            await Audit(cn, tx, actor, ip, "WEBSITE_CATEGORY_DELETE", "WebsitePostCategories", id.ToString(), "", "deleted");
            return new(WebsiteOutcome.Saved, id);
        });

    // ---- posts --------------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<WebsitePostListItem>> PostsAsync(string? status, Guid? category, string? search)
    {
        var list = new List<WebsitePostListItem>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteAdminSql.PostList, cn);
        cmd.Parameters.AddWithValue("@status", status is "draft" or "published" or "scheduled" ? status : DBNull.Value);
        cmd.Parameters.AddWithValue("@category", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@q", Like(search));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), Str(r, 3), r.GetByte(4), r.IsDBNull(5) ? null : r.GetDateTime(5), r.GetBoolean(6),
                r.GetInt32(7), r.GetDateTime(8), r.IsDBNull(9) ? null : r.GetGuid(9)));
        return list;
    }

    public async Task<WebsitePostEdit?> PostAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteAdminSql.PostDetail, cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new(r.GetGuid(0), r.GetString(1), r.GetString(2), Str(r, 3), Str(r, 4), r.IsDBNull(5) ? null : r.GetGuid(5), r.IsDBNull(6) ? null : r.GetGuid(6),
            Str(r, 7), Str(r, 8), r.GetByte(9), r.IsDBNull(10) ? null : r.GetDateTime(10), r.GetBoolean(11), Str(r, 12), Str(r, 13), r.GetInt32(14),
            r.GetDateTime(15), r.GetDateTime(16), (byte[])r.GetValue(17));
    }

    public Task<WebsiteResult> SavePostAsync(Guid actor, string? ip, Guid? id, WebsitePostInput input, DateTime nowUtc) =>
        Write(actor, AdminPermissions.WebsitePostsEdit, async (cn, tx) =>
        {
            var target = id ?? Guid.NewGuid();
            (string, object)[] values =
            [
                ("@id", target), ("@slug", input.Slug!), ("@title", input.Title!), ("@summary", Db(input.Summary)), ("@body", Db(input.BodyHtml)),
                ("@cover", (object?)input.CoverMediaId ?? DBNull.Value), ("@category", (object?)input.CategoryId ?? DBNull.Value),
                ("@author", Db(input.AuthorName)), ("@tags", Db(input.Tags)), ("@status", (byte)(input.Publish ? 1 : 0)),
                ("@published", (object?)input.PublishedAtUtc(nowUtc) ?? DBNull.Value), ("@featured", input.IsFeatured),
                ("@seoTitle", Db(input.SeoTitle)), ("@seoDescription", Db(input.SeoDescription)), ("@by", actor)
            ];
            if (id is null) await Exec(cn, tx, WebsiteAdminSql.PostInsert, values);
            else
            {
                if (string.IsNullOrEmpty(input.Stamp)) return new(WebsiteOutcome.Conflict);
                var n = await Exec(cn, tx, WebsiteAdminSql.PostUpdate, [.. values, ("@stamp", Convert.FromBase64String(input.Stamp))]);
                if (n == 0)
                    return Convert.ToInt32(await Scalar(cn, tx, "SELECT COUNT(1) FROM dbo.WebsitePosts WHERE Id=@id AND IsDeleted=0", ("@id", target))) == 0
                        ? new(WebsiteOutcome.NotFound) : new(WebsiteOutcome.Conflict);
            }
            await Audit(cn, tx, actor, ip, id is null ? "WEBSITE_POST_CREATE" : "WEBSITE_POST_UPDATE", "WebsitePosts", target.ToString(), "",
                AuditSql.Json(("title", input.Title), ("slug", input.Slug), ("status", input.Publish ? "published" : "draft")));
            return new(WebsiteOutcome.Saved, target);
        });

    public Task<WebsiteResult> DeletePostAsync(Guid actor, string? ip, Guid id) =>
        Write(actor, AdminPermissions.WebsitePostsEdit, async (cn, tx) =>
        {
            // The slug gets a suffix so a new article can reuse it.
            var n = await Exec(cn, tx, """
UPDATE dbo.WebsitePosts SET IsDeleted=1, Slug=LEFT(Slug, 180) + '-xoa-' + LOWER(LEFT(CONVERT(varchar(36), NEWID()), 8)), UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by
WHERE Id=@id AND IsDeleted=0;
""", ("@id", id), ("@by", actor));
            if (n == 0) return new(WebsiteOutcome.NotFound);
            await Audit(cn, tx, actor, ip, "WEBSITE_POST_DELETE", "WebsitePosts", id.ToString(), "", "deleted");
            return new(WebsiteOutcome.Saved, id);
        });

    // ---- leads --------------------------------------------------------------------------------------------------
    public Task<IReadOnlyList<WebsiteLeadItem>> LeadsAsync(byte? status, string? search) => LeadsAsync(status, search, 500);

    public Task<IReadOnlyList<WebsiteLeadItem>> LeadsForExportAsync(byte? status, string? search) => LeadsAsync(status, search, WebsiteAdminSql.ExportLimit);

    private async Task<IReadOnlyList<WebsiteLeadItem>> LeadsAsync(byte? status, string? search, int take)
    {
        var list = new List<WebsiteLeadItem>();
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(WebsiteAdminSql.Leads, cn);
        cmd.Parameters.AddWithValue("@take", take);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@q", Like(search));
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(ReadLead(r));
        return list;
    }

    public async Task<WebsiteLeadSummary?> LeadSummaryAsync(DateTime nowUtc)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        if (Convert.ToInt32(await Scalar(cn, null, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebsiteLeads', N'U') IS NULL THEN 0 ELSE 1 END")) == 0) return null;
        var today = (nowUtc + WebsitePostInput.VietnamOffset).Date - WebsitePostInput.VietnamOffset;
        int n, d, w, t;
        await using (var cmd = new SqlCommand(WebsiteAdminSql.LeadSummary, cn))
        {
            cmd.Parameters.AddWithValue("@today", today);
            cmd.Parameters.AddWithValue("@week", today.AddDays(-6));
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            int I(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
            (n, d, w, t) = (I(0), I(1), I(2), I(3));
        }
        var recent = new List<WebsiteLeadItem>();
        await using (var cmd = new SqlCommand(WebsiteAdminSql.LeadRecent, cn))
        {
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) recent.Add(ReadLead(r));
        }
        return new(n, d, w, t, recent);
    }

    // Exporting customer contact data is audited (who, when, which filter) before the file is built.
    public Task<WebsiteResult> AuditLeadsExportAsync(Guid actor, string? ip, byte? status, string? search) =>
        Write(actor, AdminPermissions.WebsiteLeadsView, async (cn, tx) =>
        {
            await Audit(cn, tx, actor, ip, "WEBSITE_LEADS_EXPORT", "WebsiteLeads", "export", "",
                AuditSql.Json(("status", status is byte b ? LeadStatus.Name(b) : "all"), ("search", Truncate(search?.Trim(), 100) ?? "")));
            return new(WebsiteOutcome.Saved);
        });

    public async Task<WebsiteLeadItem?> LeadAsync(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT " + WebsiteAdminSql.LeadColumns + " FROM dbo.WebsiteLeads WHERE Id=@id;", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadLead(r) : null;
    }

    private static WebsiteLeadItem ReadLead(SqlDataReader r) => new(r.GetGuid(0), r.GetDateTime(1), r.GetString(2), r.GetString(3), Str(r, 4), Str(r, 5),
        Str(r, 6), Str(r, 7), Str(r, 8), Str(r, 9), Str(r, 10), Str(r, 11), r.GetByte(12), Str(r, 13), r.IsDBNull(14) ? null : r.GetDateTime(14), (byte[])r.GetValue(15));

    public Task<WebsiteResult> UpdateLeadAsync(Guid actor, string? ip, Guid id, byte status, string? note, string? stamp) =>
        Write(actor, AdminPermissions.WebsiteLeadsEdit, async (cn, tx) =>
        {
            if (LeadStatus.All.All(s => s.Code != status) || note?.Length > 2000 || string.IsNullOrEmpty(stamp)) return new(WebsiteOutcome.Invalid);
            var n = await Exec(cn, tx, "UPDATE dbo.WebsiteLeads SET Status=@status, Note=@note, UpdatedAt=SYSUTCDATETIME(), UpdatedBy=@by WHERE Id=@id AND RowVersion=@stamp;",
                ("@id", id), ("@status", status), ("@note", Db(note)), ("@by", actor), ("@stamp", Convert.FromBase64String(stamp)));
            if (n == 0)
                return Convert.ToInt32(await Scalar(cn, tx, "SELECT COUNT(1) FROM dbo.WebsiteLeads WHERE Id=@id", ("@id", id))) == 0
                    ? new(WebsiteOutcome.NotFound) : new(WebsiteOutcome.Conflict);
            await Audit(cn, tx, actor, ip, "WEBSITE_LEAD_UPDATE", "WebsiteLeads", id.ToString(), "", AuditSql.Json(("status", LeadStatus.Name(status))));
            return new(WebsiteOutcome.Saved, id);
        });

    // ---- helpers ------------------------------------------------------------------------------------------------
    private async Task<WebsiteResult> Write(Guid actor, string permission, Func<SqlConnection, SqlTransaction, Task<WebsiteResult>> body)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            if (!await ActorPermissionSql.HasPermissionAsync(cn, tx, actor, permission)) return new(WebsiteOutcome.Forbidden);
            var result = await body(cn, tx);
            if (result.Outcome == WebsiteOutcome.Saved) await tx.CommitAsync();
            return result;
        }
        catch (AuditUnavailableException) { return new(WebsiteOutcome.AuditUnavailable); }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { return new(WebsiteOutcome.Duplicate); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { return new(WebsiteOutcome.Conflict); }
        catch (SqlException ex) when (ex.Number is 547) { return new(WebsiteOutcome.Invalid); }
        catch (FormatException) { return new(WebsiteOutcome.Conflict); } // a tampered RowVersion stamp
    }

    private async Task Audit(SqlConnection cn, SqlTransaction tx, Guid actor, string? ip, string action, string entity, string target, string oldValues, string newValues) =>
        await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, action, entity, target, oldValues, newValues, ip);

    private static async Task<int> Exec(SqlConnection cn, SqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object Like(string? search) =>
        string.IsNullOrWhiteSpace(search) ? DBNull.Value : "%" + search.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
