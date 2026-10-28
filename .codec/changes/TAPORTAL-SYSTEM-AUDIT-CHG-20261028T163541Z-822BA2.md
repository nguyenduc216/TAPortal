# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2

- Feature ID: `TAPORTAL-SYSTEM-AUDIT` (phụ thuộc `CUSTOMER-PORTAL-FOUNDATION`)
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2`
- Created UTC: 2026-10-28T16:35:41.928408+00:00
- Tiêu đề: Full system audit, Admin Users/Customers edit, Customer Portal account UI, dual publish
- Trạng thái: LOCAL_ARTIFACTS_PUBLISHED_UAT_PENDING
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu và phạm vi
Audit Admin, Customer Portal và Shared; sửa an toàn lỗi CRUD/UI/navigation (ưu tiên quản lý tài khoản Customer Portal, sửa người dùng, sửa doanh nghiệp); thống nhất contract cho Claude/Codex; build, safe test, publish cả hai artifact cục bộ; báo cáo. Không có migration, không ghi DB, không IIS, không merge. Findings chi tiết: `docs/audits/taportal-system-audit-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2.md`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY AGENTS.md, .agents/skills/codec/SKILL.md, .codec/README.md, .codec/config.json, .codec/tools/codec.py, .codec/tools/test_codec.py, docs/customer-portal-runbook.md
- MODIFY src/TAPortal.Web/Controllers/CustomerPortalAccountsController.cs, Controllers/PortalControllers.cs, Models/PortalModels.cs, Services/PortalDb.cs, Views/CustomerPortalAccounts/Index.cshtml, Views/Customers/Index.cshtml, Views/Users/Index.cshtml, Views/Shared/_Layout.cshtml, wwwroot/js/ta-portal.js
- ADD src/TAPortal.Web/Views/Customers/Edit.cshtml, src/TAPortal.Web/Views/Users/Edit.cshtml, tests/TACustomerPortal.Tests/SafeAdminCrudTests.cs
- ADD docs/audits/taportal-system-audit-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2.md, .codec/features/TAPORTAL-SYSTEM-AUDIT/*, .codec/changes/…, .codec/reports/… (+2 manifest .sha256)
- Không đụng: database/scripts/004-verify-customer-portal.sql (thay đổi sẵn có của người dùng), database/scripts/scene-image.png, src/.claude/

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có — schema hiện có (Users, Customers, CustomerPortalMemberships) đáp ứng
- Thứ tự chạy: không áp dụng
- Preflight / Verify / Rollback: không áp dụng
- Execution: NONE_REQUIRED / NOT_EXECUTED

## Test và kết quả
- dotnet restore TAPortal.sln: PASS (up-to-date)
- dotnet build TAPortal.sln -c Release --no-restore: PASS, 0 warnings, 0 errors
- dotnet test tests/TACustomerPortal.Tests/TACustomerPortal.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~SafePortalTests|FullyQualifiedName~SafeAdminCrudTests": 65 passed, 0 failed, 0 skipped (34 cũ + 31 mới); TRX: TestResults/system-audit.trx (ignored, không commit)
- python -m unittest discover -s .codec/tools -p test_codec.py: 4 PASS; python .codec/tools/codec.py validate: PASS
- git diff --cached --check: PASS
- Smoke cục bộ (Production, SQL cố ý sai, key tạm ngoài artifact, GET ẩn danh): Admin 13/13, Customer 10/10 PASS, không lộ stack trace, tiến trình đã dừng

## Publish
- TAPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web: PUBLISHED, exit 0, 2026-10-28T16:44:41Z, 57 files, artifact SHA256 4b308e3bfbfddf80cffaf1fd099981d07b597128af1d5ab03e31ebca8b05ea35
- TACustomerPortal.Web → D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web: PUBLISHED, exit 0, 2026-10-28T16:44:43Z, 54 files, artifact SHA256 ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34
- Artifact Customer cũ được backup và đã xác minh: publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2\deployed-TACustomerPortal.Web (40442b… theo thứ tự cũ = f76b42… theo thứ tự ordinal)
- NOT_DEPLOYED; IIS NOT_CHECKED

## Git
- Branch: feature/customer-portal-mvp01
- Starting SHA: 24d9f586831d9fafa52866b5b927472aa3c62f80
- Source commit SHA: 336ab7c80552e733a5df9e6c4612aeb163037007
- Delivery commit SHA: báo trong thông báo bàn giao (không tự tham chiếu)
- PR: https://github.com/nguyenduc216/TAPortal/pull/7
- Merge: PENDING_APPROVAL; Merge SHA: none

## Rủi ro, quyết định và việc còn lại
- Luồng ghi SQL (UPDATE/INSERT Users/Customers) và render trang đã xác thực chưa kiểm thử tự động → operator UAT.
- OPEN: AUD-12 (đổi vai trò, PENDING_APPROVAL), AUD-13 (throttle đăng nhập Admin), AUD-14 (no-store Admin), AUD-15..18.
- Rollback ứng dụng: chép lại bản backup Customer; artifact Admin là bản đầu tiên (xóa thư mục deployed-TAPortal.Web nếu cần). Rollback source: revert 336ab7c.
