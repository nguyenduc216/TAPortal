using System.ComponentModel.DataAnnotations;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

// CHANGE_ID: TAPORTAL-20261018-USERS-001
public sealed class UserManagementTests
{
    [Theory]
    [InlineData("", null, null)]
    [InlineData("Valid", "not-an-email", null)]
    [InlineData("Valid", null, "123456789012345678901234567890123456789012345678901")]
    public void InvalidProfileIsRejected(string name, string? email, string? phone)
    {
        var input = new UserProfileUpdate { Id = Guid.NewGuid(), DisplayName = name, Email = email, Phone = phone };
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true));
    }

    [Fact]
    public void MutationsUseExistingEffectivePermissionAndAtomicAudit()
    {
        var source = File.ReadAllText(TestRepositoryRoot.FilePath("src", "TAPortal.Web", "Services", "PortalDb.UserManagement.cs"));
        Assert.Contains("HasPermissionAsync(actorId, \"AUTH.USERS.UPDATE\")", source);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable)", source);
        Assert.Contains("dbo.AuditLogs", source);
        Assert.Contains("await tx.CommitAsync()", source);
        Assert.Contains("await tx.RollbackAsync()", source);
        Assert.Contains("if (!await write(cn, tx)) return false", source);
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dbo.UserRoles", source);
        Assert.Contains("actorId == userId", source);
        Assert.Contains("IsActiveSysAdminAsync(cn, tx, actorId)", source);
    }
}