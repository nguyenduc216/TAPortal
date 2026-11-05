# Change CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB

- Feature ID: `CUSTOMER-PROFILE`
- Change ID: `CUSTOMER-PROFILE-CHG-20261103T183438Z-C56EAB`
- Created UTC: 2026-11-03T18:34:38.542899+00:00
- Tiêu đề: Customer profile phase 1: general info, representatives, encrypted system accounts
- Trạng thái: IMPLEMENTED (chưa commit)

## Yêu cầu và phạm vi
Operator yêu cầu "Làm giai đoạn 1" theo `docs/customer-profile-design.md` (phân tích file Excel "Thông tin khách hàng.xlsx"): thêm vào Customer Workspace của Admin 3 tab — Hồ sơ doanh nghiệp, Người đại diện, Tài khoản hệ thống (mật khẩu mã hóa, quyền xem riêng, audit). Ngoài phạm vi: import Excel, dịch vụ/phí, phân công nhân viên, tờ khai, Customer Portal.

Trạng thái git trước khi sửa: branch `feature/customer-portal-mvp01`, HEAD = origin = `46d1db8b1639af246d6753c830e5672d36a2c4fa`; chưa commit sẵn: `tests/playwright/` (không phải của change này, không đụng), `docs/customer-profile-design.md` (tài liệu thiết kế của cùng yêu cầu).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/013-customer-profile-core.sql`
- ADD `database/scripts/015-verify-customer-profile.sql`
- ADD `database/scripts/016-rollback-customer-profile.sql`
- ADD `src/TAPortal.Web/Models/CustomerProfileModels.cs`
- ADD `src/TAPortal.Web/Services/CustomerProfileStore.cs`
- ADD `src/TAPortal.Web/Services/CustomerSecretProtector.cs` (AES-256-GCM, khóa cố định `CustomerSecrets:Key`)
- ADD `deploy/windows/New-TACustomerSecretKey.ps1` (sinh khóa, chỉ in ra màn hình)
- MODIFY `src/TAPortal.Web/appsettings.json` (placeholder rỗng `CustomerSecrets:Key`)
- ADD `src/TAPortal.Web/Controllers/CustomerProfileActions.cs`
- ADD `src/TAPortal.Web/Views/Customers/Profile.cshtml`, `Representatives.cshtml`, `SystemAccounts.cshtml`
- MODIFY `src/TAPortal.Web/Models/CustomerWorkspaceModels.cs` (3 tab mới)
- MODIFY `src/TAPortal.Web/Program.cs` (đăng ký store + protector)
- MODIFY `src/TAPortal.Web/wwwroot/js/ta-portal.js` (nút Hiện/Chép mật khẩu), `wwwroot/css/ta-admin.css`
- ADD `tests/TACustomerPortal.Tests/SafeCustomerProfileTests.cs` (10 test)
- ADD `docs/customer-profile-design.md` (cập nhật: bảng 1:1 `CustomerProfiles`)

Không đổi `TAPortal.Shared`, `TAPortal.Integrations`, `TACustomerPortal.Web` → chỉ `TAPortal.Web` cần publish.

## Migration SQL
- Plan cho `Invoke-TAMigrations.ps1`: `013:015:016`. Số 013/015/016 được giữ chỗ cho change này nên chạy sau 014–020 đã EXECUTED; 013 chỉ tạo 3 bảng mới, không phụ thuộc 014–020.
- Database: TAPortalSys
- Script: `database/migrations/013-customer-profile-core.sql` (chỉ CREATE TABLE/INDEX khi chưa có; không ALTER/DROP/DML; có guard `DB_NAME()` và transaction)
- Thứ tự chạy: sau 012-tingee-customer-buyers.sql
- Verify: `database/scripts/015-verify-customer-profile.sql` (PASS khi đủ bảng/constraint/FK/index; NOT_APPLIED khi chưa có bảng)
- Rollback: `database/scripts/016-rollback-customer-profile.sql` (chỉ drop khi cả 3 bảng rỗng; có dữ liệu thì THROW, không đổi gì)
- Execution: EXECUTED 2026-11-05T15:16:37Z (`Invoke-TAMigrations.ps1 -Plan 013:015:016`): pre-check NOT_APPLIED; dry-run exit 0, sau migration PASS, sau rollback NOT_APPLIED, @@TRANCOUNT=0, pre-check lại NOT_APPLIED; backup `TAPortalSys-predeploy-20261105T151518Z.bak` (RESTORE VERIFYONLY OK); gates 428/428 PASS; real run VERIFY_STATUS=PASS.

## Test và kết quả
- `dotnet restore TAPortal.sln`: OK
- `dotnet build TAPortal.sln -c Release --no-restore`: 0 warning, 0 error
- `dotnet test TAPortal.sln -c Release --no-build --filter FullyQualifiedName~TACustomerPortal.Tests.Safe`: 336/336 PASS (gồm 10 test mới `SafeCustomerProfileTests`; chạy lại sau khi đổi sang khóa AES cố định 2026-11-04: 336/336 PASS, build 0 warning/0 error, codec unittest OK, `git diff --check` OK)
- `python -m unittest discover -s .codec/tools -p test_codec.py`: 4/4 OK
- `python .codec/tools/codec.py validate`: FAIL — 3 lỗi "Publish path violates fixed convention" vì checkout này ở `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal` còn `.codec/config.json` trỏ `D:\T.A\TAPortal\TAPortal\src\publish`. Lỗi môi trường có từ trước, không do change này; không sửa config.
- `git diff --check`: OK
- 2026-11-05 (sau khi đồng bộ với HEAD `3368fba` và sửa CP-04..CP-08 theo review độc lập): restore OK; build Release 0 error (1 warning CS8604 có sẵn trong `SafeCustomerPaymentsTests.cs`, không thuộc change này); Safe tests 428/428 PASS (12 test `SafeCustomerProfileTests`); codec unittest OK; codec validate FAIL chỉ do CP-03; `git diff --check` OK.
- Giao diện: kiểm tra qua HTML render trong test; NOT_VERIFIED trên trình duyệt thật.

## Publish
- Project: TAPortal.Web
- Destination (chỉ từ config): `D:\T.A\TAPortal\TAPortal\src\publish\deployed-TAPortal.Web`
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256: —
- Smoke test: NOT_RUN

## Git
- Branch: feature/customer-portal-mvp01 (không tạo branch mới; tạo branch cần operator duyệt)
- Source commit SHA: `769f1670b2cbae69706463e3a387c3569a4e3f73`
- PR: —
- Merge: PENDING_APPROVAL
- Merge SHA: —

## Rủi ro, quyết định và việc còn lại
- PENDING_OPERATOR: chạy `deploy/windows/New-TACustomerSecretKey.ps1` một lần, đặt `CustomerSecrets__Key` cho site TAPortal.Web, giữ một bản sao ngoài server. Chưa có: tab vẫn dùng được nhưng không lưu được mật khẩu/PIN (có cảnh báo). Mất cả khóa trên server lẫn bản sao = mất toàn bộ mật khẩu đã lưu. Không sinh khóa thứ hai khi đã có dữ liệu.
- 2026-11-04: theo yêu cầu operator, thay Data Protection bằng AES-256-GCM khóa cố định (xem README quyết định 6).
- Xem mật khẩu cần `dbo.AuditLogs` ghi được, bất kể `RequireDurableAudit`.
- Quyền `CRM.CUSTOMERS.SECRETS.REVEAL` chưa có dòng trong `dbo.Permissions` (không seed); hiện Customers chỉ dành cho SYS_ADMIN nên không ảnh hưởng. Khi mở cho vai trò khác cần thêm permission.
- Tiếp theo: dry-run + chạy migration 013, publish/deploy `portal`, giai đoạn 2 Import Excel.
