using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-014
public sealed class CrmOrganizationCreateTests
{
    private static PortalDb Db() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=localhost;Database=unused;" })
        .Build(), NullLogger<PortalDb>.Instance);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidRequestFailsWithoutOpeningSql(bool share)
    {
        var db = Db();
        var today = DateOnly.FromDateTime(DateTime.Today);
        async Task<CrmRelationWriteResult> Create(CrmRelationWriteRequest request, Guid actor) => share
            ? await db.CreateCustomerBranchShareAsync(request, actor)
            : await db.CreateCustomerServiceTeamAsync(request, actor);
        Assert.Equal(CrmRelationWriteResult.Invalid, await Create(new(Guid.Empty, Guid.NewGuid(), today, null), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Create(new(Guid.NewGuid(), Guid.Empty, today, null), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Create(new(Guid.NewGuid(), Guid.NewGuid(), today, null), Guid.Empty));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Create(new(Guid.NewGuid(), Guid.NewGuid(), today, today.AddDays(-1)), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await Create(new(Guid.NewGuid(), Guid.NewGuid(), today, null, "OWNER"), Guid.NewGuid()));
    }
}