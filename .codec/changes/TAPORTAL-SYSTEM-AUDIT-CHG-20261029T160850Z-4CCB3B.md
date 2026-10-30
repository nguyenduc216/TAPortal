# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B`
- Created UTC: 2026-10-29T16:08:50.677638+00:00
- Tiêu đề: Unified Customer Workspace: per-customer Portal accounts, remove standalone sidebar menu
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin) / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Tái cấu trúc phân hệ Khách hàng thành Customer Workspace; quản lý tài khoản Customer Portal trong hồ sơ từng khách hàng; bỏ menu sidebar độc lập "Tài khoản Customer Portal". Không đổi schema/SQL, authorization, dữ liệu, Customer app.

## Audit route trước khi sửa
- CustomersController (SystemAdminOnly): GET Index; GET/POST Create (CRM.CUSTOMERS.CREATE); GET/POST Edit/{id} (CRM.CUSTOMERS.UPDATE). Sau lưu redirect về Index.
- CustomerPortalAccountsController (SystemAdminOnly): GET Index?customerId; POST Save (MembershipInput, tạo user khi UserId rỗng); POST SetActive(userId, customerId, active). Tất cả dùng MembershipAdminStore (SQL, kiểm tra lại SYS_ADMIN trong DB, audit cùng transaction, UPDATE-hoặc-INSERT chống trùng membership).
- Liên kết đến /CustomerPortalAccounts: sidebar, nút header + nút "Tài khoản" ở danh sách khách hàng, KPI dashboard (data-ta-kpi="memberships").

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD src/TAPortal.Web/Services/CustomerPortalAccounts.cs (ICustomerPortalAccounts + SqlCustomerPortalAccounts bọc MembershipAdminStore/PortalDb, không SQL mới)
- ADD src/TAPortal.Web/Models/CustomerWorkspaceModels.cs (tab registry mở rộng được, header, view model)
- MODIFY src/TAPortal.Web/Controllers/PortalControllers.cs (CustomersController: Details, PortalAccounts, SavePortalAccount, SetPortalAccountActive; Edit trong workspace; redirect sau lưu)
- MODIFY src/TAPortal.Web/Controllers/CustomerPortalAccountsController.cs (GET Index?customerId → redirect tab Portal của đúng khách hàng)
- MODIFY src/TAPortal.Web/Program.cs (DI ICustomerPortalAccounts)
- ADD Views/Customers/_Workspace.cshtml, Details.cshtml, PortalAccounts.cshtml; MODIFY Views/Customers/Edit.cshtml, Index.cshtml, Views/CustomerPortalAccounts/Index.cshtml, Views/Shared/_Layout.cshtml
- ADD src/TAPortal.Web/wwwroot/css/ta-admin.css (tab/workspace, chỉ Admin; ta-design.css dùng chung không đổi)
- Tests: ADD SafeCustomerWorkspaceTests.cs (15); MODIFY SafeUiDesignSystemTests.cs (sidebar), VisualSnapshotTests.cs (trang workspace)
- Không đụng: src/TAPortal.Shared, src/TACustomerPortal.Web, database/*, docs/mockup.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — không cần migration; không chạy SQL. Execution: NOT_EXECUTED (không áp dụng)

## Test và kết quả
- restore OK; Release build 0/0; safe tests 250/250 PASS (15 mới); Codec unittest OK, validate PASS; git diff --check OK
- Visual: 13 ảnh headless workspace (1440/1024/390); VISUAL_NOT_VERIFIED trên trình duyệt/dữ liệu/IIS thật

## Publish
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: exit 0, 2026-10-29T16:15:48Z→16:15:50Z, 62 files, SHA256 f609696d3ead90bca8b464902bcde63277a70cfcf06b409aa44dba8df710e607; sha256sum -c PASS; không secret
- Backup: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T160850Z-4CCB3B\TAPortal.Web (ddb59cce…, xác minh trùng 100%)
- TACustomerPortal.Web: NOT_REQUIRED (Customer/Shared source không đổi; artifact b39bb31c… giữ nguyên)
- Shared DLL: Admin dc682cab…2b63, Customer 4af267cc…626a — khác chỉ do InformationalVersion nhúng commit; tree src/TAPortal.Shared trùng (065cf67e) giữa 70917eb và eb8d0e6 → tương thích
- PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch feature/customer-portal-mvp01; base 2e5099ba144792a2d466ca3ba7572b527b1bcb35
- Source SHA eb8d0e656654a16c8f78930977bc8ad63e6f4a53; delivery SHA báo sau push
- PR #7 draft; Merge PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Quyết định: customer context lấy từ route; CustomerId trong body khác route → 400; thu hồi chỉ khi membership có trong danh sách của khách hàng đó; tạo user chỉ với mode=create tường minh (tái sử dụng logic hiện có, user mới không có vai trò TAPortal).
- Giữ tương thích: /CustomerPortalAccounts (tổng hợp) vẫn truy cập được (không có trong sidebar), ?customerId chuyển sang tab của khách hàng; POST Save/SetActive cũ giữ nguyên.
- Operator quyết định: (1) có gỡ hẳn trang tổng hợp cũ và trỏ KPI dashboard về Khách hàng; (2) kích hoạt lại membership khi khách hàng ngừng hoạt động hiện chỉ bị chặn ở UI, backend SetActive không kiểm tra trạng thái khách hàng (hành vi có sẵn); (3) danh sách "tài khoản có sẵn" gồm cả nhân viên TA (hành vi có sẵn) — có cần lọc; (4) tab tương lai (dịch vụ, tài liệu, công việc, báo cáo, cấu hình) cần thiết kế dữ liệu/schema riêng.
- Rollback: chép thư mục backup vào deployed-TAPortal.Web; source git revert eb8d0e6.
