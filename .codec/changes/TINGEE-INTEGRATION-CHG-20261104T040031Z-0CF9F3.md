# Change TINGEE-INTEGRATION-CHG-20261104T040031Z-0CF9F3

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T040031Z-0CF9F3`
- Created UTC: 2026-11-04T04:00:31.422691+00:00
- Tiêu đề: Tingee API call log with log-code lookup (for Tingee support)
- Trạng thái: IMPLEMENTED / TESTS_PASS

## Yêu cầu và phạm vi
Operator (2026-11-04): Tingee hỗ trợ yêu cầu gửi request gốc của lỗi liên kết VietinBank cá nhân (400 "Verify string error … Refid=…"). Cần ghi log các lần gọi Tingee, có form tra cứu log và tìm theo mã log, làm nhanh.
- Mỗi lần TingeeClient gọi Open API được ghi một dòng `dbo.TingeeApiLogs`: method, URL, x-client-id, x-request-timestamp, x-signature, body gửi đi, HTTP status, mã/thông báo Tingee, body phản hồi, lỗi mạng, thời gian. Không lưu SecretKey. Ghi log lỗi không bao giờ làm hỏng lệnh gọi.
- Mã log `TG` + yyMMddHHmmss (UTC) + 6 hex ngẫu nhiên; gắn vào `TingeeResponse.LogCode`, `TingeeApiException.LogCode` và `Exception.Data["TingeeLogCode"]` (lỗi mạng). Sandbox, Admin (tạo/kiểm tra merchant) và Customer (liên kết ngân hàng) hiện "Mã log: …" cùng thông báo lỗi.
- Trang Admin `/tingeelogs` (SystemAdminOnly, menu "Tingee log"): tìm theo mã log, lọc theo API và chỉ lỗi, 50 lần gọi gần nhất, nút chép nội dung gửi Tingee.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Integrations/Tingee/TingeeCallLog.cs` (entry, ITingeeCallLog, TingeeLogCode, Null/Sql implementation).
- MODIFY `src/TAPortal.Integrations/Tingee/{TingeeClient,TingeeApi}.cs`, `src/TAPortal.Integrations/IntegrationsServiceCollectionExtensions.cs`.
- ADD `src/TAPortal.Web/Controllers/TingeeLogsController.cs`, `src/TAPortal.Web/Views/TingeeLogs/Index.cshtml`.
- MODIFY `src/TAPortal.Web/Controllers/{TingeeSandboxController,CustomerTingeeActions}.cs`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/ta-tingee-sandbox.js`.
- MODIFY `src/TACustomerPortal.Web/Controllers/BanksController.cs`.
- ADD `database/migrations/014-tingee-api-logs.sql`, `database/scripts/017-verify-tingee-api-logs.sql`, `database/scripts/018-rollback-tingee-api-logs.sql`.
- MODIFY tests `SafeTingeeDirectMerchantTests.cs` (+5), `SafeAdminTingeeSandboxTests.cs` (+2).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/014-tingee-api-logs.sql` (bảng mới, không đổi đối tượng/dữ liệu có sẵn)
- Thứ tự chạy: sau 012 (013 thuộc CUSTOMER-PROFILE, chưa commit, không liên quan)
- Preflight / Verify / Rollback: `database/scripts/017-verify-tingee-api-logs.sql` / `database/scripts/018-rollback-tingee-api-logs.sql` (chỉ xóa bảng khi rỗng)
- Execution: theo AGENT MIGRATION EXECUTION — xem `migration_status`.

## Test và kết quả
- Build Release 0 lỗi; Safe tests pass (7 test mới: ghi log đúng request/chữ ký, mã log trong lỗi Tingee và lỗi mạng, chuẩn hóa mã log, trang tra cứu tìm theo mã / không tìm thấy / mã sai, chặn non-admin).

## Publish
- Project: TAPortal.Web (portal), TACustomerPortal.Web (invoice)
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Body request chứa số tài khoản, CCCD, số điện thoại (PII) — chỉ SYS_ADMIN xem; cân nhắc chính sách giữ log (chưa có job xóa log cũ).
- Site Customer ghi log bằng tài khoản SQL của nó; nếu tài khoản đó không có quyền INSERT trên bảng mới thì chỉ mất log, lệnh gọi vẫn chạy.
- Log chỉ có từ sau khi deploy: lỗi VietinBank Refid=f716a868-… trước đó không có log; operator cần thử liên kết lại một lần để lấy mã log.
