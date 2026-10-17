# TA Portal Super Admin Authorization & Premium Customer Actions

- Date: 2026-10-12
- Agent: OpenAI Codex
- FEATURE_ID: `TAPORTAL-SECURITY-ADMIN-AUTHORIZATION`
- CHANGE_ID: `TAPORTAL-20261012-ADMIN-RBAC-001`
- Branch: `feat/compact-dynamic-navigation`
- Commit SHA: see Git commit containing this history entry

## Request and scope

- Add a server-side Super Administrator authorization path based on trusted database role membership.
- Preserve ordinary-user RBAC and explicit DENY precedence.
- Remove the Customers toolbar warning text while retaining disabled button behavior and server-side write checks.
- Keep the existing premium Customers action-button design and verify it across MVC and Blazor.

## Root cause

Authorization previously evaluated only direct user permissions and active role mappings. The seeded system role `SYS_ADMIN` had no shared resolver treatment, so a valid system administrator without individually effective permission rows could be rejected by `PERM:`/`ACTION:` checks. Customers also rendered a permission warning below the toolbar when Create was unavailable.

## Security decision

- Super Admin is resolved only from active, non-deleted authenticated user membership in active, non-deleted role `Code='SYS_ADMIN'` with `IsSystem=1`.
- This is a server-side trusted-data check; it does not use username, display name, client role claims, query string or cookie permission snapshots.
- Approved privileged policy: this role bypasses explicit user DENY for permission resolution. Ordinary users continue to use explicit DENY precedence.
- Authentication, active-account checks, validation, CSRF, concurrency and database integrity are not bypassed.
- No production role assignment or database update was performed.

## Files changed

- `src/TAPortal.Web/Services/PortalDb.cs`
- `src/TAPortal.Web/Components/Pages/Customers.razor`
- `src/TAPortal.Web/Views/Customers/Index.cshtml`
- This history file.

## Database audit

- Read-only schema/seed audit confirms `Roles.IsSystem`, `Roles.Code`, active/deleted flags, `UserRoles`, `RolePermissions` and active/deleted user state are available.
- Existing seed defines `SYS_ADMIN` as a system role and maps it to common permissions.
- No migration was required, and no production SQL was executed.

## Verification

- `dotnet build TAPortal.sln -c Release --no-restore`: PASS, 0 warnings/errors.
- `dotnet test TAPortal.sln -c Release --no-build`: PASS, 86/86.
- Focused Customers Playwright suite `@customers-001`: PASS, 7/7.
- Playwright covered MVC/Blazor, 1440/768/390 viewports, create authorization, disabled state, modal, validation, search, export, anti-forgery and overflow.
- `git diff --check`: PASS; only existing Git line-ending warnings were emitted.

## Publish

- Required directory: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal\publish-TAPortal-blazor`
- Existing output was backed up before publish.
- `appsettings.json` was preserved.
- Release publish completed and DLL, CSS/JS, `web.config` and dependencies were checked.
- Deploy ZIP: `D:\Working\T.A\Tech\Projects\TAPortal\sourceN\TAPortal-Blazor-IIS-Deploy.zip`
- ZIP extraction and SHA-256 verification completed.

## Git

- Working tree contains prior concurrent Customers/migration/UI/history changes and a staged `appsettings.json` deletion.
- No commit or push was made because safe selective staging cannot include this task without risking unrelated changes.

## Remaining blockers

- Production assignment/review of `SYS_ADMIN` membership remains an owner-controlled operation and was not performed.
- IIS deployment and production replacement were not performed.
- Publish binaries and ZIP are local artifacts and must not be committed.