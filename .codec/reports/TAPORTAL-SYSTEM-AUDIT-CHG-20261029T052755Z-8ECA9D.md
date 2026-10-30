# TAPORTAL CHECKPOINT 10 — FINAL RELEASE CANDIDATE REPORT

Feature ID: `TAPORTAL-SYSTEM-AUDIT` · Change ID: `TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D` · Agent: Claude Code (claude-opus-5-5)

## A. Identification
- FEATURE_ID TAPORTAL-SYSTEM-AUDIT; CHANGE_ID TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D; branch feature/customer-portal-mvp01
- Starting SHA 1ebbb016bc5891b61b5bdce4024ebfa470e888d1 (bằng origin; không có commit agent khác)
- Source SHA (release candidate) 95e64cedb14479850bc3f32d28ec0aa6c36f7d02
- Delivery SHA: commit chứa báo cáo này — báo sau push

## B. Executive summary
Audit hệ thống đóng ở mức code: 42 finding, 0 CRITICAL, 2 HIGH đã sửa. 21 CLOSED, 15 FIXED_IN_CODE (chờ DB/UAT), 4 PENDING_OPERATOR, 2 PENDING_APPROVAL. Cặp release candidate đã tạo và xác minh. Triển khai: CONDITIONAL GO; production: NOT READY.

## C. Findings matrix
Xem `docs/audits/taportal-system-audit-final-TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D.md` mục 1 (đủ AUD-01…42 + migration 008 + UAT foundation). Mới ở CP10: AUD-39 security headers (CLOSED), AUD-40 guard script 003 (CLOSED), AUD-41 scripts/001–002 trỏ DB `TAPortal` (PENDING_OPERATOR), AUD-42 audit đổi mật khẩu Customer Portal (PENDING_APPROVAL).

## D. Functional regression
Admin và Customer Portal: toàn bộ chức năng trong yêu cầu có safe test tương ứng (mục 2 báo cáo tổng kết). Render các trang đọc trực tiếp từ SQL và mọi câu SQL ghi: DATABASE_NOT_VERIFIED.

## E. Security regression
Enumeration/dummy PBKDF2, throttle/proxy/giới hạn multi-instance, xác thực lại cookie, authorization + kiểm kê endpoint (chỉ 4 endpoint ẩn danh mỗi app), quyền trực tiếp chỉ đọc, tenant isolation, CSRF, output encoding, SQL tham số hóa, log không nhạy cảm, audit fail-closed, thu hồi, xóa mềm, concurrency, xử lý lỗi, security headers, no-store: PASS ở tầng ứng dụng.

## F. Database readiness
| Script | Loại | Guard | Trạng thái |
|---|---|---|---|
| migrations/001–007, scripts/001–002 | Legacy, `USE [TAPortal]` | Không | KHÔNG CHẠY (AUD-41) |
| scripts/003 schema audit | Chỉ đọc | Có (CP10) | NOT_EXECUTED |
| migrations/008 memberships | DDL + seed permission | Có | OPERATOR_REPORTED (CP3), NOT_VERIFIED — không chạy lại nếu 004 PASS |
| scripts/004 verify membership | Chỉ đọc, VERIFY_STATUS | Có | NOT_EXECUTED (bản làm việc đang được operator sửa) |
| scripts/005 rollback membership | DDL có điều kiện | Có | Chỉ khi phê duyệt; từ chối nếu có dữ liệu |
| scripts/006 least privilege Customer | Template (đã comment) | — | DBA áp dụng |
| scripts/007 verify AuditLogs | Chỉ đọc, VERIFY_STATUS | Có | NOT_EXECUTED |
| scripts/008 audit MST | Chỉ đọc, VERIFY_STATUS | Có | NOT_EXECUTED |
| migrations/009 unique MST | DDL, tự hủy nếu trùng | Có | NOT_EXECUTED (tùy chọn) |
| scripts/010 rollback 009 | DDL | Có | Chỉ khi phê duyệt |
| migrations/010 AuditLogs | DDL có điều kiện | Có | NOT_EXECUTED (chỉ khi 007 xác nhận thiếu) |
| scripts/009 grant INSERT AuditLogs | Template | Có | DBA áp dụng |
Tất cả cú pháp tương thích SQL Server 2014 (THROW, TRY/CATCH, filtered index, HAS_PERMS_BY_NAME; không STRING_AGG/CONCAT_WS). Ứng dụng không phụ thuộc migration 009/010 để khởi động.

## G. Infrastructure readiness
Proxy: chưa biết topology (PENDING_INFRASTRUCTURE_APPROVAL). IIS: IIS_NOT_VERIFIED, không kiểm tra theo chính sách. HTTPS: bắt buộc (cookie Secure, HSTS ngoài Development). Secrets: không có trong Git/artifact; phải cấp qua biến môi trường/secret store. Data Protection: bắt buộc thư mục key bền vững riêng từng app.

## H. Configuration matrix
Xem `docs/operations/final-deployment-uat-runbook.md` mục "Cấu hình": ASPNETCORE_ENVIRONMENT, Database__ConnectionString, DataProtection__KeyPath, AllowedHosts, Security__Audit__RequireDurableAudit, Security__AdminLogin__*, ReverseProxy__KnownProxies__N, ASPNETCORE_HTTPS_PORT, logging — tất cả PENDING_OPERATOR.

## I. Build/tests
Release build PASS 0/0; 215 safe tests PASS / 0 FAIL / 0 SKIP (13 mới); Codec 4/4, validate PASS; smoke Admin 16/16, Customer 10/10. Không có SQL integration test (cấm theo policy); không trình duyệt/proxy/IIS/timing thật.

## J. Release artifacts
| Project | Path | Source SHA | Files | Artifact SHA256 |
|---|---|---|---|---|
| TAPortal.Web | D:\T.A\TAPortal\TAPortal\publish\deployed-TAPortal.Web | 95e64ced… | 57 | 6cfdc285c736386f8e1b6a94303286a07cc0bbf0c912d685aa4db122e5edfe69 |
| TACustomerPortal.Web | D:\T.A\TAPortal\TAPortal\publish\deployed-TACustomerPortal.Web | 95e64ced… | 54 | cb152b0567270cb4fe64d8b2aa01dd20007fac1c30ec4b7c7b4403c35cfe3efc |
Shared DLL dca4b1ade8e096015bfab1f2970250a660ecbea8fce2acfa57d1633326384a65 ở cả hai. Manifest: .codec/reports/TAPORTAL-SYSTEM-AUDIT-CHG-20261029T052755Z-8ECA9D-<Project>.sha256.

## K. Git
Source commit 95e64cedb14479850bc3f32d28ec0aa6c36f7d02; delivery commit/push/remote HEAD trong thông báo bàn giao; PR #7 chưa merge (NOT_MERGED).

## L. Deployment/UAT runbook
`docs/operations/final-deployment-uat-runbook.md` (Phase A 13 bước, cấu hình, Phase B 10 bước, Phase C 20 tình huống + UAT-01…09, Phase D rollback) và `docs/operations/durable-audit-enablement-runbook.md`.

## M. Outstanding risks
| Rủi ro | Sev | Owner | Hành động | Chặn triển khai? |
|---|---|---|---|---|
| DB chưa xác minh (008, AuditLogs, quyền) | HIGH (vận hành) | DBA | Phase A3–A6 | Có |
| Secrets/Data Protection/HTTPS/AllowedHosts chưa cấu hình | HIGH | Operator | Phase A9–A11 | Có |
| Audit bền vững chưa bật (AUD-29) | MEDIUM | Operator | Runbook audit, bật cờ | Không (khuyến nghị bật trước go-live) |
| Throttle theo process / proxy (AUD-24) | MEDIUM | Hạ tầng | Gateway/WAF, KnownProxies | Không (nếu một instance, không proxy) |
| MST chưa unique ở DB (AUD-16) | LOW | DBA | 008 → migration 009 | Không |
| MCP reader trỏ DB cũ (AUD-41) | MEDIUM (vận hành) | DBA | Điều chỉnh scripts/001–002 cho TAPortalSys | Không |
| UAT chưa chạy | HIGH | Operator | Phase C | Chặn production |
| Các phê duyệt nghiệp vụ (AUD-12a/17 doanh nghiệp/15 Status/26 sửa/42) | LOW | Product owner | Quyết định | Không |

## N. Final recommendation
**CONDITIONAL GO** cho triển khai; code release candidate **READY**; production **NOT READY**.
Lý do: mọi gate code (build, 215 test, Codec, smoke, cặp artifact cùng SHA, không CRITICAL/HIGH mở) đạt; nhưng database chưa được xác minh trên TAPortalSys, cấu hình bảo mật môi trường chưa đặt, và UAT chưa chạy — nên không thể GO vô điều kiện.
Hành động tiếp theo của operator: (1) Phase A runbook (backup, 004/007/008, quyền SQL, cấu hình, HTTPS, Data Protection, proxy); (2) Phase B triển khai cặp artifact sau khi xác minh SHA256; (3) bật audit bền vững khi 007 PASS; (4) Phase C UAT; (5) quyết định merge PR #7 sau nghiệm thu.
