# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T010304Z-58774A

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T010304Z-58774A`
- Created UTC: 2026-11-13T01:03:04.168293+00:00
- Tiêu đề: Card thong ke dang slide ngang tren mobile (portal + hoa don)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-13): "tất cả các card trên portal và hóa đơn đều đưa về dạng slide ở giao diện mobile" (ảnh trang Việc của tôi: 4 card KPI xếp dọc chiếm cả màn hình).

Đã làm (chỉ CSS):
- Portal nội bộ (`ta-design.css`): trên màn ≤767px, nhóm `.ta-kpi-grid` (Việc của tôi, Tổng quan, Đối soát thanh toán, Gói dịch vụ) cuộn ngang, mỗi lần một card, card sau ló ra mép.
- Hóa đơn/khách hàng (`customer.css`): trên màn ≤640px, nhóm `.cp-grid` chứa card thống kê hoặc card dịch vụ (Tổng quan, Gói dịch vụ) trượt ngang như `.cp-stats` (Đơn hàng, Giao dịch đã có sẵn). Nhóm card có form (Thông báo, hồ sơ) giữ nguyên.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/wwwroot/css/ta-design.css`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; trang thử dùng CSS thật ở khổ 375px: card cuộn ngang, trang không tràn ngang.

## Publish
- portal, invoice. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: 14ac25b
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không.
