# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T115031Z-7CC4D1

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T115031Z-7CC4D1`
- Created UTC: 2026-11-11T11:50:31Z
- Tiêu đề: Danh sách đơn: chấm màu trạng thái trước mã đơn, mô tả thu gọn bấm để mở
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-11, ảnh danh sách đơn trên điện thoại): thêm chấm nhỏ màu theo trạng thái đơn trước mã đơn; mô tả dài dưới mã đơn hiển thị "…", bấm để mở rộng.
Đã làm: `Views/Orders/Index.cshtml` — `.cp-dot--{blue|gold|green|red|soft}` theo màu badge trạng thái (`PaymentText.OrderStatus`), mã đơn không xuống dòng; mô tả là nút `[data-cp-clamp]` một dòng (tối đa 200px, có title), bấm thì mở/đóng (aria-expanded). JS trong `customer.js`.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Views/Orders/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TACustomerPortal.Web/wwwroot/js/customer.js`

## Migration SQL (KHÔNG tự chạy)
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 625/625; Playwright 360 px: chấm 4 màu đúng trạng thái, mô tả dài một dòng "…", bấm mở ra (aria-expanded=true).

## Publish
- invoice DEPLOYED. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 075af61
- Merge: main
