using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using TAPortal.Shared;

namespace TAPortal.Web.Services;

// Ảnh đại diện nhân viên (operator 2026-11-14): one small square photo per employee (dbo.EmployeePhotos, migration 051),
// shrunk in the browser before upload. The index (who has a photo, which version) is cached so every avatar on a page
// costs no query; uploading or removing a photo clears it.
public sealed record EmployeePhoto(byte[] Data, string ContentType, DateTime UpdatedAt);

public sealed record AvatarIndex(IReadOnlyDictionary<Guid, long> ByEmployee, IReadOnlyDictionary<Guid, (Guid EmployeeId, long Version)> ByUser)
{
    public static readonly AvatarIndex Empty = new(new Dictionary<Guid, long>(), new Dictionary<Guid, (Guid, long)>());
}

public enum EmployeePhotoOutcome { Saved, Invalid, NotFound }

public static class EmployeePhotoPolicy
{
    public const int MaxBytes = 300 * 1024;
    public const string TooLarge = "Ảnh quá lớn (tối đa 300 KB sau khi thu nhỏ). Thử ảnh khác hoặc dùng trình duyệt khác.";

    // The type is read from the file's first bytes, never from the name or the browser's claim.
    public static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public static string? Validate(byte[] data)
    {
        if (data.Length == 0) return "Chọn một ảnh.";
        if (data.Length > MaxBytes) return TooLarge;
        return Sniff(data) is null ? "Chỉ nhận ảnh JPG, PNG hoặc WebP." : null;
    }
}

public static class EmployeePhotoSql
{
    public const string Index = """
IF OBJECT_ID(N'dbo.EmployeePhotos', N'U') IS NOT NULL
 SELECT p.EmployeeId,e.UserId,p.UpdatedAt FROM dbo.EmployeePhotos p JOIN dbo.Employees e ON e.Id=p.EmployeeId WHERE e.IsDeleted=0;
ELSE SELECT TOP 0 CAST(NULL AS uniqueidentifier),CAST(NULL AS uniqueidentifier),CAST(NULL AS datetime2(3));
""";
    public const string Get = "SELECT p.Data,p.ContentType,p.UpdatedAt FROM dbo.EmployeePhotos p JOIN dbo.Employees e ON e.Id=p.EmployeeId AND e.IsDeleted=0 WHERE p.EmployeeId=@e;";
    public const string EmployeeOfUser = "SELECT TOP 1 Id FROM dbo.Employees WHERE UserId=@u AND IsDeleted=0;";
    public const string Exists = "SELECT COUNT(1) FROM dbo.Employees WHERE Id=@e AND IsDeleted=0;";
    public const string Upsert = """
UPDATE dbo.EmployeePhotos SET Data=@data,ContentType=@type,ByteSize=@size,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@by WHERE EmployeeId=@e;
IF @@ROWCOUNT=0 INSERT dbo.EmployeePhotos(EmployeeId,Data,ContentType,ByteSize,UpdatedBy) VALUES(@e,@data,@type,@size,@by);
""";
    public const string Delete = "DELETE FROM dbo.EmployeePhotos WHERE EmployeeId=@e;";
}

public interface IEmployeePhotoStore
{
    Task<AvatarIndex> IndexAsync();
    Task<EmployeePhoto?> GetAsync(Guid employeeId);
    Task<Guid?> EmployeeOfUserAsync(Guid userId);
    Task<(EmployeePhotoOutcome Outcome, string? Message)> SaveAsync(Guid actor, Guid employeeId, byte[] data, string? ip);
    Task<EmployeePhotoOutcome> DeleteAsync(Guid actor, Guid employeeId, string? ip);
}

public sealed class SqlEmployeePhotoStore(IConfiguration configuration, IMemoryCache cache, ILogger<SqlEmployeePhotoStore> logger) : IEmployeePhotoStore
{
    private const string CacheKey = "avatar-index";
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<AvatarIndex> IndexAsync()
    {
        if (cache.TryGetValue(CacheKey, out AvatarIndex? hit) && hit is not null) return hit;
        AvatarIndex index;
        try
        {
            var byEmployee = new Dictionary<Guid, long>(); var byUser = new Dictionary<Guid, (Guid, long)>();
            await using var cn = Connection();
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(EmployeePhotoSql.Index, cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var version = r.GetDateTime(2).Ticks;
                byEmployee[r.GetGuid(0)] = version;
                if (!r.IsDBNull(1)) byUser[r.GetGuid(1)] = (r.GetGuid(0), version);
            }
            index = new(byEmployee, byUser);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            // Avatars are decoration: fall back to initials rather than failing the page.
            logger.LogWarning(ex, "Avatar index unavailable.");
            index = AvatarIndex.Empty;
        }
        cache.Set(CacheKey, index, TimeSpan.FromMinutes(10));
        return index;
    }

    public async Task<EmployeePhoto?> GetAsync(Guid employeeId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(EmployeePhotoSql.Get, cn);
        cmd.Parameters.Add("@e", SqlDbType.UniqueIdentifier).Value = employeeId;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new((byte[])r[0], r.GetString(1), r.GetDateTime(2)) : null;
    }

    public async Task<Guid?> EmployeeOfUserAsync(Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(EmployeePhotoSql.EmployeeOfUser, cn);
        cmd.Parameters.Add("@u", SqlDbType.UniqueIdentifier).Value = userId;
        return await cmd.ExecuteScalarAsync() is Guid id ? id : null;
    }

    public async Task<(EmployeePhotoOutcome Outcome, string? Message)> SaveAsync(Guid actor, Guid employeeId, byte[] data, string? ip)
    {
        if (EmployeePhotoPolicy.Validate(data) is string error) return (EmployeePhotoOutcome.Invalid, error);
        var type = EmployeePhotoPolicy.Sniff(data)!;
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        if (Convert.ToInt32(await Scalar(cn, tx, EmployeePhotoSql.Exists, ("@e", employeeId))) != 1) return (EmployeePhotoOutcome.NotFound, null);
        await using (var cmd = new SqlCommand(EmployeePhotoSql.Upsert, cn, tx))
        {
            cmd.Parameters.Add("@e", SqlDbType.UniqueIdentifier).Value = employeeId;
            cmd.Parameters.Add("@data", SqlDbType.VarBinary, -1).Value = data;
            cmd.Parameters.Add("@type", SqlDbType.VarChar, 50).Value = type;
            cmd.Parameters.Add("@size", SqlDbType.Int).Value = data.Length;
            cmd.Parameters.Add("@by", SqlDbType.UniqueIdentifier).Value = actor;
            await cmd.ExecuteNonQueryAsync();
        }
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, "EMPLOYEE_PHOTO_SET", "dbo.EmployeePhotos", employeeId.ToString(), "{}",
            AuditSql.Json(("contentType", type), ("bytes", data.Length)), ip);
        await tx.CommitAsync();
        cache.Remove(CacheKey);
        return (EmployeePhotoOutcome.Saved, null);
    }

    public async Task<EmployeePhotoOutcome> DeleteAsync(Guid actor, Guid employeeId, string? ip)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        await using (var cmd = new SqlCommand(EmployeePhotoSql.Delete, cn, tx))
        {
            cmd.Parameters.Add("@e", SqlDbType.UniqueIdentifier).Value = employeeId;
            if (await cmd.ExecuteNonQueryAsync() == 0) return EmployeePhotoOutcome.NotFound;
        }
        await AuditSql.WriteOrThrowAsync(cn, tx, AuditPolicy.RequireDurable(configuration), actor, "EMPLOYEE_PHOTO_REMOVED", "dbo.EmployeePhotos", employeeId.ToString(), "{}", "{}", ip);
        await tx.CommitAsync();
        cache.Remove(CacheKey);
        return EmployeePhotoOutcome.Saved;
    }

    private static async Task<object?> Scalar(SqlConnection cn, SqlTransaction tx, string sql, (string Name, object Value) arg)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.AddWithValue(arg.Name, arg.Value);
        return await cmd.ExecuteScalarAsync();
    }
}
