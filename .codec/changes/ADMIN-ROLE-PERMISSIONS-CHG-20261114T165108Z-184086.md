# Change ADMIN-ROLE-PERMISSIONS-CHG-20261114T165108Z-184086

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261114T165108Z-184086`
- Created UTC: 2026-11-14T16:51:08.251583+00:00
- Tiêu đề: Anh dai dien (avatar) nhan vien: tai anh, hien thi tren moi danh sach
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-14): "BỔ SUNG PHẦN CẬP NHẬT ẢNH AVATAR CỦA NHÂN VIÊN".

Đã làm:
- Migration 051 `dbo.EmployeePhotos` (1 ảnh/nhân viên, ≤ 300 KB, JPEG/PNG/WebP), verify 091, rollback 092.
- Tải ảnh: trang hồ sơ nhân viên (quyền HR.EMPLOYEE.UPDATE, `AvatarController.SetEmployee`) và Hồ sơ cá nhân (ảnh của chính mình, `AccountController.SetAvatar`). Trình duyệt cắt vuông, thu nhỏ 320 px JPEG rồi gửi; server kiểm tra kiểu theo byte đầu file và kích thước; audit EMPLOYEE_PHOTO_SET / REMOVED; có nút xóa ảnh.
- Hiển thị (`Avatar.Render`, ảnh qua `/Account/Avatar/{employeeId}?v=`; chưa có ảnh thì chữ cái đầu): thanh trên cùng, Quản lý người dùng, danh sách nhân viên, hồ sơ nhân viên, Hồ sơ cá nhân, ô chọn người khi giao việc. Danh sách ai có ảnh được cache 10 phút, xóa cache khi đổi ảnh.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/051-employee-photos.sql`, `database/scripts/091-verify-employee-photos.sql`, `database/scripts/092-rollback-employee-photos.sql`
- ADD `src/TAPortal.Web/Services/EmployeePhotoStore.cs`, `Controllers/AvatarController.cs`, `Controllers/AccountController.Avatar.cs`, `Models/AvatarModels.cs`, `Views/Shared/_PhotoForm.cshtml`, `tests/TACustomerPortal.Tests/SafeEmployeePhotoTests.cs`
- MODIFY `Program.cs`, `Views/Shared/_Layout.cshtml`, `Views/Users/Index.cshtml`, `Views/Employees/Index.cshtml`, `Views/Employees/Details.cshtml`, `Views/Account/Profile.cshtml`, `Views/Work/_PeoplePicker.cshtml`, `wwwroot/css/ta-design.css`, `wwwroot/js/ta-portal.js`

## Migration SQL
- Database: TAPortalSys; Script: `database/migrations/051-employee-photos.sql`; Verify 091; Rollback 092.
- Execution: xem report.

## Test và kết quả
- Release build 0 lỗi; Safe tests 775/775 (thêm SafeEmployeePhotoTests 4).

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- Merge: main

## Rủi ro, quyết định và việc còn lại
- Chưa thử tải ảnh trên portal thật; ảnh HEIC (iPhone) chỉ thu nhỏ được nếu trình duyệt đọc được, nếu không server từ chối và báo định dạng.
