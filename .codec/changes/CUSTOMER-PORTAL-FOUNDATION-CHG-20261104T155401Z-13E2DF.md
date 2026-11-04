# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261104T155401Z-13E2DF`
- Created UTC: 2026-11-04T15:54:01.655240+00:00
- Tiêu đề: Gói dịch vụ: sửa lỗi trùng tên cột DisplayName trong danh sách giao dịch mua gói
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-04, ảnh chụp Admin "Giao dịch mua gói"): trang báo "Không đọc được dữ liệu gói dịch vụ". Log production: `SqlException: The column 'DisplayName' was specified multiple times for 'x'` (SERVICE_PURCHASES_UNAVAILABLE). Danh sách giao dịch bọc câu SELECT trong bảng dẫn xuất `x`, mà câu SELECT có 3 cột `DisplayName` (người yêu cầu/xác nhận/hủy) và `Code`/`Name` không alias. Lỗi cũng ảnh hưởng lịch sử mua gói ở Customer Portal (cùng hàm PurchasesAsync).

Sửa: đặt alias duy nhất (CustomerCode, CustomerName, RequestedByName, ConfirmedByName, CancelledByName). Reader đọc theo thứ tự cột nên không đổi gì khác.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Shared/ServicePackages.cs`
- MODIFY `tests/TACustomerPortal.Tests/SafeServicePackagesTests.cs` (test `PurchaseSelectHasUniqueColumnNames`)

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Execution: NOT_APPLICABLE (không có thay đổi SQL)

## Test và kết quả
- Safe tests: 401/401 pass.
- Chạy thử câu truy vấn trên DB production: NOT_RUN (lệnh bị chặn bởi quyền của phiên agent); xác nhận qua trang sau deploy.

## Publish
- portal + invoice. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: cdc3432
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không có.
