using System.ComponentModel.DataAnnotations;
using TAPortal.Web.Models;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class Phase6MigrationTests
{
    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-006 — verify MVC-equivalent normalization and validation.
    [Fact]
    public void DepartmentAndTeamCodes_NormalizeLikeMvc()
    {
        Assert.Equal("HR", DepartmentRules.NormalizeCode(" hr "));
        Assert.Equal("OPS", TeamRules.NormalizeCode(" ops "));
        var model = new DepartmentCreateVm();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true));
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-006 — verify hierarchy safeguards used by Team writes.
    [Fact]
    public void TeamRules_RejectCrossDepartmentParentAndCycles()
    {
        var department = Guid.NewGuid();
        var parent = new TeamParentOption(Guid.NewGuid(), Guid.NewGuid(), "P", "Parent", null);
        Assert.False(TeamRules.IsParentInDepartment(department, parent));
        var team = Guid.NewGuid(); var other = Guid.NewGuid();
        Assert.True(TeamRules.WouldCreateCycle(team, other, new Dictionary<Guid, Guid?> { [other] = team }));
        Assert.False(TeamRules.WouldCreateCycle(team, null, new Dictionary<Guid, Guid?>()));
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-006 — ensure write models cannot overpost identity or authorization fields.
    [Fact]
    public void Phase6WriteModels_DoNotExposeIdentityOrPermissionFields()
    {
        foreach (var type in new[] { typeof(DepartmentCreateVm), typeof(TeamCreateVm) })
        {
            var names = type.GetProperties().Select(x => x.Name).ToHashSet();
            Assert.DoesNotContain("Id", names); Assert.DoesNotContain("CreatedBy", names);
            Assert.DoesNotContain("IsDeleted", names); Assert.DoesNotContain("Permissions", names);
        }
    }

    // CHANGE_ID: TAPORTAL-20261011-BLAZOR-MIGRATION-006 — verify the trial route source contracts and MVC ownership remain distinct.
    [Fact]
    public void TrialRoutes_AreDedicatedToBlazorComponents()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TAPortal.Web", "Components", "Pages"));
        Assert.Contains("@page \"/blazor/customers\"", File.ReadAllText(Path.Combine(root, "Customers.razor")));
        Assert.Contains("@page \"/blazor/hr/departments\"", File.ReadAllText(Path.Combine(root, "HrDepartments.razor")));
        Assert.Contains("@page \"/blazor/hr/teams\"", File.ReadAllText(Path.Combine(root, "HrTeams.razor")));
    }
}