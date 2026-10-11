# TAPORTAL-20261010-HR-MASTER-DATA-002 — Team master

- Date: 2026-10-10
- Agent: Codex
- Project: T.A Portal
- Branch: `feat/compact-dynamic-navigation`
- FEATURE_ID: `TAPORTAL-HR-MASTER-DATA`
- CHANGE_ID: `TAPORTAL-20261010-HR-MASTER-DATA-002`
- Starting SHA: `c27c057612c065c4072251ab20f098dc2bcba532`

## Request and scope

Continue Phase 7 with the second production HR master: Team list/search/Department filter, create/edit/status/soft-delete, optional arbitrary-depth parent hierarchy, optimistic concurrency, dynamic authorization/menu, migration/verify artifacts, tests, and a reusable light CRUD modal correction for Department and Team.

## Out of scope

JobTitle, Specialization, Employee, EmployeeAssignment implementation, authorization redesign, another data-access owner, drag/drop hierarchy, physical deletion, and reconstruction of the externally executed migration 054.

## Architecture reused

The Department CHANGE 001 baseline is retained: `PortalDb` is the sole SQL owner; Phase 6A `ACTION:` policies and database-filtered menu remain authoritative; Phase 6B compact table, shell, responsive, loading, and accessibility behavior remain intact. No EF Core, repository layer, generic master framework, or role-name bypass was introduced.

## Team rules and implementation

`dbo.Teams` belongs to one active, nondeleted Department. Code is trimmed/uppercased and globally unique among nondeleted Teams. Parent is optional, must be nondeleted and in the same Department, cannot be self, and cannot make the Team its own descendant. Create/edit hierarchy validation runs in a serializable transaction. Edit, status, and delete require the original RowVersion. Delete is soft and is blocked by nondeleted children or future `EmployeeAssignments.TeamId`; the pre-existing Department dependency probe remains compatible with `Teams.DepartmentId`.

The `/hr/teams` page resolves capabilities once and supplies compact list/search/Department filtering, Department-aware parent choices, permission-gated actions, status control, edit and delete confirmation. Browser filtering improves UX; backend validation remains authoritative.

## Modal/backdrop correction

Removed Tabler `modal-blur` from Department create/edit/delete. Department and Team now use reusable `modal fade ta-crud-modal` markup. Shared CSS applies a light `.18` Bootstrap backdrop, clear bordered/elevated content, compact spacing, viewport-safe body and responsive mobile footer. This global backdrop rule intentionally normalizes application modals without affecting the separate mobile navigation overlay.

## SQL and authorization

Migration `056-hr-team-master.sql` is additive, transactional, restart-aware, guarded for `TAPortalSys`, and SQL Server 2014 compatible. It creates or safely extends compatible Teams, rejects incompatible required legacy columns/data, validates hierarchy, creates trusted Department/self FKs, canonical/self checks, filtered global Code uniqueness and lookup indexes. It reconciles TEAMS, four permissions/actions/mappings, Team menu and VIEW mapping, and SYS_ADMIN grants through RolePermissions only.

Read-only `056-verify-hr-team.sql` reports 23 PASS/WARN/FAIL checks, including schema, trusted FKs, indexes, auth/menu grants and all hierarchy/data invariants, then raises on failure.

## Tests, build, and publish

- Focused rules cover normalization, same-Department parent, self/cycle prevention, and invalid RowVersion.
- Authorization contracts cover Team controller and all mutations; semantics cover denied unmapped Team actions and menu permission.
- SQL contracts cover hierarchy, uniqueness, authorization, RowVersion, child/future dependencies, non-destructive migration and read-only verification.
- Release build: PASS, 0 warnings, 0 errors.
- Automated tests: PASS, 44/44.
- SQL Server 2014 PARSEONLY: migration PASS; verify PASS.
- Runtime migration/verify/CRUD smoke: BLOCKED because Windows integrated login cannot open `TAPortalSys` (SQL 4060/18456). No runtime PASS is claimed.
- Publish and final Git checks are recorded in the delivery response.

## Files changed

Team models/controller/views; `PortalDb`; shared JS/CSS; Department modal markup; migration/verify 056; focused authorization/rule/SQL tests; the existing feature history; and this single change history.

## Deployment notes

Using an authorized identity, confirm migrations 053–055 and exact 054 deployment evidence, execute 056 against `TAPortalSys` with `sqlcmd -b -I`, run verify 056, then perform permission-specific CRUD, stale RowVersion, same/cross-Department parent, cycle, child-delete, and Department-delete smoke tests. Never run migrations 001–052 against TAPortalSys.
