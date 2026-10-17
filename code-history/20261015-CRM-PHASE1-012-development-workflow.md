# CRM Phase1-012 development workflow

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-012`
- Status: PARTIAL, not production-ready. No writes or Blazor relation UI enabled.
- Reviewed 011C authorization against HR resolver: selecting TEAM assignment
  by MAX(Id) is not equivalent to resolving exactly one current primary HR
  assignment. Removed MAX(Id), required uniqueness before organizational
  validity filtering and validated TEAM's active team, department, branch and
  company against the actual effective assignment. The existing list VIEW
  continues to use its prior predicate and is untouched.
- SQL Server 2014 integration, full authorization allow/deny fixtures and
  concurrency NOT_RUN; manual 011D read-only handoff remains available.
- Create/Change/Revoke still require target-policy implementation, audit
  rollback and RowVersion tests before safe enablement. No DDL, seed or DB
  execution. Git tree contains unrelated dirty state and staged configuration
  deletion; no branch, commit, push, PR or merge.