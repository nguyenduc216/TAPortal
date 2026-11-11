# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T112456Z-A427C0

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T112456Z-A427C0`
- Created UTC: 2026-11-11T11:24:56Z
- Tiêu đề: Chi tiết đơn: sửa giao diện mobile, đổi thời hạn / tạo lại QR
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-11, ảnh chụp trên điện thoại): (1) giao diện chi tiết đơn trên mobile chưa chuẩn; (2) chưa đổi được thời hạn QR (ví dụ QR 1 ngày muốn thành 4 ngày); (3) chưa biết cách hủy QR để tạo QR mới.
Đã làm:
- Mobile: `.cp-grid--2` dùng `minmax(0, 1fr)` và mọi phần tử của `.cp-grid` có `min-width: 0` — trước đây bảng hàng hóa đẩy cột rộng hơn màn hình (tràn ngang, top bar chỉ phủ một phần). Bảng hàng hóa (`cp-table--stack`) thành thẻ trên màn hình ≤ 640px. Link QR cho khách: ô link chiếm cả dòng, hai nút chia đôi (≤ 560px). Ô ngày/giờ/phút ẩn icon trên điện thoại để thấy được số.
- Đổi thời hạn / tạo lại QR: khối "Đổi thời hạn / tạo lại QR" trong thẻ QR (thu gọn khi QR đang mở; mở sẵn "Tạo QR thanh toán" khi đơn nháp/hết hạn), nhập ngày/giờ/phút (mặc định thời hạn hiện tại, tối đa 30 ngày) và tài khoản nhận tiền. `IssueQr` nhận cả đơn đang chờ/đã thu một phần (`OrderMath.CanReissueQr`); `OrderPayments.ReplaceQrAsync` tạo QR mới cho số còn phải thu, hiệu lực tính từ lúc tạo, rồi mới đóng QR động cũ (best effort); nếu tạo QR mới lỗi thì QR cũ giữ nguyên. Link trang thanh toán cho khách không đổi (token theo đơn).
- `UpdateOrderAsync` lưu thêm `QrExpireMinutes` (cột đã có, không cần migration).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Controllers/OrdersController.cs`, `src/TACustomerPortal.Web/Services/OrderPayments.cs`, `src/TACustomerPortal.Web/Services/PaymentStore.cs`, `src/TACustomerPortal.Web/Views/Orders/Details.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 625/625 (mới: AnOpenQrCanBeReplacedWithANewLifetimeAndTheOldQrIsClosed, AFailedReplacementKeepsTheCurrentQr); codec tests OK; git diff --check OK.
- Trình duyệt (Playwright, 360 px, HTML render từ test host): scrollWidth = 360 (không tràn ngang), bảng hàng hóa dạng thẻ, khối đổi thời hạn hiển thị đủ số.

## Publish
- invoice DEPLOYED 2026-11-11 11:34Z (artifact ce8fe1db…3d7e, health 200). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 77e4ec7
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Dịch vụ thanh toán chỉ xóa được QR động của MB/BIDV/VietinBank; ở ngân hàng khác QR cũ có thể vẫn quét được tới hết hạn. Tiền chuyển vào QR cũ vẫn được ghi nhận cho đơn (khớp theo tài khoản + nội dung); nếu chuyển trùng sẽ hiện "Trả dư" để đối soát.
- Không có nút "chỉ hủy QR" riêng: tạo QR mới đã thay QR cũ; muốn dừng hẳn thì dùng "Hủy đơn" (đơn chưa nhận tiền).
