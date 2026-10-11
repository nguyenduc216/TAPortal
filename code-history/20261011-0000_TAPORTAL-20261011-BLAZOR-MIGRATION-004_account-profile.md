# TAPORTAL-20261011-BLAZOR-MIGRATION-004

- FEATURE_ID: `TAPORTAL-PLATFORM-BLAZOR-MIGRATION`
- CHANGE_ID: `TAPORTAL-20261011-BLAZOR-MIGRATION-004`
- Scope: Account Profile and Change Password Blazor trial routes

## Implementation

- Added `/blazor/account/profile` and `/blazor/account/change-password` as authenticated Interactive Server components.
- Kept `/account/profile`, `/account/change-password`, login, and logout MVC routes unchanged.
- Profile editing is restricted to `DisplayName`, `Email`, and `Phone`. Username, id, roles, status, and password hash are not client-editable.
- Server-side identity is resolved from the authenticated `NameIdentifier` claim and the database update is scoped to that active user. No client-supplied user id is trusted.
- Change Password reuses `ChangePasswordVm`, `PasswordPolicy`, and `PasswordService`; no cookie is written from the SignalR circuit.
- Loading scopes are disposed with `using`, duplicate submits are guarded, and database errors return generic messages without sensitive values.
- No schema, migration, credential, authentication endpoint, or production route ownership change was made.

## Automated validation

- Release build: PASS, 0 warnings/errors.
- Full tests: PASS, 50/50.
- New tests cover profile validation, overposting surface, PBKDF2/policy behavior; full database-backed cross-user behavior remains runtime validation work.
- Authenticated browser tests: BLOCKED — authorized test account required.
- Publish: non-production output requested at `publish-TAPortal-phase4`; no production deployment.

## Security workstream

Existing plaintext credentials, negotiated TLS 1.0, public SQL endpoint exposure, and missing OperationalDb tables remain unresolved findings from PRECHECK-004. This change does not remediate them. Rotate exposed credentials, use approved secret configuration, and have SQL/network owners remediate TLS to 1.2+ before production use.

## Acceptance status

`PARTIAL — Implementation complete, authenticated runtime verification pending.`

Route cutover is **NO** until authenticated parity, authorization, persistence, logout/session, and error-path browser tests pass.