# Change TAPORTAL-SYSTEM-AUDIT-CHG-20261103T143443Z-55C344

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261103T143443Z-55C344`
- Created UTC: 2026-11-03T14:34:43.539970+00:00
- Tiêu đề: Execute migration 009 on production, pin Pkcs 8.0.2, first agent deploy of portal and invoice
- Trạng thái: MIGRATION_EXECUTED / DEPLOY_PENDING

## Yêu cầu và phạm vi
Operator (2026-11-03) cho phép deploy thường trực portal + invoice với điều kiện: (1) backup bản đang chạy trước khi thay; (2) migration phải có rollback, chạy thử toàn bộ SQL trước rồi mới chạy thật. Change này mở đường cho lần deploy đầu: xử lý 2 lỗi audit (migration trong phạm vi chưa xác minh; gói Pkcs 8.0.1 lỗi High).

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `src/TAPortal.Shared/TAPortal.Shared.csproj` — thêm `System.Security.Cryptography.Pkcs` 8.0.2 (vá GHSA-846f-f764-76mx, High; trước là 8.0.1 kéo gián tiếp qua Microsoft.Data.SqlClient 7.0.2). Ảnh hưởng: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks.
- MODIFY `AGENTS.md`, `.agents/skills/codec/SKILL.md`, `.codec/README.md` — quyền deploy thường trực có điều kiện; tự tạo backup COPY_ONLY khi chưa có backup ≤ 24h.

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Kiểm tra trước (chỉ đọc, qua SSH `ta-server`, sqlcmd `.\MSSQLSERVER2014`): 008 PASS, 009 MISSING, 010 PASS, 011 PASS, 012 PASS; 0 nhóm TaxCode trùng; chưa từng có full backup (recovery FULL).
- Backup: `TAPortalSys-predeploy-20261103T143226Z.bak` (COPY_ONLY, CHECKSUM, 4.9 MB) trong thư mục Backup của instance; RESTORE VERIFYONLY: valid.
- Script: `database/migrations/009-customer-taxcode-unique-index.sql`; rollback `database/scripts/010-rollback-customer-taxcode-unique-index.sql`.
- Dry-run (một transaction bọc ngoài, `sqlcmd -b`, kiểm tra @@TRANCOUNT sau mỗi batch): migration OK → PASS index có → rollback OK → PASS index mất → ROLLBACK, @@TRANCOUNT=0; kiểm tra lại: 009 vẫn MISSING, 0 transaction mở.
- Gates trước khi chạy thật: build Release 0 lỗi, 308/308 Safe tests.
- Execution: EXECUTED 2026-11-03 ~14:33 UTC — `009-customer-taxcode-unique-index.sql: OK`; verify: 008–012 đều PASS.

## Test và kết quả
- `dotnet list package --vulnerable --include-transitive`: TAPortal.Web, TACustomerPortal.Web, TAPortal.Webhooks không còn gói lỗi (Pkcs 8.0.2).
- Build Release 0 lỗi / 0 cảnh báo; 308/308 Safe tests.

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: NOT_PUBLISHED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
