# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T073457Z-C2D04D

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T073457Z-C2D04D`
- Created UTC: 2026-11-06T07:34:57.923292+00:00
- Tiêu đề: Trang QR: dau tich xanh khi da thanh toan, chi tiet hang hoa duoi tieu de
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-06), trang /pay: (1) khi đơn đã thanh toán đủ, icon giữa đổi thành icon thành công màu xanh lá; (2) phần "Chi tiết hàng hóa, dịch vụ" đưa lên ngay dưới tiêu đề đơn hàng.

Sửa: cầu nối có `is-paid` khi trạng thái PAID ⇒ icon `circle-check`, nút và đường nối xanh lá, dừng hiệu ứng chạy (trang đang mở tự tải lại khi trạng thái đổi nên khách thấy ngay). Chi tiết hàng hóa nằm ngay sau tiêu đề/trạng thái, nút "Chuyển khoản" đầu tiên ngay sau chi tiết hàng hóa (rộng hết thẻ).

Ghi chú vận hành: đơn DH261106-0001 đã nhận webhook thanh toán lúc 2026-11-06T07:27:53Z (đối tác gửi lại), PAID 54.000 đ; xuất hóa đơn tự động bị nhà cung cấp hóa đơn từ chối mã 400 CallSignServiceFail (dịch vụ ký số của tài khoản MISA) — cần doanh nghiệp/MISA kiểm tra chữ ký số rồi bấm "Xuất lại hóa đơn".

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 479/479 (đã thanh toán ⇒ `cp-pay__bridge is-paid`; chi tiết hàng hóa nằm giữa tiêu đề và nút Chuyển khoản / thông tin hóa đơn).

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
