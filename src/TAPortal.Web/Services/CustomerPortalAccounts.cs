using TAPortal.Shared;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Customer Workspace "Tài khoản Portal" tab. Thin seam over the existing MembershipAdminStore (SYS_ADMIN re-checked in SQL,
// audit in the same transaction, duplicate membership handled by UPDATE-or-INSERT) and the existing user list, so the
// controller's customer-scoping rules can be tested without SQL. No new SQL, schema or business rule.
public interface ICustomerPortalAccounts
{
    Task<List<MembershipAdminRow>> ListAsync(Guid actor, Guid customerId);
    // Every membership of every customer (Customers → "Tài khoản Portal" tab). SYS_ADMIN re-checked in SQL.
    Task<List<MembershipAdminRow>> ListAllAsync(Guid actor);
    Task SaveAsync(MembershipInput input, Guid actor, string? passwordHash);
    Task SetActiveAsync(Guid userId, Guid customerId, bool active, Guid actor);
    // Accounts that may be granted Customer Portal access: TA staff are excluded. A TA staff account is a user that holds
    // at least one TAPortal role (dbo.UserRoles); there is no user-type column, see the change record.
    Task<List<PortalUser>> CandidateUsersAsync();
    Task<List<CustomerRow>> CustomersAsync();
}

public sealed class SqlCustomerPortalAccounts(MembershipAdminStore store, PortalDb db) : ICustomerPortalAccounts
{
    public Task<List<MembershipAdminRow>> ListAsync(Guid actor, Guid customerId) => store.List(actor, customerId);
    public Task<List<MembershipAdminRow>> ListAllAsync(Guid actor) => store.List(actor, null);
    public Task SaveAsync(MembershipInput input, Guid actor, string? passwordHash) => store.Save(input, actor, passwordHash);
    public Task SetActiveAsync(Guid userId, Guid customerId, bool active, Guid actor) => store.SetActive(userId, customerId, active, actor);
    public Task<List<CustomerRow>> CustomersAsync() => db.GetCustomersAsync();

    public async Task<List<PortalUser>> CandidateUsersAsync() => PortalCandidates.Filter(await db.GetUsersAsync(), await db.GetUserRoleMapAsync());
}

public static class PortalCandidates
{
    // A user holding any TAPortal role is TA staff and is never offered for Customer Portal access.
    public static List<PortalUser> Filter(IEnumerable<PortalUser> users, IReadOnlyDictionary<Guid, List<string>> roleMap) =>
        users.Where(u => !roleMap.TryGetValue(u.Id, out var roles) || roles.Count == 0).ToList();
}
