# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41`
- Created UTC: 2026-10-29T15:17:02.248774+00:00
- Tiêu đề: UI/UX redesign per operator mockup: design system, sidebar, Customers/Users, Customer Portal
- Trạng thái: UI_IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE / VISUAL_NOT_VERIFIED (production) / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Audit + chuẩn hóa giao diện Admin và Customer Portal theo mockup operator: design system chung, sidebar (lỗi trống/mất icon), Customers, Users, Customer Portal accounts, các trang còn lại, responsive/a11y; publish hai artifact. Ngoài phạm vi: DB/schema/SQL, auth/authorization, API, nghiệp vụ, security headers/cookie/Data Protection, IIS, merge PR #7.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
Chi tiết: docs/audits/ui-redesign-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41.md mục 9. Tóm tắt:
- ADD src/TAPortal.Shared/UiIcon.cs; THIRD-PARTY-NOTICES.md; MODIFY .gitattributes (wwwroot/lib -text)
- Admin: MODIFY 19 view + _Layout/_ViewImports; MODIFY Controllers/PortalControllers.cs (Users Index nạp roles/roleMap ở controller); RENAME css/ta-portal.css → css/ta-design.css; ADD js/ta-shell.js, lib/tabler/*; MODIFY js/ta-portal.js
- Customer: MODIFY 8 view + _Layout/_ViewImports, css/customer.css; ADD css/ta-design.css, js/ta-shell.js, lib/tabler/*
- Tests: ADD SafeUiDesignSystemTests.cs (20), VisualSnapshotTests.cs; MODIFY SafeAdminDashboardUxTests.cs, SafeAdminSecurityTests.cs, SafeReleaseRegressionTests.cs
- Docs: ADD docs/audits/ui-redesign-…md + 21 ảnh before/after
- Không đụng: database/scripts/004-verify-customer-portal.sql, database/scripts/scene-image.png, docs/mockup.png (operator), src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — không có migration, không chạy SQL.
- Execution: NOT_EXECUTED (không áp dụng)

## Test và kết quả
- restore OK; Release build 0 warning / 0 error (Razor compile cả hai app)
- Safe tests 235 PASS / 0 FAIL / 0 SKIP (215 cũ + 20 mới); Codec unittest OK, validate PASS; git diff --check OK
- Visual: 47 ảnh headless (1440/1024/390 + drawer/collapsed/offline) từ VisualSnapshotTests, chụp offline; VISUAL_NOT_VERIFIED trên trình duyệt/dữ liệu/IIS thật

## Publish
- Preflight: HEAD 70917eb = source; hai thư mục đích khớp manifest lần trước (f97e0bcb…, 564e037d…), 0 lock, 0 reparse; backup publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41\ đã xác minh trùng 100%
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: exit 0, 2026-10-29T15:43:05Z→15:43:08Z, 61 files, SHA256 ddb59cce55fad80178b14cbbedba69731936f4e625bfefd0a85ceaf80b8398e2
- TACustomerPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web: exit 0, 2026-10-29T15:43:08Z→15:43:10Z, 58 files, SHA256 b39bb31c86eeb817b200d34e43df0c85a350a069aac051f937d7b9aa6477dc0e
- Shared DLL 4af267cc9cfa7831d55e7e273612ce720280f7314ad1251d962aafa0d94b626a ở cả hai; InformationalVersion 1.0.0+70917eb9…; không secret; không còn tham chiếu CDN
- Trạng thái: PUBLISHED (local) / NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch: feature/customer-portal-mvp01; base d4c5332ded98ac2fb8d0be83973d38265a3ebbc4
- Source SHA: 70917eb9170426d8843eaed3dd3c6b0822b5800e; delivery SHA báo sau push
- PR: https://github.com/nguyenduc216/TAPortal/pull/7 (draft); Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Quyết định: dùng Tabler (đã tích hợp) bundle local thay CDN; icon Tabler SVG inline; nút chính #0F64C6 và nút vàng chữ navy vì tương phản; không thêm chuông/⋮ khách hàng/phân trang/"Loại KH"/"Tài liệu" (không có chức năng).
- Còn lại: UAT giao diện thật (operator); phân trang server; CSP chặt hơn + bỏ inline handler (PENDING_APPROVAL); Razor Class Library cho asset chung (PENDING_APPROVAL).
- Rollback: chép publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261029T151702Z-ABDD41\<Project> vào thư mục đích; source: git revert 70917eb.
