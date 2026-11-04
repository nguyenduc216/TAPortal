# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T112825Z-F9FA74`
- Created UTC: 2026-11-04T11:28:25.511674+00:00
- Tiêu đề: Gói dịch vụ: quản lý gói (hóa đơn / tra soát giao dịch / combo), số dư khách hàng, giao dịch mua gói; trang mua gói trên Customer Portal
- Trạng thái: IMPLEMENTED / TESTS_PASS

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
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_EXECUTED

## Test và kết quả
- Build solution Release 0 lỗi; Safe tests 388/388 (working tree, gồm test của agent khác chưa commit).

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
