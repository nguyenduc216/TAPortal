# Change ADMIN-ROLE-PERMISSIONS-CHG-20261126T123328Z-3F2256

- Feature ID: `ADMIN-ROLE-PERMISSIONS`
- Change ID: `ADMIN-ROLE-PERMISSIONS-CHG-20261126T123328Z-3F2256`
- Created UTC: 2026-11-26T12:33:28.869116+00:00
- Tiêu đề: Reset Customer Portal password no longer rejects customer accounts as TA staff
- Trạng thái: DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-26, ảnh màn hình Khách hàng → Tài khoản Portal): "đặt mật khẩu người dùng của hóa đơn tự động bị lỗi, hãy chỉnh lại là tài khoản hóa đơn tự động không phải là tài khoản portal". Nút "Đặt lại mật khẩu" báo "Không đặt lại mật khẩu tài khoản nhân viên TA ở đây; dùng màn hình Người dùng." cho một tài khoản chỉ dùng Hóa đơn tự động.

Nguyên nhân (HIGH, lỗi hồi quy từ ADMIN-ROLE-PERMISSIONS-CHG-20261114T013654Z-1EEB49, commit 005d430):
- `PortalDb.GetUsersAsync` được đổi sang `StaffAccountSql.StaffUsers` để màn hình Người dùng chỉ hiện nhân viên. Câu này ẩn mọi tài khoản chỉ thuộc khách hàng.
- `CustomerPortalAccounts.CandidateUsersAsync` vẫn đọc từ `GetUsersAsync`. Vì vậy tài khoản khách hàng không bao giờ là "ứng viên", và `ResetPortalPassword` coi nó là nhân viên TA.
- Kiểm tra trên DB (chỉ đọc): tài khoản trong ảnh không có vai trò TAPortal nào, chỉ có 1 quyền truy cập khách hàng.
- Cùng nguyên nhân, "Cấp quyền cho tài khoản có sẵn" không cho chọn tài khoản khách hàng đã có (tài khoản dùng chung giữa nhiều khách hàng).

Sửa:
- `StaffAccountSql.AllUsers`: mọi tài khoản chưa xóa.
- `PortalDb.GetAllUsersAsync`.
- `CandidateUsersAsync` đọc `GetAllUsersAsync` rồi lọc bằng `PortalCandidates.Filter` (bỏ tài khoản có vai trò TAPortal), đúng quy tắc ban đầu. SQL `MembershipAdminStore.ResetPassword` không đổi (vẫn yêu cầu là thành viên của khách hàng và không có vai trò TAPortal).
- Màn hình Người dùng không đổi (vẫn chỉ nhân viên).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Web/Services/PortalDb.cs`
- MODIFY `src/TAPortal.Web/Services/CustomerPortalAccounts.cs`
- MODIFY `tests/TACustomerPortal.Tests/SafeStaffUsersTests.cs` (test mới `CustomerPortalCandidatesReadEveryAccountNotTheStaffList`)

## Migration SQL (KHÔNG tự chạy)
- Không có migration.
- Execution: NOT_APPLICABLE

## Test và kết quả
- `dotnet build TAPortal.sln -c Release`: 0 lỗi.
- Safe tests: 858/858 PASS.

## Publish
- Project: TAPortal.Web (target `portal`)
- Trạng thái: DEPLOYED (commit 340fcd9, health 200)

## Git
- Branch: feature/einvoice-softdreams (đang là nhánh làm việc, đã gộp main)
- Merge: vào main sau deploy (standing authorization)

## Rủi ro, quyết định và việc còn lại
- Danh sách "Cấp quyền cho tài khoản có sẵn" giờ có cả tài khoản khách hàng khác (tài khoản dùng chung), như thiết kế trước 14/11.
- Operator thử lại "Đặt lại mật khẩu" trên tài khoản trong ảnh.
