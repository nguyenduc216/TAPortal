# Change ADMIN-ROLE-PERMISSIONS-CHG-20261114T013654Z-1EEB49

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261114T013654Z-1EEB49`
- Created UTC: 2026-11-14T01:36:54.394790+00:00
- Tiêu đề: Quan ly nguoi dung chi hien tai khoan nhan vien T.A, an tai khoan khach hang
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-14): Quản lý người dùng trên TAPortal chỉ có tài khoản nhân viên T.A; tài khoản khách hàng nằm trong từng khách hàng.

Nguyên nhân: `PortalDb.GetUsersAsync` lấy mọi dòng dbo.Users. Production: 9 tài khoản, 2 là tài khoản khách (có CustomerPortalMemberships, không vai trò/quyền TAPortal, không gắn nhân viên).

Đã làm: `StaffAccountSql.IsStaff` = không có membership khách, hoặc có vai trò, quyền trực tiếp, hay hồ sơ nhân viên (nhân viên kiêm chủ doanh nghiệp vẫn hiện). Áp dụng cho danh sách Người dùng và ô "Người dùng hiện hành" ở Tổng quan. Ghi chú cuối danh sách chỉ đường tới Khách hàng > Tài khoản Portal. Chọn tài khoản liên kết nhân viên đã loại tài khoản khách từ trước. Kiểm tra trên production (đọc): còn 7, ẩn 2.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Services/PortalDb.cs`, `src/TAPortal.Web/Services/DashboardStore.cs`, `src/TAPortal.Web/Views/Users/Index.cshtml`
- ADD `tests/TACustomerPortal.Tests/SafeStaffUsersTests.cs`

## Migration SQL
- Không có.

## Test và kết quả
- Safe tests 737/737.

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- Merge: xem report

## Rủi ro, quyết định và việc còn lại
- Trang Sửa/Xóa/Vai trò theo id vẫn mở được tài khoản khách nếu biết đường dẫn (không còn link từ danh sách).
