using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace TACustomerPortal.Web.Services;

// The public payment page of an order (migration 024). Logos: TAPortal.Integrations.Branding (shared with TAPortal Admin).

/// <summary>An order reached through its public page token: the company it belongs to and what the payer may correct.</summary>
public sealed record PayPageOrder(Guid CustomerId, Guid OrderId, string SellerName, string? SellerTaxCode, string? InvoiceBuyerTaxCode, string? InvoiceBuyerName,
    DateTime? InvoiceBuyerUpdatedAt);

/// <summary>Tax code and invoice name the payer entered on the public page; they replace the buyer profile on the e-invoice.</summary>
public sealed record InvoiceBuyer(string? TaxCode, string? Name, DateTime? UpdatedAt)
{
    public bool IsSet => TaxCode is not null || Name is not null;
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

public sealed class SqlOrderPageStore(IConfiguration configuration) : IOrderPageStore
{
    private async Task<SqlConnection> Open()
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, Guid customerId)
    {
        var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@c", SqlDbType.UniqueIdentifier).Value = customerId;
        return cmd;
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
