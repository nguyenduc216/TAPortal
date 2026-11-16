# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T103709Z-09BFF1

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261113T103709Z-09BFF1`
- Created UTC: 2026-11-13T10:37:09.314223+00:00
- Tiêu đề: Payment page bridge: moving dot and bank pulse, also under reduced motion
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-13), ảnh trang thanh toán đơn: "Kiểm tra phần này không thấy animation nữa nhỉ", rồi đồng ý làm animation rõ hơn và vẫn chạy khi thiết bị bật giảm chuyển động.

Kiểm tra: CSS nét đứt (`cp-pay-flow`) không đổi và trùng file đang chạy; chỉ dừng khi thiết bị bật giảm chuyển động (luật chung của trang) hoặc đơn đã thanh toán.

Đã làm (chỉ CSS, trang /pay):
- Chấm sáng chạy khách hàng -> ngân hàng -> người bán trong 2,4s, đổi từ xanh sang vàng khi qua icon ngân hàng; icon ngân hàng phóng nhẹ và tỏa vòng sáng khi chấm đi qua.
- Khi thiết bị bật giảm chuyển động, riêng cầu nối thanh toán vẫn chạy (luật cụ thể hơn luật chung).
- Đơn đã thanh toán: không có chấm, giữ đường xanh lá liền nét.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/wwwroot/css/customer.css`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Trang thử dùng CSS thật: vị trí/màu chấm và độ phóng icon đúng tại 0; 0,6; 1,1; 1,3; 2,0; 2,35s; đơn đã thanh toán không có chấm.
- Safe tests và Release build: xem log deploy.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: 7b4d3fa
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa xem trên trang thật; nhánh giảm chuyển động chưa giả lập được trong trình duyệt thử.
