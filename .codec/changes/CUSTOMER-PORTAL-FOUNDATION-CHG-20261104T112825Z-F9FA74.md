# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74`
- Created UTC: 2026-11-04T11:28:25.511674+00:00
- Tiêu đề: Gói dịch vụ: quản lý gói (hóa đơn / tra soát giao dịch / combo), số dư khách hàng, giao dịch mua gói; trang mua gói trên Customer Portal
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-04): trên TA Portal tạo chức năng quản lý gói dịch vụ — gói hóa đơn, gói tra soát giao dịch và gói combo (cả hai); mỗi gói có mã, tên, giá, số lượng hóa đơn và/hoặc giao dịch. Mỗi khách hàng có hai số: giao dịch còn khả dụng, hóa đơn còn khả dụng. Giao dịch mua gói lưu đầy đủ thông tin. Trên Customer Portal làm trang mua gói dịch vụ. Thanh toán tự động và xuất hóa đơn gắn ở bước sau.
Quyết định của operator: gói có thời hạn theo ngày (để trống = không hết hạn); trước khi có thanh toán tự động, số lượng chỉ được cộng khi admin xác nhận đã thanh toán; giá nhập là giá đã gồm VAT.
- Thiết kế: mỗi giao dịch mua (dbo.ServicePackagePurchases) chụp lại thông tin gói lúc mua; khi xác nhận thanh toán trở thành một "lô" có số lượng còn lại và ExpiresAt = PaidAt + ValidityDays. Số khả dụng = tổng còn lại của các lô PAID chưa hết hạn (không lưu số dư riêng, tránh lệch). Trừ số lượng khi dùng: làm ở bước tích hợp hóa đơn / tra soát.
- Admin (SYS_ADMIN): menu "Gói dịch vụ" (Danh sách gói: tạo/sửa/ngừng bán; Giao dịch mua gói: lọc, chi tiết, xác nhận thanh toán, hủy); tab "Gói dịch vụ" trong hồ sơ khách hàng (số khả dụng, lịch sử, tạo giao dịch thay khách).
- Customer Portal: menu "Dịch vụ → Gói dịch vụ": số khả dụng, các gói đang bán theo loại, mua (OWNER/ADMIN), lịch sử mua và hủy yêu cầu đang chờ.
- Mọi thay đổi ghi AuditLogs (SERVICE_PACKAGE_*).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/018-service-packages.sql`, `database/scripts/025-verify-service-packages.sql`, `database/scripts/026-rollback-service-packages.sql`.
- ADD `src/TAPortal.Shared/ServicePackages.cs`; MODIFY `src/TAPortal.Shared/UiIcon.cs` (package, calendar, cash) ⇒ deploy portal + invoice.
- ADD `src/TAPortal.Web/{Controllers/ServicePackagesController.cs, Controllers/CustomerServicePackageActions.cs, Models/ServicePackageModels.cs, Views/ServicePackages/*, Views/Customers/ServicePackages.cshtml}`; MODIFY `Views/Shared/_Layout.cshtml` (menu, ParentAction), `Program.cs` và `Models/CustomerWorkspaceModels.cs` (chỉ dòng của change này; phần CUSTOMER-PROFILE chưa commit của agent khác giữ ngoài commit).
- ADD `src/TACustomerPortal.Web/{Controllers/ServicePackagesController.cs, Models/ServicePackageModels.cs, Views/ServicePackages/Index.cshtml}`; MODIFY `Program.cs`, `Views/Shared/_Layout.cshtml`.
- ADD `tests/TACustomerPortal.Tests/SafeServicePackagesTests.cs`; MODIFY `SafeCustomerPaymentsTests.cs`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path: database/migrations/018-service-packages.sql
- Thứ tự chạy: sau 017; chỉ tạo bảng mới
- Preflight / Verify / Rollback: database/scripts/025-verify-service-packages.sql / database/scripts/026-rollback-service-packages.sql (từ chối khi đã có giao dịch PAID)
- Pre-check: VERIFY_STATUS=NOT_APPLIED. Dry-run (một transaction, sqlcmd -b, kiểm tra @@TRANCOUNT sau mỗi batch): tạo 2 bảng → verify PASS (15/27 cột, 2 unique index, 7 check, 2 FK) → rollback → verify NOT_APPLIED → ROLLBACK, DRYRUN_TRANCOUNT=0; pre-check lặp lại không đổi.
- Gates trên HEAD 0a5c9b6 (worktree sạch): restore OK, build Release 0 lỗi, Safe tests 377/377. Backup: bản COPY_ONLY 2026-11-04 17:38 (UTC+7) còn trong 24 giờ.
- Chạy thật 2026-11-04 11:41Z: 2 bảng tạo xong; verify 025 VERIFY_STATUS=PASS; chạy các truy vấn của SqlServicePackageStore với WHERE 1=0 trên schema thật: STORECHECK=PASS.
- Execution: EXECUTED

## Test và kết quả
- Build solution Release 0 lỗi; Safe tests 388/388 (working tree, gồm test của agent khác chưa commit).

## Publish
- portal (TAPortal.Web → TA.Portal): DEPLOYED 2026-11-04 18:44 (UTC+7), commit 0a5c9b6, artifact SHA256 5e11f0d8aedc9ed758c32fbab6a11c3f9eca1f2a7f2351e422351c96f24e6504, package 3435C43677C7BEB5A8D194A2646F84C6EA2F19A69B2D4B2B5DA292C5FF6D3C4D; backup C:\TADeploy\backups\portal-20261104-184437; health HTTP 200.
- invoice (TACustomerPortal.Web → TA.Invoice): DEPLOYED 2026-11-04 18:46 (UTC+7), commit 0a5c9b6, artifact SHA256 ed2267d49179a9209dc57920bb41e680084a172dde3cd206a8b165ad3e63676c, package 0EC99BD77E50670290D5053B856961FA8373ADD9E99385A8D32195B05803E97E; backup C:\TADeploy\backups\invoice-20261104-184558; health HTTP 200.
- webhooks: không deploy (dùng TAPortal.Shared nhưng không dùng phần gói dịch vụ).

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 0a5c9b6 (source)
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa trừ số lượng khi dùng (xuất hóa đơn / tra soát giao dịch): làm khi tích hợp hai chức năng đó, trừ theo lô hết hạn sớm nhất trước.
- Chưa có thanh toán tự động cho giao dịch mua gói: hiện admin xác nhận tay; bước sau gắn QR thanh toán + webhook để tự xác nhận, và xuất hóa đơn cho giao dịch mua gói.
- Chưa có điều chỉnh số dư thủ công (tặng/thu hồi) ngoài luồng mua gói.
