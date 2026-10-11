using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TAPortal.Web.Services;

namespace TAPortal.Web.Security;

public static class PermissionPolicies
{
    public const string Prefix = "PERM:";
    public const string FunctionActionPrefix = "ACTION:";
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class FunctionActionRequirement(string actionKey) : IAuthorizationRequirement
{
    public string ActionKey { get; } = actionKey;
}

public sealed class PermissionHandler(PortalDb db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-001
        // Authorization is resolved from current database assignments; role names and cookie permission snapshots never grant access.
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await db.HasPermissionAsync(userId, requirement.Permission))
            context.Succeed(requirement);
    }
}

public sealed class FunctionActionHandler(PortalDb db) : AuthorizationHandler<FunctionActionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FunctionActionRequirement requirement)
    {
        // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
        // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-001
        // Unknown or unmapped action keys fail closed; any mapped effective permission grants the known action.
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await db.HasFunctionActionAsync(userId, requirement.ActionKey))
            context.Succeed(requirement);
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicies.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var permission = policyName[PermissionPolicies.Prefix.Length..];
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)).Build();
        }

        if (policyName.StartsWith(PermissionPolicies.FunctionActionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var actionKey = policyName[PermissionPolicies.FunctionActionPrefix.Length..];
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new FunctionActionRequirement(actionKey)).Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
