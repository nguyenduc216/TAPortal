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
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T074006Z-654653` | 2026-10-28 | DEV TEST machine policy and Customer Portal local publish | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Current DEV/TEST checkpoint ? supersedes earlier publish blocker
Local Customer Portal artifact PUBLISHED to the conventional target. Release build 0 warnings/errors; 34 safe tests and 3 Codec tests PASS. Database VERIFIED_WITH_WARNINGS by operator; no SQL run. IIS NOT_CHECKED / NOT_DEPLOYED; no server access required or permitted. See latest report and SHA256 manifest for evidence. Earlier checkpoint descriptions are historical.
| `CUSTOMER-PORTAL-FOUNDATION-CHG-20261028T075636Z-260F08` | 2026-10-28 | Checkpoint 4 local smoke and manual functional acceptance | — | NOT_EXECUTED | NOT_PUBLISHED | PENDING_APPROVAL |

## Checkpoint 4 - current status
CHECKPOINT_4A: PASS (existing artifact integrity, isolated localhost startup and 10 anonymous GET checks). CHECKPOINT_4B: PENDING_OPERATOR; UAT-01 through UAT-09 NOT_RUN. CUSTOMER_PORTAL_MVP01: NOT_YET_ACCEPTED. Runtime process stopped, artifact unchanged, no republish or SQL access; database NOT_VERIFIED for this checkpoint. No application source changes. See latest report and docs/customer-portal-checkpoint4-uat.md. Prior entries remain historical.
