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

    /// <summary>MEMBER (migration 029): permissions per function, set by the company on the Customer Portal; never offered for assignment here.</summary>
    public const string Member = "MEMBER", MemberLabel = "Thành viên (phân quyền riêng)";

    public static string Label(string? code) => string.Equals(code, Member, StringComparison.OrdinalIgnoreCase) ? MemberLabel
        : Roles.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)).Label ?? code ?? "";
}
