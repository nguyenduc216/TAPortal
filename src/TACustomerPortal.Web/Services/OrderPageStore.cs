using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace TACustomerPortal.Web.Services;

// Logos and the public payment page of an order (migration 024). Logos are kept in the database (a deploy never loses
// them); a company has one logo (BuyerId null) and each of its own customers may have one.

public sealed record LogoImage(string ContentType, byte[] Data, DateTime UpdatedAt)
{
    /// <summary>Cache-busting value for image URLs.</summary>
    public static string Version(DateTime updatedAt) => updatedAt.Ticks.ToString("x");
}

/// <summary>An order reached through its public page token: the company it belongs to and what the payer may correct.</summary>
public sealed record PayPageOrder(Guid CustomerId, Guid OrderId, string SellerName, string? SellerTaxCode, string? InvoiceBuyerTaxCode, string? InvoiceBuyerName,
    DateTime? InvoiceBuyerUpdatedAt);

/// <summary>Tax code and invoice name the payer entered on the public page; they replace the buyer profile on the e-invoice.</summary>
public sealed record InvoiceBuyer(string? TaxCode, string? Name, DateTime? UpdatedAt)
{
    public bool IsSet => TaxCode is not null || Name is not null;
}

public interface ILogoStore
{
    /// <summary>Last change of the logo of the company (buyerId null) or of one of its customers; null when there is none.</summary>
    Task<DateTime?> LogoVersionAsync(Guid customerId, Guid? buyerId);
    Task<LogoImage?> LogoAsync(Guid customerId, Guid? buyerId);
    /// <summary>Insert or replace. A buyer logo is only written when the buyer belongs to the customer.</summary>
    Task SaveLogoAsync(Guid customerId, Guid? buyerId, LogoImage logo, Guid actor);
    Task RemoveLogoAsync(Guid customerId, Guid? buyerId);
}

public interface IOrderPageStore
{
    /// <summary>The order's public page token, created on first use.</summary>
    Task<string?> TokenAsync(Guid customerId, Guid orderId);
    Task<PayPageOrder?> FindAsync(string token);
    Task<InvoiceBuyer> InvoiceBuyerAsync(Guid customerId, Guid orderId);
    /// <summary>
    /// Stores the payer's tax code and invoice name. False when the invoice can no longer change: it is being issued or was
    /// issued, or the order was cancelled.
    /// </summary>
    Task<bool> SaveInvoiceBuyerAsync(string token, string? taxCode, string? name);
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

public sealed class SqlOrderPageStore(IConfiguration configuration) : ILogoStore, IOrderPageStore
{
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

    public async Task<DateTime?> LogoVersionAsync(Guid customerId, Guid? buyerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"SELECT UpdatedAt FROM dbo.CustomerLogos WHERE {Owner}", customerId, buyerId);
        return await cmd.ExecuteScalarAsync() is DateTime at ? at : null;
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
    }

    public async Task RemoveLogoAsync(Guid customerId, Guid? buyerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, $"DELETE FROM dbo.CustomerLogos WHERE {Owner}", customerId, buyerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> TokenAsync(Guid customerId, Guid orderId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
UPDATE dbo.CustomerOrders SET PublicToken=@t WHERE CustomerId=@c AND Id=@o AND PublicToken IS NULL;
SELECT PublicToken FROM dbo.CustomerOrders WHERE CustomerId=@c AND Id=@o;
""", customerId);
        cmd.Parameters.Add("@o", SqlDbType.UniqueIdentifier).Value = orderId;
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 40).Value = NewToken();
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>24 random bytes, base64url (32 characters): the page cannot be found by guessing or by the order number.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');

    public async Task<PayPageOrder?> FindAsync(string token)
    {
        await using var cn = await Open();
        await using var cmd = new SqlCommand("""
SELECT o.CustomerId,o.Id,c.Name,c.TaxCode,o.InvoiceBuyerTaxCode,o.InvoiceBuyerName,o.InvoiceBuyerUpdatedAt
FROM dbo.CustomerOrders o JOIN dbo.Customers c ON c.Id=o.CustomerId
WHERE o.PublicToken=@t AND c.IsDeleted=0 AND c.IsActive=1
""", cn);
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 40).Value = token;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDateTime(6))
            : null;
    }

    public async Task<InvoiceBuyer> InvoiceBuyerAsync(Guid customerId, Guid orderId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT InvoiceBuyerTaxCode,InvoiceBuyerName,InvoiceBuyerUpdatedAt FROM dbo.CustomerOrders WHERE CustomerId=@c AND Id=@o", customerId);
        cmd.Parameters.Add("@o", SqlDbType.UniqueIdentifier).Value = orderId;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetDateTime(2)) : new(null, null, null);
    }

    public async Task<bool> SaveInvoiceBuyerAsync(string token, string? taxCode, string? name)
    {
        await using var cn = await Open();
        await using var cmd = new SqlCommand("""
UPDATE dbo.CustomerOrders SET InvoiceBuyerTaxCode=@tax,InvoiceBuyerName=@name,InvoiceBuyerUpdatedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
WHERE PublicToken=@t AND InvoiceStatus IN ('NONE','FAILED') AND Status NOT IN ('CANCELLED','CANCELLED_PAID')
""", cn);
        cmd.Parameters.Add("@t", SqlDbType.VarChar, 40).Value = token;
        cmd.Parameters.Add("@tax", SqlDbType.VarChar, 20).Value = (object?)taxCode ?? DBNull.Value;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 250).Value = (object?)name ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync() == 1;
    }
}
