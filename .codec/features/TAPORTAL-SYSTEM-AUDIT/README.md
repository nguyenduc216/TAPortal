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
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T170619Z-0CBC7E` | 2026-10-28 | Checkpoint 5 Admin login rate limiting (AUD-13) and authenticated no-store (AUD-14) | 183414102691ffb373d436951b27cf191d902fc2 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 5 — current status
AUD-13 and AUD-14 FIXED; new findings AUD-20/21/22 FIXED, AUD-23/24 OPEN. Release build 0/0; 84 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 11/11. Admin artifact `42d6a733…19cb4` (57 files); Customer NOT_REQUIRED (unchanged `ea77b3be…ac34`). Database not accessed. Prior status lines above are historical.
| `TAPORTAL-SYSTEM-AUDIT-CHG-20261028T172500Z-CE30E8` | 2026-10-28 | Checkpoint 6 user role management (AUD-12) | 36db1038ebe071ebab4f588538e118d79fb416e9 | NONE_REQUIRED | PUBLISHED (local, Admin only) / NOT_DEPLOYED | PENDING_APPROVAL |

## Checkpoint 6 — current status
AUD-12 FIXED for role assignment (SYS_ADMIN-only, last SYS_ADMIN protected, stale-page conflicts rejected); permission matrix/role creation/direct UserPermissions PENDING_APPROVAL; AUD-25 (AuditLogs in TAPortalSys unverified) PENDING_OPERATOR; AUD-26 OPEN. Release build 0/0; 104 safe tests + 4 Codec tests PASS; validate PASS; Admin smoke 12/12. Admin artifact `5adf697b…afd36` (57 files); Customer NOT_REQUIRED (unchanged `ea77b3be…ac34`). Database NOT_VERIFIED. Operator decision: no deploy/global UAT until the final checkpoint. Prior status lines above are historical.
