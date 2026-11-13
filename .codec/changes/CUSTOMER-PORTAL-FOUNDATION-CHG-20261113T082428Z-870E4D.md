# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T082428Z-870E4D

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T082428Z-870E4D`
- Created UTC: 2026-11-13T08:24:28.369388+00:00
- Tiêu đề: Card groups always one sliding row on every screen (portal + invoice)
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-13): "Luôn đảm bảo các card luôn slide trên 1 hàng ở các chức năng có card" (ảnh Đơn hàng & QR trên desktop: 11 card xếp 3 hàng).

Đã làm (chỉ CSS), ở mọi khổ màn hình:
- Hóa đơn (`customer.css`): `.cp-stats` (Đơn hàng, Giao dịch) và `.cp-grid` chứa card thống kê/dịch vụ luôn là 1 hàng. Tối đa 4 card chia đều chiều rộng; nhiều hơn thì trượt ngang, card thứ 5 ló ra mép. Màn chuột có thanh cuộn mảnh; màn cảm ứng ẩn thanh cuộn. Mobile giữ 1 card mỗi lần.
- Portal nội bộ (`ta-design.css`, `ta-website.css`): `.ta-kpi-grid` (4 card/khung, `--3` là 3) và `.ta-web-stats` cùng cách trên; bỏ luật grid 2 cột/1 cột cũ.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `src/TAPortal.Web/wwwroot/css/ta-design.css`, `src/TAPortal.Web/wwwroot/css/ta-website.css`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Trang thử dùng CSS thật ở 1500px, 1000px, 375px: mọi nhóm 1 hàng; 11 card hiện 4 + ló card 5; 3-4 card lấp đầy hàng; mobile 1 card + ló.
- Safe tests và Release build: xem log deploy.

## Publish
- invoice, portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xem trên trang thật (không đăng nhập được); con lăn chuột dọc không cuộn ngang, dùng Shift+lăn, kéo thanh cuộn hoặc touchpad.
