# Change TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA

- Feature ID: `TINGEE-INTEGRATION`
- Change ID: `TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA`
- Tiêu đề: Separate CustomerBuyers table; stop using the existing CustomerContacts table
- Trạng thái: IMPLEMENTED / BUILD_VERIFIED / LOCAL_PUBLISH_COMPLETE (Customer) / MIGRATION_NOT_EXECUTED / TINGEE_NOT_VERIFIED / PRODUCTION_NOT_DEPLOYED
- Agent: Claude Code (claude-opus-5-5)

## Quyết định
Không dùng chung hay ALTER bảng `dbo.CustomerContacts` có sẵn vì đang chứa dữ liệu không rõ chủ sở hữu. Khách hàng của doanh nghiệp lưu ở `dbo.CustomerBuyers`; tên cột `CustomerOrders.ContactId` giữ nguyên.

## Database
- Script 014 do operator chạy xác nhận: `dbo.CustomerContacts` tạo ngày 2026-10-19, có 10 dòng, thiếu 10 cột so với code và thiếu `UX_CC_Customer_Code`. Trong repo không có code nào khác dùng bảng này; nơi dùng ngoài repo: NOT_VERIFIED.
- ADD `database/migrations/012-tingee-customer-buyers.sql` (NOT_EXECUTED): tạo `dbo.CustomerBuyers`, bỏ `FK_CO_Contact`, thêm `FK_CO_Buyer`. Không đổi `dbo.CustomerContacts` và dữ liệu của nó. Guard `DB_NAME()`, transaction, chạy lại được; hủy nếu đơn hàng đã có `ContactId` khi FK cũ còn.
- Thứ tự: (1) backup; (2) migration 012; (3) `012-verify-tingee-foundation.sql`; (4) `014-diagnose-tingee-foundation.sql`.
- `013-rollback-tingee-foundation.sql` không còn đụng tới `dbo.CustomerContacts`.
- `TA_User` là db_owner nên cấp quyền không giới hạn được user này (WARN, cần operator quyết định trước production).

## File
- ADD `database/migrations/012-tingee-customer-buyers.sql`
- MODIFY `src/TACustomerPortal.Web/Services/PaymentStore.cs`, `database/scripts/011-tingee-least-privilege-template.sql`, `012-verify-tingee-foundation.sql`, `013-rollback-tingee-foundation.sql`, `014-diagnose-tingee-foundation.sql`

## Test
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec tests: OK; codec validate: PASS; git diff --check: sạch. Safe tests dùng store giả nên không kiểm chứng câu SQL; dùng script 012 và 014 sau khi chạy migration.

## Publish
TACustomerPortal.Web: `src\publish\deployed-TACustomerPortal.Web`, exit 0, 2026-10-30T13:14:40Z→2026-10-30T13:14:43Z, 58 file, SHA256 `3c4095209311e454bdb1fcaa596ce3e248217a2a967d0ce1b94bb6b0030559b7` (trước: `336a9fb0726c1e4040f78a3596d34e6ecccc4dc856bd1fb3dce39ecd64e1fcb1`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA-TACustomerPortal.Web.sha256`; rollback `src\publish\rollback\TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA\TACustomerPortal.Web`. PUBLISHED / NOT_DEPLOYED.

## Git
Branch feature/customer-portal-mvp01; base c78fe5c73cf90359130d27b9d9976b8661420460; source 5db5161254907991e697eca76e3b46133edb5e33; PR #7 chưa merge.

## Operator xác nhận (2026-10-30)
Operator đã chạy migration 012 (`012-tingee-customer-buyers.sql: OK`), `012-verify-tingee-foundation.sql` (`VERIFY_STATUS=PASS`) và `014-diagnose-tingee-foundation.sql` (`PASS_WITH_WARNINGS (1)`: TA_User là db_owner). `dbo.CustomerContacts` vẫn 10 dòng. TG-02 về database: VERIFIED; chức năng trên trang và Tingee thật: NOT_VERIFIED.
