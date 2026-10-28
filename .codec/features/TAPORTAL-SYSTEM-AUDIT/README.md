# Feature: TAPORTAL-SYSTEM-AUDIT

- Feature ID: `TAPORTAL-SYSTEM-AUDIT`
- Tên: Full system audit, Admin Users/Customers edit, Customer Portal account UI, dual publish
- Mục tiêu: audit Admin + Customer Portal + Shared; sửa an toàn các lỗi CRUD/UI/navigation; thống nhất operating contract cho mọi AI agent; publish hai artifact cục bộ.
- Phạm vi / ngoài phạm vi: source Admin (Users, Customers, CustomerPortalAccounts, layout, JS), Codec policy/validator/tests, safe tests. Ngoài phạm vi: migration/DDL/DML, ghi database, IIS/server, merge main, đổi vai trò người dùng (chờ quy tắc nghiệp vụ).
- Kiến trúc / bảng liên quan: dbo.Users, dbo.Customers, dbo.UserRoles/Roles (chỉ đọc), dbo.CustomerPortalMemberships (qua MembershipAdminStore sẵn có). Schema hiện có đáp ứng; không cần migration mới.
- Các phụ thuộc: `CUSTOMER-PORTAL-FOUNDATION` (migration 008 do operator báo đã áp dụng; PR #7; UAT-01…09 vẫn PENDING_OPERATOR).
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T163541Z-822BA2` | 2026-10-28 | Full system audit, Admin Users/Customers edit, Customer Portal account UI, dual publish | 336ab7c80552e733a5df9e6c4612aeb163037007 | NONE_REQUIRED / NOT_EXECUTED | PUBLISHED (local, both) / NOT_DEPLOYED | PENDING_APPROVAL |

## Current status
19 findings (0 CRITICAL, 2 HIGH, 8 MEDIUM, 9 LOW); 12 FIXED, 7 OPEN. Release build 0/0; 65 safe tests + 4 Codec tests PASS; validate PASS. Admin artifact `4b308e3b…ea35` (57 files), Customer artifact `ea77b3be…ac34` (54 files), both local only. SQL write paths and authenticated UI rendering await operator UAT. Details: latest report and docs/audits/.
