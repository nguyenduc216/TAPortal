# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T070417Z-914962

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261110T070417Z-914962`
- Created UTC: 2026-11-10T07:04:17.142219+00:00
- Tiêu đề: Nut sao chep tren trang thanh toan va danh sach app ngan hang tu VietQR
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-10): chỉ 5 app ngân hàng điền sẵn được (giới hạn của VietQR/ngân hàng). Làm cả hai: (1) nút Sao chép số tài khoản, số tiền, nội dung chuyển khoản; (2) danh sách app tự cập nhật theo VietQR.
Đã làm:
- Trang thanh toán (QR đang mở): các dòng Ngân hàng nhận, Số tài khoản, Số tiền, Nội dung chuyển khoản; ba dòng sau có nút sao chép (số tiền sao chép dạng số nguyên không dấu chấm). Hộp chọn ngân hàng nhóm "chỉ mở ứng dụng" gợi ý thêm việc sao chép.
- `VietQrBankAppCatalog` (`IBankAppCatalog`, singleton): đọc https://api.vietqr.io/v2/android-app-deeplinks và ios-app-deeplinks tối đa 12 giờ/lần, chạy nền không chặn trang; lỗi thì thử lại sau 10 phút và giữ danh sách đang có (ban đầu là danh sách dựng sẵn `BankAppLinks.Apps`). `BankAppLinks.Merge`: chỉ app có trên cả hai hệ điều hành, "điền sẵn" khi cả hai cùng autofill=1, mã app phải khớp `^[a-z0-9-]{1,30}$`, giữ tên ngắn của app đã biết, bỏ qua danh sách dưới 10 app.
- Danh sách thật hôm nay ghép ra 33 app, điền sẵn: VietinBank, BIDV, OCB, ACB, MB.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TACustomerPortal.Web/Services/BankAppCatalog.cs`
- MODIFY `src/TACustomerPortal.Web/Services/BankAppLinks.cs`, `src/TACustomerPortal.Web/Program.cs`, `src/TACustomerPortal.Web/Views/Pay/Index.cshtml`, `src/TACustomerPortal.Web/wwwroot/css/customer.css`, `tests/TACustomerPortal.Tests/SafeCustomerPayPageTests.cs`, `tests/TACustomerPortal.Tests/SafeCustomerPaymentsTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: không có
- Thứ tự chạy: không áp dụng
- Preflight / Verify / Rollback: không áp dụng
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 623/623 (mới: TheBankAppListFollowsVietQr; test không gọi VietQR, dùng danh sách dựng sẵn).
- Trình duyệt (khung 375 px): nút sao chép đổi màu khi bấm, không tràn ngang.

## Publish
- invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 8972a5e
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Server cần gọi ra api.vietqr.io (đã dùng cho tra cứu mã số thuế); nếu bị chặn thì vẫn dùng danh sách dựng sẵn.
