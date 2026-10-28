---
name: codec
description: Workflow quản lý phát triển TAPortal với Feature ID, Change ID, migration thủ công, publish đúng đích, Git review/merge và lịch sử thay đổi.
---

# Codec — quy trình bắt buộc

## 0. Trước khi làm
- Đọc `AGENTS.md`, `.codec/config.json`, feature README và change history gần nhất của feature.
- Xác định FEATURE_ID (slug IN HOA, ví dụ `CUSTOMER-MANAGEMENT`, `USER-MANAGEMENT`, `TINGEE-REGISTRATION`).
- Tạo CHANGE_ID bằng `python .codec/tools/codec.py new --feature FEATURE_ID --title "..."` trước khi sửa.
- Tóm tắt yêu cầu, phạm vi, dependency, rủi ro, rollback, tiêu chí chấp nhận. Không làm vượt phạm vi.

## 1. Source và Git
- Việc nhỏ, thuộc feature đang phát triển: dùng branch hiện tại theo chỉ định. Chức năng mới/độc lập hoặc thay đổi lớn: đề xuất branch `feature/<feature-slug>` và chỉ tạo khi cần.
- Không trực tiếp push vào main trừ khi có chỉ định rõ. Không tự merge khi chưa có xác nhận hoàn thành/review từ operator.
- Khi đã duyệt merge: kiểm tra CI, working tree sạch, target `main`, tránh force push; merge theo policy repo. Ghi merge commit SHA. Nếu chưa duyệt, báo `PENDING_APPROVAL`.

## 2. Database — tuyệt đối không tự chạy
- DB duy nhất theo cấu hình `.codec/config.json`: `TAPortalSys` (đang là môi trường phát triển nhưng website đang sử dụng).
- KHÔNG tạo DB mới, KHÔNG chạy migration/DDL/DML sửa dữ liệu, kể cả qua ORM, CLI, DBeaver, MCP, test setup hoặc PowerShell trên DB này.
- Chỉ tạo file migration SQL trong `database/migrations/` với tên `NNN-<feature>-<change-id>.sql` (nếu dự án dùng numbering khác thì theo convention hiện hành, tránh trùng).
- Migration phải có guard `DB_NAME()` chính xác, kiểm tra tiền điều kiện, idempotency nếu phù hợp, transaction/XACT_ABORT nếu an toàn, mô tả rollback và các tác động dữ liệu.
- Báo đường dẫn relative + absolute (nếu biết root), tên DB, thứ tự migration, script verify và rollback, trạng thái `NOT_EXECUTED` cho từng file.
- Chỉ người dùng/DBA thực thi migration; Codex không tự thực thi dù người dùng yêu cầu publish sau build. Nếu muốn đổi quy tắc này phải sửa policy có phê duyệt rõ.
- Test DB integration: chỉ được dùng DB test cô lập đã được người dùng cho phép; không tự tạo database mới trên server hiện tại.

## Publish convention ? Checkpoint 3 (2026-10-28)
- Every web project publishes only to `<WORKSPACE_ROOT>\publish\deployed-<PROJECT_NAME>`; PROJECT_NAME is the .csproj filename without extension. Paths are fixed, never timestamped, renamed or moved; never publish into source.
- Creating exactly that conventional folder is authorized. Before any write, inspect existing files, write permissions and all IIS site/application/virtual-directory physical paths (including parent/child overlap and path aliases). An unreadable IIS configuration is an unresolved safety preflight: STOP publish until the operator supplies verified mappings or access. Do not elevate automatically.
- No automatic publish to a live IIS directory. Stop for separate approval before overwriting any served folder; never clean it blindly. Back up existing artifacts/configuration before approved replacement.
- Publishing artifacts does not authorize IIS site/binding/app-pool changes, starts or recycles. Report PUBLISHED separately from NOT_DEPLOYED, with source commit SHA and artifact SHA256.
- This checkpoint authorizes only TACustomerPortal.Web. TAPortal.Web follows the same path convention for future authorized work, but is not published now.

## 4. Sau mỗi change
- Build/test phù hợp, ghi kết quả trung thực (không chạy thì ghi NOT_RUN).
- Ghi mọi file thay đổi, SQL scripts, manual action, commit SHA, PR URL, deploy path, artifact hash, merge SHA vào `.codec/changes/<CHANGE_ID>.md`.
- Cập nhật `.codec/features/<FEATURE_ID>/README.md` và `history.json`.
- Tạo report `.codec/reports/<CHANGE_ID>.md` theo template.
- Ghi những gì chưa hoàn thành và các phê duyệt còn thiếu; không tuyên bố merge/publish khi chưa thực hiện.

## 5. Quy tắc bảo mật
- Không lưu secret, password, PII vào Git/history.
- Không lấy dữ liệu production cho test mà không có sự cho phép rõ.
- Hướng dẫn trong skill không thay thế phân quyền OS/SQL/IIS. Khi thiếu quyền thì báo, không tự nâng quyền.

## Installation policy additions (CODEC-WORKFLOW)
- Intake order: identify FEATURE_ID; read AGENTS.md, Codec skill and config; read feature README.md AND history.json; read recent related changes/reports; only then inspect required source. If history is missing, initialize it and record facts verified from source. Do not scan all chat/repository without need.
- CHANGE_ID format: FEATURE-ID-CHG-YYYYMMDDTHHMMSSZ-XXXXXX (UTC, unique). Preserve previous changes/reports/history entries; append new records, update only the current change record.
- SQL Server: `WIN-HSHL54Q184K\MSSQLSERVER2014`; ONLY database: `TAPortalSys`. No DEV/UAT/TEST or other databases, including integration-test databases. No automatic migration, DDL, DML, seed or rollback via any channel. Only author SQL files; operator runs them manually with DBeaver or SSMS.
- Before authoring migrations, check existing scripts to avoid duplicates. Every mutating migration must check `DB_NAME() = 'TAPortalSys'` and abort on mismatch. Document exact path, execution order, purpose, preconditions and verification instructions.
- Publish targets use the fixed convention below, explicitly authorized in Checkpoint 3. Separate approval remains required for live IIS overwrite.
- Reuse the current feature branch; create a branch only for independent new features or substantial work. Never reset/delete uncommitted work, force push, or merge main without user acceptance and approval. Record merge SHA after approved merge.
- Record objective, scope, technical decisions, changed files, migrations, build/test, publish, source commit SHA, merge SHA and remaining work in feature history/change/report. Never store secrets or PII.
