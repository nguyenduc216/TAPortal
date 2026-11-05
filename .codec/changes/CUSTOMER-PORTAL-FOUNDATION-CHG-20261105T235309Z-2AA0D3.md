# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3`
- Created UTC: 2026-11-05T23:53:09.687249+00:00
- Tiêu đề: Customer Portal: tự đăng ký doanh nghiệp (tra MST) và khách hàng linh hoạt theo loại (khách lẻ/hộ KD/doanh nghiệp/tổ chức)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-06): (1) thêm form đăng ký trên Customer Portal — MST, email, điện thoại; nhập MST thì tra cứu và tự điền tên/địa chỉ; (2) quản lý khách hàng của doanh nghiệp linh hoạt: Zalo, điện thoại, số tài khoản ngân hàng; khách lẻ (không MST, có thể CCCD), hộ kinh doanh, nhà hàng, doanh nghiệp (cần MST).
Quyết định operator: đăng ký xong đăng nhập dùng ngay, tự chọn gói và chuyển khoản (thanh toán tự động gắn sau); tra MST qua API VietQR.

Trong phạm vi:
- `/account/register` (anonymous, antiforgery, rate limit "login"): Doanh nghiệp (MST 10 / 10-3) hoặc Hộ kinh doanh (MST 10 / 12), tên, địa chỉ, người liên hệ, điện thoại, email (= tên đăng nhập), mật khẩu theo PasswordPolicy, đồng ý điều khoản. `SqlSignupStore`: một transaction — kiểm tra MST/email trùng (UPDLOCK, HOLDLOCK), tạo dbo.Customers (Code = MST, hoặc "DK"+MST khi mã đã dùng), dbo.Users, membership OWNER, audit `CUSTOMER_SELF_REGISTERED` (không có mật khẩu/thông tin liên hệ). Xong đăng nhập và chuyển tới Gói dịch vụ. Trang đăng nhập có link "Đăng ký doanh nghiệp".
- `/account/tax-lookup` (anonymous, rate limit "lookup" 20/phút/IP): `VietQrTaxCodeLookup` gọi `https://api.vietqr.io/v2/business/{mst}` (không khóa), cache tìm thấy 24h / không có 1h; 429/lỗi ⇒ "nhập tay". Chỉ MST rời server. Nút "Tra cứu" + tự tra khi rời ô MST (chỉ điền ô trống).
- Khách hàng (dbo.CustomerBuyers): loại khách INDIVIDUAL/HOUSEHOLD/BUSINESS/ORGANIZATION, nhóm khách (gợi ý: nhà hàng, bán lẻ…), CCCD, Zalo, ngân hàng/số tài khoản/chủ tài khoản. Doanh nghiệp bắt buộc MST + tên đơn vị; tổ chức bắt buộc tên đơn vị; khách lẻ bỏ MST/tên đơn vị; doanh nghiệp/tổ chức bỏ CCCD; số tài khoản cần tên ngân hàng. Form hiện/ẩn trường theo loại; danh sách hiện loại, nhóm, MST/CCCD, Zalo. Dòng cũ không có loại: có MST ⇒ doanh nghiệp, không ⇒ khách lẻ (không cập nhật dữ liệu cũ).
- Không nêu tên đối tác thanh toán trên trang khách (VietQR là nguồn tra cứu, không hiện cho khách).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/021-customer-buyer-profile.sql`, `database/scripts/031-verify-customer-buyer-profile.sql`, `database/scripts/032-rollback-customer-buyer-profile.sql`
- ADD `src/TACustomerPortal.Web/Services/SignupStore.cs`, `src/TACustomerPortal.Web/Services/TaxCodeLookup.cs`, `src/TACustomerPortal.Web/Views/Account/Register.cshtml`
- MODIFY `src/TACustomerPortal.Web/Controllers/AccountController.cs`, `Controllers/ContactsController.cs`, `Models/AccountModels.cs`, `Models/PaymentModels.cs`, `Services/PaymentStore.cs`, `Program.cs`, `Views/Account/Login.cshtml`, `Views/Contacts/Edit.cshtml`, `Views/Contacts/Index.cshtml`, `wwwroot/css/customer.css`, `wwwroot/js/customer.js`
- ADD `tests/TACustomerPortal.Tests/SafeCustomerSignupTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs` (BuyerTypesDecideWhichDetailsAreRequiredAndKept), `SafeReleaseRegressionTests.cs` (3 action anonymous đã review)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- 021-customer-buyer-profile.sql: thêm 7 cột nullable vào dbo.CustomerBuyers (+ CK_CB_BuyerType). Verify 031, rollback 032 (từ chối khi cột có dữ liệu).
- Execution: EXECUTED 2026-11-05T23:55:38Z (pre-check NOT_APPLIED; dry-run afterMigration=PASS, afterRollback=NOT_APPLIED, trancount 0; full backup age 519 phút; gates 455/455; real run VERIFY_STATUS=PASS). Log cục bộ (gitignored): `.codec/reports/CUSTOMER-PORTAL-FOUNDATION-CHG-20261105T235309Z-2AA0D3-migrations-20261105T235407Z.log`.

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi. Safe tests: 455/455 pass. `test_codec.py` OK. `git diff --check` OK. `codec.py validate`: lỗi publish path có sẵn từ trước (không do change này).
- Chạy thử local (Development, không DB): trang đăng ký hiển thị; tra MST 0100109106 qua VietQR điền đúng tên và địa chỉ.

## Publish
- invoice (TACustomerPortal.Web). Shared/Integrations không đổi. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: b3074b4
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- API VietQR giới hạn lượt gọi theo IP (đã gặp 429): có cache và thông báo nhập tay.
- Chưa có xác minh email khi đăng ký; chưa có captcha (đang dựa vào rate limit). Đề xuất bổ sung nếu có đăng ký rác.
- Thanh toán tự động gói dịch vụ: change sau.
