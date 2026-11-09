using System.Data;
using Microsoft.Data.SqlClient;
using TAPortal.Integrations.Branding;

namespace TACustomerPortal.Web.Services;

// Product catalogue of a company and the prices agreed with each of its own customers (migration 034). Catalogue prices
// are reference prices INCLUDING VAT; an order line converts them to the price before tax (OrderMath.PriceBeforeTax).
// A customer price replaces the catalogue price on that customer's next orders (operator request 2026-11-09).

/// <summary>ImageAt: last change of the image (null: no image), used as cache-busting version.</summary>
public sealed record Product(Guid Id, string Name, string? UnitName, decimal Price, string TaxRateCode, bool IsActive, DateTime? ImageAt, int CustomPrices = 0);

/// <summary>A price agreed with one customer (dbo.CustomerBuyers) for one product.</summary>
public sealed record BuyerPrice(Guid BuyerId, string BuyerCode, string BuyerName, Guid ProductId, decimal Price, DateTime UpdatedAt);

public interface IProductStore
{
    Task<IReadOnlyList<Product>> ProductsAsync(Guid customerId);
    Task<Product?> ProductAsync(Guid customerId, Guid id);
    /// <summary>Insert (Id empty) or update; returns the id. image: new image; removeImage: drop the current one.</summary>
    Task<Guid> SaveProductAsync(Guid customerId, Product product, LogoImage? image, bool removeImage, Guid actor);
    Task SetActiveAsync(Guid customerId, Guid id, bool active, Guid actor);
    Task<LogoImage?> ImageAsync(Guid customerId, Guid id);
    /// <summary>Prices agreed with one customer, by product id.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> BuyerPricesAsync(Guid customerId, Guid buyerId);
    /// <summary>Customers with their own price for one product.</summary>
    Task<IReadOnlyList<BuyerPrice>> ProductPricesAsync(Guid customerId, Guid productId);
    /// <summary>
    /// Stores the prices agreed with a customer. A price equal to the catalogue price removes the customer's own price
    /// (the customer is back on the catalogue price). Products or buyers of another company are ignored.
    /// </summary>
    Task SaveBuyerPricesAsync(Guid customerId, Guid buyerId, IReadOnlyDictionary<Guid, decimal> prices, Guid actor);
    Task RemoveBuyerPriceAsync(Guid customerId, Guid buyerId, Guid productId);
}

public sealed class SqlProductStore(IConfiguration configuration) : IProductStore
{
    private async Task<SqlConnection> Open()
    {
        var cn = new SqlConnection(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
        await cn.OpenAsync();
        return cn;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, Guid customerId, SqlTransaction? tx = null)
    {
        var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@c", SqlDbType.UniqueIdentifier).Value = customerId;
        return cmd;
    }

    private const string Select = """
SELECT p.Id,p.Name,p.UnitName,p.Price,p.TaxRateCode,p.IsActive,p.ImageUpdatedAt,
 (SELECT COUNT(*) FROM dbo.CustomerBuyerPrices bp WHERE bp.ProductId=p.Id)
FROM dbo.CustomerProducts p WHERE p.CustomerId=@c
""";

    private static Product Read(SqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetDecimal(3), r.GetString(4), r.GetBoolean(5),
        r.IsDBNull(6) ? null : r.GetDateTime(6), r.GetInt32(7));

    public async Task<IReadOnlyList<Product>> ProductsAsync(Guid customerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, Select + " ORDER BY p.IsActive DESC, p.Name", customerId);
        var list = new List<Product>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(Read(r));
        return list;
    }

    public async Task<Product?> ProductAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, Select + " AND p.Id=@id", customerId);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? Read(r) : null;
    }

    public async Task<Guid> SaveProductAsync(Guid customerId, Product p, LogoImage? image, bool removeImage, Guid actor)
    {
        await using var cn = await Open();
        // Image columns: replaced by a new image, cleared on removal, otherwise kept.
        var imageSet = image is not null ? ",ImageType=@it,Image=@img,ImageUpdatedAt=SYSUTCDATETIME()" : removeImage ? ",ImageType=NULL,Image=NULL,ImageUpdatedAt=NULL" : "";
        await using var cmd = Command(cn, p.Id == Guid.Empty
            ? $"""
INSERT dbo.CustomerProducts(CustomerId,Name,UnitName,Price,TaxRateCode,IsActive,ImageType,Image,ImageUpdatedAt,UpdatedBy)
OUTPUT inserted.Id VALUES(@c,@name,@unit,@price,@tax,@active,@it,@img,{(image is null ? "NULL" : "SYSUTCDATETIME()")},@a)
"""
            : $"""
UPDATE dbo.CustomerProducts SET Name=@name,UnitName=@unit,Price=@price,TaxRateCode=@tax,IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@a{imageSet}
OUTPUT inserted.Id WHERE CustomerId=@c AND Id=@id
""", customerId);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = p.Id;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = p.Name;
        cmd.Parameters.Add("@unit", SqlDbType.NVarChar, 50).Value = (object?)p.UnitName ?? DBNull.Value;
        cmd.Parameters.Add("@price", SqlDbType.Decimal).Value = p.Price;
        cmd.Parameters["@price"].Precision = 18;
        cmd.Parameters.Add("@tax", SqlDbType.VarChar, 3).Value = p.TaxRateCode;
        cmd.Parameters.Add("@active", SqlDbType.Bit).Value = p.IsActive;
        cmd.Parameters.Add("@it", SqlDbType.VarChar, 20).Value = (object?)image?.ContentType ?? DBNull.Value;
        cmd.Parameters.Add("@img", SqlDbType.VarBinary, -1).Value = (object?)image?.Data ?? DBNull.Value;
        cmd.Parameters.Add("@a", SqlDbType.UniqueIdentifier).Value = actor;
        return await cmd.ExecuteScalarAsync() is Guid id ? id : Guid.Empty;
    }

    public async Task SetActiveAsync(Guid customerId, Guid id, bool active, Guid actor)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "UPDATE dbo.CustomerProducts SET IsActive=@active,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@a WHERE CustomerId=@c AND Id=@id", customerId);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        cmd.Parameters.Add("@active", SqlDbType.Bit).Value = active;
        cmd.Parameters.Add("@a", SqlDbType.UniqueIdentifier).Value = actor;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<LogoImage?> ImageAsync(Guid customerId, Guid id)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT ImageType,Image,ImageUpdatedAt FROM dbo.CustomerProducts WHERE CustomerId=@c AND Id=@id AND Image IS NOT NULL", customerId);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetString(0), (byte[])r[1], r.GetDateTime(2)) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> BuyerPricesAsync(Guid customerId, Guid buyerId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "SELECT ProductId,Price FROM dbo.CustomerBuyerPrices WHERE CustomerId=@c AND BuyerId=@b", customerId);
        cmd.Parameters.Add("@b", SqlDbType.UniqueIdentifier).Value = buyerId;
        var map = new Dictionary<Guid, decimal>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.GetGuid(0)] = r.GetDecimal(1);
        return map;
    }

    public async Task<IReadOnlyList<BuyerPrice>> ProductPricesAsync(Guid customerId, Guid productId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, """
SELECT bp.BuyerId,b.Code,b.Name,bp.ProductId,bp.Price,bp.UpdatedAt
FROM dbo.CustomerBuyerPrices bp JOIN dbo.CustomerBuyers b ON b.Id=bp.BuyerId AND b.CustomerId=bp.CustomerId
WHERE bp.CustomerId=@c AND bp.ProductId=@p ORDER BY b.Name
""", customerId);
        cmd.Parameters.Add("@p", SqlDbType.UniqueIdentifier).Value = productId;
        var list = new List<BuyerPrice>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetGuid(3), r.GetDecimal(4), r.GetDateTime(5)));
        return list;
    }

    public async Task SaveBuyerPricesAsync(Guid customerId, Guid buyerId, IReadOnlyDictionary<Guid, decimal> prices, Guid actor)
    {
        if (prices.Count == 0) return;
        await using var cn = await Open();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        foreach (var (productId, price) in prices)
        {
            await using var cmd = Command(cn, """
IF EXISTS(SELECT 1 FROM dbo.CustomerProducts WHERE Id=@p AND CustomerId=@c AND Price=@price)
 DELETE dbo.CustomerBuyerPrices WHERE CustomerId=@c AND BuyerId=@b AND ProductId=@p;
ELSE IF EXISTS(SELECT 1 FROM dbo.CustomerProducts WHERE Id=@p AND CustomerId=@c)
   AND EXISTS(SELECT 1 FROM dbo.CustomerBuyers WHERE Id=@b AND CustomerId=@c AND IsDeleted=0)
BEGIN
 UPDATE dbo.CustomerBuyerPrices SET Price=@price,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@a WHERE CustomerId=@c AND BuyerId=@b AND ProductId=@p;
 IF @@ROWCOUNT=0 INSERT dbo.CustomerBuyerPrices(CustomerId,BuyerId,ProductId,Price,UpdatedBy) VALUES(@c,@b,@p,@price,@a);
END
""", customerId, tx);
            cmd.Parameters.Add("@b", SqlDbType.UniqueIdentifier).Value = buyerId;
            cmd.Parameters.Add("@p", SqlDbType.UniqueIdentifier).Value = productId;
            cmd.Parameters.Add("@price", SqlDbType.Decimal).Value = price;
            cmd.Parameters["@price"].Precision = 18;
            cmd.Parameters.Add("@a", SqlDbType.UniqueIdentifier).Value = actor;
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task RemoveBuyerPriceAsync(Guid customerId, Guid buyerId, Guid productId)
    {
        await using var cn = await Open();
        await using var cmd = Command(cn, "DELETE dbo.CustomerBuyerPrices WHERE CustomerId=@c AND BuyerId=@b AND ProductId=@p", customerId);
        cmd.Parameters.Add("@b", SqlDbType.UniqueIdentifier).Value = buyerId;
        cmd.Parameters.Add("@p", SqlDbType.UniqueIdentifier).Value = productId;
        await cmd.ExecuteNonQueryAsync();
    }
}
