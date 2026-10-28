# TAPORTAL — AUDIT & DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID: TAPORTAL-SYSTEM-AUDIT (phụ thuộc CUSTOMER-PORTAL-FOUNDATION)
- CHANGE_ID: TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2
- Branch: feature/customer-portal-mvp01
- Starting Git SHA: 24d9f586831d9fafa52866b5b927472aa3c62f80 (bằng origin lúc bắt đầu)
- Source Git SHA: 336ab7c80552e733a5df9e6c4612aeb163037007
- Delivery commit SHA: commit chứa báo cáo này; SHA được báo trong thông báo bàn giao sau khi push (không tự tham chiếu)
- PR URL: https://github.com/nguyenduc216/TAPortal/pull/7
- Merge status / merge SHA: NOT_MERGED / none

## B. Audit Summary
- Modules inspected: Admin (Program, Account/Home/Users/Customers/Roles/Menus/CustomerPortalAccounts, PortalDb, phân quyền, views, JS); Customer Portal (Program, Account/Dashboard/Profile, CustomerSecurity, CustomerStore); Shared (PasswordService, MembershipAdminStore); migration/seed để đối chiếu; tests; Codec.
- Total findings: 19 — CRITICAL 0 / HIGH 2 / MEDIUM 8 / LOW 9
- Fixed: 12 (AUD-01..11, AUD-19). Remaining: 7 OPEN (AUD-12 PENDING_APPROVAL, AUD-13..18)
- Limitations: không truy cập database (NOT_VERIFIED); luồng ghi SQL và render trang đã xác thực chưa được kiểm thử tự động; chưa kiểm tra trực quan trên trình duyệt/mobile; không có HTTPS/IIS.
- Ledger chi tiết: docs/audits/taportal-system-audit-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2.md

## C. Customer Portal Account Management
- Backend implemented: YES (MembershipAdminStore: kiểm tra lại SYS_ADMIN trong DB, transaction serializable, upsert theo cặp UserId–CustomerId, thu hồi/kích hoạt)
- Admin UI implemented: YES (trước: PARTIAL — không có style, ký tự `?`, không có bộ lọc) — AUD-05 FIXED
- Menu accessible: YES (trước: NO trong sidebar, chỉ có link phụ) — AUD-04 FIXED
- Membership CRUD: YES cho tạo/gán, xem, đổi vai trò, thu hồi/kích hoạt; không có xóa cứng (cố ý, giữ lịch sử). Trùng cặp UserId/CustomerId: update tại chỗ + unique constraint (migration 008).
- Tenant isolation: 34 SafePortalTests PASS (cô lập A/B, bỏ qua customerId trên URL, thu hồi phiên); SafeAdminCrudTests: vai trò OWNER + mọi permission chi tiết vẫn bị 403 ở quản trị → vai trò Customer Portal không trở thành SYS_ADMIN.
- Pending operator actions: UAT với dữ liệu thật (UAT-01…09); membership hiện có theo báo cáo operator là 0.

## D. Admin CRUD
- User Create/Read/Update/Deactivate: Create (đã có, sửa lỗi mất dữ liệu + trùng email), Read, Update (MỚI), Deactivate qua IsActive (MỚI; chặn tự vô hiệu hóa). Không có xóa (AUD-17 OPEN).
- Customer Create/Read/Update/Deactivate: Create (MỚI), Read, Update (MỚI, mã bất biến), Deactivate qua IsActive (MỚI). Không có xóa (AUD-17 OPEN).
- Role/permission management: gán vai trò lúc tạo; đổi vai trò sau tạo chưa có (AUD-12 PENDING_APPROVAL). Menu Vai trò/Menu chỉ hiện khi có quyền.
- Navigation and validation: sidebar + breadcrumb; DataAnnotations tiếng Việt phía server; ràng buộc HTML maxlength/required; giữ dữ liệu khi lỗi.
- Regression status: build + 65 safe tests PASS; smoke ẩn danh PASS; luồng ghi SQL = PENDING_OPERATOR.

## E. Changed Files
| File | Lý do | Findings |
|---|---|---|
| src/TAPortal.Web/Controllers/PortalControllers.cs | Users Edit, Create giữ dữ liệu/kiểm tra email; Customers Create/Edit | AUD-01, 02, 06 |
| src/TAPortal.Web/Services/PortalDb.cs | Hàm cập nhật/tạo/đọc có tham số, kiểm tra trùng | AUD-01, 02, 06, 08 |
| src/TAPortal.Web/Models/PortalModels.cs | UserEditVm, CustomerEditVm, CustomerRow.IsActive | AUD-01, 02, 08 |
| src/TAPortal.Web/Controllers/CustomerPortalAccountsController.cs | Bộ lọc, giữ bộ lọc, thông báo | AUD-05 |
| src/TAPortal.Web/Views/Users/Index.cshtml, Users/Edit.cshtml | Nút Sửa, form sửa, mở lại modal | AUD-01, 06, 08, 19 |
| src/TAPortal.Web/Views/Customers/Index.cshtml, Customers/Edit.cshtml | Thêm/Sửa, sửa vị trí nút, badge | AUD-02, 03, 08 |
| src/TAPortal.Web/Views/CustomerPortalAccounts/Index.cshtml | Viết lại UI | AUD-05 |
| src/TAPortal.Web/Views/Shared/_Layout.cshtml | Mục menu, ẩn theo quyền, placeholder | AUD-04, 07 |
| src/TAPortal.Web/wwwroot/js/ta-portal.js | Mở lại modal sau lỗi | AUD-06 |
| tests/TACustomerPortal.Tests/SafeAdminCrudTests.cs | 31 regression test không SQL | AUD-01, 02, 05, 06 |
| AGENTS.md, .agents/skills/codec/SKILL.md | MULTI-AGENT CONTRACT, sửa tiêu đề | AUD-09, 10, 11 |
| .codec/config.json, .codec/tools/codec.py, .codec/tools/test_codec.py | Quy tắc máy đọc được + validator + test | AUD-09 |
| .codec/README.md, docs/customer-portal-runbook.md | Sửa tiêu đề, lệnh test | AUD-10 |
| docs/audits/…, .codec/features/TAPORTAL-SYSTEM-AUDIT/*, .codec/changes/…, .codec/reports/… | Báo cáo, lịch sử, manifest | — |
| .gitattributes (mới, commit bàn giao) | `*.sha256 -text`: giữ nguyên byte manifest khi checkout (core.autocrlf=true sẽ đổi sang CRLF làm sai digest) | AUD-11 |

## F. Database
- Target: TAPortalSys (WIN-HSHL54Q184K\MSSQLSERVER2014)
- Direct DB access status: NOT_ACCESSED / NOT_VERIFIED (không chạy truy vấn nào)
- Migration files: none (schema hiện có đáp ứng)
- Verification scripts: none mới. `database/scripts/004-verify-customer-portal.sql` có thay đổi chưa commit sẵn có của người dùng — không sửa, không stage.
- Migration execution status: NOT_EXECUTED (không áp dụng)
- Manual instructions: không cần SQL. Ứng dụng Admin cần quyền UPDATE/INSERT sẵn có trên dbo.Users và dbo.Customers (như chức năng tạo người dùng hiện tại).

## G. Build & Tests
- Restore: PASS
- Release build: PASS — 0 warnings, 0 errors
- Test project: tests/TACustomerPortal.Tests (SafePortalTests 34 + SafeAdminCrudTests 31) — 65 passed / 0 failed / 0 skipped; legacy SQL PortalTests not selected
- Codec: 4 unit tests PASS; `codec.py validate` PASS; `git diff --check` PASS
- Smoke: Admin 13/13, Customer 10/10 (GET ẩn danh, Production, không SQL)
- Known limitations: không có SQL integration test (bị cấm theo policy); không kiểm tra trên trình duyệt; CI chạy toàn solution với legacy tests đã skip.

## H. Admin Publish
- Project: TAPortal.Web — Status: PUBLISHED (local) / NOT_DEPLOYED
- Target path: D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web (mới tạo, theo đúng convention)
- Source Git SHA: 336ab7c80552e733a5df9e6c4612aeb163037007
- Publish timestamp: 2026-10-28T16:44:38Z → 16:44:41Z; exit code 0
- File count: 57; TAPortal.Web.dll SHA256 07b3f9bdccc5ec475caf43a83593ce3bdb8e6c3034d4c7320e38388417a6a9fb
- Manifest path: .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2-TAPortal.Web.sha256
- Artifact SHA256: 4b308e3bfbfddf80cffaf1fd099981d07b597128af1d5ab03e31ebca8b05ea35

## I. Customer Publish
- Project: TACustomerPortal.Web — Status: PUBLISHED (local) / NOT_DEPLOYED
- Target path: D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web
- Source Git SHA: 336ab7c80552e733a5df9e6c4612aeb163037007 (source Customer/Shared không đổi kể từ ff965ef; hash thay đổi vì assembly nhúng commit SHA)
- Publish timestamp: 2026-10-28T16:44:41Z → 16:44:43Z; exit code 0
- File count: 54; TACustomerPortal.Web.dll SHA256 d0023b90c41c0aeea4d92063be7e8cc5d106cab58bc8f7b8e80e848d02684a63
- Manifest path: .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2-TACustomerPortal.Web.sha256
- Artifact SHA256: ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34
- Artifact trước: backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2\deployed-TACustomerPortal.Web (54 files; 40442b… theo thứ tự cũ = f76b421a… theo thứ tự ordinal; backup đã xác minh giống hệt)

Phương pháp hash (có thể kiểm tra lại): sắp xếp mọi file theo đường dẫn tương đối POSIX (ordinal, phân biệt hoa/thường); mỗi dòng `<sha256 chữ thường>  <đường dẫn>` + LF; UTF-8 không BOM; artifact SHA256 = SHA256 của các byte manifest. Kiểm tra: `sha256sum` file .sha256 phải ra đúng artifact SHA256, và `sha256sum -c` trong thư mục artifact phải PASS.

## J. Security and Acceptance
- Authentication: không đổi. Cookie Admin được kiểm tra lại mỗi request với DB; tắt IsActive sẽ chấm dứt phiên ở request kế tiếp (cả hai ứng dụng).
- Authorization: chức năng mới yêu cầu `SystemAdminOnly` + permission chi tiết; mọi POST có CSRF (đã kiểm tra bằng reflection + HTTP).
- Tenant isolation: không đổi; đã kiểm tra lại bằng test.
- UI/CRUD regression: PASS ở mức build/test/smoke; luồng ghi SQL đã xác thực = PENDING_OPERATOR.
- Open CRITICAL/HIGH defects: none
- Open MEDIUM: AUD-12 (đổi vai trò, PENDING_APPROVAL), AUD-13 (Admin không giới hạn tần suất đăng nhập)
- Manual UAT pending: xem mục 5 của báo cáo audit + UAT-01…09
- Release readiness: READY_FOR_UAT

## K. Git Delivery
- Commit 1 (source): 336ab7c80552e733a5df9e6c4612aeb163037007 — 19 file source/test/policy
- Commit 2 (delivery): báo cáo, lịch sử, manifest — SHA được báo sau khi push
- Not staged (by design): database/scripts/004-verify-customer-portal.sql (người dùng), database/scripts/scene-image.png (người dùng), src/.claude/ (cấu hình công cụ cục bộ), publish/ và TestResults/ (đã gitignore)
- Push result / remote HEAD / PR state: báo trong thông báo bàn giao
- Merge: NOT_MERGED

## L. Operator Handoff
- Artifact folders: D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web và D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web
- Chép lên server (chỉ khi có phê duyệt deploy riêng): toàn bộ nội dung từng thư mục vào site tương ứng; giữ cấu hình bên ngoài artifact (Database__ConnectionString, DataProtection__KeyPath, ASPNETCORE_ENVIRONMENT=Production); xác minh lại artifact SHA256 trước khi chép.
- SQL scripts to run manually: none
- Unresolved blockers: none cho UAT. Quyết định nghiệp vụ cần cho AUD-12/15/16.
- Backup/rollback: bản Customer trước nằm ở publish\rollback\…; Admin là artifact đầu tiên. Trên server: operator backup thư mục site trước khi thay, để rollback bằng cách khôi phục bản đó. Rollback source: `git revert 336ab7c`.
- Next recommended checkpoint: operator UAT Admin (sửa user/khách hàng, quản lý tài khoản Customer Portal) + UAT-01…09; sau đó một change riêng cho AUD-13/AUD-14 (bảo vệ đăng nhập Admin, no-store) và AUD-12 (quản lý vai trò) khi đã chốt quy tắc.
