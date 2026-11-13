extern alias admin;
using Microsoft.Extensions.Configuration;
using TAPortal.Shared;

namespace TACustomerPortal.Tests;

// Operator 2026-11-13: weak password list (';'), initial password 123456, forced change at sign-in, new password emailed.
public sealed class SafePasswordSecurityTests
{
    [Fact]
    public void WeakListIsCaseInsensitiveTrimmedAndAlwaysHoldsTheInitialPassword()
    {
        var s = new PasswordSecuritySettings(" abc123 ;Password@123;\nqwerty;;", "Khoitao#1");
        Assert.True(s.IsWeak("ABC123"));
        Assert.True(s.IsWeak("password@123"));
        Assert.True(s.IsWeak("Khoitao#1"));
        Assert.False(s.IsWeak("Xy7!kq2Lm"));
        Assert.False(s.IsWeak(""));
        Assert.Equal("abc123; Password@123; qwerty", PasswordSecuritySettings.Normalize(" abc123 ;Password@123;\nqwerty;;ABC123"));
    }

    [Fact]
    public void DefaultsHold123456AndTheCommonOnes()
    {
        var s = PasswordSecuritySettings.Default;
        Assert.Equal("123456", s.TemporaryPassword);
        foreach (var p in new[] { "123456", "12345678", "111111", "Password@123", "Admin@123", "qwerty" })
            Assert.True(s.IsWeak(p), p);
    }

    [Fact]
    public void ChosenPasswordsMustPassPolicyAndNotBeWeak_AssignedOnesMayBeWeakButMustChange()
    {
        var s = PasswordSecuritySettings.Default;
        Assert.False(s.ValidateNew("Password@123").IsValid);   // passes PasswordPolicy, but on the list
        Assert.False(s.ValidateNew("123456").IsValid);
        Assert.True(s.ValidateNew("Xy7!kq2Lm").IsValid);

        var weak = s.ValidateAssigned("123456");
        Assert.True(weak.IsValid);
        Assert.True(weak.MustChange);
        var strong = s.ValidateAssigned("Xy7!kq2Lm");
        Assert.True(strong.IsValid);
        Assert.False(strong.MustChange);
        Assert.False(s.ValidateAssigned("abc").IsValid);        // neither strong nor on the list
    }

    [Fact]
    public void TemporaryHashVerifiesWhileTheNormalHashStillEnforcesPolicy()
    {
        var service = new PasswordService();
        Assert.Throws<ArgumentException>(() => service.Hash("123456"));
        var hash = service.HashTemporary("123456");
        Assert.True(service.Verify("123456", hash));
        Assert.False(service.Verify("1234567", hash));
        Assert.Throws<ArgumentException>(() => service.HashTemporary(" "));
    }

    [Fact]
    public void StoreFallsBackToDefaultsWithoutADatabase()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = "INVALID: tests must never use SQL" }).Build();
        var s = new SqlPasswordSecurityStore(config).GetAsync().GetAwaiter().GetResult();
        Assert.True(s.IsWeak("123456"));
    }

    [Fact]
    public void PasswordEmailsCarryTheNewPasswordAndTheRightBrand()
    {
        var customer = TACustomerPortal.Web.Controllers.AccountController.PasswordChangedEmail("a@example.com", "Khách", "khach", "Xy7!kq2Lm");
        Assert.Contains("Xy7!kq2Lm", customer.TextBody);
        Assert.Contains("Hóa đơn tự động", customer.Subject);
        foreach (var text in new[] { customer.Subject, customer.TextBody, customer.HtmlBody })
        {
            Assert.DoesNotContain("T.A", text);
            Assert.DoesNotContain("Tingee", text, StringComparison.OrdinalIgnoreCase);
        }
        var staff = admin::TAPortal.Web.Controllers.AccountController.PasswordChangedEmail("Nhân viên", "nv", "Xy7!kq2Lm");
        Assert.Contains("Xy7!kq2Lm", staff);
        Assert.Contains("TAPortal", staff);
    }

    [Fact]
    public void AdminPortalSendsMustChangeAccountsToChangePassword()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var program = File.ReadAllText(Path.Combine(root, "src", "TAPortal.Web", "Program.cs"));
        Assert.Contains("AccountController.MustChangePasswordClaim", program);
        Assert.Contains("\"/account/changepassword\"", program);
        Assert.True(program.IndexOf("passwordChangeAllowed", StringComparison.Ordinal) < program.IndexOf("app.UseAuthorization()", StringComparison.Ordinal));
    }
}
