# CRM Phase1-011C transaction-bound authorization foundation

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011C`
- Status: PARTIAL; no CRM relation writer enabled.
- Internal `PortalDb.AuthorizeCustomerMutationAsync` accepts caller-owned open
  SqlConnection and SERIALIZABLE SqlTransaction; checks eligible linked employee,
  one effective primary HR assignment, action metadata/permission and matching
  customer function-level scope, including targeted User DENY. Action allowlist
  is Assign/Share/Team. SQL errors propagate rather than becoming denial.
- Mutation uses only `Customers` function-level scope; `Customers.Index` remains
  VIEW-only. Customer list/export code was not changed. Scope SQL is not yet
  factored into a shared VIEW/mutation predicate; database integration is
  necessary before treating behavior as verified.
- Customer UPDLOCK/HOLDLOCK under SERIALIZABLE protects customer row and
  transaction-duration reads against cooperating writers. RBAC/HR editors
  outside this lock protocol and range-lock plans need DB concurrency tests.
- Git tree contains unrelated modifications and staged configuration deletion;
  no branch, stage, commit, PR, merge or production SQL execution.