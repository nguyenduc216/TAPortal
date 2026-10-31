# CODEC DELIVERY REPORT — CODEC-WORKFLOW-CHG-20261031T205904Z-79AB8C

## A. Identification
Feature `CODEC-WORKFLOW`; change `CODEC-WORKFLOW-CHG-20261031T205904Z-79AB8C`; branch feature/customer-portal-mvp01.

## B. Audit Summary
Không có phát hiện mới về code. Đã kiểm tra file của operator trước khi commit; một ảnh chân dung không được commit.

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
NOT_APPLICABLE.

## E. Changed Files
Xem `.codec/changes/CODEC-WORKFLOW-CHG-20261031T205904Z-79AB8C.md`.

## F. Database
NOT_RUN: `004-verify-customer-portal.sql` là script chỉ đọc của operator, agent không chạy.

## G. Build & Tests
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec tests: OK; codec validate: PASS; git diff --check: sạch.

## H. Admin Publish
NOT_RUN: không đổi source ứng dụng.

## I. Customer Publish
NOT_RUN: không đổi source ứng dụng.

## J. Security and Acceptance
Không có secret hoặc PII trong commit.

## K. Git Delivery
Source `c0be494f4c00807b911b2d3c3ee6fc60b7d8d8c0`; base `b7837f007528b32e0323efbd5e9e92671449d802`; merge PENDING_APPROVAL.

## L. Operator Handoff
## Bàn giao sang máy khác
- Tiếp tục trên nhánh `feature/customer-portal-mvp01`; PR #7 chưa merge.
- Không có trên Git: `src/publish/`, `publish/`, `bin/`, `obj/`, `TestResults/`, `Codec-TAPortal-Workflow-v1.zip`, và `database/scripts/scene-image.png` (ảnh chân dung không liên quan dự án, giữ lại máy cũ). Artifact cần publish lại ở máy mới nếu cần; đường dẫn `.codec/config.json` cố định theo `D:\T.A\TAPortal\TAPortal`, nên đặt repo đúng đường dẫn này hoặc đổi config có phê duyệt.
- Khóa Tingee và connection string không nằm trong Git, phải cấu hình lại ở môi trường chạy.
- Database: migration 011 và 012 đã được operator xác nhận; verify PASS; cảnh báo còn lại là TA_User thuộc db_owner.
- Việc còn lại: test Tingee thật bằng `Tingee__Mode=Direct` (NOT_VERIFIED); deploy (NOT_DEPLOYED); worker xử lý webhook và đối soát; phân quyền linh hoạt; Zalo; hóa đơn điện tử; Master Merchant khi được kích hoạt.
