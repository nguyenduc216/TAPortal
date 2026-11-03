# Change TINGEE-INTEGRATION-CHG-20261103T174253Z-B2EEA8

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T174253Z-B2EEA8`
- Created UTC: 2026-11-03T17:42:53.117363+00:00
- Tiêu đề: Verify an existing Tingee merchant id with Tingee before linking
- Trạng thái: IMPLEMENTED / TESTS_PASS / DEPLOY_PENDING

## Yêu cầu và phạm vi
Operator hai lần nhập số điện thoại (888578074, 898386060) vào "Liên kết merchant có sẵn" của khách hàng TVGPTA; ô này chỉ lưu số vào dbo.TingeeMerchants mà không hỏi Tingee, nên lưu một merchant không tồn tại (get-paging vẫn 0 merchant con). Sửa: ở chế độ Master có key, Admin gọi /v1/merchant/get-paging và chỉ lưu khi ID có trong danh sách merchant con (lưu kèm số điện thoại Tingee trả về); lỗi Tingee/mạng ⇒ không lưu. Direct/không có key giữ hành vi cũ. Hướng dẫn trên form nói rõ Merchant ID không phải số điện thoại.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeApi.cs` — TingeeMerchantItem, ITingeeApi.GetMerchantsAsync (merchant/get-paging).
- MODIFY `src/TAPortal.Web/Controllers/CustomerTingeeActions.cs`, `Views/Customers/Tingee.cshtml`.
- MODIFY tests: SafeAdminTingeeTests (+1 test, fake danh sách merchant con), fakes của SafeAdminTingeeSandboxTests/SafeCustomerPaymentsTests.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 323/323.

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
- Liên kết sai hiện có (TVGPTA ↔ #898386060) không tự xóa: operator bấm "Hủy liên kết" rồi dùng "Tạo tài khoản Tingee qua API".
