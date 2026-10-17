# CRM Phase1-011B unified permission and DataScope authorization

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-011B`
- Status: BLOCKED for release; no CRM writer restored.
- Approved change from 011A: effective action permission plus existing CRM
  function-level customer scope, not independently granted management scopes.
  Existing primary-key cardinality limits remain intact; do not generate
  customer-specific grants, NULL wildcards or additional grant identities.
- Source: `PortalDb.GetCustomersAsync` only resolves `Customers` and
  `Customers.Index` grants after checking the VIEW action; this is not a
  transactional management authorization API. `HasFunctionActionAsync` and
  `GetEmployeeAccessContextAsync` open independent connections. A mutation
  must resolve account, eligible linked employee, action permission, matching
  scope and target under the SAME transaction, then audit before commit.
- Source: `EmployeeAssignmentRules.Resolve` preserves access for accounts
  without employee links for legacy reads; management must require the link.
  Current CRM create methods all return Forbidden. No runtime authorization
  or business write was introduced on incomplete verification.
- Target organizational policy still needs a precise reviewed implementation:
  eligible active target EmployeeAssignment(s), target branch and team must
  be checked against T.A internal company/branch rules under the transaction.
  Do not equate customer businesses with HR companies or broaden cross-branch
  writes through visibility. Preserve matching User DENY and valid SYS_ADMIN
  action semantics when building the transactional routine.
- Git: unrelated dirty working tree, staged appsettings.json deletion;
  GIT_NEEDS_APPROVAL. No branch, commit, push, PR or merge.
- SQL integration NOT_RUN; Playwright NOT_RUN; publish NOT_RUN. No schema,
  database execution, grants or external customer identities added.