using TAPortal.Shared;

namespace TAPortal.Web.Models;

// Customer Workspace: one page per customer with tabs. To add a module later (services, documents, tasks, reports,
// module settings) add a controller action that renders the workspace header and append a tab here — only once the
// module actually works; unfinished modules are not listed.
public sealed record CustomerWorkspaceTab(string Key, string Label, string Icon, string Action);

public static class CustomerWorkspace
{
    public const string Overview = "overview";
    public const string Info = "info";
    public const string Portal = "portal";
    public const string Tingee = "tingee";

    public static readonly IReadOnlyList<CustomerWorkspaceTab> Tabs =
    [
        new(Overview, "Tổng quan", "layout-dashboard", "Details"),
        new(Info, "Thông tin khách hàng", "id", "Edit"),
        new(Portal, "Tài khoản Portal", "users-group", "PortalAccounts"),
        new(Tingee, "Thanh toán Tingee", "building-bank", "Tingee"),
    ];
}

// Header data always comes from the stored customer (not from a posted form), so a failed edit never relabels the workspace.
public sealed record CustomerWorkspaceHeader(Guid Id, string Code, string Name, bool IsActive, string Status, string ActiveTab)
{
    public bool Operational => IsActive && Status == "ACTIVE";
    public static CustomerWorkspaceHeader From(CustomerEditVm c, string tab) => new(c.Id, c.Code, c.Name, c.IsActive, c.Status, tab);
}

// Portal account counts for the overview tab; null when the membership list could not be read.
public sealed record PortalAccountSummary(int Total, int Effective, int Revoked);

public sealed record CustomerOverviewVm(CustomerEditVm Customer, PortalAccountSummary? PortalAccounts);

public sealed record CustomerPortalAccountsVm(CustomerEditVm Customer, IReadOnlyList<MembershipAdminRow>? Accounts, IReadOnlyList<PortalUser> Users);

// Customers → "Tài khoản Portal" tab; Accounts is null when the membership list could not be read.
public sealed record CustomerAccountsOverviewVm(IReadOnlyList<MembershipAdminRow>? Accounts, IReadOnlyList<CustomerRow> Customers);
