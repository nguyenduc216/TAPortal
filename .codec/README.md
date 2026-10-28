# Codec — TAPortal Codex Workflow Kit

Bộ quy tắc và công cụ cho Codex trong repository TAPortal. Không phải plugin ChatGPT đã cài đặt.

## Cài vào repository
Sao chép **nội dung** thư mục Codec vào root repository (gồm `.agents`, `.codec`, `AGENTS.md`).
Codex cần được hướng dẫn đọc `AGENTS.md` và `.agents/skills/codec/SKILL.md` khi bắt đầu tác vụ.

## Cấu hình trước khi sử dụng
Chỉnh `.codec/config.json`:
- `database.name`: mặc định `TAPortalSys` theo thông tin người dùng đã xác nhận.
- `deploy.targets`: PHẢI điền đường dẫn publish thực tế cho từng website; để trống nghĩa là KHÔNG publish.
- `git.main_branch`: `main`.
- `deploy.require_approval`: true; `git.merge_requires_approval`: true.

## Khởi tạo change
`python .codec/tools/codec.py new --feature CUSTOMER-MANAGEMENT --title "Bổ sung lọc khách hàng"`

## Xem trạng thái
`python .codec/tools/codec.py list`
`python .codec/tools/codec.py validate`

## Vòng đời
Intake -> Plan -> Implement -> Build/Test -> SQL handoff (KHÔNG execute) -> Publish to configured exact path (after approval) -> Verify -> Review -> Merge (after approval) -> Report.

Mỗi thay đổi có một `Change ID` duy nhất; một `Feature ID` có nhiều changes. History ghi bằng Markdown và JSON, commit vào Git. Không lưu mật khẩu, connection string có password hoặc dữ liệu cá nhân vào history.

**Lưu ý:** Các guard trong skill là hướng dẫn tác nghiệp, không phải sandbox bảo mật. Giới hạn quyền SQL/IIS của tài khoản chạy Codex vẫn cần được áp dụng.

## DEV/TEST MACHINE POLICY (2026-10-28)
- This machine is for development and testing, not a production deployment server.
- Codex must not inspect IIS, websites, application pools, bindings or physical paths; connect to or probe server configuration; request Administrator access; deploy or change IIS websites.
- Never run migrations, seed, rollback, mutating SQL or data-writing integration tests; never create databases or execute stored procedures with write effects.
- Only TAPortalSys read queries using read-only permissions are allowed. Without read access, record NOT_VERIFIED and continue independent work.
- Author migration SQL files for operator review and manual execution only. SQL verification defaults to TEXT messages via PRINT; SELECT details only when diagnosing failures.

## Local publish policy ? Checkpoint 3
- Publish every web project only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the csproj filename without extension. Never rename, move or timestamp the target, or publish into source.
- This is a local artifact directory, not an assumed IIS deployment. Do not inspect IIS before local publish; unreadable IIS configuration does not block local artifacts.
- Check project, Git, build/safe tests, target contents and write permission. Creating the exact conventional folder is authorized. Reject path aliases/reparse points escaping the target; never delete or overwrite data outside it.
- Local artifact publishing does not authorize server/IIS configuration or deployment. Report PUBLISHED separately from NOT_DEPLOYED, source commit SHA separately from artifact SHA256.
- Checkpoint 3 authorizes TACustomerPortal.Web only; TAPortal.Web is not published. Any future live deployment requires separate approval and operator-managed backup/replacement.
