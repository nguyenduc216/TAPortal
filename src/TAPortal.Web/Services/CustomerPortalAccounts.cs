using TAPortal.Shared;
using TAPortal.Web.Models;

namespace TAPortal.Web.Services;

// Customer Workspace "Tài khoản Portal" tab. Thin seam over the existing MembershipAdminStore (SYS_ADMIN re-checked in SQL,
// audit in the same transaction, duplicate membership handled by UPDATE-or-INSERT) and the existing user list, so the
// controller's customer-scoping rules can be tested without SQL. No new SQL, schema or business rule.
public interface ICustomerPortalAccounts
{
    Task<List<MembershipAdminRow>> ListAsync(Guid actor, Guid customerId);
    Task SaveAsync(MembershipInput input, Guid actor, string? passwordHash);
    Task SetActiveAsync(Guid userId, Guid customerId, bool active, Guid actor);
    Task<List<PortalUser>> UsersAsync();
}

public sealed class SqlCustomerPortalAccounts(MembershipAdminStore store, PortalDb db) : ICustomerPortalAccounts
{
    public Task<List<MembershipAdminRow>> ListAsync(Guid actor, Guid customerId) => store.List(actor, customerId);
    public Task SaveAsync(MembershipInput input, Guid actor, string? passwordHash) => store.Save(input, actor, passwordHash);
    public Task SetActiveAsync(Guid userId, Guid customerId, bool active, Guid actor) => store.SetActive(userId, customerId, active, actor);
    public Task<List<PortalUser>> UsersAsync() => db.GetUsersAsync();
}
