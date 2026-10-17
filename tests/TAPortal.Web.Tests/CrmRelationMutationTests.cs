using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-015
public sealed class CrmRelationMutationTests
{
    private static PortalDb Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=localhost;Database=unused;" })
        .Build(), NullLogger<PortalDb>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidChangeAndRevokeFailBeforeSql(int kind)
    {
        var db = Db();
        var today = DateOnly.FromDateTime(DateTime.Today);
        Task<CrmRelationWriteResult> Change(CrmRelationChangeRequest r, Guid actor) => kind switch
        {
            0 => db.ChangeCustomerEmployeeAssignmentAsync(r, actor),
            1 => db.ChangeCustomerBranchShareAsync(r, actor),
            _ => db.ChangeCustomerServiceTeamAsync(r, actor)
        };
        Task<CrmRelationWriteResult> Revoke(CrmRelationRevokeRequest r, Guid actor) => kind switch
        {
            0 => db.RevokeCustomerEmployeeAssignmentAsync(r, actor),
            1 => db.RevokeCustomerBranchShareAsync(r, actor),
            _ => db.RevokeCustomerServiceTeamAsync(r, actor)
        };
        var role = kind == 0 ? "OWNER" : null;
        Assert.Equal(CrmRelationWriteResult.Invalid, await Change(new(Guid.Empty, new byte[8], today, null, role), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Change(new(Guid.NewGuid(), new byte[7], today, null, role), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Change(new(Guid.NewGuid(), new byte[8], today, today.AddDays(-1), role), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Change(new(Guid.NewGuid(), new byte[8], today, null, role), Guid.Empty));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Revoke(new(Guid.Empty, new byte[8]), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Revoke(new(Guid.NewGuid(), new byte[9]), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Revoke(new(Guid.NewGuid(), new byte[8]), Guid.Empty));
    }
}