# CODEC DELIVERY REPORT — CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB

Feature ID: `CUSTOMER-PROFILE`
Change ID: `CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB`

## A. Identification
- Agent: Claude Code. Yêu cầu operator: "Làm giai đoạn 1" của thiết kế hồ sơ khách hàng (`docs/customer-profile-design.md`).
- Branch `feature/customer-portal-mvp01`; HEAD = origin = `46d1db8b1639af246d6753c830e5672d36a2c4fa` lúc bắt đầu. Thay đổi chưa commit của người khác: `tests/playwright/` (không đụng).

## B. Audit Summary
Change tính năng mới, không phải audit sửa lỗi.

| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| CP-01 | TAPortal.Web | Customer profile | HIGH | Mật khẩu khách hàng nằm dạng chữ thường trong Excel dùng chung | Không có nơi lưu có mã hóa/phân quyền | Bảng `CustomerSystemAccounts` chỉ lưu ciphertext; xem cần quyền + audit | CustomerProfileStore.cs, CustomerSecretProtector.cs, CustomerProfileActions.cs | SafeCustomerProfileTests | FIXED (chờ migration/deploy) |
| CP-02 | TAPortal.Web | Customer profile | MEDIUM | Chưa có khóa mã hóa mật khẩu khách hàng trên server | `CustomerSecrets__Key` chưa đặt | Khóa AES-256 cố định (không tự xoay, không cần backup định kỳ); ứng dụng từ chối lưu mật khẩu khi thiếu/sai khóa | CustomerSecretProtector.cs, New-TACustomerSecretKey.ps1 | SecretsCannotBeSavedWithoutAConfiguredKey, AesGcmProtectorRoundTripsWithAFixedKeyAndRequiresOne | PENDING_OPERATOR |
| CP-03 | Repo | Codec | LOW | `codec.py validate` báo sai đường dẫn publish | Checkout ở `D:\Working\...` khác đường dẫn trong config | Không sửa (môi trường) | — | codec validate | OPEN |
| CP-04 | TAPortal.Web | Representatives | HIGH | Stored XSS: họ tên người đại diện nằm trong `onsubmit="confirm('...')"` (entity được giải mã trước khi chạy JS) | Giá trị Razor trong JS inline | `data-ta-confirm` + handler trong `ta-portal.js` | Representatives.cshtml, ta-portal.js | ARepresentativeNameCannotBreakOutOfTheDeleteConfirmation | FIXED |
| CP-05 | TAPortal.Web | Customer profile | MEDIUM | Sau xung đột, form giữ dữ liệu cũ của người dùng nhưng nhận stamp mới → lưu lại sẽ ghi đè thay đổi của người khác | Thay stamp thay vì nạp lại bản ghi | Chỉ khi Conflict: nạp lại bản ghi đã lưu; lỗi validate giữ stamp đã gửi | CustomerProfileActions.cs | AConflictingSaveReloadsTheStoredVersionInsteadOfKeepingStaleInput | FIXED |
| CP-06 | TAPortal.Web | Customer profile | MEDIUM | Quyền `CRM.CUSTOMERS.SECRETS.REVEAL` hiện không hạn chế ai: Customers chỉ dành cho SYS_ADMIN và SYS_ADMIN qua mọi quyền | Thiết kế phân quyền hiện tại | Ghi nhận; tách quyền xem mật khẩu khỏi SYS_ADMIN là quyết định kiến trúc của operator | — | — | PENDING_OPERATOR |
| CP-07 | TAPortal.Web | Customer profile | LOW | Ciphertext có thể bị chép sang tài khoản/trường/khách hàng khác trong DB mà vẫn giải mã được | AAD chỉ là chuỗi purpose | AAD = purpose + customerId + accountId + field | CustomerSecretProtector.cs, CustomerProfileActions.cs | AesGcmProtectorRoundTripsWithAFixedKeyAndRequiresOne | FIXED |
| CP-08 | Database | Migration 013 | LOW | Cột varchar nhận nvarchar → ký tự tiếng Việt thành `?` (Phone, LoginPhone, TaxOfficerPhone, BankAccountNumber, CertificateSerial) | Kiểu cột | Đổi sang nvarchar (migration chưa chạy) | 013-customer-profile-core.sql | — | FIXED |
| CP-09 | TAPortal.Web | Customer profile | LOW | Kiểm tra đơn vị liên kết chỉ chặn vòng 2 khách hàng, không khóa khi lưu đồng thời | Truy vấn đơn giản | Chưa sửa (chỉ ảnh hưởng hiển thị liên kết) | CustomerProfileStore.cs | — | OPEN |

## C. Customer Portal Account Management
NOT_APPLICABLE — không thay đổi Customer Portal.

## D. Admin CRUD
Customer Workspace thêm 3 tab: Hồ sơ doanh nghiệp (tạo/sửa 1:1), Người đại diện (thêm/sửa/xóa mềm, CCCD che 4 số cuối trong danh sách), Tài khoản hệ thống (thêm/sửa/xóa mềm, nhóm theo loại, cảnh báo hết hạn 30 ngày, Hiện/Chép mật khẩu). Mọi thao tác ghi dùng antiforgery, optimistic concurrency (rowversion), audit `dbo.AuditLogs`, và scope theo customer id trên route.

## E. Changed Files
Xem `.codec/changes/CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB.md` mục "File thay đổi".

## F. Database
- `database/migrations/013-customer-profile-core.sql` — tạo `dbo.CustomerProfiles`, `dbo.CustomerRepresentatives`, `dbo.CustomerSystemAccounts`; EXECUTED.
- Verify `database/scripts/015-verify-customer-profile.sql`; rollback `database/scripts/016-rollback-customer-profile.sql` (từ chối khi có dữ liệu).
- Dry-run: PASS; real run EXECUTED 2026-11-05T15:16:37Z (chi tiết trong change record).

## G. Build & Tests
2026-11-05: đồng bộ checkout với HEAD `3368fba` (nhánh đã đi thêm 76 commit; working tree trước đó còn ở `c7949c8`), áp lại thay đổi của change này, review độc lập, sửa CP-04..CP-08. Kết quả mới ghi trong change record.
restore OK; build Release 0 warning/0 error; Safe tests 336/336 PASS; codec unittest 4/4 OK; codec validate FAIL (CP-03, có từ trước); `git diff --check` OK. UI: NOT_VERIFIED trên trình duyệt thật.

## H. Admin Publish
DEPLOYED portal 2026-11-05T15:19:07Z: source 7492d78, artifact e7072a9bdee53a1adccc4bd8151ff238aea31f131dccc8ffa69072368f985621 (65 files), package 0E4D1DEFBB8CFBDE5AF0FCDEBEE16D69217021F0F9148A0CD7B53A56503C028D, server backup C:\TADeployackups\portal-20261105-221858, health HTTP 200.

## I. Customer Publish
NOT_APPLICABLE — TACustomerPortal.Web và TAPortal.Shared không đổi.

## J. Security and Acceptance
- Ciphertext `varbinary`: AES-256-GCM `[version][nonce 12][tag 16][data]`, nonce ngẫu nhiên mỗi lần, associated data `TAPortal.CustomerSecrets.v1`, khóa `CustomerSecrets__Key` ngoài Git; plaintext không bao giờ render vào HTML, không trả lại khi form lỗi, không ghi audit/log.
- `RevealSecret`: POST + antiforgery + quyền `CRM.CUSTOMERS.SECRETS.REVEAL` + audit bắt buộc; response `no-store` (middleware chung).
- Link đăng nhập chỉ nhận/hiển thị http(s).
- Acceptance còn lại: operator thử trên môi trường thật sau deploy.

## K. Git Delivery
Chưa commit/push (chờ operator). Merge: PENDING_APPROVAL.

## L. Operator Handoff
1. Chạy `deploy/windows/New-TACustomerSecretKey.ps1` một lần, đặt `CustomerSecrets__Key` cho TAPortal.Web, giữ một bản sao ngoài server.
2. Duyệt commit; agent chạy dry-run + migration 013 theo `## AGENT MIGRATION EXECUTION`, rồi deploy `portal`.
3. Giai đoạn 2: Import Excel có màn hình duyệt.
