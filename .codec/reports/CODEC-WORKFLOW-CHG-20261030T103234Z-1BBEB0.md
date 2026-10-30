# CODEC DELIVERY REPORT — CODEC-WORKFLOW-CHG-20261030T103234Z-1BBEB0

## A. Identification
Feature `CODEC-WORKFLOW`; change `CODEC-WORKFLOW-CHG-20261030T103234Z-1BBEB0`; agent Claude Code (claude-opus-5-5); branch feature/customer-portal-mvp01.

## B. Audit Summary
Validator cố định đường dẫn publish cũ; đã đổi cùng config và tài liệu. Quy tắc text trước đây chỉ áp dụng cho SQL kiểm tra, nay áp dụng cho mọi SQL chỉ đọc và được kiểm tra trong `codec validate`.

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
NOT_APPLICABLE.

## E. Changed Files
Xem `.codec/changes/CODEC-WORKFLOW-CHG-20261030T103234Z-1BBEB0.md`.

## F. Database
NOT_APPLICABLE: không có SQL trong change này.

## G. Build & Tests
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec unit tests: OK; codec validate: PASS; git diff --check: sạch.

## H. Admin Publish
NOT_RUN trong change này: xem `TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE`.

## I. Customer Publish
NOT_RUN trong change này: xem `TINGEE-INTEGRATION-CHG-20261030T103236Z-05E4FE`.

## J. Security and Acceptance
Không có secret hoặc PII. Chờ operator nghiệm thu.

## K. Git Delivery
Source `53dbe68cf9907ff3931fd593203435c15a180029`; base `205febb3dde0933dc80c6df9fab6f8dd172f81f3`; merge PENDING_APPROVAL.

## L. Operator Handoff
Xác nhận tên thư mục `deployed-<PROJECT_NAME>` trong `src\publish`. Thư mục `publish\` cũ giữ nguyên chờ operator quyết định.
