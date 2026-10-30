# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T170242Z-4FCD90

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T170242Z-4FCD90`
- Created UTC: 2026-10-29T17:02:42Z
- Tiêu đề: Distinct Customer Portal design (gold/green), Portal accounts tab in Customers, staff exclusion, pagination and smooth navigation
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin + Customer) / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
1. Customer Portal có giao diện riêng, đẹp hơn, khác TAPortal; tông chủ đạo vàng + xanh lá; tham khảo giao diện SaaS.
2. Bỏ trang tổng hợp cũ, đưa vào tab mới trong Quản lý khách hàng.
3. Loại tài khoản nhân viên TA khỏi việc cấp quyền Customer Portal.
4. Phân trang; tải trang/tab mượt, không tạo cảm giác chờ server.

## Thiết kế và quyết định
- Tham khảo: roundup client-portal/SaaS 2026 (AdminLTE client dashboards: Assembly, AgencyAnalytics; 925Studios: progressive disclosure; fintech xanh lá trên nền sáng: Finova/Expensy; PhonePe dùng vàng làm điểm nhấn). Áp dụng: nền kem sáng, nhiều khoảng trắng, xanh lá là màu chính, vàng là điểm nhấn/trạng thái, ít "chrome".
- Customer Portal: hệ thống thiết kế riêng `customer.css` (không Tabler, không ta-design.css); top navigation dạng pill + chip "Đổi doanh nghiệp" + menu tài khoản; thanh tab nổi dưới đáy trên điện thoại; trang đăng nhập chia đôi (panel xanh có hoa văn chấm + vầng sáng vàng); dashboard hero xanh với vai trò dạng pill vàng; bảng màu kiểm tra tương phản WCAG AA (trắng/#146C3F 6.47, chữ/#F2B705 8.92...).
- Trang tổng hợp: Customers có tab "Danh sách khách hàng" | "Tài khoản Portal" (/Customers/Accounts, chỉ đọc, mỗi dòng mở tab quản lý trong workspace). /CustomerPortalAccounts chỉ còn GET chuyển hướng (customerId → tab workspace; không → tab mới); POST Save/SetActive cũ đã gỡ (404). KPI dashboard trỏ về tab mới.
- Nhân viên TA = user có ít nhất một vai trò TAPortal (dbo.UserRoles). Không có trong danh sách ứng viên; backend từ chối cấp quyền mới; membership đã tồn tại vẫn quản lý được. Hạn chế: tài khoản nhân viên chưa có vai trò nào vẫn được coi là ứng viên (không có cột loại người dùng) → cần quyết định schema nếu muốn tuyệt đối.
- Phân trang phía client cho danh sách đã render (Khách hàng, Tài khoản Portal, Người dùng, Vai trò, Menu, workspace, chọn doanh nghiệp): 10/20/50 dòng, nhớ lựa chọn, kết hợp ô lọc, không gọi server. Không đổi câu SQL (phân trang server-side để sau khi dữ liệu lớn).
- Điều hướng mềm (ta-shell.js, cả hai app): prefetch khi rê/focus/chạm link; thay nội dung tại chỗ bằng View Transition (chỉ vùng nội dung, sidebar/topbar đứng yên); form POST gửi nền (fetch, theo redirect, giữ antiforgery/cookie); Back/Forward; tự rơi về tải trang thường nếu layout khác (login/logout) hoặc lỗi. Bỏ thanh tiến trình tải của Admin. Bảo mật không đổi: cùng request, cookie, antiforgery và kiểm tra quyền phía server.

## File thay đổi (source commit 123c2ff6c2376962030c7fff159506163f8d1f01)
- Customer: Views/Shared/_Layout, Account/{Login,SelectCustomer,ChangePassword,AccessDenied,Error}, Dashboard/Index, Profile/{Company,Personal}; wwwroot/css/customer.css (viết mới), wwwroot/js/customer.js, wwwroot/js/ta-shell.js; DELETE wwwroot/css/ta-design.css, wwwroot/lib/tabler/*
- Admin: Controllers/{PortalControllers,CustomerPortalAccountsController}.cs; Services/CustomerPortalAccounts.cs (ListAllAsync, CandidateUsersAsync, PortalCandidates); Models/CustomerWorkspaceModels.cs; ADD Views/Customers/{Accounts,_CustomersTabs}; DELETE Views/CustomerPortalAccounts/Index; MODIFY Views/Customers/{Index,PortalAccounts,_Workspace}, Home/Index, Users/Index, Roles/Index, Menus/Index, Shared/_Layout; wwwroot/css/ta-design.css, wwwroot/js/{ta-portal,ta-shell}.js
- THIRD-PARTY-NOTICES.md; tests: SafeUiDesignSystemTests, SafeCustomerWorkspaceTests, SafeAdminCrudTests, SafeAdminDashboardUxTests, VisualSnapshotTests

## Migration SQL
- Không có. NOT_EXECUTED (không áp dụng)

## Test và kết quả
- restore OK; Release build 0/0; safe tests 254/254 PASS; Codec unittest OK, validate PASS; git diff --check OK
- E2E trên Chrome thật qua HTTP cục bộ (trang render thật, dữ liệu mẫu): Admin — phân trang (trang 1/2 đúng dòng), lọc, chuyển tab Khách hàng → Tài khoản Portal không reload (biến JS giữ nguyên), URL/tiêu đề/tab/sidebar active đúng, Back quay lại không reload; Customer — chuyển Tổng quan → Doanh nghiệp không reload, nav trên và tab bar dưới active đúng. Gửi form nền (POST) chưa kiểm chứng E2E (cần server ứng dụng thật).
- VISUAL_NOT_VERIFIED trên trình duyệt/dữ liệu/IIS Production.

## Publish
- TAPortal.Web: exit 0, 2026-10-29T17:17:40Z→17:17:44Z, 62 files, SHA256 cc224fd32f4470a870b5d97dcb9278b3eba8044a64ac47d8b6757d8dd331ddfa
- TACustomerPortal.Web: exit 0, 2026-10-29T17:17:44Z→17:17:46Z, 55 files (bỏ tabler + ta-design.css), SHA256 ac27314e0a4b9db55b0f80dbe8f429d072a9a6dfe7b3839bff072ccf4b355e79
- Shared DLL 22184b03cea39ddda5740e1666be0cf0731bfbb3b42fe6b116f97cc895499b08 ở cả hai; không secret
- Backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T170242Z-4FCD90\ (9b428ced…, de8af4e6…) xác minh trùng 100%
- PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Source SHA 123c2ff6c2376962030c7fff159506163f8d1f01; base fad169fc0ae38b908b87cd40c1b87eb4f55dc27a; delivery SHA báo sau push; PR #7 draft, merge PENDING_APPROVAL

## Còn lại / operator quyết định
- Định nghĩa "nhân viên TA" tuyệt đối cần cột loại người dùng (schema) — PENDING_APPROVAL.
- Phân trang phía server khi dữ liệu lớn (đổi truy vấn) — đề xuất.
- CSP chặt hơn (bỏ inline handler) — PENDING_APPROVAL.
- Rollback: chép hai thư mục backup; git revert 123c2ff6c2376962030c7fff159506163f8d1f01.
