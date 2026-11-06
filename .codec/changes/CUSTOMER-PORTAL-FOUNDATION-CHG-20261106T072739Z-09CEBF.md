# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261106T072739Z-09CEBF`
- Created UTC: 2026-11-06T07:27:39.763527+00:00
- Tiêu đề: Trang QR: anh chia se, nut Chuyen khoan mo app ngan hang, luu thong tin hoa don khi tra cuu duoc MST
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-06), trang thanh toán QR công khai (/pay/{token}):
1. Link chia sẻ chưa có ảnh và mô tả ⇒ ảnh logo T.A (hoadontudong.vn dùng cùng logo), mô tả "Thanh toán " + tiêu đề đơn hàng.
2. Nút "Chuyển khoản" ngay dưới nội dung đơn hàng và một nút ở cuối trang; bấm vào mở ứng dụng ngân hàng như khi bấm ảnh QR trên Zalo.
3. Nút lưu thông tin xuất hóa đơn để khách sửa nếu sai: chỉ cho đơn tạo khi chưa có thông tin xuất hóa đơn, và chỉ lưu khi tra cứu được mã số thuế.

Sửa:
- `<head>`: `description`, Open Graph (og:title "<tiêu đề> · <người bán>", og:description "Thanh toán <tiêu đề>", og:url, og:image `/images/og-share.png` 1200×630 dựng từ `ta-logo.png`), twitter:card summary_large_image. URL tuyệt đối theo host của request.
- `BankAppLinks`: đọc QR VietQR của đơn (EMVCo tag 38 → BIN + tài khoản, 54 số tiền, 62/08 nội dung) và dựng deeplink VietQR `https://dl.vietqr.io/pay?app=<app>&ba=<tài khoản>@<BIN>&am=<tiền>&tn=<nội dung>` cho 30 ứng dụng ngân hàng (danh sách api.vietqr.io ngày 2026-11-06, có cả Android và iOS). Hai nút "Chuyển khoản" (dưới tiêu đề/trạng thái và cuối trang, kèm số tiền) mở hộp chọn ngân hàng (`<dialog>`, đóng bằng ×, Escape, chạm ra ngoài). Chỉ hiện khi QR còn hiệu lực và đọc được. Nội dung chuyển khoản lấy đúng từ QR, nên giao dịch khớp đơn như khi quét.
- Thông tin xuất hóa đơn: chỉ sửa được khi đơn không có khách hoặc khách chưa có MST (khách người bán đã ghi MST ⇒ hiện chỉ đọc "do người bán cung cấp", server từ chối bài gửi). Nút Lưu luôn hiện; ô tên chỉ đọc, tự điền khi tra cứu. Server bắt buộc MST doanh nghiệp 10 số (10-3), tra cứu đăng ký kinh doanh: tìm thấy ⇒ lưu MST + tên theo đăng ký (bỏ qua tên gõ tay); không thấy / chưa tra cứu được ⇒ báo lỗi, không lưu.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Services/BankAppLinks.cs`
- ADD `src/TACustomerPortal.Web/wwwroot/images/og-share.png`
- MODIFY `src/TACustomerPortal.Web/Controllers/PayController.cs`, `Models/PaymentModels.cs` (PayPageVm.Transfer), `Views/Pay/Index.cshtml`, `wwwroot/css/customer.css`
- MODIFY `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`, `SafeCustomerPaymentsTests.cs` (factory dùng tra cứu MST giả)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 479/479: thẻ OG, hai nút Chuyển khoản, deeplink từ QR (BIN 970422, tài khoản, 54000, DH2611060001), QR hỏng ⇒ không có nút; MST sai/không tìm thấy/chưa tra cứu được ⇒ 400 và không lưu; tìm thấy ⇒ lưu tên theo đăng ký; khách đã có MST ⇒ chỉ đọc, bài gửi giả bị từ chối.
- Xem giao diện hộp chọn ngân hàng bằng trang thử dùng customer.css thật.

## Publish
- invoice (TACustomerPortal.Web). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 0bb7fe1
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Deeplink do VietQR cung cấp (dl.vietqr.io); ứng dụng ngân hàng tự quyết điền sẵn đến đâu (một số app chỉ mở màn hình chuyển khoản). Trên máy tính: quét QR bằng điện thoại.
- Biểu tượng ứng dụng tải từ cdn.vietqr.io.
- Zalo/Facebook lưu đệm bản xem trước: link đã chia sẻ trước đây có thể vẫn hiện bản cũ một thời gian.
- "Đơn tạo khi chưa có thông tin" được xác định theo khách hàng hiện tại của đơn (không có khách hoặc khách chưa có MST).
