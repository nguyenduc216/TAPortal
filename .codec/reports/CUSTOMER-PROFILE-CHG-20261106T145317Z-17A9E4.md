# CODEC DELIVERY REPORT — CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4

Feature ID: `CUSTOMER-PROFILE`
Change ID: `CUSTOMER-PROFILE-CHG-20261106T145317Z-17A9E4`

## A. Identification
- Agent: Claude Code. Operator báo lỗi xem mật khẩu (ảnh chụp 2026-11-06).
- Branch `feature/customer-portal-mvp01`, HEAD = origin = `0037f2a` lúc chạy migration. Thay đổi chưa commit của người khác: `tests/playwright/` (không đụng).

## B. Audit Summary
| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| AL-01 | Database | AuditLogs | HIGH | Admin không ghi được dòng audit nào; xem mật khẩu khách hàng luôn lỗi | `dbo.AuditLogs` production là schema cũ (ActionCode/EntityType), thiếu `Action`/`EntityName` mà `AuditSql.Insert` ghi | Migration 025 thêm 2 cột nullable | 025, 039, 040 | Verify 039 PASS sau khi chạy thật | FIXED |
| AL-02 | TAPortal.Web | Cấu hình | MEDIUM | `RequireDurableAudit=false` nên thay đổi quản trị vẫn lưu dù audit lỗi (vì vậy lỗi AL-01 không ai thấy) | Cấu hình chờ verify | Operator quyết định bật sau khi chạy 007 | — | — | PENDING_OPERATOR |

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
Không đổi giao diện. Sau migration, Hiện/Sao chép mật khẩu ghi được `CUSTOMER_SECRET_REVEALED`; mọi audit khác của Admin (tạo/sửa khách hàng, người dùng, nhập Excel) cũng bắt đầu được lưu.

## E. Changed Files
Xem change record.

## F. Database
EXECUTED 2026-11-06T14:55:58Z: 025 (verify 039, rollback 040) - precheck NOT_APPLIED, rolled-back dry run PASS/NOT_APPLIED/trancount 0, latest full backup 1419 min old (< 24 h, no new backup), gates 481/481 Safe tests PASS, real run VERIFY_STATUS=PASS.

## G. Build & Tests
Restore/build Release/Safe tests 481/481 PASS (gate của script migration).

## H. Admin Publish
NOT_APPLICABLE (không đổi code).

## I. Customer Publish
NOT_APPLICABLE.

## J. Security and Acceptance
- Không thêm quyền, không đổi dữ liệu cũ; chỉ đọc schema/quyền production qua `Invoke-TAReadQuery.ps1`; không ghi dữ liệu khách hàng vào báo cáo.
- Nút Hiện chưa được thử lại bằng tài khoản thật: NOT_VERIFIED.

## K. Git Delivery
Trong hand-off.

## L. Operator Handoff
1. Tải lại trang Tài khoản hệ thống, bấm Hiện/Sao chép.
2. Tùy chọn: bật `Security__Audit__RequireDurableAudit=true` theo runbook audit.
