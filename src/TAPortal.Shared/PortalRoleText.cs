namespace TAPortal.Shared;

// Display names for Customer Portal membership roles. The stored/posted values stay OWNER/ADMIN/VIEWER (MembershipInput
// accepts only these); screens show the Vietnamese name only.
public static class PortalRoleText
{
    public static readonly IReadOnlyList<(string Code, string Label)> Roles =
    [
        ("VIEWER", "Chỉ xem"),
        ("ADMIN", "Quản trị doanh nghiệp"),
        ("OWNER", "Chủ sở hữu"),
    ];

    public static string Label(string? code) => Roles.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)).Label ?? code ?? "";
}
