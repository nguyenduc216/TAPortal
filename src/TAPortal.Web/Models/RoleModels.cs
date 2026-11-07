using TAPortal.Web.Services;

namespace TAPortal.Web.Models;

public sealed record RolesIndexVm(IReadOnlyList<RoleListItem> Roles, string? Error);

// Role is null on the create screen. Available: catalogue codes present in dbo.Permissions (others are shown disabled).
public sealed record RoleEditVm(RoleDetail? Role, RoleInput Input, IReadOnlySet<string> Available, bool ReadOnly)
{
    public bool IsCreate => Role is null;
    public bool IsSystemAdmin => Role?.IsSystemAdmin == true;
    public bool Granted(string code) => IsSystemAdmin || (Input.Permissions ?? []).Contains(code, StringComparer.OrdinalIgnoreCase);
}
