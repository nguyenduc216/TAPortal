# Change CONTRACTS-AND-TASKS-CHG-20261113T114748Z-364101

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261113T114748Z-364101`
- Created UTC: 2026-11-13T11:47:48.574815+00:00
- Tiêu đề: Customer staff assignment (manager/executors) and task filter popup
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-13): cạnh ô tìm kiếm có nút tìm và nút mở popup lọc (mobile và desktop); popup lọc theo tiến độ (chọn nhiều), nhân viên, khách hàng (chọn nhiều), khoảng thời gian (chọn loại ngày rồi từ ngày - đến ngày). Khách của nhân viên: khách được phân công (1 quản lý, nhiều thực hiện), khách của công việc nhân viên phụ trách/cùng làm, khách của hợp đồng giao cho nhân viên.

Kiểm tra trước: khách hàng chưa gắn trực tiếp với nhân viên (chỉ gián tiếp qua hợp đồng - 0 bản ghi, việc lặp - 0, công việc). Operator chọn thêm phân công, 1 quản lý + nhiều thực hiện, chọn được loại ngày.

Đã làm:
- Migration 041 `dbo.CustomerStaff` (CustomerId, EmployeeId, Role MANAGER/EXECUTOR; index duy nhất lọc: 1 quản lý/khách).
- Khách hàng > Tổng quan: thẻ "Nhân sự phụ trách" (xem; sửa khi có CRM.CUSTOMERS.UPDATE): chọn 1 quản lý, tick nhân viên thực hiện. `CustomerStaffStore`, action `CustomersController.Staff`.
- Danh sách công việc: ô tìm + nút Tìm + nút "Bộ lọc" (đếm số bộ lọc đang dùng) + dòng tóm tắt bộ lọc và "Xóa lọc". Popup (<dialog>, mobile là bottom sheet): Phạm vi; Tiến độ/trạng thái (nhiều: Chưa hoàn thành, Quá hạn, Mới, Đang làm, Hoàn thành, Đã hủy); Nhân viên (nhiều, phụ trách hoặc cùng làm, có ô tìm); Khách hàng (nhiều, có ô tìm; khi chọn nhân viên chỉ hiện khách của họ kèm lý do Quản lý/Thực hiện/Có việc/Hợp đồng, nút "Hiện tất cả"); Khoảng thời gian (Hạn hoàn thành / Ngày bắt đầu / Ngày hoàn thành, từ - đến).
- `WorkTaskFilter`: Statuses/Employees/Customers/DateField/From/To; tham số cũ status/owner/customer vẫn dùng được (link từ Báo cáo). SQL lọc danh sách dạng ",a,b," (SQL Server 2014).
- Icon "filter" thêm vào `UiIcon` (Shared).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/041-customer-staff.sql`, `database/scripts/071-verify-customer-staff.sql`, `database/scripts/072-rollback-customer-staff.sql`
- ADD `src/TAPortal.Web/Services/CustomerStaffStore.cs`, `src/TAPortal.Web/Controllers/CustomerStaffActions.cs`, `tests/TACustomerPortal.Tests/SafeTaskFilterTests.cs`
- MODIFY `src/TAPortal.Web/Services/WorkTaskStore.cs`, `Program.cs`, `Views/Work/Tasks.cshtml`, `Views/Customers/Details.cshtml`, `wwwroot/css/ta-admin.css`, `wwwroot/js/ta-work.js`, `src/TAPortal.Shared/UiIcon.cs`, `tests/TACustomerPortal.Tests/SafeAdminWorkTests.cs`

## Migration SQL
- Database: TAPortalSys
- Script: `database/migrations/041-customer-staff.sql` (sau 038; 039/040 là migration dữ liệu một lần, không lưu Git, số giữ lại)
- Verify 071, Rollback 072.
- Execution: xem report.

## Test và kết quả
- Safe tests 724/724 (thêm SafeTaskFilterTests 6, TheTaskListHasASearchBarAndAFilterPopup).
- Trang render thật (test host) xem ở 375px và 1280px: thanh tìm 1 dòng; popup bottom sheet trên mobile, 640px giữa màn trên desktop; chọn/bỏ chọn/đóng chạy đúng.

## Publish
- portal, invoice, webhooks (Shared đổi). Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- Merge: xem report

## Rủi ro, quyết định và việc còn lại
- Chưa xem trên trang thật. Hiện chưa khách nào có phân công; cần nhập trên trang khách hàng.
