# Change TINGEE-INTEGRATION-CHG-20261104T172006Z-EE62CC

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T172006Z-EE62CC`
- Created UTC: 2026-11-04T17:20:06.278874+00:00
- Tiêu đề: Customer Portal: bank logo opens the Bank Link popup (SDK); Sync adds accounts linked there
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): đưa Bank Link (SDK) vào trang "Tài khoản ngân hàng → Liên kết tài khoản" của Customer Portal (hoadon). Bấm logo ngân hàng là mở luôn popup liên kết; loại tài khoản do SDK hỏi (bỏ bước "2. Loại tài khoản").

- `BanksController.LinkSession(bin)` (POST, chỉ OWNER/quản lý): create-bank-link-session `bank-link` cho merchant của doanh nghiệp, `bankName` = mã ngân hàng, không gửi loại tài khoản, metadata `{source: ta-customer, customer, bank}`; trả JSON `{ok, url, script}`. URL không phải https `*.tingee.vn` bị từ chối. Lỗi đối tác qua `PartnerText.Scrub`.
- `customer.js`: bấm logo (capture phase, trước soft navigation) → gọi LinkSession → nạp script SDK → `Setup` + `Url.Open` (popup). onSuccess → gửi form "Đồng bộ trạng thái" ẩn → về danh sách với thông báo.
- `Sync`: ngoài cập nhật trạng thái, thêm các tài khoản `active` trên đối tác (ngân hàng có trong danh mục) chưa có trong danh sách của doanh nghiệp.
- `TingeeOptions`: `IsProduction`, `BankLinkSdkScript`, `IsBankLinkSdkUrl` (URL đối tác nằm trong Integrations, không nằm trong source Customer Portal).
- Không có JS / chưa kích hoạt dịch vụ: logo vẫn là link tới form cũ (fallback).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Integrations/Tingee/TingeeOptions.cs`
- MODIFY `src/TACustomerPortal.Web/Controllers/BanksController.cs`
- MODIFY `src/TACustomerPortal.Web/Views/Banks/Add.cshtml`
- MODIFY `src/TACustomerPortal.Web/wwwroot/js/customer.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs` (3 test mới, fake hỗ trợ session/VA list)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (không có thay đổi SQL)

## Test và kết quả
- `dotnet build TAPortal.sln -c Release --no-restore`: 0 lỗi (1 cảnh báo CS8604 có sẵn ở `PaymentPagesNeverNamePartners`).
- Safe tests: 405/405 pass (mới: `ABankLogoOpensTheLinkPopupSessionForTheCustomersMerchant`, `ViewersCannotOpenTheLinkPopup`, `SyncAddsAccountsLinkedInThePopup`).
- Popup SDK thật trên trình duyệt: NOT_VERIFIED (cần thử trên hoadon sau deploy).

## Publish
- Project: TACustomerPortal.Web (invoice). Integrations đổi (thêm thuộc tính, không đổi hành vi) — Admin/Webhooks republish ở lần deploy sau.
- Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem hand-off
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Màn hình popup là của đối tác: có thể hiện tên/logo đối tác — cần operator kiểm tra (Admin Tingee Sandbox mục 5 dùng để xem trước).
- Tài khoản liên kết qua popup được ghi vào danh sách khi popup báo thành công (Sync). Nếu khách đóng trình duyệt trước đó, bấm "Đồng bộ trạng thái" ở trang danh sách sẽ thêm vào. Chưa xử lý webhook `bank-link` để tự ghi (việc sau).
