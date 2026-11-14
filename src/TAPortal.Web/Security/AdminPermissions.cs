using System.Security.Claims;

namespace TAPortal.Web.Security;

public sealed record AdminPermission(string Code, string Name, string Action);

public sealed record AdminPermissionModule(string Key, string Name, IReadOnlyList<AdminPermission> Permissions);

// Single source of truth for every Admin portal function that can be granted to a role. The codes of every module except
// "HR" are inserted by database/migrations/028-admin-permission-catalogue.sql (SafeAdminPermissionTests keeps both lists
// identical). The "HR" module (Nhân sự) uses rows that already exist in dbo.Permissions (HR core bootstrap of TAPortalSys,
// created outside the repository migrations), so it needs no migration. Only these codes are shown and assignable on the
// role screen; other rows of dbo.Permissions (e.g. HR.EMPLOYEE_DOCUMENT.*, HR.SPECIALIZATION.*) are never shown or changed.
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
    public const string UsersImpersonate = "AUTH.USERS.IMPERSONATE";   // "Truy cập hộ" (migration 042)
    public const string RolesView = "AUTH.ROLES.VIEW";
    public const string RolesManage = "AUTH.ROLES.MANAGE";
    public const string MenusManage = "SYSTEM.MENUS.MANAGE";

    // Nhân sự (HR core rows of dbo.Permissions; not inserted by migration 028).
    public const string HrModuleKey = "HR";
    public const string EmployeesView = "HR.EMPLOYEE.VIEW";
    public const string EmployeesCreate = "HR.EMPLOYEE.CREATE";
    public const string EmployeesUpdate = "HR.EMPLOYEE.UPDATE";
    public const string EmployeesDelete = "HR.EMPLOYEE.DELETE";
    public const string EmployeesChangeStatus = "HR.EMPLOYEE.CHANGE_STATUS";
    public const string EmployeesChangeAssignment = "HR.EMPLOYEE.CHANGE_ASSIGNMENT";
    public const string EmployeesAccountLink = "HR.EMPLOYEE.ACCOUNT_LINK";
    public const string EmployeesViewPrivate = "HR.EMPLOYEE.VIEW_PRIVATE";
    public const string EmployeesUpdatePrivate = "HR.EMPLOYEE.UPDATE_PRIVATE";
    public const string DepartmentsView = "HR.DEPARTMENT.VIEW";
    public const string DepartmentsCreate = "HR.DEPARTMENT.CREATE";
    public const string DepartmentsUpdate = "HR.DEPARTMENT.UPDATE";
    public const string DepartmentsDelete = "HR.DEPARTMENT.DELETE";
    public const string TeamsView = "HR.TEAM.VIEW";
    public const string TeamsCreate = "HR.TEAM.CREATE";
    public const string TeamsUpdate = "HR.TEAM.UPDATE";
    public const string TeamsDelete = "HR.TEAM.DELETE";
    public const string JobTitlesView = "HR.JOB_TITLE.VIEW";
    public const string JobTitlesCreate = "HR.JOB_TITLE.CREATE";
    public const string JobTitlesUpdate = "HR.JOB_TITLE.UPDATE";
    public const string JobTitlesDelete = "HR.JOB_TITLE.DELETE";

    // Hợp đồng và Công việc (rows inserted by migration 031, not 028).
    public const string ContractsModuleKey = "CONTRACTS";
    public const string TasksModuleKey = "TASKS";
    public const string ContractsView = "CONTRACTS.VIEW";
    public const string ContractsCreate = "CONTRACTS.CREATE";
    public const string ContractsUpdate = "CONTRACTS.UPDATE";
    public const string ContractsDelete = "CONTRACTS.DELETE";

    // Website TA (tuvangiaiphapta.vn content), inserted by migration 035.
    public const string WebsiteModuleKey = "WEBSITE";
    public const string WebsiteView = "WEBSITE.VIEW";
    public const string WebsiteContentEdit = "WEBSITE.CONTENT.EDIT";
    public const string WebsitePostsEdit = "WEBSITE.POSTS.EDIT";
    public const string WebsiteMediaEdit = "WEBSITE.MEDIA.EDIT";
    public const string WebsiteSettingsEdit = "WEBSITE.SETTINGS.EDIT";
    public const string WebsiteLeadsView = "WEBSITE.LEADS.VIEW";
    public const string WebsiteLeadsEdit = "WEBSITE.LEADS.EDIT";
    // Hóa đơn tự động content (email templates, terms of service), inserted by migration 036.
    public const string InvoiceAppModuleKey = "INVOICEAPP";
    public const string InvoiceAppContentView = "INVOICEAPP.CONTENT.VIEW";
    public const string InvoiceAppContentEdit = "INVOICEAPP.CONTENT.EDIT";
    public const string TasksView = "TASKS.VIEW";
    public const string TasksCreate = "TASKS.CREATE";
    public const string TasksAssign = "TASKS.ASSIGN";
    public const string TasksViewAll = "TASKS.VIEW_ALL";
    public const string TaskTemplatesManage = "TASKS.TEMPLATES.MANAGE";
    public const string TaskReportView = "TASKS.REPORT.VIEW";
    public const string TaskCatalogManage = "TASKS.CATALOG.MANAGE";   // Danh mục công việc (migration 043)

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
        public const string UsersImpersonate = PermissionPolicies.Prefix + AdminPermissions.UsersImpersonate;
        public const string RolesView = PermissionPolicies.Prefix + AdminPermissions.RolesView;
        public const string RolesManage = PermissionPolicies.Prefix + AdminPermissions.RolesManage;
        public const string MenusManage = PermissionPolicies.Prefix + AdminPermissions.MenusManage;
        public const string EmployeesView = PermissionPolicies.Prefix + AdminPermissions.EmployeesView;
        public const string EmployeesCreate = PermissionPolicies.Prefix + AdminPermissions.EmployeesCreate;
        public const string EmployeesUpdate = PermissionPolicies.Prefix + AdminPermissions.EmployeesUpdate;
        public const string EmployeesDelete = PermissionPolicies.Prefix + AdminPermissions.EmployeesDelete;
        public const string EmployeesChangeStatus = PermissionPolicies.Prefix + AdminPermissions.EmployeesChangeStatus;
        public const string EmployeesChangeAssignment = PermissionPolicies.Prefix + AdminPermissions.EmployeesChangeAssignment;
        public const string EmployeesAccountLink = PermissionPolicies.Prefix + AdminPermissions.EmployeesAccountLink;
        public const string DepartmentsView = PermissionPolicies.Prefix + AdminPermissions.DepartmentsView;
        public const string DepartmentsCreate = PermissionPolicies.Prefix + AdminPermissions.DepartmentsCreate;
        public const string DepartmentsUpdate = PermissionPolicies.Prefix + AdminPermissions.DepartmentsUpdate;
        public const string DepartmentsDelete = PermissionPolicies.Prefix + AdminPermissions.DepartmentsDelete;
        public const string TeamsView = PermissionPolicies.Prefix + AdminPermissions.TeamsView;
        public const string TeamsCreate = PermissionPolicies.Prefix + AdminPermissions.TeamsCreate;
        public const string TeamsUpdate = PermissionPolicies.Prefix + AdminPermissions.TeamsUpdate;
        public const string TeamsDelete = PermissionPolicies.Prefix + AdminPermissions.TeamsDelete;
        public const string JobTitlesView = PermissionPolicies.Prefix + AdminPermissions.JobTitlesView;
        public const string JobTitlesCreate = PermissionPolicies.Prefix + AdminPermissions.JobTitlesCreate;
        public const string JobTitlesUpdate = PermissionPolicies.Prefix + AdminPermissions.JobTitlesUpdate;
        public const string JobTitlesDelete = PermissionPolicies.Prefix + AdminPermissions.JobTitlesDelete;
        public const string ContractsView = PermissionPolicies.Prefix + AdminPermissions.ContractsView;
        public const string ContractsCreate = PermissionPolicies.Prefix + AdminPermissions.ContractsCreate;
        public const string ContractsUpdate = PermissionPolicies.Prefix + AdminPermissions.ContractsUpdate;
        public const string ContractsDelete = PermissionPolicies.Prefix + AdminPermissions.ContractsDelete;
        public const string WebsiteView = PermissionPolicies.Prefix + AdminPermissions.WebsiteView;
        public const string WebsiteContentEdit = PermissionPolicies.Prefix + AdminPermissions.WebsiteContentEdit;
        public const string WebsitePostsEdit = PermissionPolicies.Prefix + AdminPermissions.WebsitePostsEdit;
        public const string WebsiteMediaEdit = PermissionPolicies.Prefix + AdminPermissions.WebsiteMediaEdit;
        public const string WebsiteSettingsEdit = PermissionPolicies.Prefix + AdminPermissions.WebsiteSettingsEdit;
        public const string WebsiteLeadsView = PermissionPolicies.Prefix + AdminPermissions.WebsiteLeadsView;
        public const string WebsiteLeadsEdit = PermissionPolicies.Prefix + AdminPermissions.WebsiteLeadsEdit;
        public const string InvoiceAppContentView = PermissionPolicies.Prefix + AdminPermissions.InvoiceAppContentView;
        public const string InvoiceAppContentEdit = PermissionPolicies.Prefix + AdminPermissions.InvoiceAppContentEdit;
        public const string TasksView = PermissionPolicies.Prefix + AdminPermissions.TasksView;
        public const string TaskTemplatesManage = PermissionPolicies.Prefix + AdminPermissions.TaskTemplatesManage;
        public const string TaskReportView = PermissionPolicies.Prefix + AdminPermissions.TaskReportView;
        public const string TaskCatalogManage = PermissionPolicies.Prefix + AdminPermissions.TaskCatalogManage;
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
        new(TasksModuleKey, "Công việc",
        [
            new(TasksView, "Dùng \"Việc của tôi\": xem và cập nhật việc mình tham gia", "VIEW"),
            new(TasksCreate, "Tự tạo việc cho mình", "CREATE"),
            new(TasksAssign, "Giao việc cho nhân viên khác", "ASSIGN"),
            new(TasksViewAll, "Giám sát: xem và tương tác mọi công việc", "VIEW_ALL"),
            new(TaskTemplatesManage, "Quản lý việc lặp theo kỳ", "MANAGE"),
            new(TaskReportView, "Xem báo cáo tiến độ từng nhân viên", "VIEW"),
            new(TaskCatalogManage, "Quản lý danh mục công việc (tên các đầu việc được phép chọn)", "MANAGE")
        ]),
        new(ContractsModuleKey, "Hợp đồng",
        [
            new(ContractsView, "Xem hợp đồng và file đính kèm", "VIEW"),
            new(ContractsCreate, "Thêm hợp đồng", "CREATE"),
            new(ContractsUpdate, "Sửa hợp đồng, đính kèm hoặc gỡ file", "UPDATE"),
            new(ContractsDelete, "Xóa (mềm) hợp đồng", "DELETE")
        ]),
        new(HrModuleKey, "Nhân sự",
        [
            new(EmployeesView, "Xem danh sách và hồ sơ nhân viên", "VIEW"),
            new(EmployeesCreate, "Thêm nhân viên", "CREATE"),
            new(EmployeesUpdate, "Sửa thông tin nhân viên", "UPDATE"),
            new(EmployeesDelete, "Xóa (mềm) nhân viên", "DELETE"),
            new(EmployeesChangeStatus, "Đổi trạng thái nhân viên", "CHANGE_STATUS"),
            new(EmployeesChangeAssignment, "Chuyển chi nhánh, phòng ban, nhóm, chức danh", "CHANGE_ASSIGNMENT"),
            new(EmployeesAccountLink, "Tạo, liên kết, gỡ tài khoản đăng nhập của nhân viên", "ACCOUNT_LINK"),
            new(EmployeesViewPrivate, "Xem thông tin riêng tư (CCCD, MST cá nhân, BHXH, địa chỉ)", "VIEW_PRIVATE"),
            new(EmployeesUpdatePrivate, "Sửa thông tin riêng tư", "UPDATE_PRIVATE"),
            new(DepartmentsView, "Xem phòng ban", "VIEW"),
            new(DepartmentsCreate, "Thêm phòng ban", "CREATE"),
            new(DepartmentsUpdate, "Sửa phòng ban", "UPDATE"),
            new(DepartmentsDelete, "Xóa (mềm) phòng ban", "DELETE"),
            new(TeamsView, "Xem nhóm", "VIEW"),
            new(TeamsCreate, "Thêm nhóm", "CREATE"),
            new(TeamsUpdate, "Sửa nhóm", "UPDATE"),
            new(TeamsDelete, "Xóa (mềm) nhóm", "DELETE"),
            new(JobTitlesView, "Xem chức danh", "VIEW"),
            new(JobTitlesCreate, "Thêm chức danh", "CREATE"),
            new(JobTitlesUpdate, "Sửa chức danh", "UPDATE"),
            new(JobTitlesDelete, "Xóa (mềm) chức danh", "DELETE")
        ]),
        new(WebsiteModuleKey, "Website TA",
        [
            new(WebsiteView, "Mở mục Website TA, xem nội dung và bài viết", "VIEW"),
            new(WebsiteContentEdit, "Sửa nội dung các trang, section và SEO", "EDIT"),
            new(WebsitePostsEdit, "Viết, xuất bản, xóa bài viết và chuyên mục", "EDIT"),
            new(WebsiteMediaEdit, "Tải lên, sửa mô tả, xóa ảnh website", "EDIT"),
            new(WebsiteSettingsEdit, "Sửa thông tin liên hệ và cài đặt chung", "EDIT"),
            new(WebsiteLeadsView, "Xem yêu cầu liên hệ – báo giá từ website", "VIEW"),
            new(WebsiteLeadsEdit, "Cập nhật trạng thái, ghi chú yêu cầu từ website", "EDIT")
        ]),
        new(InvoiceAppModuleKey, "Hóa đơn tự động",
        [
            new(InvoiceAppContentView, "Xem mẫu email và điều khoản dịch vụ Hóa đơn tự động", "VIEW"),
            new(InvoiceAppContentEdit, "Sửa mẫu email và điều khoản dịch vụ Hóa đơn tự động", "EDIT")
        ]),
        new("SYSTEM", "Hệ thống",
        [
            new(UsersView, "Xem danh sách người dùng", "VIEW"),
            new(UsersCreate, "Tạo người dùng", "CREATE"),
            new(UsersUpdate, "Sửa, khóa/mở người dùng", "UPDATE"),
            new(UsersDelete, "Xóa (mềm) người dùng", "DELETE"),
            new(UsersImpersonate, "Truy cập hộ (đăng nhập dưới quyền) tài khoản người dùng, xem lịch sử truy cập hộ", "IMPERSONATE"),
            new(RolesView, "Xem vai trò và phân quyền", "VIEW"),
            new(MenusManage, "Xem cấu hình menu", "MANAGE")
        ])
    ];

    public static readonly IReadOnlyList<AdminPermission> All = Modules.SelectMany(m => m.Permissions).ToList();

    public static readonly IReadOnlySet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Codes inserted by migration 028: every module except HR, whose rows come from the HR core bootstrap of TAPortalSys.
    // AUTH.ROLES.MANAGE is inserted too but is not grantable: role management is SYS_ADMIN only (operator 2026-11-07).
    public static readonly IReadOnlyList<string> MigrationCodes = Modules.Where(m => m.Key is not (HrModuleKey or ContractsModuleKey or TasksModuleKey or WebsiteModuleKey or InvoiceAppModuleKey))
        .SelectMany(m => m.Permissions).Select(p => p.Code).Where(c => c != UsersImpersonate).Append(RolesManage).ToList();

    // Code inserted by migration 042 ("Truy cập hộ").
    public static readonly IReadOnlyList<string> Migration042Codes = [UsersImpersonate];

    // Codes inserted by migration 031 (Công việc, Hợp đồng).
    public static readonly IReadOnlyList<string> Migration031Codes = Modules.Where(m => m.Key is ContractsModuleKey or TasksModuleKey)
        .SelectMany(m => m.Permissions).Select(p => p.Code).Where(c => c != TaskCatalogManage).ToList();

    // Code inserted by migration 043 (Danh mục công việc).
    public static readonly IReadOnlyList<string> Migration043Codes = [TaskCatalogManage];

    // Codes inserted by migration 035 (Website TA).
    public static readonly IReadOnlyList<string> Migration035Codes = Modules.Where(m => m.Key is WebsiteModuleKey)
        .SelectMany(m => m.Permissions).Select(p => p.Code).ToList();

    // Codes inserted by migration 036 (Hóa đơn tự động content).
    public static readonly IReadOnlyList<string> Migration036Codes = Modules.Where(m => m.Key is InvoiceAppModuleKey)
        .SelectMany(m => m.Permissions).Select(p => p.Code).ToList();

    public static bool IsCatalogued(string? code) => code is not null && Codes.Contains(code);

    public static bool IsSystemAdmin(ClaimsPrincipal user) => user.IsInRole(SystemAdminRole);

    // Same rule as PermissionHandler: SYS_ADMIN has every permission; otherwise a "permission" claim (refreshed per request).
    public static bool Has(ClaimsPrincipal user, string code) =>
        IsSystemAdmin(user) || !string.Equals(code, RolesManage, StringComparison.OrdinalIgnoreCase)
            && user.Claims.Any(c => c.Type == "permission" && string.Equals(c.Value, code, StringComparison.OrdinalIgnoreCase));
}

public sealed record AdminLandingPage(string Permission, string Controller, string Action);

// Pages in sidebar order. Used for the post-login redirect: dashboard when allowed, else the first page the user may open.
public static class AdminNavigation
{
    public static readonly IReadOnlyList<AdminLandingPage> Pages =
    [
        new(AdminPermissions.TasksView, "Work", "Index"),          // "Việc của tôi" is every employee's landing page
        new(AdminPermissions.DashboardView, "Home", "Index"),
        new(AdminPermissions.CustomersView, "Customers", "Index"),
        new(AdminPermissions.ReconciliationView, "PaymentReconciliation", "Index"),
        new(AdminPermissions.ContractsView, "Contracts", "Index"),
        new(AdminPermissions.TaskReportView, "Work", "Report"),
        new(AdminPermissions.EmployeesView, "Employees", "Index"),
        new(AdminPermissions.DepartmentsView, "Departments", "Index"),
        new(AdminPermissions.TeamsView, "Teams", "Index"),
        new(AdminPermissions.JobTitlesView, "JobTitles", "Index"),
        new(AdminPermissions.PackagesView, "ServicePackages", "Index"),
        new(AdminPermissions.PurchasesView, "ServicePackages", "Purchases"),
        new(AdminPermissions.IntegrationsSandbox, "TingeeSandbox", "Index"),
        new(AdminPermissions.IntegrationsLogsView, "TingeeLogs", "Index"),
        new(AdminPermissions.NotificationsManage, "NotificationSettings", "Index"),
        new(AdminPermissions.WebsiteView, "Website", "Index"),
        new(AdminPermissions.WebsiteLeadsView, "WebsiteLeads", "Index"),
        new(AdminPermissions.InvoiceAppContentView, "InvoiceAppContent", "Index"),
        new(AdminPermissions.UsersView, "Users", "Index"),
        new(AdminPermissions.RolesView, "Roles", "Index"),
        new(AdminPermissions.MenusManage, "Menus", "Index")
    ];

    public static AdminLandingPage? FirstAllowed(ClaimsPrincipal user) => Pages.FirstOrDefault(p => AdminPermissions.Has(user, p.Permission));

    public static IEnumerable<AdminLandingPage> AllowedPages(ClaimsPrincipal user) => Pages.Where(p => AdminPermissions.Has(user, p.Permission));
}
