namespace TAPortal.Web.Security;

internal static class AuthorizationSemantics
{
    // FEATURE_ID: TAPORTAL-CORE-AUTH-MENU
    // CHANGE_ID: TAPORTAL-20261010-CORE-AUTH-MENU-002
    // Keep DENY precedence and mapped-resource behavior explicit and shared by runtime authorization/menu filtering.
    internal static bool IsPermissionEffective(bool roleAllowed, string? userEffect) =>
        string.Equals(userEffect, "DENY", StringComparison.OrdinalIgnoreCase)
            ? false
            : string.Equals(userEffect, "ALLOW", StringComparison.OrdinalIgnoreCase) || roleAllowed;

    internal static bool HasAnyMappedPermission(
        IReadOnlySet<string> effectivePermissions,
        IEnumerable<string> mappedPermissions,
        bool hasMappings,
        bool allowWhenUnmapped) =>
        hasMappings
            ? mappedPermissions.Any(effectivePermissions.Contains)
            : allowWhenUnmapped;
}
