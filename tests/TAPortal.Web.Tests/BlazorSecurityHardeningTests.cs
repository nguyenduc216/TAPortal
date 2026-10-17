using Xunit;

namespace TAPortal.Web.Tests;

public sealed class BlazorSecurityHardeningTests
{
    [Fact]
    public void TrialWrites_RevalidateServerIdentityAndPermissions()
    {
        var root=RepositoryRoot();
        var profile=File.ReadAllText(Path.Combine(root,"src","TAPortal.Web","Components","Pages","AccountProfile.razor"));
        var departments=File.ReadAllText(Path.Combine(root,"src","TAPortal.Web","Components","Pages","HrDepartments.razor"));
        var teams=File.ReadAllText(Path.Combine(root,"src","TAPortal.Web","Components","Pages","HrTeams.razor"));
        Assert.Contains("GetAuthenticationStateAsync",profile); Assert.Contains("currentId != _user.Id",profile);
        foreach(var key in new[]{"Departments.Create","Departments.Update","Departments.Delete"}) Assert.Contains($"HasFunctionActionAsync(id.Value, \"{key}\")",departments);
        foreach(var key in new[]{"Teams.Create","Teams.Update","Teams.Delete"}) Assert.Contains($"HasFunctionActionAsync(id.Value, \"{key}\")",teams);
    }

    [Fact]
    public void PasswordMutation_FailsClosedForDisabledAccount()
    {
        var source=File.ReadAllText(Path.Combine(RepositoryRoot(),"src","TAPortal.Web","Services","PortalDb.cs"));
        Assert.Contains("UPDATE dbo.Users SET PasswordHash",source);
        Assert.Contains("WHERE Id=@id AND IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'",source);
        Assert.Contains("return await cmd.ExecuteNonQueryAsync() == 1",source);
    }

    [Fact]
    public void Login_DoesNotAdvertiseBootstrapUsername()
    {
        var source=File.ReadAllText(Path.Combine(RepositoryRoot(),"src","TAPortal.Web","Views","Account","Login.cshtml"));
        Assert.DoesNotContain("Tài khoản khởi tạo",source,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<strong>admin</strong>",source,StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        return TestRepositoryRoot.Resolve();
    }
}
