# Change TINGEE-INTEGRATION-CHG-20261104T105303Z-FCDC7B

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T105303Z-FCDC7B`
- Created UTC: 2026-11-04T10:53:03.007955+00:00
- Tiêu đề: Customer Portal: không hiển thị tên đối tác (Tingee/HENO); thông báo khi chưa cấu hình liên kết ngân hàng
- Trạng thái: DEPLOYED (cấu hình Tingee cho site invoice: PENDING_OPERATOR)

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh trang Tài khoản ngân hàng của Todox): "khách hàng chưa tự liên kết tới tài khoản ngân hàng được, kiểm tra lại. và tạo rule bỏ tất cả các thông tin đối tác như tingee trên tất cả các chức năng trên hệ thống customer".
- Nguyên nhân nút "Liên kết tài khoản" bị khoá: `appsettings.json` của site invoice (H:\WEBDEPLOYED\T.A\customer-portal) không có mục `Tingee` nên `ITingeeApi.IsConfigured=false` (Portal và site webhook đều có). Sửa cấu hình server cần operator đồng ý (chép khoá) — PENDING_OPERATOR.
- Trang ngân hàng hiện thông báo "Dịch vụ liên kết ngân hàng đang được T.A cấu hình" khi thiếu cấu hình, thay vì nút bị khoá không lý do.
- Quy tắc trung lập đối tác: mọi chữ khách hàng thấy không nêu Tingee/HENO; chữ từ đối tác đi qua `PartnerText.Scrub`; `Flash()` tự lọc. Ghi vào AGENTS.md mục "CUSTOMER PORTAL PARTNER NEUTRALITY".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Services/PartnerText.cs`; ADD `tests/TACustomerPortal.Tests/SafeCustomerPartnerNeutralityTests.cs`.
- MODIFY `src/TACustomerPortal.Web/Controllers/{BanksController,OrdersController,CustomerAreaController}.cs`, `Services/OrderPayments.cs`, `Views/Banks/{Index,Add,Confirm}.cshtml`, `Views/Dashboard/Index.cshtml`, `Views/Shared/_PaymentsNotLinked.cshtml` (deploy invoice).
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`, `AGENTS.md`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Safe tests 372/372 (working tree), gồm quét view/chuỗi, hàm lọc và quét trang render (kể cả lỗi đối tác trả về có chữ Tingee).

## Publish
- invoice (TACustomerPortal.Web → TA.Invoice, H:\WEBDEPLOYED\T.A\customer-portal): DEPLOYED 2026-11-04 17:56 (UTC+7), commit 5f73a62, artifact SHA256 e826b4522190b71c5b6afba32231c487ac77b86a7fb28e264b6c7457ffa6806b, package 05415E171A3F70362281441E683FCD2C164B6AD682F957D452E7707A586DBC2E; backup C:\TADeploy\backups\invoice-20261104-175627; health HTTP 200. Audit cần -MigrationsApplied vì migration 017 (đã EXECUTED) nằm trong khoảng audit.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 5f73a62 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
