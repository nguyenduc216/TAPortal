# CODEC DELIVERY REPORT — TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA

## A. Identification
Feature `TINGEE-INTEGRATION`; change `TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA`; branch feature/customer-portal-mvp01.

## B. Audit Summary
| ID | Project | Module | Severity | Problem | Root Cause | Fix | Files | Test | Status |
|---|---|---|---|---|---|---|---|---|---|
| TG-02 | Customer/Database | Khách hàng, đơn hàng | HIGH | Bảng CustomerContacts có sẵn thiếu cột, trang khách hàng và đơn hàng không chạy được | Migration 011 bỏ qua khi bảng đã tồn tại | Bảng CustomerBuyers, migration 012 | PaymentStore.cs, 012-tingee-customer-buyers.sql | Script 014 đã phát hiện; chờ chạy lại sau migration | PENDING_OPERATOR |
| TG-03 | Database | Rollback 013 | MEDIUM | Có thể xóa bảng không thuộc Tingee | Rollback gồm CustomerContacts | Đổi sang CustomerBuyers | 013-rollback-tingee-foundation.sql | Chưa chạy (không được chạy rollback) | FIXED |
| TG-05 | Database | Quyền | MEDIUM | TA_User là db_owner | Cấu hình hiện có | Operator quyết định trước production | — | Script 014 | PENDING_OPERATOR |

## C. Customer Portal Account Management
NOT_APPLICABLE.

## D. Admin CRUD
NOT_APPLICABLE.

## E. Changed Files
Xem `.codec/changes/TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA.md`.

## F. Database
- Script 014 do operator chạy xác nhận: `dbo.CustomerContacts` tạo ngày 2026-10-19, có 10 dòng, thiếu 10 cột so với code và thiếu `UX_CC_Customer_Code`. Trong repo không có code nào khác dùng bảng này; nơi dùng ngoài repo: NOT_VERIFIED.
- ADD `database/migrations/012-tingee-customer-buyers.sql` (NOT_EXECUTED): tạo `dbo.CustomerBuyers`, bỏ `FK_CO_Contact`, thêm `FK_CO_Buyer`. Không đổi `dbo.CustomerContacts` và dữ liệu của nó. Guard `DB_NAME()`, transaction, chạy lại được; hủy nếu đơn hàng đã có `ContactId` khi FK cũ còn.
- Thứ tự: (1) backup; (2) migration 012; (3) `012-verify-tingee-foundation.sql`; (4) `014-diagnose-tingee-foundation.sql`.
- `013-rollback-tingee-foundation.sql` không còn đụng tới `dbo.CustomerContacts`.
- `TA_User` là db_owner nên cấp quyền không giới hạn được user này (WARN, cần operator quyết định trước production).

## G. Build & Tests
dotnet restore: OK; Release build: 0 warning / 0 error; safe tests: 308 passed / 0 failed / 0 skipped; Codec tests: OK; codec validate: PASS; git diff --check: sạch. Safe tests dùng store giả nên không kiểm chứng câu SQL; dùng script 012 và 014 sau khi chạy migration.

## H. Admin Publish
NOT_RUN: Admin không đổi source.

## I. Customer Publish
TACustomerPortal.Web: `src\publish\deployed-TACustomerPortal.Web`, exit 0, 2026-10-30T13:14:40Z→2026-10-30T13:14:43Z, 58 file, SHA256 `3c4095209311e454bdb1fcaa596ce3e248217a2a967d0ce1b94bb6b0030559b7` (trước: `336a9fb0726c1e4040f78a3596d34e6ecccc4dc856bd1fb3dce39ecd64e1fcb1`); manifest `.codec/reports/TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA-TACustomerPortal.Web.sha256`; rollback `src\publish\rollback\TINGEE-INTEGRATION-CHG-20261030T131324Z-F23ADA\TACustomerPortal.Web`. PUBLISHED / NOT_DEPLOYED.

## J. Security and Acceptance
Không có secret hoặc PII; script chỉ in tên và số lượng. Không tuyên bố production-ready.

## K. Git Delivery
Source `5db5161254907991e697eca76e3b46133edb5e33`; base `c78fe5c73cf90359130d27b9d9976b8661420460`; merge PENDING_APPROVAL.

## L. Operator Handoff
Backup, chạy migration 012, chạy script 012 và 014, gửi kết quả. Cho biết hệ thống nào dùng `dbo.CustomerContacts` nếu biết.
