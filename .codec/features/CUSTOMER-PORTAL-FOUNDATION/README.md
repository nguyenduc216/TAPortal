# Feature: CUSTOMER-PORTAL-FOUNDATION

- Feature ID: `CUSTOMER-PORTAL-FOUNDATION`
- Tên: Customer Portal MVP 01 Checkpoint 1
- Mục tiêu:
- Phạm vi / ngoài phạm vi:
- Kiến trúc / bảng liên quan:
- Các phụ thuộc:
- Trạng thái: ACTIVE

## Change history

| Change ID | Ngày UTC | Nội dung | Commit SHA | Migration | Publish | Merge |
|---|---|---|---|---|---|---|
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261027T141827Z-E64647` | 2026-10-27 | Customer Portal MVP 01 Checkpoint 1 | 1d048d1ec3b52cda301c18154949bb1e1ac41c8f | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Verified baseline and checkpoint state
Existing source already implements independent Customer Portal login/logout, company selection, dashboard, profiles, shared password changes and SYS_ADMIN-only membership management. No feature history preceded this change; baseline verified from source/PR #7, not assumed from chat.
Checkpoint 1 adds fixed session lifetime across company switches, no-store sensitive pages, TAPortalSys SQL guards and SQL-free HTTP security tests. Release build: 0 errors/warnings. Tests: 34 pass, 0 fail; 25 legacy SQL test methods skipped by Codec policy.
Status: SOURCE_AND_SAFE_TESTS_COMPLETE. SQL CRUD/real Admin regression, schema audit and IIS/browser UAT await operator. No SQL, publish, deploy or merge performed. See current change/report for exact paths, files, limits and handoff.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T005216Z-E6FF4A` | 2026-10-28 | Customer Portal MVP01 Publish Checkpoint 3 | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Checkpoint 3 current state
Release build and 34 selected safe tests PASS; 3 Codec tests PASS. Conventional publish paths configured; Customer-only artifact publishing authorized. BLOCKED before publish: IIS services running but effective mappings unreadable due to permissions. No target folder created, no publish/hash, no IIS/SQL changes. Operator reports DB VERIFIED_WITH_WARNINGS and 0 memberships; do not rerun migration 008. See current report/runbook for handoff. Prior checkpoint statements above are historical.
