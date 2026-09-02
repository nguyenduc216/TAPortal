# TAPortal Database Skill

## Purpose
Hướng dẫn AI/Codex/ChatGPT làm việc an toàn, chính xác với SQL Server của TAPortal thông qua MCP read-only.

## Source of truth
- Database production là nguồn sự thật cho schema và dữ liệu hiện tại.
- MCP endpoint: `https://mcp.tuvangiaiphapta.vn/mcp`.
- Database: `TAPortal`.
- Common application tables hiện dùng schema `dbo`.
- Không suy đoán table/column/FK/index nếu chưa inspect thực tế.

## MCP tools
Ưu tiên sử dụng các tool MCP hiện có:
- `DbPing`
- `DbListSchemas`
- `DbListTables`
- `DbGetTableSchema`
- `DbGetForeignKeys`
- `DbGetIndexes`
- `DbGetConstraints`
- `DbGetViews`
- `DbGetStoredProcedures`
- `DbGetSchemaSnapshot`
- `DbQueryReadOnly`

## Mandatory workflow
Khi yêu cầu phụ thuộc schema hoặc dữ liệu thật:
1. Gọi `DbPing` nếu chưa xác nhận kết nối trong phiên.
2. Nếu chưa biết object liên quan, gọi `DbListTables` hoặc `DbGetSchemaSnapshot`.
3. Trước khi query bảng chưa quen, gọi `DbGetTableSchema`.
4. Trước JOIN phức tạp, gọi `DbGetForeignKeys`.
5. Khi phân tích hiệu năng, gọi `DbGetIndexes` và `DbGetConstraints`.
6. Chỉ sau khi hiểu schema mới gọi `DbQueryReadOnly`.
7. Nếu query lỗi vì table/column không tồn tại, inspect schema lại; tuyệt đối không đoán tên thay thế.
8. Khi đề xuất thay đổi database, tạo migration/script trong `database/migrations` và không chạy DDL trực tiếp qua MCP.

## Query safety
MCP database được coi là read-only.

Được phép:
- SELECT
- CTE
- JOIN
- GROUP BY
- aggregate/analytical queries

Không được phép qua MCP database:
- INSERT
- UPDATE
- DELETE
- MERGE
- EXEC/EXECUTE
- CREATE
- ALTER
- DROP
- TRUNCATE

Không đề xuất bypass tài khoản `taportal_ai_reader` hoặc cấp quyền ghi thô cho AI.

## Query discipline
- Dùng schema-qualified names, ví dụ `dbo.Users`.
- Tránh `SELECT *` nếu không thực sự cần.
- Khi khám phá dữ liệu lớn, dùng `TOP`.
- Chỉ lấy các cột cần thiết.
- Không truy vấn dữ liệu nhạy cảm nếu không cần cho yêu cầu.

## Common schema baseline
Baseline hiện tại gồm:

### Authentication / authorization
- `dbo.Users`
- `dbo.Roles`
- `dbo.Permissions`
- `dbo.UserRoles`
- `dbo.RolePermissions`
- `dbo.UserPermissions`

### Organization
- `dbo.Companies`
- `dbo.Branches`
- `dbo.Teams`
- `dbo.UserBranches`
- `dbo.UserTeams`

### System
- `dbo.Modules`
- `dbo.Functions`
- `dbo.Menus`
- `dbo.MenuPermissions`
- `dbo.Settings`
- `dbo.NumberSequences`

### Data scopes
- `dbo.DataScopes`
- `dbo.RoleDataScopes`
- `dbo.UserDataScopes`

Supported scope codes:
- `SELF`
- `ASSIGNED`
- `TEAM`
- `BRANCH`
- `COMPANY`
- `CUSTOM`

### Audit
- `dbo.AuditLogs`
- `dbo.LoginHistories`
- `dbo.SystemLogs`

Luôn verify live schema trước khi dựa vào baseline này.

## Change-management rules
1. Không DROP/TRUNCATE khi chưa có phê duyệt rõ ràng.
2. Không commit password/token/connection string thật.
3. DDL phải đi qua migration/script có thể review.
4. Sau migration phải verify schema thực tế.
5. Foreign key mặc định NO ACTION; CASCADE chỉ khi có lý do nghiệp vụ rõ ràng.
6. Thay đổi database phải ưu tiên backward-compatible khi có thể.

## Response contract
Khi trả lời câu hỏi database:
1. Nêu phần nào lấy từ database thực tế.
2. Phân biệt rõ hiện trạng và đề xuất.
3. Nếu đưa SQL, SQL phải phù hợp schema vừa inspect.
4. Nếu thiếu thông tin, nói rõ cần inspect thêm object nào thay vì đoán.
