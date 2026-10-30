using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Services;

// Customer (T.A) <-> Tingee child merchant (dbo.TingeeMerchants, migration 011). One merchant per customer and per
// merchant id; both are unique in SQL. Link/unlink are audited in the same transaction (fail closed when required).
public sealed record TingeeMerchantLink(Guid CustomerId, long MerchantId, string? MerchantCode, string? PhoneNumber, string? Email, string Source, DateTime CreatedAt);

public enum TingeeLinkOutcome { Saved, CustomerAlreadyLinked, MerchantAlreadyLinked, NotLinked, AuditUnavailable }

public interface ITingeeMerchantStore
{
    Task<TingeeMerchantLink?> GetAsync(Guid customerId);
    Task<TingeeLinkOutcome> LinkAsync(TingeeMerchantLink link, Guid actor);
    Task<TingeeLinkOutcome> UnlinkAsync(Guid customerId, Guid actor);
}

public sealed class SqlTingeeMerchantStore(IConfiguration configuration, AdminAuditOptions audit) : ITingeeMerchantStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"]);

    public async Task<TingeeMerchantLink?> GetAsync(Guid customerId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT CustomerId,MerchantId,MerchantCode,PhoneNumber,Email,Source,CreatedAt FROM dbo.TingeeMerchants WHERE CustomerId=@c", cn);
        cmd.Parameters.AddWithValue("@c", customerId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new(r.GetGuid(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetDateTime(6))
            : null;
    }

    public async Task<TingeeLinkOutcome> LinkAsync(TingeeMerchantLink link, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using (var check = new SqlCommand("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.TingeeMerchants WITH (UPDLOCK,HOLDLOCK) WHERE CustomerId=@c) THEN 1 WHEN EXISTS(SELECT 1 FROM dbo.TingeeMerchants WITH (UPDLOCK,HOLDLOCK) WHERE MerchantId=@m) THEN 2 ELSE 0 END", cn, tx))
            {
                check.Parameters.AddWithValue("@c", link.CustomerId);
                check.Parameters.AddWithValue("@m", link.MerchantId);
                switch (Convert.ToInt32(await check.ExecuteScalarAsync()))
                {
                    case 1: await tx.RollbackAsync(); return TingeeLinkOutcome.CustomerAlreadyLinked;
                    case 2: await tx.RollbackAsync(); return TingeeLinkOutcome.MerchantAlreadyLinked;
                }
            }
            await using (var insert = new SqlCommand("INSERT dbo.TingeeMerchants(CustomerId,MerchantId,MerchantCode,PhoneNumber,Email,Source,CreatedBy) VALUES(@c,@m,@code,@phone,@email,@source,@actor)", cn, tx))
            {
                insert.Parameters.AddWithValue("@c", link.CustomerId);
                insert.Parameters.AddWithValue("@m", link.MerchantId);
                insert.Parameters.AddWithValue("@code", (object?)link.MerchantCode ?? DBNull.Value);
                insert.Parameters.AddWithValue("@phone", (object?)link.PhoneNumber ?? DBNull.Value);
                insert.Parameters.AddWithValue("@email", (object?)link.Email ?? DBNull.Value);
                insert.Parameters.AddWithValue("@source", link.Source);
                insert.Parameters.AddWithValue("@actor", actor);
                await insert.ExecuteNonQueryAsync();
            }
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "TINGEE_MERCHANT_LINKED", "dbo.TingeeMerchants", link.CustomerId.ToString(),
                "{}", AuditSql.Json(("merchantId", link.MerchantId), ("merchantCode", link.MerchantCode), ("source", link.Source)), null);
            await tx.CommitAsync();
            return TingeeLinkOutcome.Saved;
        }
        catch (AuditUnavailableException)
        {
            await tx.RollbackAsync();
            return TingeeLinkOutcome.AuditUnavailable;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await tx.RollbackAsync();
            return TingeeLinkOutcome.MerchantAlreadyLinked;
        }
    }

    public async Task<TingeeLinkOutcome> UnlinkAsync(Guid customerId, Guid actor)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            await using var delete = new SqlCommand("DELETE dbo.TingeeMerchants OUTPUT deleted.MerchantId WHERE CustomerId=@c", cn, tx);
            delete.Parameters.AddWithValue("@c", customerId);
            var merchantId = await delete.ExecuteScalarAsync();
            if (merchantId is null) { await tx.RollbackAsync(); return TingeeLinkOutcome.NotLinked; }
            await AuditSql.WriteOrThrowAsync(cn, tx, audit.RequireDurableAudit, actor, "TINGEE_MERCHANT_UNLINKED", "dbo.TingeeMerchants", customerId.ToString(),
                AuditSql.Json(("merchantId", merchantId)), "{}", null);
            await tx.CommitAsync();
            return TingeeLinkOutcome.Saved;
        }
        catch (AuditUnavailableException)
        {
            await tx.RollbackAsync();
            return TingeeLinkOutcome.AuditUnavailable;
        }
    }
}
