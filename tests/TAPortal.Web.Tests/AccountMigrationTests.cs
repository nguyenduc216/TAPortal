using System.ComponentModel.DataAnnotations;
using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class AccountMigrationTests
{
    [Fact]
    public void ProfileEditVm_RejectsInvalidEmailAndBlankDisplayName()
    {
        var model = new ProfileEditVm { DisplayName = " ", Email = "not-an-email" };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(ProfileEditVm.DisplayName)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(ProfileEditVm.Email)));
    }

    [Fact]
    public void ProfileEditVm_DoesNotExposeIdentityOrAuthorizationFields()
    {
        var names = typeof(ProfileEditVm).GetProperties().Select(property => property.Name).ToHashSet();

        Assert.DoesNotContain("Id", names);
        Assert.DoesNotContain("Username", names);
        Assert.DoesNotContain("Roles", names);
        Assert.DoesNotContain("PasswordHash", names);
    }

    [Fact]
    public void PasswordChange_UsesExistingPolicyAndPbkdf2Service()
    {
        var passwords = new PasswordService();
        const string value = "Valid-Test!123";
        var hash = passwords.Hash(value);

        Assert.True(PasswordPolicy.Validate(value).IsValid);
        Assert.True(passwords.Verify(value, hash));
        Assert.False(passwords.Verify("Wrong-Test!123", hash));
        Assert.False(PasswordPolicy.Validate("short").IsValid);
    }
}