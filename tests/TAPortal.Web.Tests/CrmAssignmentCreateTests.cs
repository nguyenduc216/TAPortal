using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261015-CRM-PHASE1-013
public sealed class CrmAssignmentCreateTests
{
    private static PortalDb CreateDb() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "Server=localhost;Database=unused;" })
        .Build(), NullLogger<PortalDb>.Instance);

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    [InlineData("owner")]
    public async Task InvalidRoleFailsBeforeOpeningSql(string role)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = await CreateDb().CreateCustomerEmployeeAssignmentAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), today, null, role), Guid.NewGuid());
        Assert.Equal(CrmRelationWriteResult.Invalid, result);
    }

    [Fact]
    public async Task InvalidPeriodOrIdentityFailsBeforeOpeningSql()
    {
        var db = CreateDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerEmployeeAssignmentAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), today, today.AddDays(-1), "OWNER"), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerEmployeeAssignmentAsync(
            new(Guid.Empty, Guid.NewGuid(), today, null, "OWNER"), Guid.NewGuid()));
        Assert.Equal(CrmRelationWriteResult.Invalid, await db.CreateCustomerEmployeeAssignmentAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), today, null, "OWNER"), Guid.Empty));
    }
}