# Change TINGEE-INTEGRATION-CHG-20261104T183759Z-3C176A

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T183759Z-3C176A`
- Tiêu đề: Customer Portal: QR transfer content without '-' (fix 400 Purpose of transaction is wrong format)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-05): tạo QR cho đơn DH261105-0001 (MB Bank, VA) lỗi "Không tạo được QR (mã 400): Purpose of transaction is wrong format".

Nguyên nhân: `OrderPayments` gửi `purpose` = mã đơn `DH261105-0001`; nội dung chuyển khoản chỉ được chứa chữ và số (sandbox Admin gửi "TA SANDBOX" thì chạy). Sửa: `OrderMath.TransferContent` bỏ ký tự khác chữ/số (`DH2611050001`) cho cả QR động (purpose) và QR tĩnh (content); `extraInfo` vẫn giữ mã đơn gốc. Trang chi tiết đơn hiển thị đúng nội dung chuyển khoản này. Đơn đã lỗi chỉ cần bấm "Tạo QR" lại.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Services/OrderPayments.cs`
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Details.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 396/396 pass. Tạo QR thật: kiểm tra sau deploy.

## Publish
- Project: TACustomerPortal.Web (invoice). Trạng thái: xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Merge: PENDING_APPROVAL

## Rủi ro, quyết định và việc còn lại
- Khi làm đối soát giao dịch theo nội dung (QR tĩnh), so khớp phải dùng cùng `TransferContent` (bỏ ký tự đặc biệt).