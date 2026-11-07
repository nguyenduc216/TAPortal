using System.Security.Claims;

namespace TAPortal.Web.Security;

public sealed record AdminPermission(string Code, string Name, string Action);

public sealed record AdminPermissionModule(string Key, string Name, IReadOnlyList<AdminPermission> Permissions);

// Single source of truth for every Admin portal function that can be granted to a role. The same codes are inserted by
// database/migrations/028-admin-permission-catalogue.sql (SafeAdminPermissionTests keeps both lists identical).
// Only these codes are shown and assignable on the role screen; other rows of dbo.Permissions (e.g. HR.* of another
// system sharing the database) are never shown and never changed.
public static class AdminPermissions
{
    public const string SystemAdminRole = "SYS_ADMIN";

    public const string DashboardView = "DASHBOARD.VIEW";

    public const string CustomersView = "CRM.CUSTOMERS.VIEW";
    public const string CustomersCreate = "CRM.CUSTOMERS.CREATE";
    public const string CustomersUpdate = "CRM.CUSTOMERS.UPDATE";
    public const string CustomerSecretsReveal = "CRM.CUSTOMERS.SECRETS.REVEAL";
    public const string CustomerPortalManage = "CUSTOMERS.PORTAL.MANAGE";

    public const string ReconciliationView = "PAYMENTS.RECONCILIATION.VIEW";
    public const string ReconciliationManage = "PAYMENTS.RECONCILIATION.MANAGE";

    public const string PackagesView = "PACKAGES.VIEW";
    public const string PackagesManage = "PACKAGES.MANAGE";
    public const string PurchasesView = "PACKAGES.PURCHASES.VIEW";
    public const string PurchasesManage = "PACKAGES.PURCHASES.MANAGE";

    public const string IntegrationsSandbox = "INTEGRATIONS.SANDBOX";
    public const string IntegrationsLogsView = "INTEGRATIONS.LOGS.VIEW";
    public const string NotificationsManage = "NOTIFICATIONS.MANAGE";

    public const string UsersView = "AUTH.USERS.VIEW";
    public const string UsersCreate = "AUTH.USERS.CREATE";
    public const string UsersUpdate = "AUTH.USERS.UPDATE";
    public const string UsersDelete = "AUTH.USERS.DELETE";
    public const string RolesView = "AUTH.ROLES.VIEW";
    public const string RolesManage = "AUTH.ROLES.MANAGE";
    public const string MenusManage = "SYSTEM.MENUS.MANAGE";

    // Policy names for [Authorize(Policy = ...)] (PermissionPolicyProvider resolves "PERM:<code>").
    public static class Policy
    {
        public const string DashboardView = PermissionPolicies.Prefix + AdminPermissions.DashboardView;
        public const string CustomersView = PermissionPolicies.Prefix + AdminPermissions.CustomersView;
        public const string CustomersCreate = PermissionPolicies.Prefix + AdminPermissions.CustomersCreate;
        public const string CustomersUpdate = PermissionPolicies.Prefix + AdminPermissions.CustomersUpdate;
        public const string CustomerPortalManage = PermissionPolicies.Prefix + AdminPermissions.CustomerPortalManage;
        public const string ReconciliationView = PermissionPolicies.Prefix + AdminPermissions.ReconciliationView;
        public const string ReconciliationManage = PermissionPolicies.Prefix + AdminPermissions.ReconciliationManage;
        public const string PackagesView = PermissionPolicies.Prefix + AdminPermissions.PackagesView;
        public const string PackagesManage = PermissionPolicies.Prefix + AdminPermissions.PackagesManage;
        public const string PurchasesView = PermissionPolicies.Prefix + AdminPermissions.PurchasesView;
        public const string PurchasesManage = PermissionPolicies.Prefix + AdminPermissions.PurchasesManage;
        public const string IntegrationsSandbox = PermissionPolicies.Prefix + AdminPermissions.IntegrationsSandbox;
        public const string IntegrationsLogsView = PermissionPolicies.Prefix + AdminPermissions.IntegrationsLogsView;
        public const string NotificationsManage = PermissionPolicies.Prefix + AdminPermissions.NotificationsManage;
        public const string UsersView = PermissionPolicies.Prefix + AdminPermissions.UsersView;
        public const string UsersCreate = PermissionPolicies.Prefix + AdminPermissions.UsersCreate;
        public const string UsersUpdate = PermissionPolicies.Prefix + AdminPermissions.UsersUpdate;
        public const string UsersDelete = PermissionPolicies.Prefix + AdminPermissions.UsersDelete;
        public const string RolesView = PermissionPolicies.Prefix + AdminPermissions.RolesView;
        public const string RolesManage = PermissionPolicies.Prefix + AdminPermissions.RolesManage;
        public const string MenusManage = PermissionPolicies.Prefix + AdminPermissions.MenusManage;
    }

    public static readonly IReadOnlyList<AdminPermissionModule> Modules =
    [
        new("DASHBOARD", "Tổng quan",
        [
            new(DashboardView, "Xem trang tổng quan", "VIEW")
        ]),
        new("CUSTOMERS", "Khách hàng",
        [
            new(CustomersView, "Xem danh sách và hồ sơ khách hàng", "VIEW"),
            new(CustomersCreate, "Thêm khách hàng (kể cả nhập từ Excel)", "CREATE"),
            new(CustomersUpdate, "Sửa thông tin, hồ sơ, logo, người đại diện, tài khoản hệ thống, liên kết thanh toán", "UPDATE"),
            new(CustomerSecretsReveal, "Xem mật khẩu tài khoản hệ thống của khách hàng", "REVEAL"),
            new(CustomerPortalManage, "Quản lý tài khoản Customer Portal của khách hàng", "MANAGE")
        ]),
        new("PAYMENTS", "Đối soát giao dịch",
        [
            new(ReconciliationView, "Xem giao dịch cần đối soát", "VIEW"),
            new(ReconciliationManage, "Gán đơn, chấp nhận, đóng giao dịch đối soát", "MANAGE")
        ]),
        new("PACKAGES", "Gói dịch vụ",
        [
            new(PackagesView, "Xem danh sách gói dịch vụ", "VIEW"),
            new(PackagesManage, "Thêm, sửa, bật/tắt gói dịch vụ", "MANAGE"),
            new(PurchasesView, "Xem giao dịch mua gói", "VIEW"),
            new(PurchasesManage, "Tạo đơn mua gói, xác nhận thanh toán, hủy giao dịch mua gói", "MANAGE")
        ]),
        new("INTEGRATIONS", "Tích hợp",
        [
            new(IntegrationsSandbox, "Dùng Tingee Sandbox và Hóa đơn Sandbox", "USE"),
            new(IntegrationsLogsView, "Xem nhật ký gọi Tingee", "VIEW"),
            new(NotificationsManage, "Cài đặt thông báo", "MANAGE")
        ]),
        new("SYSTEM", "Hệ thống",
        [
            new(UsersView, "Xem danh sách người dùng", "VIEW"),
            new(UsersCreate, "Tạo người dùng", "CREATE"),
            new(UsersUpdate, "Sửa, khóa/mở người dùng", "UPDATE"),
            new(UsersDelete, "Xóa (mềm) người dùng", "DELETE"),
            new(RolesView, "Xem vai trò và phân quyền", "VIEW"),
            new(RolesManage, "Tạo, sửa, xóa vai trò; gán vai trò cho người dùng", "MANAGE"),
            new(MenusManage, "Xem cấu hình menu", "MANAGE")
        ])
    ];

    public static readonly IReadOnlyList<AdminPermission> All = Modules.SelectMany(m => m.Permissions).ToList();

    public static readonly IReadOnlySet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsCatalogued(string? code) => code is not null && Codes.Contains(code);

    public static bool IsSystemAdmin(ClaimsPrincipal user) => user.IsInRole(SystemAdminRole);

    // Same rule as PermissionHandler: SYS_ADMIN has every permission; otherwise a "permission" claim (refreshed per request).
    public static bool Has(ClaimsPrincipal user, string code) =>
        IsSystemAdmin(user) || user.Claims.Any(c => c.Type == "permission" && string.Equals(c.Value, code, StringComparison.OrdinalIgnoreCase));
}

public sealed record AdminLandingPage(string Permission, string Controller, string Action);

// Pages in sidebar order. Used for the post-login redirect: dashboard when allowed, else the first page the user may open.
public static class AdminNavigation
{
    public static readonly IReadOnlyList<AdminLandingPage> Pages =
    [
        new(AdminPermissions.DashboardView, "Home", "Index"),
        new(AdminPermissions.CustomersView, "Customers", "Index"),
        new(AdminPermissions.ReconciliationView, "PaymentReconciliation", "Index"),
        new(AdminPermissions.PackagesView, "ServicePackages", "Index"),
        new(AdminPermissions.PurchasesView, "ServicePackages", "Purchases"),
        new(AdminPermissions.IntegrationsSandbox, "TingeeSandbox", "Index"),
        new(AdminPermissions.IntegrationsLogsView, "TingeeLogs", "Index"),
        new(AdminPermissions.NotificationsManage, "NotificationSettings", "Index"),
        new(AdminPermissions.UsersView, "Users", "Index"),
        new(AdminPermissions.RolesView, "Roles", "Index"),
        new(AdminPermissions.MenusManage, "Menus", "Index")
    ];

    public static AdminLandingPage? FirstAllowed(ClaimsPrincipal user) => Pages.FirstOrDefault(p => AdminPermissions.Has(user, p.Permission));

    public static IEnumerable<AdminLandingPage> AllowedPages(ClaimsPrincipal user) => Pages.Where(p => AdminPermissions.Has(user, p.Permission));
}
