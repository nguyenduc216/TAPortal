# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T101303Z-AC77C1

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T101303Z-AC77C1`
- Created UTC: 2026-11-12T10:13:03.846527+00:00
- Tiêu đề: Trang đăng nhập/đăng ký: minh họa thu tiền → hóa đơn và nền động
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-12): trang đăng nhập Hóa đơn tự động "hơi đơn điệu, thiếu thông tin". Bên vàng thêm thông tin hoặc hình ảnh cho cuốn hút; bên trắng thêm hiệu ứng nền chạy bên dưới để nhìn vào nhận ra đây là hóa đơn điện tử.

Đã làm:
- Bên vàng (`_AuthShowcase`): minh họa một lần thu tiền: chip "Khách quét mã QR" → chip "+1.250.000 ₫ Tiền đã về tài khoản" → thẻ hóa đơn điện tử đóng dấu "Đã xuất" → chip "Đã gửi hóa đơn". Lặp 9 giây, kèm dải 4 bước (Quét QR, Tiền về, Xuất hóa đơn, Báo khách) sáng theo nhịp. Số liệu là mẫu, không phải dữ liệu. Thêm vệt sáng xoay chậm trên nền vàng. Ẩn minh họa trên màn hình < 768 px.
- Bên trắng (`_AuthBackdrop`): lưới chấm xanh nhạt, hai quầng màu vàng/xanh, 12 hình tờ hóa đơn, mã QR, biên lai, đồng xu trôi chậm lên phía sau thẻ đăng nhập.
- Áp dụng cho trang đăng nhập, đăng ký, đăng ký thành công, quên/đặt lại mật khẩu (minh họa) và điều khoản (nền). Toàn bộ là trang trí (aria-hidden), không bắt sự kiện chuột; khi người dùng bật giảm chuyển động thì đứng yên.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Views/Account/_AuthShowcase.cshtml`, `src/TACustomerPortal.Web/Views/Account/_AuthBackdrop.cshtml`
- MODIFY `src/TACustomerPortal.Web/Views/Account/{Login,Register,RegisterDone,ForgotPassword,ResetPassword,Terms}.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 663/663.
- Chạy thử cục bộ: 1440x860 (đăng nhập, đăng ký) dừng hoạt cảnh ở 6,5 giây: chip, thẻ hóa đơn, dấu "Đã xuất" không chồng lên nhau; 375 px: minh họa ẩn, nền động vẫn chạy, không tràn ngang.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/auth-visual
- Commit SHA: 889619c
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Quyết định: dựng bằng HTML/CSS/SVG nội tuyến, không thêm ảnh, không thêm script.
