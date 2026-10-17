using Microsoft.Data.SqlClient;

namespace TAPortal.Web.Tests;

// Dedicated customer per invocation; leave audit and relationship history intact.
internal sealed class CrmRunFixture
{
    public Guid RunId { get; } = Guid.NewGuid();
    public Guid CustomerId { get; } = Guid.NewGuid();
    public string Code => $"TA_E2E_{RunId:N}";

    public async Task ProvisionAsync(SqlConnection connection, Guid actor, Guid ownerBranch)
    {
        if (connection.Database != "TAPortalSys")
            throw new InvalidOperationException("CRM fixtures require TAPortalSys.");
        await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.Branches WHERE Id=@branch AND IsActive=1 AND IsDeleted=0", connection);
        check.Parameters.AddWithValue("@branch", ownerBranch);
        if ((int)(await check.ExecuteScalarAsync())! != 1)
            throw new InvalidOperationException($"CRM fixture preflight: owner branch {ownerBranch} is not active.");
        await using var insert = new SqlCommand("INSERT dbo.Customers(Id,Code,Name,OwnerBranchId,CreatedBy) VALUES(@id,@code,@name,@branch,@actor)", connection);
        insert.Parameters.AddWithValue("@id", CustomerId);
        insert.Parameters.AddWithValue("@code", Code);
        insert.Parameters.AddWithValue("@name", $"TA E2E CRM {RunId:N}");
        insert.Parameters.AddWithValue("@branch", ownerBranch);
        insert.Parameters.AddWithValue("@actor", actor);
        if (await insert.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException("CRM fixture customer insertion failed.");
    }
}