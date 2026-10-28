# TAPORTAL CHECKPOINT 6 — ROLE MANAGEMENT DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID: TAPORTAL-SYSTEM-AUDIT (checkpoint CHECKPOINT-06-ROLE-MANAGEMENT)
- CHANGE_ID: TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8
- Branch: feature/customer-portal-mvp01
- Starting SHA: 900db91ecb1671c072a958cd240ee12064cc76cb (bằng origin; không có commit nào từ agent khác)
- Source SHA: 36db1038ebe071ebab4f588538e118d79fb416e9
- Delivery SHA: commit chứa báo cáo này — báo sau khi push

## B. Audit
- Mô hình: UserRoles → Roles → RolePermissions → Permissions, cộng UserPermissions ALLOW/DENY; claims dựng lại từ DB mỗi request; SYS_ADMIN thỏa mọi permission.
- AUD-12: vai trò chỉ được chọn lúc tạo user, không có cách đổi/gỡ sau đó.
- Phát hiện mới: AUD-25 (MEDIUM, PENDING_OPERATOR) — `dbo.AuditLogs` trong TAPortalSys chưa xác minh; AUD-26 (LOW, OPEN) — override UserPermissions không hiển thị. AUD-12a (ma trận quyền/tạo role/UserPermissions) PENDING_APPROVAL.
- Bảng câu hỏi audit đầy đủ: docs/audits/taportal-checkpoint6-role-management-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8.md

## C. Implementation
- Danh sách người dùng: cột Vai trò + nút "Vai trò"; trang sửa có link "Quản lý vai trò".
- `/users/roles/{id}`: trạng thái tài khoản, checkbox vai trò đang hoạt động (SYS_ADMIN có nhãn "Toàn quyền hệ thống"), vai trò ngừng hoạt động hiển thị chỉ đọc và được giữ nguyên, cảnh báo khi tự sửa chính mình.
- Rules: chỉ SYS_ADMIN (kiểm tra lại từ DB); chỉ vai trò đang hoạt động trong danh mục; không mất SYS_ADMIN cuối cùng (kể cả tự gỡ); không tạo vai trò/không sửa ma trận quyền; optimistic stamp; transaction serializable + UPDLOCK; audit AuditLogs trong cùng transaction nếu có bảng; quyền mới có hiệu lực ở request kế tiếp.

## D. Security
- SYS_ADMIN protection: tập SYS_ADMIN hoạt động được khóa trong transaction; không thể còn 0 quản trị viên kể cả khi hai admin sửa chéo cùng lúc (mô hình tuần tự đã test; khóa SQL thật cần UAT).
- Self-escalation: user không phải SYS_ADMIN bị 403 ở controller (kể cả có AUTH.ROLES.MANAGE) và bị store từ chối; actor bị thu hồi sau khi mở trang → 403.
- Authorization: SystemAdminOnly + AUTH.ROLES.MANAGE; CSRF; chỉ bind roleIds + stamp (overposting bị bỏ qua); role ID do client gửi chỉ được chấp nhận khi có trong danh mục đang hoạt động; response no-store (AUD-14).

## E. Customer Portal impact
- Không đổi source/Shared của Customer Portal; không đọc/ghi CustomerPortalMemberships (kiểm tra tĩnh câu SQL); OWNER/ADMIN/VIEWER không phát sinh vai trò Admin; tenant isolation không đổi (34 SafePortalTests PASS).

## F. Changed files
| File | Lý do |
|---|---|
| src/TAPortal.Web/Services/UserRoleStore.cs (mới) | RoleChangePolicy, IUserRoleStore, SqlUserRoleStore, UserRoleSql |
| src/TAPortal.Web/Controllers/PortalControllers.cs | Action Roles GET/POST, xử lý kết quả và thông báo |
| src/TAPortal.Web/Services/PortalDb.cs | GetUserRoleMapAsync (chỉ đọc) cho danh sách |
| src/TAPortal.Web/Program.cs | Đăng ký IUserRoleStore |
| src/TAPortal.Web/Views/Users/Roles.cshtml (mới), Index.cshtml, Edit.cshtml | Giao diện |
| tests/TACustomerPortal.Tests/SafeAdminRoleTests.cs (mới) | 20 test |
| docs/audits/…checkpoint6…md, .codec/* | Báo cáo, lịch sử, manifest |

## G. Database
- Schema: không đổi. Migration: none. Execution: NONE_REQUIRED / NOT_EXECUTED.
- Access: DATABASE_NOT_VERIFIED (MCP chỉ đọc trả lỗi; không có truy vấn nào thành công).
- Operator: xác nhận `dbo.AuditLogs` (cột UserId, Action, EntityName, EntityId, OldValues, NewValues, IpAddress) và quyền INSERT cho tài khoản SQL của Admin (AUD-25). Cần quyền DELETE/INSERT trên `dbo.UserRoles` (đã cần cho tạo user).

## H. Build/tests
- Release build: PASS 0/0. Safe tests: 104 passed / 0 failed / 0 skipped (gồm 20 mới; hồi quy AUD-13/14 và Customer Portal PASS). Codec tests 4/4, validate PASS. Smoke Admin 12/12.
- Limitations: không kiểm chứng transaction/khóa SQL thật, ghi AuditLogs thật hay claims cập nhật trên trình duyệt — chỉ có mô hình in-memory và bằng chứng từ mã nguồn.

## I. Admin publish
- PUBLISHED (local) / NOT_DEPLOYED — D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web
- Source SHA 36db1038ebe071ebab4f588538e118d79fb416e9; 2026-10-28T17:29:11Z→17:29:13Z; exit 0; 57 files
- Manifest: .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8-TAPortal.Web.sha256
- Artifact SHA256: 5adf697b9fab26d521761cdb81d667121eb49147939bb1a3bfe9326c3c4afd36
- Bản trước 42d6a733… backup tại publish\rollback\TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8\deployed-TAPortal.Web

## J. Customer publish
- NOT_REQUIRED — không thay đổi src/TACustomerPortal.Web hay src/TAPortal.Shared. Artifact giữ nguyên ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34 (đã kiểm tra lại).

## K. Git
- Source commit 36db1038ebe071ebab4f588538e118d79fb416e9; delivery commit, push, remote HEAD báo trong thông báo bàn giao.
- PR #7 (https://github.com/nguyenduc216/TAPortal/pull/7); merge: NOT_MERGED.
- Không commit: 004-verify-customer-portal.sql, scene-image.png, src/.claude/, publish/, TestResults/.

## L. Operator handoff
- Theo quyết định operator: không deploy/UAT ở checkpoint này; gộp vào đợt deploy/UAT tổng thể sau checkpoint cuối.
- Việc chờ operator: AUD-25 (AuditLogs + quyền INSERT); quyết định AUD-12a nếu cần ma trận quyền/tạo role; AUD-24 từ Checkpoint 5.
- UAT checklist: mục 6 của báo cáo audit Checkpoint 6.
- Rollback: khôi phục thư mục backup Admin; `git revert 36db103`.
- Remaining risks: khóa SQL thật chưa kiểm chứng; nếu thiếu quyền INSERT AuditLogs, đổi vai trò thất bại (fail-closed); AUD-23/24/26.
- Trạng thái: READY_FOR_UAT (không production-ready).
