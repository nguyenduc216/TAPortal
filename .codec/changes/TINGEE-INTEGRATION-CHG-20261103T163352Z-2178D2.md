# Change TINGEE-INTEGRATION-CHG-20261103T163352Z-2178D2

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T163352Z-2178D2`
- Created UTC: 2026-11-03T16:33:52.341291+00:00
- Tiêu đề: Tingee Sandbox works with a Master Merchant key on a linked child merchant
- Trạng thái: IMPLEMENTED / TESTS_PASS / DEPLOY_PENDING

## Yêu cầu và phạm vi
Kiểm tra trên server (chỉ đọc, dùng key đã cấu hình, không in key): `/v1/merchant/get-paging` trả code 00 với 0 merchant ⇒ key của T.A là **Master Merchant** (merchant thường nhận 1133) và chưa có merchant con. Tài liệu create-va: merchantId "Bắt buộc nếu kết nối bằng tài khoản Master Merchant" ⇒ giải thích lỗi 1096. Operator chọn: tạo merchant con cho T.A qua khách hàng T.A trong Admin (tab Tingee → Tạo tài khoản Tingee qua API, Mode=Master).
Sandbox được sửa để chạy cả Master: mọi lệnh Tingee dùng một merchant con đã liên kết với khách hàng trong Admin (dbo.TingeeMerchants), chọn ở mục 1; không nhận merchantId tùy ý từ request.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Controllers/TingeeSandboxController.cs` — Resolve(merchant): Direct → merchant của T.A; Master → merchant con đã liên kết; mọi action nhận `merchant`.
- MODIFY `src/TAPortal.Web/Models/TingeeSandboxModels.cs`, `Services/TingeeSandboxInbox.cs` (ITingeeSandboxMerchants, đọc TingeeMerchants JOIN Customers), `Program.cs`, `Views/TingeeSandbox/Index.cshtml` (chọn merchant con, mẫu cấu hình Mode=Master).
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs` (+2 test Master).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 322/322.

## Publish
- Project:
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
