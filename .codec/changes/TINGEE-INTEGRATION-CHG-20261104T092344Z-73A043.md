# Change TINGEE-INTEGRATION-CHG-20261104T092344Z-73A043

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T092344Z-73A043`
- Created UTC: 2026-11-04T09:23:44.190498+00:00
- Tiêu đề: Sandbox: đếm ngược thời gian hết hạn QR động
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh mục 3 Sandbox): "Bổ sung phần đếm giây lùi để còn biết bao nhiêu giây" cho QR động.
- Dòng "Hết hạn" có nhãn `còn m:ss` (`data-ta-qr-countdown`, `data-expires` = hạn UTC ISO), cập nhật mỗi giây; còn ≤60 giây đổi màu cam; hết hạn đổi đỏ "đã hết hạn", dừng kiểm tra trạng thái và báo "QR đã hết hạn, chưa nhận đủ tiền"; nhận đủ tiền thì nhãn thành "đã thanh toán".

Lần 2 (operator, ảnh "còn 15:00" đứng yên): "phải đếm giây lùi realtime, hết về 0 thì khoá QR và ra thông báo hết hạn".
- Đếm từ số giây còn lại do server tính lúc render (`data-seconds-left`, dùng `TimeProvider` của app), không phụ thuộc đồng hồ máy người dùng.
- Về 0: ảnh QR bị làm mờ + lớp phủ "QR đã hết hạn" (`data-ta-qr-image` `.is-locked`), dừng kiểm tra trạng thái, báo vàng hết hạn; mở lại trang khi đã hết hạn thì khoá ngay, không gọi kiểm tra. Nhận đủ tiền: lớp phủ xanh "Đã thanh toán".
- Kiểm tra logic JS bằng DOM giả (node): đếm 2→1→"đã hết hạn", khoá QR, 0 lần gọi trạng thái khi đã hết hạn lúc tải.

Lần 3 (operator: "vẫn đứng"): nguyên nhân là soft navigation của shell (ta-shell.js thay <main> sau khi POST "Tạo QR" mà không chạy lại script trang), nên đếm ngược và kiểm tra trạng thái không khởi động cho QR mới.
- `ta-tingee-sandbox.js`: phần theo trang gom vào `init()`, chạy lúc tải và mỗi `ta:load`; `ta:before-swap` dừng mọi timer cũ.
- Script nạp một lần trong `_Layout.cshtml` (bỏ `@section Scripts` ở TingeeSandbox và TingeeLogs) để vào Sandbox bằng soft navigation vẫn có script; trang khác không có phần tử nên không làm gì.
- Kiểm tra bằng DOM giả: tải trang không có QR → `ta:load` có QR → đếm 2, 1, hết hạn, khoá QR.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`, `src/TAPortal.Web/Views/TingeeLogs/Index.cshtml`, `src/TAPortal.Web/Views/Shared/_Layout.cshtml`, `src/TAPortal.Web/wwwroot/js/ta-tingee-sandbox.js` (deploy portal).
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- Safe tests 363/363 (working tree).

## Publish
- portal (TAPortal.Web → TA.Portal, H:\WEBDEPLOYED\T.A\portal_released): DEPLOYED 2026-11-04 16:26 (UTC+7), commit e7a46dc, artifact SHA256 525ef8f755ab68e8d437e82fec1254772687d238738c7027363eb94dc6bab1b1, package 9159EF8073B3D6297E4C033C38AF9CF81024D5A19F80ACF94FEB5CA20C00532F; backup C:\TADeploy\backups\portal-20261104-162611; health HTTP 200.
- portal lần 2 (khoá QR khi hết hạn): DEPLOYED 2026-11-04 16:33 (UTC+7), commit 5138179, artifact SHA256 ece5a424fe016d079c275734a7c82ea2cd3738e653ff05312341ee7605ff8e44, package A762A843FBE81464980BA792D6004B581471BDC0ECC6526B162D925ECB617C00; backup C:\TADeploy\backups\portal-20261104-163349; health HTTP 200.
- portal lần 3 (soft navigation): DEPLOYED 2026-11-04 16:48 (UTC+7), commit 6275e88, artifact SHA256 e1c8fd29355cee982c245bb6107e337673fb1a4007ad5e090676b0f3846ea9a4, package 849C32684C0EC1C583B7F00FD46FE355965CC863FC5351DE95E203C2779612A6; backup C:\TADeploy\backups\portal-20261104-164813; health HTTP 200.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: e7a46dc, 5138179, 6275e88 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
