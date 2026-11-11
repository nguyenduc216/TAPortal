# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T114023Z-9B9B64

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261111T114023Z-9B9B64`
- Created UTC: 2026-11-11T11:40:23Z
- Tiêu đề: Thẻ trạng thái trượt ngang trên điện thoại
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-11, ảnh chụp trang Đơn hàng trên điện thoại): các thẻ tổng hợp trạng thái dàn trải quá, trên mobile làm dạng slider.
Đã làm: `.cp-stats` (trang Đơn hàng & QR và Đối soát giao dịch) trên màn hình ≤ 640px thành một hàng cuộn ngang có scroll-snap, mỗi thẻ ~72% chiều rộng (thẻ kế tiếp ló ra), thẻ gọn hơn, ẩn thanh cuộn. `customer.js` cuộn thẻ đang lọc (aria-current) vào khung nhìn mà không cuộn trang.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TACustomerPortal.Web/wwwroot/js/customer.js`

## Migration SQL (KHÔNG tự chạy)
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 625/625; git diff --check OK.
- Playwright 360 px (HTML render từ test host): trang không tràn ngang (scrollWidth 360), hàng thẻ cuộn ngang, thẻ đang lọc hiện trong khung.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: a34bca6
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Desktop/tablet không đổi.
