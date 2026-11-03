# Change CODEC-WORKFLOW-CHG-20261103T135116Z-54E75E

- Feature ID: `CODEC-WORKFLOW`
- Change ID: `CODEC-WORKFLOW-CHG-20261103T135116Z-54E75E`
- Created UTC: 2026-11-03T13:51:16.504009+00:00
- Tiêu đề: Agent deploy of portal and invoice to production IIS over SSH
- Trạng thái: IMPLEMENTED / DRY_RUN_VERIFIED / NOT_DEPLOYED

## Yêu cầu và phạm vi
Operator (2026-11-03) yêu cầu agent tự deploy được 2 site production `portal` (TA.Portal) và `invoice` (TA.Invoice, Customer Portal) qua SSH. Operator đã chạy bộ cài trên server WIN-HSHL54Q184K; SSH `ta-server` (113.160.249.61:22022, key, host key đã đối chiếu) hoạt động. Change này chỉ thêm policy + script; KHÔNG deploy.

## File thay đổi (relative path, thao tác ADD/MODIFY/DELETE)
- MODIFY `AGENTS.md`, `.agents/skills/codec/SKILL.md`, `.codec/README.md` — thêm mục `## AGENT PRODUCTION DEPLOY`; migration chạy qua SSH trên server.
- MODIFY `.codec/config.json` — thêm `deploy.remote`.
- MODIFY `.codec/tools/codec.py`, `.codec/tools/test_codec.py` — validator cho `deploy.remote`.
- ADD `deploy/windows/Deploy-TARemote.ps1` — script phía máy dev: kiểm tra git, kiểm tra server (chỉ đọc), gates, publish, manifest SHA256, zip, upload, gọi Apply, `-DryRun`, `-Rollback`.
- ADD `deploy/windows/server/` — bộ cài phía server (`Setup-TADeployServer.bat/.ps1`, `Apply-TARelease.ps1`, `ta_deploy.pub`), bản đã chạy trên server (chỉ sửa kiểm tra ANCM).

## Migration SQL (KHÔNG tự chạy)
- Database: TAPortalSys
- Script / absolute path:
- Thứ tự chạy:
- Preflight / Verify / Rollback:
- Execution: NOT_APPLICABLE

## Test và kết quả
- `Deploy-TARemote.ps1 -Target portal,invoice -DryRun` (source 2af37cb + thay đổi chưa commit): server preflight OK (2 target đúng site/pool/thư mục, ổ H còn 142.8 GB); build Release 0 lỗi; 308/308 Safe tests; publish TAPortal.Web 64 file (artifact 3022cc4a…74b3), TACustomerPortal.Web 57 file (artifact 9a2e6b60…178a); zip dùng '/' và giải nén thử đúng. Không upload.
- Codec unittest OK; `codec.py validate` chỉ còn lỗi publish path do checkout ở `D:\Working\...` (có sẵn).
- Chưa kiểm chứng: `Apply-TARelease.ps1` chưa chạy thật trên server (lần deploy đầu sẽ là lần đầu).

## Publish
- Project:
- Destination (chỉ từ config):
- Trạng thái: DRY_RUN_ONLY / NOT_DEPLOYED
- Artifact SHA256:
- Smoke test:

## Git
- Branch:
- Commit SHA:
- PR:
- Merge: PENDING_APPROVAL
- Merge SHA:

## Rủi ro, quyết định và việc còn lại
- Mỗi lần deploy cần operator yêu cầu trong hội thoại; chỉ 2 target portal/invoice; webhooks chưa đăng ký.
- Script ưu tiên ssh của Git for Windows vì ssh.exe của Windows từ chối `~/.ssh/config` có ACL lạ.
- Health check chỉ là HTTP 200 trang chủ; Portal/Customer chưa có endpoint version để so commit.
