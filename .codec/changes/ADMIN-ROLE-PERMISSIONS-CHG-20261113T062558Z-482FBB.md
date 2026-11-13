# Change ADMIN-ROLE-PERMISSIONS-CHG-20261113T062558Z-482FBB

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261113T062558Z-482FBB`
- Created UTC: 2026-11-13T06:25:58.840756+00:00
- Tiêu đề: Cau hinh he thong gom Mau email, Dieu khoan, Bao mat mat khau
- Trạng thái: IMPLEMENTED

## Yêu cầu và phạm vi
Operator (2026-11-13): rà soát "Cấu hình hệ thống" để đưa các chức năng cấu hình vào đây thay vì menu riêng; chọn phương án "Gộp, giữ nhóm Tingee".

Đã làm:
- Mục menu "Cấu hình hệ thống" (trước là "Sắp có") mở `SystemSettingsController.Index`, chuyển tới tab đầu tiên được phép.
- Tab (`Views/Shared/_SystemSettingsTabs.cshtml`): Mẫu email, Điều khoản dịch vụ (quyền INVOICEAPP.CONTENT.VIEW), Bảo mật mật khẩu (AUTH.ROLES.MANAGE, chỉ SYS_ADMIN). Breadcrumb "Cấu hình hệ thống".
- Bỏ nhóm menu "Hóa đơn tự động" và mục "Bảo mật mật khẩu" riêng. Nhóm Tingee (Sandbox, Hóa đơn Sandbox, log, Cài đặt thông báo) giữ nguyên.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- ADD `src/TAPortal.Web/Controllers/SystemSettingsController.cs`, `src/TAPortal.Web/Views/Shared/_SystemSettingsTabs.cshtml`
- MODIFY `src/TAPortal.Web/Views/Shared/_Layout.cshtml`, `src/TAPortal.Web/Views/InvoiceAppContent/{Index,Email,Terms}.cshtml`, `src/TAPortal.Web/Views/PasswordSecurity/Index.cshtml`
- MODIFY `tests/TACustomerPortal.Tests/{SafeWebsiteTests,SafeUiDesignSystemTests}.cs`

## Migration SQL (KHÔNG tự chạy)
- Không có.

## Test và kết quả
- Release build 0 lỗi; Safe tests 683/683 (tab, mục menu đang chọn, chuyển hướng /SystemSettings).

## Publish
- portal. Xem report.

## Git
- Branch: feature/tx-sync
- Commit SHA: xem report
- PR:
- Merge: xem report
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Không.
