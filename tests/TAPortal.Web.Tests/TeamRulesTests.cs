using TAPortal.Web.Models;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class TeamRulesTests
{
    [Theory]
    [InlineData(" kt-sme ","KT-SME")]
    [InlineData("Sales-01","SALES-01")]
    public void NormalizeCode_TrimsAndUppercases(string input,string expected) => Assert.Equal(expected,TeamRules.NormalizeCode(input));

    [Fact]
    public void Parent_MustBelongToSameDepartment()
    {
        var department=Guid.NewGuid();
        Assert.True(TeamRules.IsParentInDepartment(department,null));
        Assert.True(TeamRules.IsParentInDepartment(department,new(Guid.NewGuid(),department,"A","A",null)));
        Assert.False(TeamRules.IsParentInDepartment(department,new(Guid.NewGuid(),Guid.NewGuid(),"B","B",null)));
    }

    [Fact]
    public void Hierarchy_RejectsSelfAndRecursiveCycle()
    {
        var a=Guid.NewGuid(); var b=Guid.NewGuid(); var c=Guid.NewGuid();
        var parents=new Dictionary<Guid,Guid?>{{a,b},{b,c},{c,null}};
        Assert.True(TeamRules.WouldCreateCycle(a,a,parents));
        Assert.True(TeamRules.WouldCreateCycle(c,a,parents));
        Assert.False(TeamRules.WouldCreateCycle(a,c,parents));
    }

    [Fact]
    public void RowVersion_RejectsInvalidBrowserValue() => Assert.False(TeamRules.TryDecodeRowVersion("stale-invalid",out _));
}
