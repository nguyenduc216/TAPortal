# Change CONTRACTS-AND-TASKS-CHG-20261107T074015Z-920E5B

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261107T074015Z-920E5B`
- Created UTC: 2026-11-07T07:40:15.563872+00:00
- Tiêu đề: Sua loi SQL Viec cua toi va loi Tong quan khi nhat ky thieu Action
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07) gửi ảnh "Không thể xử lý yêu cầu", mã 00-71414085193f743268885eafef5d39ce-…: "kiểm tra lỗi này".

Nguyên nhân (Event Log ứng dụng trên server):
1. `/` (Tổng quan, đúng mã yêu cầu): `SqlDashboardStore` đọc `dbo.AuditLogs.Action` bằng GetString; 230/232 dòng (HR core bootstrap, tháng 10) có Action NULL → SqlNullValueException (lỗi có từ trước, lộ khi SYS_ADMIN mở Tổng quan). Sửa: đọc null an toàn, hiển thị "Thay đổi dữ liệu".
2. `/Work` (Việc của tôi): SQL ghép từ raw string literal (C# bỏ dấu xuống dòng cuối) thành `SELECTt.Id` → "Incorrect syntax near 'SELECTt'" (cũng ảnh hưởng Danh sách, Chi tiết, thẻ). Sửa: mỗi mảnh có dấu xuống dòng riêng; MyOpen/Card bỏ table variable, chỉ còn SELECT.
Kiểm chứng: mọi câu SELECT của WorkTaskSql và ContractSql (MyOpen, Card, List, Detail, Report, Templates, Lookups, ContractSql.*) chạy qua Invoke-TAReadQuery trên TAPortalSys: OK. Test mới chặn lỗi dính token.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Services/WorkTaskStore.cs`, `src/TAPortal.Web/Services/DashboardStore.cs`, `src/TAPortal.Web/Views/Home/Index.cshtml`, `tests/TACustomerPortal.Tests/SafeAdminWorkTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 594/594.

## Publish
- portal. Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Câu lệnh ghi (INSERT/UPDATE) không chạy thử được bằng công cụ chỉ đọc; chúng là chuỗi đơn, không ghép.
