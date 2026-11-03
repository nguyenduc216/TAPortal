# Change TINGEE-INTEGRATION-CHG-20261103T175724Z-F1997E

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T175724Z-F1997E`
- Created UTC: 2026-11-03T17:57:24.882172+00:00
- Tiêu đề: Send non-ASCII JSON unescaped to Tingee (Invalid signature 97)
- Trạng thái: IMPLEMENTED / TESTS_PASS / DEPLOY_PENDING

## Yêu cầu và phạm vi
"Tạo tài khoản Tingee qua API" cho TVGPTA (tên "Tư vấn giải pháp T.A") bị Tingee trả 97 "Invalid signature". Kiểm chứng trên server bằng lệnh chỉ đọc merchant/get-paging (key đã cấu hình): body ASCII → 00; filter "Tư" (escape) → 97; filter "Tư" UTF-8 thô → 00. Nguyên nhân: System.Text.Json mặc định escape ký tự ngoài ASCII; Tingee tính chữ ký trên body đã giải mã. Sửa: TingeeClient dùng JavaScriptEncoder.UnsafeRelaxedJsonEscaping (body chỉ gửi tới API Tingee, không render HTML).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeClient.cs`.
- MODIFY `tests/TACustomerPortal.Tests/SafeTingeeDirectMerchantTests.cs` (+1 test).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 324/324.

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
- Ảnh hưởng cả Customer Portal (tên khách hàng/nội dung có dấu trong create-va, QR purpose): trước đây cũng sẽ lỗi 97, nay hết.
