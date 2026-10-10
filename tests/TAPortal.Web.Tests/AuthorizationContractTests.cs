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
    public void ProtectedControllers_UseDatabaseFunctionActionPolicy(Type controllerType, string actionKey)
    {
        var attribute = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(PermissionPolicies.FunctionActionPrefix + actionKey, attribute.Policy);
    }
}
