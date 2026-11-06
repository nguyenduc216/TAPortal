using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TAPortal.Integrations.Branding;

// Logos of the companies using the Customer Portal (BuyerId null) and of their own customers (dbo.CustomerLogos, migration
// 024). Shared by the Customer Portal (the company manages its own) and TAPortal Admin (T.A staff manage a company's logo).
// Kept in the database so a deploy never loses them.

public sealed record LogoImage(string ContentType, byte[] Data, DateTime UpdatedAt)
{
    /// <summary>Cache-busting value for image URLs.</summary>
    public static string Version(DateTime updatedAt) => updatedAt.Ticks.ToString("x");
}

public interface ILogoStore
{
    /// <summary>Last change of the logo of the company (buyerId null) or of one of its customers; null when there is none.</summary>
    Task<DateTime?> LogoVersionAsync(Guid customerId, Guid? buyerId);
    /// <summary>Last change of every customer logo of a company, by buyer id (for lists).</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> BuyerLogoVersionsAsync(Guid customerId);
    Task<LogoImage?> LogoAsync(Guid customerId, Guid? buyerId);
    /// <summary>Insert or replace. A buyer logo is only written when the buyer belongs to the customer.</summary>
    Task SaveLogoAsync(Guid customerId, Guid? buyerId, LogoImage logo, Guid actor);
    Task RemoveLogoAsync(Guid customerId, Guid? buyerId);
}

/// <summary>Uploaded logo checks: PNG, JPEG or WEBP by content (not by file name), at most 1 MB. SVG is refused (scripts).</summary>
public static class LogoUpload
{
    public const int MaxBytes = 1024 * 1024;
    public const string Accept = "image/png,image/jpeg,image/webp";

    public static async Task<(LogoImage? Logo, string? Error)> ReadAsync(IFormFile? file, DateTime now, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return (null, null);
        if (file.Length > MaxBytes) return (null, "Logo tối đa 1 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();
        return Sniff(data) is { } type ? (new LogoImage(type, data, now), null) : (null, "Logo phải là ảnh PNG, JPG hoặc WEBP.");
    }

    public static string? Sniff(ReadOnlySpan<byte> d) =>
        d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 ? "image/png"
        : d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? "image/jpeg"
        : d.Length > 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;
}

/// <summary>
/// SQL store. The company logo's version is shown on every portal page (sidebar), so versions are cached for a minute per
/// process; a change made here clears this process's entry, the other site (Admin / Customer Portal) catches up within a minute.
/// </summary>
public sealed class SqlLogoStore(IConfiguration configuration, IMemoryCache cache) : ILogoStore
{
    private static readonly TimeSpan VersionTtl = TimeSpan.FromMinutes(1);
    private static readonly DateTime None = DateTime.MinValue;

    private async Task<SqlConnection> Open()
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, Guid customerId, Guid? buyerId = null)
    {
        var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@c", SqlDbType.UniqueIdentifier).Value = customerId;
        cmd.Parameters.Add("@b", SqlDbType.UniqueIdentifier).Value = buyerId is Guid b ? b : DBNull.Value;
        return cmd;
    }

    private const string Owner = "CustomerId=@c AND ((@b IS NULL AND BuyerId IS NULL) OR BuyerId=@b)";
    private static string Key(Guid customerId, Guid? buyerId) => $"logo-version:{customerId:N}:{buyerId:N}";

    public async Task<DateTime?> LogoVersionAsync(Guid customerId, Guid? buyerId)
    {
        if (!cache.TryGetValue(Key(customerId, buyerId), out DateTime at))
        {
            await using var cn = await Open();
            await using var cmd = Command(cn, $"SELECT UpdatedAt FROM dbo.CustomerLogos WHERE {Owner}", customerId, buyerId);
            at = await cmd.ExecuteScalarAsync() is DateTime found ? found : None;
            cache.Set(Key(customerId, buyerId), at, VersionTtl);
        }
        return at == None ? null : at;
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> BuyerLogoVersionsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT BuyerId,UpdatedAt FROM dbo.CustomerLogos WHERE CustomerId=@c AND BuyerId IS NOT NULL", customerId);
        var map = new Dictionary<Guid, DateTime>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetGuid(0)] = r.GetDateTime(1);
        return map;
    }

    public async Task<LogoImage?> LogoAsync(Guid customerId, Guid? buyerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT ContentType,Data,UpdatedAt FROM dbo.CustomerLogos WHERE {Owner}", customerId, buyerId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetString(0), (byte[])r[1], r.GetDateTime(2)) : null;
    }

    public async Task SaveLogoAsync(Guid customerId, Guid? buyerId, LogoImage logo, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"""
UPDATE dbo.CustomerLogos SET ContentType=@t,Data=@d,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@a WHERE {Owner};
IF @@ROWCOUNT=0
 INSERT dbo.CustomerLogos(CustomerId,BuyerId,ContentType,Data,UpdatedBy)
 SELECT @c,@b,@t,@d,@a WHERE @b IS NULL OR EXISTS(SELECT 1 FROM dbo.CustomerBuyers WHERE Id=@b AND CustomerId=@c AND IsDeleted=0);
""", customerId, buyerId);
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 20).Value = logo.ContentType;
        cmd.Parameters.Add("@d", SqlDbType.VarBinary, -1).Value = logo.Data;
        cmd.Parameters.Add("@a", SqlDbType.UniqueIdentifier).Value = actor;
        await cmd.ExecuteNonQueryAsync();
        cache.Remove(Key(customerId, buyerId));
    }

    public async Task RemoveLogoAsync(Guid customerId, Guid? buyerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"DELETE FROM dbo.CustomerLogos WHERE {Owner}", customerId, buyerId);
        await cmd.ExecuteNonQueryAsync();
        cache.Remove(Key(customerId, buyerId));
    }
}

public static class LogoServiceCollectionExtensions
{
    public static IServiceCollection AddLogos(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<ILogoStore, SqlLogoStore>();
        return services;
    }
}
