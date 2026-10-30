# Change TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C`
- Created UTC: 2026-10-29T20:31:04Z
- Tiêu đề: Tingee merchant mapping (Admin); bank accounts, contacts and QR orders (Customer); webhook inbox
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Admin, Customer, Webhooks) / MIGRATION_NOT_EXECUTED / TINGEE_NOT_VERIFIED / VISUAL_NOT_VERIFIED / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Yêu cầu
Operator (2026-10-30): (1) thống nhất mọi project publish vào `publish\deployed-<tên dự án>`; (2) xây dựng chức năng để customer cài đặt ngân hàng, quản lý danh sách khách hàng, tạo đơn hàng cho khách; T.A tạo thông tin khách hàng qua Tingee và ánh xạ với khách hàng T.A để dùng API Tingee. Merchant/UAT Tingee: operator cung cấp sau.

## Phạm vi đã làm
- Config: thêm target `TAPortal.Webhooks` (`publish\deployed-TAPortal.Webhooks`) và đưa vào `shared_dependents` (Webhooks tham chiếu TAPortal.Shared). `codec.py validate` PASS.
- Admin: tab "Thanh toán Tingee" trong Customer Workspace — tạo merchant con qua `/v1/merchant/create` (Master Merchant) hoặc liên kết merchant có sẵn; hủy liên kết chỉ xóa ánh xạ cục bộ; audit trong cùng transaction; merchant đã tạo nhưng chưa lưu được liên kết được báo kèm mã để liên kết tay (không tạo lại).
- Customer Portal (menu "Thanh toán"):
  - Tài khoản ngân hàng: form riêng theo từng ngân hàng/loại tài khoản (TingeeBankCatalog), create-va → OTP confirm-va; xác thực phía ngân hàng (VCB/TPBank) qua link + webhookUrl; hủy liên kết (delete-va + OTP); đồng bộ trạng thái (get-va-paging); đặt mặc định; tùy chọn nhận biến động STK gốc.
  - Khách hàng: danh sách, thêm/sửa, ngừng/kích hoạt (không xóa vì đơn hàng tham chiếu).
  - Đơn hàng & QR: dòng hàng có thuế suất; QR động `dynamic-recurring-payment` (trả nhiều lần đến khi đủ) hoặc QR tĩnh nội dung = mã đơn; kiểm tra thanh toán (get-status-dynamic-qr), tạo lại QR cho số còn lại, hủy đơn (không hủy khi đã nhận tiền), polling trạng thái 10 giây; QR SVG nội bộ (QRCoder).
  - Cài đặt thanh toán: tự xuất hóa đơn khi thanh toán đủ (mặc định BẬT), thời hạn QR mặc định; mỗi đơn chỉnh riêng.
  - Quyền tạm thời: mọi thành viên xem toàn bộ dữ liệu doanh nghiệp; OWNER/ADMIN được thay đổi (sẽ thay bằng phân quyền linh hoạt CUSTOMER-PORTAL-RBAC).
  - Trang "chưa sẵn sàng" (503 + mã sự cố) khi bảng thanh toán chưa có.
- Integrations: API Tingee có kiểu (merchant, VA, QR), quy tắc ngân hàng, render QR SVG.
- Webhooks: inbox SQL `dbo.TingeeWebhookInbox`, khóa chống trùng = transactionCode / x-request-id / SHA256 body; chỉ bật khi có connection string.

## Quyết định kỹ thuật
- Không lưu secret: khóa Tingee chỉ qua `Tingee__ClientId`, `Tingee__SecretKey`; URL webhook trạng thái VA qua `Tingee__VaStatusWebhookUrl`.
- Đơn hàng được lưu trước rồi mới gọi Tingee: lỗi Tingee không làm mất đơn (DRAFT + lỗi hiển thị + nút tạo lại).
- QR tạo lại cho phần còn lại: số đã trả = (tiền đơn − tiền QR) + tổng đã trả trên QR mới.
- Hóa đơn: chỉ đặt `InvoiceStatus=PENDING` khi PAID và bật tự xuất; phát hành thực tế ở P6.
- Xử lý webhook (khớp giao dịch → đơn) và đối soát cuối ngày: change sau (worker).

## File
- ADD database/migrations/011-tingee-payments-foundation.sql; database/scripts/011-tingee-least-privilege-template.sql, 012-verify-tingee-foundation.sql, 013-rollback-tingee-foundation.sql
- ADD src/TAPortal.Integrations: Tingee/TingeeApi.cs, Tingee/TingeeBankCatalog.cs, QrImage.cs; MODIFY TingeeClient.cs, IntegrationsServiceCollectionExtensions.cs, csproj (QRCoder 1.6.0)
- ADD src/TAPortal.Web: Controllers/CustomerTingeeActions.cs, Models/CustomerTingeeModels.cs, Services/TingeeMerchantStore.cs, Views/Customers/Tingee.cshtml; MODIFY PortalControllers.cs (partial), CustomerWorkspaceModels.cs (tab), Program.cs, csproj
- ADD src/TACustomerPortal.Web: Controllers (CustomerArea, Banks, Contacts, Orders, PaymentSettings), Models/PaymentModels.cs, Services/PaymentStore.cs, Services/OrderPayments.cs, Views (Banks, Contacts, Orders, PaymentSettings, Shared/PaymentsUnavailable, Shared/_PaymentsNotLinked); MODIFY Program.cs, _Layout.cshtml (menu Thanh toán + thông báo), Dashboard/Index.cshtml, customer.css, customer.js, csproj
- ADD src/TAPortal.Webhooks/SqlTingeeWebhookInbox.cs; MODIFY Program.cs, csproj (ref Shared)
- ADD tests: SafeCustomerPaymentsTests.cs, SafeAdminTingeeTests.cs
- MODIFY THIRD-PARTY-NOTICES.md (QRCoder MIT), .codec/config.json

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys — NOT_EXECUTED
- Thứ tự: (1) `database/migrations/011-tingee-payments-foundation.sql` (sau 008); (2) cấp quyền theo `database/scripts/011-tingee-least-privilege-template.sql` (thay principal thật); (3) verify `database/scripts/012-verify-tingee-foundation.sql` → `VERIFY_STATUS=PASS`.
- Rollback: `database/scripts/013-rollback-tingee-foundation.sql` (chỉ khi các bảng còn trống).
- Guard `DB_NAME() = 'TAPortalSys'`; chỉ tạo bảng khi chưa có; không ghi dữ liệu.

## Test và kết quả
- dotnet restore: OK; Release build: 0 warning / 0 error
- Safe tests: 303 passed / 0 failed / 0 skipped (thêm 34: payments Customer + Admin Tingee + dedup)
- Codec unit tests OK; codec validate PASS; git diff --check sạch
- Tingee thật: NOT_VERIFIED (chưa có Master Merchant/UAT key). Giao diện: VISUAL_NOT_VERIFIED (chưa chụp ảnh).

## Publish
- TAPortal.Web: exit 0, 2026-10-29T20:51:05Z→2026-10-29T20:51:06Z, 65 files, SHA256 `9f9fa64103b37c0bcd127ae27bb4f475df6073bc8d2cdf17f2bfe21be79cd244` (trước: cc224fd32f4470a870b5d97dcb9278b3eba8044a64ac47d8b6757d8dd331ddfa); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C-TAPortal.Web.sha256`
- TACustomerPortal.Web: exit 0, 2026-10-29T20:51:15Z→2026-10-29T20:51:16Z, 58 files, SHA256 `d932771b21c577fbe5359d848f035dddb40ae107a6c57a5e1e6fd489986c95e8` (trước: 45728db69dd6302831edf5f8613fc5e45b57dec069a0e46b6095c39449dc0ddc); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C-TACustomerPortal.Web.sha256`
- TAPortal.Webhooks: exit 0, 2026-10-29T20:51:20Z→2026-10-29T20:51:22Z, 53 files, SHA256 `a51d29acdf543b7e30105c2a60c4430dc9cb3c1366a0a2a93d44bc9c2f1ad9c5` (trước: chưa có — target mới); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C-TAPortal.Webhooks.sha256`
- Backup: `publish\rollback\TINGEE-INTEGRATION-CHG-20261029T203104Z-1FE09C\TAPortal.Web` (cc224fd3…), `...\TACustomerPortal.Web` (45728db6…); Webhooks là target mới.
- Không có secret trong artifact. NOT_DEPLOYED; IIS NOT_CHECKED.

## Git
- Branch: feature/customer-portal-mvp01; base 04eabd9f76cbe30cca985d8ee54828acc37f7fe5; source 91e36178531a9d8b525f857b68f36dc88798371e; delivery SHA báo sau push; PR #7 chưa merge.

## Rủi ro và việc còn lại
- Chạy migration 011 + cấp quyền (operator). Trước đó trang thanh toán hiển thị "chưa sẵn sàng".
- Tingee Master Merchant + key UAT; xác minh chữ ký webhook với dữ liệu thật; một số ngân hàng (VPBank/VCB DN-HKD) có thể yêu cầu API key riêng của merchant (mã 1133).
- Worker xử lý inbox (khớp giao dịch → đơn, QR tĩnh theo mã đơn), đối soát cuối ngày, phân quyền linh hoạt, Zalo, hóa đơn: các change sau.
- Cấu hình `Tingee__VaStatusWebhookUrl` khi có domain công khai cho TAPortal.Webhooks.
