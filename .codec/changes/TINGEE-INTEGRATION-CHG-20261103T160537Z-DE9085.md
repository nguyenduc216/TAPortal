# Change TINGEE-INTEGRATION-CHG-20261103T160537Z-DE9085

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261103T160537Z-DE9085`
- Created UTC: 2026-11-03T16:05:37.499169+00:00
- Tiêu đề: Direct mode sends T.A's own Merchant ID (Tingee 1096)
- Trạng thái: DEPLOYED (portal, invoice) / WAITING_MERCHANT_ID

## Yêu cầu và phạm vi
Sandbox production (Direct, key T.A): liên kết MB Bank trả **Tingee 1096 "Giá trị Merchant ID không được bỏ trống"**. Nguyên nhân (đã chứng minh bằng phản hồi Tingee): chế độ Direct không gửi merchantId, nhưng create-va bắt buộc. Sửa: cấu hình `Tingee:MerchantId` (Merchant ID của chính merchant); ở Direct mode TingeeApi gửi ID này thay cho id theo khách hàng; không cấu hình thì giữ hành vi cũ (không gửi).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeOptions.cs` (MerchantId), `TingeeApi.cs` (Mid() + delete-va query).
- MODIFY `src/TAPortal.Web/{Models/TingeeSandboxModels.cs, Controllers/TingeeSandboxController.cs, Views/TingeeSandbox/Index.cshtml}` — hiển thị Merchant ID, mẫu cấu hình có "MerchantId", chặn liên kết NH khi thiếu (báo rõ 1096).
- MODIFY tests `SafeTingeeDirectMerchantTests.cs` (+1), `SafeAdminTingeeSandboxTests.cs` (+1).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Build Release 0 lỗi; Safe tests 321/321.

## Publish
- portal: DEPLOYED 2026-11-03 16:10 UTC, source cebf5e4, artifact ed08f40c211fe73fe3ecee7c328780b71ab118c126a14911bb8cb609de2c6c1c, package 6ED702F6D95010A10799F8C7CA0ABE05376D600647E4D3FF8679CB960744EFCE, health 200.
- invoice: DEPLOYED 2026-11-03 16:10 UTC, source cebf5e4, artifact 6489bd76d88540e4264d5d99de2e7ea67dbf1d8ff825be3967b39b8c0e8386eb, package B46594EAA5F3BEEB53B68011D72BE947F5622478E11E4F1B44CB2C2739D682A5, health 200.
- Chờ operator thêm "MerchantId" vào appsettings site Portal. Ghi chú: /v1/merchant/get-paging chỉ dành cho Master Merchant (Tingee 1133), nên merchant thường lấy Merchant ID trên dashboard Tingee hoặc hỏi Tingee.

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Ảnh hưởng Customer Portal ở Direct mode: khi đặt MerchantId, mọi khách hàng dùng chung merchant T.A (đã cảnh báo trên trang Tingee của Admin). Operator cần thêm "MerchantId" vào appsettings site Portal.
