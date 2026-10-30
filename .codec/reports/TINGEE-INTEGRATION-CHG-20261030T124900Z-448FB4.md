# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261030T124900Z-448FB4

## A. Identification
Feature `TINGEE-INTEGRATION`; change `TINGEE-INTEGRATION-CHG-20261030T124900Z-448FB4`; branch feature/customer-portal-mvp01.

## B. Audit Summary
| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| TG-04 | Database | Script 014 | MEDIUM | Script dừng với Msg 468 | So sánh cột hệ thống khác collation | COLLATE DATABASE_DEFAULT | 014-diagnose-tingee-foundation.sql | Chờ operator chạy lại | FIXED |

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
NOT_APPLICABLE.

## E. Changed Files
`database/scripts/014-diagnose-tingee-foundation.sql`.

## F. Database
Operator chạy 014 gặp `Msg 468` ở lần so sánh `sys.database_permissions.permission_name` (collation `Latin1_General_CI_AS_KS_WS`) với cột của biến bảng (`SQL_Latin1_General_CP1_CI_AS`). Đã thêm `COLLATE DATABASE_DEFAULT` và dòng in user đang chạy. Script chỉ đọc, NOT_EXECUTED sau khi sửa; agent không chạy được nên chờ operator xác nhận.

## G. Build & Tests
dotnet restore: OK; Release build: 0 error; safe tests: 308 passed; Codec tests: OK; codec validate: PASS; git diff --check: sạch. Script SQL chưa được chạy lại.

## H. Admin Publish
NOT_RUN: không đổi source ứng dụng.

## I. Customer Publish
NOT_RUN: không đổi source ứng dụng.

## J. Security and Acceptance
Không có secret hoặc PII.

## K. Git Delivery
Source `a04dab999b24c3779607a0296eb946ff4e5d0deb`; base `f24133867acc447c2f685347929485308e3926b9`; merge PENDING_APPROVAL.

## L. Operator Handoff
Chạy lại script 014 và gửi phần Messages.
