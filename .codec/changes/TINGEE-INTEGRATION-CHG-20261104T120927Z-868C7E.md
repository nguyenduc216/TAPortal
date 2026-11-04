# Change TINGEE-INTEGRATION-CHG-20261104T120927Z-868C7E

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T120927Z-868C7E`
- Created UTC: 2026-11-04T12:09:27.626870+00:00
- Tiêu đề: Customer Portal: chọn ngân hàng chỉ bằng logo; quy tắc hover cam nhạt cho ô trắng bấm được
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh trang chọn ngân hàng): "Bỏ text đi, có logo là được rồi, và rule các div trắng khi hover vào phải đổi background color thành màu cam nhạt".
- Ô chọn ngân hàng chỉ còn logo; tên ngân hàng giữ ở `alt` và `title`.
- Quy tắc hover: token `--cp-hover-bg: #FFF4E5`, `--cp-hover-line: #FBD38D`; áp cho `a.cp-card` (thẻ tổng quan, dịch vụ), `.cp-bank-pick a`, `.cp-company`, `.cp-filter` (chưa chọn), `.cp-plan` (thẻ gói dịch vụ). Ghi vào AGENTS.md mục "CUSTOMER PORTAL HOVER RULE" và test kiểm tra.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Banks/Add.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TACustomerPortal.Web/Views/ServicePackages/Index.cshtml` (deploy invoice).
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPartnerNeutralityTests.cs`, `AGENTS.md`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Safe tests 389/389 (working tree).

## Publish
- invoice (TACustomerPortal.Web → TA.Invoice): DEPLOYED 2026-11-04 19:11 (UTC+7), commit e23dc07, artifact SHA256 676252c62a7b2fec391fa70db3d591e03b990dc1baf90c5cdd24019f1b6a334f, package D92DAFB094364F3BE2944759CB1F192ADE41FB665E75277685435E480B72525B; backup C:\TADeploy\backups\invoice-20261104-191118; health HTTP 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: e23dc07 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
