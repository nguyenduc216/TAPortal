using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TAWebsite.Core.Content;

namespace TAWebsite.Core.Data;

// What the public site reads (and the one thing it writes: contact requests and view counts).
public interface IWebsiteReader
{
    Task<RenderedPage> PageAsync(string pageKey);
    Task<SiteSettings> SettingsAsync();
    Task<IReadOnlyList<PostCategory>> CategoriesAsync();
    Task<PostPage> PostsAsync(PostQuery query);
    Task<IReadOnlyList<PostCard>> LatestAsync(int count);
    Task<IReadOnlyList<PostCard>> PopularAsync(int count);
    Task<PostDetail?> PostAsync(string slug);
    Task<IReadOnlyList<PostCard>> RelatedAsync(PostDetail post, int count);
    Task<(PostCard? Previous, PostCard? Next)> NeighboursAsync(PostDetail post);
    Task<MediaFile?> MediaAsync(Guid id);
    Task CountViewAsync(Guid postId);
    Task<LeadOutcome> AddLeadAsync(LeadInput lead, string? clientIp);
}

public static class WebsiteSql
{
    public const string Sections = "SELECT PageKey, SectionKey, ContentJson, SortOrder, IsVisible, UpdatedAt, RowVersion FROM dbo.WebsiteSections WHERE PageKey IN (@page, 'global');";
    public const string Page = "SELECT PageKey, SeoTitle, SeoDescription, IsPublished, UpdatedAt, RowVersion FROM dbo.WebsitePages WHERE PageKey=@page;";
    public const string Settings = "SELECT SettingKey, Value FROM dbo.WebsiteSettings;";

    public const string Visible = "p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME()";

    public const string CardColumns = """
p.Id, p.Slug, p.Title, p.Summary, p.CoverMediaId, c.Slug, c.Name, p.PublishedAt,
CASE WHEN p.BodyHtml IS NULL THEN 1 ELSE (LEN(p.BodyHtml) / 1400) + 1 END, p.IsFeatured, p.ViewCount
""";

    public const string Categories = """
SELECT c.Id, c.Slug, c.Name, c.SortOrder,
 (SELECT COUNT(1) FROM dbo.WebsitePosts p WHERE p.CategoryId=c.Id AND p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME())
FROM dbo.WebsitePostCategories c WHERE c.IsDeleted=0 ORDER BY c.SortOrder, c.Name;
""";

    public const string Filter = """
FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME()
 AND (@cat IS NULL OR c.Slug=@cat)
 AND (@q IS NULL OR p.Title COLLATE Latin1_General_CI_AI LIKE @q OR p.Summary COLLATE Latin1_General_CI_AI LIKE @q
      OR p.Tags COLLATE Latin1_General_CI_AI LIKE @q)
""";

    // SQL Server 2014: OFFSET/FETCH is available (2012+).
    public const string Posts = "SELECT " + CardColumns + " " + Filter + " ORDER BY p.PublishedAt DESC, p.Id OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;"
        + " SELECT COUNT(1) " + Filter + ";";

    public const string Latest = "SELECT TOP (@take) " + CardColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME() ORDER BY p.IsFeatured DESC, p.PublishedAt DESC;
""";

    public const string Popular = "SELECT TOP (@take) " + CardColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME() ORDER BY p.ViewCount DESC, p.PublishedAt DESC;
""";

    public const string Related = "SELECT TOP (@take) " + CardColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME() AND p.Id<>@id
ORDER BY CASE WHEN p.CategoryId=@category THEN 0 ELSE 1 END, p.PublishedAt DESC;
""";

    public const string Previous = "SELECT TOP 1 " + CardColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME() AND (p.PublishedAt<@at OR (p.PublishedAt=@at AND p.Id<@id))
ORDER BY p.PublishedAt DESC, p.Id DESC;
""";

    public const string Next = "SELECT TOP 1 " + CardColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
WHERE p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME() AND (p.PublishedAt>@at OR (p.PublishedAt=@at AND p.Id>@id))
ORDER BY p.PublishedAt, p.Id;
""";

    public const string DetailColumns = """
p.Id, p.Slug, p.Title, p.Summary, ISNULL(p.BodyHtml, N''), p.CoverMediaId, m.AltText, p.CategoryId, c.Slug, c.Name, p.AuthorName, p.Tags,
p.PublishedAt, p.UpdatedAt, p.SeoTitle, p.SeoDescription, p.ViewCount
""";

    public const string Detail = "SELECT " + DetailColumns + """
 FROM dbo.WebsitePosts p LEFT JOIN dbo.WebsitePostCategories c ON c.Id=p.CategoryId AND c.IsDeleted=0
 LEFT JOIN dbo.WebsiteMedia m ON m.Id=p.CoverMediaId
WHERE p.Slug=@slug AND p.Status=1 AND p.IsDeleted=0 AND p.PublishedAt<=SYSUTCDATETIME();
""";

    public const string Media = "SELECT Id, ContentType, Data, Sha256 FROM dbo.WebsiteMedia WHERE Id=@id AND IsDeleted=0;";

    public const string CountView = "UPDATE dbo.WebsitePosts SET ViewCount=ViewCount+1 WHERE Id=@id AND Status=1 AND IsDeleted=0;";

    public const string RecentLeads = "SELECT COUNT(1) FROM dbo.WebsiteLeads WHERE ClientIpHash=@ip AND CreatedAt>DATEADD(HOUR,-1,SYSUTCDATETIME());";

    public const string InsertLead = """
INSERT dbo.WebsiteLeads(FullName, Phone, Email, CompanyName, Services, CompanyType, Scale, ContactTime, Message, SourcePage, ClientIpHash)
VALUES(@name, @phone, @email, @company, @services, @type, @scale, @time, @message, @source, @ip);
""";

    public static PostCard Card(SqlDataReader r, int o = 0) => new(
        r.GetGuid(o), r.GetString(o + 1), r.GetString(o + 2), Str(r, o + 3), r.IsDBNull(o + 4) ? null : r.GetGuid(o + 4), Str(r, o + 5), Str(r, o + 6),
        r.IsDBNull(o + 7) ? null : r.GetDateTime(o + 7), Convert.ToInt32(r.GetValue(o + 8)), r.GetBoolean(o + 9), r.GetInt32(o + 10));

    public static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}

// Reads are cached for a short time (60 s): an edit in the Admin portal appears on the site within a minute. When the
// database or the website tables are unavailable (e.g. before migration 035) the site still renders its default content.
public sealed class SqlWebsiteReader(IConfiguration configuration, IMemoryCache cache, ILogger<SqlWebsiteReader> logger) : IWebsiteReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    public const int LeadsPerHourPerIp = 5;

    private string? ConnectionString => configuration["Database:ConnectionString"];

    private SqlConnection Connection() => new(ConnectionString);

    private bool Configured => !string.IsNullOrWhiteSpace(ConnectionString);

    public Task<RenderedPage> PageAsync(string pageKey) => Cached($"page:{pageKey}", async () =>
    {
        var def = SiteMap.Get(pageKey);
        var rows = new List<SectionRow>();
        PageRow? page = null;
        await Safe("WEBSITE_PAGE_READ", async cn =>
        {
            await using (var cmd = new SqlCommand(WebsiteSql.Sections, cn))
            {
                cmd.Parameters.AddWithValue("@page", pageKey);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    rows.Add(new(r.GetString(0), r.GetString(1), WebsiteSql.Str(r, 2), r.GetInt32(3), r.GetBoolean(4), r.GetDateTime(5)));
            }
            await using (var cmd = new SqlCommand(WebsiteSql.Page, cn))
            {
                cmd.Parameters.AddWithValue("@page", pageKey);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync()) page = new(r.GetString(0), WebsiteSql.Str(r, 1), WebsiteSql.Str(r, 2), r.GetBoolean(3), r.GetDateTime(4));
            }
        });
        return PageComposer.Compose(def, page, rows);
    });

    public Task<SiteSettings> SettingsAsync() => Cached("settings", async () =>
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        await Safe("WEBSITE_SETTINGS_READ", async cn =>
        {
            await using var cmd = new SqlCommand(WebsiteSql.Settings, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) values[r.GetString(0)] = WebsiteSql.Str(r, 1);
        });
        return new SiteSettings(values);
    });

    public Task<IReadOnlyList<PostCategory>> CategoriesAsync() => Cached<IReadOnlyList<PostCategory>>("categories", async () =>
    {
        var list = new List<PostCategory>();
        await Safe("WEBSITE_CATEGORIES_READ", async cn =>
        {
            await using var cmd = new SqlCommand(WebsiteSql.Categories, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4)));
        });
        return list;
    });

    public async Task<PostPage> PostsAsync(PostQuery query)
    {
        var page = Math.Max(1, query.Page);
        var items = new List<PostCard>();
        var total = 0;
        await Safe("WEBSITE_POSTS_READ", async cn =>
        {
            await using var cmd = new SqlCommand(WebsiteSql.Posts, cn);
            cmd.Parameters.AddWithValue("@cat", WebsiteSql.Db(query.CategorySlug));
            cmd.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(query.Search) ? DBNull.Value : "%" + Like(query.Search.Trim()) + "%");
            cmd.Parameters.AddWithValue("@skip", (page - 1) * query.PageSize);
            cmd.Parameters.AddWithValue("@take", query.PageSize);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) items.Add(WebsiteSql.Card(r));
            if (await r.NextResultAsync() && await r.ReadAsync()) total = r.GetInt32(0);
        });
        return new(items, total, page, query.PageSize);
    }

    public Task<IReadOnlyList<PostCard>> LatestAsync(int count) => Cached($"latest:{count}", () => Cards(WebsiteSql.Latest, ("@take", count)));

    public Task<IReadOnlyList<PostCard>> PopularAsync(int count) => Cached($"popular:{count}", () => Cards(WebsiteSql.Popular, ("@take", count)));

    public async Task<PostDetail?> PostAsync(string slug)
    {
        if (!Slug.IsValid(slug)) return null;
        PostDetail? post = null;
        await Safe("WEBSITE_POST_READ", async cn =>
        {
            await using var cmd = new SqlCommand(WebsiteSql.Detail, cn);
            cmd.Parameters.AddWithValue("@slug", slug);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) post = ReadDetail(r);
        });
        return post;
    }

    public static PostDetail ReadDetail(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), WebsiteSql.Str(r, 3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetGuid(5), WebsiteSql.Str(r, 6), r.IsDBNull(7) ? null : r.GetGuid(7), WebsiteSql.Str(r, 8), WebsiteSql.Str(r, 9),
        WebsiteSql.Str(r, 10), WebsiteSql.Str(r, 11), r.IsDBNull(12) ? null : r.GetDateTime(12), r.GetDateTime(13), WebsiteSql.Str(r, 14),
        WebsiteSql.Str(r, 15), r.GetInt32(16));

    public Task<IReadOnlyList<PostCard>> RelatedAsync(PostDetail post, int count) =>
        Cards(WebsiteSql.Related, ("@take", count), ("@id", post.Id), ("@category", (object?)post.CategoryId ?? DBNull.Value));

    public async Task<(PostCard? Previous, PostCard? Next)> NeighboursAsync(PostDetail post)
    {
        if (post.PublishedAt is not DateTime at) return (null, null);
        var prev = await Cards(WebsiteSql.Previous, ("@at", at), ("@id", post.Id));
        var next = await Cards(WebsiteSql.Next, ("@at", at), ("@id", post.Id));
        return (prev.FirstOrDefault(), next.FirstOrDefault());
    }

    public Task<MediaFile?> MediaAsync(Guid id) => Cached($"media:{id:N}", async () =>
    {
        MediaFile? file = null;
        await Safe("WEBSITE_MEDIA_READ", async cn =>
        {
            await using var cmd = new SqlCommand(WebsiteSql.Media, cn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync()) file = new(r.GetGuid(0), r.GetString(1), (byte[])r.GetValue(2), r.GetString(3));
        });
        return file;
    }, TimeSpan.FromMinutes(10));

    public Task CountViewAsync(Guid postId) => Safe("WEBSITE_VIEW_COUNT", async cn =>
    {
        await using var cmd = new SqlCommand(WebsiteSql.CountView, cn);
        cmd.Parameters.AddWithValue("@id", postId);
        await cmd.ExecuteNonQueryAsync();
    });

    public async Task<LeadOutcome> AddLeadAsync(LeadInput lead, string? clientIp)
    {
        if (!Configured) return LeadOutcome.Unavailable;
        var ipHash = clientIp is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ta-website:" + clientIp))).ToLowerInvariant();
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            if (ipHash is not null)
            {
                await using var count = new SqlCommand(WebsiteSql.RecentLeads, cn);
                count.Parameters.AddWithValue("@ip", ipHash);
                if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= LeadsPerHourPerIp) return LeadOutcome.TooMany;
            }
            await using var cmd = new SqlCommand(WebsiteSql.InsertLead, cn);
            cmd.Parameters.AddWithValue("@name", lead.FullName.Trim());
            cmd.Parameters.AddWithValue("@phone", lead.Phone.Trim());
            cmd.Parameters.AddWithValue("@email", WebsiteSql.Db(lead.Email));
            cmd.Parameters.AddWithValue("@company", WebsiteSql.Db(lead.CompanyName));
            cmd.Parameters.AddWithValue("@services", WebsiteSql.Db(lead.Services));
            cmd.Parameters.AddWithValue("@type", WebsiteSql.Db(lead.CompanyType));
            cmd.Parameters.AddWithValue("@scale", WebsiteSql.Db(lead.Scale));
            cmd.Parameters.AddWithValue("@time", WebsiteSql.Db(lead.ContactTime));
            cmd.Parameters.AddWithValue("@message", WebsiteSql.Db(lead.Message));
            cmd.Parameters.AddWithValue("@source", WebsiteSql.Db(lead.SourcePage));
            cmd.Parameters.AddWithValue("@ip", (object?)ipHash ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return LeadOutcome.Saved;
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "WEBSITE_LEAD_WRITE_FAILED");
            return LeadOutcome.Unavailable;
        }
    }

    private async Task<IReadOnlyList<PostCard>> Cards(string sql, params (string Name, object Value)[] parameters)
    {
        var list = new List<PostCard>();
        await Safe("WEBSITE_POSTS_READ", async cn =>
        {
            await using var cmd = new SqlCommand(sql, cn);
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(WebsiteSql.Card(r));
        });
        return list;
    }

    private async Task Safe(string eventName, Func<SqlConnection, Task> body)
    {
        if (!Configured) return;
        try
        {
            await using var cn = Connection();
            await cn.OpenAsync();
            await body(cn);
        }
        catch (SqlException ex) { logger.LogWarning(ex, "{Event}: database unavailable, using defaults", eventName); }
    }

    private Task<T> Cached<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) =>
        cache.GetOrCreateAsync("website:" + key, e => { e.AbsoluteExpirationRelativeToNow = ttl ?? Ttl; return factory(); })!;

    private static string Like(string s) => s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
