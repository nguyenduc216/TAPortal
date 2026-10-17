# CRM Phase1-010 secure relation workflows

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-010`
- Status: BLOCKED for relation writes; no authorization bypass introduced.
- Inspected existing `PortalDb` scope query, relation entry points, scope
  resolver, Phase1-005 SQL contract and Phase1-007/008/009 handoffs.
- Contract gap: the existing customer scope query checks `Customers.Index`
  and DataScope FunctionCode `Customers`/`Customers.Index`, which governs
  viewing; action metadata `Customers.Assign/Share/Team` establishes only an
  action permission, not a per-customer write scope. No documented mapping
  specifies an independently assigned management DataScope and its DENY,
  SYS_ADMIN, or cross-organization semantics. Reusing VIEW or only the action
  permission would broaden write access. Three create methods remain Forbidden;
  no change/revoke methods or schema/data changes were added.
- Files: this record and `code-history/features/TAPORTAL-CRM-CUSTOMER-360.md`.
- Assumptions: SQL Server 2014, migration 053 and Phase1-005 contract deployed
  per owner baseline (not queried here). Database-backed verification NOT_RUN.
- Git: existing unrelated dirty tree and staged configuration deletion;
  GIT_NEEDS_APPROVAL. No branch, stage, commit, push, PR or deployment.
- Next: owner approves precise management scope policy before implementing
  transaction-scoped authorization, HR target checks, history/RowVersion,
  atomic audit and service-level integration tests.