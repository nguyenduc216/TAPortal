# Change CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9

- Feature ID: `CONTRACTS-AND-TASKS`
- Change ID: `CONTRACTS-AND-TASKS-CHG-20261107T062339Z-50D5E9`
- Created UTC: 2026-11-07T06:23:39.023750+00:00
- Tiêu đề: Hop dong theo khach hang, giao viec, viec cua toi, viec lap, bao cao tien do
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-07), TA Portal (nội bộ): quản lý hợp đồng theo khách hàng (có file đính kèm); giao việc và kiểm soát công việc nhân viên; việc lặp tự sinh theo kỳ; báo cáo tiến độ từng người. Bổ sung của operator: một việc có nhiều người làm và một người chịu trách nhiệm chính; checklist bắt buộc, mỗi mục ghi thời điểm hoàn thành; luồng ghi chú/trao đổi; chỉ người làm, người giao và cấp giám sát thấy và tương tác (người khác không thấy việc); quyền tự tạo việc cho mình tách với quyền giao việc; dashboard "Việc của tôi" khi đăng nhập (việc chưa hoàn thành, sắp theo hạn gần nhất hoặc tương tác gần nhất, cập nhật nhanh trạng thái / checklist / trao đổi ngay trên thẻ); nút micro nhập bằng giọng nói cạnh các ô nhập văn bản.

Quyết định: không có bước duyệt (NEW → IN_PROGRESS → DONE, mở lại được; chỉ người giao / giám sát có ASSIGN mới hủy); DONE chỉ khi có checklist và mọi mục đã tích; tiến độ % tính từ checklist. Trang đích sau đăng nhập là "Việc của tôi" cho mọi tài khoản có TASKS.VIEW (kể cả SYS_ADMIN). Nhập giọng nói dùng Web Speech API của trình duyệt (vi-VN; Chrome/Edge gửi âm thanh tới dịch vụ nhận dạng của trình duyệt); TA Portal đổi Permissions-Policy thành microphone=(self), Customer Portal không đổi. Việc lặp: tác vụ nền mỗi giờ + nút "Tạo việc kỳ này ngay"; không tạo bù kỳ đã qua trước ngày tạo mẫu.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `database/migrations/031-contracts-and-work-tasks.sql`, `database/scripts/051-verify-contracts-and-work-tasks.sql`, `database/scripts/052-rollback-contracts-and-work-tasks.sql`
- ADD `src/TAPortal.Web/Services/WorkTaskPolicy.cs`, `WorkTaskStore.cs`, `WorkTaskScheduler.cs`, `ContractStore.cs`; `Controllers/WorkController.cs`, `ContractsController.cs`; `Models/WorkModels.cs`, `ContractModels.cs`; `Views/Work/*` (Index, _TaskCard, Tasks, Details, Edit, Templates, TemplateEdit, Report), `Views/Contracts/*`; `wwwroot/js/ta-work.js`
- MODIFY `src/TAPortal.Web/Security/AdminPermissions.cs`, `Program.cs`, `Views/Shared/_Layout.cshtml`, `Views/Account/AccessDenied.cshtml`, `Views/Customers/Details.cshtml`, `wwwroot/css/ta-admin.css`
- ADD `tests/TACustomerPortal.Tests/SafeAdminWorkTests.cs`, `SafeAdminContractTests.cs`; MODIFY `SafeAdminPermissionTests.cs`, `SafeAdminDashboardUxTests.cs`, `SafeReleaseRegressionTests.cs`

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- `031-contracts-and-work-tasks.sql` (verify 051, rollback 052): 7 bảng mới (CustomerContracts, CustomerContractFiles, WorkTaskTemplates, WorkTasks, WorkTaskMembers, WorkTaskChecklistItems, WorkTaskActivities), dãy số TASK_CODE (CV + 6 số), 10 quyền CONTRACTS.* / TASKS.* cấp cho SYS_ADMIN (marker A0000031-…). Rollback từ chối khi đã có dữ liệu.
- Execution: xem report

## Test và kết quả
- Release build 0 lỗi; Safe tests 592/592 (thêm SafeAdminWorkTests, SafeAdminContractTests 20 test).

## Publish
- Chỉ TAPortal.Web (portal). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Chưa thử đăng nhập thật trên production; nhân viên cần được liên kết tài khoản với hồ sơ nhân viên và được cấp vai trò có TASKS.VIEW (hiện chỉ SYS_ADMIN có quyền mới).
- Chưa có thông báo email/Zalo khi được giao việc (để sau theo thống nhất).
