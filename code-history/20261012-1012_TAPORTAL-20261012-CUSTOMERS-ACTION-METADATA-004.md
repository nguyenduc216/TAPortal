# Customers Action Metadata 004

- Date: 2026-10-12
- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-CUSTOMERS-ACTION-METADATA-004`
- Branch: `feat/compact-dynamic-navigation`
- Commit/push: not performed; the working tree contains overlapping unrelated changes and a staged `appsettings.json` deletion.

## Root cause

ADMIN-RBAC-003 established that production contains an active `Customers.Index` -> `CRM.CUSTOMERS.VIEW` mapping but no active `Customers.Create` action mapping. The SYS_ADMIN account and membership are valid; authorization correctly fails closed for the unmapped action.

## Metadata before

- `FunctionActions`: active `Customers.Index` exists; active `Customers.Create` does not.
- `Permissions`: active `CRM.CUSTOMERS.VIEW` exists; active `CRM.CUSTOMERS.CREATE` was not returned by the read-only verification.
- `FunctionActionPermissions`: no active Customers.Create -> CRM.CUSTOMERS.CREATE mapping.
- No role/user metadata was changed.

## Metadata after (script design; production not applied)

The review migration adds, only when absent and non-conflicting:

- active `CRM.CUSTOMERS.CREATE` permission;
- active `Customers.Create` / `customers.create` action under the existing active `CUSTOMERS` function;
- one composite permission mapping.

It does not assign a role or user permission. It uses schema/index guards, serializable locking, one transaction, rollback on error, postconditions, and idempotent existence checks. Production remains unchanged, so the effective after-state is not claimed.

## SQL and verification

- Migration: `database/migrations/20261012_CUSTOMERS-001_create-action-review.sql`
- Read-only verification: `database/scripts/verify/20261012_CUSTOMERS-001_verify.sql`
- Live read-only verification: PASS for database/schema audit; CustomersCreateMetadataStatus is `FAIL` before approved execution.
- Production SQL write execution: **NO**.
- Production approval: **NOT PROVIDED / BLOCKED**.

## Tests

- Focused authorization and SQL artifact tests: PASS, 35/35.
- Release build: PASS, 0 warnings/errors.
- Full automated suite: not rerun in this change; prior audit recorded 86/86. Therefore no new full-suite PASS is claimed.
- Playwright Customers: not rerun in this change; prior audit recorded 7/7 local. No production acceptance is claimed.

## Git and publish

- `git diff --check`: attempted; output is affected by existing working-tree line-ending warnings.
- No application source changed; no publish performed.
- No ZIP and no IIS deployment.
- `appsettings.json` was not restored or modified.

## Remaining blockers

1. Owner approval and controlled execution of the migration in the approved environment.
2. Post-execution read-only verification must return PASS.
3. Authenticated IIS acceptance for SYS_ADMIN Create, validation/save/export, and unauthorized denial.
4. Selective staging/commit remains unsafe until unrelated working-tree changes and the staged configuration deletion are isolated.