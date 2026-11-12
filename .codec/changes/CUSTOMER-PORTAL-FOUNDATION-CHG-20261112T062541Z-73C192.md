# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192`
- Created UTC: 2026-11-12T06:25:41.344750+00:00
- Tiêu đề: Đăng ký cá nhân, mật khẩu gửi email, mẫu email và điều khoản dịch vụ
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-12), trang đăng ký Hóa đơn tự động:
1. Thêm loại "Cá nhân": mã số thuế không bắt buộc (cá nhân không có MST chỉ dùng phần thu tiền); tên hiển thị "Họ và tên".
2. Bỏ ô mật khẩu: hệ thống tạo mật khẩu ngẫu nhiên, gửi email thông tin đăng nhập; đổi mật khẩu ở lần đăng nhập đầu.
3. Mẫu email soạn được trên TA Portal; email có logo, thông tin truy cập, hướng dẫn tra cứu hóa đơn.
4. Điều khoản sử dụng dịch vụ: liên kết mở popup danh sách điều khoản; soạn/sửa trên TA Portal; bản soạn sẵn theo pháp luật hiện hành.
5. Dòng "Sản phẩm của Tư vấn giải pháp T.A" (liên kết tuvangiaiphapta.vn) dưới cùng các trang đăng nhập/đăng ký, cả trên mobile.

Đã làm:
- Đăng ký (`AccountController.Register`): BUSINESS / HOUSEHOLD / INDIVIDUAL. Cá nhân: MST để trống được, nếu nhập thì 10 hoặc 12 số; mã khách hàng "CN" + 8 số ngẫu nhiên (kiểm tra trùng dưới khóa). Mật khẩu `PasswordPolicy.GenerateTemporary()`, `MustChangePassword = 1`. Không tự đăng nhập sau đăng ký: chuyển đến trang "Đăng ký thành công" (`/account/register/done`), chỉ chủ hộp thư dùng được tài khoản. Gửi email lỗi/chưa cấu hình: tài khoản vẫn giữ, trang báo dùng "Quên mật khẩu" hoặc liên hệ hỗ trợ. Bản ghi audit có phiên bản điều khoản đã đồng ý (`termsAccepted`).
- Mẫu email (`TAPortal.Shared/PortalContent.cs`): "Thông tin đăng nhập" (mới) và "Đặt lại mật khẩu" (chuyển sang mẫu). Nhân viên soạn văn bản thường với {{Giá trị}}; khung email (logo, thẻ trắng, chân trang), khung thông tin đăng nhập và nút bấm do hệ thống dựng; mọi nội dung được mã hóa HTML. Không lưu được mẫu thiếu khung bắt buộc hoặc dùng giá trị lạ.
- Điều khoản: 16 điều soạn sẵn (`PortalTermsDefault.cs`) theo Luật Giao dịch điện tử 20/2023/QH15, Luật Quản lý thuế 108/2025/QH15, Nghị định 254/2026/NĐ-CP, Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15 và Nghị định 356/2025/NĐ-CP, Luật An ninh mạng 116/2025/QH15, Nghị định 52/2024/NĐ-CP, Bộ luật Dân sự 2015, Luật Thương mại 2005, Luật Bảo vệ quyền lợi người tiêu dùng 2023. Còn chỗ trống [TÊN CÔNG TY], [MST], [ĐỊA CHỈ], [EMAIL HỖ TRỢ], [HOTLINE], [NGÀY HIỆU LỰC] và các số ngày; cần luật sư rà soát. Popup trên trang đăng ký (nút "Tôi đồng ý" tích ô đồng ý), trang `/account/terms` khi không có script.
- TA Portal: nhóm menu "Hóa đơn tự động" gồm "Mẫu email" (sửa, xem trước bằng dữ liệu mẫu, khôi phục mặc định) và "Điều khoản dịch vụ" (sửa, xem trước, cảnh báo chỗ trống). Quyền INVOICEAPP.CONTENT.VIEW / EDIT (SYS_ADMIN).
- Dòng T.A dưới cùng trang đăng nhập, đăng ký, quên/đặt lại mật khẩu, điều khoản; AGENTS.md (CUSTOMER PORTAL BRAND) ghi thêm ngoại lệ này.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/036-portal-content.sql`, `database/scripts/061-verify-portal-content.sql`, `database/scripts/062-rollback-portal-content.sql`
- ADD `src/TAPortal.Shared/PortalContent.cs`, `src/TAPortal.Shared/PortalTermsDefault.cs`
- ADD `src/TAPortal.Web/Controllers/InvoiceAppContentController.cs`, `src/TAPortal.Web/Views/InvoiceAppContent/{Index,Email,Terms}.cshtml`
- ADD `src/TACustomerPortal.Web/Views/Account/{RegisterDone,Terms,_TermsBody,_AuthMaker}.cshtml`, `tests/TACustomerPortal.Tests/SafePortalContentTests.cs`
- MODIFY `AGENTS.md`, `src/TAPortal.Web/{Program.cs,Security/AdminPermissions.cs,Views/Shared/_Layout.cshtml,wwwroot/css/ta-admin.css}`, `src/TACustomerPortal.Web/{Program.cs,Controllers/AccountController.cs,Models/AccountModels.cs,Services/SignupStore.cs,Views/Account/{Register,Login,ForgotPassword,ResetPassword}.cshtml,wwwroot/css/customer.css,wwwroot/js/customer.js}`, `tests/TACustomerPortal.Tests/{SafeCustomerSignupTests,SafeReleaseRegressionTests,SafeWebsiteTests}.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: `database/migrations/036-portal-content.sql` (bảng dbo.PortalContent; quyền INVOICEAPP.CONTENT.VIEW/EDIT cấp SYS_ADMIN, marker A0000036-...)
- Thứ tự chạy: sau 035
- Preflight / Verify / Rollback: verify `database/scripts/061-verify-portal-content.sql`; rollback `database/scripts/062-rollback-portal-content.sql` (từ chối khi đã có nội dung sửa)
- Execution: EXECUTED 2026-11-12T06:29:25Z qua `deploy/windows/Invoke-TAMigrations.ps1 -Plan "036:061:062"` (pre-check NOT_APPLIED → dry-run có rollback PASS → gates → chạy thật VERIFY_STATUS=PASS). Log: `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261112T062541Z-73C192-migrations-20261112T062756Z.log`.

## Test và kết quả
- Release build 0 lỗi; Safe tests 663/663 (mới: SafePortalContentTests, AnIndividualMaySignUpWithoutATaxCode, ValidSignUpCreatesTheAccountAndMailsAGeneratedPassword, WhenMailCannotBeSentTheAccountIsKeptAndTheUserIsToldWhatToDo, InvoiceAppContentPagesOpenAndPreviewWithoutSaving).
- Chạy thử cục bộ: đổi loại "Cá nhân" (MST không bắt buộc, ẩn Tra cứu, nhãn "Họ và tên"), popup 16 điều, "Tôi đồng ý" tích ô; khung 375 px: dòng T.A dưới cùng, không tràn ngang.

## Publish
- invoice và portal. Xem report.

## Git
- Branch: feature/signup-individual
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Quyết định: không tự đăng nhập sau đăng ký (xác minh email); mật khẩu tạm buộc đổi lần đầu.
- Điều khoản soạn sẵn cần operator điền thông tin công ty, các số ngày và cần luật sư rà soát (thời điểm lập hóa đơn khi nhận tiền cọc/ứng trước; vai trò xử lý dữ liệu cá nhân và chia sẻ cho đối tác).
- Email thông báo thanh toán (gửi từ hệ thống thông báo) chưa chuyển sang mẫu.
