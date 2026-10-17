# CRM Phase1-014 branch share and service team create

- FEATURE_ID: `TAPORTAL-CRM-CUSTOMER-360`
- CHANGE_ID: `TAPORTAL-20261015-CRM-PHASE1-014`
- Status: development implementation; SQL integration NOT_RUN, not production-ready.
- Added real `CreateCustomerBranchShareAsync` and `CreateCustomerServiceTeamAsync`
  writes to the existing PortalDb. Both validate IDs, inclusive date order and
  unused RoleCode, and use one connection and Serializable transaction.
- `AuthorizeCustomerMutationAsync` checks Customers.Share or Customers.Team,
  linked employee, action DENY and effective customer DataScope in that transaction.
- Branch share requires active customer owner branch/company and active target
  branch/company in the same company, different from owner. Service team requires
  active team/branch/company, valid optional active department in same branch,
  and target branch in the customer's owner company. These are conservative
  cross-organization restrictions; no customer-specific grant is created.
- Active overlapping periods for the same customer/target are rejected using
  UPDLOCK/HOLDLOCK. Relations and CRM SHARE/TEAM audit entries are committed
  together; any error rolls back. Database generates RowVersion.
- No migration, no SQL executed on TAPortalSys. Offline validation tests cover
  invalid requests; SQL success/denial, explicit DENY, rollback and concurrent
  create remain unverified without an isolated SQL test database.
- Existing unrelated Git changes and staged configuration deletion preserved;
  no commit, merge or IIS deployment.