# Checkpoint 7 — Trạng thái tài khoản, toàn vẹn MST, xóa mềm, audit preflight (AUD-15/16/17/25)

- CHANGE_ID `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T173600Z-62724B`; starting SHA `3fddd8c28a0f30cc29b39c75e7be323ce2d5b4db`; source SHA `6a2462b526de34e9f166541447c6e2f6d51bad59`
- Agent: Claude Code (Claude Opus 5.5). Database: DATABASE_NOT_VERIFIED (MCP chỉ đọc `taportal-db` lỗi `db_ping`). Không chạy SQL nào.

## 1. AUD-15 — Trạng thái tài khoản

### Hiện trạng (đọc từ source)
| Đường kiểm tra | Điều kiện |
|---|---|
| Admin đăng nhập / xác thực lại cookie mỗi request (`PortalDb.GetAuthUserAsync`) | `IsDeleted=0 AND IsActive=1 AND Status='ACTIVE'` |
| Customer Portal đăng nhập / cookie (`CustomerStore.ReadUser`) | giống trên |
| Customer Portal membership (`CustomerStore.Memberships`) | giống trên + membership `IsActive=1` + customer `IsActive=1 AND IsDeleted=0 AND Status='ACTIVE'` |
| Đổi mật khẩu Customer Portal | giống trên |
| Gán membership (`MembershipAdminStore.Save`) | user và customer phải hợp lệ; chỉ ghi `CustomerPortalMemberships` |
| Tập SYS_ADMIN (CP6, CP7) | giống trên + role SYS_ADMIN hoạt động |

Kết luận: ở tầng SQL, quy tắc đăng nhập **đã nhất quán**. Kích hoạt membership không kích hoạt user toàn hệ thống. Session mất hiệu lực ở request kế tiếp nhờ xác thực lại cookie.

### Lỗi đã chứng minh và cách sửa
- **AUD-27 (MEDIUM, FIXED):** `Edit` chỉ chặn tự vô hiệu hóa, không có kiểm tra theo transaction. Hai SYS_ADMIN có thể vô hiệu hóa lẫn nhau cùng lúc và làm hệ thống không còn quản trị viên. Sửa: `SqlUserAccountStore` dùng transaction serializable, UPDLOCK/HOLDLOCK trên user đích và mọi user/assignment SYS_ADMIN, và kiểm tra lại actor là SYS_ADMIN hợp lệ từ DB.
- **AUD-28 (LOW, FIXED):** hai người sửa cùng một user thì người lưu sau ghi đè người lưu trước mà không có cảnh báo. Sửa: stamp SHA-256 của DisplayName/Email/Phone/IsActive/Status; stamp cũ → thông báo và tải lại dữ liệu.
- **Hiển thị:** `AccountStatusPolicy.Describe` hiển thị thống nhất "Hoạt động / Đã vô hiệu hóa / Bị khóa (trạng thái hệ thống: X) / Đã xóa". User có `Status≠ACTIVE` được cảnh báo rằng bật "Cho phép đăng nhập" vẫn không đăng nhập được.
- **Không tự kích hoạt lại:** form không bind `Status`. Ý nghĩa của các giá trị `Status` khác ACTIVE và việc đổi chúng là **PENDING_APPROVAL**.
- Email trùng được kiểm tra trong cùng transaction (UPDLOCK), có thêm lớp bảo vệ là unique index `UX_Users_NormalizedEmail` theo migration 002 (chưa xác minh trên TAPortalSys).

## 2. AUD-16 — Mã số thuế

| Hạng mục | Kết quả |
|---|---|
| Cột | `Customers.TaxCode varchar(50) NULL` (migration 007); index `IX_Customers_TaxCode` **không unique**, lọc `IsDeleted=0` |
| Validation cũ | Chỉ giới hạn độ dài; không kiểm tra trùng |
| Chuẩn hóa | Chỉ bỏ khoảng trắng (space, tab, NBSP); giữ dấu "-" chi nhánh và số 0 đầu |
| Định dạng | 10 chữ số hoặc 10 chữ số + "-" + 3 chữ số (định dạng MST của Việt Nam). Chỉ áp dụng khi MST **mới hoặc thay đổi**; dữ liệu cũ không chặn các lần sửa khác |
| Trùng | So với mọi khách hàng **chưa xóa** (kể cả đang ngừng hoạt động, để kích hoạt lại không sinh trùng); sửa chính mình không bị báo trùng; MST chi nhánh khác MST công ty mẹ |
| Concurrency | Kiểm tra và ghi trong cùng transaction SERIALIZABLE với UPDLOCK/HOLDLOCK → các request **qua ứng dụng** được tuần tự hóa |
| Mức database | **Chưa có ràng buộc.** Migration tùy chọn `009` thêm filtered unique index `UX_Customers_TaxCode`; NOT_EXECUTED. Khi chưa chạy, việc ghi trực tiếp vào DB (SSMS/công cụ khác) vẫn có thể tạo trùng |
| Dữ liệu thật | Không sửa. Script chỉ đọc `008` liệt kê trùng/khoảng trắng/sai định dạng |

Trạng thái: **PARTIAL** — tầng ứng dụng FIXED; tầng database PENDING_OPERATOR.

## 3. AUD-17 — Xóa mềm

### Quan hệ dữ liệu (theo migration; schema TAPortalSys chưa xác minh)
`Users` được tham chiếu bởi UserRoles, UserPermissions, UserBranches, UserTeams, UserDataScopes, AuditLogs, LoginHistories, CustomerPortalMemberships (đều FK, không cascade). `Customers` được tham chiếu bởi CustomerPortalMemberships; chưa có bảng hợp đồng/hóa đơn trong repo.

### User soft delete — FIXED
- `/users/delete/{id}`: trang xác nhận (vai trò, số doanh nghiệp đang truy cập, cảnh báo), POST có CSRF + stamp; `SystemAdminOnly` + `AUTH.USERS.DELETE`; actor kiểm tra lại từ DB.
- Chặn tự xóa, chặn xóa SYS_ADMIN hoạt động cuối cùng (kể cả khi đồng thời).
- `IsDeleted=1, IsActive=0, UpdatedAt/UpdatedBy`; `DeletedAt/DeletedBy` chỉ khi cột tồn tại (`COL_LENGTH` + `sp_executesql`). Không xóa vật lý, không cascade: vai trò, membership và lịch sử được giữ.
- Hệ quả: mọi truy vấn đăng nhập/cookie/membership/SYS_ADMIN loại `IsDeleted=1`; trang sửa/vai trò/xóa trả 404; `MembershipAdminStore.Save` từ chối user đã xóa. Username/email được giải phóng theo các unique index lọc `IsDeleted=0`.
- Chưa có khôi phục, purge hay retention (PENDING_APPROVAL).

### Customer soft delete — PENDING_APPROVAL
Chưa có quy tắc nghiệp vụ cho doanh nghiệp đang có membership/dữ liệu. Giữ cách "Ngừng hoạt động" (IsActive=0) đã có: chặn mọi truy cập Customer Portal tới doanh nghiệp mà không xóa gì. Không triển khai xóa mềm doanh nghiệp.

## 4. AUD-25 — Audit log preflight

| Câu hỏi | Trả lời |
|---|---|
| Định nghĩa | `database/migrations/006-create-audit-core.sql` (có `USE [TAPortal]` — **không chạy lại**) |
| Index/FK | PK, `IX_AuditLogs_User_CreatedAt`, `IX_AuditLogs_Entity`, `IX_AuditLogs_CorrelationId`; FK `UserId → Users` |
| Quyền ứng dụng | INSERT trên `dbo.AuditLogs` (không cần SELECT/UPDATE/DELETE) |
| Nội dung ghi | actor (`UserId`), `Action` (USER_ROLES_UPDATED / USER_UPDATED / USER_SOFT_DELETED), `EntityName`, target (`EntityId`), `OldValues`/`NewValues` (JSON), `IpAddress`, `CreatedAt` (default DB) |
| Lỗi khi ghi | Cùng transaction với thay đổi: INSERT lỗi → rollback toàn bộ |
| Thay đổi thành công mà không có audit? | **Có**, khi bảng không tồn tại **hoặc không nhìn thấy được** (principal không có quyền gì trên bảng → `OBJECT_ID` trả NULL) và `RequireDurableAudit=false`. Nhận định "fail-closed" của báo cáo Checkpoint 6 chỉ đúng khi principal nhìn thấy bảng nhưng thiếu INSERT (AUD-29) |
| Khắc phục | Thêm `Security__Audit__RequireDurableAudit` (mặc định `false` để không làm gãy chức năng). Khi `true`: không ghi được audit → hủy thay đổi (vai trò, sửa user, xóa mềm) và báo lỗi |

Script cho operator (chưa chạy):
1. `database/scripts/007-verify-audit-logs.sql` — chỉ đọc. Kiểm tra DB, bảng, cột/kiểu/độ dài, cột NOT NULL thiếu default, CreatedAt default, FK, trigger; quyền INSERT của principal ứng dụng qua `@AppPrincipal` + `EXECUTE AS USER … REVERT` (không impersonate được → NOT_VERIFIED). Kết thúc `VERIFY_STATUS=PASS|WARN|FAIL`.
2. `database/migrations/010-create-audit-logs-if-missing.sql` — chỉ khi 007 báo thiếu bảng; tạo đúng định nghĩa 006; không sửa bảng có sẵn.
3. `database/scripts/009-grant-admin-audit-insert-template.sql` — template, mọi lệnh GRANT đều đã comment.

## 5. Kiểm thử (SafeAdminAccountTests — 32 test, không SQL)
- Trạng thái (7): bảng chân trị điều kiện đăng nhập/nhãn; tập SYS_ADMIN dùng cùng điều kiện; policy chặn tự vô hiệu hóa/tự xóa/stamp cũ/actor không phải admin; trạng thái LOCKED không được kích hoạt lại; hai admin vô hiệu hóa/xóa lẫn nhau vẫn còn ≥1 admin.
- MST (14): chuẩn hóa (5), định dạng (6), dữ liệu cũ không chặn sửa (1), luồng HTTP trùng/sai định dạng/sửa chính mình/MST chi nhánh (1), 8 request tạo đồng thời (mô hình tuần tự) chỉ 1 thành công — kèm khẳng định chưa có unique index ở DB (1).
- HTTP user (11): trang sửa hiển thị trạng thái thực tế và bỏ qua trường overpost (Username/Status/PasswordHash/IsDeleted); stamp cũ/tự vô hiệu hóa/audit bắt buộc → 400, không ghi (3); xóa mềm giữ dòng, vai trò, membership và chặn sửa tiếp (404); tự xóa bị chặn và nút xác nhận disabled; OWNER có AUTH.USERS.DELETE vẫn 403 (2); thiếu CSRF 400.
- Hồi quy: 104 test trước đó (Customer Portal, CRUD, AUD-13/14, AUD-12) PASS. Smoke artifact 15/15.
- **NOT_VERIFIED:** khóa SQL thật; câu UPDATE xóa mềm và cột DeletedAt/DeletedBy; ghi AuditLogs; truy vấn SQL thật từ chối user đã xóa (bằng chứng từ mã nguồn); script 007–010.

## 6. UAT (gộp vào đợt UAT tổng thể)
1. Chạy `scripts/007` (đặt `@AppPrincipal`) và `scripts/008`; ghi lại `VERIFY_STATUS`.
2. Nếu 007 FAIL vì thiếu bảng → review/chạy `migrations/010`; cấp INSERT theo template 009; chạy lại 007 đến PASS → đặt `Security__Audit__RequireDurableAudit=true`.
3. Nếu 008 PASS và đồng ý ràng buộc ở DB → chạy `migrations/009`.
4. Sửa user test (2 tab → tab sau báo xung đột); vô hiệu hóa → phiên user test bị đăng xuất ở cả hai website.
5. Xóa mềm user test → không đăng nhập được Admin/Customer Portal; dòng vẫn còn trong DB với `IsDeleted=1`; membership/vai trò còn nguyên; `AuditLogs` có `USER_SOFT_DELETED`.
6. Thử tự xóa / xóa SYS_ADMIN cuối → bị chặn.
7. Tạo khách hàng trùng MST (có khoảng trắng) → bị chặn với mã KH trùng; MST chi nhánh `…-001` được chấp nhận.
