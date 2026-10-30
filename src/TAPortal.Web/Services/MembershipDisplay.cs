using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

public sealed record MembershipEffect(bool Effective, string Label, string? Reason);

// Mirrors the Customer Portal login/membership query: membership active, user login-eligible, customer active and not deleted.
// userStatus is MembershipAdminRow.Status ('DELETED', 'LOCKED' for IsActive=0, otherwise Users.Status).
public static class MembershipDisplay
{
    public static MembershipEffect Evaluate(bool membershipActive, string userStatus, CustomerRow? customer)
    {
        if (!membershipActive) return new(false, "Đã thu hồi", null);
        var reasons = new List<string>();
        if (userStatus == "DELETED") reasons.Add("tài khoản đã xóa");
        else if (userStatus == "LOCKED") reasons.Add("tài khoản đã vô hiệu hóa");
        else if (userStatus != AccountStatusPolicy.ActiveStatus) reasons.Add($"tài khoản bị khóa ({userStatus})");
        if (customer is null) reasons.Add("doanh nghiệp không còn tồn tại");
        else if (!(customer.IsActive && customer.Status == AccountStatusPolicy.ActiveStatus)) reasons.Add("doanh nghiệp ngừng hoạt động");
        return reasons.Count == 0 ? new(true, "Đang có hiệu lực", null) : new(false, "Không hiệu lực", string.Join("; ", reasons));
    }
}
