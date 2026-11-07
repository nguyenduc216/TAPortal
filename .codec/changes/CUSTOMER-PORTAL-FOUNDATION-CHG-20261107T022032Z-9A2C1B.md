# Change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T022032Z-9A2C1B

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Change ID: `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T022032Z-9A2C1B`
- Created UTC: 2026-11-07T02:20:32.414792+00:00
- Tiêu đề: Revert Customer Portal staff accounts and per-function permissions (operator: TA Portal only)
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-07): việc quản lý tài khoản và phân quyền chỉ làm trên T.A Portal (nội bộ T.A). Customer Portal chưa làm phân quyền: khách hàng dùng một tài khoản cho mọi chức năng như trước.

Sửa: hoàn nguyên toàn bộ change `CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T020426Z-95622D` (merge 3daf4d3, commit 4066956) bằng `git revert -m 1 3daf4d3`: bỏ trang /members, CustomerAccess/CustomerModule, ModuleDenied, CustomerMemberStore, các thay đổi ẩn theo quyền ở controller/view, customer.css/js, test SafeCustomerMembersTests; CanManage(m) trở lại OWNER/ADMIN. Bỏ nhãn MEMBER trong TAPortal.Shared và ô chọn MEMBER ở trang quản trị (thêm ở c16e082).

Giữ lại: file migration 029 và script 047/048 (029 đã EXECUTED trên TAPortalSys: cột `Permissions` NULL và giá trị 'MEMBER' trong CK_CPM_Role vẫn còn nhưng không được dùng; 0 dòng MEMBER/Permissions). Không chạy rollback 048 (chỉ chạy theo quy trình migration; chờ operator quyết).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- Hoàn nguyên 38 file của change CUSTOMER-PORTAL-FOUNDATION-CHG-20261107T020426Z-95622D trong `src/TACustomerPortal.Web`, `tests/TACustomerPortal.Tests`.
- MODIFY `src/TAPortal.Shared/PortalRoleText.cs`, `MembershipAdminStore.cs`, `src/TAPortal.Web/Views/Customers/PortalAccounts.cshtml`.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Không có migration mới. 029 vẫn EXECUTED (không dùng).
- Execution: NOT_APPLICABLE

## Test và kết quả
- Release build 0 lỗi; Safe tests 531/531.

## Publish
- invoice (Customer Portal) và portal (Shared đổi). Xem report.

## Git
- Branch: feature/customer-portal-mvp01
- Commit SHA: 03432e8
- PR:
- Merge: fast-forward main
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Cột Permissions/giá trị MEMBER còn trong DB: có thể rollback bằng 048 theo quy trình nếu operator muốn.
