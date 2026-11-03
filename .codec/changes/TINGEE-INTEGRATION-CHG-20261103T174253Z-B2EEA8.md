# Change TINGEE-INTEGRATION-CHG-20261103T174253Z-B2EEA8

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T174253Z-B2EEA8`
- Created UTC: 2026-11-03T17:42:53.117363+00:00
- Tiêu đề: Verify an existing Tingee merchant id with Tingee before linking
- Trạng thái: DEPLOYED (portal, invoice)

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
- portal: DEPLOYED 2026-11-03 17:45 UTC, source 7a05e6d, artifact 0fca43ec4172fa8d51848b48f5ded597413451f56071ca001fa3ed02665d51cf, package DBAD4E53CFC18085B0B43DEB8157E8DECB067647DA544E6C9AC6B4D150545B58, health 200.
- invoice: DEPLOYED 2026-11-03 17:45 UTC, source 7a05e6d, artifact fc2967bcea72cad7bdc42605ed853b9cab21d2fb5f467d52a19cc796f4c9b592, package AAC2B7432C958158B182F41B868DEB8345416CD652BAF4B14464DF64AA2EDF1D, health 200.
- Sự cố Git: commit đầu (3a3a488, chưa push) lỡ chứa 467 file tests/playwright/node_modules (untracked, không phải của agent) do `git add tests`; đã reset --soft và commit lại chỉ các file của change (7a05e6d). Không có file playwright nào trên origin.

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Liên kết sai hiện có (TVGPTA ↔ #898386060) không tự xóa: operator bấm "Hủy liên kết" rồi dùng "Tạo tài khoản Tingee qua API".
