# CRM Phase1-009 customer relations management

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-009`
- Status: PARTIAL; not end-to-end complete.
- Scope: reviewed `PortalDb.CrmRelations.cs`, existing authorization, UI and
  Phase1-005 migration. The existing three create entry points were gated only
  by action permission, not customer scope or target eligibility. They now
  return Forbidden until a verified atomic validation flow is delivered.
- SQL assumptions: existing CRM relation tables and AuditLogs per Phase1-005;
  no schema migration, seed, or database execution in this change.
- Tests: Release build PASS (0 warnings/errors). Focused tests 18/19 and full
  tests 115/116 initially failed on an obsolete assertion requiring MERGE in
  the Phase1-008 seed; updated the assertion to enforce insert-only behavior.
  Added fail-closed writer regression test. Rerun: focused CRM 20/20 PASS;
  full suite 117/117 PASS; Release build 0 warnings/errors. `git diff --check`
  on tracked modified test file PASS. Database-backed and browser tests are
  NOT_RUN. No customer records were changed.
- Publish: NOT_RUN; authorization workflow is incomplete and must not be
  released as a completed feature.
- Git: existing unrelated dirty files and staged appsettings deletion prevent
  safe branch isolation, staging, commit and PR. Human security review required.
- Remaining risk: no create/change/revoke/history UI; RowVersion and audit
  rollback have not been validated end-to-end. Do not mark completed or deploy.