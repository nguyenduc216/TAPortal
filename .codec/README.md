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
