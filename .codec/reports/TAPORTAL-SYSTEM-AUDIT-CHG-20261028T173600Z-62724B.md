# TAPORTAL CHECKPOINT 7 — ACCOUNT INTEGRITY DELIVERY REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID TAPORTAL-SYSTEM-AUDIT; CHANGE_ID TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B; branch feature/customer-portal-mvp01
- Starting SHA 3fddd8c28a0f30cc29b39c75e7be323ce2d5b4db (bằng origin, không có commit nào từ agent khác)
- Source SHA 6a2462b526de34e9f166541447c6e2f6d51bad59
- Delivery SHA: commit chứa báo cáo này — báo sau push

## B. Audit
- AUD-15: điều kiện đăng nhập ở SQL đã nhất quán; lỗi nằm ở luồng sửa (race mất SYS_ADMIN cuối — AUD-27, ghi đè lẫn nhau — AUD-28, hiển thị trạng thái gây hiểu nhầm).
- AUD-16: TaxCode không có chuẩn hóa/kiểm tra trùng; index không unique.
- AUD-17: chưa có xóa; schema có IsDeleted; FK không cascade.
- AUD-25: AuditLogs định nghĩa ở migration 006 nhưng chưa xác minh trên TAPortalSys; có thể đổi quyền thành công mà không có audit (AUD-29).
- Chi tiết: docs/audits/taportal-checkpoint7-account-integrity-TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B.md

## C. Account Status
- Chính sách: được đăng nhập ⇔ IsDeleted=0 ∧ IsActive=1 ∧ Status='ACTIVE' (Admin, Customer Portal, membership, tập SYS_ADMIN).
- Validation server-side: transaction serializable; stamp; email trùng; chặn tự vô hiệu hóa; không làm mất SYS_ADMIN hoạt động cuối kể cả khi đồng thời; không đổi Status; membership độc lập với user.
- Login: user bị vô hiệu hóa/khóa/xóa bị từ chối ở lần đăng nhập và ở request kế tiếp của phiên hiện có.
- Status: **FIXED (application)**; ý nghĩa Status khác ACTIVE PENDING_APPROVAL.

## D. Customer Tax Code
- Chuẩn hóa: bỏ khoảng trắng; giữ "-" và số 0 đầu. Định dạng 10 hoặc 10-3 số cho giá trị mới/thay đổi.
- Trùng: với mọi khách hàng chưa xóa; không báo trùng chính mình; thông báo nêu mã KH bị trùng.
- Concurrency: tuần tự hóa trong ứng dụng (SERIALIZABLE + UPDLOCK/HOLDLOCK). **Không** có ràng buộc DB cho tới khi operator chạy migration 009 (tùy chọn).
- Status: **PARTIAL** (application FIXED, database PENDING_OPERATOR).

## E. Soft Delete
- User: **FIXED** — xác nhận, CSRF, SYS_ADMIN + AUTH.USERS.DELETE, không tự xóa/không xóa SYS_ADMIN cuối, IsDeleted=1 + IsActive=0, không hard delete/cascade, giữ vai trò/membership/lịch sử, endpoint sửa trả 404 sau khi xóa.
- Customer: **PENDING_APPROVAL** (chưa có quy tắc cho doanh nghiệp đang có membership/dữ liệu); dùng "Ngừng hoạt động" hiện có.
- Restore/purge/retention: PENDING_APPROVAL.

## F. Security
- SYS_ADMIN: khóa tập SYS_ADMIN trong transaction cho đổi vai trò, sửa user và xóa user; actor kiểm tra lại từ DB.
- Tenant isolation: không đổi Customer Portal/Shared; membership không bị chạm; 34 SafePortalTests PASS.
- Session: user không còn đủ điều kiện bị từ chối ở request kế tiếp (cơ chế xác thực lại cookie hiện có).
- Overposting: Username/Status/PasswordHash/IsDeleted bị bỏ qua (test).
- Audit: tùy chọn RequireDurableAudit bắt buộc có audit bền vững cho mọi thay đổi user/vai trò.

## G. Database
- Schema: không đổi. DATABASE_NOT_VERIFIED.
- Script/migration (đều NOT_EXECUTED): scripts/007 verify AuditLogs (read-only, VERIFY_STATUS), scripts/008 audit trùng MST (read-only), migrations/009 unique index MST (tùy chọn), scripts/010 rollback 009, migrations/010 tạo AuditLogs nếu thiếu (có điều kiện), scripts/009 template GRANT INSERT.
- Thứ tự cho operator: xem change record. Artifact không phụ thuộc migration nào.

## H. Changed Files
| File | Lý do | Finding |
|---|---|---|
| src/TAPortal.Web/Services/UserAccountStore.cs (mới) | Policy trạng thái, store sửa/xóa mềm user, AuditSql | AUD-15/17/25/27/28 |
| src/TAPortal.Web/Services/CustomerAdminStore.cs (mới) | TaxCodePolicy, store khách hàng | AUD-16 |
| src/TAPortal.Web/Services/UserRoleStore.cs | RequireDurableAudit | AUD-25 |
| src/TAPortal.Web/Services/PortalDb.cs | Gỡ các hàm ghi cũ không có bảo vệ | AUD-15/16 |
| src/TAPortal.Web/Controllers/PortalControllers.cs | Edit/Delete user, Customers qua store | AUD-15/16/17 |
| src/TAPortal.Web/Models/PortalModels.cs | UserEditVm.Stamp | AUD-28 |
| src/TAPortal.Web/Program.cs | DI store + AdminAuditOptions | AUD-25 |
| src/TAPortal.Web/Views/Users/{Edit,Delete,Index}.cshtml, Views/Customers/Edit.cshtml | Giao diện | AUD-15/16/17 |
| tests/TACustomerPortal.Tests/SafeAdminAccountTests.cs (mới) | 32 test | tất cả |
| database/scripts/007–010, database/migrations/009–010 | Script cho operator | AUD-16/25 |

## I. Build & Tests
- Release build PASS 0/0. Safe tests 136 passed / 0 failed / 0 skipped. Codec 4/4, validate PASS. Smoke Admin 15/15.
- NOT_VERIFIED: khóa SQL thật, UPDATE xóa mềm và cột DeletedAt/DeletedBy, ghi AuditLogs, truy vấn SQL từ chối user đã xóa, script 007–010.

## J. Publish
- Admin TAPortal.Web: PUBLISHED (local) / NOT_DEPLOYED — D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web; source 6a2462b526de34e9f166541447c6e2f6d51bad59; 2026-10-28T17:44:29Z; exit 0; 57 files; manifest .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B-TAPortal.Web.sha256; **artifact SHA256 fc042e76e4c6a4e55c7a9a681106cc986e1114e6c9c97240f592750c9bbea727**. Bản trước 5adf697b… đã backup.
- Customer TACustomerPortal.Web: NOT_REQUIRED — không đổi Customer/Shared; ea77b3be501ea28fe80ce54665a805b6798b502137347ce11f7d356547f8ac34 (source 336ab7c).

## K. Git
- Source commit 6a2462b526de34e9f166541447c6e2f6d51bad59; delivery commit/push/remote HEAD trong thông báo bàn giao; PR #7; NOT_MERGED.

## L. Operator Handoff
- PENDING_APPROVAL: xóa mềm doanh nghiệp; restore/purge/retention; ý nghĩa Users.Status khác ACTIVE.
- SQL cho operator (thứ tự): 007 → (010 nếu thiếu bảng) → GRANT theo 009 → 007 lại đến PASS → `Security__Audit__RequireDurableAudit=true`; 008 → (009 nếu PASS và đồng ý).
- Remaining risks: AUD-29 cho tới khi bật RequireDurableAudit; trùng MST qua công cụ ngoài ứng dụng cho tới khi chạy migration 009; AUD-23/24/26.
- UAT: mục 6 báo cáo audit — gộp vào đợt UAT tổng thể sau checkpoint cuối.
- Rollback: backup Admin trong publish\rollback\…; `git revert 6a2462b`; migration 009 → scripts/010.
- Next checkpoint: theo kế hoạch operator (ví dụ AUD-23/24/26 hoặc chính sách xóa mềm doanh nghiệp sau khi phê duyệt).
- Trạng thái: READY_FOR_UAT (PARTIAL cho AUD-16/17/25), không production-ready.
