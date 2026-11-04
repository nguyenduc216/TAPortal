# Change TINGEE-INTEGRATION-CHG-20261104T163209Z-755A18

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261104T163209Z-755A18`
- Created UTC: 2026-11-04T16:32:09.041822+00:00
- Tiêu đề: Tingee Sandbox: popup liên kết/xóa liên kết cập nhật tại chỗ, không tải lại trang
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh popup "Liên kết tài khoản ngân hàng"): đổi giá trị trong popup thì màn hình nháy vì tải lại popup; xử lý các đoạn postback trong popup cho mượt.

Nguyên nhân: select ngân hàng/loại tài khoản dùng `onchange="this.form.submit()"` — tải lại cả trang, popup đóng rồi mở lại với hiệu ứng fade.

Sửa:
- Select dùng `requestSubmit()` (phát sự kiện submit).
- `ta-tingee-sandbox.js`: form trong popup liên kết/xóa liên kết (chọn ngân hàng, LinkBank, ConfirmOtp, DeleteVa, ConfirmDeleteVa) được gửi bằng fetch; nếu server trả popup vẫn mở thì chỉ thay `.modal-content` (giữ giá trị đã nhập, focus ô OTP); nếu popup đóng (thành công → redirect) thì ẩn popup rồi thay nội dung trang như soft navigation.
- Ô chọn merchant con ở Tingee Sandbox và Hóa đơn Sandbox dùng `requestSubmit()` để đi qua soft navigation.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Views/TingeeSandbox/Index.cshtml`
- MODIFY `src/TAPortal.Web/Views/TingeeInvoiceSandbox/Index.cshtml`
- MODIFY `src/TAPortal.Web/wwwroot/js/ta-tingee-sandbox.js`
- MODIFY `tests/TACustomerPortal.Tests/SafeAdminTingeeSandboxTests.cs` (test `LinkPopupPickersAreHandledInPlace`)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (không có thay đổi SQL)

## Test và kết quả
- Safe tests: 402/402 pass; `node --check` JS OK.
- Trình duyệt thật: NOT_VERIFIED (operator kiểm tra sau deploy).

## Publish
- portal. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 123371c
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có JS → form vẫn gửi bình thường (tải lại trang).
