using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// FEATURE_ID: TAPORTAL-CRM-CUSTOMER-360
// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-019
public sealed class CrmPhase1019SqlTests
{
    [Fact]
    public async Task RealServiceExecutesAllNineRelationMutations()
    {
        if (Environment.GetEnvironmentVariable("TAPORTAL_CRM_019_SQL") != "1") return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "TAPortal.Web", "TAPortal.Web.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root!.FullName, "src", "TAPortal.Web", "appsettings.json")).Build();
        var connectionString = config["Database:ConnectionString"]!;
        var db = new PortalDb(config, NullLogger<PortalDb>.Instance);
        static Guid Id(int n) => Guid.Parse($"01900000-0000-4000-8000-{n:000000000000}");
        var actor = Id(11);
        var customer = Id(41);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync();
        Assert.Equal("TAPortalSys", cn.Database);
        async Task<(Guid id, byte[] version)> Latest(string table, string targetColumn, Guid target)
        {
            await using var cmd = new SqlCommand($"SELECT TOP(1) Id,RowVersion FROM dbo.{table} WHERE CustomerId=@customer AND {targetColumn}=@target AND CreatedBy=@actor ORDER BY CreatedAt DESC,Id DESC", cn);
            cmd.Parameters.AddWithValue("@customer", customer);
            cmd.Parameters.AddWithValue("@target", target);
            cmd.Parameters.AddWithValue("@actor", actor);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetGuid(0), (byte[])reader[1]);
        }
        Assert.NotNull(await db.GetCrmRelationshipDetailAsync(customer, actor));
        Assert.Equal(CrmRelationWriteResult.Forbidden, await db.CreateCustomerEmployeeAssignmentAsync(new(customer, Id(21), start, null, "OWNER"), Id(12)));
        async Task Cycle(string table, string column, Guid target,
            Func<CrmRelationWriteRequest, Guid, Task<CrmRelationWriteResult>> create,
            Func<CrmRelationChangeRequest, Guid, Task<CrmRelationWriteResult>> change,
            Func<CrmRelationRevokeRequest, Guid, Task<CrmRelationWriteResult>> revoke, string? role)
        {
            var current = await LatestOrNull();
            if (current.HasValue) Assert.Equal(CrmRelationWriteResult.Success, await revoke(new(current.Value.id, current.Value.version), actor));
            Assert.Equal(CrmRelationWriteResult.Success, await create(new(customer, target, start, null, role), actor));
            var created = await Latest(table, column, target);
            Assert.Equal(CrmRelationWriteResult.Overlap, await create(new(customer, target, start, null, role), actor));
            Assert.Equal(CrmRelationWriteResult.Success, await change(new(created.id, created.version, start, start.AddDays(30), role), actor));
            Assert.Equal(CrmRelationWriteResult.ConcurrencyConflict, await revoke(new(created.id, created.version), actor));
            var changed = await Latest(table, column, target);
            Assert.Equal(CrmRelationWriteResult.Success, await revoke(new(changed.id, changed.version), actor));
            Assert.Equal(CrmRelationWriteResult.ConcurrencyConflict, await revoke(new(changed.id, changed.version), actor));
            async Task<(Guid id, byte[] version)?> LatestOrNull()
            {
                await using var cmd = new SqlCommand($"SELECT TOP(1) Id,RowVersion FROM dbo.{table} WHERE CustomerId=@customer AND {column}=@target AND IsActive=1 AND CreatedBy=@actor ORDER BY CreatedAt DESC", cn);
                cmd.Parameters.AddWithValue("@customer", customer); cmd.Parameters.AddWithValue("@target", target); cmd.Parameters.AddWithValue("@actor", actor);
                await using var rd = await cmd.ExecuteReaderAsync();
                return await rd.ReadAsync() ? (rd.GetGuid(0), (byte[])rd[1]) : null;
            }
        }
        await Cycle("CustomerEmployeeAssignments", "EmployeeId", Id(21), db.CreateCustomerEmployeeAssignmentAsync, db.ChangeCustomerEmployeeAssignmentAsync, db.RevokeCustomerEmployeeAssignmentAsync, "OWNER");
        await Cycle("CustomerBranchShares", "BranchId", Id(3), db.CreateCustomerBranchShareAsync, db.ChangeCustomerBranchShareAsync, db.RevokeCustomerBranchShareAsync, null);
        await Cycle("CustomerServiceTeams", "TeamId", Id(5), db.CreateCustomerServiceTeamAsync, db.ChangeCustomerServiceTeamAsync, db.RevokeCustomerServiceTeamAsync, null);
        Assert.NotNull(await db.GetCrmRelationshipDetailAsync(customer, actor));
    }
}