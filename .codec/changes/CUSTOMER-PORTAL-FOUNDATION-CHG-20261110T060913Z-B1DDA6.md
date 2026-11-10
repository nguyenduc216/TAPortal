# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T060913Z-B1DDA6`
- Created UTC: 2026-11-10T06:09:13.454976+00:00
- Tiêu đề: Tai anh QR kem thong tin va sua link mo app ngan hang dien san
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-10), trang thanh toán công khai (/pay/<token>):
1. Chưa tải được hình QR: cần tải một ảnh gồm mã QR và thông tin đi kèm bên dưới.
2. Bấm "Chuyển khoản" chỉ mở app ngân hàng, không điền sẵn số tài khoản, số tiền, nội dung và không mở màn hình chuyển khoản.
Đã làm:
- Nút "Tải ảnh mã QR" dưới mã QR (và trong hộp chọn ngân hàng): trình duyệt vẽ một PNG 1440 px gồm tên người bán, tiêu đề đơn, mã QR, số tiền cần thanh toán, ngân hàng nhận/số tài khoản, nội dung chuyển khoản, mã đơn hàng, hạn QR; tải về tên `QR-<mã đơn>.png` và hiện ảnh để nhấn giữ lưu (trình duyệt trong Zalo/Messenger không tải file).
- Nguyên nhân lỗi 2: link VietQR gửi `ba=<tài khoản>@<BIN>` và thiếu `url`; VietQR cần mã ngân hàng ngắn (mb, vcb…) và `url` quay lại. Đã thử trực tiếp: với `ba=…@mb&url=…` VietQR trả link `mbbank://applink?targetPage=QRPay&qrContent=<QR>` (màn hình thanh toán QR của MB, thông tin lấy từ QR nên khóa như khi quét).
- `BankAppLinks`: bảng BIN → mã ngân hàng VietQR, tham số `url` = chính trang thanh toán; cờ Autofill theo danh sách VietQR 2026-11-10 (chỉ MB, BIDV, VietinBank, ACB, OCB điền sẵn trên cả Android và iOS). Hộp chọn ngân hàng chia 2 nhóm: "Điền sẵn thông tin chuyển khoản" và "Ngân hàng khác: chỉ mở ứng dụng" (gợi ý tải ảnh QR rồi quét từ ảnh).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TACustomerPortal.Web/Services/BankAppLinks.cs`, `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy: không áp dụng
- Preflight / Verify / Rollback: không áp dụng
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 622/622; Codec unit tests OK; `git diff --check` sạch.
- Trình duyệt (bản HTML trang thanh toán từ test, khung điện thoại 375 px): ảnh QR 1440×1940 đúng nội dung; hộp chọn ngân hàng 2 nhóm, không tràn ngang; link MB đúng định dạng mới.
- Link VietQR thử trên trình duyệt giả lập Android: `ba=…@mb` + `url` trả về link QRPay của MB kèm qrContent.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: bd93fff
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Các ngân hàng khác (Vietcombank, Techcombank, VPBank…) VietQR chưa hỗ trợ điền sẵn: chỉ mở ứng dụng; người trả dùng ảnh QR đã tải để quét từ ảnh.
- Chưa thử trên điện thoại thật với app ngân hàng thật.
