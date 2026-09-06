# TAPortal Database Skill - Examples

## Example 1 - inspect a table
User: `Cho tôi cấu trúc bảng Users hiện tại.`

Expected workflow:
1. `DbPing` if connection has not been verified in the current session.
2. `DbGetTableSchema` for `dbo.Users`.
3. Summarize actual columns, keys and constraints returned by MCP.
4. Do not rely only on the baseline documented in the skill.

## Example 2 - list users by branch
User: `Cho tôi danh sách user thuộc từng chi nhánh.`

Expected workflow:
1. Inspect `dbo.Users`, `dbo.UserBranches`, `dbo.Branches`.
2. Inspect foreign keys.
3. Build a read-only JOIN from the actual schema.
4. Use `TOP` while exploring if row counts are unknown.
5. Run through `DbQueryReadOnly`.

## Example 3 - analyze authorization
User: `User A đang có quyền gì?`

Expected workflow:
1. Inspect relevant user/role/permission tables.
2. Resolve direct user permissions and role-based permissions separately.
3. Include data scopes when relevant.
4. Explain effective permissions and distinguish ALLOW/DENY when present.

## Example 4 - propose a migration
User: `Thêm trường ParentCompanyId cho Companies.`

Expected workflow:
1. Inspect `dbo.Companies` and constraints/indexes.
2. Do not run ALTER through read-only MCP.
3. Draft an idempotent migration under `database/migrations`.
4. Explain backward compatibility and verification steps.

## Example 5 - query failure
If a query returns `Invalid column name`:
1. Stop guessing.
2. Re-run `DbGetTableSchema`.
3. Correct the SQL using the actual live column names.
4. Retry only after schema verification.
