using Microsoft.Data.SqlClient;

namespace TACustomerPortal.Web.Services;
public sealed record CustomerUser(Guid Id, string Username, string DisplayName, string? Email, string PasswordHash);
public sealed record CustomerMembership(Guid CustomerId, string Code, string Name, string? TaxCode, string? Email, string? Phone, string? Address, string Role);
public interface ICustomerStore
{
    Task<CustomerUser?> FindUser(string username);
    Task<CustomerUser?> FindUser(Guid id);
    Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId);
    Task<bool> ChangePassword(Guid id, string oldHash, string newHash);
    Task TouchLogin(Guid id);
}

public sealed class CustomerStore(IConfiguration configuration) : ICustomerStore
{
    private SqlConnection Connection() => new(configuration["Database:ConnectionString"] ?? throw new InvalidOperationException("Configure Database__ConnectionString"));
    private async Task<CustomerUser?> ReadUser(string predicate, object value)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Id,Username,DisplayName,Email,PasswordHash FROM dbo.Users WHERE " + predicate + " AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'", cn);
        cmd.Parameters.AddWithValue("@value", value);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4)) : null;
    }

    public Task<CustomerUser?> FindUser(string username) => ReadUser("NormalizedUsername=@value", username.Trim().ToUpperInvariant());
    public Task<CustomerUser?> FindUser(Guid id) => ReadUser("Id=@value", id);
    public async Task<IReadOnlyList<CustomerMembership>> Memberships(Guid userId)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
SELECT c.Id,c.Code,c.Name,c.TaxCode,c.Email,c.Phone,c.Address,m.PortalRole
FROM dbo.CustomerPortalMemberships m JOIN dbo.Customers c ON c.Id=m.CustomerId JOIN dbo.Users u ON u.Id=m.UserId
WHERE m.UserId=@u AND m.IsActive=1 AND c.IsActive=1 AND c.IsDeleted=0 AND c.Status='ACTIVE'
AND u.IsActive=1 AND u.IsDeleted=0 AND u.Status='ACTIVE' ORDER BY c.Name
""", cn);
        cmd.Parameters.AddWithValue("@u", userId);
        var list = new List<CustomerMembership>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new(r.GetGuid(0), r.GetString(1), r.GetString(2), Text(r, 3), Text(r, 4), Text(r, 5), Text(r, 6), r.GetString(7)));
        return list;
    }

    private static string? Text(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    public async Task<bool> ChangePassword(Guid id, string oldHash, string newHash)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET PasswordHash=@new,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@id WHERE Id=@id AND PasswordHash=@old AND IsActive=1 AND IsDeleted=0 AND Status='ACTIVE'", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@old", oldHash);
        cmd.Parameters.AddWithValue("@new", newHash);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task TouchLogin(Guid id)
    {
        await using var cn = Connection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE dbo.Users SET LastLoginAt=SYSUTCDATETIME() WHERE Id=@id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
