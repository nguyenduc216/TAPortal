using Microsoft.AspNetCore.Authorization;
using TAPortal.Web.Controllers;
using TAPortal.Web.Security;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class AuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(UsersController), "Users.Index")]
    [InlineData(typeof(RolesController), "Roles.Index")]
    [InlineData(typeof(CustomersController), "Customers.Index")]
    [InlineData(typeof(MenusController), "Menus.Index")]
    [InlineData(typeof(DepartmentsController), "Departments.Index")]
    [InlineData(typeof(TeamsController), "Teams.Index")]
    public void ProtectedControllers_UseDatabaseFunctionActionPolicy(Type controllerType, string actionKey)
    {
        var attribute = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(PermissionPolicies.FunctionActionPrefix + actionKey, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(DepartmentsController.Create), "Departments.Create")]
    [InlineData(nameof(DepartmentsController.Update), "Departments.Update")]
    [InlineData(nameof(DepartmentsController.ToggleStatus), "Departments.Update")]
    [InlineData(nameof(DepartmentsController.Delete), "Departments.Delete")]
    public void DepartmentMutations_EnforceDatabaseFunctionActionPolicy(string methodName, string actionKey)
    {
        var method = typeof(DepartmentsController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(PermissionPolicies.FunctionActionPrefix + actionKey, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(TeamsController.Create), "Teams.Create")]
    [InlineData(nameof(TeamsController.Update), "Teams.Update")]
    [InlineData(nameof(TeamsController.ToggleStatus), "Teams.Update")]
    [InlineData(nameof(TeamsController.Delete), "Teams.Delete")]
    public void TeamMutations_EnforceDatabaseFunctionActionPolicy(string methodName,string actionKey)
    {
        var method=typeof(TeamsController).GetMethod(methodName)!;
        var attribute=Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>());
        Assert.Equal(PermissionPolicies.FunctionActionPrefix+actionKey,attribute.Policy);
    }
}
